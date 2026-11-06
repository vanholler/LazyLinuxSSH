using System.Reflection;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LazyLinuxSSH.Core.Interfaces;
using LazyLinuxSSH.Core.Models;
using LazyLinuxSSH.Core.Services;
using LazyLinuxSSH.Desktop.Services;

namespace LazyLinuxSSH.Desktop.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly IDatabaseInitializer _databaseInitializer;
    private readonly IConnectionRepository _connectionRepository;
    private readonly ICredentialProtector _credentialProtector;
    private readonly IQuickCommandService _quickCommandService;
    private readonly ICommandSequenceExecutor _sequenceExecutor;
    private readonly ISshSessionManager _sessionManager;
    private readonly IClipboardService _clipboardService;
    private readonly IFileSaveDialogService _fileSaveDialogService;
    private readonly IThemeService _themeService;
    private readonly ICommandHistoryService _historyService;
    private readonly ILocalShellSessionFactory _localShellSessionFactory;
    private readonly ICommandTemplateService _commandTemplateService;
    private readonly IAppLocalizationService? _localizationService;
    private readonly IReadOnlyList<QuickCommandCategory> _catalogCategories;
    private ConnectionProfile? _profilePendingDeletion;
    private QuickCommandViewModel? _pendingDangerousCommand;
    private readonly Dictionary<Guid, bool> _groupExpansion = [];

    public MainWindowViewModel(
        IDatabaseInitializer databaseInitializer,
        IConnectionRepository connectionRepository,
        ICredentialProtector credentialProtector,
        IQuickCommandService quickCommandService,
        ICommandSequenceExecutor sequenceExecutor,
        ISshSessionManager sessionManager,
        IClipboardService clipboardService,
        IFileSaveDialogService fileSaveDialogService,
        IThemeService themeService,
        ICommandHistoryService historyService,
        ILocalShellSessionFactory localShellSessionFactory,
        ICommandTemplateService commandTemplateService,
        IAppLocalizationService? localizationService = null)
    {
        _databaseInitializer = databaseInitializer;
        _connectionRepository = connectionRepository;
        _credentialProtector = credentialProtector;
        _quickCommandService = quickCommandService;
        _sequenceExecutor = sequenceExecutor;
        _sessionManager = sessionManager;
        _clipboardService = clipboardService;
        _fileSaveDialogService = fileSaveDialogService;
        _themeService = themeService;
        _historyService = historyService;
        _localShellSessionFactory = localShellSessionFactory;
        _commandTemplateService = commandTemplateService;
        _localizationService = localizationService;
        AppText.ApplyResources();
        if (_localizationService is not null)
            _localizationService.LanguageChanged += OnLanguageChanged;
        for (var index = 0; index < WorkspaceTabViewModel.MaxGridCells; index++)
            TerminalSlots.Add(new TerminalSlotViewModel(index, AssignSessionToSlot));
        _catalogCategories = quickCommandService.GetBuiltInCategories();
        TotalCommandCount = _catalogCategories.Sum(category => category.Commands.Count);
        var defaultTemplate = CreateDefaultTemplate();
        CommandTemplates.Add(defaultTemplate);
        ActiveTemplate = defaultTemplate;
        RebuildQuickCatalog();
        SelectedTemplate = defaultTemplate;
        IsDarkTheme = themeService.CurrentTheme == AppTheme.Dark;
        themeService.ThemeChanged += OnThemeChanged;
        OpenNewTab();
    }

    public string AppVersion { get; } = ReadAppVersion();
    public string StatusBarTechLabel => $"{AppVersion}   ·   SSH.NET   ·   UTF-8   ·   PTY";

    public ObservableCollection<ConnectionGroup> Groups { get; } = [];
    public ObservableCollection<ConnectionGroupViewModel> VisibleGroups { get; } = [];
    public ObservableCollection<ConnectionProfile> SavedConnections { get; } = [];
    public ObservableCollection<TerminalViewModelBase> Sessions { get; } = [];
    public ObservableCollection<TerminalSlotViewModel> TerminalSlots { get; } = [];
    public ObservableCollection<WorkspaceTabViewModel> Tabs { get; } = [];
    public ObservableCollection<CommandHistoryEntry> HistoryEntries { get; } = [];
    public ObservableCollection<CustomCommandViewModel> CustomCommands { get; } = [];
    public ObservableCollection<CustomCommandViewModel> VisibleCustomCommands { get; } = [];
    public ObservableCollection<QuickCommandCategoryViewModel> BuiltInCategories { get; } = [];
    public ObservableCollection<CommandTemplateListItemViewModel> CommandTemplates { get; } = [];
    public ObservableCollection<TemplateCategoryNodeViewModel> TemplateTree { get; } = [];
    public int TotalCommandCount { get; }
    public int GridRows => LayoutMode == 0 ? 1 : LayoutMode == 1 ? 2 : 4;
    public int GridColumns => GridRows;
    public string QuickModeLabel => AppText.T(QuickCopyMode ? "Копировать" : "Выполнять");
    public bool IsExecuteMode => !QuickCopyMode;
    public bool HasNoCommandResults => !string.IsNullOrWhiteSpace(CommandSearchText) && BuiltInCategories.All(category => !category.HasCommands);
    public string ThemeButtonLabel => AppText.T(IsDarkTheme ? "Светлая тема" : "Тёмная тема");
    public string LayoutLabel => AppText.T(LayoutMode == 0 ? "Вкладки" : LayoutMode == 1 ? "Сетка 2×2" : "Сетка 4×4");
    public bool IsSingleLayout => LayoutMode == 0;
    public bool IsGrid22Layout => LayoutMode == 1;
    public bool IsGrid44Layout => LayoutMode == 2;
    public bool HasCustomCommands => CustomCommands.Count > 0;
    public bool HasVisibleCustomCommands => VisibleCustomCommands.Count > 0;
    public bool HasNoScriptResults => !HasVisibleCustomCommands;
    public bool IsQuickCommandsActive => !IsScriptsActive;
    public bool HasActiveSession => ActiveSession is not null;
    public bool IsStartTabActive => ActiveTab?.Kind == WorkspaceTabKind.Start;
    public bool IsHistoryTabActive => ActiveTab?.Kind == WorkspaceTabKind.History;
    public bool IsSettingsTabActive => ActiveTab?.Kind == WorkspaceTabKind.Settings;
    public bool IsTerminalTabActive => ActiveTab?.Kind == WorkspaceTabKind.Terminal;
    public bool HasConnections => Groups.Any(group => group.Connections.Count > 0);
    public bool ShowCreateConnection => !HasConnections;
    public bool HasHistory => HistoryEntries.Count > 0;
    public string HomeTitle => AppText.T(HasConnections ? "Выберите сервер для подключения" : "Начните с первого подключения");
    public string HomeDescription => AppText.T(HasConnections
        ? "Нажмите «Подключиться» и выберите сервер — каждая сессия появится в новой вкладке."
        : "Создайте SSH-подключение к Linux-серверу или откройте терминал локального компьютера.");
    public string HistorySummary => AppText.T(HasHistory
        ? $"{HistoryEntries.Count} команд · сохраняется локально"
        : "Запущенные команды появятся здесь");
    public string CatalogSummary => AppText.CurrentLanguage == AppLanguage.English
        ? $"{BuiltInCategories.Count} sections · {BuiltInCategories.Sum(category => category.TotalCount)} of {TotalCommandCount} commands"
        : $"{BuiltInCategories.Count} разделов · {BuiltInCategories.Sum(category => category.TotalCount)} из {TotalCommandCount} команд";
    public bool IsDefaultTemplate => SelectedTemplate?.IsDefault ?? true;
    public bool IsRussianLanguage => AppText.CurrentLanguage == AppLanguage.Russian;
    public bool IsEnglishLanguage => AppText.CurrentLanguage == AppLanguage.English;
    public bool IsTemplatesSettingsActive => !IsInterfaceSettingsActive;
    public bool CanEditTemplate => !IsDefaultTemplate;
    public bool CanSaveTemplate => CanEditTemplate && !string.IsNullOrWhiteSpace(TemplateName) && SelectedTemplateCommandCount > 0 && !IsBusy;
    public string TemplateSelectionSummary => AppText.CurrentLanguage == AppLanguage.English
        ? $"Selected: {SelectedTemplateCommandCount} of {TotalCommandCount} commands"
        : $"Выбрано: {SelectedTemplateCommandCount} из {TotalCommandCount} команд";
    public string ActiveTemplateName => ActiveTemplate?.Name ?? "Default";
    public string ActiveTemplateBadge => AppText.T("Активен: {0}", ActiveTemplateName);
    public string PanelTemplateLabel => AppText.T("Панель команд: {0}", ActiveTemplateName);
    public bool IsLeftPanelCollapsed => !IsLeftPanelVisible;
    public double LeftPanelWidth => IsLeftPanelVisible ? 300 : 64;
    public string LeftPanelButtonLabel => AppText.T(IsLeftPanelVisible ? "Свернуть панель подключений" : "Развернуть панель подключений");

    [ObservableProperty] private string _statusMessage = "Инициализация…";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLeftPanelCollapsed))]
    [NotifyPropertyChangedFor(nameof(LeftPanelWidth))]
    [NotifyPropertyChangedFor(nameof(LeftPanelButtonLabel))]
    private bool _isLeftPanelVisible = true;
    [ObservableProperty] private bool _isRightPanelVisible = true;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(QuickModeLabel))]
    [NotifyPropertyChangedFor(nameof(IsExecuteMode))]
    private bool _quickCopyMode;
    [ObservableProperty] private string _commandSearchText = string.Empty;
    [ObservableProperty] private string _scriptSearchText = string.Empty;
    [ObservableProperty] private string _serverSearchText = string.Empty;
    [ObservableProperty] private QuickCommandViewModel? _selectedQuickCommand;
    [ObservableProperty] private bool _isDangerousConfirmationOpen;
    [ObservableProperty] private string _dangerousConfirmationText = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsQuickCommandsActive))]
    private bool _isScriptsActive;
    [ObservableProperty] private bool _isDarkTheme;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTemplatesSettingsActive))]
    private bool _isInterfaceSettingsActive;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDefaultTemplate))]
    [NotifyPropertyChangedFor(nameof(CanEditTemplate))]
    [NotifyPropertyChangedFor(nameof(CanSaveTemplate))]
    private CommandTemplateListItemViewModel? _selectedTemplate;
    [ObservableProperty] private CommandTemplateListItemViewModel? _activeTemplate;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSaveTemplate))]
    private string _templateName = "Default";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TemplateSelectionSummary))]
    [NotifyPropertyChangedFor(nameof(CanSaveTemplate))]
    private int _selectedTemplateCommandCount;
    [ObservableProperty] private bool _isDeleteConfirmationOpen;
    [ObservableProperty] private string _deleteConfirmationText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConnectionEditorOpen))]
    private ConnectionEditorViewModel? _connectionEditor;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustomCommandEditorOpen))]
    private CustomCommandEditorViewModel? _customCommandEditor;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GridRows))]
    [NotifyPropertyChangedFor(nameof(GridColumns))]
    [NotifyPropertyChangedFor(nameof(LayoutLabel))]
    [NotifyPropertyChangedFor(nameof(IsSingleLayout))]
    [NotifyPropertyChangedFor(nameof(IsGrid22Layout))]
    [NotifyPropertyChangedFor(nameof(IsGrid44Layout))]
    private int _layoutMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveSession))]
    private TerminalViewModelBase? _activeSession;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStartTabActive))]
    [NotifyPropertyChangedFor(nameof(IsHistoryTabActive))]
    [NotifyPropertyChangedFor(nameof(IsSettingsTabActive))]
    [NotifyPropertyChangedFor(nameof(IsTerminalTabActive))]
    private WorkspaceTabViewModel? _activeTab;

    public bool IsConnectionEditorOpen => ConnectionEditor is not null;
    public bool IsCustomCommandEditorOpen => CustomCommandEditor is not null;

    public async Task InitializeAsync()
    {
        IsBusy = true;
        try
        {
            await _databaseInitializer.InitializeAsync();
            await LoadConnectionsAsync();
            await LoadCustomCommandsAsync();
            await LoadCommandTemplatesAsync();
            StatusMessage = "Готово. Выберите сервер или создайте подключение.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Ошибка инициализации: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void OpenNewTab()
    {
        var tab = CreateTab("Новая вкладка", WorkspaceTabKind.Start);
        Tabs.Add(tab);
        ActiveTab = tab;
    }

    [RelayCommand]
    private void ActivateTab(WorkspaceTabViewModel? tab)
    {
        if (tab is not null) ActiveTab = tab;
    }

    [RelayCommand]
    private async Task CloseTab(WorkspaceTabViewModel? tab)
    {
        if (tab is null) return;
        if (tab.Terminal is not null)
        {
            await CloseSessionAsync(tab.Terminal);
            return;
        }

        var index = Tabs.IndexOf(tab);
        var wasActive = ActiveTab == tab;
        Tabs.Remove(tab);
        if (wasActive) ActiveTab = Tabs.ElementAtOrDefault(Math.Min(index, Tabs.Count - 1));
        EnsureAnyTab();
    }

    [RelayCommand]
    private async Task OpenHistory()
    {
        var tab = Tabs.FirstOrDefault(item => item.Kind == WorkspaceTabKind.History);
        if (tab is null)
        {
            tab = CreateTab("История", WorkspaceTabKind.History);
            Tabs.Add(tab);
        }
        ActiveTab = tab;
        await RefreshHistoryAsync();
    }

    [RelayCommand]
    private void OpenSettings()
    {
        var tab = Tabs.FirstOrDefault(item => item.Kind == WorkspaceTabKind.Settings);
        if (tab is null)
        {
            tab = CreateTab("Настройки", WorkspaceTabKind.Settings);
            Tabs.Add(tab);
        }
        ActiveTab = tab;
    }

    [RelayCommand]
    private void SelectCommandTemplate(CommandTemplateListItemViewModel? template)
    {
        if (template is not null) SelectedTemplate = template;
    }

    [RelayCommand]
    private void NewCommandTemplate()
    {
        var allKeys = _catalogCategories.SelectMany(category =>
            category.Commands.Select(command => $"{category.Name}::{command.Name}"));
        var draft = new CommandTemplate
        {
            Id = Guid.NewGuid(),
            Name = "Новый шаблон",
            SelectedCommandKeysJson = JsonSerializer.Serialize(allKeys)
        };
        SelectedTemplate = new CommandTemplateListItemViewModel(draft);
    }

    [RelayCommand]
    private async Task SaveCommandTemplate()
    {
        if (SelectedTemplate is null || IsDefaultTemplate) return;
        var keys = TemplateTree.SelectMany(category => category.Commands)
            .Where(command => command.IsSelected)
            .Select(command => command.Key)
            .ToArray();
        try
        {
            IsBusy = true;
            OnPropertyChanged(nameof(CanSaveTemplate));
            var saved = await _commandTemplateService.SaveCustomAsync(SelectedTemplate.Id, TemplateName, keys);
            await LoadCommandTemplatesAsync(saved.Id);
            SetActiveTemplate(SelectedTemplate!);
            StatusMessage = $"Шаблон «{saved.Name}» сохранён и применён.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Не удалось сохранить шаблон: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(CanSaveTemplate));
        }
    }

    [RelayCommand]
    private void ApplySelectedTemplate()
    {
        if (SelectedTemplate is null || !CommandTemplates.Any(template => template.Id == SelectedTemplate.Id))
        {
            StatusMessage = "Сначала сохраните новый шаблон.";
            return;
        }
        SetActiveTemplate(SelectedTemplate);
        StatusMessage = $"Применён шаблон «{ActiveTemplateName}».";
    }

    [RelayCommand]
    private async Task RefreshHistory()
    {
        await RefreshHistoryAsync();
    }

    [RelayCommand]
    private async Task CopyHistoryEntry(CommandHistoryEntry? entry)
    {
        if (entry is null) return;
        try
        {
            await _clipboardService.SetTextAsync(entry.CommandText);
            StatusMessage = "Команда скопирована в буфер обмена.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Не удалось скопировать команду: {exception.Message}";
        }
    }

    [RelayCommand]
    private async Task ClearHistory()
    {
        try
        {
            await _historyService.ClearAsync();
            HistoryEntries.Clear();
            NotifyHistoryChanged();
            StatusMessage = "История команд очищена.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Не удалось очистить историю: {exception.Message}";
        }
    }

    [RelayCommand]
    private async Task OpenLocalTerminal()
    {
        try
        {
            var terminal = new LocalTerminalViewModel(
                _localShellSessionFactory,
                _historyService,
                _clipboardService,
                _fileSaveDialogService,
                CloseSessionAsync,
                SetActiveSession);
            var tab = CreateTab(terminal.Title, WorkspaceTabKind.Terminal, terminal);
            Sessions.Add(terminal);

            if (ActiveTab?.Kind == WorkspaceTabKind.Start)
            {
                var index = Tabs.IndexOf(ActiveTab);
                Tabs[index] = tab;
            }
            else
            {
                Tabs.Add(tab);
            }

            ActiveTab = tab;
            await terminal.StartAsync();
            StatusMessage = terminal.IsConnected
                ? $"Локальный терминал открыт во вкладке: {terminal.StateLabel}."
                : terminal.StatusMessage;
        }
        catch (Exception exception)
        {
            StatusMessage = $"Не удалось открыть локальный терминал: {exception.Message}";
        }
    }

    [RelayCommand]
    private async Task NewConnection()
    {
        if (Groups.Count == 0) await LoadConnectionsAsync();
        await OpenConnectionEditorAsync(null);
    }

    [RelayCommand]
    private async Task EditConnection(ConnectionProfile? profile)
    {
        if (profile is not null) await OpenConnectionEditorAsync(profile);
    }

    [RelayCommand]
    private async Task Connect(ConnectionProfile? profile)
    {
        if (profile is null) return;
        var terminal = new TerminalSessionViewModel(
            profile,
            _sessionManager,
            _sequenceExecutor,
            _historyService,
            _clipboardService,
            _fileSaveDialogService,
            CloseSessionAsync,
            SetActiveSession);
        var tab = CreateTab(terminal.Title, WorkspaceTabKind.Terminal, terminal);
        Sessions.Add(terminal);
        Tabs.Add(tab);
        ActiveTab = tab;
        StatusMessage = $"Подключение: {profile.Name}";
        await terminal.ConnectAsync();
        StatusMessage = terminal.IsConnected
            ? $"Подключено: {profile.Name}"
            : $"Не удалось подключить {profile.Name}: {terminal.StatusMessage}";
    }

    [RelayCommand]
    private void SetLayout(object? parameter)
    {
        var mode = parameter switch
        {
            int intValue => intValue,
            string text when int.TryParse(text, out var parsedValue) => parsedValue,
            _ => 0
        };
        if (ActiveTab?.Kind != WorkspaceTabKind.Terminal) return;
        LayoutMode = Math.Clamp(mode, 0, 2);
        ActiveTab.LayoutMode = LayoutMode;
        if (LayoutMode == 0) ActiveSession = ActiveTab.Terminal;
        RefreshTerminalSlots();
    }

    [RelayCommand]
    private void RequestDeleteConnection(ConnectionProfile? profile)
    {
        if (profile is null) return;
        _profilePendingDeletion = profile;
        DeleteConfirmationText = $"Удалить подключение «{profile.Name}»? Активные сессии этого профиля будут закрыты.";
        IsDeleteConfirmationOpen = true;
    }

    [RelayCommand]
    private void CancelDeleteConnection()
    {
        _profilePendingDeletion = null;
        IsDeleteConfirmationOpen = false;
    }

    [RelayCommand]
    private async Task ConfirmDeleteConnection()
    {
        if (_profilePendingDeletion is null) return;
        try
        {
            var profileId = _profilePendingDeletion.Id;
            foreach (var session in Sessions.OfType<TerminalSessionViewModel>().Where(session => session.Profile.Id == profileId).ToArray())
            {
                await CloseSessionAsync(session);
            }

            await _connectionRepository.DeleteProfileAsync(profileId);
            CancelDeleteConnection();
            await LoadConnectionsAsync();
            StatusMessage = "Подключение удалено.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Ошибка удаления: {exception.Message}";
        }
    }

    [RelayCommand]
    private void NewCustomCommand()
    {
        IsScriptsActive = true;
        OpenCustomCommandEditor(null);
    }

    [RelayCommand]
    private void EditCustomCommand(CustomCommandViewModel? customCommand)
    {
        if (customCommand is not null) OpenCustomCommandEditor(customCommand);
    }

    private void OpenCustomCommandEditor(CustomCommandViewModel? existing)
    {
        CloseCustomCommandEditor();
        var editor = new CustomCommandEditorViewModel(_quickCommandService, existing);
        editor.Saved += OnCustomCommandSaved;
        editor.Cancelled += OnCustomCommandCancelled;
        CustomCommandEditor = editor;
    }

    [RelayCommand]
    private async Task RunQuickCommand(QuickCommandViewModel? quickCommand)
    {
        if (quickCommand is null) return;
        SelectedQuickCommand = quickCommand;
        if (quickCommand.IsDangerous && !QuickCopyMode)
        {
            _pendingDangerousCommand = quickCommand;
            DangerousConfirmationText = $"«{quickCommand.Name}» может изменить или удалить данные.\n\n{quickCommand.Command}\n\nПроверьте команду и все placeholders перед отправкой в активный терминал.";
            IsDangerousConfirmationOpen = true;
            return;
        }

        await RunCommandSequenceAsync(quickCommand.Name, [quickCommand.Command]);
    }

    [RelayCommand]
    private void CancelDangerousCommand()
    {
        _pendingDangerousCommand = null;
        IsDangerousConfirmationOpen = false;
    }

    [RelayCommand]
    private async Task ConfirmDangerousCommand()
    {
        var command = _pendingDangerousCommand;
        CancelDangerousCommand();
        if (command is not null) await RunCommandSequenceAsync(command.Name, [command.Command]);
    }

    [RelayCommand]
    private async Task RunCustomCommand(CustomCommandViewModel? customCommand)
    {
        if (customCommand is null) return;
        foreach (var script in CustomCommands) script.IsSelected = ReferenceEquals(script, customCommand);
        await RunCommandSequenceAsync(
            customCommand.Name,
            customCommand.Sequence,
            CommandHistorySource.CustomCommand,
            respectQuickCopyMode: false);
    }

    [RelayCommand]
    private async Task CopyCustomCommand(CustomCommandViewModel? customCommand)
    {
        if (customCommand is null) return;
        try
        {
            await _clipboardService.SetTextAsync(customCommand.ScriptText);
            StatusMessage = $"Скрипт «{customCommand.Name}» скопирован в буфер обмена.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Не удалось скопировать скрипт: {exception.Message}";
        }
    }

    [RelayCommand]
    private async Task DeleteCustomCommand(CustomCommandViewModel? customCommand)
    {
        if (customCommand is null) return;
        try
        {
            await _quickCommandService.DeleteCustomCommandAsync(customCommand.Id);
            await LoadCustomCommandsAsync();
            StatusMessage = "Скрипт удалён.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Ошибка удаления скрипта: {exception.Message}";
        }
    }

    [RelayCommand]
    private void ToggleTheme() => _themeService.SetTheme(IsDarkTheme ? AppTheme.Light : AppTheme.Dark);

    [RelayCommand]
    private void ToggleLeftPanel() => IsLeftPanelVisible = !IsLeftPanelVisible;

    [RelayCommand]
    private void ToggleRightPanel() => IsRightPanelVisible = !IsRightPanelVisible;

    [RelayCommand]
    private void SetQuickMode(object? parameter) => QuickCopyMode = parameter switch
    {
        bool boolValue => boolValue,
        string text when bool.TryParse(text, out var parsedValue) => parsedValue,
        _ => false
    };

    [RelayCommand]
    private void ClearCommandSearch() => CommandSearchText = string.Empty;

    [RelayCommand]
    private void ClearServerSearch() => ServerSearchText = string.Empty;

    [RelayCommand]
    private void ClearScriptSearch() => ScriptSearchText = string.Empty;

    [RelayCommand]
    private void SetToolsSection(object? parameter) => IsScriptsActive = parameter switch
    {
        bool boolValue => boolValue,
        string text when bool.TryParse(text, out var parsedValue) => parsedValue,
        string text => text.Equals("Scripts", StringComparison.OrdinalIgnoreCase),
        _ => false
    };

    [RelayCommand]
    private void SetSettingsSection(object? parameter) =>
        IsInterfaceSettingsActive = string.Equals(parameter as string, "Interface", StringComparison.OrdinalIgnoreCase);

    [RelayCommand]
    private void SetLanguage(object? parameter)
    {
        var language = string.Equals(parameter as string, "English", StringComparison.OrdinalIgnoreCase)
            ? AppLanguage.English
            : AppLanguage.Russian;
        if (_localizationService is null)
        {
            AppText.SetLanguage(language);
            OnLanguageChanged(this, language);
        }
        else
        {
            _localizationService.SetLanguage(language);
        }
    }

    public async Task CloseAllAsync()
    {
        foreach (var session in Sessions.ToArray()) await session.DisposeAsync();
        Sessions.Clear();
        Tabs.Clear();
        RefreshTerminalSlots();
    }

    partial void OnCommandSearchTextChanged(string value)
    {
        for (var index = 0; index < BuiltInCategories.Count; index++)
            BuiltInCategories[index].ApplyFilter(value, index == 0);
        OnPropertyChanged(nameof(HasNoCommandResults));
    }

    partial void OnScriptSearchTextChanged(string value) => ApplyScriptFilter();

    partial void OnServerSearchTextChanged(string value) => ApplyConnectionFilter();

    partial void OnSelectedQuickCommandChanged(QuickCommandViewModel? oldValue, QuickCommandViewModel? newValue)
    {
        if (oldValue is not null) oldValue.IsSelected = false;
        if (newValue is not null) newValue.IsSelected = true;
    }

    partial void OnIsDarkThemeChanged(bool value) => OnPropertyChanged(nameof(ThemeButtonLabel));

    partial void OnSelectedTemplateChanged(CommandTemplateListItemViewModel? oldValue, CommandTemplateListItemViewModel? newValue)
    {
        if (oldValue is not null) oldValue.IsSelected = false;
        if (newValue is not null) newValue.IsSelected = true;
        TemplateName = newValue?.Name ?? string.Empty;
        TemplateTree.Clear();
        if (newValue is null) return;
        foreach (var category in _catalogCategories)
            TemplateTree.Add(new TemplateCategoryNodeViewModel(
                category,
                newValue.SelectedKeys,
                NotifyTemplateSelectionChanged,
                OnTemplateCategoryExpanded));
        NotifyTemplateSelectionChanged();
    }

    partial void OnActiveTemplateChanged(CommandTemplateListItemViewModel? oldValue, CommandTemplateListItemViewModel? newValue)
    {
        if (oldValue is not null) oldValue.IsActive = false;
        if (newValue is not null) newValue.IsActive = true;
        OnPropertyChanged(nameof(ActiveTemplateName));
        OnPropertyChanged(nameof(ActiveTemplateBadge));
        OnPropertyChanged(nameof(PanelTemplateLabel));
    }

    partial void OnActiveSessionChanged(TerminalViewModelBase? oldValue, TerminalViewModelBase? newValue)
    {
        if (oldValue is not null) oldValue.IsActive = false;
        if (newValue is not null) newValue.IsActive = true;
        RefreshTerminalSlots();
    }

    partial void OnActiveTabChanged(WorkspaceTabViewModel? oldValue, WorkspaceTabViewModel? newValue)
    {
        if (oldValue is not null) oldValue.IsActive = false;
        if (newValue is not null) newValue.IsActive = true;
        LayoutMode = newValue?.LayoutMode ?? 0;
        ActiveSession = newValue?.Terminal;
        RefreshTerminalSlots();
    }

    private async Task RunCommandSequenceAsync(
        string name,
        IReadOnlyList<string> commands,
        CommandHistorySource source = CommandHistorySource.QuickCommand,
        bool respectQuickCopyMode = true)
    {
        try
        {
            if (respectQuickCopyMode && QuickCopyMode)
            {
                await _clipboardService.SetTextAsync(string.Join(Environment.NewLine, commands));
                StatusMessage = $"«{name}» скопировано в буфер обмена.";
                return;
            }

            if (ActiveSession is null)
            {
                StatusMessage = "Откройте SSH или локальный терминал, чтобы выполнить команду.";
                return;
            }

            await ActiveSession.ExecuteCommandsAsync(commands, source);
            StatusMessage = ActiveSession.IsConnected
                ? $"Команда «{name}» отправлена в активный терминал."
                : ActiveSession.StatusMessage;
        }
        catch (Exception exception)
        {
            StatusMessage = $"Команда не выполнена: {exception.Message}";
        }
    }

    private void SetActiveSession(TerminalViewModelBase session)
    {
        if (ActiveTab?.Kind == WorkspaceTabKind.Terminal &&
            ActiveTab.GridSessions.Take(GridRows * GridColumns).Any(item => ReferenceEquals(item, session)))
        {
            ActiveSession = session;
            return;
        }

        var tab = Tabs.FirstOrDefault(item => item.Terminal == session);
        if (tab is not null) ActiveTab = tab;
    }

    private void AssignSessionToSlot(TerminalSlotViewModel slot, TerminalViewModelBase session)
    {
        if (ActiveTab?.Kind != WorkspaceTabKind.Terminal || LayoutMode == 0 || !Sessions.Contains(session)) return;
        if (ReferenceEquals(session, ActiveTab.Terminal) || ActiveTab.GridSessions.Contains(session)) return;
        ActiveTab.SetGridSession(slot.Index, session);
        ActiveSession = session;
        RefreshTerminalSlots();
        StatusMessage = $"Терминал «{session.Title}» добавлен в сетку вкладки «{ActiveTab.Title}».";
    }

    private async Task CloseSessionAsync(TerminalViewModelBase session)
    {
        var ownerTab = Tabs.FirstOrDefault(item => item.Terminal == session);
        var ownerIndex = ownerTab is null ? Sessions.IndexOf(session) : Tabs.IndexOf(ownerTab);
        var previousActiveTab = ActiveTab;
        var wasActiveSession = ActiveSession == session;
        Sessions.Remove(session);
        foreach (var tab in Tabs) tab.RemoveGridSession(session);
        if (ownerTab is not null) Tabs.Remove(ownerTab);
        await session.DisposeAsync();

        if (previousActiveTab is null || !Tabs.Contains(previousActiveTab))
            ActiveTab = Tabs.ElementAtOrDefault(Math.Min(ownerIndex, Tabs.Count - 1));
        else if (wasActiveSession)
            ActiveSession = previousActiveTab.Terminal;

        EnsureAnyTab();
        RefreshTerminalSlots();
    }

    private void EnsureAnyTab()
    {
        if (Tabs.Count == 0) OpenNewTab();
    }

    private WorkspaceTabViewModel CreateTab(
        string title,
        WorkspaceTabKind kind,
        TerminalViewModelBase? terminal = null) =>
        new(title, kind, tab => ActiveTab = tab, CloseTabAsync, terminal);

    private Task CloseTabAsync(WorkspaceTabViewModel tab) => CloseTabCommand.ExecuteAsync(tab);

    private void RefreshTerminalSlots()
    {
        var tab = ActiveTab;
        var cellCount = IsTerminalTabActive && tab is not null ? GridRows * GridColumns : 0;
        var assignedSessions = tab?.GridSessions.Where(session => session is not null).ToArray() ?? [];
        var availableSessions = tab is null
            ? []
            : Sessions.Where(session =>
                !ReferenceEquals(session, tab.Terminal) &&
                !assignedSessions.Contains(session)).ToArray();

        for (var index = 0; index < TerminalSlots.Count; index++)
        {
            var isVisible = index < cellCount;
            var terminal = isVisible && tab is not null
                ? LayoutMode == 0 ? tab.Terminal : tab.GetGridSession(index)
                : null;
            var slot = TerminalSlots[index];
            if (!ReferenceEquals(slot.Terminal, terminal)) slot.Terminal = terminal;
            slot.IsVisible = isVisible;
            slot.SetAvailableSessions(isVisible && terminal is null && LayoutMode > 0
                ? availableSessions
                : []);
        }
    }

    private void RebuildQuickCatalog()
    {
        var selectedKeys = ActiveTemplate?.SelectedKeys;
        BuiltInCategories.Clear();
        foreach (var category in _catalogCategories)
        {
            var commands = selectedKeys is null
                ? category.Commands
                : category.Commands.Where(command => selectedKeys.Contains($"{category.Name}::{command.Name}")).ToArray();
            if (commands.Count == 0) continue;
            BuiltInCategories.Add(new QuickCommandCategoryViewModel(
                new QuickCommandCategory(category.Name, category.Icon, commands),
                OnQuickCategoryExpanded));
        }
        if (BuiltInCategories.Count > 0) BuiltInCategories[0].IsExpanded = true;
        OnPropertyChanged(nameof(CatalogSummary));
        OnPropertyChanged(nameof(HasNoCommandResults));
    }

    private void OnQuickCategoryExpanded(QuickCommandCategoryViewModel expanded)
    {
        foreach (var category in BuiltInCategories)
        {
            if (!ReferenceEquals(category, expanded)) category.IsExpanded = false;
        }
    }

    private void OnTemplateCategoryExpanded(TemplateCategoryNodeViewModel expanded)
    {
        foreach (var category in TemplateTree)
        {
            if (!ReferenceEquals(category, expanded)) category.IsExpanded = false;
        }
    }

    private async Task LoadCommandTemplatesAsync(Guid? selectedId = null)
    {
        var templates = await _commandTemplateService.GetAllAsync();
        var activeId = ActiveTemplate?.Id ?? CommandTemplate.DefaultId;
        CommandTemplates.Clear();
        foreach (var template in templates) CommandTemplates.Add(new CommandTemplateListItemViewModel(template));
        var active = CommandTemplates.FirstOrDefault(template => template.Id == activeId) ?? CommandTemplates[0];
        ActiveTemplate = active;
        SelectedTemplate = CommandTemplates.FirstOrDefault(template => template.Id == selectedId) ?? active;
        RebuildQuickCatalog();
    }

    private void SetActiveTemplate(CommandTemplateListItemViewModel template)
    {
        ActiveTemplate = template;
        RebuildQuickCatalog();
    }

    private void NotifyTemplateSelectionChanged()
    {
        SelectedTemplateCommandCount = TemplateTree.Sum(category => category.SelectedCount);
    }

    private static CommandTemplateListItemViewModel CreateDefaultTemplate() => new(new CommandTemplate
    {
        Id = CommandTemplate.DefaultId,
        Name = "Default",
        IsDefault = true
    });

    private async Task RefreshHistoryAsync()
    {
        try
        {
            var entries = await _historyService.GetAllAsync();
            HistoryEntries.Clear();
            foreach (var entry in entries) HistoryEntries.Add(entry);
            NotifyHistoryChanged();
        }
        catch (Exception exception)
        {
            StatusMessage = $"Не удалось загрузить историю: {exception.Message}";
        }
    }

    private void NotifyHistoryChanged()
    {
        OnPropertyChanged(nameof(HasHistory));
        OnPropertyChanged(nameof(HistorySummary));
    }

    private async Task OpenConnectionEditorAsync(ConnectionProfile? profile)
    {
        var editor = new ConnectionEditorViewModel(_connectionRepository, _credentialProtector, Groups, profile);
        await editor.LoadSecretsAsync();
        editor.Saved += OnConnectionSaved;
        editor.Cancelled += OnConnectionCancelled;
        ConnectionEditor = editor;
    }

    private async void OnConnectionSaved(object? sender, ConnectionProfile profile)
    {
        try
        {
            CloseConnectionEditor();
            await LoadConnectionsAsync();
            StatusMessage = $"Подключение «{profile.Name}» сохранено.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Ошибка обновления списка: {exception.Message}";
        }
    }

    private void OnConnectionCancelled(object? sender, EventArgs args) => CloseConnectionEditor();

    private void CloseConnectionEditor()
    {
        if (ConnectionEditor is null) return;
        ConnectionEditor.Saved -= OnConnectionSaved;
        ConnectionEditor.Cancelled -= OnConnectionCancelled;
        ConnectionEditor = null;
    }

    private async void OnCustomCommandSaved(object? sender, CustomCommand command)
    {
        try
        {
            CloseCustomCommandEditor();
            await LoadCustomCommandsAsync();
            StatusMessage = $"Команда «{command.Name}» сохранена.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Ошибка обновления команд: {exception.Message}";
        }
    }

    private void OnCustomCommandCancelled(object? sender, EventArgs args) => CloseCustomCommandEditor();

    private void CloseCustomCommandEditor()
    {
        if (CustomCommandEditor is null) return;
        CustomCommandEditor.Saved -= OnCustomCommandSaved;
        CustomCommandEditor.Cancelled -= OnCustomCommandCancelled;
        CustomCommandEditor = null;
    }

    private async Task LoadConnectionsAsync()
    {
        var groups = await _connectionRepository.GetGroupsAsync();
        Groups.Clear();
        SavedConnections.Clear();
        foreach (var group in groups)
        {
            Groups.Add(group);
            foreach (var profile in group.Connections) SavedConnections.Add(profile);
        }
        ApplyConnectionFilter();
        OnPropertyChanged(nameof(HasConnections));
        OnPropertyChanged(nameof(ShowCreateConnection));
        OnPropertyChanged(nameof(HomeTitle));
        OnPropertyChanged(nameof(HomeDescription));
    }

    private void ApplyConnectionFilter()
    {
        var filter = ServerSearchText.Trim();
        VisibleGroups.Clear();
        foreach (var group in Groups)
        {
            var groupMatches = group.Name.Contains(filter, StringComparison.OrdinalIgnoreCase);
            var connections = string.IsNullOrWhiteSpace(filter) || groupMatches
                ? group.Connections
                : group.Connections.Where(profile =>
                    profile.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                    profile.Host.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                    profile.Username.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
            if (connections.Count == 0 && !string.IsNullOrWhiteSpace(filter)) continue;
            var isExpanded = !_groupExpansion.TryGetValue(group.Id, out var expanded) || expanded;
            VisibleGroups.Add(new ConnectionGroupViewModel(
                new ConnectionGroup
                {
                    Id = group.Id,
                    Name = group.Name,
                    CreatedAt = group.CreatedAt,
                    Connections = connections.ToList()
                },
                isExpanded,
                (id, value) => _groupExpansion[id] = value));
        }
    }

    private async Task LoadCustomCommandsAsync()
    {
        var selectedId = CustomCommands.FirstOrDefault(command => command.IsSelected)?.Id;
        var commands = await _quickCommandService.GetCustomCommandsAsync();
        CustomCommands.Clear();
        foreach (var command in commands)
        {
            var script = new CustomCommandViewModel(
                command,
                ReadSequence(command),
                RunCustomCommand,
                EditCustomCommand,
                CopyCustomCommand,
                DeleteCustomCommand)
            {
                IsSelected = command.Id == selectedId
            };
            CustomCommands.Add(script);
        }
        ApplyScriptFilter();
        OnPropertyChanged(nameof(HasCustomCommands));
    }

    private void ApplyScriptFilter()
    {
        var filter = ScriptSearchText.Trim();
        VisibleCustomCommands.Clear();
        foreach (var script in CustomCommands)
        {
            if (filter.Length == 0 ||
                script.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                script.Summary.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                VisibleCustomCommands.Add(script);
            }
        }
        OnPropertyChanged(nameof(HasVisibleCustomCommands));
        OnPropertyChanged(nameof(HasNoScriptResults));
    }

    private static IReadOnlyList<string> ReadSequence(CustomCommand command)
    {
        try { return JsonSerializer.Deserialize<string[]>(command.CommandsJson) ?? []; }
        catch (JsonException) { return []; }
    }

    private void OnLanguageChanged(object? sender, AppLanguage language)
    {
        AppText.ApplyResources();
        OnPropertyChanged(nameof(IsRussianLanguage));
        OnPropertyChanged(nameof(IsEnglishLanguage));
        OnPropertyChanged(nameof(QuickModeLabel));
        OnPropertyChanged(nameof(ThemeButtonLabel));
        OnPropertyChanged(nameof(LayoutLabel));
        OnPropertyChanged(nameof(HomeTitle));
        OnPropertyChanged(nameof(HomeDescription));
        OnPropertyChanged(nameof(HistorySummary));
        OnPropertyChanged(nameof(CatalogSummary));
        OnPropertyChanged(nameof(TemplateSelectionSummary));
        OnPropertyChanged(nameof(ActiveTemplateBadge));
        OnPropertyChanged(nameof(PanelTemplateLabel));
        OnPropertyChanged(nameof(LeftPanelButtonLabel));
        foreach (var category in BuiltInCategories) category.RefreshLocalization();
        foreach (var category in TemplateTree) category.RefreshLocalization();
        foreach (var template in CommandTemplates) template.RefreshLocalization();
        CustomCommandEditor?.RefreshLocalization();
    }

    private void OnThemeChanged(object? sender, AppTheme theme) => IsDarkTheme = theme == AppTheme.Dark;

    private static string ReadAppVersion()
    {
        var assembly = typeof(MainWindowViewModel).Assembly;
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plus = informational.IndexOf('+');
            return plus > 0 ? informational[..plus] : informational;
        }

        return assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }
}
