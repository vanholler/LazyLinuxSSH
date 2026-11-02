using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LazyLinuxSSH.Desktop.ViewModels;

public enum WorkspaceTabKind
{
    Start,
    Terminal,
    History,
    Settings
}

public sealed partial class WorkspaceTabViewModel : ObservableObject
{
    public const int MaxGridCells = 16;

    private readonly Action<WorkspaceTabViewModel> _activate;
    private readonly Func<WorkspaceTabViewModel, Task> _close;
    private readonly TerminalViewModelBase?[] _gridSessions = new TerminalViewModelBase?[MaxGridCells];

    public WorkspaceTabViewModel(
        string title,
        WorkspaceTabKind kind,
        Action<WorkspaceTabViewModel> activate,
        Func<WorkspaceTabViewModel, Task> close,
        TerminalViewModelBase? terminal = null)
    {
        Title = title;
        Kind = kind;
        Terminal = terminal;
        _gridSessions[0] = terminal;
        _activate = activate;
        _close = close;
    }

    public Guid Id { get; } = Guid.NewGuid();
    public string Title { get; }
    public WorkspaceTabKind Kind { get; }
    public TerminalViewModelBase? Terminal { get; }
    public int LayoutMode { get; set; }
    public IReadOnlyList<TerminalViewModelBase?> GridSessions => _gridSessions;

    public TerminalViewModelBase? GetGridSession(int index) =>
        index >= 0 && index < _gridSessions.Length ? _gridSessions[index] : null;

    public void SetGridSession(int index, TerminalViewModelBase? session)
    {
        if (index > 0 && index < _gridSessions.Length) _gridSessions[index] = session;
    }

    public void RemoveGridSession(TerminalViewModelBase session)
    {
        for (var index = 1; index < _gridSessions.Length; index++)
        {
            if (ReferenceEquals(_gridSessions[index], session)) _gridSessions[index] = null;
        }
    }

    public string Icon => Kind switch
    {
        WorkspaceTabKind.History => "◷",
        WorkspaceTabKind.Settings => "⚙",
        WorkspaceTabKind.Terminal => "›_",
        _ => "⌂"
    };
    [ObservableProperty] private bool _isActive;

    [RelayCommand]
    private void Activate() => _activate(this);

    [RelayCommand]
    private Task Close() => _close(this);
}
