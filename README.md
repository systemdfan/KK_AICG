# kkaicg — Koikatsu AI Chat Gateway

`kkaicg` заменяет стандартные диалоги Koikatsu на свободный чат, управляемый большой
языковой моделью через SillyTavern. Пиши текст прямо в игре — персонаж отвечает,
меняет выражение лица, позу и параметры отношений.

## Состав

- `kkaicg.dll` — BepInEx-плагин для Koikatsu.
- `sillytavern-extension/` — Node.js WebSocket-сервер (мост).
- `sillytavern-ui-extension/` — браузерное расширение SillyTavern, внедряющее
  сообщения игрока в текущий чат и возвращающее ответ LLM.

## Установка

1. Собрать плагин:
   ```cmd
   dotnet build
   ```
   Готовый `kkaicg.dll` (и зависимости) автоматически копируются в
   `C:\KKIRV2 R1.7\KKIRV2\BepInEx\plugins\`.

2. Установить SillyTavern-расширение:
   - Скопируй (или сделай symlink) папку `sillytavern-extension` в
     `C:\Users\User\Downloads\SillyTavern-Launcher\SillyTavern\extensions\kkaicg-server`.
   - Запусти SillyTavern. Сервер поднимется на `ws://127.0.0.1:8085`.

3. Установить UI-расширение:
   - Скопируй папку `sillytavern-ui-extension` в
     `C:\Users\User\Downloads\SillyTavern-Launcher\SillyTavern\extensions\kkaicg`.

4. Открой чат с персонажем в SillyTavern и начни разговор в игре.

## Управление в игре

- **F9** — показать/скрыть окно чата.
- **F10** — выгрузить список доступных эмоций/поз в
  `BepInEx/config/kkaicg-lists.txt`.
- Команды в окне чата:
  - `/help` — справка.
  - `/dump` — то же, что F10.
  - `/close` — закрыть окно.

## Настройка алиасов эмоций и поз

При первом запуске плагин создаёт `BepInEx/config/kkaicg-mapping.json`. В нём
можно сопоставлять английские алиасы из промпта LLM с реальными игровыми именами.

## Протокол

См. [PROTOCOL.md](PROTOCOL.md).

## Промпт для карточки SillyTavern

См. [PROMPT-REFERENCE.md](PROMPT-REFERENCE.md).

## Лицензия

Лицензия проекта GPLv3 с примечанием атвора указана в файле `LICENSE`.