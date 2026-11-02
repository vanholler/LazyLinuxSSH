using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using LazyLinuxSSH.Core.Terminal;
using LazyLinuxSSH.Desktop.ViewModels;

namespace LazyLinuxSSH.Desktop.Controls;

public partial class TerminalView : UserControl
{
    private readonly AnsiColorizingTransformer _colorizer = new();
    private TerminalViewModelBase? _viewModel;
    private bool _renderQueued;
    private bool _scrollToLatestPending;

    public TerminalView()
    {
        InitializeComponent();
        Editor.TextArea.TextView.LineTransformers.Add(_colorizer);
        Editor.AddHandler(InputElement.TextInputEvent, OnTextInput, RoutingStrategies.Tunnel);
        Editor.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        PointerPressed += OnPointerPressed;
        SizeChanged += OnSizeChanged;
        Editor.LayoutUpdated += OnEditorLayoutUpdated;
        DataContextChanged += OnDataContextChanged;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    private void OnDataContextChanged(object? sender, EventArgs args)
    {
        if (_viewModel is not null) _viewModel.Buffer.Updated -= OnBufferUpdated;
        _viewModel = DataContext as TerminalViewModelBase;
        if (_viewModel is not null)
        {
            _viewModel.Buffer.Updated += OnBufferUpdated;
            RenderBuffer();
            FocusEditorIfActive();
        }
        else
        {
            _colorizer.SetSnapshot(new TerminalSnapshot(string.Empty, []));
            Editor.Document.Text = string.Empty;
        }
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs args)
    {
        if (_viewModel is null) return;
        _viewModel.Buffer.Updated -= OnBufferUpdated;
        _viewModel.Buffer.Updated += OnBufferUpdated;
        RenderBuffer();
        FocusEditorIfActive();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs args)
    {
        if (_viewModel is not null) _viewModel.Buffer.Updated -= OnBufferUpdated;
    }

    private void OnBufferUpdated(object? sender, EventArgs args)
    {
        if (_renderQueued || this.GetVisualRoot() is null) return;
        _renderQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _renderQueued = false;
            RenderBuffer();
        }, DispatcherPriority.Background);
    }

    private void RenderBuffer()
    {
        if (_viewModel is null || this.GetVisualRoot() is null || !Dispatcher.UIThread.CheckAccess()) return;
        var snapshot = _viewModel.Buffer.GetSnapshot();

        // The color spans and document must always belong to the same snapshot. Updating
        // the transformer first also keeps rendering safe when a tab changes its session.
        _colorizer.SetSnapshot(snapshot);
        if (!string.Equals(Editor.Document.Text, snapshot.Text, StringComparison.Ordinal))
            Editor.Document.Text = snapshot.Text;
        Editor.CaretOffset = Math.Min(snapshot.Text.Length, Editor.Document.TextLength);
        Editor.TextArea.TextView.InvalidateVisual();
        QueueScrollToLatest();
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs args)
    {
        // Keep roughly one fifth of the terminal below the latest output. The user can
        // immediately read the response and type the next command without extra scrolling.
        var bottomReserve = Math.Clamp(Editor.Bounds.Height * 0.20, 48, 180);
        Editor.Padding = new Thickness(12, 12, 12, bottomReserve);
        QueueScrollToLatest();
    }

    private void FocusEditorIfActive()
    {
        if (_viewModel?.IsActive != true) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (_viewModel?.IsActive == true && this.GetVisualRoot() is not null)
                Editor.TextArea.Focus();
        }, DispatcherPriority.Input);
    }

    private void QueueScrollToLatest() => _scrollToLatestPending = true;

    private void OnEditorLayoutUpdated(object? sender, EventArgs args)
    {
        if (!_scrollToLatestPending || this.GetVisualRoot() is null) return;
        _scrollToLatestPending = false;
        Dispatcher.UIThread.Post(() =>
        {
            if (this.GetVisualRoot() is null) return;
            Editor.ScrollTo(
                Editor.Document.LineCount,
                1,
                VisualYPosition.LineBottom,
                Editor.ViewportHeight * 0.88,
                0);
        }, DispatcherPriority.Background);
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs args)
    {
        _viewModel?.ActivateCommand.Execute(null);
        Editor.TextArea.Focus();
    }

    private void OnTextInput(object? sender, TextInputEventArgs args)
    {
        if (string.IsNullOrEmpty(args.Text) || _viewModel is null) return;
        args.Handled = true;
        _viewModel.ActivateCommand.Execute(null);
        SendInput(args.Text);
    }

    private void OnKeyDown(object? sender, KeyEventArgs args)
    {
        if (_viewModel is null) return;
        _viewModel.ActivateCommand.Execute(null);
        if (args.KeyModifiers.HasFlag(KeyModifiers.Control) && args.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            if (args.Key == Key.V)
            {
                args.Handled = true;
                _ = PasteSafelyAsync();
            }
            return; // Ctrl+Shift+C remains the editor's copy shortcut.
        }

        if (args.KeyModifiers.HasFlag(KeyModifiers.Control) && args.Key is >= Key.A and <= Key.Z)
        {
            args.Handled = true;
            SendInput(((char)(args.Key - Key.A + 1)).ToString());
            return;
        }

        var input = args.Key switch
        {
            Key.Enter => "\r",
            Key.Back => "\u007f",
            Key.Tab => "\t",
            Key.Escape => "\u001b",
            Key.Up => "\u001b[A",
            Key.Down => "\u001b[B",
            Key.Right => "\u001b[C",
            Key.Left => "\u001b[D",
            Key.Home => "\u001b[H",
            Key.End => "\u001b[F",
            Key.Delete => "\u001b[3~",
            Key.PageUp => "\u001b[5~",
            Key.PageDown => "\u001b[6~",
            _ => null
        };

        if (input is null) return;
        args.Handled = true;
        SendInput(input);
    }

    private void SendInput(string text) => _ = SendInputSafelyAsync(text);

    private async Task SendInputSafelyAsync(string text)
    {
        try
        {
            if (_viewModel is not null) await _viewModel.SendTextAsync(text);
        }
        catch (Exception exception)
        {
            if (_viewModel is not null) _viewModel.StatusMessage = $"Ошибка ввода: {exception.Message}";
        }
    }

    private async Task PasteSafelyAsync()
    {
        try
        {
            if (_viewModel is null) return;
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is null) return;
            var text = await clipboard.TryGetTextAsync();
            if (!string.IsNullOrEmpty(text)) await _viewModel.SendTextAsync(text);
        }
        catch (Exception exception)
        {
            if (_viewModel is not null) _viewModel.StatusMessage = $"Не удалось вставить текст: {exception.Message}";
        }
    }
}

internal sealed class AnsiColorizingTransformer : DocumentColorizingTransformer
{
    private static readonly IReadOnlyDictionary<TerminalColor, IBrush> DarkBrushes = CreateBrushes(true);
    private static readonly IReadOnlyDictionary<TerminalColor, IBrush> LightBrushes = CreateBrushes(false);
    private IReadOnlyList<TerminalColorSpan> _spans = [];

    public void SetSnapshot(TerminalSnapshot snapshot) => _spans = snapshot.Spans;

    protected override void ColorizeLine(DocumentLine line)
    {
        var lineStart = line.Offset;
        var lineEnd = line.EndOffset;
        foreach (var span in _spans)
        {
            var spanStart = span.Start;
            var spanEnd = span.Start + span.Length;
            var start = Math.Max(lineStart, spanStart);
            var end = Math.Min(lineEnd, spanEnd);
            if (start >= end || span.Color == TerminalColor.Default) continue;
            var brush = GetBrush(span.Color);
            ChangeLinePart(start, end, element => element.TextRunProperties.SetForegroundBrush(brush));
        }
    }

    private static IBrush GetBrush(TerminalColor color)
    {
        var dark = Application.Current?.ActualThemeVariant != ThemeVariant.Light;
        return (dark ? DarkBrushes : LightBrushes).GetValueOrDefault(color, Brushes.Gray);
    }

    private static IReadOnlyDictionary<TerminalColor, IBrush> CreateBrushes(bool dark)
    {
        return new Dictionary<TerminalColor, IBrush>
        {
            [TerminalColor.Black] = Brush.Parse(dark ? "#667085" : "#202938"),
            [TerminalColor.Red] = Brush.Parse(dark ? "#FF6B7A" : "#B42335"),
            [TerminalColor.Green] = Brush.Parse(dark ? "#5DD6A2" : "#087A55"),
            [TerminalColor.Yellow] = Brush.Parse(dark ? "#E9C46A" : "#8A5A00"),
            [TerminalColor.Blue] = Brush.Parse(dark ? "#70A7FF" : "#175CD3"),
            [TerminalColor.Magenta] = Brush.Parse(dark ? "#D792EA" : "#9333A8"),
            [TerminalColor.Cyan] = Brush.Parse(dark ? "#58D5E5" : "#087F8C"),
            [TerminalColor.White] = Brush.Parse(dark ? "#E6EAF0" : "#303948"),
            [TerminalColor.BrightBlack] = Brush.Parse(dark ? "#98A2B3" : "#667085"),
            [TerminalColor.BrightRed] = Brush.Parse(dark ? "#FF8C98" : "#C43246"),
            [TerminalColor.BrightGreen] = Brush.Parse(dark ? "#7CE8B8" : "#099268"),
            [TerminalColor.BrightYellow] = Brush.Parse(dark ? "#F4D35E" : "#9A6700"),
            [TerminalColor.BrightBlue] = Brush.Parse(dark ? "#94BDFF" : "#2477C9"),
            [TerminalColor.BrightMagenta] = Brush.Parse(dark ? "#E5ADEF" : "#A24294"),
            [TerminalColor.BrightCyan] = Brush.Parse(dark ? "#84E6F1" : "#0891B2"),
            [TerminalColor.BrightWhite] = Brush.Parse(dark ? "#F5F7FA" : "#111827")
        };
    }
}
