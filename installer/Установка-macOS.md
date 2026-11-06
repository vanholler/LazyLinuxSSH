# Установка и сборка LazyLinuxSSH на macOS

## Что подготовлено

В каталоге `installer` проекта находятся:

- `build-macos.sh` — публикует self-contained сборки для **Apple Silicon** (`osx-arm64`) и **Intel** (`osx-x64`), собирает `.app` и упаковывает `.dmg`;
- `macos/Info.plist.template` — шаблон bundle (`com.lazylinuxssh.app`).

Подпись Apple Developer и нотаризация **не** выполняются. При первом запуске macOS может показать предупреждение Gatekeeper — откройте приложение через **ПКМ → Открыть**.

## 1. Установить инструменты

На MacBook нужны:

1. .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0
2. Стандартные утилиты macOS: `hdiutil`, `sips`, `iconutil` (идут с системой)

Отдельно устанавливать .NET Runtime на компьютерах пользователей не нужно: сборка self-contained.

## 2. Собрать DMG

Клонируйте или обновите репозиторий на MacBook, затем в корне проекта:

```bash
chmod +x installer/build-macos.sh
./installer/build-macos.sh 1.0.0
```

Предварительный `dotnet build` **не обязателен**: скрипт сам делает `restore`, `publish` и упаковку.

Результат:

```text
artifacts/installer/LazyLinuxSSH-1.0.0-osx-arm64.dmg   # Apple Silicon (M1/M2/M3/…)
artifacts/installer/LazyLinuxSSH-1.0.0-osx-x64.dmg     # Intel
```

Для следующей версии укажите новый номер, например `./installer/build-macos.sh 1.0.1`.

### Если появляется NU1301

Проверьте интернет, DNS, прокси, VPN и доступ к `https://api.nuget.org/v3/index.json`, затем повторите команду.

## 3. Установить приложение

1. Откройте нужный `.dmg` (arm64 или x64 под ваш Mac).
2. Перетащите `LazyLinuxSSH.app` в папку **Applications**.
3. При первом запуске: **ПКМ по приложению → Открыть → Открыть**.

## 4. Удалить приложение

- Удалите `LazyLinuxSSH.app` из Applications;
- при полном удалении данных удалите каталог `~/Library/Application Support/LazyLinuxSSH`.

Пользовательские профили, секреты и настройки при простом удалении `.app` сохраняются в Application Support.
