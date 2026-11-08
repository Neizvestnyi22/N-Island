using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NIsland.Core;

namespace NIsland.Services
{
    // Telegram: состояние ПК, файлы, снимки, камера, ИИ — то, что вызывают команды и меню
    public partial class TelegramHub
    {
        // ------------------------------------------------------------------
        //  Состояние
        // ------------------------------------------------------------------

        static string StatusText()
        {
            var sys = SystemService.I;
            var m = MediaService.I;
            sys.CpuPercent();
            Thread.Sleep(300);
            var sb = new StringBuilder("💻 <b>" + H(Environment.MachineName) + "</b> · " + DateTime.Now.ToString("HH:mm") + "\n");
            if (sys.HasBattery) sb.Append($"🔋 {sys.BatteryPercent}%{(sys.Charging ? " ⚡ заряжается" : sys.BatteryLeft is TimeSpan left ? $" · ещё {(int)left.TotalHours} ч {left.Minutes} мин" : "")}\n");
            sb.Append($"⚙️ Процессор {sys.CpuPercent()}% · память {sys.RamPercent()}%\n");
            var up = Maint.Uptime;
            sb.Append($"⏱ Работает {(up.TotalDays >= 1 ? (int)up.TotalDays + " дн " : "")}{up.Hours} ч {up.Minutes} мин\n");
            sb.Append($"🔊 Громкость {Math.Round(AudioService.I.Volume * 100)}%{(AudioService.I.Muted ? " (без звука)" : "")} · {H(AudioService.I.OutputName)}\n");
            sb.Append($"🪟 Активное окно: {H(Cut(PcControl.ActiveTitle(), 60))}\n");
            if (Vpn.Active) sb.Append("🔒 VPN включён\n");
            if (sys.KeepAwake) sb.Append("☕ Не засыпает\n");
            sb.Append($"🌙 Режим: {H(T(Modes.Current.Name))}");
            if (m.HasSession) sb.Append($"\n🎵 {(m.IsPlaying ? "Играет" : "Пауза")}: {H(m.TrackLine)}");
            return sb.ToString();
        }

        static string ShortStatus()
        {
            var sys = SystemService.I;
            var m = MediaService.I;
            var parts = new List<string> { $"⚙️ {sys.CpuPercent()}%", $"🧠 {sys.RamPercent()}%" };
            if (sys.HasBattery) parts.Add($"🔋 {sys.BatteryPercent}%{(sys.Charging ? "⚡" : "")}");
            parts.Add($"🔊 {(AudioService.I.Muted ? "выкл" : Math.Round(AudioService.I.Volume * 100) + "%")}");
            var s = string.Join(" · ", parts);
            if (m.HasSession) s += $"\n🎵 {(m.IsPlaying ? "▶" : "⏸")} {H(Cut(m.TrackLine, 60))}";
            return s;
        }

        static string DisksText() =>
            "💾 <b>Диски</b>\n" + string.Join("\n", DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType is DriveType.Fixed or DriveType.Removable)
                .Select(d => $"{d.Name.TrimEnd('\\')} {H(d.VolumeLabel)} — свободно {Maint.Size(d.AvailableFreeSpace)} из {Maint.Size(d.TotalSize)} ({100 - d.AvailableFreeSpace * 100 / Math.Max(1, d.TotalSize)}% занято)"));

        static async Task<string> BatteryText()
        {
            var sys = SystemService.I;
            if (!sys.HasBattery) return "🔌 У этого ПК нет батареи";
            var hb = await Maint.Battery();
            return $"🔋 {sys.BatteryPercent}%{(sys.Charging ? " ⚡ заряжается" : sys.BatteryLeft is TimeSpan l ? $" · ещё {(int)l.TotalHours} ч {l.Minutes} мин" : "")}" +
                   (hb != null && hb.Design > 0 ? $"\nИзнос: ёмкость {hb.Full * 100 / hb.Design}% от новой" + (hb.Cycles > 0 ? $" · циклов {hb.Cycles}" : "") : "");
        }

        static async Task<string> WifiText()
        {
            var l = await WifiTools.Current();
            return l == null ? "📶 Wi-Fi не подключён" : $"📶 <b>{H(l.Ssid)}</b> · сигнал {l.Signal}% ({l.Rssi} dBm)\nканал {l.Channel} · {H(l.Band)} · {H(l.Standard)} · {Math.Max(l.RxMbps, l.TxMbps):0} Мбит/с";
        }

        static async Task<string> PingText(string arg)
        {
            var hosts = arg != "" ? new[] { arg.Trim() } : new[] { WifiTools.Gateway()?.ToString(), "1.1.1.1", "google.com" }.Where(h => h != null).ToArray();
            var sb = new StringBuilder("🏓 <b>Пинг</b>\n");
            foreach (var h in hosts)
            {
                using var ping = new System.Net.NetworkInformation.Ping();
                var times = new List<long>();
                int lost = 0;
                for (int i = 0; i < 4; i++)
                {
                    try
                    {
                        var r = await ping.SendPingAsync(h, 2000);
                        if (r.Status == System.Net.NetworkInformation.IPStatus.Success) times.Add(r.RoundtripTime); else lost++;
                    }
                    catch { lost++; }
                }
                sb.Append(times.Count == 0 ? $"{H(h)} — не отвечает\n" : $"{H(h)} — {times.Average():0} мс (от {times.Min()} до {times.Max()}){(lost > 0 ? $", потеряно {lost} из 4" : "")}\n");
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------
        //  Процессы
        // ------------------------------------------------------------------

        static readonly string[] Protected = { "system", "idle", "csrss", "wininit", "winlogon", "lsass", "services", "smss", "svchost", "dwm", "fontdrvhost",
                                               "nisland", "registry", "memory compression", "audiodg", "explorer", "sihost", "ctfmon", "msmpeng" };

        (string, object) TopMenu()
        {
            var sys = SystemService.I;
            sys.CpuPercent();
            var before = System.Diagnostics.Process.GetProcesses().Select(p => { try { return (p.Id, p.ProcessName, Cpu: p.TotalProcessorTime.TotalMilliseconds); } catch { return (p.Id, p.ProcessName, Cpu: -1.0); } }).ToList();
            Thread.Sleep(1000);
            int cpuAll = sys.CpuPercent();
            var rows = new List<(string Name, double Cpu, long Ram)>();
            foreach (var b in before.Where(b => b.Cpu >= 0))
            {
                try
                {
                    using var p = System.Diagnostics.Process.GetProcessById(b.Id);
                    rows.Add((b.ProcessName, (p.TotalProcessorTime.TotalMilliseconds - b.Cpu) / 10.0 / Environment.ProcessorCount, p.WorkingSet64));
                }
                catch { }
            }
            var byName = rows.GroupBy(r => r.Name).Select(g => (Name: g.Key, Cpu: g.Sum(x => x.Cpu), Ram: g.Sum(x => x.Ram))).ToList();
            var cpuTop = byName.Where(x => x.Name != "Idle").OrderByDescending(x => x.Cpu).Take(6).ToList();
            var ramTop = byName.OrderByDescending(x => x.Ram).Take(6).ToList();
            var sb = new StringBuilder($"🔥 <b>Процессор {cpuAll}% · память {sys.RamPercent()}%</b>\n\n<b>Процессор:</b>\n");
            foreach (var r in cpuTop) sb.Append($"{r.Cpu:0}% — {H(r.Name)}\n");
            sb.Append("\n<b>Память:</b>\n");
            foreach (var r in ramTop) sb.Append($"{Maint.Size(r.Ram)} — {H(r.Name)}\n");
            var keys = new List<object[]>();
            if (Flag("apps"))
            {
                sb.Append("\nНажми, чтобы закрыть программу:");
                var names = cpuTop.Concat(ramTop).Select(x => x.Name).Distinct().Where(n => !Protected.Contains(n.ToLowerInvariant())).Take(8);
                keys.AddRange(Grid(names.Select(n => B("✕ " + Cut(n, 20), "kill:" + Cut(n, 50))), 2));
            }
            keys.Add(Row(B("🔄", "nav:top"), B("⬅️ Назад", "nav:main")));
            return (sb.ToString(), K(keys.ToArray()));
        }

        /// <summary>Закрыть по номеру процесса или имени: сначала вежливо (как крестик окна), через 3 с — принудительно.</summary>
        static string Kill(string what)
        {
            what = (what ?? "").Trim();
            if (what == "") return "Напиши: /kill chrome  или нажми кнопку в /top";
            System.Diagnostics.Process[] list;
            try
            {
                list = int.TryParse(what, out var pid)
                    ? new[] { System.Diagnostics.Process.GetProcessById(pid) }
                    : System.Diagnostics.Process.GetProcessesByName(what.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? what[..^4] : what);
            }
            catch { list = Array.Empty<System.Diagnostics.Process>(); }
            if (list.Length == 0) return "Нет такой программы: " + H(what);
            var name = list[0].ProcessName;
            if (Protected.Contains(name.ToLowerInvariant())) { foreach (var p in list) p.Dispose(); return "⛔ " + H(name) + " — системный процесс, его закрывать нельзя"; }
            int n = 0;
            foreach (var p in list)
            {
                try
                {
                    if (p.MainWindowHandle != IntPtr.Zero) p.CloseMainWindow();
                    if (!p.WaitForExit(3000)) p.Kill(true);
                    n++;
                }
                catch { }
                finally { p.Dispose(); }
            }
            Toast("Закрыто из Telegram", name);
            return n > 0 ? $"✕ Закрыто: {H(name)}" + (n > 1 ? $" ({n} шт.)" : "") : "Не получилось закрыть " + H(name);
        }

        // ------------------------------------------------------------------
        //  Файлы
        // ------------------------------------------------------------------

        static string Expand(string path)
        {
            path = (path ?? "").Trim().Trim('"');
            if (path == "" || path == "~") return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var known = path.ToLowerInvariant() switch
            {
                "загрузки" or "downloads" or "завантаження" => PcControl.Places[1].Path(),
                "рабочий стол" or "desktop" or "робочий стіл" => Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                "документы" or "documents" or "документи" => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "изображения" or "pictures" or "фото" => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                _ => null,
            };
            return known ?? Environment.ExpandEnvironmentVariables(path.StartsWith("~") ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + path[1..] : path);
        }

        const int PageSize = 16;

        (string, object) Dir(string dir, int page, bool recent = false)
        {
            if (!Directory.Exists(dir)) return ("Нет такой папки: " + H(dir), K(Row(B("📁 Места", "nav:files"))));
            var di = new DirectoryInfo(dir);
            List<FileSystemInfo> entries;
            try
            {
                var dirs = recent ? new List<FileSystemInfo>() : di.EnumerateDirectories().Where(x => !x.Attributes.HasFlag(FileAttributes.Hidden)).OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).Cast<FileSystemInfo>().ToList();
                var files = di.EnumerateFiles().Where(x => !x.Attributes.HasFlag(FileAttributes.Hidden)).OrderByDescending(x => x.LastWriteTime).Cast<FileSystemInfo>().ToList();
                entries = dirs.Concat(recent ? files.Take(PageSize) : files).ToList();
            }
            catch (UnauthorizedAccessException) { return ("⛔ Нет доступа к папке " + H(dir), K(Row(B("📁 Места", "nav:files")))); }
            int pages = Math.Max(1, (entries.Count + PageSize - 1) / PageSize);
            page = Math.Clamp(page, 0, pages - 1);
            var tok = Put(di.FullName);
            var rows = entries.Skip(page * PageSize).Take(PageSize).Select(e => e is DirectoryInfo
                ? Row(B("📁 " + Cut(e.Name, 40), "d:" + Put(e.FullName)))
                : Row(B("📄 " + Cut(e.Name, 34) + " · " + Maint.Size(((FileInfo)e).Length), "f:" + Put(e.FullName)))).ToList();
            var nav = new List<object>();
            if (di.Parent != null && !recent) nav.Add(B("⬆ Вверх", "d:" + Put(di.Parent.FullName)));
            if (page > 0) nav.Add(B("◀", $"dp:{tok}:{page - 1}"));
            if (page < pages - 1) nav.Add(B("▶", $"dp:{tok}:{page + 1}"));
            if (!recent && di.Parent != null) nav.Add(B("🗜 Скачать zip", "z:" + tok));
            if (nav.Count > 0) rows.Add(nav.ToArray());
            rows.Add(Row(B("📁 Места", "nav:files"), B("⬅️ Пульт", "nav:main")));
            var head = recent ? "⬇️ <b>Новое в Загрузках</b>" : $"📁 <code>{H(di.FullName)}</code>";
            return ($"{head}\nэлементов: {entries.Count}{(pages > 1 ? $" · стр. {page + 1}/{pages}" : "")}" + (entries.Count == 0 ? "\n(пусто)" : ""), K(rows.ToArray()));
        }

        (string, object) SearchFiles(string q)
        {
            var found = PcControl.Search(q.Trim());
            if (found.Count == 0) return ($"🔎 Ничего не нашлось по «{H(q)}» (искал в Рабочем столе, Загрузках, Документах, Изображениях, Видео, Музыке)", null);
            var rows = found.Select(p => Row(B((Directory.Exists(p) ? "📁 " : "📄 ") + Cut(Path.GetFileName(p), 40), (Directory.Exists(p) ? "d:" : "f:") + Put(p)))).ToList();
            return ($"🔎 Найдено по «{H(q)}»: {found.Count}", K(rows.ToArray()));
        }

        const long MaxUpload = 49L * 1024 * 1024;

        /// <summary>Отправить файл или папку (zip). Возвращает текст ошибки или null.</summary>
        async Task<string> SendPath(string path)
        {
            string zip = null;
            try
            {
                if (Directory.Exists(path))
                {
                    zip = await Task.Run(() => PcControl.Zip(path, MaxUpload));
                    if (zip == null) return "🗜 Папка больше 50 МБ — Telegram не примет её от бота. Открой её и скачай файлы по отдельности";
                    path = zip;
                }
                if (!File.Exists(path)) return "Нет такого файла: " + H(path) + "\nПосмотреть папку: /files";
                var fi = new FileInfo(path);
                if (fi.Length > MaxUpload) return $"Файл {Maint.Size(fi.Length)} — больше 50 МБ, Telegram не примет его от бота";
                await using var fs = File.OpenRead(path);
                var content = new StreamContent(fs);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                await Upload("sendDocument", "document", content, fi.Name);
                Toast("Файл отправлен в Telegram", fi.Name);
                return null;
            }
            catch (IOException e) { return "Файл занят другой программой: " + H(e.Message); }
            finally { if (zip != null) try { File.Delete(zip); } catch { } }
        }

        async Task<(byte[] Bytes, string Path)> Download(string fileId)
        {
            var info = await Call("getFile", new { file_id = fileId });
            var fp = Integrations.S(info, "file_path");
            if (fp == "") return (null, null);
            return (await Big.GetByteArrayAsync($"https://api.telegram.org/file/bot{V("token")}/{fp}"), fp);
        }

        /// <summary>Файл, фото, видео, голосовое, присланные боту, — в «Загрузки\N-Island» (Telegram отдаёт ботам файлы до 20 МБ).</summary>
        async Task ReceiveFile(JsonElement m, string caption)
        {
            string fileId = null, name = null;
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            if (m.TryGetProperty("document", out var doc)) { fileId = Integrations.S(doc, "file_id"); name = Integrations.S(doc, "file_name"); }
            else if (m.TryGetProperty("photo", out var ph) && ph.GetArrayLength() > 0) { fileId = Integrations.S(ph[ph.GetArrayLength() - 1], "file_id"); name = $"photo-{stamp}.jpg"; }
            else if (m.TryGetProperty("video", out var vd)) { fileId = Integrations.S(vd, "file_id"); name = Integrations.S(vd, "file_name") is { Length: > 0 } vn ? vn : $"video-{stamp}.mp4"; }
            else if (m.TryGetProperty("audio", out var au)) { fileId = Integrations.S(au, "file_id"); name = Integrations.S(au, "file_name"); }
            else if (m.TryGetProperty("voice", out var vo)) { fileId = Integrations.S(vo, "file_id"); name = $"voice-{stamp}.ogg"; }
            else if (m.TryGetProperty("video_note", out var vn2)) { fileId = Integrations.S(vn2, "file_id"); name = $"video-note-{stamp}.mp4"; }
            if (fileId == null) return;
            if (Need("files") is { } no) { await Message(no); return; }
            var (bytes, fp) = await Download(fileId);
            if (bytes == null) { await Message("Файл больше 20 МБ — Telegram не отдаёт такие ботам"); return; }
            var dir = Path.Combine(PcControl.Places[1].Path(), "N-Island");
            Directory.CreateDirectory(dir);
            name = string.Concat((string.IsNullOrEmpty(name) ? Path.GetFileName(fp) : name).Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            var target = Path.Combine(dir, name);
            for (int i = 2; File.Exists(target); i++) target = Path.Combine(dir, Path.GetFileNameWithoutExtension(name) + $" ({i})" + Path.GetExtension(name));
            await File.WriteAllBytesAsync(target, bytes);
            // пометка «из интернета»: SmartScreen и Office предупредят, если это программа или документ с макросами
            try { File.WriteAllText(target + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\nReferrerUrl=https://t.me/\r\n"); } catch { }
            OnUi(() =>
            {
                NIsland.Pages.ShelfPage.Add(new[] { target });   // сразу на полку острова — удобно перетащить куда нужно
                App.Island?.ShowActivity(new Activity { Kind = "hub", Glyph = "", Tint = System.Windows.Media.Color.FromRgb(0x2A, 0xAB, 0xEE), Title = "Файл с телефона", Sub = Path.GetFileName(target), Seconds = 6, Wide = true,
                    OnClick = () => System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{target}\"")?.Dispose() });
            });
            var t = Put(target);
            string extra = "";
            var cap = caption.ToLowerInvariant();
            if ((cap.StartsWith("/open") || cap.StartsWith("/print")) && Need("apps") is { } noApps) extra = "\n" + noApps;
            else if (cap.StartsWith("/open")) { await Ui(() => SystemService.Run(target)); extra = "\n▶ Открыт на ПК"; }
            else if (cap.StartsWith("/print"))
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { Verb = "print", UseShellExecute = true })?.Dispose(); extra = "\n🖨 Отправлен на печать"; }
                catch (Exception e) { extra = "\n🖨 Не удалось напечатать: " + H(e.Message); }
            }
            await Message($"💾 Сохранено на ПК: <code>{H(target)}</code>{extra}",
                K(Row(Flag("apps") ? B("▶ Открыть", "fo:" + t) : null, B("📋 Путь в буфер", "fc:" + t), B("🗑 В корзину", "fd:" + t))));
        }

        async Task<(string, object)?> Wallpaper(JsonElement m)
        {
            if (Need("control") is { } no) return (no, null);
            string fileId = null, ext = ".jpg";
            if (m.TryGetProperty("photo", out var ph) && ph.GetArrayLength() > 0) fileId = Integrations.S(ph[ph.GetArrayLength() - 1], "file_id");
            else if (m.TryGetProperty("document", out var doc) && Integrations.S(doc, "mime_type").StartsWith("image/"))
            {
                fileId = Integrations.S(doc, "file_id");
                ext = Path.GetExtension(Integrations.S(doc, "file_name")) is { Length: > 1 } e ? e : ".jpg";
            }
            if (fileId == null) return ("🖼 Пришли картинку (фото или файл) с подписью /wallpaper", null);
            var (bytes, _) = await Download(fileId);
            if (bytes == null) return ("Картинка больше 20 МБ — Telegram не отдаёт такие ботам", null);
            var err = await Task.Run(() => PcControl.SetWallpaper(bytes, ext));
            if (err == null) Toast("Новые обои из Telegram", null);
            return (err == null ? "🖼 Обои рабочего стола изменены" : "⚠️ " + H(err), null);
        }

        // ------------------------------------------------------------------
        //  Экран, камера, местоположение
        // ------------------------------------------------------------------

        async Task Screenshot(int which, bool png)
        {
            await Action("upload_photo");
            var bytes = await Task.Run(() => PcControl.Screenshot(which, png));
            var caption = "🖥 " + DateTime.Now.ToString("HH:mm:ss") + (which == -1 ? " · " + H(Cut(PcControl.ActiveTitle(), 60)) : "");
            var keys = K(Row(B("🔄 Ещё раз", "sh:" + which), B("🖼 Без сжатия", "sh:png"), B("🪟 Окно", "sh:-1")));
            if (png) await Upload("sendDocument", "document", Bytes(bytes, "image/png"), $"screen-{DateTime.Now:HHmmss}.png", caption);
            else
            {
                try { await Upload("sendPhoto", "photo", Bytes(bytes, "image/jpeg"), "screen.jpg", caption, keys); }
                catch (TgError) { await Upload("sendDocument", "document", Bytes(bytes, "image/jpeg"), $"screen-{DateTime.Now:HHmmss}.jpg", caption, keys); }   // очень широкий снимок нескольких мониторов
            }
            Toast("Снимок экрана отправлен в Telegram", null);
        }

        async Task CameraShot()
        {
            Toast("Снимок с камеры", "по команде из твоего Telegram");
            await Action("upload_photo");
            byte[] jpg;
            try { jpg = await PcControl.CameraPhoto(); }
            catch (Exception e) { await Message("📷 " + H(e.Message)); return; }
            await Upload("sendPhoto", "photo", Bytes(jpg, "image/jpeg"), "camera.jpg", "📷 " + DateTime.Now.ToString("HH:mm:ss"), K(Row(B("📷 Ещё", "do:cam"), B("📍 Где он", "do:where"))));
        }

        async Task<string> Where()
        {
            await Action("find_location");
            var loc = await PcControl.Location();
            Toast("Местоположение отправлено", "по команде из твоего Telegram");
            if (loc is { } l)
            {
                await Call("sendLocation", new { chat_id = V("chat"), latitude = l.Lat, longitude = l.Lon, horizontal_accuracy = Math.Clamp(l.Acc, 0, 1500) });
                return $"📍 Точность около {Math.Round(l.Acc)} м · {DateTime.Now:HH:mm}";
            }
            try { await Vpn.LoadIp(); } catch { }
            var ip = Vpn.PublicIp;
            return "📍 Windows не дала координаты — включи «Расположение» в Параметрах → Конфиденциальность (и доступ для классических приложений)." +
                   (ip.Ip != null ? $"\nПо IP: {ip.Flag} {H(ip.City)}{(string.IsNullOrEmpty(ip.City) ? "" : ", ")}{H(ip.Country)} · {H(ip.Isp)} ({H(ip.Ip)})" : "") +
                   (Vpn.Active ? "\n⚠️ Включён VPN — место по IP неточное" : "");
        }

        // ------------------------------------------------------------------
        //  Погода, тревога, посылки, напоминания
        // ------------------------------------------------------------------

        static async Task<string> WeatherText()
        {
            var w = await WeatherService.Get();
            if (w == null) return "🌦 Погода недоступна: " + H(WeatherService.LastError ?? "нет связи");
            var (icon, desc) = WeatherService.Describe(w.Code);
            var sb = new StringBuilder($"{icon} <b>{H(w.City)}: {Math.Round(w.Temp)}°</b>, {H(T(desc))}\nощущается {Math.Round(w.Feels)}° · ветер {Math.Round(w.Wind)} м/с · влажность {Math.Round(w.Humidity)}%");
            if (WeatherService.Nowcast(w) is { } nc) sb.Append("\n☔ " + H(nc));
            foreach (var d in w.Days.Take(4)) sb.Append($"\n{Fmt.Cap(d.Day.ToString("ddd d", Fmt.Ru))}: {WeatherService.Describe(d.Code).Icon} {Math.Round(d.Max)}° / {Math.Round(d.Min)}°");
            return sb.ToString();
        }

        static async Task<string> ParcelsText()
        {
            var np = Integrations.Get("novaposhta");
            if (np == null || !np.Enabled || !np.Ready) return "📦 Посылки не подключены (Интеграции → Нова пошта)";
            await Integrations.PollOne(np);
            return np.Rows.Count == 0 ? "📦 Посылок нет" : "📦 " + string.Join("\n\n", np.Rows.Select(r => "<b>" + H(r.Title) + "</b>\n" + H(r.Sub)));
        }

        async Task<string> Remind(string arg)
        {
            if (Need("control") is { } no) return no;
            if (arg == "")
            {
                var next = Store.Data.Events.Where(e => e.At is DateTime at && at > DateTime.Now).OrderBy(e => e.At).Take(8).ToList();
                return (next.Count == 0 ? "📅 Напоминаний нет" : "📅 <b>Ближайшие</b>\n" + string.Join("\n", next.Select(e => $"{e.At:dd.MM HH:mm} — {H(e.Title)}"))) +
                       "\n\nДобавить: /remind 30 текст · /remind 2ч текст · /remind 18:30 текст · /remind завтра 9:00 текст";
            }
            DateTime when;
            string what;
            var rel = Regex.Match(arg, @"^(\d{1,4})\s*(м|мин|хв|m|min|ч|год|h)?\s+(.+)$", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            var abs = Regex.Match(arg, @"^(завтра|tomorrow)?\s*(?:в\s+|о\s+|at\s+)?(\d{1,2})[:.](\d{2})\s+(.+)$", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (abs.Success && int.Parse(abs.Groups[2].Value) < 24 && int.Parse(abs.Groups[3].Value) < 60)
            {
                when = DateTime.Today.AddHours(int.Parse(abs.Groups[2].Value)).AddMinutes(int.Parse(abs.Groups[3].Value));
                if (abs.Groups[1].Success && abs.Groups[1].Value != "") when = when.AddDays(1);
                else if (when <= DateTime.Now) when = when.AddDays(1);
                what = abs.Groups[4].Value.Trim();
            }
            else if (rel.Success)
            {
                int n = int.Parse(rel.Groups[1].Value);
                when = DateTime.Now.AddMinutes(rel.Groups[2].Value.ToLowerInvariant() is "ч" or "h" or "год" ? n * 60 : n);
                what = rel.Groups[3].Value.Trim();
            }
            else return "Напиши так: /remind 30 вынуть пиццу (минуты) · /remind 2ч созвон · /remind 18:30 позвонить · /remind завтра 9:00 встреча";
            await Ui(() => { Store.Data.Events.Add(new CalEvent { Date = when.Date, Time = when.ToString("HH:mm"), Title = what }); Store.SaveData(); });
            return $"⏰ Напомню {(when.Date == DateTime.Today ? "сегодня" : when.Date == DateTime.Today.AddDays(1) ? "завтра" : when.ToString("dd.MM"))} в {when:HH:mm}: {H(what)}";
        }

        // ------------------------------------------------------------------
        //  ИИ прямо в чате
        // ------------------------------------------------------------------

        /// <summary>Свой диалог бота, если общий с островом выключен.</summary>
        static readonly List<AiMessage> AiHistory = new();
        static bool _noDraft;
        static string _lastAi, _lastQuestion;

        async Task<(string, object)?> AskAi(string q, bool retry = false)
        {
            var p = Ai.Current;
            if (!Ai.Ready(p, out var why) && Store.Settings.AiFallback == "none") return ("🤖 " + H(why), null);
            bool shared = Store.Settings.AiShareTelegram;
            _lastQuestion = q;
            var userMsg = new AiMessage("user", q, "tg");
            List<AiMessage> hist;
            if (retry)
            {
                // «ещё вариант»: убираем прошлый ответ и спрашиваем то же самое
                var list = shared ? Ai.Chat : AiHistory;
                lock (list) { if (list.Count > 0 && list[^1].Role == "assistant") list.RemoveAt(list.Count - 1); hist = list.ToList(); }
                if (hist.Count == 0 || hist[^1].Role != "user") hist.Add(userMsg);
            }
            else if (shared) { await Ui(() => Ai.AddToChat(userMsg)); lock (Ai.Chat) hist = Ai.Chat.ToList(); }
            else lock (AiHistory) { AiHistory.Add(userMsg); hist = AiHistory.ToList(); }
            long replyTo = retry ? 0 : _lastMsgId;
            await React(replyTo, "👀");

            // поток: черновик (Bot API 9.3, sendMessageDraft), а если он недоступен — правка одного сообщения раз в ~1,5 с
            var sb = new StringBuilder();
            long liveId = 0;
            int draftId = (Environment.TickCount & 0x7FFFFFFF) | 1;
            var lastPush = DateTime.MinValue;
            string pushed = "";
            var gate = new SemaphoreSlim(1, 1);
            async Task Push()
            {
                if (!await gate.WaitAsync(0)) return;
                try
                {
                    string text;
                    lock (sb) text = sb.ToString();
                    if (text.Length == 0 || text == pushed) return;
                    var preview = Cut(text, 3900);
                    if (!_noDraft)
                    {
                        try { await Call("sendMessageDraft", new { chat_id = V("chat"), draft_id = draftId, text = preview }); pushed = text; return; }
                        catch (TgError) { _noDraft = true; }
                    }
                    if (liveId == 0) liveId = await Message("🤖 " + H(preview), translate: false, replyTo: replyTo, silent: true);
                    else
                        try { await Call("editMessageText", new { chat_id = V("chat"), message_id = liveId, text = "🤖 " + preview, link_preview_options = NoPreview }); }
                        catch (TgError e) when (e.Message.Contains("not modified")) { }
                    pushed = text;
                }
                catch { }
                finally { gate.Release(); }
            }

            string ans;
            using (var cts = new CancellationTokenSource(TimeSpan.FromMinutes(4)))
            {
                _ = Task.Run(async () => { while (!cts.IsCancellationRequested) { await Action("typing"); await Task.Delay(4500, cts.Token).ContinueWith(_ => { }); } });
                try
                {
                    ans = await Ai.Ask(hist, d =>
                    {
                        lock (sb) { if (d == "") sb.Clear(); else sb.Append(d); }
                        if (Store.Settings.AiStream && (DateTime.Now - lastPush).TotalMilliseconds > 1400) { lastPush = DateTime.Now; _ = Push(); }
                    }, cts.Token, channel: "tg");
                    ans = Ai.StripThink(ans);
                }
                catch (Exception e)
                {
                    if (!retry) { if (shared) lock (Ai.Chat) Ai.Chat.Remove(userMsg); else lock (AiHistory) AiHistory.Remove(userMsg); }
                    await React(replyTo, "🤷");
                    var err = "🤖 ⚠️ " + H(Cut(e.Message, 500));
                    if (liveId != 0) { await Edit(liveId, err, null); return null; }
                    return (err, null);
                }
                finally { cts.Cancel(); }
            }
            if (string.IsNullOrWhiteSpace(ans)) ans = "(пустой ответ)";
            var botMsg = new AiMessage("assistant", ans, "tg");
            if (shared) await Ui(() => Ai.AddToChat(botMsg));
            else lock (AiHistory) { AiHistory.Add(botMsg); while (AiHistory.Count > 24) AiHistory.RemoveRange(0, 2); }
            _lastAi = ans;
            await React(replyTo, null);
            var who = Ai.LastAnswered ?? p.Name;
            var html = $"🤖 <i>{H(who)} · {H(Ai.Model(p))}</i>\n\n" + Md(ans);
            var keys = K(Row(B("🆕 Новый диалог", "ainew"), B("💻 В буфер ПК", "aicp"), Copy("📱 Копировать", ans)),
                         Row(B("🔁 Другой ответ", "airetry"), B("🧠 Нейросеть", "nav:ai")));
            if (liveId != 0 && html.Length <= 4000) await Edit(liveId, html, keys);
            else
            {
                if (liveId != 0) try { await Call("deleteMessage", new { chat_id = V("chat"), message_id = liveId }); } catch { }
                await Message(html, keys, translate: false, replyTo: replyTo);
            }
            return null;
        }

        /// <summary>Markdown ответа модели → HTML Telegram (код, жирный, заголовки).</summary>
        static string Md(string s)
        {
            var parts = (s ?? "").Split("```");
            var sb = new StringBuilder();
            for (int i = 0; i < parts.Length; i++)
            {
                if (i % 2 == 1)
                {
                    var code = parts[i];
                    int nl = code.IndexOf('\n');
                    if (nl >= 0 && nl < 20 && !code[..nl].Contains(' ')) code = code[(nl + 1)..];   // ```python
                    sb.Append("<pre>").Append(H(code.TrimEnd())).Append("</pre>");
                    continue;
                }
                var t = H(parts[i]);
                t = Regex.Replace(t, @"`([^`\n]+)`", "<code>$1</code>");
                t = Regex.Replace(t, @"\*\*([^*\n]+)\*\*", "<b>$1</b>");
                t = Regex.Replace(t, @"(?m)^#{1,6}\s*(.+)$", "<b>$1</b>");
                t = Regex.Replace(t, @"(?m)^(\s*)[-*]\s+", "$1• ");
                sb.Append(t);
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------
        //  Консоль
        // ------------------------------------------------------------------

        static async Task<string> Shell(bool powershell, string command)
        {
            var psi = powershell
                ? new System.Diagnostics.ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -Command -") { RedirectStandardInput = true }
                : new System.Diagnostics.ProcessStartInfo("cmd.exe", "/d /c " + command);
            psi.UseShellExecute = false; psi.CreateNoWindow = true; psi.RedirectStandardOutput = true; psi.RedirectStandardError = true;
            var enc = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
            psi.StandardOutputEncoding = psi.StandardErrorEncoding = powershell ? Encoding.UTF8 : enc;
            using var p = System.Diagnostics.Process.Start(psi);
            if (powershell)
            {
                await p.StandardInput.WriteLineAsync("[Console]::OutputEncoding=[Text.Encoding]::UTF8; " + command);
                p.StandardInput.Close();
            }
            var outTask = p.StandardOutput.ReadToEndAsync();
            var errTask = p.StandardError.ReadToEndAsync();
            bool done = await Task.Run(() => p.WaitForExit(60000));
            if (!done) { try { p.Kill(true); } catch { } }
            var text = (await outTask + (await errTask is { Length: > 0 } err ? "\n" + err : "")).Trim();
            Toast("Выполнена команда из Telegram", Cut(command, 60));
            if (text.Length > 3500) text = text[..3500] + "\n…";
            return (done ? "" : "⏱ Остановлено через 60 с\n") + (text == "" ? "✓ Выполнено, вывода нет" : "<pre>" + H(text) + "</pre>");
        }
    }
}
