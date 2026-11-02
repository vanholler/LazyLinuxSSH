using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LazyLinuxSSH.Desktop.ViewModels;

public sealed partial class TerminalSlotViewModel : ObservableObject
{
    private readonly Action<TerminalSlotViewModel, TerminalViewModelBase> _assignSession;

    public TerminalSlotViewModel(
        int index,
        Action<TerminalSlotViewModel, TerminalViewModelBase> assignSession)
    {
        Index = index;
        _assignSession = assignSession;
    }

    public int Index { get; }
    public bool HasTerminal => Terminal is not null;
    public bool IsEmpty => IsVisible && Terminal is null;
    public bool HasAvailableSessions => AvailableSessions.Count > 0;
    public ObservableCollection<TerminalSlotSessionViewModel> AvailableSessions { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTerminal))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private TerminalViewModelBase? _terminal;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool _isVisible;

    public void SetAvailableSessions(IEnumerable<TerminalViewModelBase> sessions)
    {
        AvailableSessions.Clear();
        foreach (var session in sessions)
            AvailableSessions.Add(new TerminalSlotSessionViewModel(session, AssignSession));
        OnPropertyChanged(nameof(HasAvailableSessions));
    }

    private void AssignSession(TerminalViewModelBase session) => _assignSession(this, session);
}

public sealed partial class TerminalSlotSessionViewModel : ObservableObject
{
    private readonly Action<TerminalViewModelBase> _select;

    public TerminalSlotSessionViewModel(
        TerminalViewModelBase session,
        Action<TerminalViewModelBase> select)
    {
        Session = session;
        _select = select;
    }

    public TerminalViewModelBase Session { get; }
    public string Title => Session.Title;
    public string StateLabel => Session.StateLabel;

    [RelayCommand]
    private void Select() => _select(Session);
}
