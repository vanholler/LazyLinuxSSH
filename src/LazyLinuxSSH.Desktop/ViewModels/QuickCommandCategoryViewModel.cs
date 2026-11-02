using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LazyLinuxSSH.Core.Models;
using LazyLinuxSSH.Desktop.Services;

namespace LazyLinuxSSH.Desktop.ViewModels;

public sealed partial class QuickCommandViewModel : ObservableObject
{
    private readonly string _categoryName;

    public QuickCommandViewModel(QuickCommand model, string categoryName = "")
    {
        Model = model;
        _categoryName = categoryName;
    }

    public QuickCommand Model { get; }
    public string Key => $"{_categoryName}::{Model.Name}";
    public string Name => AppText.T(Model.Name);
    public string Command => Model.Command;
    public string Description => AppText.T(Model.Description);

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Description));
    }
    public bool IsDangerous => Model.IsDangerous;

    [ObservableProperty]
    private bool _isSelected;
}

public sealed partial class QuickCommandCategoryViewModel : ObservableObject
{
    private readonly IReadOnlyList<QuickCommandViewModel> _allCommands;
    private readonly Action<QuickCommandCategoryViewModel>? _expanded;
    private readonly string _originalName;

    public QuickCommandCategoryViewModel(QuickCommandCategory category, Action<QuickCommandCategoryViewModel>? expanded = null)
    {
        _originalName = category.Name;
        Icon = category.Icon;
        _expanded = expanded;
        _allCommands = category.Commands.Select(command => new QuickCommandViewModel(command, Name)).ToArray();
        foreach (var command in _allCommands) Commands.Add(command);
    }

    public string Name => AppText.T(_originalName);
    public string Icon { get; }
    public int TotalCount => _allCommands.Count;
    public ObservableCollection<QuickCommandViewModel> Commands { get; } = [];
    public bool HasCommands => Commands.Count > 0;
    public string CountLabel => Commands.Count == TotalCount ? TotalCount.ToString() : $"{Commands.Count}/{TotalCount}";

    [ObservableProperty]
    private bool _isExpanded;

    [RelayCommand]
    private void ToggleExpanded() => IsExpanded = !IsExpanded;

    partial void OnIsExpandedChanged(bool value)
    {
        if (value) _expanded?.Invoke(this);
    }

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(Name));
        foreach (var command in _allCommands) command.RefreshLocalization();
    }

    public void ApplyFilter(string? query, bool expandWhenEmpty = false)
    {
        var normalized = query?.Trim() ?? string.Empty;
        var categoryMatches = Name.Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
                              _originalName.Contains(normalized, StringComparison.OrdinalIgnoreCase);
        Commands.Clear();
        foreach (var command in _allCommands)
        {
            if (normalized.Length == 0 || categoryMatches || Matches(command, normalized)) Commands.Add(command);
        }

        IsExpanded = HasCommands && (normalized.Length > 0 || expandWhenEmpty);
        OnPropertyChanged(nameof(HasCommands));
        OnPropertyChanged(nameof(CountLabel));
    }

    private static bool Matches(QuickCommandViewModel command, string query) =>
        command.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        command.Command.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        command.Description.Contains(query, StringComparison.OrdinalIgnoreCase);
}
