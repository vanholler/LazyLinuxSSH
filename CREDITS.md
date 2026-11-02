# Источники

## Linux Cheat Sheet

Встроенный каталог `src/LazyLinuxSSH.Infrastructure/Commands/linux-commands.json` подготовлен на основе русскоязычной шпаргалки **Linux Cheat Sheet** автора **justxor**:

- репозиторий: https://github.com/justxor/Linux-/
- источник: `README.md` репозитория;
- лицензия исходного материала: **MIT**, как указано автором в репозитории;
- дата изучения источника: 2 ноября 2026 г.

Команды сгруппированы для desktop-сценариев LazyLinuxSSH, снабжены краткими русскими названиями и описаниями. Часть длинных учебных скриптов адаптирована в отдельные однострочные команды. Теория, вопросы собеседований, горячие клавиши и справочные таблицы, которые нельзя осмысленно запустить как SSH-команду, в исполняемый каталог не включались.

Потенциально разрушительные команды сохранены для справки и помечены `isDangerous: true`. Перед отправкой такой команды в терминал приложение запрашивает дополнительное подтверждение. Примеры содержат placeholders вроде `PID`, `URL`, `host` и `file`: замените их на реальные значения до запуска.

## Текст лицензии MIT исходной шпаргалки

Copyright (c) justxor/Linux- contributors

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
