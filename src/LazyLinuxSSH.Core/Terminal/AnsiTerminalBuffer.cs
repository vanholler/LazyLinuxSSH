using System.Globalization;
using System.Text;

namespace LazyLinuxSSH.Core.Terminal;

/// <summary>
/// A bounded, append-only terminal screen with a stateful ANSI/VT parser.
/// It intentionally implements the sequences used by interactive bash tools rather
/// than pretending to be a full VT520 emulator. Incomplete escape sequences are
/// retained between SSH read chunks, including split UTF-8-independent OSC/CSI data.
/// </summary>
public sealed class AnsiTerminalBuffer
{
    private const int MaxLines = 5000;
    private const int Columns = 200;
    private readonly object _sync = new();
    private readonly List<List<TerminalCell>> _lines = [[]];
    private TerminalStyle _style = TerminalStyle.Default;
    private string _pending = string.Empty;
    private int _row;
    private int _column;
    private int _savedRow;
    private int _savedColumn;
    private TerminalStyle _savedStyle = TerminalStyle.Default;

    public event EventHandler? Updated;

    public void Append(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        lock (_sync)
        {
            var input = _pending + text;
            _pending = string.Empty;
            var index = 0;

            while (index < input.Length)
            {
                var character = input[index];
                if (character == '\u001b')
                {
                    if (!TryConsumeEscape(input, ref index))
                    {
                        _pending = input[index..];
                        break;
                    }

                    continue;
                }

                switch (character)
                {
                    case '\r':
                        _column = 0;
                        break;
                    case '\n':
                        NewLine();
                        break;
                    case '\b':
                        _column = Math.Max(0, _column - 1);
                        break;
                    case '\t':
                        var nextTab = Math.Min(Columns - 1, ((_column / 8) + 1) * 8);
                        while (_column < nextTab)
                        {
                            WriteCharacter(' ');
                        }
                        break;
                    case '\a':
                        break;
                    default:
                        if (!char.IsControl(character) &&
                            !char.IsSurrogate(character) &&
                            CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.Format)
                        {
                            WriteCharacter(character);
                        }
                        break;
                }

                index++;
            }

            TrimToLimit();
        }

        Updated?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        lock (_sync)
        {
            _lines.Clear();
            _lines.Add([]);
            _row = 0;
            _column = 0;
            _pending = string.Empty;
            _style = TerminalStyle.Default;
        }

        Updated?.Invoke(this, EventArgs.Empty);
    }

    public string GetCursorLine()
    {
        lock (_sync)
        {
            if (_row < 0 || _row >= _lines.Count) return string.Empty;
            var line = _lines[_row];
            var builder = new StringBuilder(line.Count);
            foreach (var cell in line) builder.Append(cell.Character);
            return builder.ToString().TrimEnd();
        }
    }

    public TerminalSnapshot GetSnapshot()
    {
        lock (_sync)
        {
            var builder = new StringBuilder();
            var spans = new List<TerminalColorSpan>();
            var offset = 0;

            for (var lineIndex = 0; lineIndex < _lines.Count; lineIndex++)
            {
                var line = _lines[lineIndex];
                var lineStart = offset;
                foreach (var cell in line)
                {
                    builder.Append(cell.Character);
                    offset++;
                }

                AddLineSpans(line, lineStart, spans);
                if (lineIndex < _lines.Count - 1)
                {
                    builder.Append('\n');
                    offset++;
                }
            }

            return new TerminalSnapshot(builder.ToString(), spans);
        }
    }

    private static void AddLineSpans(
        IReadOnlyList<TerminalCell> line,
        int lineStart,
        ICollection<TerminalColorSpan> spans)
    {
        var index = 0;
        while (index < line.Count)
        {
            var style = line[index].Style;
            var start = index++;
            while (index < line.Count && line[index].Style == style)
            {
                index++;
            }

            if (style != TerminalStyle.Default)
            {
                spans.Add(new TerminalColorSpan(
                    lineStart + start,
                    index - start,
                    style.Color,
                    style.Bold));
            }
        }
    }

    private bool TryConsumeEscape(string input, ref int index)
    {
        // index points at ESC. A bare ESC at the end of a network chunk must wait.
        if (index + 1 >= input.Length)
        {
            return false;
        }

        var kind = input[index + 1];
        switch (kind)
        {
            case '[':
                return TryConsumeCsi(input, ref index);
            case ']':
                return TryConsumeOsc(input, ref index);
            case '(':
            case ')':
                if (index + 2 >= input.Length)
                {
                    return false;
                }

                index += 3; // Character-set selection has no effect on Unicode text.
                return true;
            case '7':
                SaveCursor();
                index += 2;
                return true;
            case '8':
                RestoreCursor();
                index += 2;
                return true;
            case 'c':
                ResetParser();
                index += 2;
                return true;
            case 'M':
                _row = Math.Max(0, _row - 1);
                index += 2;
                return true;
            default:
                index += 2; // Ignore Fe escape sequences not relevant to this buffer.
                return true;
        }
    }

    private bool TryConsumeCsi(string input, ref int index)
    {
        var finalIndex = -1;
        for (var cursor = index + 2; cursor < input.Length; cursor++)
        {
            var value = input[cursor];
            if (value is >= '@' and <= '~')
            {
                finalIndex = cursor;
                break;
            }
        }

        if (finalIndex < 0)
        {
            return false;
        }

        var body = input[(index + 2)..finalIndex];
        var final = input[finalIndex];
        var isPrivate = body.StartsWith('?') || body.StartsWith('>');
        var parametersBody = isPrivate ? body[1..] : body;
        var parameters = ParseParameters(parametersBody);
        if (!isPrivate)
        {
            ExecuteCsi(final, parameters);
        }

        index = finalIndex + 1;
        return true;
    }

    private bool TryConsumeOsc(string input, ref int index)
    {
        for (var cursor = index + 2; cursor < input.Length; cursor++)
        {
            if (input[cursor] == '\a')
            {
                index = cursor + 1;
                return true;
            }

            if (input[cursor] == '\u001b' && cursor + 1 < input.Length && input[cursor + 1] == '\\')
            {
                index = cursor + 2;
                return true;
            }
        }

        return false;
    }

    private static int[] ParseParameters(string body)
    {
        if (string.IsNullOrEmpty(body))
        {
            return [0];
        }

        return body.Split(';', StringSplitOptions.TrimEntries)
            .Select(part => int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
                ? Math.Clamp(value, 0, 9999)
                : 0)
            .ToArray();
    }

    private void ExecuteCsi(char final, IReadOnlyList<int> parameters)
    {
        var amount = parameters.Count == 0 || parameters[0] == 0 ? 1 : parameters[0];
        switch (final)
        {
            case 'm':
                ApplySgr(parameters);
                break;
            case 'H':
            case 'f':
                MoveCursor(
                    parameters.Count > 0 ? parameters[0] : 1,
                    parameters.Count > 1 ? parameters[1] : 1);
                break;
            case 'A':
                _row = Math.Max(0, _row - amount);
                break;
            case 'B':
                MoveToRow(_row + amount);
                break;
            case 'C':
                _column = Math.Min(Columns - 1, _column + amount);
                break;
            case 'D':
                _column = Math.Max(0, _column - amount);
                break;
            case 'G':
            case '`':
                _column = Math.Clamp(amount - 1, 0, Columns - 1);
                break;
            case 'd':
                MoveToRow(Math.Max(0, amount - 1));
                break;
            case 'J':
                ClearScreen(parameters.Count == 0 ? 0 : parameters[0]);
                break;
            case 'K':
                ClearLine(parameters.Count == 0 ? 0 : parameters[0]);
                break;
            case 's':
                SaveCursor();
                break;
            case 'u':
                RestoreCursor();
                break;
        }
    }

    private void ApplySgr(IReadOnlyList<int> parameters)
    {
        if (parameters.Count == 0)
        {
            _style = TerminalStyle.Default;
            return;
        }

        for (var index = 0; index < parameters.Count; index++)
        {
            var code = parameters[index];
            switch (code)
            {
                case 0:
                    _style = TerminalStyle.Default;
                    break;
                case 1:
                    _style = _style with { Bold = true };
                    break;
                case 21:
                case 22:
                    _style = _style with { Bold = false };
                    break;
                case 39:
                    _style = _style with { Color = TerminalColor.Default };
                    break;
                case >= 30 and <= 37:
                    _style = _style with { Color = (TerminalColor)(code - 29) };
                    break;
                case >= 90 and <= 97:
                    _style = _style with { Color = (TerminalColor)(code - 81) };
                    break;
                case 38 when index + 4 < parameters.Count && parameters[index + 1] == 2:
                    _style = _style with
                    {
                        Color = ApproximateRgb(parameters[index + 2], parameters[index + 3], parameters[index + 4])
                    };
                    index += 4;
                    break;
                case 38 when index + 2 < parameters.Count && parameters[index + 1] == 5:
                    _style = _style with { Color = ApproximatePalette(parameters[index + 2]) };
                    index += 2;
                    break;
            }
        }
    }

    private static TerminalColor ApproximateRgb(int red, int green, int blue)
    {
        var max = Math.Max(red, Math.Max(green, blue));
        var bright = max > 170;
        if (max < 55)
        {
            return TerminalColor.Black;
        }

        if (Math.Max(red, Math.Max(green, blue)) - Math.Min(red, Math.Min(green, blue)) < 35)
        {
            return bright ? TerminalColor.BrightWhite : TerminalColor.White;
        }

        if (red >= green && red >= blue)
        {
            if (green > blue * 1.4 && green > 80) return bright ? TerminalColor.BrightYellow : TerminalColor.Yellow;
            if (blue > green * 1.4 && blue > 80) return bright ? TerminalColor.BrightMagenta : TerminalColor.Magenta;
            return bright ? TerminalColor.BrightRed : TerminalColor.Red;
        }

        if (green >= red && green >= blue)
        {
            if (blue > red * 1.4 && blue > 80) return bright ? TerminalColor.BrightCyan : TerminalColor.Cyan;
            return bright ? TerminalColor.BrightGreen : TerminalColor.Green;
        }

        return bright ? TerminalColor.BrightBlue : TerminalColor.Blue;
    }

    private static TerminalColor ApproximatePalette(int color) => color switch
    {
        >= 0 and <= 15 => (TerminalColor)(color + 1),
        >= 16 and <= 231 => TerminalColor.BrightBlue,
        _ => TerminalColor.White
    };

    private void WriteCharacter(char character)
    {
        if (_column >= Columns)
        {
            NewLine();
        }

        MoveToRow(_row);
        var line = _lines[_row];
        while (line.Count <= _column)
        {
            line.Add(new TerminalCell(' ', TerminalStyle.Default));
        }

        line[_column] = new TerminalCell(character, _style);
        _column++;
    }

    private void NewLine()
    {
        _column = 0;
        MoveToRow(_row + 1);
    }

    private void MoveToRow(int row)
    {
        row = Math.Max(0, row);
        while (_lines.Count <= row)
        {
            _lines.Add([]);
        }

        _row = row;
    }

    private void MoveCursor(int row, int column)
    {
        MoveToRow(Math.Max(0, row - 1));
        _column = Math.Clamp(column - 1, 0, Columns - 1);
    }

    private void ClearScreen(int mode)
    {
        if (mode == 2 || mode == 3)
        {
            _lines.Clear();
            _lines.Add([]);
            _row = 0;
            _column = 0;
            return;
        }

        if (mode == 0)
        {
            ClearLine(0);
            for (var index = _row + 1; index < _lines.Count; index++)
            {
                _lines[index].Clear();
            }
        }
        else if (mode == 1)
        {
            ClearLine(1);
            for (var index = 0; index < _row; index++)
            {
                _lines[index].Clear();
            }
        }
    }

    private void ClearLine(int mode)
    {
        MoveToRow(_row);
        var line = _lines[_row];
        switch (mode)
        {
            case 2:
                line.Clear();
                break;
            case 0:
                if (_column < line.Count)
                {
                    line.RemoveRange(_column, line.Count - _column);
                }
                break;
            case 1:
                for (var index = 0; index <= _column && index < line.Count; index++)
                {
                    line[index] = new TerminalCell(' ', TerminalStyle.Default);
                }
                break;
        }
    }

    private void SaveCursor()
    {
        _savedRow = _row;
        _savedColumn = _column;
        _savedStyle = _style;
    }

    private void RestoreCursor()
    {
        MoveToRow(_savedRow);
        _column = _savedColumn;
        _style = _savedStyle;
    }

    private void ResetParser()
    {
        _lines.Clear();
        _lines.Add([]);
        _row = 0;
        _column = 0;
        _style = TerminalStyle.Default;
        SaveCursor();
    }

    private void TrimToLimit()
    {
        if (_lines.Count <= MaxLines)
        {
            return;
        }

        var remove = _lines.Count - MaxLines;
        _lines.RemoveRange(0, remove);
        _row = Math.Max(0, _row - remove);
        _savedRow = Math.Max(0, _savedRow - remove);
    }

    private readonly record struct TerminalCell(char Character, TerminalStyle Style);
}
