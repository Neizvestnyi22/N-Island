using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using NIsland.Core;

namespace NIsland.Services
{
    // =====================================================================
    //  Telegram 4.0:
    //  • Rich Messages (Bot API 10.1+): sendRichMessage / editMessageText(rich_message) / sendRichMessageDraft —
    //    заголовки, абзацы, таблицы, сворачиваемые блоки; ответы ИИ уходят как Rich Markdown (до 32 768 символов, поток черновиком).
    //    Если Telegram или клиент не принимает — автоматически обычный HTML.
    //  • Цветные кнопки (style: danger / success / primary).
    //  • Ещё ~50 команд: все установленные программы (список, сведения, запуск, закрыть, перезапуск, заморозить,
    //    приоритет, звук, автозапуск, удаление, обновления через winget), Windows (параметры, проекция экрана,
    //    раскладка, Caps Lock, хранитель экрана, корзина, недавние файлы, принтеры, журнал ошибок, сбои, BSOD,
    //    обновления, брандмауэр, Защитник Windows, пользователи, общие папки, устройства и драйверы, отчёт о батарее),
    //    Wi-Fi (здоровье, качество, каналы, переподключение, забыть сеть, роутер).
    // =====================================================================
    public partial class TelegramHub
    {
        // ------------------------------------------------------------------
        //  Rich Messages
        // ------------------------------------------------------------------

        static volatile bool _noRich;   // Telegram сказал, что метод недоступен — до перезапуска шлём обычный HTML

        bool RichOn => Remote == null && !_noRich && V("rich") != "0";

        /// <summary>Таблица для сообщений: в rich — настоящая таблица, в обычном HTML и других мессенджерах — выровненный моноширинный текст.</summary>
        static string Tbl(string[] head, IEnumerable<string[]> rows)
        {
            var data = new List<string[]> { head.Select(T).ToArray() };
            data.AddRange(rows.Select(r => r.Select(c => c ?? "").ToArray()));
            return "\u0002" + JsonSerializer.Serialize(data) + "\u0003";
        }

        internal static string TestTbl(string[] head, IEnumerable<string[]> rows) => Tbl(head, rows);
        internal static string TestMd(string s) => Md(s);

        static readonly Regex TblRx = new("\u0002(.*?)\u0003", RegexOptions.Singleline | RegexOptions.Compiled);

        static List<string[]> TblData(Match m) { try { return JsonSerializer.Deserialize<List<string[]>>(m.Groups[1].Value) ?? new(); } catch { return new(); } }

        /// <summary>Обычный HTML Telegram: таблицы → &lt;pre&gt; с колонками.</summary>
        public static string Legacy(string html) => TblRx.Replace(html ?? "", m =>
        {
            var rows = TblData(m);
            if (rows.Count == 0) return "";
            int cols = rows.Max(r => r.Length);
            var w = Enumerable.Range(0, cols).Select(c => Math.Min(28, rows.Max(r => c < r.Length ? r[c].Length : 0))).ToArray();
            var sb = new StringBuilder("<pre>");
            for (int i = 0; i < rows.Count; i++)
            {
                sb.Append(H(string.Join("  ", Enumerable.Range(0, cols).Select(c => Cut(c < rows[i].Length ? rows[i][c] : "", 28).PadRight(w[c]))).TrimEnd())).Append('\n');
                if (i == 0) sb.Append(new string('─', Math.Min(60, w.Sum() + 2 * (cols - 1)))).Append('\n');
            }
            return sb.ToString().TrimEnd('\n') + "</pre>";
        });

        /// <summary>
        /// Наш HTML Telegram → Rich HTML (Bot API 10.1): переносы строк превращаются в абзацы и &lt;br&gt;
        /// (в rich HTML они, как в вебе, иначе бы склеились), первая жирная строка — заголовок, таблицы — &lt;table&gt;.
        /// </summary>
        public static string ToRichHtml(string html) => MenuRichRenderer.Render(html);

        /// <summary>Markdown модели для rich-сообщения: только экранируем то, что в нашей шапке.</summary>
        static string MdEsc(string s) => Regex.Replace(s ?? "", @"([\\`*_\[\]#|~=<>])", @"\$1");

        /// <summary>Отправить rich-сообщение; null — не получилось (тогда отправим обычным).</summary>
        async Task<long?> SendRich(object richMessage, object keys, bool silent, long replyTo)
        {
            try
            {
                var r = await Call("sendRichMessage", new
                {
                    chat_id = V("chat"), rich_message = richMessage, disable_notification = silent, reply_markup = keys,
                    reply_parameters = replyTo > 0 ? new { message_id = replyTo, allow_sending_without_reply = true } : null,
                });
                return r.ValueKind == JsonValueKind.Object && r.TryGetProperty("message_id", out var mid) ? mid.GetInt64() : 0;
            }
            catch (TgError e)
            {
                // метода нет (старый сервер / ошибка 404) — больше не пробуем; кривая разметка — только это сообщение обычным HTML
                if (e.Code == 404 || e.Message.Contains("method", StringComparison.OrdinalIgnoreCase) && e.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
                    _noRich = true;
                Log.Info("telegram rich: " + e.Message);
                return null;
            }
        }

        /// <summary>Стиль кнопки (Bot API 9.4+): опасное — красным, подтверждение — зелёным, главное — синим.</summary>
        static string ButtonStyle(string data) => data switch
        {
            "pw:shutdown" or "pw:restart" or "pw:logoff" => "danger",
            _ when data.EndsWith('!') => "danger",
            _ when data.StartsWith("kill:") || data.StartsWith("ap:u:") || data.StartsWith("ap:ok:") || data.StartsWith("x4:bin") => "danger",
            _ when data.StartsWith("ap:upg:") || data.StartsWith("ap:ins:") || data.StartsWith("ap:l:") => "success",
            "nav:main" or "status!" => "primary",
            _ => null,
        };

        // ------------------------------------------------------------------
        //  Установленные программы
        // ------------------------------------------------------------------

        public record InstalledApp(string Name, string Version, string Publisher, string Location, string Uninstall, string QuietUninstall, DateTime? Installed, long SizeKb, string Key);

        static List<InstalledApp> _installed;
        static DateTime _installedAt;

        static List<InstalledApp> Installed()
        {
            if (_installed != null && (DateTime.Now - _installedAt).TotalMinutes < 10) return _installed;
            var list = new Dictionary<string, InstalledApp>(StringComparer.OrdinalIgnoreCase);
            void Scan(RegistryKey root, string path)
            {
                try
                {
                    using var k = root.OpenSubKey(path);
                    if (k == null) return;
                    foreach (var sub in k.GetSubKeyNames())
                    {
                        try
                        {
                            using var a = k.OpenSubKey(sub);
                            var name = a?.GetValue("DisplayName") as string;
                            if (string.IsNullOrWhiteSpace(name) || a.GetValue("SystemComponent") is int sc && sc == 1 || a.GetValue("ParentKeyName") != null) continue;
                            if (Regex.IsMatch(name, @"^(KB\d+|Update for|Security Update|Microsoft Visual C\+\+ 20\d\d .*(Additional|Minimum) Runtime)", RegexOptions.IgnoreCase)) continue;
                            DateTime? when = DateTime.TryParseExact(a.GetValue("InstallDate") as string ?? "", "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var d) ? d : null;
                            long size = a.GetValue("EstimatedSize") is int es ? es : 0;
                            list[name.Trim()] = new InstalledApp(name.Trim(), a.GetValue("DisplayVersion") as string ?? "", a.GetValue("Publisher") as string ?? "",
                                a.GetValue("InstallLocation") as string ?? "", a.GetValue("UninstallString") as string ?? "", a.GetValue("QuietUninstallString") as string ?? "", when, size, sub);
                        }
                        catch { }
                    }
                }
                catch { }
            }
            using (var lm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            {
                Scan(lm, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                Scan(lm, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall");
            }
            Scan(Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            _installed = list.Values.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            _installedAt = DateTime.Now;
            return _installed;
        }

        static InstalledApp FindInstalled(string q)
        {
            q = q.Trim();
            var all = Installed();
            return all.FirstOrDefault(a => a.Name.Equals(q, StringComparison.OrdinalIgnoreCase))
                ?? all.FirstOrDefault(a => a.Name.StartsWith(q, StringComparison.OrdinalIgnoreCase))
                ?? all.FirstOrDefault(a => a.Name.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Запущенные процессы программы: по имени процесса или по папке установки.</summary>
        static List<Process> ProcsOf(string q, InstalledApp app = null)
        {
            var res = new List<Process>();
            string loc = app?.Location?.TrimEnd('\\');
            foreach (var p in Process.GetProcesses())
            {
                bool hit = p.ProcessName.Equals(q, StringComparison.OrdinalIgnoreCase) || p.ProcessName.StartsWith(q.Replace(" ", ""), StringComparison.OrdinalIgnoreCase);
                if (!hit && !string.IsNullOrEmpty(loc) && loc.Length > 3)
                    try { hit = p.MainModule?.FileName?.StartsWith(loc, StringComparison.OrdinalIgnoreCase) == true; } catch { }
                if (hit && p.Id != Environment.ProcessId) res.Add(p); else p.Dispose();
            }
            return res;
        }

        static readonly HashSet<string> Critical = new(StringComparer.OrdinalIgnoreCase)
        {
            "csrss", "winlogon", "wininit", "lsass", "smss", "services", "svchost", "dwm", "System", "Idle", "Registry", "fontdrvhost", "explorer", "NIsland", "MsMpEng", "audiodg", "conhost", "sihost", "ctfmon",
        };

        static readonly HashSet<string> Suspended = new(StringComparer.OrdinalIgnoreCase)
        {
            "SystemSettings", "ApplicationFrameHost", "TextInputHost", "ShellExperienceHost", "SearchHost", "StartMenuExperienceHost", "LockApp", "Widgets", "WindowsTerminal",
        };

        [DllImport("ntdll.dll")] static extern int NtSuspendProcess(IntPtr h);
        [DllImport("ntdll.dll")] static extern int NtResumeProcess(IntPtr h);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern short GetKeyState(int key);
        [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

        /// <summary>Запустить внешнюю утилиту и вернуть вывод (без окна, с таймаутом).</summary>
        static string Run(string exe, string args, int timeoutMs = 60000)
        {
            try
            {
                var psi = new ProcessStartInfo(exe, args)
                {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
                };
                using var p = Process.Start(psi);
                var o = p.StandardOutput.ReadToEndAsync();
                var e = p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit(timeoutMs)) { try { p.Kill(true); } catch { } return "(не уложилось во время)"; }
                return (o.Result + (string.IsNullOrWhiteSpace(e.Result) ? "" : "\n" + e.Result)).Trim();
            }
            catch (Exception ex) { return "⚠️ " + ex.Message; }
        }

        static string Ps(string script, int timeoutMs = 60000) =>
            Run("powershell.exe", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"[Console]::OutputEncoding=[Text.Encoding]::UTF8; " + script.Replace("\"", "\\\"") + "\"", timeoutMs);

        static string Winget(string args, int timeoutMs = 120000) =>
            Run("winget", args + " --disable-interactivity --accept-source-agreements", timeoutMs);

        /// <summary>Строки таблицы winget (search / upgrade): имя, id, версия, доступно.</summary>
        static List<string[]> WingetTable(string output)
        {
            var lines = output.Replace("\r", "").Split('\n').Select(l => Regex.Replace(l, @"^[\s\-\\|/]+(?=\S)", "")).ToList();
            int hi = lines.FindIndex(l => Regex.IsMatch(l, @"^(Name|Имя|Назва)\s+(Id|ИД|Ідентифікатор)\s", RegexOptions.IgnoreCase));
            if (hi < 0) return new();
            var header = lines[hi];
            var starts = Regex.Matches(header, @"\S+").Select(m => m.Index).ToList();
            var res = new List<string[]>();
            foreach (var l in lines.Skip(hi + 2))
            {
                if (l.Trim().Length == 0 || Regex.IsMatch(l, @"^\d+ (upgrades|обновлен|оновлен)", RegexOptions.IgnoreCase)) break;
                var cells = new List<string>();
                for (int i = 0; i < starts.Count; i++)
                {
                    int s = Math.Min(starts[i], l.Length), e = i + 1 < starts.Count ? Math.Min(starts[i + 1], l.Length) : l.Length;
                    cells.Add(l[s..e].Trim());
                }
                res.Add(cells.ToArray());
            }
            return res;
        }

        // ------------------------------------------------------------------
        //  Команды 4.0
        // ------------------------------------------------------------------

        static readonly (string Cmd, string Desc)[] Commands4 =
        {
            ("installed", "Установленные программы"), ("appinfo", "О программе: /appinfo chrome"), ("launch", "Запустить программу: /launch spotify"),
            ("quit", "Закрыть программу: /quit telegram"), ("relaunch", "Перезапустить программу"), ("focus", "Окно программы на передний план"),
            ("hang", "Зависшие программы"), ("freeze", "Заморозить программу"), ("unfreeze", "Разморозить программу"), ("priority", "Приоритет: /priority obs high"),
            ("appmute", "Звук программы вкл/выкл"), ("appcpu", "Сколько ест программа"), ("autostart", "Автозапуск: /autostart discord off"),
            ("uninstall", "Удалить программу"), ("upgrades", "Обновления программ (winget)"), ("upgrade", "Обновить программу"), ("winstall", "Установить через winget"),
            ("wsearch", "Найти программу в winget"), ("winset", "Открыть параметры Windows"), ("project", "Экран: pc · clone · extend · second"),
            ("layout", "Сменить раскладку"), ("caps", "Caps Lock"), ("saver", "Хранитель экрана"), ("emptybin", "Очистить корзину"), ("recent", "Недавние файлы"),
            ("printers", "Принтеры"), ("printq", "Очередь печати"), ("events", "Ошибки Windows за сутки"), ("crashes", "Сбои программ за неделю"), ("bsod", "Синие экраны"),
            ("hotfix", "Последние обновления Windows"), ("firewall", "Брандмауэр"), ("wdstatus", "Защитник Windows"), ("users", "Кто вошёл в Windows"),
            ("shares", "Общие папки"), ("drivers", "Устройства с ошибками"), ("usbdev", "USB-устройства"), ("audiodev", "Звуковые устройства"), ("outdev", "Вывод звука: /outdev наушники"),
            ("batreport", "Отчёт о батарее"), ("clipimg", "Картинка из буфера ПК"), ("sysreport", "Полный отчёт о ПК"), ("wifihealth", "Здоровье Wi-Fi"),
            ("wifiq", "Качество связи Wi-Fi"), ("channels", "Загрузка каналов Wi-Fi"), ("reconnect", "Переподключить Wi-Fi"), ("forget", "Забыть сеть Wi-Fi"), ("router", "Роутер и шлюз"),
        };

        static readonly (string Key, string Name, string Uri)[] WinPages =
        {
            ("display", "Экран", "ms-settings:display"), ("sound", "Звук", "ms-settings:sound"), ("bluetooth", "Bluetooth", "ms-settings:bluetooth"),
            ("wifi", "Wi-Fi", "ms-settings:network-wifi"), ("network", "Сеть", "ms-settings:network-status"), ("update", "Обновления", "ms-settings:windowsupdate"),
            ("apps", "Приложения", "ms-settings:appsfeatures"), ("startup", "Автозагрузка", "ms-settings:startupapps"), ("power", "Питание", "ms-settings:powersleep"),
            ("battery", "Батарея", "ms-settings:batterysaver"), ("storage", "Память", "ms-settings:storagesense"), ("privacy", "Конфиденциальность", "ms-settings:privacy"),
            ("notifications", "Уведомления", "ms-settings:notifications"), ("personalization", "Персонализация", "ms-settings:personalization"),
            ("nightlight", "Ночной свет", "ms-settings:nightlight"), ("mouse", "Мышь", "ms-settings:mousetouchpad"), ("keyboard", "Клавиатура", "ms-settings:keyboard"),
            ("printers", "Принтеры", "ms-settings:printers"), ("vpn", "VPN", "ms-settings:network-vpn"), ("defender", "Безопасность Windows", "windowsdefender:"),
        };

        async Task<(string Html, object Keys)?> Command4(string cmd, string arg)
        {
            string no;
            switch (cmd)
            {
                // ---------------- Программы ----------------
                case "installed":
                {
                    var all = await Task.Run(Installed);
                    var list = arg == "" ? all : all.Where(a => a.Name.Contains(arg, StringComparison.OrdinalIgnoreCase) || a.Publisher.Contains(arg, StringComparison.OrdinalIgnoreCase)).ToList();
                    if (list.Count == 0) return ("📦 Ничего не нашёл. Все: /installed", null);
                    var shown = list.Take(60).ToList();
                    return ($"📦 <b>Установлено программ: {all.Count}</b>" + (arg != "" ? $" · найдено {list.Count}" : "") + "\n\n" +
                            Tbl(new[] { "Программа", "Версия", "Размер" }, shown.Select(a => new[] { Cut(a.Name, 40), Cut(a.Version, 16), a.SizeKb > 0 ? Maint.Size(a.SizeKb * 1024) : "" })) +
                            (list.Count > shown.Count ? $"\n…и ещё {list.Count - shown.Count}. Уточни: /installed слово" : "") + "\n\nПодробно: /appinfo имя", null);
                }
                case "appinfo":
                {
                    if (arg == "") return ("Напиши так: /appinfo chrome", null);
                    var a = await Task.Run(() => FindInstalled(arg));
                    if (a == null) return ("Не нашёл такую программу. Список: /installed", null);
                    var procs = await Task.Run(() => ProcsOf(arg, a));
                    long mem = procs.Sum(p => { try { return p.WorkingSet64; } catch { return 0; } });
                    procs.ForEach(p => p.Dispose());
                    var html = $"📦 <b>{H(a.Name)}</b>\n" +
                               (a.Version != "" ? $"Версия: {H(a.Version)}\n" : "") + (a.Publisher != "" ? $"Издатель: {H(a.Publisher)}\n" : "") +
                               (a.Installed is { } d ? $"Установлена: {d:dd.MM.yyyy}\n" : "") + (a.SizeKb > 0 ? $"Размер: {Maint.Size(a.SizeKb * 1024)}\n" : "") +
                               (a.Location != "" ? $"Папка: <code>{H(a.Location)}</code>\n" : "") +
                               (procs.Count > 0 ? $"▶ Запущена · процессов {procs.Count} · {Maint.Size(mem)}" : "⏹ Не запущена");
                    var n = Cut(arg, 30);
                    return (html, K(Row(B("▶ Запустить", "ap:l:" + n), B("✖ Закрыть", "ap:q:" + n), B("🔄 Перезапуск", "ap:r:" + n)),
                                    Row(B("🧊 Заморозить", "ap:f:" + n), B("🔥 Разморозить", "ap:uf:" + n)),
                                    Row(B("🗑 Удалить…", "ap:u:" + n))));
                }
                case "launch":
                {
                    if ((no = Need("apps")) != null) return (no, null);
                    if (arg == "") return ("Напиши так: /launch spotify", null);
                    return (await LaunchApp(arg), null);
                }
                case "quit": case "relaunch":
                {
                    if ((no = Need("apps")) != null) return (no, null);
                    if (arg == "") return ($"Напиши так: /{cmd} telegram", null);
                    return (await QuitApp(arg, cmd == "relaunch"), null);
                }
                case "focus":
                {
                    if ((no = Need("apps")) != null) return (no, null);
                    if (arg == "") return ("Напиши так: /focus telegram", null);
                    var w = PcControl.ListWindows().FirstOrDefault(x => x.Title.Contains(arg, StringComparison.OrdinalIgnoreCase) || x.Process.Contains(arg, StringComparison.OrdinalIgnoreCase));
                    if (w == null) return ("Окно не найдено. Список: /windows", null);
                    await Ui(() => PcControl.Focus(w.Handle));
                    return ($"🪟 На переднем плане: {H(Cut(w.Title, 80))}", null);
                }
                case "hang":
                {
                    var hung = await Task.Run(() =>
                    {
                        var res = new List<(string, int)>();
                        foreach (var p in Process.GetProcesses())
                        {
                            // приостановленные Windows UWP-окна (Параметры, поиск, Пуск) «не отвечают» по задумке — это не зависание
                            try { if (p.MainWindowHandle != IntPtr.Zero && !p.Responding && !Suspended.Contains(p.ProcessName)) res.Add((p.ProcessName, p.Id)); } catch { }
                            p.Dispose();
                        }
                        return res;
                    });
                    if (hung.Count == 0) return ("✓ Зависших программ нет", null);
                    return ("🥶 <b>Не отвечают</b>\n" + string.Join("\n", hung.Select(h => "• " + H(h.Item1))),
                        Flag("apps") ? K(hung.Take(6).Select(h => Row(B("✖ Завершить " + Cut(h.Item1, 20), "kill:" + h.Item2))).ToArray()) : null);
                }
                case "freeze": case "unfreeze":
                {
                    if ((no = Need("apps")) != null) return (no, null);
                    if (arg == "") return ($"Напиши так: /{cmd} chrome", null);
                    return (FreezeApp(arg, cmd == "freeze"), null);
                }
                case "priority":
                {
                    if ((no = Need("apps")) != null) return (no, null);
                    var m = Regex.Match(arg, @"^(.+?)\s+(high|above|normal|below|low|высокий|выше|обычный|ниже|низкий)$", RegexOptions.IgnoreCase);
                    if (!m.Success) return ("Напиши так: /priority obs high (high · above · normal · below · low)", null);
                    var cls = m.Groups[2].Value.ToLowerInvariant() switch
                    {
                        "high" or "высокий" => ProcessPriorityClass.High, "above" or "выше" => ProcessPriorityClass.AboveNormal,
                        "below" or "ниже" => ProcessPriorityClass.BelowNormal, "low" or "низкий" => ProcessPriorityClass.Idle, _ => ProcessPriorityClass.Normal,
                    };
                    var procs = ProcsOf(m.Groups[1].Value.Trim());
                    int ok = 0;
                    foreach (var p in procs) { try { if (!Critical.Contains(p.ProcessName)) { p.PriorityClass = cls; ok++; } } catch { } p.Dispose(); }
                    return (ok == 0 ? "Не нашёл такую запущенную программу" : $"⚙️ Приоритет {cls} для {ok} процессов", null);
                }
                case "appmute":
                {
                    if ((no = Need("control")) != null) return (no, null);
                    var app = AudioService.I.Apps().FirstOrDefault(x => x.Name.Contains(arg, StringComparison.OrdinalIgnoreCase));
                    if (app == null || arg == "") return ("Эта программа сейчас не звучит. Список: /appvol", null);
                    AudioService.I.SetAppVolume(app.Name, app.Volume, !app.Muted);
                    return (!app.Muted ? $"🔇 {H(app.Name)} без звука" : $"🔊 {H(app.Name)} снова со звуком", null);
                }
                case "appcpu":
                {
                    if (arg == "") return ("Напиши так: /appcpu chrome", null);
                    var r = await Task.Run(() => TopCpu(40).Where(x => x.Name.Contains(arg, StringComparison.OrdinalIgnoreCase)).ToList());
                    var procs = ProcsOf(arg);
                    long mem = procs.Sum(p => { try { return p.WorkingSet64; } catch { return 0; } });
                    int n = procs.Count;
                    procs.ForEach(p => p.Dispose());
                    if (n == 0) return ("Не запущена", null);
                    return ($"📊 <b>{H(arg)}</b>: процессор {r.Sum(x => x.Pct):0.#}% · память {Maint.Size(mem)} · процессов {n}", null);
                }
                case "autostart":
                {
                    var items = Maint.Startup();
                    var m = Regex.Match(arg, @"^(.+?)\s+(on|off|вкл|выкл)$", RegexOptions.IgnoreCase);
                    if (!m.Success)
                        return ("🚀 <b>Автозагрузка</b>\n" + string.Join("\n", items.Take(30).Select(s => (s.Enabled ? "🟢 " : "⚪ ") + H(s.Name))) + "\n\nВключить или выключить: /autostart имя off", null);
                    if ((no = Need("apps")) != null) return (no, null);
                    var it = items.FirstOrDefault(s => s.Name.Contains(m.Groups[1].Value.Trim(), StringComparison.OrdinalIgnoreCase));
                    if (it == null) return ("Нет такого пункта автозагрузки", null);
                    bool on = m.Groups[2].Value.ToLowerInvariant() is "on" or "вкл";
                    await Ui(() => Maint.SetStartup(it, on));
                    return ($"🚀 {H(it.Name)}: {(on ? "запускается с Windows" : "больше не запускается с Windows")}", null);
                }
                case "uninstall":
                {
                    if ((no = Need("apps")) != null) return (no, null);
                    var a = arg == "" ? null : await Task.Run(() => FindInstalled(arg));
                    if (a == null) return ("Напиши так: /uninstall имя (список: /installed)", null);
                    if (a.Uninstall == "") return ("У этой программы нет удаления через Windows", null);
                    return ($"🗑 Удалить <b>{H(a.Name)}</b>?\nНа ПК откроется её мастер удаления — может понадобиться подтвердить там.",
                        K(Row(B("🗑 Да, удалить", "ap:ok:" + Cut(arg, 40)), B("Отмена", "ap:no"))));
                }
                case "upgrades":
                {
                    if (Remote == null) await Message("⬆️ Спрашиваю winget… (до минуты)");
                    var rows = WingetTable(await Task.Run(() => Winget("upgrade --include-unknown")));
                    if (rows.Count == 0) return ("✓ Все программы обновлены (по данным winget)", null);
                    return ($"⬆️ <b>Можно обновить: {rows.Count}</b>\n\n" + Tbl(new[] { "Программа", "Сейчас", "Новая" }, rows.Take(40).Select(r => new[] { Cut(r[0], 34), r.ElementAtOrDefault(2) ?? "", r.ElementAtOrDefault(3) ?? "" })) +
                            "\n\nОбновить одну: /upgrade id · все: /upgrade all",
                        Flag("apps") ? K(rows.Take(5).Select(r => Row(B("⬆️ " + Cut(r[0], 24), "ap:upg:" + Cut(r.ElementAtOrDefault(1) ?? "", 50)))).Append(Row(B("⬆️ Обновить всё", "ap:upg:all"))).ToArray()) : null);
                }
                case "upgrade": case "winstall":
                {
                    if ((no = Need("apps")) != null) return (no, null);
                    var id = arg.Trim();
                    if (!Regex.IsMatch(id, @"^[\w\.\-+]{2,80}$")) return ($"Напиши так: /{cmd} Mozilla.Firefox (id из /wsearch или /upgrades)", null);
                    return ($"{(cmd == "upgrade" ? "⬆️ Обновить" : "📥 Установить")} <b>{H(id)}</b> через winget?", K(Row(B("✅ Да", (cmd == "upgrade" ? "ap:upg:" : "ap:ins:") + id), B("Отмена", "ap:no"))));
                }
                case "wsearch":
                {
                    if (!Regex.IsMatch(arg, @"^[\w\.\- +]{2,60}$")) return ("Напиши так: /wsearch firefox", null);
                    var rows = WingetTable(await Task.Run(() => Winget("search \"" + arg + "\" --count 15")));
                    if (rows.Count == 0) return ("Ничего не нашёл в winget", null);
                    return ("🔎 <b>winget</b>\n\n" + Tbl(new[] { "Название", "Id", "Версия" }, rows.Select(r => new[] { Cut(r[0], 30), r.ElementAtOrDefault(1) ?? "", r.ElementAtOrDefault(2) ?? "" })) +
                            "\n\nУстановить: /winstall Id", null);
                }

                // ---------------- Windows ----------------
                case "winset":
                {
                    var page = WinPages.FirstOrDefault(p => p.Key == arg.ToLowerInvariant() || T(p.Name).Equals(arg, StringComparison.OrdinalIgnoreCase));
                    if (page.Key == null)
                        return ("⚙️ <b>Параметры Windows</b>\nОткрыть на ПК: /winset раздел\n\n" + string.Join(" · ", WinPages.Select(p => $"<code>{p.Key}</code> {H(T(p.Name))}")), null);
                    if ((no = Need("control")) != null) return (no, null);
                    await Ui(() => SystemService.Run(page.Uri));
                    return ($"⚙️ Открыто на ПК: {H(T(page.Name))}", null);
                }
                case "project":
                {
                    if ((no = Need("control")) != null) return (no, null);
                    var mode = arg.ToLowerInvariant() switch
                    {
                        "pc" or "internal" or "только" => "/internal", "clone" or "duplicate" or "дубль" => "/clone",
                        "extend" or "расширить" => "/extend", "second" or "external" or "второй" => "/external", _ => null,
                    };
                    if (mode == null) return ("Напиши так: /project pc · clone · extend · second", null);
                    Process.Start(new ProcessStartInfo("DisplaySwitch.exe", mode) { UseShellExecute = false, CreateNoWindow = true })?.Dispose();
                    return ("🖥 Режим экрана переключён", null);
                }
                case "layout":
                    if ((no = Need("input")) != null) return (no, null);
                    PcControl.Hotkey("win+space");
                    return ("⌨️ Раскладка переключена", null);
                case "caps":
                {
                    if ((no = Need("input")) != null) return (no, null);
                    bool on = (GetKeyState(0x14) & 1) != 0;
                    bool want = arg.ToLowerInvariant() switch { "on" or "вкл" => true, "off" or "выкл" => false, _ => !on };
                    if (want != on) { keybd_event(0x14, 0x45, 1, UIntPtr.Zero); keybd_event(0x14, 0x45, 3, UIntPtr.Zero); on = want; }
                    return ($"⇪ Caps Lock {(on ? "включён" : "выключен")}", null);
                }
                case "saver":
                    if ((no = Need("control")) != null) return (no, null);
                    await Ui(() => SendMessage((IntPtr)0xFFFF, 0x0112, (IntPtr)0xF140, IntPtr.Zero));
                    return ("🌌 Хранитель экрана запущен", null);
                case "emptybin":
                {
                    if ((no = Need("files")) != null) return (no, null);
                    var (bytes, items) = Maint.RecycleBin();
                    if (items == 0) return ("🗑 Корзина уже пуста", null);
                    return ($"🗑 Очистить корзину навсегда? Там {items} объектов · {Mb(bytes)}. Вернуть будет нельзя.", K(Row(B("🗑 Да, очистить", "x4:bin!"), B("Отмена", "ap:no"))));
                }
                case "recent":
                {
                    if ((no = Need("files")) != null) return (no, null);
                    var dir = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
                    var files = await Task.Run(() => new DirectoryInfo(dir).GetFiles("*.lnk").OrderByDescending(f => f.LastWriteTime).Take(20)
                        .Select(f => (Name: Path.GetFileNameWithoutExtension(f.Name), f.LastWriteTime)).ToList());
                    if (files.Count == 0) return ("Недавних файлов нет", null);
                    return ("🕘 <b>Недавние файлы</b>\n\n" + Tbl(new[] { "Файл", "Когда" }, files.Select(f => new[] { Cut(f.Name, 44), f.LastWriteTime.ToString("dd.MM HH:mm") })), null);
                }
                case "printers":
                {
                    var outp = await Task.Run(() => Ps("Get-CimInstance Win32_Printer | ForEach-Object { $_.Name + '|' + $_.Default + '|' + $_.WorkOffline + '|' + $_.PrinterStatus }"));
                    var rows = outp.Split('\n').Select(l => l.Trim().Split('|')).Where(r => r.Length >= 4).ToList();
                    if (rows.Count == 0) return ("🖨 Принтеров нет", null);
                    return ("🖨 <b>Принтеры</b>\n\n" + Tbl(new[] { "Принтер", "По умолчанию", "Связь" }, rows.Select(r => new[] { Cut(r[0], 36), r[1] == "True" ? "✓" : "", r[2] == "True" ? "нет" : "есть" })), null);
                }
                case "printq":
                {
                    var outp = await Task.Run(() => Ps("Get-CimInstance Win32_PrintJob | ForEach-Object { $_.Document + '|' + $_.Name + '|' + $_.JobStatus + '|' + $_.TotalPages }"));
                    var rows = outp.Split('\n').Select(l => l.Trim().Split('|')).Where(r => r.Length >= 4).ToList();
                    return (rows.Count == 0 ? "🖨 Очередь печати пуста" : "🖨 <b>Очередь печати</b>\n\n" + Tbl(new[] { "Документ", "Состояние", "Стр." }, rows.Select(r => new[] { Cut(r[0], 36), r[2], r[3] })), null);
                }
                case "events": case "crashes": case "bsod":
                {
                    var q = cmd switch
                    {
                        "events" => "Get-WinEvent -FilterHashtable @{LogName='System','Application'; Level=1,2; StartTime=(Get-Date).AddDays(-1)} -MaxEvents 15 -ErrorAction SilentlyContinue",
                        "crashes" => "Get-WinEvent -FilterHashtable @{LogName='Application'; Id=1000,1002; StartTime=(Get-Date).AddDays(-7)} -MaxEvents 15 -ErrorAction SilentlyContinue",
                        _ => "Get-WinEvent -FilterHashtable @{LogName='System'; Id=1001,41; StartTime=(Get-Date).AddDays(-60)} -MaxEvents 10 -ErrorAction SilentlyContinue",
                    };
                    var outp = await Task.Run(() => Ps(q + " | ForEach-Object { $_.TimeCreated.ToString('dd.MM HH:mm') + '|' + $_.ProviderName + '|' + (($_.Message -split \"`n\")[0]) }", 45000));
                    var rows = outp.Split('\n').Select(l => l.Trim().Split('|', 3)).Where(r => r.Length == 3).ToList();
                    var title = cmd switch { "events" => "🧾 Ошибки Windows за сутки", "crashes" => "💥 Сбои программ за неделю", _ => "🟦 Синие экраны и аварийные выключения (60 дней)" };
                    if (rows.Count == 0) return ($"✓ {title}: нет", null);
                    return ($"<b>{title}</b>\n\n" + Tbl(new[] { "Когда", "Источник", "Что" }, rows.Select(r => new[] { r[0], Cut(r[1], 24), Cut(r[2], 70) })), null);
                }
                case "hotfix":
                {
                    var outp = await Task.Run(() => Ps("Get-HotFix | Sort-Object InstalledOn -Descending | Select-Object -First 12 | ForEach-Object { $_.HotFixID + '|' + $_.Description + '|' + ($_.InstalledOn -as [datetime]).ToString('dd.MM.yyyy') }"));
                    var rows = outp.Split('\n').Select(l => l.Trim().Split('|')).Where(r => r.Length == 3).ToList();
                    return (rows.Count == 0 ? "Нет данных" : "🔄 <b>Последние обновления Windows</b>\n\n" + Tbl(new[] { "KB", "Тип", "Дата" }, rows), null);
                }
                case "firewall":
                {
                    var outp = await Task.Run(() => Ps("Get-NetFirewallProfile | ForEach-Object { $_.Name + '|' + $_.Enabled + '|' + $_.DefaultInboundAction }"));
                    var rows = outp.Split('\n').Select(l => l.Trim().Split('|')).Where(r => r.Length == 3).ToList();
                    return (rows.Count == 0 ? "Нет данных" : "🧱 <b>Брандмауэр Windows</b>\n\n" + Tbl(new[] { "Профиль", "Включён", "Входящие" }, rows.Select(r => new[] { r[0], r[1] == "True" ? "🟢 да" : "🔴 нет", r[2] })), null);
                }
                case "wdstatus":
                {
                    var outp = await Task.Run(() => Ps("$s = Get-MpComputerStatus; 'Защита в реальном времени|' + $s.RealTimeProtectionEnabled; 'Антивирус|' + $s.AntivirusEnabled; 'Базы обновлены|' + $s.AntivirusSignatureLastUpdated.ToString('dd.MM HH:mm'); 'Последняя быстрая проверка|' + $s.QuickScanEndTime; 'Защита от подделки|' + $s.IsTamperProtected"));
                    var rows = outp.Split('\n').Select(l => l.Trim().Split('|', 2)).Where(r => r.Length == 2).ToList();
                    return (rows.Count == 0 ? "Защитник Windows не отвечает (может, стоит другой антивирус)" :
                        "🛡 <b>Защитник Windows</b>\n\n" + Tbl(new[] { "Что", "Состояние" }, rows.Select(r => new[] { T(r[0]), r[1] switch { "True" => "🟢 да", "False" => "🔴 нет", var x => x } })), null);
                }
                case "users":
                {
                    var outp = await Task.Run(() => Run("query", "user"));
                    return ("👤 <b>Сеансы Windows</b>\n<pre>" + H(Cut(outp, 1500)) + "</pre>", null);
                }
                case "shares":
                {
                    var outp = await Task.Run(() => Ps("Get-SmbShare | Where-Object { -not $_.Special } | ForEach-Object { $_.Name + '|' + $_.Path }"));
                    var rows = outp.Split('\n').Select(l => l.Trim().Split('|')).Where(r => r.Length == 2).ToList();
                    return (rows.Count == 0 ? "📂 Общих папок нет ✓" : "📂 <b>Общие папки</b>\n\n" + Tbl(new[] { "Имя", "Папка" }, rows), null);
                }
                case "drivers":
                {
                    var outp = await Task.Run(() => Ps("Get-CimInstance Win32_PnPEntity | Where-Object { $_.ConfigManagerErrorCode -ne 0 } | ForEach-Object { $_.Name + '|' + $_.ConfigManagerErrorCode }"));
                    var rows = outp.Split('\n').Select(l => l.Trim().Split('|')).Where(r => r.Length == 2).ToList();
                    return (rows.Count == 0 ? "✓ Все устройства работают" : "⚠️ <b>Устройства с ошибками</b>\n\n" + Tbl(new[] { "Устройство", "Код" }, rows), null);
                }
                case "usbdev":
                {
                    var outp = await Task.Run(() => Ps("Get-PnpDevice -PresentOnly -Class USB,HIDClass,Mouse,Keyboard,Image,Media,DiskDrive,Bluetooth -ErrorAction SilentlyContinue | Where-Object { $_.Status -eq 'OK' } | Sort-Object Class | ForEach-Object { $_.Class + '|' + $_.FriendlyName }"));
                    var rows = outp.Split('\n').Select(l => l.Trim().Split('|')).Where(r => r.Length == 2 && !Regex.IsMatch(r[1], @"Root Hub|Host Controller|Composite|HID-compliant (vendor|system|consumer)", RegexOptions.IgnoreCase)).Distinct().Take(40).ToList();
                    return (rows.Count == 0 ? "Нет данных" : "🔌 <b>Подключённые устройства</b>\n\n" + Tbl(new[] { "Тип", "Устройство" }, rows.Select(r => new[] { r[0], Cut(r[1], 50) })), null);
                }
                case "audiodev":
                {
                    var outs = AudioService.I.Outputs();
                    return ("🔊 <b>Вывод звука</b>\n" + string.Join("\n", outs.Select(o => (o.IsDefault ? "▶ " : "· ") + H(o.Name))) + "\n\nПереключить: /outdev часть имени", null);
                }
                case "outdev":
                {
                    if ((no = Need("control")) != null) return (no, null);
                    var o = AudioService.I.Outputs().FirstOrDefault(x => x.Name.Contains(arg, StringComparison.OrdinalIgnoreCase));
                    if (o == null || arg == "") return ("Не нашёл такое устройство. Список: /audiodev", null);
                    await Ui(() => { AudioService.I.SetDefaultOutput(o.Id); });
                    return ($"🔊 Звук идёт в: {H(o.Name)}", null);
                }
                case "batreport":
                {
                    if ((no = Need("control")) != null) return (no, null);
                    if (!SystemService.I.HasBattery) return ("🔌 У этого ПК нет батареи", null);
                    var file = Path.Combine(Path.GetTempPath(), "N-Island-battery-report.html");
                    await Task.Run(() => Run("powercfg", $"/batteryreport /output \"{file}\""));
                    if (!File.Exists(file)) return ("Не удалось создать отчёт", null);
                    await Upload("sendDocument", "document", Bytes(await File.ReadAllBytesAsync(file), "text/html"), "battery-report.html", "🔋 Отчёт о батарее Windows — открой в браузере");
                    return null;
                }
                case "clipimg":
                {
                    if ((no = Need("control")) != null) return (no, null);
                    var png = await Ui(() =>
                    {
                        if (!System.Windows.Clipboard.ContainsImage()) return null;
                        var img = System.Windows.Clipboard.GetImage();
                        var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                        enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(img));
                        using var ms = new MemoryStream();
                        enc.Save(ms);
                        return ms.ToArray();
                    });
                    if (png == null) return ("📋 В буфере ПК нет картинки", null);
                    await Upload("sendPhoto", "photo", Bytes(png, "image/png"), "clipboard.png", "📋 Картинка из буфера ПК");
                    return null;
                }
                case "sysreport":
                {
                    if (Remote == null) await Message("🧾 Собираю отчёт…");
                    return (await Task.Run(SysReport), null);
                }

                // ---------------- Wi-Fi ----------------
                case "wifihealth":
                {
                    if (Remote == null) await Message("📶 Проверяю Wi-Fi… (до 30 с)");
                    var (items, _, q) = await WifiTools.Health();
                    return ("📶 <b>Здоровье Wi-Fi</b>\n\n" + string.Join("\n", items.Take(14).Select(i => $"{(i.Level >= 2 ? "🔴" : i.Level == 1 ? "🟡" : "🟢")} <b>{H(T(i.Title))}</b>" + (string.IsNullOrEmpty(i.Advice) ? "" : $" — {H(T(i.Advice))}"))) +
                            (q != null ? $"\n\nДо роутера {q.GwMs:0} мс · интернет {q.NetMs:0} мс · потери {q.NetLoss:0}%" : ""), null);
                }
                case "wifiq":
                {
                    if (Remote == null) await Message("📶 Меряю качество связи… (до 20 с)");
                    var q = await WifiTools.TestQuality();
                    return ("📶 <b>Качество связи</b>\n\n" + Tbl(new[] { "Куда", "Задержка", "Джиттер", "Потери" }, new[]
                            {
                                new[] { T("Роутер"), $"{q.GwMs:0} мс", $"{q.GwJitter:0} мс", $"{q.GwLoss:0}%" },
                                new[] { T("Интернет"), $"{q.NetMs:0} мс", $"{q.NetJitter:0} мс", $"{q.NetLoss:0}%" },
                                new[] { "DNS", $"{q.DnsMs:0} мс", "", "" },
                            }) + $"\n\n<b>{H(T(q.Verdict))}</b>\n{H(T(q.Advice))}", null);
                }
                case "channels":
                {
                    var aps = await WifiTools.Nearby();
                    var link = await WifiTools.Current();
                    var table = WifiTools.ChannelTable(aps, link);
                    var (now, best) = WifiTools.ChannelAdvice(aps, link);
                    return ("📶 <b>Загрузка каналов</b>\n\n" + Tbl(new[] { "Канал", "Диапазон", "Сетей", "Нагрузка" }, table.Where(c => c.Networks > 0 || c.Current || c.Best).Take(24)
                                .Select(c => new[] { c.Channel + (c.Current ? " ◀" : "") + (c.Best ? " ★" : ""), c.Band, c.Networks.ToString(), $"{c.Load * 100:0}%" })) +
                            $"\n\n{H(T(now))}\n{H(T(best))}", null);
                }
                case "reconnect":
                    if ((no = Need("control")) != null) return (no, null);
                    return ("📶 " + H(T(await WifiTools.Reconnect())), null);
                case "forget":
                {
                    if ((no = Need("control")) != null) return (no, null);
                    if (arg == "") return ("Напиши так: /forget ИмяСети", null);
                    return ("📶 " + H(T(await WifiTools.Forget(arg.Trim()))), null);
                }
                case "router":
                {
                    var gw = WifiTools.Gateway();
                    var link = await WifiTools.Current();
                    var dns = string.Join(", ", WifiTools.CurrentDns().Take(3));
                    return ($"🛜 <b>Роутер</b>\nШлюз: <code>{H(gw?.ToString() ?? "нет")}</code>\nDNS: <code>{H(dns)}</code>" +
                            (link != null ? $"\nСеть: {H(link.Ssid)} · {link.Signal}% · {H(link.Band)} к{link.Channel}" : ""), null);
                }
            }
            return null;
        }

        async Task<string> LaunchApp(string q)
        {
            var app = AppIndex.Apps.FirstOrDefault(a => a.Name.Equals(q, StringComparison.OrdinalIgnoreCase))
                   ?? AppIndex.Apps.FirstOrDefault(a => a.Name.StartsWith(q, StringComparison.OrdinalIgnoreCase))
                   ?? AppIndex.Apps.FirstOrDefault(a => a.Name.Contains(q, StringComparison.OrdinalIgnoreCase));
            if (app == null) return "Не нашёл такую программу в меню «Пуск». Список: /installed";
            await Ui(() => SystemService.Run(app.Target));
            Toast("Запущено из Telegram", app.Name);
            return "▶ Запускаю: " + H(app.Name);
        }

        async Task<string> QuitApp(string q, bool restart)
        {
            var procs = await Task.Run(() => ProcsOf(q));
            procs.RemoveAll(p => { if (Critical.Contains(p.ProcessName)) { p.Dispose(); return true; } return false; });
            if (procs.Count == 0) return "Такая программа не запущена";
            string path = null;
            foreach (var p in procs) { try { path ??= p.MainModule?.FileName; } catch { } }
            // сначала вежливо: закрыть окна (сохранит работу), через 5 с — принудительно, кто не закрылся
            foreach (var p in procs) try { if (p.MainWindowHandle != IntPtr.Zero) PostMessage(p.MainWindowHandle, 0x0010, IntPtr.Zero, IntPtr.Zero); } catch { }
            await Task.Delay(5000);
            int killed = 0;
            foreach (var p in procs) { try { if (!p.HasExited) { p.Kill(true); killed++; } } catch { } p.Dispose(); }
            if (restart && path != null) { await Task.Delay(800); await Ui(() => SystemService.Run(path)); }
            return (restart ? "🔄 Перезапущено: " : "✖ Закрыто: ") + H(q) + (killed > 0 ? $" (принудительно: {killed})" : "");
        }

        string FreezeApp(string q, bool freeze)
        {
            var procs = ProcsOf(q);
            int n = 0;
            foreach (var p in procs)
            {
                try { if (!Critical.Contains(p.ProcessName)) { if ((freeze ? NtSuspendProcess(p.Handle) : NtResumeProcess(p.Handle)) == 0) n++; } } catch { }
                p.Dispose();
            }
            return n == 0 ? "Не нашёл такую запущенную программу (системные не трогаю)" : freeze ? $"🧊 Заморожено процессов: {n}. Разморозить: /unfreeze {H(q)}" : $"🔥 Разморожено процессов: {n}";
        }

        static string SysReport()
        {
            var sys = SystemService.I;
            int cpu = sys.CpuPercent(), ram = sys.RamPercent();
            var up = TimeSpan.FromMilliseconds(Environment.TickCount64);
            var rows = new List<string[]>
            {
                new[] { T("Компьютер"), Environment.MachineName }, new[] { "Windows", Environment.OSVersion.Version.ToString() },
                new[] { T("Процессор"), cpu + "%" }, new[] { T("Память"), ram + "%" },
                new[] { T("Работает"), $"{(int)up.TotalDays} д {up.Hours} ч" },
            };
            if (sys.HasBattery) rows.Add(new[] { T("Батарея"), sys.BatteryPercent + "%" + (sys.Charging ? " ⚡" : "") });
            foreach (var d in Maint.Disks()) rows.Add(new[] { T("Диск") + " " + d.Name, $"{Maint.Size(d.Free)} / {Maint.Size(d.Total)}" });
            var top = TopCpu(5);
            var reboot = PendingReboot();
            return "🧾 <b>Отчёт о ПК</b>\n\n" + Tbl(new[] { "Что", "Значение" }, rows) +
                   "\n\n<b>Кто грузит процессор</b>\n" + Tbl(new[] { "Программа", "CPU" }, top.Select(t => new[] { t.Name, $"{t.Pct:0.#}%" })) +
                   (reboot.Count > 0 ? "\n\n🔁 " + T("Windows ждёт перезагрузки") : "");
        }

        /// <summary>Кнопки «ap:…» и «x4:…» из сообщений 4.0.</summary>
        async Task<string> Callback4(string head, string rest, long msgId)
        {
            string no;
            var parts = rest.Split(':', 2);
            string op = parts[0], arg = parts.Length > 1 ? parts[1] : "";
            if (head == "x4" && op == "bin!")
            {
                if ((no = Need("files")) != null) return no;
                bool ok = await Ui(Maint.EmptyRecycleBin);
                await Edit(msgId, ok ? "🗑 Корзина очищена" : "Не удалось очистить корзину", null);
                return ok ? "🗑 Очищено" : "Не удалось";
            }
            if (head != "ap") return null;
            if (op == "no") { await Edit(msgId, "Отменено", null); return "Отменено"; }
            if ((no = Need("apps")) != null) return no;
            switch (op)
            {
                case "l": return await LaunchApp(arg);
                case "q": return await QuitApp(arg, false);
                case "r": return await QuitApp(arg, true);
                case "f": return FreezeApp(arg, true);
                case "uf": return FreezeApp(arg, false);
                case "u": { var r = await Command4("uninstall", arg); if (r?.Html != null) await Message(r.Value.Html, r.Value.Keys); return "🗑"; }
                case "ok":
                {
                    var a = FindInstalled(arg);
                    if (a == null || a.Uninstall == "") return "Программа не найдена";
                    await Ui(() =>
                    {
                        var cmdline = a.Uninstall.Trim();
                        var m = Regex.Match(cmdline, "^\"([^\"]+)\"\\s*(.*)$");
                        var psi = m.Success ? new ProcessStartInfo(m.Groups[1].Value, m.Groups[2].Value) : cmdline.StartsWith("MsiExec", StringComparison.OrdinalIgnoreCase)
                            ? new ProcessStartInfo("msiexec.exe", cmdline[(cmdline.IndexOf(' ') + 1)..]) : new ProcessStartInfo("cmd.exe", "/c " + cmdline);
                        psi.UseShellExecute = true;
                        Process.Start(psi)?.Dispose();
                    });
                    _installed = null;
                    await Edit(msgId, $"🗑 На ПК открыт мастер удаления <b>{H(a.Name)}</b>", null);
                    return "🗑 Мастер удаления открыт";
                }
                case "upg": case "ins":
                {
                    var id = arg;
                    if (!Regex.IsMatch(id, @"^[\w\.\-+]{2,80}$")) return "Странный id";
                    await Edit(msgId, $"{(op == "upg" ? "⬆️ Обновляю" : "📥 Устанавливаю")} <b>{H(id)}</b>… это может занять несколько минут", null);
                    _ = Task.Run(async () =>
                    {
                        var args = op == "ins" ? $"install --id {id} -e --silent --accept-package-agreements"
                                 : id == "all" ? "upgrade --all --silent --include-unknown --accept-package-agreements"
                                 : $"upgrade --id {id} -e --silent --accept-package-agreements";
                        var outp = Winget(args, 20 * 60_000);
                        bool ok = Regex.IsMatch(outp, @"(Successfully|Успешно|Успішно)", RegexOptions.IgnoreCase);
                        var tail = string.Join("\n", outp.Replace("\r", "").Split('\n').Where(l => l.Trim().Length > 2 && !Regex.IsMatch(l, @"^[\s\-\\|/█▒]+$")).TakeLast(6));
                        await Message($"{(ok ? "✅" : "⚠️")} winget: <b>{H(id)}</b>\n<blockquote expandable>{H(Cut(tail, 1500))}</blockquote>");
                        _installed = null;
                    });
                    return "⏳ Запущено";
                }
            }
            return null;
        }
    }
}
