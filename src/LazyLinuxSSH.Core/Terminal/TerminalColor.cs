namespace LazyLinuxSSH.Core.Terminal;

public enum TerminalColor
{
    Default,
    Black,
    Red,
    Green,
    Yellow,
    Blue,
    Magenta,
    Cyan,
    White,
    BrightBlack,
    BrightRed,
    BrightGreen,
    BrightYellow,
    BrightBlue,
    BrightMagenta,
    BrightCyan,
    BrightWhite
}

public readonly record struct TerminalStyle(TerminalColor Color, bool Bold)
{
    public static TerminalStyle Default => new(TerminalColor.Default, false);
}

public sealed record TerminalColorSpan(int Start, int Length, TerminalColor Color, bool Bold);

public sealed record TerminalSnapshot(string Text, IReadOnlyList<TerminalColorSpan> Spans);
