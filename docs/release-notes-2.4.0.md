## N-Island 2.4.0 — PC tracking and control, 32 new widgets, live sync with the PC, a louder equalizer

*made in TG [@Ne1zvestny1i](https://t.me/Ne1zvestny1i) & Claude*

Update from the app: **Settings → About → Update** · Обновление прямо из приложения: **Настройки → О программе → Обновить**.

- **Tracking** (new Control Center section “Tracking and control”): screen time per app and the week at the PC, sessions (boot, lock, sleep, wake), charge history, traffic per day and 14 days, Wi-Fi signal history, USB drive log, public IP and its changes, new files in Downloads, who sends notifications, internet speed history, temperature (ACPI sensors), who uses the memory, today's event log and a daily report. Everything stays on the PC (14 days).
- **Control:** per-app time limits (warn or minimize), focus sessions that minimize distracting windows, app start/stop watch, alarms with “Snooze 10 min”, microphone volume, wallpaper rotation from a folder, “stay awake” for 1–4 hours, CPU and memory load watchdog, quick fixes (DNS, Wi-Fi, memory, restart File Explorer) and a daily report to Telegram at a set time.
- **Telegram bot:** 32 new commands, each with a “🧩 Widget” button; **32 new widgets (91 in total)** — 15 for tracking, 9 for control and 8 for features that had no widget yet (Wi-Fi and Bluetooth, who is listening and watching, prayer times, upcoming events, shelf, translator, cleanup, news).
- **“⟳ Refresh” in every widget**, and **live sync with the PC:** pause, track, volume, equalizer, mode and settings changed on the PC itself update the latest widgets and remote-control messages right away.
- **Player widget:** a mini mascot dances while music plays (gone on pause), plus the battery — time left, or time to full while charging.
- **The equalizer is built in:** the official Equalizer APO 1.4.2 installer (GPL-2.0, unmodified, SHA-256 checked) is embedded in N-Island (x64 builds, +12 MB) — nothing to download: “Install” → one Windows confirmation (silent, no wizard) → pick your speakers and headphones → restart. **Engine updates** right from the app: Settings → Equalizer → “Engine version” checks the official project page and updates with a SHA-256 check; it also checks weekly by itself. Fixed: a leftover config folder after uninstalling Equalizer APO made N-Island think it was still installed.
- **Equalizer no longer makes sound quieter:** the preamp used to subtract the whole biggest boost; now it keeps the loudness (half the headroom) and adds a −6…+12 dB volume boost for quiet laptop speakers. Equalizer APO installs silently and turns on right away.
- **Fixed:** the −10 / +10 and A–B buttons in the island player showed squares.

Отслеживание и контроль ПК, 32 новых виджета (всего 91), «⟳» в каждом виджете, мгновенная синхронизация с ПК, танцующий персонаж и батарея в плеере, эквалайзер без потери громкости.
