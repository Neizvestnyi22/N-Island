## N-Island 2.0.0 — Telegram 2.0, precise lyrics sync, a living character

*made in TG [@Ne1zvestny1i](https://t.me/Ne1zvestny1i) & Claude*

Update from the app: **Settings → About → Update** · Обновление прямо из приложения: **Настройки → О программе → Обновить**.

### 💾 Settings are saved again · Настройки снова сохраняются
The delayed-save timer was created on a background thread and never fired, so `settings.json` and `data.json` were written only on exit or update — a crash or power loss lost your changes, including Telegram. Now changes are written 0.7 s after you make them.
Таймер отложенной записи создавался в фоновом потоке и не срабатывал: настройки записывались только при выходе. Теперь — через 0,7 с после изменения.

### 🤖 Telegram 2.0 — the biggest integration update · Самое крупное обновление Telegram
- **New dashboard:** track with a progress bar, volume, battery, CPU, memory and weather as ▰▱ bars.
- **`/do` — smart command in plain words:** “volume 30 and turn off the screen”, “lock in 10 minutes”. The AI turns the phrase into up to 5 bot commands from an allow-list; dangerous ones are never run.
- **`/live`** — a panel that updates itself every 4 s; **`/watch`** — a live screen: the screenshot in the chat refreshes every 3 s.
- **`/now`** — now playing with the cover and a remote; shuffle, repeat, `/seek 1:30`.
- **Scheduled commands:** `/in 10m /lock`, `/at 18:30 /shutdown` (up to 20, `/jobs` to list and cancel).
- **`/scene`** — work, movie, games, saver, presentation in one tap.
- Voice messages to the bot play on the PC; `/tr` translate, `/calc`, `/qr`, `/speed` internet speed, `/health` PC health, `/av` Defender (quick scan, threats), `/radio` Wi-Fi and Bluetooth, `/wake` Wake-on-LAN, `/startup`, `/cliphist`, `/history`, `/pin` the remote in the chat.
- A cleaner menu: “🎬 Scenes”, “📡 Live panel”, “✨ More”. The bot is checked by a test bench that presses every button and watches Telegram's limits.
- MCP: new `integrations_status` tool (which integrations are on and working, no secrets).

Умная команда `/do` обычными словами, живая панель и живой экран, трек с обложкой, команды по расписанию, сценарии, голосовые на ПК, перевод, калькулятор, QR, скорость сети, здоровье ПК, Защитник, Wi-Fi и Bluetooth, Wake-on-LAN, автозагрузка, история буфера и музыки.

### 🎤 Lyrics: precise sync, one style everywhere · Точная синхронизация, единый стиль
- Bluetooth headphones and speakers play sound 0.15–0.25 s after the player reports it. The new **Device audio delay** setting compensates for that (“auto” adds 0.2 s over Bluetooth).
- The player position is re-checked regularly: small jitter is ignored, real jumps are taken at once. Word highlighting updates more often.
- Font, color, uppercase, shadow and line animation now look the same in the player, the collapsed island, the visualizer and on the desktop, and change instantly.

Учитывается задержка Bluetooth, позиция сверяется чаще, стиль текста одинаковый везде и применяется сразу.

### 🫧 A living character · Персонаж ожил
- **24 moods** (was 9): winks, waves, yawns, cries, freezes, feels hot, celebrates, spins, gets dizzy, gets angry, gets curious, charges, looks cool in shades…
- **Accessories:** cap, crown, bow, party hat, headphones, glasses, Santa hat, flower; “auto” picks headphones for music, a party hat on holidays, a Santa hat in winter.
- **Reacts to almost everything:** notifications, calls, timers, agent tasks, copying, USB drives, a new track, pause and resume, mute, switching headphones, losing and regaining the internet, unlocking and waking from sleep, low battery and full charge, low memory, heat and frost outside, the top of the hour and Friday evening, Telegram commands, a caught virus, love lines in a song, a heavy beat; when you write code or a document it focuses with you.
- **Click it** for a reaction — poke it too often and it gets dizzy.
- **Settings:** accessory, size, animation speed, lives on its own, dances to music, reacts to events, reacts to the computer, sleeps at night, poke the character.

24 настроения, 9 аксессуаров, реакции почти на всё, клик по персонажу и девять новых настроек.
