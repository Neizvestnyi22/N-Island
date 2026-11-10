using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NIsland.Core;

namespace NIsland.Services
{
    // =====================================================================
    //  Telegram 3.0: ещё 66 команд (система, сеть, проверки сайтов, утилиты, заметки и задачи, окна,
    //  звук программ, персонаж, тексты на рабочем столе) и общий движок пульта для других мессенджеров
    //  (Discord, Slack, Matrix): те же команды, те же разрешения, ответы — текстом и файлами.
    // =====================================================================
    public partial class TelegramHub
    {
        // ------------------------------------------------------------------
        //  Пульт из другого мессенджера: команда выполняется тем же движком, но ответы
        //  не уходят в Telegram, а собираются здесь и отправляются туда, откуда пришла команда.
        // ------------------------------------------------------------------

        public sealed class RemoteCtx
        {
            public string Source;                       // «Discord», «Slack»…
            public Func<string, bool> Flags;            // разрешения того мессенджера
            public readonly List<string> Out = new();   // ответы (HTML Telegram)
            public readonly List<(string Name, byte[] Data, string Caption)> Files = new();
            public Func<string, (string Name, byte[] Data, string Caption)?, Task> Push;   // поздние ответы (расписание, долгие проверки)
            public volatile bool Done;
            readonly object _gate = new();

            internal void Add(string html, bool replaceLast = false)
            {
                if (string.IsNullOrWhiteSpace(html)) return;
                if (Done) { _ = SafePush(html, null); return; }
                lock (_gate) { if (replaceLast && Out.Count > 0) Out[^1] = html; else Out.Add(html); }
            }

            internal void AddFile(string name, byte[] data, string caption)
            {
                if (Done) { _ = SafePush(caption, (name, data, caption)); return; }
                lock (_gate) Files.Add((name, data, caption));
            }

            async Task SafePush(string html, (string, byte[], string)? file)
            {
                try { if (Push != null) await Push(html, file); } catch (Exception e) { Log.Info("remote push: " + e.Message); }
            }
        }

        static readonly AsyncLocal<RemoteCtx> _remote = new();
        static RemoteCtx Remote => _remote.Value;

        /// <summary>Разрешение: в Telegram — свои флаги, из другого мессенджера — флаги того мессенджера.</summary>
        new bool Flag(string key) => Remote is { } r ? r.Flags(key) : base.Flag(key);

        /// <summary>Команды, которые держат «живое» сообщение или кнопки Telegram — в других мессенджерах не работают.</summary>
        static readonly HashSet<string> TelegramOnly = new() { "live", "dash", "watch", "stream", "pin", "keyboard", "nokeyboard", "start", "kbd",
            "in", "at", "jobs", "widget", "widgets", "pet" };   // расписание выполнялось бы позже уже с разрешениями Telegram, а не этого мессенджера

        /// <summary>Выполнить строку команды так, будто она пришла в Telegram, но ответ вернуть вызывающему.</summary>
        public static async Task<RemoteCtx> RunRemote(string text, string source, Func<string, bool> flags,
                                                       Func<string, (string Name, byte[] Data, string Caption)?, Task> push, bool plainIsAi)
        {
            var hub = Integrations.All.OfType<TelegramHub>().FirstOrDefault() ?? new TelegramHub();
            var ctx = new RemoteCtx { Source = source, Flags = flags, Push = push };
            _remote.Value = ctx;
            try
            {
                text = (text ?? "").Trim();
                string cmd, arg;
                if (text.StartsWith('/') || text.StartsWith('!'))
                {
                    var sp = text.IndexOfAny(new[] { ' ', '\n' });
                    cmd = (sp < 0 ? text[1..] : text[1..sp]).ToLowerInvariant();
                    int at = cmd.IndexOf('@'); if (at > 0) cmd = cmd[..at];
                    arg = sp < 0 ? "" : text[(sp + 1)..].Trim();
                }
                else if (plainIsAi) { cmd = "ai"; arg = text; }
                else { cmd = "say"; arg = text; }

                if (cmd is "help" or "commands")
                    ctx.Add(hub.HelpText().Replace("/menu — всё кнопками\n", "") + "\n\n<i>Кнопки, живая панель и живой экран — только в Telegram</i>");
                else if (TelegramOnly.Contains(cmd))
                    ctx.Add("Это работает только в Telegram: там нужны кнопки и обновляемые сообщения. Попробуй /status или /now");
                else
                {
                    var r = await hub.Command(cmd, arg);
                    if (r?.Html != null) ctx.Add(Legacy(Tr(r.Value.Html)));
                }
            }
            catch (Exception e)
            {
                ctx.Add("⚠️ " + Cut(e.Message, 200));
                Log.Info($"remote {source}: {e.Message}");
            }
            finally { ctx.Done = true; }
            return ctx;
        }

        /// <summary>HTML Telegram → Markdown (Discord) / mrkdwn (Slack) / простой текст.</summary>
        public static string ToMarkdown(string html, bool slack)
        {
            if (string.IsNullOrEmpty(html)) return "";
            string Dec(string x) => System.Net.WebUtility.HtmlDecode(x);
            // Slack сам экранирует только & < > — после раскодирования возвращаем их обратно
            string Esc(string x) => slack ? x.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;") : x;
            var links = new List<string>();
            var s = Regex.Replace(html, @"<a\s+href=""([^""]*)""[^>]*>(.*?)</a>", m =>
            {
                var url = Dec(m.Groups[1].Value);
                var text = Esc(Dec(Regex.Replace(m.Groups[2].Value, "<[^>]+>", "")));
                links.Add(slack ? $"<{url.Replace("|", "%7C").Replace(">", "%3E")}|{text.Replace("|", "¦")}>" : $"[{text.Replace("]", ")")}]({url.Replace(")", "%29")})");
                return "\u0001" + (links.Count - 1) + "\u0002";
            }, RegexOptions.Singleline);
            s = Regex.Replace(s, @"<pre[^>]*>(.*?)</pre>", m => "```\n" + m.Groups[1].Value.Trim('\n') + "\n```", RegexOptions.Singleline);
            s = Regex.Replace(s, @"</?code[^>]*>", "`");
            s = Regex.Replace(s, @"</?(b|strong)>", slack ? "*" : "**");
            s = Regex.Replace(s, @"</?(i|em)>", "_");
            s = Regex.Replace(s, @"</?(s|strike|del)>", "~");
            s = Regex.Replace(s, @"</?u>", slack ? "" : "__");
            s = Regex.Replace(s, @"</?tg-spoiler>", slack ? "" : "||");
            s = Regex.Replace(s, @"<blockquote[^>]*>(.*?)</blockquote>", m => "> " + m.Groups[1].Value.Replace("\n", "\n> "), RegexOptions.Singleline);
            s = Regex.Replace(s, "<[^>]+>", "");
            s = Esc(Dec(s));
            s = Regex.Replace(s, "\u0001(\\d+)\u0002", m => links[int.Parse(m.Groups[1].Value)]);
            return s.Trim();
        }

        /// <summary>HTML Telegram → безопасный HTML Matrix (только разрешённые теги).</summary>
        public static string ToMatrixHtml(string html)
        {
            var s = Regex.Replace(html ?? "", @"<(/?)(tg-spoiler)>", "<$1span>");
            s = Regex.Replace(s, @"<(?!/?(b|i|u|s|code|pre|a|blockquote|span)\b)[^>]*>", "");
            s = Regex.Replace(s, @"<a\s+href=""(?!https?://)[^""]*""", "<a href=\"#\"");   // только http(s)-ссылки
            return s.Replace("\n", "<br>");
        }

        public static string ToPlain(string html) => System.Net.WebUtility.HtmlDecode(Regex.Replace(html ?? "", "<[^>]+>", "")).Trim();

        // ------------------------------------------------------------------
        //  Команды 3.0
        // ------------------------------------------------------------------

        static readonly (string Cmd, string Desc)[] Commands3 =
        {
            ("cpu", "Процессор и кто его грузит"), ("mem", "Память по программам"), ("gpu", "Видеокарта"), ("idle", "Сколько ПК без дела"),
            ("active", "Активное окно"), ("monitors", "Мониторы"), ("ports", "Открытые порты"), ("conns", "Кто в сети"), ("usb", "Флешки и диски"),
            ("eject", "Извлечь флешку: /eject E"), ("bin", "Корзина"), ("temp", "Временные файлы"), ("cleantemp", "Очистить временные файлы"),
            ("boot", "Когда включился ПК"), ("pending", "Ждёт ли Windows перезагрузки"), ("update", "Обновление N-Island"), ("service", "Служба: /service spooler"),
            ("bathealth", "Износ батареи"), ("diskhealth", "Здоровье дисков"), ("flushdns", "Сбросить кэш DNS"), ("netusage", "Скорость сети сейчас"),
            ("wifiscan", "Сети Wi-Fi вокруг"), ("lan", "Устройства в сети"), ("http", "Проверить сайт: /http site.com"), ("ssl", "Сертификат сайта"),
            ("whois", "Кто владелец домена"), ("dig", "DNS-записи домена"), ("ipinfo", "Об IP-адресе"), ("link", "Проверить ссылку на фишинг"),
            ("trace", "Маршрут до сайта"), ("port", "Открыт ли порт: /port host 443"), ("unshort", "Раскрыть короткую ссылку"),
            ("conv", "Валюта: /conv 100 usd uah"), ("wiki", "Википедия: /wiki Киев"), ("clock", "Время в городах"), ("days", "Дней до даты: /days 31.12"),
            ("moon", "Фаза луны"), ("sun", "Восход и закат"), ("tomorrow", "Погода на завтра"), ("password", "Надёжный пароль"), ("uuid", "UUID"),
            ("hash", "SHA-256 и MD5 текста"), ("b64", "Base64: /b64 текст · /b64 -d …"), ("roll", "Кубик: /roll 100"), ("coin", "Монетка"),
            ("sw", "Секундомер: /sw · lap · reset"), ("pomo", "Помодоро: /pomo · stop · skip"), ("sleeptimer", "Музыка уснёт: /sleeptimer 30"),
            ("water", "Выпил воды: /water 250"), ("notes", "Заметки"), ("todos", "Список задач"), ("done", "Задача сделана: /done 2"),
            ("deltodo", "Удалить задачу: /deltodo 2"), ("notif", "Последние уведомления"), ("clipclear", "Очистить буфер обмена"),
            ("close", "Закрыть активное окно"), ("maximize", "Развернуть активное окно"), ("minimize", "Свернуть активное окно"),
            ("minall", "Свернуть все окна"), ("appvol", "Громкость программы: /appvol spotify 40"), ("volup", "Громче"), ("voldown", "Тише"),
            ("like", "Трек в избранное"), ("desklyrics", "Текст песни на рабочем столе"), ("viz", "Визуализатор на весь экран"),
            ("mascot", "Персонаж: /mascot crown · party"), ("icons", "Значки рабочего стола"), ("restartapp", "Перезапустить N-Island"),
        };

        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [StructLayout(LayoutKind.Sequential)] struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }
        [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        static readonly Regex HostRx = new(@"^[A-Za-z0-9\.\-:\[\]]{1,253}$", RegexOptions.Compiled);

        /// <summary>Хост из «site.com», «https://site.com/путь» — или null, если там что-то лишнее (защита от подстановки аргументов).</summary>
        static string CleanHost(string s)
        {
            s = (s ?? "").Trim();
            var m = Regex.Match(s, @"^(?:[a-z]+://)?([^/\s?#]+)", RegexOptions.IgnoreCase);
            var host = m.Success ? m.Groups[1].Value : "";
            int at = host.LastIndexOf('@'); if (at >= 0) host = host[(at + 1)..];
            return HostRx.IsMatch(host) && !host.StartsWith('-') ? host : null;
        }

        static string Kv(IEnumerable<(string K, string V)> items, int max = 14) =>
            string.Join("\n", items.Take(max).Select(i => $"<b>{H(T(i.K))}</b>: {H(Cut(i.V, 200))}"));

        static string Mb(long bytes) => Maint.Size(bytes);

        async Task<(string Html, object Keys)?> Command3(string cmd, string arg)
        {
            var m = MediaService.I;
            string no;
            switch (cmd)
            {
                // ---------------- Система ----------------
                case "cpu":
                {
                    int cpu = SystemService.I.CpuPercent();
                    var top = await Task.Run(() => TopCpu(6));
                    var sb = new StringBuilder($"⚙️ <b>Процессор</b>\n<code>{Bar(cpu / 100.0)} {cpu}%</code>\n");
                    foreach (var (name, pct) in top) sb.Append($"\n{H(name)} — {pct:0.#}%");
                    return (sb.ToString(), null);
                }
                case "mem":
                {
                    var top = await Task.Run(() => Maint.TopProcesses(10));
                    long total = Maint.TotalRam(), free = Maint.AvailableRam();
                    var sb = new StringBuilder($"🧠 <b>Память</b>: занято {Mb(total - free)} из {Mb(total)}\n<code>{Bar(total > 0 ? 1 - (double)free / total : 0)}</code>\n");
                    foreach (var p in top) sb.Append($"\n{H(p.Name)}{(p.Count > 1 ? " ×" + p.Count : "")} — {Mb(p.Bytes)}");
                    return (sb.ToString(), null);
                }
                case "gpu":
                    return (await Task.Run(() =>
                    {
                        var sb = new StringBuilder("🎮 <b>Видеокарта</b>");
                        try
                        {
                            using var q = new System.Management.ManagementObjectSearcher("SELECT Name, DriverVersion, DriverDate, CurrentHorizontalResolution, CurrentVerticalResolution, CurrentRefreshRate FROM Win32_VideoController");
                            foreach (System.Management.ManagementObject o in q.Get())
                            {
                                sb.Append($"\n\n<b>{H(o["Name"]?.ToString())}</b>\nдрайвер {H(o["DriverVersion"]?.ToString())}");
                                if (o["CurrentHorizontalResolution"] != null) sb.Append($"\n{o["CurrentHorizontalResolution"]}×{o["CurrentVerticalResolution"]} · {o["CurrentRefreshRate"]} Гц");
                            }
                        }
                        catch (Exception e) { sb.Append("\n" + H(e.Message)); }
                        return sb.ToString();
                    }), null);
                case "idle":
                {
                    var li = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
                    var idle = GetLastInputInfo(ref li) ? TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - li.dwTime)) : TimeSpan.Zero;
                    var t = idle.TotalMinutes < 1 ? "ПК используют прямо сейчас" : $"Без дела {(idle.TotalHours >= 1 ? (int)idle.TotalHours + " ч " : "")}{idle.Minutes} мин";
                    return ($"⌨️ {t}", null);
                }
                case "active":
                {
                    if ((no = Need("apps")) != null) return (no, null);
                    var h = GetForegroundWindow();
                    GetWindowThreadProcessId(h, out var pid);
                    string proc = "";
                    try { using var p = System.Diagnostics.Process.GetProcessById((int)pid); proc = p.ProcessName; } catch { }
                    return ($"🪟 <b>{H(PcControl.ActiveTitle())}</b>\n{H(proc)}", null);
                }
                case "monitors":
                {
                    var sb = new StringBuilder("🖥 <b>Мониторы</b>");
                    int i = 1;
                    foreach (var s in System.Windows.Forms.Screen.AllScreens)
                        sb.Append($"\n{i++}. {s.Bounds.Width}×{s.Bounds.Height}{(s.Primary ? " · основной" : "")}");
                    return (sb.ToString(), null);
                }
                case "ports":
                    return (await Task.Run(ListeningPorts), null);
                case "conns":
                {
                    var list = await Laptop.Connections();
                    if (list.Count == 0) return ("🌐 Сейчас никто не в сети", null);
                    return ("🌐 <b>Программы в сети</b>\n" + string.Join("\n", list.Take(15).Select(u => $"{H(u.Process)} — {u.Connections} · <code>{H(Cut(u.Sample, 40))}</code>")), null);
                }
                case "usb":
                {
                    var drives = DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType is DriveType.Removable or DriveType.CDRom).ToList();
                    if (drives.Count == 0) return ("💾 Флешек не подключено", null);
                    return ("💾 <b>Съёмные диски</b>\n" + string.Join("\n", drives.Select(d => $"{H(d.Name)} {H(Drives.Label(d))} — свободно {H(Drives.Free(d))}")) + "\n\nИзвлечь: /eject E", null);
                }
                case "eject":
                {
                    if ((no = Need("files")) != null) return (no, null);
                    var letter = Regex.Match(arg, @"^\s*([A-Za-z])\s*:?\\?\s*$");
                    if (!letter.Success) return ("Напиши так: /eject E", null);
                    var root = letter.Groups[1].Value.ToUpperInvariant() + ":\\";
                    var d = DriveInfo.GetDrives().FirstOrDefault(x => x.Name.Equals(root, StringComparison.OrdinalIgnoreCase));
                    if (d == null || d.DriveType is not (DriveType.Removable or DriveType.CDRom)) return ("Это не съёмный диск", null);
                    await Ui(() =>
                    {
                        var shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
                        dynamic sh = shell;
                        sh.NameSpace(17).ParseName(root).InvokeVerb("Eject");
                    });
                    return ($"⏏️ Извлекаю {H(root)} — дождись, пока Windows скажет, что можно вынимать", null);
                }
                case "bin":
                {
                    var (bytes, items) = Maint.RecycleBin();
                    return ($"🗑 В корзине {items} объектов · {Mb(bytes)}\nОчистить можно на ПК", null);
                }
                case "temp":
                {
                    var (bytes, files) = await Maint.ScanTemp();
                    return ($"🧹 Временных файлов: {files} · {Mb(bytes)}\nОчистить: /cleantemp", null);
                }
                case "cleantemp":
                {
                    if ((no = Need("control")) != null) return (no, null);
                    var (freed, deleted, skipped) = await Maint.CleanTemp();
                    return ($"🧹 Удалено {deleted} файлов · освобождено {Mb(freed)}" + (skipped > 0 ? $"\nПропущено занятых: {skipped}" : ""), null);
                }
                case "boot":
                {
                    var up = TimeSpan.FromMilliseconds(Environment.TickCount64);
                    return ($"⏻ ПК включён {DateTime.Now - up:dd.MM HH:mm}\nРаботает {(up.TotalDays >= 1 ? (int)up.TotalDays + " дн " : "")}{up.Hours} ч {up.Minutes} мин", null);
                }
                case "pending":
                    return (PendingReboot() is { Count: > 0 } why ? "🔁 <b>Windows ждёт перезагрузки</b>\n" + string.Join("\n", why.Select(w => "• " + H(T(w)))) : "✓ Перезагрузка не нужна", null);
                case "update":
                {
                    var (state, info, err) = await Updater.Check();
                    return (state switch
                    {
                        UpdateState.Available => $"⬆️ Есть N-Island {H(info.Version)} (у тебя {AppInfo.Version})\nОбновить: на ПК — Настройки → О программе → Обновить\n{H(info.PageUrl)}",
                        UpdateState.UpToDate => $"✓ У тебя последняя версия N-Island {AppInfo.Version}",
                        _ => "⚠️ Не удалось проверить: " + H(err ?? "нет релизов"),
                    }, null);
                }
                case "service":
                {
                    if (!Regex.IsMatch(arg, @"^[\w\.\-]{1,80}$")) return ("Напиши так: /service spooler", null);
                    var outp = await Task.Run(() => Maint.RunCmd("sc", "query \"" + arg + "\""));
                    var st = Regex.Match(outp ?? "", @"STATE\s*:\s*\d+\s+(\w+)");
                    return (st.Success ? $"⚙️ Служба <b>{H(arg)}</b>: {H(st.Groups[1].Value)}" : "Нет такой службы", null);
                }
                case "bathealth":
                {
                    if (!SystemService.I.HasBattery) return ("🔌 У этого ПК нет батареи", null);
                    var b = await Maint.Battery();
                    if (b.Design <= 0) return ("🔋 Windows не сообщает ёмкость батареи", null);
                    return ($"🔋 <b>Здоровье батареи</b>: {b.Percent}%\n<code>{Bar(b.Percent / 100.0)}</code>\nЁмкость {b.Full / 1000.0:0.#} из {b.Design / 1000.0:0.#} Вт·ч" + (b.Cycles > 0 ? $" · циклов {b.Cycles}" : ""), null);
                }
                case "diskhealth":
                {
                    var l = await Maint.DiskHealth();
                    return ("💽 <b>Диски</b>\n" + (l.Count == 0 ? "Нет данных" : string.Join("\n", l.Select(H))), null);
                }
                case "flushdns":
                    if ((no = Need("control")) != null) return (no, null);
                    return (Maint.FlushDns() ? "✓ Кэш DNS сброшен" : "Не удалось сбросить кэш DNS", null);
                case "netusage":
                    NetSpeed.Sample();
                    await Task.Delay(1100);
                    NetSpeed.Sample();
                    return ($"📶 ↓ {NetSpeed.Fmt(NetSpeed.Down)} · ↑ {NetSpeed.Fmt(NetSpeed.Up)}", null);
                case "wifiscan":
                {
                    var aps = await WifiTools.Nearby();
                    if (aps.Count == 0) return ("📶 Сетей не видно (Wi-Fi выключен?)", null);
                    return ("📶 <b>Сети вокруг</b>\n" + string.Join("\n", aps.OrderByDescending(a => a.Signal).Take(12)
                        .Select(a => $"{WifiTools.Bars(a.Signal)} {(a.Current ? "<b>" : "")}{H(a.Ssid == "" ? "(скрытая)" : a.Ssid)}{(a.Current ? "</b>" : "")} · {a.Band} к{a.Channel}")), null);
                }
                case "lan":
                {
                    if (Remote == null) await Message("🔎 Ищу устройства в сети… (до минуты)");
                    var devs = await WifiTools.Devices();
                    return ($"🏠 <b>Устройства в сети</b>: {devs.Count}\n" + string.Join("\n", devs.Take(25).Select(d => $"<code>{H(d.Ip)}</code> {H(d.Name != "" ? d.Name : d.Kind)}")), null);
                }

                // ---------------- Сайты и сеть ----------------
                case "http":
                {
                    var url = arg.Trim();
                    if (url == "") return ("Напиши так: /http example.com", null);
                    if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) url = "https://" + url;
                    if (CleanHost(url) == null) return ("Странный адрес", null);
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    try
                    {
                        using var resp = await Integrations.Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                        sw.Stop();
                        var server = resp.Headers.Server?.ToString();
                        return ($"{((int)resp.StatusCode < 400 ? "🟢" : "🔴")} <b>{(int)resp.StatusCode} {H(resp.ReasonPhrase)}</b> · {sw.ElapsedMilliseconds} мс\n{H(resp.RequestMessage?.RequestUri?.ToString())}" +
                                (string.IsNullOrEmpty(server) ? "" : "\nсервер: " + H(server)), null);
                    }
                    catch (Exception e) { return ("🔴 Сайт не отвечает: " + H(Cut(e.Message, 160)), null); }
                }
                case "ssl": case "whois": case "dig":
                {
                    var host = CleanHost(arg);
                    if (host == null) return ($"Напиши так: /{cmd} example.com", null);
                    var list = cmd switch { "ssl" => await Osint.Certificate(host), "whois" => await Osint.Whois(host), _ => await Osint.DnsRecords(host) };
                    var title = cmd switch { "ssl" => "🔐 Сертификат", "whois" => "🪪 Домен", _ => "🧭 DNS" };
                    return ($"{title} <b>{H(host)}</b>\n" + (list.Count == 0 ? "Нет данных" : Kv(list, 20)), null);
                }
                case "ipinfo":
                {
                    var ip = arg.Trim();
                    if (!System.Net.IPAddress.TryParse(ip, out _)) return ("Напиши так: /ipinfo 8.8.8.8", null);
                    return ($"🌍 <b>{H(ip)}</b>\n" + Kv(await Osint.Ip(ip)), null);
                }
                case "link":
                {
                    if (arg == "") return ("Напиши так: /link https://…", null);
                    var r = await Osint.CheckLink(arg.Trim());
                    var icon = r.Risk >= 60 ? "🔴" : r.Risk >= 25 ? "🟡" : "🟢";
                    return ($"{icon} <b>{H(T(r.Verdict))}</b> · риск {r.Risk}/100\n{H(r.Final)}\n" + string.Join("\n", r.Signals.Take(8).Select(s => (s.Bad ? "⚠️ " : "✓ ") + H(T(s.Text)))), null);
                }
                case "trace":
                {
                    var host = CleanHost(arg);
                    if (host == null) return ("Напиши так: /trace google.com", null);
                    if (Remote == null) await Message("🛰 Строю маршрут… (до 40 с)");
                    var outp = await Task.Run(() => Maint.RunCmd("tracert", "-d -h 15 -w 600 " + host));
                    var hops = (outp ?? "").Split('\n').Where(l => Regex.IsMatch(l, @"^\s*\d+\s")).Select(l => Regex.Replace(l.Trim(), @"\s{2,}", " "));
                    return ($"🛰 <b>Маршрут до {H(host)}</b>\n<pre>{H(string.Join("\n", hops))}</pre>", null);
                }
                case "port":
                {
                    var mm = Regex.Match(arg, @"^\s*(\S+?)(?:[\s:]+(\d{1,5}))\s*$");
                    var host = mm.Success ? CleanHost(mm.Groups[1].Value) : null;
                    if (host == null || !int.TryParse(mm.Groups[2].Value, out var port) || port is < 1 or > 65535) return ("Напиши так: /port example.com 443", null);
                    using var tcp = new TcpClient();
                    using var cts = new CancellationTokenSource(4000);
                    try { await tcp.ConnectAsync(host, port, cts.Token); return ($"🟢 Порт {port} на {H(host)} открыт", null); }
                    catch { return ($"🔴 Порт {port} на {H(host)} закрыт или не отвечает", null); }
                }
                case "unshort":
                {
                    if (arg == "") return ("Напиши так: /unshort https://bit.ly/…", null);
                    var chain = await Osint.Unshorten(arg.Trim());
                    return ("🔗 <b>Куда ведёт ссылка</b>\n" + string.Join("\n↓\n", chain.Select(H)), null);
                }

                // ---------------- Утилиты ----------------
                case "conv":
                {
                    // «100 usd uah» → «100 usd to uah»
                    var p = Currency.Parse(Regex.Replace(arg, @"^(\s*[\d.,]+\s*[^\d\s]+)\s+([^\d\s]+)\s*$", "$1 to $2"));
                    if (p == null) return ("Напиши так: /conv 100 usd uah", null);
                    var v = await Currency.Convert(p.Value.Amount, p.Value.From, p.Value.To);
                    return (v == null ? "Курс сейчас недоступен" : $"💱 {Currency.Fmt(p.Value.Amount, p.Value.From)} = <b>{Currency.Fmt(v.Value, p.Value.To)}</b>", null);
                }
                case "wiki":
                    return (await Wiki(arg), null);
                case "clock":
                {
                    if (arg != "")
                    {
                        var q = arg.Trim();
                        var tz = TimeZoneInfo.GetSystemTimeZones().FirstOrDefault(z => z.Id.Contains(q, StringComparison.OrdinalIgnoreCase) || z.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase));
                        if (tz == null) return ("Не нашёл такой город в часовых поясах Windows", null);
                        return ($"🕐 <b>{TimeZoneInfo.ConvertTime(DateTime.Now, tz):HH:mm}</b> · {H(tz.DisplayName)}", null);
                    }
                    var list = WeatherService.WorldClocks();
                    return ($"🕐 <b>{DateTime.Now:HH:mm}</b> тут\n" + string.Join("\n", list.Select(c => $"{c.Time:HH:mm} — {H(c.Name)}")) + "\n\nДругой город: /clock Tokyo", null);
                }
                case "days":
                {
                    var d = ParseDate(arg);
                    if (d == null) return ("Напиши так: /days 31.12 или /days 2027-01-01", null);
                    int n = (int)Math.Round((d.Value.Date - DateTime.Today).TotalDays);
                    return (n == 0 ? "📅 Это сегодня!" : n > 0 ? $"📅 До {d:dd.MM.yyyy} — <b>{n}</b> дн." : $"📅 С {d:dd.MM.yyyy} прошло <b>{-n}</b> дн.", null);
                }
                case "moon":
                {
                    var (icon, name, lit) = WeatherService.Moon(DateTime.Now);
                    return ($"{icon} {H(T(name))} · освещено {lit}%", null);
                }
                case "sun":
                {
                    var w = await WeatherService.Get();
                    if (w?.Sunrise == null) return ("Погода сейчас недоступна", null);
                    var day = (w.Sunset ?? w.Sunrise.Value) - w.Sunrise.Value;
                    return ($"🌅 Восход {w.Sunrise:HH:mm} · 🌇 закат {w.Sunset:HH:mm}\nСветовой день {(int)day.TotalHours} ч {day.Minutes} мин · {H(w.City)}", null);
                }
                case "tomorrow":
                {
                    var w = await WeatherService.Get();
                    return (w == null ? "Погода сейчас недоступна" : "🌦 <b>Завтра</b>: " + H(T(WeatherService.Tomorrow(w) ?? "нет данных")), null);
                }
                case "password":
                {
                    int len = int.TryParse(arg, out var pl) ? Math.Clamp(pl, 8, 64) : 20;
                    const string chars = "abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789!@#$%^&*-_=+?";
                    var pw = new string(Enumerable.Range(0, len).Select(_ => chars[RandomNumberGenerator.GetInt32(chars.Length)]).ToArray());
                    return ($"🔑 <tg-spoiler><code>{H(pw)}</code></tg-spoiler>\n<i>Создан на ПК случайно; удали сообщение, когда сохранишь</i>", null);
                }
                case "uuid": return ($"<code>{Guid.NewGuid()}</code>", null);
                case "hash":
                {
                    if (arg == "") return ("Напиши так: /hash текст", null);
                    var bytes = Encoding.UTF8.GetBytes(arg);
                    return ($"SHA-256 <code>{Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()}</code>\nMD5 <code>{Convert.ToHexString(MD5.HashData(bytes)).ToLowerInvariant()}</code>", null);
                }
                case "b64":
                {
                    if (arg.StartsWith("-d ") || arg.StartsWith("-d\n"))
                    {
                        try { return ($"<code>{H(Encoding.UTF8.GetString(Convert.FromBase64String(arg[3..].Trim())))}</code>", null); }
                        catch { return ("Это не Base64", null); }
                    }
                    return (arg == "" ? "Напиши так: /b64 текст · /b64 -d SGVsbG8=" : $"<code>{Convert.ToBase64String(Encoding.UTF8.GetBytes(arg))}</code>", null);
                }
                case "roll":
                {
                    var nums = Regex.Matches(arg, @"-?\d+").Select(x => long.TryParse(x.Value, out var v) ? v : 0).ToList();
                    long lo = nums.Count >= 2 ? Math.Min(nums[0], nums[1]) : 1, hi = nums.Count >= 2 ? Math.Max(nums[0], nums[1]) : nums.Count == 1 ? Math.Max(1, nums[0]) : 6;
                    hi = Math.Min(hi, int.MaxValue - 1); lo = Math.Max(lo, int.MinValue);
                    return ($"🎲 <b>{RandomNumberGenerator.GetInt32((int)lo, (int)hi + 1)}</b> <i>({lo}–{hi})</i>", null);
                }
                case "coin": return (RandomNumberGenerator.GetInt32(2) == 0 ? "🪙 Орёл" : "🪙 Решка", null);
                case "sw":
                {
                    var t = TimerService.I;
                    var a = arg.ToLowerInvariant();
                    await Ui(() => { if (a is "lap" or "круг") t.SwLap(); else if (a is "reset" or "сброс") t.SwReset(); else if (a is "" or "start" or "stop" or "старт" or "стоп") t.SwToggle(); });
                    var el = t.SwElapsed;
                    return ($"⏱ {(t.SwRunning ? "идёт" : "стоит")} · <b>{(int)el.TotalHours:00}:{el.Minutes:00}:{el.Seconds:00}</b>" +
                            (t.Laps.Count > 0 ? "\n" + string.Join("\n", t.Laps.Take(5).Select((l, i) => $"круг {t.Laps.Count - i}: {(int)l.TotalMinutes:00}:{l.Seconds:00}")) : ""), null);
                }
                case "pomo":
                {
                    if ((no = Need("control")) != null) return (no, null);
                    var p = PomodoroService.I;
                    var a = arg.ToLowerInvariant();
                    await Ui(() => { if (a is "stop" or "reset" or "стоп") p.Reset(); else if (a is "skip" or "дальше") p.Skip(); else if (a is "status" or "?") { } else p.Toggle(); });
                    var phase = p.Phase switch { PomoPhase.Focus => "фокус", PomoPhase.Short => "короткий перерыв", _ => "длинный перерыв" };
                    return ($"🍅 {(p.Running ? "идёт" : "на паузе")}: {phase} · осталось <b>{(int)p.Remaining.TotalMinutes}:{p.Remaining.Seconds:00}</b> · раунд {p.Round + 1}", null);
                }
                case "sleeptimer":
                {
                    if ((no = Need("control")) != null) return (no, null);
                    int min = int.TryParse(arg, out var sm) ? Math.Clamp(sm, 0, 600) : 30;
                    await Ui(() => TimerService.I.SetSleep(min));
                    return (min == 0 ? "😴 Таймер сна выключен" : $"😴 Музыка встанет на паузу через {min} мин", null);
                }
                case "water":
                {
                    int ml = int.TryParse(arg, out var wm) ? Math.Clamp(wm, 50, 2000) : 250;
                    await Ui(() => Water.Drink(ml));
                    int today = Water.Hourly().Sum();
                    return ($"💧 +{ml} мл · сегодня {today} мл", null);
                }

                // ---------------- Заметки, задачи, уведомления ----------------
                case "notes":
                {
                    var n = Store.Data?.Notes ?? "";
                    return (n.Trim() == "" ? "📝 Заметок нет. Добавить: /note текст" : "📝 <b>Заметки</b>\n" + H(Cut(n, 3500)), null);
                }
                case "todos":
                    return (TodosText(), null);
                case "done": case "deltodo":
                {
                    if ((no = Need("control")) != null) return (no, null);
                    var list = Store.Data?.Todos;
                    if (list == null || !int.TryParse(arg, out var idx) || idx < 1 || idx > list.Count) return ($"Напиши так: /{cmd} 2 (номер из /todos)", null);
                    await Ui(() =>
                    {
                        var it = list[idx - 1];
                        if (cmd == "done") it.Done = !it.Done; else list.Remove(it);
                        Store.SaveData();
                    });
                    return (TodosText(), null);
                }
                case "notif":
                {
                    if ((no = Need("control")) != null) return (no, null);
                    var items = await Ui(() => NotificationLog.Items.Take(10).ToList());
                    if (items.Count == 0) return ("🔔 Уведомлений нет", null);
                    return ("🔔 <b>Последние уведомления</b>\n" + string.Join("\n\n", items.Select(i => $"<b>{H(i.App)}</b> · {i.Time:HH:mm}\n{H(Cut(i.Title, 80))}" + (string.IsNullOrEmpty(i.Body) ? "" : "\n" + H(Cut(i.Body, 160))))), null);
                }
                case "clipclear":
                    if ((no = Need("control")) != null) return (no, null);
                    await Ui(() => { try { System.Windows.Clipboard.Clear(); } catch { } });
                    return ("📋 Буфер обмена очищен", null);

                // ---------------- Окна ----------------
                case "close": case "maximize": case "minimize":
                {
                    if ((no = Need("apps")) != null) return (no, null);
                    var h = GetForegroundWindow();
                    var title = PcControl.ActiveTitle();
                    var r = PcControl.WindowAction(h, cmd == "close" ? 'c' : cmd == "maximize" ? 'x' : 'm');
                    return ($"🪟 {H(T(r ?? ""))}: {H(Cut(title, 80))}", null);
                }
                case "minall":
                    if ((no = Need("apps")) != null) return (no, null);
                    PcControl.Hotkey("win+d");
                    return ("🪟 Все окна свёрнуты (повтори — вернутся)", null);

                // ---------------- Звук и музыка ----------------
                case "appvol":
                {
                    if ((no = Need("control")) != null) return (no, null);
                    var apps = AudioService.I.Apps();
                    var mm = Regex.Match(arg, @"^(.+?)\s+(\d{1,3})\s*%?$");
                    if (!mm.Success)
                        return ("🎚 <b>Громкость программ</b>\n" + (apps.Count == 0 ? "Сейчас ничего не звучит" : string.Join("\n", apps.Select(x => $"{H(x.Name)} — {(x.Muted ? "выкл" : Math.Round(x.Volume * 100) + "%")}"))) +
                                "\n\nПоменять: /appvol spotify 40", null);
                    var name = mm.Groups[1].Value.Trim();
                    var app = apps.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? apps.FirstOrDefault(x => x.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
                    if (app == null) return ("Такая программа сейчас не звучит. Список: /appvol", null);
                    int v = Math.Clamp(int.Parse(mm.Groups[2].Value), 0, 100);
                    AudioService.I.SetAppVolume(app.Name, v / 100f, v == 0 ? true : false);
                    return ($"🎚 {H(app.Name)} — {v}%", null);
                }
                case "volup": case "voldown":
                {
                    if ((no = Need("control")) != null) return (no, null);
                    int step = int.TryParse(arg, out var st) ? Math.Clamp(st, 1, 50) : 10;
                    var a = AudioService.I;
                    double nv = await Ui(() => { a.RefreshLevels(); a.SetVolume(Math.Clamp(a.Volume + (cmd == "volup" ? step : -step) / 100.0, 0, 1)); return a.Volume; });
                    return ($"🔊 {Math.Round(nv * 100)}%\n<code>{Bar(nv)}</code>", null);
                }
                case "like":
                    if ((no = Need("control")) != null) return (no, null);
                    if (!m.HasSession) return ("Сейчас ничего не играет", null);
                    return (await Ui(() => m.ToggleFavorite()) ? $"💖 В избранном: {H(m.TrackLine)}" : "Убрано из избранного", null);
                case "desklyrics":
                {
                    if ((no = Need("control")) != null) return (no, null);
                    bool on = arg.ToLowerInvariant() switch { "on" or "1" or "вкл" => true, "off" or "0" or "выкл" => false, _ => !Store.Settings.DesktopLyrics };
                    await Ui(() => Store.Settings.DesktopLyrics = on);
                    return (on ? "🎤 Текст песни на рабочем столе включён" : "🎤 Текст на рабочем столе выключен", null);
                }
                case "viz":
                    if ((no = Need("control")) != null) return (no, null);
                    await Ui(VisualizerWindow.Open);
                    return ("🌈 Визуализатор открыт на весь экран (Esc — закрыть)", null);

                // ---------------- Остров ----------------
                case "mascot":
                {
                    var s = Store.Settings;
                    var a = arg.Trim().ToLowerInvariant();
                    if (a == "")
                        return ($"🫧 Персонаж: <b>{H(T(Controls.Mascot.Skins.FirstOrDefault(k => k.Key == s.Mascot).Name ?? s.Mascot))}</b>, аксессуар {H(s.MascotHat)}\n\n" +
                                "Облик: " + string.Join(", ", Controls.Mascot.Skins.Select(k => k.Key)) +
                                "\nАксессуар: " + string.Join(", ", Controls.Mascot.Hats.Select(k => k.Key)) +
                                "\nЭмоция: " + string.Join(", ", Controls.Mascot.MoodNames.Select(k => k.Key)), null);
                    if ((no = Need("control")) != null) return (no, null);
                    if (Controls.Mascot.Hats.Any(k => k.Key == a)) { await Ui(() => s.MascotHat = a); return ($"🫧 Аксессуар: {H(a)}", null); }
                    if (Controls.Mascot.Skins.Any(k => k.Key == a)) { await Ui(() => s.Mascot = a); return ($"🫧 Облик: {H(a)}", null); }
                    if (Controls.Mascot.MoodNames.Any(k => k.Key == a)) { App.Island?.MascotReact(a, 4); return ($"🫧 Персонаж: {H(T(Controls.Mascot.MoodNames.First(k => k.Key == a).Name))}", null); }
                    return ("Не знаю такого. /mascot — список", null);
                }
                case "icons":
                    if ((no = Need("control")) != null) return (no, null);
                    await Ui(Laptop.ToggleDesktopIcons);
                    return ("🖼 Значки рабочего стола переключены", null);
                case "restartapp":
                    if ((no = Need("control")) != null) return (no, null);
                    _ = Task.Delay(1500).ContinueWith(_ => OnUi(() => App.Restart()));
                    return ("🔄 N-Island перезапускается…", null);
            }
            return null;
        }

        static List<(string Name, double Pct)> TopCpu(int n)
        {
            var first = new Dictionary<int, (string, TimeSpan)>();
            foreach (var p in System.Diagnostics.Process.GetProcesses())
            {
                try { first[p.Id] = (p.ProcessName, p.TotalProcessorTime); } catch { }
                finally { p.Dispose(); }
            }
            Thread.Sleep(700);
            var res = new Dictionary<string, double>();
            foreach (var p in System.Diagnostics.Process.GetProcesses())
            {
                try
                {
                    if (first.TryGetValue(p.Id, out var f) && p.Id != 0)
                    {
                        double pct = (p.TotalProcessorTime - f.Item2).TotalMilliseconds / 700.0 / Environment.ProcessorCount * 100;
                        res[f.Item1] = res.GetValueOrDefault(f.Item1) + pct;
                    }
                }
                catch { }
                finally { p.Dispose(); }
            }
            return res.Where(kv => kv.Value >= 0.1).OrderByDescending(kv => kv.Value).Take(n).Select(kv => (kv.Key, kv.Value)).ToList();
        }

        static string ListeningPorts()
        {
            var outp = Maint.RunCmd("netstat", "-ano -p tcp") ?? "";
            var names = new Dictionary<int, string>();
            string Name(int pid)
            {
                if (names.TryGetValue(pid, out var n)) return n;
                try { using var p = System.Diagnostics.Process.GetProcessById(pid); n = p.ProcessName; } catch { n = "pid " + pid; }
                return names[pid] = n;
            }
            var rows = outp.Split('\n').Select(l => Regex.Match(l, @"^\s*TCP\s+(\S+):(\d+)\s+\S+\s+LISTENING\s+(\d+)", RegexOptions.IgnoreCase)).Where(x => x.Success)
                .Select(x => (Addr: x.Groups[1].Value, Port: int.Parse(x.Groups[2].Value), Pid: int.Parse(x.Groups[3].Value)))
                .GroupBy(r => r.Port).Select(g => g.First()).OrderBy(r => r.Port).Take(40).ToList();
            if (rows.Count == 0) return "🔌 Открытых портов нет";
            return "🔌 <b>Открытые порты</b> <i>(🌐 — доступен из сети)</i>\n" +
                   string.Join("\n", rows.Select(r => $"{(r.Addr is "127.0.0.1" or "[::1]" ? "🏠" : "🌐")} <code>{r.Port}</code> {H(Name(r.Pid))}"));
        }

        static List<string> PendingReboot()
        {
            var why = new List<string>();
            try
            {
                using var hklm = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64);
                if (hklm.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired") != null) why.Add("Центр обновления Windows установил обновления");
                if (hklm.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending") != null) why.Add("Компоненты Windows ждут перезагрузки");
                using var sm = hklm.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager");
                if (sm?.GetValue("PendingFileRenameOperations") is string[] { Length: > 0 }) why.Add("Программы ждут замены файлов");
            }
            catch { }
            return why;
        }

        static DateTime? ParseDate(string s)
        {
            s = (s ?? "").Trim();
            string[] fmts = { "d.M.yyyy", "d.M.yy", "d.M", "yyyy-M-d", "d/M/yyyy", "d/M" };
            if (DateTime.TryParseExact(s, fmts, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            {
                if (!Regex.IsMatch(s, @"\d{2,4}$") || Regex.IsMatch(s, @"^\d{1,2}[./]\d{1,2}$"))
                    if (d.Date < DateTime.Today) d = d.AddYears(1);   // «31.12» — ближайшее 31 декабря
                return d;
            }
            return null;
        }

        static string TodosText()
        {
            var list = Store.Data?.Todos;
            if (list == null || list.Count == 0) return "✅ Задач нет. Добавить: /todo текст";
            return "✅ <b>Задачи</b>\n" + string.Join("\n", list.Take(40).Select((t, i) => $"{i + 1}. {(t.Done ? "☑️ <s>" + H(t.Text) + "</s>" : "⬜ " + H(t.Text))}")) +
                   "\n\nОтметить: /done 2 · удалить: /deltodo 2";
        }

        static async Task<string> Wiki(string q)
        {
            if (string.IsNullOrWhiteSpace(q)) return "Напиши так: /wiki Киев";
            var lang = I18n.Current is "uk" or "en" ? I18n.Current : "ru";
            foreach (var l in new[] { lang, "en" }.Distinct())
            {
                try
                {
                    using var resp = await Integrations.Http.GetAsync($"https://{l}.wikipedia.org/api/rest_v1/page/summary/{Uri.EscapeDataString(q.Trim().Replace(' ', '_'))}?redirect=true");
                    if (!resp.IsSuccessStatusCode) continue;
                    using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                    var r = doc.RootElement;
                    var text = Integrations.S(r, "extract");
                    if (text == "") continue;
                    var url = r.TryGetProperty("content_urls", out var cu) && cu.TryGetProperty("desktop", out var dk) ? Integrations.S(dk, "page") : "";
                    return $"📚 <b>{H(Integrations.S(r, "title"))}</b>\n{H(Cut(text, 1500))}" + (url != "" ? $"\n<a href=\"{H(url)}\">Википедия</a>" : "");
                }
                catch { }
            }
            return "📚 Ничего не нашёл в Википедии";
        }
    }
}
