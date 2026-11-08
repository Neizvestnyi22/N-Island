## N-Island 1.8.0 — living island, MCP for AI agents, built-in Defender, Telegram PC remote 2.0

*made in TG [@Ne1zvestny1i](https://t.me/Ne1zvestny1i) & Claude*

Update from the app: **Settings → About → Update** · Обновление прямо из приложения: **Настройки → О программе → Обновить**.

### 🫧 Living island · Живой остров
A little character lives in the collapsed island (blob, cat, ghost, robot, bunny): it dances to music, thinks while the AI answers, listens during calls, sleeps at night and in Do Not Disturb, smiles when you hover. Real liquid glass: a sharper, more vibrant background that the edges refract like a lens, a glint that follows the cursor, a rim that pulses with the music, a shimmer on notifications.
Персонаж в чёлке с настроением, настоящее жидкое стекло, блик за курсором, ободок в такт музыке.

### 🌈 47 visualizers · 47 визуализаторов
34 new styles in two groups — Nostalgia (fire, starfield, plasma, VU meters, Lissajous, light music, matrix, CRT, pixels, cassette, vinyl, night city…) and Modern (orb, galaxy, aurora, ridges, synthwave, DNA, lightning, lava, kaleidoscope, fountain, ECG, flower, hexagons…). 12 new palettes and your own colours, favourites ★, and fine-tuning: sensitivity, smoothness, trails, density, speed, glow, mirror, beat flash, scanlines, background, FPS.
34 новых стиля, «Ностальгия» и «Современные», избранное и тонкая настройка.

### 🎤 Lyrics · Тексты песен
Five styles (Apple, classic, neon, minimal, bounce), font, colour, alignment, a romanized line under Cyrillic/Greek/Korean lyrics, copy and save as .lrc, and **desktop lyrics** — the current line on top of all windows with a karaoke fill (click-through when locked, hidden in games and movies). More stable search: a failing source is skipped for a while, 429 is retried, a strong NetEase/Kugou match no longer waits for LRCLIB.
5 стилей, строка латиницей, .lrc и текст на рабочем столе.

### 📱 Telegram PC remote 2.0 · Пульт ПК в Telegram 2.0
Button menus for music, equalizer, sound outputs, per-app volume, screen (brightness, screenshots, monitor off, theme, wallpaper), windows, keyboard and mouse, presentations, files (browse, search, download, zip, Recycle Bin, print), camera, “where is my laptop”, power, network, VPN, modes, to-dos, timer and reminders. **Chat with the island's AI from the bot** — one shared conversation with the island chat, the answer streams in as it is written. Uses recent Bot API features: reactions, copy buttons, expandable quotes, bot description. Dangerous permissions can only be changed on the PC.
Всё управление ПК кнопками и ИИ острова прямо в боте.

### 🧠 AI and providers · ИИ и провайдеры
New providers (keyless LLM7, Cerebras, Perplexity, GitHub Models, Hugging Face, Zhipu GLM, Qwen…), a searchable model list fetched from the server and refreshed every 3 days, a backup provider when the main one fails, automatic model pick when a provider drops yours, custom providers with headers (Azure `api-key`), fixes for addresses ending in `/chat/completions`, retries for strict servers, hidden `<think>` blocks, “Check all”.
Новые провайдеры, обновляемый список моделей, запасной провайдер, починены свои провайдеры.

### 🤖 AI agents on your PC · ИИ-агенты на ПК
Claude Code, Codex, Gemini CLI, Qwen Code, OpenCode, Copilot CLI, Aider, Cursor Agent: what is installed and running, launch in a project folder or run a task without a window, “agent is waiting for confirmation / finished” in the island and in Telegram (Claude Code hooks, Codex notify), today's Claude Code token usage, `/agents` and `/agent` in the bot.

### 🔌 MCP server · MCP-сервер
About 30 tools for AI agents: PC and island status, now playing and lyrics, notifications, notes, to-dos, reminders, timer, clipboard, screenshot, island pop-ups and progress bars, Telegram messages, changing the island's look, security audit and file scan. Three access levels (read, control, island settings), local only, token-protected; one-click setup for Claude Code and ready configs for Claude Desktop, Cursor and Codex.
Агенты показывают ход задач в острове, пишут в Telegram и меняют вид острова.

### 🛡 N-Island Defender · Защитник N-Island
Complements Windows Defender: a known-threat hash database (signature-base), the Windows engine (AMSI), signatures, heuristics for programs (packers, entropy, dangerous API combos) and scripts (hidden launch, download-and-run, disabling Defender, deleting shadow copies), quick/folder/file scans, an online hash lookup (CIRCL), encrypted quarantine, real-time protection for Downloads, Desktop and startup, autorun watch and a Windows security audit.

### 🩺 PC health · Здоровье ПК
A 0–100 score, crashes and blue screens, devices with errors, app updates via winget, CPU throttling, Turbo mode (one click back), disk usage breakdown, files over 100 MB, duplicates, memory test, TRIM.

### 📶 Wi-Fi · 🌦 Weather · 🧩 Integrations
Wi-Fi: mobile hotspot, stability test (jitter, loss, spikes), fastest DNS, traceroute, quick internet repair, connection details, Wake-on-LAN, a signal map by room, nearby network security (evil twins, WPS, open networks), latency to world regions, strong password generator.
Weather: dew point, visibility, clouds, pollen, moon phase, radar, tomorrow vs today, other cities, dangerous-weather alerts to the screen and phone; more accurate city search.
Integrations: free games, game deals, earthquakes, geomagnetic storms and aurora, NASA picture of the day, GitHub releases, “On this day”, and **your own integrations** without code (any JSON API, a number with thresholds, page changes, uptime). Monobank and air-raid alerts were removed.

### 🛠 Fixes · Исправлено
The character frowned instead of smiling; Claude Code bundled with the Claude app was not detected; AppData size counted cloud placeholders; Windows crash logs were not read because of the console code page; some cities resolved to villages; AI providers with a trailing `/v1/`; Ukrainian used “AI” and “челка”. All new UI strings are translated into English and Ukrainian by hand.
