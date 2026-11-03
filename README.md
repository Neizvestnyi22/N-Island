<div align="center">

<img src="docs/logo.png" width="96" alt="N-Island"/>

# N-Island

**Dynamic Island for Windows — plus a beginner-friendly Laptop Control Center**

**English** · [Русский](docs/readme/README.ru.md) · [Українська](docs/readme/README.uk.md)

[![Release](https://img.shields.io/github/v/release/Neizvestnyi22/N-Island?label=download&color=0A84FF)](https://github.com/Neizvestnyi22/N-Island/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/Neizvestnyi22/N-Island/total?color=30D158)](https://github.com/Neizvestnyi22/N-Island/releases)
![Windows 10/11](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4)
![.NET 8](https://img.shields.io/badge/.NET-8-512BD4)
[![License: MIT](https://img.shields.io/badge/license-MIT-lightgrey)](LICENSE)

*made in TG [@Ne1zvestny1i](https://t.me/Ne1zvestny1i) & Claude*

</div>

---

An iPhone-style notch at the top of your screen that shows **music from any app with synced lyrics, calls, notifications, timers and an AI chat** — and a full **Laptop Control Center**: deep cleanup with strict safety checks, anti-tracking, fine-grained Windows optimization, an activity journal (which apps and sites used your camera, mic, location…), power, network, apps and one-click fixes.
Every option has a safety label, a plain-language explanation and an undo button. Works even on weak laptops (Lite mode) and uses ~15–40 MB of RAM.

<p align="center">
  <img src="docs/screenshots/island.png" width="720" alt="Island with music and lyrics"/>
</p>

## ⬇️ Download

| File | For whom |
|---|---|
| [**NIsland-win-x64.zip**](https://github.com/Neizvestnyi22/N-Island/releases/latest) | **Recommended.** 64-bit Windows 10/11. Everything included, nothing to install. |
| [NIsland-win-x86.zip](https://github.com/Neizvestnyi22/N-Island/releases/latest) | Old 32-bit Windows 10 laptops and netbooks. |
| [NIsland-win-x64-lite.exe](https://github.com/Neizvestnyi22/N-Island/releases/latest) | Small download. Needs the free [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (Windows offers to install it on first launch). |

1. Download, unzip anywhere, run `NIsland.exe` — no installer, no admin rights needed.
2. If SmartScreen says *“Windows protected your PC”* → **More info → Run anyway** (the app isn't code-signed; the source is right here).
3. Hover the notch to expand it, right-click for settings, **Ctrl+Alt+L** for the Laptop Control Center.

Checksums are in `SHA256SUMS.txt` in every release.

## ✨ Features

### The island
- **Music from any app or website** — Spotify, YouTube / YouTube Music, Yandex Music, VK, Apple Music, AIMP, browsers, Telegram… (Windows media controls).
  Cover, progress in the notch, shuffle / repeat / ±10 s / speed, history and favorites.
  Works even with apps that don't report track time (Firefox, some players): N-Island counts the time itself and takes the length from a catalog (marked “≈”).
- **Synced lyrics with karaoke** — LRCLIB (exact match by length) with NetEase and Kugou as fallbacks, **word-by-word highlighting**, smart title cleanup for YouTube/Spotify/VK titles, per-song timing offset that is remembered, line translation, focus mode, offline cache.
- **VPN in the island** — Cloudflare WARP, NordVPN, Mullvad, Windscribe, Tailscale, PIA, OpenVPN, WireGuard and Windows VPN connections switch on and off right in the island; any other VPN shows its status. External IP and country, a pop-up when a VPN drops.
- **System-wide equalizer** — a draggable 10-band curve over a live spectrum, 17 presets and a separate profile for every output device (headphones, speakers, Bluetooth), powered by the free Equalizer APO engine (one-click install).
- **Music visualizers** — 13 styles, from old-player classics (bars and wave, oscilloscope, LED meter, ambience, alchemy) to new ones (radial spectrum, tunnel, particles, spectrogram, neon waves, liquid drop); in the player, as live bars in the notch and in a full-screen window with lyrics on top. They react to the real sound (WASAPI loopback, beat and BPM detection).
- **36 integrations** — Telegram bot with deep PC control (button remote, status, top processes, apps, files, screenshots, lock/sleep/restart, console — dangerous parts are off by default), ntfy, Pushover, Pushbullet, Gotify, Discord, Slack, Matrix, webhooks, Home Assistant, Philips Hue, Todoist, Notion, Trello, Linear, Jira, GitHub, GitLab, Toggl Track, WakaTime, email over IMAP (Gmail, Outlook, ukr.net), calendars, RSS, Hacker News, Mastodon, Bluesky, Twitch, Steam, stocks, exchange rates and crypto, Monobank, Nova Poshta, website monitoring, air raid alerts (siren.pp.ua without a key or alerts.in.ua).
- **Wi-Fi and network tools** — live signal graph (find the best spot), nearby networks, devices on your network, a full network check with advice (5 GHz, channel load, WPS, router vs adapter standard, latency and loss, DNS speed and hijacking, captive portal, IPv6, MTU, lag under load / bufferbloat, clock drift, driver age), free channels for 2.4/5 GHz, speed test. Works over Ethernet too.
- **OSINT and checks** — phishing link check, WHOIS/RDAP, DNS, certificates, subdomains, Wayback, site technologies and robots.txt, IP info, open ports of your own devices, MAC vendor, password leak check via HIBP k-anonymity, photo metadata/GPS with a clean copy, file check (real type, double extension, Authenticode signature, where it was downloaded from, VirusTotal), email header analysis (SPF/DKIM/DMARC, route, spoofed reply-to), email address check, invisible characters and look-alike letters in text.
- **Deep Windows integration** — Explorer context menu (check a file, strip photo metadata), `nisland://` links for scripts and Stream Deck, Windows “Do not disturb” and accent color follow-up.
- **No duplicates** — turn off what the island already shows: Windows notification banners (they stay in the notification center), the AutoPlay window, the volume OSD. Everything comes back when N-Island closes.
- **Calls** from apps *and* websites (Telegram, WhatsApp, Discord, Teams, Zoom, Meet in the browser…) with a timer and a “back to call” button.
- **Notifications** from all apps: 2FA codes with a copy button, important keywords that break through Do Not Disturb, block list, privacy mode.
- **AI chat** right in the notch: free provider without a key, Claude, OpenAI, Gemini, Groq, DeepSeek, Mistral, OpenRouter, Ollama, LM Studio and your own providers. Keys are encrypted with Windows DPAPI.
- Timer, stopwatch, Pomodoro, to-dos, calendar with reminders, weather, world clocks, translator, clipboard history, notes, file shelf, launcher, prayer times, water tracker, sound mixer per app, 30+ API tools (speed test, IP, DNS, QR, currency, units…).
- **15 styles** (liquid glass, frosted, iPhone, neon, **AMOLED** pure black…), 7 modes (Do Not Disturb, Focus, Game, Presentation…), deep customization and transparency.
- **Proper sounds** — chimes, marimba, bell, alarm; synthesized on the fly, per event, with volume.

### Quick search (Ctrl+Alt+Space)
A Spotlight/Raycast-style window: programs and Store apps (by first letters or even in the wrong keyboard layout), Windows settings, calculator, currency and unit conversion, translation, island commands (“timer 25”, “note …”, “task …”), recent files, clipboard, web search — and an **AI answer right in the window** (Tab).

### Laptop Control Center (Ctrl+Alt+L)
<p align="center">
  <img src="docs/screenshots/center-overview.png" width="49%" alt="Overview"/>
  <img src="docs/screenshots/center-optimize.png" width="49%" alt="Optimization"/>
</p>

| Section | What it does |
|---|---|
| **Overview** | Laptop health score 0–100 with tips, live CPU/RAM/disk/battery, one-click optimize, restore point, scenarios (Work, Movie, Games, Battery saver, Presentation). |
| **Deep cleanup** | Temp files, browser caches (cache only — never passwords, history or cookies), Discord/Telegram/Spotify/Teams/VS Code/Steam caches, crash dumps, update leftovers, activity traces. A “Don't touch” list explains myths (Prefetch, WinSxS, Installer…). |
| **Optimization** | 20 real Windows settings (light UI, Game Bar recording, mouse acceleration, network throttling, GPU scheduling, background apps, widgets, Edge/Chrome in background, SysMain, search indexing, hibernation, Fast Startup, Delivery Optimization, AutoPlay…), ready profiles (Weak laptop, Games, Battery) and **per-app tuning**: GPU preference, permanent CPU priority, compatibility flags. |
| **Anti-tracking** | 25 items: telemetry, advertising ID, activity history, Copilot, Recall, Bing in Start, DiagTrack, data-collection tasks, Edge data… plus a tracker-blocking hosts list. |
| **Activity journal** | Who used the camera, microphone and location — when and for how long; what websites are allowed in your browsers; which programs ran and when; who sent notifications; what was installed; which USB drives were connected. |
| **Speed, Power, Files, Internet, Programs, Fix** | Power mode, battery health and 80 % charge alert, sleep timer, duplicate/large/old files, DNS switcher (Cloudflare, AdGuard…), “who is online now”, startup advisor, uninstaller, 14 official fixes (SFC, DISM, chkdsk, audio, network, printer, time, Store, icons…). |

Next to every option there is a **✨ button** — the AI explains in plain words what it does, who should enable it, the risks and how to undo it.

### Safety first
- Only **documented Windows settings and policies** — no patched system files, no “magic registry packs”.
- Before every change the original value is saved (`%APPDATA%\NIsland\backup`) — **flip the switch back and it's restored**.
- Admin rights are requested **only for a specific action** through the standard Windows prompt; the exact script is kept in `%APPDATA%\NIsland\admin` so you can read it.
- Cleanup deletes only inside a whitelist of cache folders, never follows links/junctions, skips system, locked and fresh files, and cleans an app's cache only when the app is closed. Your files always go to the Recycle Bin.
- System files are repaired only by Windows' own tools (SFC, DISM).

### For weak PCs
**Lite mode** switches off glass blur, the equalizer and shadows and is enabled automatically on laptops with ≤4 GB RAM, 2 threads or no GPU acceleration. Pages are created only when opened; idle memory is about 15–40 MB.

### Languages
Русский, English, Українська — built in, works offline (Settings → Language).

## 📸 Screenshots
<p align="center">
  <img src="docs/screenshots/center-privacy.png" width="49%" alt="Anti-tracking"/>
  <img src="docs/screenshots/center-clean.png" width="49%" alt="Deep cleanup"/>
</p>
<p align="center">
  <img src="docs/screenshots/settings-styles.png" width="49%" alt="Modes and styles"/>
  <img src="docs/screenshots/settings-sounds.png" width="49%" alt="Sounds"/>
</p>

## ⌨️ Hotkeys

| Keys | Action |
|---|---|
| Ctrl+Alt+N | open the island |
| Ctrl+Alt+Space | quick search |
| Ctrl+Alt+L | Laptop Control Center |
| Ctrl+Alt+A | AI chat |
| Ctrl+Alt+H / D | hide the island / Do Not Disturb |
| Ctrl+Alt+C / V / T | color picker / clipboard / translator |

## 🖥 Requirements
Windows 10 version 2004 (build 19041) or newer, or Windows 11. x64 or x86. 4 GB RAM is enough.

## ❓ FAQ
**Is it safe? Will it break Windows?** Everything is reversible and labelled *Safe / Caution / Not recommended*. Items marked “Not recommended” are explanations only — they can't be applied. Create a restore point on the Overview page before big changes if you like.

**Antivirus complains.** Some antiviruses flag unsigned apps that change system settings. The full source code is here — build it yourself if you prefer (see below).

**Where is my data?** `%APPDATA%\NIsland` — settings, notes, backups. Nothing is sent anywhere. Network is used only for features you use: lyrics (LRCLIB, optional NetEase), weather (Open-Meteo), translation, the AI provider you choose, API tools and a manual update check on GitHub.

**How to uninstall?** Turn off “Start with Windows” in Settings, exit from the tray menu, delete the folder and `%APPDATA%\NIsland`.

## 🛠 Build from source
```powershell
git clone https://github.com/Neizvestnyi22/N-Island.git
cd N-Island
dotnet build -c Release          # .NET 8 SDK
.\build.ps1                      # release zips and exe into dist\
```
UI strings are written in Russian; translations live in `Assets/i18n/*.json` and are generated by `tools/I18nGen`
(`dotnet run --project tools/I18nGen -- . en uk`). Fixes to translations are welcome — just edit the JSON.

Project layout: `IslandWindow` (the notch), `Pages/` (notch pages), `SettingsWindow*.cs` (settings and Control Center), `Services/` (media, lyrics, calls, cleanup, privacy, optimization, activity, AI…), `Core/` (settings, sounds, i18n, presets).

## 📜 License
[MIT](LICENSE) © 2026 [@Ne1zvestny1i](https://t.me/Ne1zvestny1i). Built together with Claude (Anthropic). Version history — [CHANGELOG.md](CHANGELOG.md).
