## N-Island 2.2.0 — 45 Telegram widgets, rich messages, 48 PC commands, 17 new visualizers, a livelier character

*made in TG [@Ne1zvestny1i](https://t.me/Ne1zvestny1i) & Claude*

Update from the app: **Settings → About → Update** · Обновление прямо из приложения: **Настройки → О программе → Обновить**.

### 🧩 45 Telegram widgets · 45 виджетов в чате (Bot API 10.4)
Interactive mini-apps right inside a message: tap — it changes on the PC; live ones refresh themselves. `/widgets` opens the catalog, `/widget player` sends one, the bot can refresh or stop all live widgets.
- **Main:** PC remote, the island character.
- **Music:** player (seek step, A–B loop), lyrics, app volume, equalizer, recently played.
- **System:** load chart, battery, network, disks, what's loading the PC, PC health, protection.
- **Network:** VPN, Wi-Fi around, internet speed.
- **Apps:** startup items, open windows, app updates (winget).
- **Control:** island modes, scenes, display, power, keys on the PC, presentation remote.
- **Time:** timer, stopwatch, Pomodoro, world clock, scheduled commands, calendar.
- **To-do and briefs:** tasks, notes, clipboard, water, AI on the clipboard (summary, translate, fix, explain), notifications, AI agents, weather, exchange rates, sun and moon.
- **Fun:** calculator, dice and coin, breathing exercise.
Long jobs (speed test, winget, scans, AI) answer at once with “⏳” and the result arrives by itself. Every widget fits Telegram's limits (HTML ≤ 4 KB compressed, state ≤ 1 KB) — checked by the self-test.
**The character in the widget is drawn exactly like on the island:** the same skin, colour, accessory, faces and effects; poke it — the island reacts too.
Інтерактивні віджети в повідомленні: натиснув — змінилося на ПК, живі оновлюються самі. Персонаж у віджеті такий самий, як на острові.

### 💬 Rich messages · Новое оформление сообщений
Bot API 10 rich messages: headings, real tables, collapsible blocks, code with highlighting; AI answers stream as a formatted draft and arrive with full Markdown (tables, formulas). Buttons are coloured (danger / success / primary). If Telegram doesn't accept rich messages, plain HTML is used automatically; the toggle is in the Telegram integration.

### 🖥 48 more commands — the whole PC and its apps · Ещё 48 команд
- **Apps:** `/installed`, `/appinfo`, `/launch`, `/quit`, `/relaunch`, `/focus`, `/hang` (frozen apps), `/freeze` / `/unfreeze`, `/priority`, `/appmute`, `/appcpu`, `/autostart`, `/uninstall` (opens the app's own uninstaller after confirmation), `/upgrades`, `/upgrade`, `/winstall`, `/wsearch`.
- **Windows:** `/winset` (Settings pages), `/project`, `/layout`, `/caps`, `/saver`, `/emptybin` (with confirmation), `/recent`, `/printers`, `/printq`, `/events`, `/crashes`, `/bsod`, `/hotfix`, `/firewall`, `/wdstatus`, `/users`, `/shares`, `/drivers`, `/usbdev`, `/audiodev`, `/outdev`, `/batreport`, `/clipimg`, `/sysreport`.
- **Wi-Fi:** `/wifihealth`, `/wifiq`, `/channels`, `/reconnect`, `/forget`, `/router`.

### 🎆 17 new visualizers · 17 новых визуализаторов
**3D:** cube in a cube, sound torus, warp jump, gyroscope, particle tunnel, 3D bars, icosahedron, sound surface. **2D:** mandala, fireworks, piano roll, sound rain, radar, polygons, bouncing balls, circular oscilloscope, constellation. VU meters, ridges, orbits and piano roll react much stronger to the music now.

### 🫧 A livelier character · Персонаж живее
- **12 new moods:** laughs, scared stiff, bored, proud, shy, sick, puzzled, delighted, blows a kiss, headbangs, amazed, sneezes.
- **Play with it:** double-click — delight or headbang, mouse wheel over it — tickle (giggles, then laughs, then gets shy), move the cursor back and forth — pet it. A poke wakes it up gently if it's asleep.
- **New reactions:** AI error — puzzled, three skipped tracks in a row — puzzled, volume to max — headbangs or amazed, battery at 5 % — scared, low memory or a full system drive — sick, nothing happening for a while — bored, back after a long lock — blows a kiss, AI answered — sometimes proud, lots of copying — delighted.

### 🏝 Island, player, Wi-Fi · Остров, плеер, Wi-Fi
- The cursor glint now follows the mouse across **the whole panel** (it used to cover only half); its size, brightness and colour (white, accent, cover) are adjustable.
- The collapsed island grows slightly under the cursor; an optional soft shadow.
- Player: adjustable seek step (also in the bot, widget and MCP), **A–B fragment loop**, how many times you've played the track.
- Wi-Fi: a warning when the signal gets weak.
- **12 new settings** in total.

### 🐞 Fixes · Исправления
- Bot: table conversion dropped all text in some cases; long messages could be cut in the middle of an emoji; app buttons with “|” in the data opened the menu instead.
- AI: hidden reasoning (`<think>`) no longer flashes in streamed answers in the island and Telegram; “Translate” uses the interface language.
- `/hang` no longer lists suspended Store apps like Settings.
