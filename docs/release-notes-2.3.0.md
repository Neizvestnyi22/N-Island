## N-Island 2.3.0 — 59 Telegram widgets, synced lyrics in the chat, visualizer, “Answer with widgets”

*made in TG [@Ne1zvestny1i](https://t.me/Ne1zvestny1i) & Claude*

Update from the app: **Settings → About → Update** · Обновление прямо из приложения: **Настройки → О программе → Обновить**.

### 🧩 14 new widgets (59 in total) · 14 новых виджетов
- **Touchpad** — move the cursor in 8 directions (3 step sizes), left/right/double click, scroll.
- **Type on the PC** — tap “✍️ Write text”, send the text in the chat — it's typed in the active window; Enter, ⌫, Tab, Esc, Ctrl+A/C/V/Z.
- **Launch apps** — your launcher favorites plus “✍️ another app…” by name.
- **Sound output** — switch the output device, volume 0/25/50/75/100, mute.
- **Screenshot** — whole screen, active window or the image from the PC clipboard arrives as a photo.
- **Hotspot** — turn mobile hotspot on and off, see connected devices.
- **Ping** — live latency to the router, Cloudflare and Google with average and losses.
- **Devices on the network**, **Crypto**, **Ask AI** (the answer appears in the widget and can be copied or put into the PC clipboard).
- **Visualizer** — a spectrum snapshot, level and tempo every 4 seconds; the phone animates bars, a ring, a wave or particles in the cover colour (tap to switch).
- **Games and time:** tic-tac-toe (vs the bot or two players), 2048, countdown (New Year, summer, weekend, midnight, ±days).

### 🎤 Synced lyrics in the chat · Синхронный текст песни
The player and lyrics widgets get a block of upcoming lines with their timing and switch lines themselves, exactly in time — the bot only updates them when the track changes, on pause, seek and every few lines.

### ✍️ Typing through the chat · Ввод через чат
Widgets have no text fields, so “✍️” buttons ask you to send a message: add a task, append to notes, put text into the PC clipboard, set a timer for any number of minutes, type on the PC, ask the AI. The widget updates itself afterwards.

### ⚙️ “Answer with widgets” · «Отвечать виджетами»
A new switch in the Telegram integration: `/music`, `/weather`, `/todos`, `/timer`, `/vpn` and 34 more commands without arguments answer with a widget instead of a message. If Telegram doesn't accept the widget, a normal message is sent.

### 📤 Forward · Переслать
A “📤 Forward” button under every widget sends a shareable copy (a snapshot without PC control) that you can forward to any chat. The bot's main menu now has a “🧩 Widgets” button.

### 🪶 Lighter on the PC · Меньше нагрузка
One state per widget kind every 1.5 s for all copies, at most 8 live widgets at once, slower refresh for heavy ones (processes, Wi-Fi, ping), the player's position and lyrics tick on the phone instead of being re-sent, and audio capture for the visualizer runs at a low rate only while it's live.

### ✅ Checked · Проверено
The self-test now presses through every widget: each of the 180 buttons must lead to a real action of its widget, requests stay short (long app names are sent as short fingerprints), “Answer with widgets” must really send widgets. In a browser all 59 widgets render without script errors and 252 button presses produce valid requests.
