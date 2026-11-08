## N-Island 2.1.0 — 66 more commands, PC remote in Discord, Slack and Matrix, security review

*made in TG [@Ne1zvestny1i](https://t.me/Ne1zvestny1i) & Claude*

Update from the app: **Settings → About → Update** · Обновление прямо из приложения: **Настройки → О программе → Обновить**.

### 🤖 66 more Telegram commands · Ещё 66 команд
- **System:** `/cpu` (who loads the CPU), `/mem`, `/gpu`, `/idle`, `/active`, `/monitors`, `/ports`, `/conns`, `/usb` + `/eject E`, `/bin`, `/temp` + `/cleantemp`, `/boot`, `/pending` (does Windows need a restart), `/update`, `/service`, `/bathealth`, `/diskhealth`, `/flushdns`, `/netusage`, `/wifiscan`, `/lan`.
- **Sites & network:** `/http`, `/ssl`, `/whois`, `/dig`, `/ipinfo`, `/link` (phishing check), `/trace`, `/port`, `/unshort`.
- **Utilities:** `/conv 100 usd uah`, `/wiki`, `/clock Tokyo`, `/days 31.12`, `/moon`, `/sun`, `/tomorrow`, `/password`, `/uuid`, `/hash`, `/b64`, `/roll`, `/coin`, `/sw`, `/pomo`, `/sleeptimer`, `/water`.
- **To-dos:** `/notes`, `/todos`, `/done`, `/deltodo`, `/notif`, `/clipclear`.
- **Windows & sound:** `/close`, `/maximize`, `/minimize`, `/minall`, `/appvol spotify 40`, `/volup`, `/voldown`, `/like`, `/desklyrics`, `/viz`, `/mascot crown`, `/icons`, `/restartapp`.

### 💬 PC remote in Discord, Slack and Matrix · Пульт ПК в других мессенджерах
The same commands as the Telegram bot, run by the same engine, with each messenger's own permissions (dangerous ones are off by default and can only be changed on the PC). Only the owner's messages (their ID) are obeyed; screenshots and files arrive as attachments. Discord — a bot in a channel; Slack — a bot with `!command`; Matrix — a room without end-to-end encryption. Event webhooks work as before.
Те же команды, что у Telegram-бота, свои разрешения у каждого мессенджера, слушается только владелец.

### 🛡 Security review · Проверка безопасности
- Admin scripts (DNS, fixes, privacy, hosts) were written to a user folder and could be swapped while the UAC prompt was open. Now the job is verified by SHA-256 with admin rights and runs from a folder ordinary programs can't touch.
- MCP: the Origin check let `http://localhost.evil.com` through and Host wasn't checked (DNS rebinding). Fixed; the token is compared in constant time; `open` at the “Control” level opens only links.
- Links from service notifications, RSS, ntfy, free games and the web archive could launch programs or `file://` on click — now only http(s) and mailto.
- Telegram `/url httpd.exe` started a program with only the “Control” permission — now only website addresses.
- Clipboard history no longer stores passwords copied from password managers, and `/clip` won't send them.
- An imported settings file can no longer change the AI server address (where your key would go), turn on MCP or the `nisland://` protocol; the MCP token isn't exported.
- Telegram and Discord give control only to a single private owner; updates install only from GitHub with a SHA256SUMS check; files received from Telegram get the “from the internet” mark.
Скрипты администратора, MCP, ссылки из уведомлений, `/url`, пароли в буфере, импорт настроек, выбор владельца бота, обновление — всё закрыто.

### 🎨 Visual fixes · Визуальные исправления
Found with a batch of screenshots of all 31 settings sections and 24 island pages: values in −/+ steppers overlapped the buttons, the services counter was cut off, the RSS field floated in the middle, calendar weekdays were wrong in Ukrainian and English, several labels weren't translated, and 150+ wrong translations were fixed (“Питання” → “Живлення”, “Lurking” → “Floating”…).
Пачка скриншотов всех разделов: обрезки и наложения исправлены, больше 150 строк перевода поправлено.
