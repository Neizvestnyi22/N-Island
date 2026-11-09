## N-Island 2.3.2 — a real “Forward”, the PC defender in Telegram, the “different thread” error fixed, the glint across the whole notch

*made in TG [@Ne1zvestny1i](https://t.me/Ne1zvestny1i) & Claude*

Update from the app: **Settings → About → Update** · Обновление прямо из приложения: **Настройки → О программе → Обновить**.

- **A real “📤 Forward”:** the button opens Telegram's chat picker and the bot posts the widget there through inline mode (a snapshot without PC control; the calculator, games, clock and calendar work for everyone). Only the owner gets inline results; forwarded cards have no control buttons. If the bot's inline mode is off, the button explains how to turn it on in @BotFather (/setinline). No more copies into the same chat.
- **More buttons in widgets (238 instead of 180):** player volume, mute, a 30-min sleep timer and jumps to lyrics, visualizer and equalizer; lyrics seek and like; mixer levels and output device; timer +1/+5/+15; display brightness levels and projection modes; power 15/30/60 min; network → Wi-Fi around, speed, ping; windows Alt+Tab; presentation first/last slide, space, white screen; more keys; quick scheduled commands.
- **The PC defender in Telegram:** threat alerts come with “🧊 Quarantine” and “✓ Trust”, so do scan results; the menu and the widget can toggle real-time protection, update the database, list and restore quarantine (double tap) and run a Windows Defender scan. Previously any unknown defender button started a scan.
- **Fixed “The calling thread cannot access this object because a different thread owns it”:** settings changed from the bot, MCP or the defender now notify on the UI thread; showing pop-ups, switching modes and expanding the island marshal to the UI thread themselves. The self-test now runs bot commands on a background thread like the real bot.
- **The glint in the collapsed notch** was clipped by a 32-px layout cell, so its bright centre vanished — now it lives on a canvas, is stretched along the notch, lights the full height and reaches every corner.
- **Widget and bot fixes:** long taps (screenshot, Wi-Fi, weather) no longer hit Telegram's 15-second limit; a 429 no longer stops a live widget; tapping an expired live widget restarts its updates; “Refresh live” touches only the latest 20; the sent-widgets list is capped; a reply-keyboard button pressed while the bot waits for text is treated as a button.

Настоящая «Переслать» через выбор чата, защитник в Telegram с карантином, исправлена ошибка «другого потока», блик доходит до каждого угла чёлки, больше кнопок в виджетах.
