using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Win32;
using NIsland.Core;

namespace NIsland.Services
{
    public record HealthIssue(int Penalty, string Title, string Advice, string Section);
    public record CrashInfo(DateTime At, string Kind, string App, string Detail);
    public record ProblemDevice(string Name, int Code, string Meaning);
    public record WingetUpgrade(string Name, string Id, string Current, string Available);

    /// <summary>
    /// 1.8: здоровье ПК — оценка, сбои и синие экраны, устройства с ошибками, обновления программ (winget), троттлинг,
    /// турбо-режим, место на диске, большие файлы и дубликаты, обновления Windows, TRIM, проверка памяти.
    /// Всё только читает систему, кроме явных действий по кнопке (турбо-режим, обновления, удаление дубликатов в корзину).
    /// </summary>
    public static class PcHealth
    {
        static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // ------------------------------------------------------------------
        //  Сбои: синие экраны, внезапные выключения, падения программ (журналы Windows)
        // ------------------------------------------------------------------

        static string Wevt(string log, string query, int count) =>
            Maint.RunCmd("wevtutil", $"qe {log} /q:\"{query}\" /c:{count} /rd:true /f:xml");

        public static Task<List<CrashInfo>> Crashes(int days = 30) => Task.Run(() =>
        {
            var list = new List<CrashInfo>();
            var since = DateTime.Now.AddDays(-days);
            void Parse(string xml, Func<XElement, CrashInfo> map)
            {
                if (string.IsNullOrWhiteSpace(xml)) return;
                // каждое событие отдельно: один «битый» символ в тексте события не должен ломать весь список
                foreach (var part in xml.Split("<Event ", StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!part.Contains("</Event>")) continue;
                    try
                    {
                        var clean = Regex.Replace("<Event " + part[..(part.LastIndexOf("</Event>") + 8)], @"[\x00-\x08\x0B\x0C\x0E-\x1F]", "");
                        var ev = XElement.Parse(clean);
                        var c = map(ev);
                        if (c != null && c.At >= since) list.Add(c);
                    }
                    catch { }
                }
            }
            DateTime Time(XElement ev) => DateTime.Parse(ev.Descendants().First(e => e.Name.LocalName == "TimeCreated").Attribute("SystemTime").Value).ToLocalTime();
            List<string> Data(XElement ev) => ev.Descendants().Where(e => e.Name.LocalName == "Data").Select(e => e.Value).ToList();
            int Id(XElement ev) => int.Parse(ev.Descendants().First(e => e.Name.LocalName == "EventID").Value);

            // 41 — выключился без завершения работы, 1001 (BugCheck) — синий экран, 6008 — неожиданное выключение
            Parse(Wevt("System", "*[System[(EventID=41 or EventID=6008 or EventID=1001)]]", 60), ev =>
            {
                int id = Id(ev);
                var prov = ev.Descendants().First(e => e.Name.LocalName == "Provider").Attribute("Name")?.Value ?? "";
                if (id == 1001 && !prov.Contains("BugCheck") && !prov.Contains("WER")) return null;
                return id switch
                {
                    1001 => new CrashInfo(Time(ev), "bsod", "Windows", "Синий экран" + (Data(ev).FirstOrDefault() is { Length: > 0 } code ? " · код " + code.Split(' ')[0] : "")),
                    41 => new CrashInfo(Time(ev), "power", "Windows", "Выключился без завершения работы (питание, зависание или перегрев)"),
                    _ => new CrashInfo(Time(ev), "power", "Windows", "Неожиданное выключение"),
                };
            });
            // 1000 — программа упала, 1002 — программа зависла и была закрыта
            Parse(Wevt("Application", "*[System[(EventID=1000 or EventID=1002)]]", 200), ev =>
            {
                var d = Data(ev);
                var app = d.FirstOrDefault() ?? "?";
                return new CrashInfo(Time(ev), Id(ev) == 1002 ? "hang" : "crash", app, Id(ev) == 1002 ? "зависла и была закрыта" : "аварийно закрылась" + (d.Count > 3 && d[3] != "" ? " · модуль " + d[3] : ""));
            });
            return list.OrderByDescending(c => c.At).ToList();
        });

        // ------------------------------------------------------------------
        //  Устройства с ошибками (как «жёлтый треугольник» в диспетчере устройств)
        // ------------------------------------------------------------------

        static string CodeMeaning(int code) => code switch
        {
            1 or 18 => "драйвер установлен неправильно — переустанови драйвер",
            3 => "драйвер повреждён или не хватает памяти",
            10 => "устройство не запускается — обнови драйвер с сайта производителя",
            12 => "конфликт ресурсов с другим устройством",
            14 => "нужна перезагрузка",
            19 => "повреждена запись в реестре — удали устройство и перезагрузись",
            21 or 47 => "устройство отключается",
            22 => "устройство отключено вручную",
            24 => "устройство отсутствует или работает неправильно",
            28 => "драйвер не установлен",
            31 => "Windows не может загрузить драйвер",
            39 => "драйвер повреждён или отсутствует",
            43 => "устройство сообщило о сбое — часто помогает переподключить его или обновить драйвер",
            45 => "устройство сейчас не подключено",
            52 => "у драйвера нет действительной цифровой подписи",
            _ => "ошибка " + code,
        };

        public static Task<List<ProblemDevice>> ProblemDevices() => Task.Run(() =>
        {
            var list = new List<ProblemDevice>();
            try
            {
                using var mos = new System.Management.ManagementObjectSearcher("SELECT Name, ConfigManagerErrorCode FROM Win32_PnPEntity WHERE ConfigManagerErrorCode <> 0");
                foreach (System.Management.ManagementObject o in mos.Get())
                {
                    int code = Convert.ToInt32(o["ConfigManagerErrorCode"]);
                    if (code is 45 or 22) continue;   // не подключено сейчас / отключено вручную — это не неисправность
                    list.Add(new ProblemDevice(o["Name"] as string ?? "Неизвестное устройство", code, CodeMeaning(code)));
                }
            }
            catch (Exception e) { Log.Info("devices: " + e.Message); }
            return list;
        });

        // ------------------------------------------------------------------
        //  Обновления программ (winget)
        // ------------------------------------------------------------------

        public static Task<(List<WingetUpgrade> List, string Error)> WingetUpgrades() => Task.Run(() =>
        {
            var list = new List<WingetUpgrade>();
            string text;
            try
            {
                var psi = new ProcessStartInfo("winget", "upgrade --include-unknown --disable-interactivity")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, StandardOutputEncoding = Encoding.UTF8 };
                using var p = Process.Start(psi);
                var o = p.StandardOutput.ReadToEndAsync();
                if (!p.WaitForExit(60000)) { try { p.Kill(true); } catch { } return (list, "winget не ответил за минуту"); }
                text = o.Result;
            }
            catch { return (list, "winget не установлен — поставь «Установщик приложений» из Microsoft Store"); }
            if (text.Contains("agreement", StringComparison.OrdinalIgnoreCase) || text.Contains("соглашени"))
                return (list, "Открой winget один раз в терминале и прими условия источника — потом список появится здесь");
            var lines = text.Replace("\r", "").Split('\n').Select(l => Regex.Replace(l, @"^[\s\-\\|/]+(?=\S)", "")).ToList();
            int head = lines.FindIndex(l => Regex.IsMatch(l, @"\bId\b") && (l.Contains("Version") || l.Contains("Верси")));
            if (head < 0) return (list, null);
            var h = lines[head];
            int cId = h.IndexOf("Id", StringComparison.Ordinal);
            int cVer = Regex.Match(h, @"Version|Верси[яи]").Index;
            int cAvail = Regex.Match(h, @"Available|Доступн").Index;
            int cSrc = Regex.Match(h, @"Source|Источник").Index;
            for (int i = head + 2; i < lines.Count; i++)
            {
                var l = lines[i];
                if (l.Length < cAvail || !Regex.IsMatch(l, @"\d")) continue;
                string Cut(int a, int b) => a < l.Length ? l.Substring(a, Math.Max(0, Math.Min(b < 0 ? l.Length : b, l.Length) - a)).Trim() : "";
                var item = new WingetUpgrade(Cut(0, cId), Cut(cId, cVer), Cut(cVer, cAvail), Cut(cAvail, cSrc > 0 ? cSrc : -1));
                if (item.Id != "" && item.Available != "") list.Add(item);
            }
            return (list, null);
        });

        // ------------------------------------------------------------------
        //  Процессор: частота и троттлинг
        // ------------------------------------------------------------------

        public static Task<(int PerfPct, int MhzNow, int MhzBase, bool Throttled, string Why)> Cpu() => Task.Run(() =>
        {
            int perf = -1, now = 0, bas = 0;
            try
            {
                using (var b = new System.Management.ManagementObjectSearcher("SELECT MaxClockSpeed FROM Win32_Processor"))
                    foreach (System.Management.ManagementObject o in b.Get()) bas = Convert.ToInt32(o["MaxClockSpeed"]);
                var samples = new List<int>();
                for (int i = 0; i < 3; i++)
                {
                    using var mos = new System.Management.ManagementObjectSearcher("SELECT PercentProcessorPerformance FROM Win32_PerfFormattedData_Counters_ProcessorInformation WHERE Name='_Total'");
                    foreach (System.Management.ManagementObject o in mos.Get()) samples.Add(Convert.ToInt32(o["PercentProcessorPerformance"]));
                    System.Threading.Thread.Sleep(400);
                }
                if (samples.Count > 0) perf = (int)samples.Average();
                now = bas * Math.Max(perf, 0) / 100;
            }
            catch (Exception e) { Log.Info("cpu perf: " + e.Message); }
            var sys = SystemService.I;
            bool onBattery = sys.HasBattery && !sys.Charging;
            int load = sys.CpuPercent();
            bool throttled = perf >= 0 && perf < 60 && load > 50;
            string why = perf < 0 ? "Windows не отдаёт данные о частоте"
                : throttled ? (onBattery ? "Работает от батареи в режиме экономии — частота снижена. От зарядки или в режиме «Производительность» будет быстрее"
                                         : "Процессор нагружен, а частота низкая — возможно, перегрев: почисти вентиляцию, не держи ноутбук на мягком")
                : perf > 100 ? "Турбо-буст работает — процессор разогнан выше базовой частоты ✓"
                : "Частота в норме";
            return (perf, now, bas, throttled, why);
        });

        // ------------------------------------------------------------------
        //  Турбо-режим: максимум скорости на время игры или тяжёлой работы, потом всё как было
        // ------------------------------------------------------------------

        public static bool TurboOn => !string.IsNullOrEmpty(Store.Settings.TurboPrev);

        public static async Task<string> Turbo(bool on)
        {
            var s = Store.Settings;
            if (on)
            {
                if (TurboOn) return "Уже включён";
                var plans = Maint.PowerPlans();
                var cur = plans.FirstOrDefault(p => p.Active);
                var fast = plans.FirstOrDefault(p => p.Guid.Equals("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c", StringComparison.OrdinalIgnoreCase))
                           ?? plans.FirstOrDefault(p => p.Name.Contains("High", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("Высок", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("Ultimate", StringComparison.OrdinalIgnoreCase));
                s.TurboPrev = (cur?.Guid ?? "-") + "|" + s.Mode + "|" + (SystemService.I.KeepAwake ? "1" : "0");
                if (fast != null) Maint.SetPowerPlan(fast.Guid);
                SystemService.I.KeepAwake = true;
                App.Island?.SetMode("game");
                long freed = await Maint.OptimizeRam();
                return (fast != null ? "Схема «" + fast.Name + "»" : "Схемы «Высокая производительность» нет — питание без изменений") + $" · освобождено {Maint.Size(freed)} памяти · ПК не уснёт · остров в режиме «Игра»";
            }
            if (!TurboOn) return "Турбо-режим не включён";
            var p = s.TurboPrev.Split('|');
            if (p[0] != "-") Maint.SetPowerPlan(p[0]);
            if (p.Length > 1) App.Island?.SetMode(p[1]);
            if (p.Length > 2) SystemService.I.KeepAwake = p[2] == "1";
            s.TurboPrev = "";
            return "Всё вернул как было";
        }

        // ------------------------------------------------------------------
        //  Что занимает диск
        // ------------------------------------------------------------------

        static long DirSize(string dir, int maxMs, out bool partial)
        {
            long sum = 0;
            partial = false;
            var sw = Stopwatch.StartNew();
            try
            {
                // облачные заглушки (Google Drive, OneDrive) показывают полный размер, но места на диске не занимают
                var skip = FileAttributes.ReparsePoint | FileAttributes.Offline | (FileAttributes)0x40000 | (FileAttributes)0x400000;
                foreach (var f in new DirectoryInfo(dir).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = skip }))
                {
                    // большие разрежённые файлы (кэши, диски виртуальных машин) реально занимают меньше, чем их «длина»
                    sum += f.Length > 512L * 1024 * 1024 ? OnDisk(f.FullName, f.Length) : f.Length;
                    if (sw.ElapsedMilliseconds > maxMs) { partial = true; break; }
                }
            }
            catch { }
            return sum;
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        static extern uint GetCompressedFileSizeW(string name, out uint high);

        static long OnDisk(string path, long fallback)
        {
            uint lo = GetCompressedFileSizeW(path, out var hi);
            if (lo == 0xFFFFFFFF && System.Runtime.InteropServices.Marshal.GetLastWin32Error() != 0) return fallback;
            return ((long)hi << 32) | lo;
        }

        public static Task<List<(string Name, long Bytes, string Path, string Hint)>> DiskUsage() => Task.Run(() =>
        {
            var list = new List<(string, long, string, string)>();
            var sysDrive = Path.GetPathRoot(Environment.SystemDirectory);
            foreach (var (file, name, hint) in new[]
            {
                ("hiberfil.sys", "Файл гибернации", "Можно убрать, если не пользуешься гибернацией: команда powercfg -h off от администратора"),
                ("pagefile.sys", "Файл подкачки", "Нужен Windows — не удаляй"),
                ("swapfile.sys", "Файл подкачки приложений", "Нужен Windows"),
            })
            {
                try { var fi = new DirectoryInfo(sysDrive).EnumerateFiles(file, new EnumerationOptions { AttributesToSkip = 0 }).FirstOrDefault(); if (fi != null) list.Add((name, fi.Length, fi.FullName, hint)); } catch { }
            }
            var old = Path.Combine(sysDrive, "Windows.old");
            if (Directory.Exists(old)) list.Add(("Старая Windows (Windows.old)", DirSize(old, 4000, out _), old, "Копия прошлой версии Windows — удаляется в «Глубокой очистке» после обновления"));
            var bin = Maint.RecycleBin();
            if (bin.Bytes > 0) list.Add(("Корзина", bin.Bytes, null, "Очисти корзину, если там ничего не нужно"));
            foreach (var d in new DirectoryInfo(Home).EnumerateDirectories().Where(d => !d.Attributes.HasFlag(FileAttributes.ReparsePoint)))
            {
                if ((d.Name.StartsWith(".") && d.Name.Length < 3) || d.Name.Equals("AppData", StringComparison.OrdinalIgnoreCase)) continue;   // AppData — ниже, по программам
                var size = DirSize(d.FullName, 2500, out var partial);
                if (size > 300L * 1024 * 1024) list.Add(("Папка «" + d.Name + "»" + (partial ? " (больше)" : ""), size, d.FullName, null));
            }
            var appData = Path.Combine(Home, "AppData");
            foreach (var sub in new[] { "Local", "Roaming" })
            {
                var dir = Path.Combine(appData, sub);
                if (!Directory.Exists(dir)) continue;
                foreach (var d in new DirectoryInfo(dir).EnumerateDirectories())
                {
                    var size = DirSize(d.FullName, 1200, out var partial);
                    if (size > 700L * 1024 * 1024) list.Add(("Данные программы «" + d.Name + "»" + (partial ? " (больше)" : ""), size, d.FullName, "Кэши браузеров и мессенджеров можно чистить в «Глубокой очистке»"));
                }
            }
            return list.OrderByDescending(x => x.Item2).Take(25).ToList();
        });

        // ------------------------------------------------------------------
        //  Большие файлы и дубликаты
        // ------------------------------------------------------------------

        static IEnumerable<string> UserRoots() => new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop), Path.Combine(Home, "Downloads"), Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
        }.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase);

        static IEnumerable<FileInfo> UserFiles() =>
            UserRoots().SelectMany(r =>
            {
                try { return new DirectoryInfo(r).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.System | FileAttributes.ReparsePoint }); }
                catch { return Enumerable.Empty<FileInfo>(); }
            });

        public static Task<List<FileInfo>> LargeFiles(int top = 30) => Task.Run(() =>
            UserFiles().Where(f => f.Length > 100L * 1024 * 1024).OrderByDescending(f => f.Length).Take(top).ToList());

        /// <summary>Одинаковые файлы (по размеру, затем по SHA-256). В каждой группе первым — самый старый (оригинал).</summary>
        public static Task<List<List<FileInfo>>> Duplicates(Action<string> progress) => Task.Run(() =>
        {
            var bySize = UserFiles().Where(f => f.Length > 256 * 1024).GroupBy(f => f.Length).Where(g => g.Count() > 1).ToList();
            var res = new List<List<FileInfo>>();
            int done = 0;
            foreach (var g in bySize)
            {
                if (++done % 20 == 0) progress?.Invoke($"Сравниваю файлы… {done} / {bySize.Count}");
                var byHash = new Dictionary<string, List<FileInfo>>();
                foreach (var f in g)
                {
                    try
                    {
                        using var s = f.OpenRead();
                        var h = Convert.ToHexString(SHA256.HashData(s));
                        if (!byHash.TryGetValue(h, out var l)) byHash[h] = l = new();
                        l.Add(f);
                    }
                    catch { }
                }
                res.AddRange(byHash.Values.Where(l => l.Count > 1).Select(l => l.OrderBy(x => x.CreationTime).ToList()));
            }
            return res.OrderByDescending(l => l[0].Length * (l.Count - 1)).ToList();
        });

        // ------------------------------------------------------------------
        //  Обновления Windows, TRIM, память
        // ------------------------------------------------------------------

        public static Task<(DateTime? LastUpdate, bool RebootPending, int DaysSinceBoot)> WindowsUpdate() => Task.Run(() =>
        {
            DateTime? last = null;
            try
            {
                using var mos = new System.Management.ManagementObjectSearcher("SELECT InstalledOn FROM Win32_QuickFixEngineering");
                foreach (System.Management.ManagementObject o in mos.Get())
                    if (DateTime.TryParse(o["InstalledOn"] as string, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d) && (last == null || d > last)) last = d;
            }
            catch { }
            bool reboot = false;
            try
            {
                using var a = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired");
                using var b = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending");
                reboot = a != null || b != null;
            }
            catch { }
            return (last, reboot, (int)Maint.Uptime.TotalDays);
        });

        public static string TrimState()
        {
            var o = Maint.RunCmd("fsutil", "behavior query DisableDeleteNotify");
            if (string.IsNullOrEmpty(o)) return "посмотри в «Оптимизации дисков»";
            var m = Regex.Match(o, @"NTFS[^=]*=\s*(\d)");
            return !m.Success ? "посмотри в «Оптимизации дисков»" : m.Groups[1].Value == "0" ? "включён ✓" : "выключен — SSD со временем медленнее";
        }

        // ------------------------------------------------------------------
        //  Общая оценка
        // ------------------------------------------------------------------

        public static async Task<(int Score, List<HealthIssue> Issues)> Score(Action<string> progress)
        {
            var issues = new List<HealthIssue>();
            progress("Диски…");
            foreach (var d in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
            {
                double free = d.AvailableFreeSpace * 100.0 / Math.Max(1, d.TotalSize);
                if (free < 8) issues.Add(new(18, $"Диск {d.Name.TrimEnd('\\')} почти заполнен: свободно {free:0}%", "Когда места меньше 10%, Windows тормозит и не может обновиться. Почисти мусор или перенеси фото и видео", "clean"));
                else if (free < 15) issues.Add(new(8, $"На диске {d.Name.TrimEnd('\\')} свободно {free:0}%", "Желательно держать свободными хотя бы 15%", "clean"));
            }
            progress("Сбои…");
            var crashes = await Crashes(30);
            int bsod = crashes.Count(c => c.Kind == "bsod"), power = crashes.Count(c => c.Kind == "power" && c.At > DateTime.Now.AddDays(-14));
            var appCrash = crashes.Where(c => c.Kind is "crash" or "hang" && c.At > DateTime.Now.AddDays(-14)).GroupBy(c => c.App).OrderByDescending(g => g.Count()).FirstOrDefault();
            if (bsod > 0) issues.Add(new(Math.Min(25, 10 * bsod), $"Синих экранов за месяц: {bsod}", "Частая причина — драйвер видеокарты или Wi-Fi, перегрев, неисправная память. Обнови драйверы и проверь память", "health"));
            if (power > 1) issues.Add(new(8, $"Внезапных выключений за 2 недели: {power}", "Компьютер выключался без завершения работы: зависание, разряд батареи или перегрев", "health"));
            if (appCrash != null && appCrash.Count() >= 3) issues.Add(new(6, $"{appCrash.Key} падала {appCrash.Count()} раз за 2 недели", "Обнови программу или переустанови её", "health"));
            progress("Устройства…");
            var devs = await ProblemDevices();
            if (devs.Count > 0) issues.Add(new(Math.Min(15, 5 * devs.Count), $"Устройств с ошибками: {devs.Count}", string.Join("; ", devs.Take(2).Select(d => d.Name + " — " + d.Meaning)), "health"));
            progress("Обновления…");
            var wu = await WindowsUpdate();
            if (wu.LastUpdate is DateTime lu && (DateTime.Now - lu).TotalDays > 45) issues.Add(new(10, $"Windows не обновлялась {(int)(DateTime.Now - lu).TotalDays} дней", "Без обновлений остаются дыры, через которые заражают ПК", "health"));
            if (wu.RebootPending) issues.Add(new(5, "Ждёт перезагрузки для установки обновлений", "Перезагрузи ПК, когда будет удобно", "health"));
            if (wu.DaysSinceBoot >= 7) issues.Add(new(5, $"Компьютер не перезагружался {wu.DaysSinceBoot} дней", "Перезагрузка раз в неделю освобождает память и устраняет накопившиеся сбои", "health"));
            progress("Память и автозапуск…");
            int ram = SystemService.I.RamPercent();
            if (ram >= 88) issues.Add(new(10, $"Память занята на {ram}%", "Закрой лишние вкладки и программы — когда память кончается, всё тормозит", "perf"));
            int startup = Maint.Startup().Count(i => i.Enabled);
            if (startup > 12) issues.Add(new(6, $"В автозапуске {startup} программ", "Чем больше программ стартует с Windows, тем дольше включение и меньше памяти", "apps"));
            if (SystemService.I.HasBattery)
            {
                var b = await Maint.Battery();
                if (b != null && b.Design > 0 && b.Full * 100 / b.Design < 70) issues.Add(new(6, $"Батарея изношена: {b.Full * 100 / b.Design}% ёмкости", "Ноутбук работает от батареи заметно меньше. Батарею можно заменить", "power"));
            }
            progress("Мусор…");
            var junk = await Maint.ScanTemp();
            if (junk.Bytes > 3L * 1024 * 1024 * 1024) issues.Add(new(5, $"Временных файлов: {Maint.Size(junk.Bytes)}", "Их можно спокойно удалить", "clean"));
            int score = Math.Max(0, 100 - issues.Sum(i => i.Penalty));
            return (score, issues.OrderByDescending(i => i.Penalty).ToList());
        }
    }
}
