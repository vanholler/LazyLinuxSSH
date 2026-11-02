using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Search;
using LazyLinuxSSH.Desktop.ViewModels;

namespace LazyLinuxSSH.Desktop.Controls;

public partial class FileEditorView : UserControl
{
    private FileEditorViewModel? _viewModel;
    private bool _loadingDocument;

    public FileEditorView()
    {
        InitializeComponent();
        FileEditor.Options.ConvertTabsToSpaces = true;
        FileEditor.Options.IndentationSize = 4;
        FileEditor.Options.CutCopyWholeLine = true;
        FileEditor.Options.EnableRectangularSelection = true;
        FileEditor.Options.HighlightCurrentLine = true;
        SearchPanel.Install(FileEditor);

        FileEditor.Document.TextChanged += OnDocumentTextChanged;
        FileEditor.TextArea.Caret.PositionChanged += OnCaretOrSelectionChanged;
        FileEditor.TextArea.SelectionChanged += OnCaretOrSelectionChanged;
        FileEditor.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        DataContextChanged += OnDataContextChanged;
        AttachedToVisualTree += OnAttachedToVisualTree;
    }

    private void OnDataContextChanged(object? sender, EventArgs args)
    {
        if (_viewModel is not null) _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel = DataContext as FileEditorViewModel;
        if (_viewModel is not null) _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _loadingDocument = true;
        FileEditor.Document.Text = _viewModel?.Content ?? string.Empty;
        _loadingDocument = false;
        FileEditor.CaretOffset = 0;
        UpdateDocumentState();
        FocusEditor();
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs args) => FocusEditor();

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (_viewModel is null) return;
        if (args.PropertyName == nameof(FileEditorViewModel.Content) &&
            !_loadingDocument &&
            !string.Equals(FileEditor.Document.Text, _viewModel.Content, StringComparison.Ordinal))
        {
            _loadingDocument = true;
            FileEditor.Document.Text = _viewModel.Content;
            FileEditor.CaretOffset = Math.Min(FileEditor.CaretOffset, FileEditor.Document.TextLength);
            _loadingDocument = false;
            UpdateDocumentState();
        }

        if (args.PropertyName == nameof(FileEditorViewModel.IsBusy) && !_viewModel.IsBusy)
            FocusEditor();
    }

    private void FocusEditor()
    {
        if (_viewModel is null) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (_viewModel?.IsBusy == false && this.GetVisualRoot() is not null)
                FileEditor.TextArea.Focus();
        }, DispatcherPriority.Input);
    }

    private void OnDocumentTextChanged(object? sender, EventArgs args)
    {
        if (_viewModel is null) return;
        if (!_loadingDocument) _viewModel.Content = FileEditor.Document.Text;
        UpdateDocumentState();
    }

    private void OnCaretOrSelectionChanged(object? sender, EventArgs args) => UpdateDocumentState();

    private void UpdateDocumentState()
    {
        if (_viewModel is null) return;
        var caret = FileEditor.TextArea.Caret;
        _viewModel.UpdateDocumentState(
            caret.Line,
            caret.Column,
            FileEditor.Document.LineCount,
            FileEditor.TextArea.Selection.Length);
    }

    private void OnKeyDown(object? sender, KeyEventArgs args)
    {
        if (_viewModel is null || args.Key != Key.S ||
            !args.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
        args.Handled = true;
        if (_viewModel.SaveCommand.CanExecute(null)) _viewModel.SaveCommand.Execute(null);
    }
}
