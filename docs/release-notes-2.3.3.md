## N-Island 2.3.3 — forwarding widgets without inline mode, proper taps and updates in widgets

*made in TG [@Ne1zvestny1i](https://t.me/Ne1zvestny1i) & Claude*

Update from the app: **Settings → About → Update** · Обновление прямо из приложения: **Настройки → О программе → Обновить**.

- **“📤 Forward” without inline mode:** the button under a widget opens Telegram's own recipient picker at the bottom — “👥 To a group”, “📢 To a channel”, “👤 To a person”. Telegram tells the bot which chat you picked, and the bot forwards the widget itself there. The bot must be in the group or channel (in a channel — an admin allowed to post); a person must have written to the bot once; otherwise the bot explains what to do. Taps in a forwarded copy never touch the PC.
- **Taps in widgets:** the button is highlighted while the request runs and extra taps are ignored; on “too fast” or “busy” the widget retries by itself; errors are shown in plain words in the phone's language and fade away. The bot runs taps of one widget in order, and an accidental double tap doesn't repeat the action (volume, mouse, keys and seeking still repeat).
- **Updates:** the new state after a tap is saved into the message itself, so the widget no longer jumps back to an old view when you scroll the chat or reopen it.
- The self-test covers forwarding, tap ordering and double taps, saved state and refusing other chats.

«Переслать» без инлайн-режима — через системный выбор чата; кнопки в виджетах откликаются, не дублируются и не откатываются.
