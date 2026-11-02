using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LazyLinuxSSH.Core.Interfaces;
using LazyLinuxSSH.Core.Models;
using LazyLinuxSSH.Desktop.Services;

namespace LazyLinuxSSH.Desktop.ViewModels;

public sealed partial class CommandTemplateListItemViewModel(CommandTemplate model) : ObservableObject
{
    public CommandTemplate Model { get; } = model;
    public Guid Id => Model.Id;
    public string Name => Model.Name;
    public bool IsDefault => Model.IsDefault || Id == CommandTemplate.DefaultId;
    public string Caption => AppText.T(IsDefault ? "Полный каталог · системный" : "Пользовательский шаблон");
    public IReadOnlySet<string>? SelectedKeys => ReadKeys(Model);

    public void RefreshLocalization() => OnPropertyChanged(nameof(Caption));
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isActive;

    private static IReadOnlySet<string>? ReadKeys(CommandTemplate template)
    {
        if (template.IsDefault || template.Id == CommandTemplate.DefaultId) return null;
        try
        {
            return new HashSet<string>(
                JsonSerializer.Deserialize<string[]>(template.SelectedCommandKeysJson) ?? [],
                StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }
    }
}

public sealed partial class TemplateCommandNodeViewModel : ObservableObject
{
    private readonly Action _selectionChanged;
    private readonly string _name;
    private bool _isSelected;

    public TemplateCommandNodeViewModel(string name, string command, string key, bool isSelected, Action selectionChanged)
    {
        _name = name;
        Command = command;
        Key = key;
        _isSelected = isSelected;
        _selectionChanged = selectionChanged;
    }

    public string Name => AppText.T(_name);
    public string Command { get; }
    public string Key { get; }

    public void RefreshLocalization() => OnPropertyChanged(nameof(Name));
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (!SetProperty(ref _isSelected, value)) return;
            _selectionChanged();
        }
    }
}

public sealed partial class TemplateCategoryNodeViewModel : ObservableObject
{
    private readonly Action _selectionChanged;
    private readonly Action<TemplateCategoryNodeViewModel>? _expandedChanged;
    private readonly string _name;
    private bool? _isChecked;
    private bool _updatingChildren;

    public TemplateCategoryNodeViewModel(
        QuickCommandCategory category,
        IReadOnlySet<string>? selectedKeys,
        Action selectionChanged,
        Action<TemplateCategoryNodeViewModel>? expandedChanged = null)
    {
        _name = category.Name;
        Icon = category.Icon;
        _selectionChanged = selectionChanged;
        _expandedChanged = expandedChanged;
        foreach (var command in category.Commands)
        {
            var key = $"{category.Name}::{command.Name}";
            Commands.Add(new TemplateCommandNodeViewModel(
                command.Name,
                command.Command,
                key,
                selectedKeys?.Contains(key) ?? true,
                OnChildSelectionChanged));
        }
        UpdateState(notifyChildren: false);
    }

    public string Name => AppText.T(_name);
    public string Icon { get; }
    public ObservableCollection<TemplateCommandNodeViewModel> Commands { get; } = [];
    public int SelectedCount => Commands.Count(command => command.IsSelected);
    public string SelectionLabel => AppText.CurrentLanguage == AppLanguage.English
        ? $"{SelectedCount} of {Commands.Count}"
        : $"{SelectedCount} из {Commands.Count}";
    public double ChevronAngle => IsExpanded ? 0 : -90;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChevronAngle))]
    private bool _isExpanded;

    [RelayCommand]
    private void ToggleExpanded()
    {
        IsExpanded = !IsExpanded;
        if (IsExpanded) _expandedChanged?.Invoke(this);
    }

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(SelectionLabel));
        foreach (var command in Commands) command.RefreshLocalization();
    }

    public bool? IsChecked
    {
        get => _isChecked;
        set
        {
            if (!SetProperty(ref _isChecked, value)) return;
            if (_updatingChildren || !value.HasValue) return;
            _updatingChildren = true;
            foreach (var command in Commands) command.IsSelected = value.Value;
            _updatingChildren = false;
            UpdateState(notifyChildren: false);
        }
    }

    private void OnChildSelectionChanged()
    {
        if (!_updatingChildren) UpdateState(notifyChildren: false);
    }

    private void UpdateState(bool notifyChildren)
    {
        _isChecked = SelectedCount == 0 ? false : SelectedCount == Commands.Count ? true : null;
        OnPropertyChanged(nameof(IsChecked));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectionLabel));
        _selectionChanged();
    }
}
