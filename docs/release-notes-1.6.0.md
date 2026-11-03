## N-Island 1.6.0 — VPN, Wi-Fi tools, OSINT, 15 integrations, quick search

*made in TG [@Ne1zvestny1i](https://t.me/Ne1zvestny1i) & Claude*

Update from the app: **Settings → About → Update** · Обновление прямо из приложения: **Настройки → О программе → Обновить**.

### 🎵 Music · Музыка
- Track time now works in **any** app — even ones that don't report it (Firefox, some players): N-Island counts the time itself and takes the length from a catalog (marked “≈”).
  Время трека видно в любом приложении, даже если оно его не сообщает.
- The player no longer sticks to a closed browser tab; it recovers after sleep and Explorer restarts.
  Плеер не «залипает» на закрытой вкладке и восстанавливается после сна.
- Much better lyrics: exact LRCLIB match by length, **Kugou** as a new fallback next to NetEase, smart cleanup of YouTube/Spotify/VK titles, **word-by-word highlighting**, adjustable line lead, “● ● ●” on instrumental breaks.
  Тексты песен: точный поиск по длительности, новый источник Kugou, чистка названий, подсветка по словам.

### 🔒 VPN in the island · VPN в острове
Cloudflare WARP, NordVPN, Mullvad, Windscribe, Tailscale, PIA, OpenVPN, WireGuard and Windows VPN connections switch on and off right in the island; other VPNs show their status. External IP and country, a pop-up when a VPN drops.

### 🔗 15 integrations · 15 интеграций
Telegram bot, ntfy, Discord, webhooks, Home Assistant, Todoist, Google/Outlook/iCloud calendar, GitHub, RSS, ListenBrainz, Last.fm, exchange rates and crypto, Nova Poshta, website monitoring, air raid alerts (alerts.in.ua). Keys are encrypted with DPAPI; every service has a “Check” button.

### 📶 Wi-Fi tools · Wi-Fi
Live signal graph, nearby networks, devices on your network, and a full network check with advice: 5 GHz, channel load and free channels, WPS, router vs adapter standard, latency and loss (router vs internet), DNS speed, driver age, metered connection. Speed test, reconnect, forget network.
Сигнал вживую, сети рядом, устройства в сети, проверка сети с советами и свободные каналы.

### 🕵️ OSINT and checks · OSINT и проверки
Phishing link check, WHOIS/RDAP, DNS, certificates, subdomains, Wayback history, IP info, password leak check (HIBP k-anonymity), photo metadata/GPS with a clean copy, file hash → VirusTotal.
Проверка ссылок, доменов, IP, утечек пароля, GPS в фото.

### 🔍 Quick search — Ctrl+Alt+Space · Быстрый поиск
Spotlight-style: apps (even typed in the wrong keyboard layout), Windows settings, calculator, currency and units, translation, island commands, recent files, clipboard, web — and an AI answer right in the window.

### 🧹 No duplicates · Без дублей
Optionally turn off what the island already shows: Windows notification banners (kept in the notification center), the AutoPlay window, the volume OSD. Everything is restored when N-Island closes.

### ⚙️ Settings and stability · Настройки и стабильность
Settings search in English/Ukrainian with highlighting, values that follow changes made from the island, mouse-wheel steppers, settings backup / restore / reset, automatic recovery of a damaged settings file. The island recovers after sleep, DPI changes, unlocking and Explorer restarts.

| File | |
|---|---|
| **NIsland-1.6.0-win-x64.zip** | Recommended · рекомендуется |
| NIsland-1.6.0-win-x86.zip | 32-bit Windows 10 |
| NIsland-1.6.0-win-x64-lite.exe | needs .NET 8 Desktop Runtime · нужен .NET 8 |
