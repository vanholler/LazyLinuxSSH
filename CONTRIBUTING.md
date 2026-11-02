# Участие в LazyLinuxSSH

**[English version](#contributing-to-lazylinuxssh)**

Спасибо за интерес к проекту! LazyLinuxSSH ещё находится в разработке, поэтому предложения, сообщения об ошибках, улучшения интерфейса, переводы и код особенно полезны.

## Как помочь

- **Предложить идею:** создайте Issue и опишите задачу, которую должна решить новая возможность.
- **Сообщить об ошибке:** укажите версию приложения, Windows, шаги воспроизведения, ожидаемый и фактический результат. Приложите скриншот или видео, если это проблема интерфейса.
- **Улучшить каталог:** предложите полезную команду, описание, категорию или более безопасный пример.
- **Помочь с переводом:** исправьте формулировки RU/EN или предложите новую локализацию.
- **Внести код:** начните с небольшого изменения или обсудите крупную доработку в Issue до начала работы.

Перед созданием Issue проверьте, нет ли уже похожего обращения.

## Подготовка окружения

Требуется [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0). Текущая поддерживаемая платформа приложения — Windows x64.

```bash
git clone <URL-вашего-fork>
cd LazyLinuxSSH
dotnet restore LazyLinuxSSH.sln
dotnet run --project src/LazyLinuxSSH.Desktop/LazyLinuxSSH.Desktop.csproj
```

Перед отправкой изменений выполните:

```bash
dotnet build LazyLinuxSSH.sln -c Release
dotnet test LazyLinuxSSH.sln -c Release
```

## Процесс Pull Request

1. Сделайте fork репозитория.
2. Создайте отдельную ветку, например `feature/script-import` или `fix/terminal-focus`.
3. Внесите небольшое, логически законченное изменение.
4. Добавьте или обновите тесты для изменённого поведения.
5. Обновите `README.md` и `README.en.md`, если изменение видно пользователю.
6. Для UI-изменений приложите скриншоты «до/после» и проверьте тёмную и светлую темы.
7. Убедитесь, что сборка проходит без новых ошибок и предупреждений, а тесты успешны.
8. В Pull Request опишите мотивацию, реализацию и способ проверки.

Один Pull Request должен решать одну задачу. Не включайте в него несвязанные рефакторинг и форматирование всего проекта.

## Технические принципы

- .NET 8, C# 12, Avalonia UI, MVVM, DI и Clean Architecture.
- Сохраняйте границы `Core`, `Infrastructure`, `Desktop` и `Tests`.
- SSH, файловые операции и работа с БД должны оставаться асинхронными; не используйте блокирующие `.Wait()` и `.Result` в UI.
- Ошибки SSH должны обрабатываться без падения приложения и с понятным состоянием reconnect.
- Для новых XAML-подписей используйте локализуемые ресурсы и добавляйте русский и английский варианты.
- Не переводите shell-команды, стабильные ключи команд или идентификаторы сущностей.
- Сохраняйте существующие профили, историю, шаблоны и скрипты при изменении SQLite-схемы.

## Безопасность — обязательно

- Никогда не добавляйте в коммит реальные пароли, passphrase, SSH-ключи, master key, токены или пользовательские базы.
- Секреты должны шифроваться перед сохранением: DPAPI на Windows или AES-GCM на других ОС.
- Кнопка показа пароля может менять только временную маску ввода, но не способ хранения.
- Не записывайте секреты и ввод на password/passphrase-запросах в историю.
- Обычное копирование команды не должно считаться её выполнением.
- Потенциально опасные команды должны сохранять подтверждение перед отправкой в SSH.
- Не добавляйте демонстрационные профили или скрипты в пользовательские данные при первом запуске.

Если случайно опубликован секрет, немедленно отзовите/замените его: удаление из следующего коммита не делает старый коммит безопасным.

## Стиль изменений

- Пишите понятные имена и небольшие методы, следуйте стилю окружающего кода.
- Не добавляйте зависимости без необходимости; при добавлении укажите причину в PR.
- Сообщения коммитов лучше писать коротко и по делу, например: `Fix terminal focus after reconnect`.
- Скриншоты для документации размещайте в `docs/screenshots`; используйте только демонстрационные данные без реальных адресов и секретов.

Спасибо за каждый Issue, Pull Request и полезное предложение!

---

# Contributing to LazyLinuxSSH

Thank you for your interest in the project! LazyLinuxSSH is still under development, so ideas, bug reports, UI improvements, translations, and code contributions are very welcome.

## Ways to help

- **Suggest an idea:** open an Issue and describe the user problem the feature should solve.
- **Report a bug:** include the app version, Windows version, reproduction steps, expected and actual behavior. Add a screenshot or recording for UI issues.
- **Improve the command catalog:** suggest a useful command, description, category, or safer example.
- **Improve translations:** refine RU/EN wording or propose another localization.
- **Contribute code:** start with a focused change or discuss a larger feature in an Issue first.

Please check for an existing Issue before creating a new one.

## Development setup

Install [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0). The currently supported application platform is Windows x64.

```bash
git clone <your-fork-url>
cd LazyLinuxSSH
dotnet restore LazyLinuxSSH.sln
dotnet run --project src/LazyLinuxSSH.Desktop/LazyLinuxSSH.Desktop.csproj
```

Before submitting changes, run:

```bash
dotnet build LazyLinuxSSH.sln -c Release
dotnet test LazyLinuxSSH.sln -c Release
```

## Pull Request workflow

1. Fork the repository.
2. Create a focused branch such as `feature/script-import` or `fix/terminal-focus`.
3. Keep the change small and logically complete.
4. Add or update tests for changed behavior.
5. Update both `README.md` and `README.en.md` for user-facing changes.
6. Attach before/after screenshots for UI changes and check both dark and light themes.
7. Confirm that the build introduces no new errors or warnings and all tests pass.
8. Explain the motivation, implementation, and verification in the Pull Request.

Keep each Pull Request focused on one task. Avoid unrelated refactoring and repository-wide formatting.

## Technical guidelines

- Use .NET 8, C# 12, Avalonia UI, MVVM, DI, and Clean Architecture.
- Preserve the boundaries between `Core`, `Infrastructure`, `Desktop`, and `Tests`.
- Keep SSH, file, and database I/O asynchronous. Do not block the UI with `.Wait()` or `.Result`.
- Handle SSH errors without crashing and provide a clear reconnect state.
- Use localizable resources for new XAML labels and provide both Russian and English text.
- Do not translate shell commands, stable command keys, or entity identifiers.
- Preserve existing profiles, history, templates, and scripts when changing the SQLite schema.

## Security requirements

- Never commit real passwords, passphrases, SSH keys, master keys, tokens, or user databases.
- Encrypt secrets before saving: DPAPI on Windows or AES-GCM on other operating systems.
- Password reveal controls must only change temporary input masking, never secret storage.
- Do not store secrets or password/passphrase prompt input in command history.
- Copying a command must not be recorded as execution.
- Keep confirmation before sending potentially dangerous commands over SSH.
- Do not seed demonstration profiles or scripts into a user's data on first launch.

If a secret is published accidentally, revoke or rotate it immediately. Removing it in a later commit does not make the earlier commit safe.

## Change style

- Use clear names and small methods, following the surrounding code style.
- Avoid unnecessary dependencies; explain any new dependency in the PR.
- Use concise commit messages such as `Fix terminal focus after reconnect`.
- Store documentation screenshots in `docs/screenshots` and use only demo data without real addresses or secrets.

Thank you for every Issue, Pull Request, and helpful suggestion!
