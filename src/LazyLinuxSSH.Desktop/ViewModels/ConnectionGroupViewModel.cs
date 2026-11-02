using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LazyLinuxSSH.Core.Models;

namespace LazyLinuxSSH.Desktop.ViewModels;

public sealed partial class ConnectionGroupViewModel : ObservableObject
{
    private readonly Action<Guid, bool> _expandedChanged;

    public ConnectionGroupViewModel(
        ConnectionGroup group,
        bool isExpanded,
        Action<Guid, bool> expandedChanged)
    {
        Id = group.Id;
        Name = group.Name;
        Connections = group.Connections;
        HasConnections = Connections.Count > 0;
        _isExpanded = HasConnections && isExpanded;
        _expandedChanged = expandedChanged;
    }

    public Guid Id { get; }
    public string Name { get; }
    public IReadOnlyList<ConnectionProfile> Connections { get; }
    public bool HasConnections { get; }
    public double ChevronAngle => IsExpanded ? 0 : -90;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChevronAngle))]
    private bool _isExpanded;

    [RelayCommand]
    private void ToggleExpanded()
    {
        if (!HasConnections) return;
        IsExpanded = !IsExpanded;
        _expandedChanged(Id, IsExpanded);
    }
}
