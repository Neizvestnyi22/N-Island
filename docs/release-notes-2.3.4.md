## N-Island 2.3.4 — player buttons work in the island, the bot and widgets

*made in TG [@Ne1zvestny1i](https://t.me/Ne1zvestny1i) & Claude*

Update from the app: **Settings → About → Update** · Обновление прямо из приложения: **Настройки → О программе → Обновить**.

- **Play/pause, next, previous:** Windows reports whether a command was carried out, and that answer was ignored. Many apps — browsers especially (YouTube, VK, Yandex Music) — don't support “toggle” but do support separate “play” and “pause”, so the button silently did nothing while its icon flipped. Now: toggle → separate command → the Windows media key (like ⏯ ⏭ ⏮ on a keyboard). This applies to the island, the bot, widgets and MCP.
- **Clicks in the expanded island:** when the island opened on hover, the “grow under the cursor” effect sometimes didn't shrink back — the island stayed 4.5 % larger and its bottom row (the player buttons) fell outside the area where Windows delivers clicks. Outside the collapsed view the island is now always at its natural size.
- Checked with a fake player: island buttons (via UI Automation), bot buttons, the player widget and the remote — every tap reaches the player.

Кнопки плеера снова работают везде: запасные команды для браузеров и приложений без «переключить», раскрытый остров больше не теряет клики по нижнему ряду.
