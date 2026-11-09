## N-Island 2.4.4 — no spam, notifications as a widget, the mascot in the player, lyrics that don't freeze

*made in TG [@Ne1zvestny1i](https://t.me/Ne1zvestny1i) & Claude*

Update from the app: **Settings → About → Update** · Обновление прямо из приложения: **Настройки → О программе → Обновить**.

- **One widget of each kind:** a new widget replaces the previous one of the same kind — the old message is deleted (bot setting, on by default).
- **Notifications as one “🔔 PC events” widget (94 in total):** PC events, sign-in and lock, and the watchdogs (limits, IP change, downloads, processes, load, alarm, report) — newest on top, history, action buttons (snooze the alarm, send the downloaded file, jump to the right widget). Important ones arrive as a new widget with sound and the old one is removed; quiet ones update the same widget. Defender alerts still come as messages.
- **Fixed: the mini mascot never showed up in the player widget** — its drawing function was overwritten by a variable with the same name, and the animation used requestAnimationFrame, which the widget viewer doesn't run. Now it dances (checked in a browser), in the lyrics widget too.
- **Synced lyrics don't freeze:** the player and lyrics widgets stay live while music plays and come back on a track change or play; line times count from the PC's calculation time (corrected by Telegram's server clock), a progress bar for the current line and two next lines.

Без спама, уведомления одним виджетом «🔔 События ПК», персонаж танцует в плеере и в тексте песни, синхронный текст не замирает.
