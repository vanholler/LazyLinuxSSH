using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LazyLinuxSSH.Core.Interfaces;
using LazyLinuxSSH.Core.Models;
using LazyLinuxSSH.Desktop.Services;

namespace LazyLinuxSSH.Desktop.ViewModels;

public sealed partial class CommandInputLineViewModel : ObservableObject
{
    [ObservableProperty] private string _value = string.Empty;
}

public sealed partial class CustomCommandViewModel : ObservableObject
{
    private readonly Func<CustomCommandViewModel, Task> _run;
    private readonly Action<CustomCommandViewModel> _edit;
    private readonly Func<CustomCommandViewModel, Task> _copy;
    private readonly Func<CustomCommandViewModel, Task> _delete;

    public CustomCommandViewModel(
        CustomCommand entity,
        IReadOnlyList<string> sequence,
        Func<CustomCommandViewModel, Task> run,
        Action<CustomCommandViewModel> edit,
        Func<CustomCommandViewModel, Task> copy,
        Func<CustomCommandViewModel, Task> delete)
    {
        Entity = entity;
        Sequence = sequence;
        _run = run;
        _edit = edit;
        _copy = copy;
        _delete = delete;
    }

    public CustomCommand Entity { get; }
    public IReadOnlyList<string> Sequence { get; }
    public Guid Id => Entity.Id;
    public string Name => Entity.Name;
    public string Summary => string.Join("  ·  ", Sequence);
    public string Description => string.Join(" · ", Sequence.Take(2));
    public string ScriptText => string.Join(Environment.NewLine, Sequence);
    public string Kind => Sequence.Count > 1 ? "Bash" : "Shell";

    [ObservableProperty]
    private bool _isSelected;

    [RelayCommand]
    private Task Run() => _run(this);

    [RelayCommand]
    private void Edit() => _edit(this);

    [RelayCommand]
    private Task Copy() => _copy(this);

    [RelayCommand]
    private Task Delete() => _delete(this);
}

public sealed partial class CustomCommandEditorViewModel : ObservableObject
{
    private readonly IQuickCommandService _quickCommandService;
    private readonly Guid? _editingId;

    public CustomCommandEditorViewModel(
        IQuickCommandService quickCommandService,
        CustomCommandViewModel? existing = null)
    {
        _quickCommandService = quickCommandService;
        _editingId = existing?.Id;
        Name = existing?.Name ?? string.Empty;
        if (existing is null || existing.Sequence.Count == 0)
        {
            Lines.Add(new CommandInputLineViewModel());
        }
        else
        {
            foreach (var command in existing.Sequence)
                Lines.Add(new CommandInputLineViewModel { Value = command });
        }
    }

    public event EventHandler<CustomCommand>? Saved;
    public event EventHandler? Cancelled;
    public ObservableCollection<CommandInputLineViewModel> Lines { get; } = [];
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool IsEditing => _editingId.HasValue;
    public string Title => AppText.T(IsEditing ? "Редактирование скрипта" : "Новый скрипт");
    public string NameLabel => AppText.T("Название скрипта");
    public string SaveButtonLabel => AppText.T(IsEditing ? "Сохранить изменения" : "Создать скрипт");

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(NameLabel));
        OnPropertyChanged(nameof(SaveButtonLabel));
    }

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;
    [ObservableProperty] private bool _isBusy;

    [RelayCommand]
    private void AddLine() => Lines.Add(new CommandInputLineViewModel());

    [RelayCommand]
    private void RemoveLine(CommandInputLineViewModel? line)
    {
        if (line is not null && Lines.Count > 1) Lines.Remove(line);
    }

    [RelayCommand]
    private async Task Save()
    {
        ErrorMessage = null;
        var commands = Lines.Select(line => line.Value.Trim()).Where(value => value.Length > 0).ToArray();
        if (string.IsNullOrWhiteSpace(Name))
        {
            ErrorMessage = AppText.T("Введите название команды.");
            return;
        }

        if (commands.Length == 0)
        {
            ErrorMessage = AppText.T("Добавьте хотя бы одну непустую команду.");
            return;
        }

        IsBusy = true;
        try
        {
            var saved = await _quickCommandService.SaveCustomCommandAsync(_editingId, Name, commands);
            Saved?.Invoke(this, saved);
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Cancel() => Cancelled?.Invoke(this, EventArgs.Empty);
}
