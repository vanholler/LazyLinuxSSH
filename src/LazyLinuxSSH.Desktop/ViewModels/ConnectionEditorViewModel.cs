using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LazyLinuxSSH.Core.Interfaces;
using LazyLinuxSSH.Core.Models;

namespace LazyLinuxSSH.Desktop.ViewModels;

public sealed partial class ConnectionEditorViewModel : ObservableObject
{
    private readonly IConnectionRepository _repository;
    private readonly ICredentialProtector _credentialProtector;
    private readonly ConnectionProfile? _existing;

    public ConnectionEditorViewModel(
        IConnectionRepository repository,
        ICredentialProtector credentialProtector,
        IEnumerable<ConnectionGroup> groups,
        ConnectionProfile? existing)
    {
        _repository = repository;
        _credentialProtector = credentialProtector;
        _existing = existing;
        Groups = new(groups.OrderBy(group => group.Name));
        Title = existing is null ? "Новое подключение" : "Изменить подключение";
        Name = existing?.Name ?? string.Empty;
        Host = existing?.Host ?? string.Empty;
        Port = existing?.Port ?? 22;
        Username = existing?.Username ?? string.Empty;
        AuthenticationIndex = existing?.AuthenticationMethod == AuthenticationMethod.PrivateKey ? 1 : 0;
        PrivateKeyPath = existing?.PrivateKeyPath ?? string.Empty;
        SelectedGroup = Groups.SingleOrDefault(group => group.Id == existing?.GroupId) ?? Groups.FirstOrDefault();
    }

    public event EventHandler<ConnectionProfile>? Saved;
    public event EventHandler? Cancelled;
    public ObservableCollection<ConnectionGroup> Groups { get; }
    public bool IsPrivateKey => AuthenticationIndex == 1;
    public bool HasPassword => !IsPrivateKey;
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool IsExisting => _existing is not null;
    public string AdvancedChevron => IsAdvancedOpen ? "⌃" : "⌄";
    public char PasswordMask => IsPasswordVisible ? '\0' : '•';
    public char PassphraseMask => IsPassphraseVisible ? '\0' : '•';

    [ObservableProperty] private string _title;
    [ObservableProperty] private string _name;
    [ObservableProperty] private string _host;
    [ObservableProperty] private int _port;
    [ObservableProperty] private string _username;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PasswordMask))]
    private bool _isPasswordVisible;
    [ObservableProperty] private string _privateKeyPath;
    [ObservableProperty] private string _passphrase = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PassphraseMask))]
    private bool _isPassphraseVisible;
    [ObservableProperty] private ConnectionGroup? _selectedGroup;
    [ObservableProperty] private bool _isGroupPopupOpen;
    [ObservableProperty] private string _newGroupName = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AdvancedChevron))]
    private bool _isAdvancedOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPrivateKey))]
    [NotifyPropertyChangedFor(nameof(HasPassword))]
    private int _authenticationIndex;

    [RelayCommand]
    private void SetAuthentication(object? parameter) => AuthenticationIndex = parameter switch
    {
        int intValue => Math.Clamp(intValue, 0, 1),
        string text when int.TryParse(text, out var parsedValue) => Math.Clamp(parsedValue, 0, 1),
        _ => 0
    };

    [RelayCommand]
    private void ToggleAdvanced() => IsAdvancedOpen = !IsAdvancedOpen;

    [RelayCommand]
    private void TogglePasswordVisibility() => IsPasswordVisible = !IsPasswordVisible;

    [RelayCommand]
    private void TogglePassphraseVisibility() => IsPassphraseVisible = !IsPassphraseVisible;

    [RelayCommand]
    private void OpenGroupPopup()
    {
        NewGroupName = string.Empty;
        ErrorMessage = null;
        IsGroupPopupOpen = true;
    }

    [RelayCommand]
    private void CloseGroupPopup() => IsGroupPopupOpen = false;

    [RelayCommand]
    private async Task AddGroup()
    {
        ErrorMessage = null;
        try
        {
            var group = await _repository.AddGroupAsync(NewGroupName);
            var ordered = Groups.Concat([group]).OrderBy(item => item.Name).ToArray();
            Groups.Clear();
            foreach (var item in ordered) Groups.Add(item);
            SelectedGroup = group;
            IsGroupPopupOpen = false;
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    [RelayCommand]
    private async Task Save()
    {
        ErrorMessage = null;
        var validation = Validate();
        if (validation is not null)
        {
            ErrorMessage = validation;
            return;
        }

        IsBusy = true;
        try
        {
            var profile = _existing ?? new ConnectionProfile { CreatedAt = DateTimeOffset.UtcNow };
            profile.Name = Name.Trim();
            profile.Host = Host.Trim();
            profile.Port = Port;
            profile.Username = Username.Trim();
            profile.GroupId = SelectedGroup!.Id;
            profile.Group = null;
            profile.AuthenticationMethod = IsPrivateKey ? AuthenticationMethod.PrivateKey : AuthenticationMethod.Password;
            profile.PrivateKeyPath = IsPrivateKey ? PrivateKeyPath.Trim() : null;

            if (IsPrivateKey)
            {
                profile.EncryptedPassword = null;
                profile.EncryptedPassphrase = string.IsNullOrEmpty(Passphrase)
                    ? _existing?.EncryptedPassphrase
                    : await _credentialProtector.ProtectAsync(Passphrase);
            }
            else
            {
                profile.EncryptedPassphrase = null;
                profile.EncryptedPassword = string.IsNullOrEmpty(Password)
                    ? _existing?.EncryptedPassword
                    : await _credentialProtector.ProtectAsync(Password);
            }

            await _repository.SaveProfileAsync(profile);
            Saved?.Invoke(this, profile);
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

    private string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Name)) return "Введите название подключения.";
        if (string.IsNullOrWhiteSpace(Host)) return "Введите адрес сервера.";
        if (Port is < 1 or > 65535) return "Порт должен быть в диапазоне 1–65535.";
        if (string.IsNullOrWhiteSpace(Username)) return "Введите имя пользователя.";
        if (SelectedGroup is null) return "Выберите или создайте группу.";
        if (IsPrivateKey && string.IsNullOrWhiteSpace(PrivateKeyPath)) return "Укажите путь к закрытому ключу.";
        if (!IsPrivateKey && string.IsNullOrEmpty(Password) && string.IsNullOrEmpty(_existing?.EncryptedPassword))
            return "Введите пароль для нового подключения.";
        return null;
    }
}
