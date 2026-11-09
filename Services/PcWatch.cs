using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Threading;
using NIsland.Core;

namespace NIsland.Services
{
    /// <summary>Один день отслеживания. Хранится в tracker.json (14 дней), запись — раз в 5 минут.</summary>
    public class TrackDay
    {
        public string Date { get; set; } = "";
        /// <summary>Секунд за ПК (был ввод с клавиатуры или мыши за последние 2 минуты).</summary>
        public int ActiveSec { get; set; }
        /// <summary>Программа на переднем плане → секунд.</summary>
        public Dictionary<string, int> Apps { get; set; } = new();
        public long Down { get; set; }
        public long Up { get; set; }
        /// <summary>[минута дня, %, заряжается 0/1] раз в 10 минут.</summary>
        public List<int[]> Bat { get; set; } = new();
        /// <summary>[минута дня, сигнал %] раз в 5 минут.</summary>
        public List<int[]> Wifi { get; set; } = new();
        /// <summary>[ЧЧ:мм, вид, текст]: boot, lock, unlock, sleep, wake, usb, ip, proc, limit, download, alarm, load.</summary>
        public List<string[]> Events { get; set; } = new();
        /// <summary>Уведомления по программам.</summary>
        public Dictionary<string, int> Notifs { get; set; } = new();
        /// <summary>[ЧЧ:мм, загрузка Мбит/с, задержка мс].</summary>
        public List<string[]> Speed { get; set; } = new();
        public int Blocked { get; set; }
    }

    /// <summary>
    /// Отслеживание и управление 2.4: экранное время и программы, сессии (вход, блокировка, сон), история заряда и Wi-Fi,
    /// трафик, журнал USB, внешний IP, новые загрузки, уведомления, скорость интернета; лимиты времени в программах,
    /// фокус-блокировщик, слежение за процессами, сторож нагрузки, будильники, ежедневный отчёт в Telegram, обои по кругу.
    /// Один таймер раз в 10 секунд; тяжёлое (сеть, Wi-Fi, IP) — реже и в фоне.
    /// </summary>
    public static class PcWatch
    {
        static Settings S => Store.Settings;
        static string FilePath => Path.Combine(Store.Dir, "tracker.json");
        static Dictionary<string, TrackDay> _days = new();
        static readonly object Gate = new();
        static DispatcherTimer _timer;
        static int _tick;
        static bool _dirty;
        static string _lastIp;
        public static DateTime BootTime { get; } = DateTime.Now - TimeSpan.FromMilliseconds(Environment.TickCount64);

        /// <summary>Сообщение «на телефон» (Telegram-бот, если подключён). Подключает TelegramHub.</summary>
        public static Action<string> ToPhone;
        public static event Action Changed;

        // ------------------------------------------------------------------
        //  Хранение
        // ------------------------------------------------------------------

        static string Key(DateTime d) => d.ToString("yyyy-MM-dd");

        public static TrackDay Day(DateTime d)
        {
            lock (Gate)
            {
                var k = Key(d);
                if (!_days.TryGetValue(k, out var t)) { t = new TrackDay { Date = k }; _days[k] = t; }
                return t;
            }
        }

        public static TrackDay Today => Day(DateTime.Today);

        /// <summary>Копия дня — для бота и виджетов (их потоки читают, пока таймер пишет).</summary>
        public static TrackDay Snap(DateTime d)
        {
            lock (Gate) return JsonSerializer.Deserialize<TrackDay>(JsonSerializer.Serialize(Day(d)));
        }

        /// <summary>Последние n дней (сегодня первым), копии.</summary>
        public static List<TrackDay> Days(int n) => Enumerable.Range(0, n).Select(i => Snap(DateTime.Today.AddDays(-i))).ToList();

        static void Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                var doc = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(FilePath));
                if (doc == null) return;
                if (doc.TryGetValue("days", out var d)) _days = JsonSerializer.Deserialize<Dictionary<string, TrackDay>>(d.GetRawText()) ?? new();
                if (doc.TryGetValue("ip", out var ip)) _lastIp = ip.GetString();
            }
            catch (Exception e) { Log.Info("tracker load: " + e.Message); }
        }

        public static void Save()
        {
            if (Store.ReadOnly) return;   // самопроверки ничего не пишут
            try
            {
                string json;
                lock (Gate)
                {
                    var cut = Key(DateTime.Today.AddDays(-14));
                    foreach (var k in _days.Keys.Where(k => string.CompareOrdinal(k, cut) < 0).ToList()) _days.Remove(k);
                    json = JsonSerializer.Serialize(new Dictionary<string, object> { ["days"] = _days, ["ip"] = _lastIp });
                    _dirty = false;
                }
                var tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, json);
                File.Move(tmp, FilePath, true);
            }
            catch (Exception e) { Log.Info("tracker save: " + e.Message); }
        }

        public static void Event(string kind, string text)
        {
            lock (Gate)
            {
                var t = Today;
                t.Events.Add(new[] { DateTime.Now.ToString("HH:mm"), kind, text });
                if (t.Events.Count > 300) t.Events.RemoveAt(0);
                _dirty = true;
            }
            Changed?.Invoke();
        }

        // ------------------------------------------------------------------
        //  Запуск
        // ------------------------------------------------------------------

        public static void Start()
        {
            Load();
            ToPhone ??= html => { try { Integrations.All.OfType<TelegramHub>().FirstOrDefault()?.PushText(html); } catch { } };
            if (BootTime.Date == DateTime.Today && !Today.Events.Any(e => e[1] == "boot"))
                lock (Gate) Today.Events.Insert(0, new[] { BootTime.ToString("HH:mm"), "boot", "ПК включён" });
            Microsoft.Win32.SystemEvents.SessionSwitch += (_, e) =>
            {
                if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionLock) Event("lock", "ПК заблокирован");
                else if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionUnlock) Event("unlock", "ПК разблокирован");
            };
            Microsoft.Win32.SystemEvents.PowerModeChanged += (_, e) =>
            {
                if (e.Mode == Microsoft.Win32.PowerModes.Suspend) { Event("sleep", "Сон"); Save(); }
                else if (e.Mode == Microsoft.Win32.PowerModes.Resume) Event("wake", "Пробуждение");
            };
            NotificationLog.Changed += CountNotifs;
            WatchDownloads();
            AppDomain.CurrentDomain.ProcessExit += (_, __) => Save();
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            _timer.Tick += (_, __) => { try { Tick(); } catch (Exception e) { Log.Info("tracker: " + e.Message); } };
            _timer.Start();
        }

        // ------------------------------------------------------------------
        //  Раз в 10 секунд
        // ------------------------------------------------------------------

        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
        [StructLayout(LayoutKind.Sequential)] struct LASTINPUTINFO { public uint cbSize, dwTime; }
        [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO p);

        public static double IdleSeconds()
        {
            var li = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
            return GetLastInputInfo(ref li) ? unchecked((uint)Environment.TickCount - li.dwTime) / 1000.0 : 0;
        }

        static readonly Dictionary<uint, string> _pidName = new();
        static string ForegroundApp(out IntPtr hwnd)
        {
            hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return null;
            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0) return null;
            if (_pidName.TryGetValue(pid, out var n)) return n;
            try { using var p = Process.GetProcessById((int)pid); n = p.ProcessName; } catch { return null; }
            if (_pidName.Count > 400) _pidName.Clear();
            return _pidName[pid] = n;
        }

        static long _lastDown = -1, _lastUp = -1;
        static HashSet<string> _usb;
        static HashSet<string> _procsUp = new(StringComparer.OrdinalIgnoreCase);
        static readonly HashSet<string> _limitWarned = new(StringComparer.OrdinalIgnoreCase);
        static DateTime _focusToastAt, _loadAt, _cpuHighSince = DateTime.MinValue, _ramHighSince = DateTime.MinValue;
        static string _reportSent, _alarmFired;

        static void Tick()
        {
            _tick++;
            if (!S.TrackOn) return;
            var now = DateTime.Now;
            var t = Today;
            bool active = IdleSeconds() < 120;
            var app = ForegroundApp(out var hwnd);
            lock (Gate)
            {
                if (active)
                {
                    t.ActiveSec += 10;
                    if (!string.IsNullOrEmpty(app) && app != "Idle") t.Apps[app] = t.Apps.GetValueOrDefault(app) + 10;
                }
                _dirty = true;
            }

            // лимиты времени в программах
            if (active && app != null && Limits().TryGetValue(app, out var limitMin) && t.Apps.GetValueOrDefault(app) >= limitMin * 60)
            {
                if (_limitWarned.Add(app + "|" + Key(now)))
                {
                    Event("limit", $"{app}: лимит {limitMin} мин на сегодня исчерпан");
                    Toast("Лимит времени: " + app, $"Сегодня уже {limitMin} мин");
                    ToPhone?.Invoke($"⏳ <b>{H(app)}</b>: лимит {limitMin} мин на сегодня исчерпан");
                }
                if (S.LimitAction == "minimize" && hwnd != IntPtr.Zero) ShowWindow(hwnd, 6 /*SW_MINIMIZE*/);
            }

            // фокус-блокировщик
            if (FocusUntil is DateTime fu)
            {
                if (now > fu) { FocusUntil = null; Event("focus", "Фокус-сессия закончилась"); Toast("Фокус закончился", "Отличная работа!"); ToPhone?.Invoke("🎯 Фокус-сессия закончилась"); }
                else if (app != null && hwnd != IntPtr.Zero && FocusApps().Contains(app))
                {
                    ShowWindow(hwnd, 6);
                    lock (Gate) t.Blocked++;
                    if ((now - _focusToastAt).TotalSeconds > 30) { _focusToastAt = now; Toast("Фокус: " + app + " свёрнут", $"Ещё {(int)Math.Ceiling((fu - now).TotalMinutes)} мин"); }
                }
            }

            if (AwakeUntil is DateTime au && now >= au) SetAwake(false, 0);
            if (_tick % 6 == 0) Minute(now);                    // раз в минуту
            if (_tick % 30 == 3) _ = Task.Run(Wifi);            // раз в 5 минут
            if (_tick % 60 == 5) Battery(now);                  // раз в 10 минут
            if (_tick % 90 == 7) _ = Task.Run(CheckIp);         // раз в 15 минут
            if (_tick % 30 == 0 && _dirty) Save();              // раз в 5 минут
            Usb();
            if (!string.IsNullOrWhiteSpace(S.WatchProcs)) Procs();
            Alarms(now);
        }

        static void Minute(DateTime now)
        {
            // трафик
            try
            {
                long d = 0, u = 0;
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                    var st = ni.GetIPStatistics();
                    d += st.BytesReceived; u += st.BytesSent;
                }
                if (_lastDown >= 0 && d >= _lastDown && u >= _lastUp) lock (Gate) { Today.Down += d - _lastDown; Today.Up += u - _lastUp; }
                _lastDown = d; _lastUp = u;
            }
            catch { }

            var sys = SystemService.I;
            if (sys.HasBattery)
                lock (_batMin)
                {
                    if (_batMin.Count > 0 && _batMin[^1].Chg != sys.Charging) _batMin.Clear();   // включили или выключили зарядку — считаем заново
                    _batMin.Add((now, sys.BatteryPercent, sys.Charging));
                    if (_batMin.Count > 40) _batMin.RemoveAt(0);
                }

            // сторож нагрузки
            if (S.LoadAlertCpu > 0)
            {
                if (sys.CpuPercent() >= S.LoadAlertCpu) { if (_cpuHighSince == DateTime.MinValue) _cpuHighSince = now; } else _cpuHighSince = DateTime.MinValue;
                if (_cpuHighSince != DateTime.MinValue && (now - _cpuHighSince).TotalMinutes >= 5 && (now - _loadAt).TotalMinutes > 30)
                {
                    _loadAt = now;
                    var top = string.Join(", ", TopRam(3).Select(x => x.Name));
                    Event("load", $"Процессор ≥ {S.LoadAlertCpu}% уже 5 мин");
                    ToPhone?.Invoke($"🔥 Процессор загружен на {sys.CpuPercent()}% уже 5 минут. Больше всего памяти: {H(top)} — /apps");
                }
            }
            if (S.LoadAlertRam > 0)
            {
                if (sys.RamPercent() >= S.LoadAlertRam) { if (_ramHighSince == DateTime.MinValue) _ramHighSince = now; } else _ramHighSince = DateTime.MinValue;
                if (_ramHighSince != DateTime.MinValue && (now - _ramHighSince).TotalMinutes >= 5 && (now - _loadAt).TotalMinutes > 30)
                {
                    _loadAt = now;
                    Event("load", $"Память ≥ {S.LoadAlertRam}% уже 5 мин");
                    ToPhone?.Invoke($"🧠 Память занята на {sys.RamPercent()}% уже 5 минут. Больше всего: {H(string.Join(", ", TopRam(3).Select(x => x.Name)))} — /ramtop");
                }
            }

            // ежедневный отчёт
            if (!string.IsNullOrEmpty(S.DailyReportTime) && now.ToString("HH:mm") == S.DailyReportTime && _reportSent != Key(now))
            {
                _reportSent = Key(now);
                ToPhone?.Invoke(Report(now.Date));
            }
        }

        static void Battery(DateTime now)
        {
            var sys = SystemService.I;
            if (!sys.HasBattery) return;
            lock (Gate) { Today.Bat.Add(new[] { now.Hour * 60 + now.Minute, sys.BatteryPercent, sys.Charging ? 1 : 0 }); _dirty = true; }
        }

        static async Task Wifi()
        {
            try
            {
                var l = await WifiTools.Current();
                if (l == null) return;
                var now = DateTime.Now;
                lock (Gate) { Today.Wifi.Add(new[] { now.Hour * 60 + now.Minute, l.Signal }); _dirty = true; }
            }
            catch { }
        }

        static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

        public static string LastIp => _lastIp;

        public static Task CheckIpNow() => CheckIp();

        static async Task CheckIp()
        {
            try
            {
                var ip = (await Http.GetStringAsync("https://api.ipify.org")).Trim();
                if (ip.Length is < 7 or > 45) return;
                if (_lastIp != null && ip != _lastIp)
                {
                    Event("ip", $"{_lastIp} → {ip}");
                    if (S.IpAlert) ToPhone?.Invoke($"🌐 Внешний IP сменился: <code>{H(_lastIp)}</code> → <code>{H(ip)}</code>" + (Vpn.Active ? " (VPN включён)" : ""));
                }
                _lastIp = ip;
                _dirty = true;
            }
            catch { }
        }

        static void Usb()
        {
            HashSet<string> now;
            try { now = DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Removable && d.IsReady).Select(d => d.Name.TrimEnd('\\') + " " + Safe(() => d.VolumeLabel)).ToHashSet(); }
            catch { return; }
            if (_usb != null)
            {
                foreach (var n in now.Where(n => !_usb.Contains(n))) Event("usb", "+ " + n.Trim());
                foreach (var n in _usb.Where(n => !now.Contains(n))) Event("usb", "− " + n.Trim());
            }
            _usb = now;
        }

        static string Safe(Func<string> f) { try { return f(); } catch { return ""; } }

        static void Procs()
        {
            var want = S.WatchProcs.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var running = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var w in want) { try { if (Process.GetProcessesByName(w).Length > 0) running.Add(w); } catch { } }
            foreach (var n in running.Where(n => !_procsUp.Contains(n))) { Event("proc", n + " запущен"); ToPhone?.Invoke($"▶ На ПК запущен <b>{H(n)}</b>"); }
            foreach (var n in _procsUp.Where(n => want.Contains(n) && !running.Contains(n))) { Event("proc", n + " закрыт"); ToPhone?.Invoke($"⏹ На ПК закрыт <b>{H(n)}</b>"); }
            _procsUp = running;
        }

        static DateTime _notifAt = DateTime.Now;
        static void CountNotifs()
        {
            try
            {
                var fresh = NotificationLog.Items.Where(i => i.Time > _notifAt).ToList();
                if (fresh.Count == 0) return;
                _notifAt = fresh.Max(i => i.Time);
                lock (Gate) foreach (var i in fresh) { var a = string.IsNullOrEmpty(i.App) ? "?" : i.App; Today.Notifs[a] = Today.Notifs.GetValueOrDefault(a) + 1; _dirty = true; }
            }
            catch { }
        }

        // ------------------------------------------------------------------
        //  Новые загрузки
        // ------------------------------------------------------------------

        public static readonly List<(DateTime At, string Path)> Downloads = new();
        static FileSystemWatcher _dl;

        public static string DownloadsDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

        static void WatchDownloads()
        {
            try
            {
                if (!Directory.Exists(DownloadsDir)) return;
                _dl = new FileSystemWatcher(DownloadsDir) { IncludeSubdirectories = false, NotifyFilter = NotifyFilters.FileName };
                void On(string path)
                {
                    var ext = Path.GetExtension(path).ToLowerInvariant();
                    if (ext is ".crdownload" or ".part" or ".tmp" or ".download" or ".opdownload" || Path.GetFileName(path).StartsWith("~$")) return;
                    lock (Downloads)
                    {
                        if (Downloads.Any(d => d.Path.Equals(path, StringComparison.OrdinalIgnoreCase))) return;
                        Downloads.Insert(0, (DateTime.Now, path));
                        if (Downloads.Count > 30) Downloads.RemoveAt(30);
                    }
                    Event("download", Path.GetFileName(path));
                    if (S.DownloadsNotify) ToPhone?.Invoke("📥 Скачан файл: <code>" + H(Path.GetFileName(path)) + "</code> — /downloads");
                }
                _dl.Created += (_, e) => On(e.FullPath);
                _dl.Renamed += (_, e) => On(e.FullPath);
                _dl.EnableRaisingEvents = true;
            }
            catch (Exception e) { Log.Info("downloads watch: " + e.Message); }
        }

        /// <summary>Последние файлы в «Загрузках» (из наблюдения и с диска).</summary>
        public static List<(DateTime At, string Path)> RecentDownloads(int n)
        {
            var list = new List<(DateTime, string)>();
            lock (Downloads) list.AddRange(Downloads.Where(d => File.Exists(d.Path) && !Path.GetFileName(d.Path).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)));
            try
            {
                foreach (var f in new DirectoryInfo(DownloadsDir).EnumerateFiles().Where(f => (f.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0 && !f.Name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
                             .OrderByDescending(f => f.LastWriteTime).Take(n))
                    if (!list.Any(x => x.Item2.Equals(f.FullName, StringComparison.OrdinalIgnoreCase))) list.Add((f.LastWriteTime, f.FullName));
            }
            catch { }
            return list.OrderByDescending(x => x.Item1).Take(n).ToList();
        }

        // ------------------------------------------------------------------
        //  Управление
        // ------------------------------------------------------------------

        /// <summary>Лимиты: «chrome=120;steam=60» → программа → минут в день.</summary>
        public static Dictionary<string, int> Limits()
        {
            var d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in (S.AppLimits ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = p.Split('=');
                if (kv.Length == 2 && int.TryParse(kv[1], out var m) && m > 0 && kv[0].Trim() != "") d[kv[0].Trim()] = m;
            }
            return d;
        }

        public static void SetLimit(string app, int minutes)
        {
            var d = Limits();
            if (minutes <= 0) d.Remove(app); else d[app] = minutes;
            S.AppLimits = string.Join(";", d.Select(x => $"{x.Key}={x.Value}"));
            _limitWarned.RemoveWhere(x => x.StartsWith(app + "|", StringComparison.OrdinalIgnoreCase));
        }

        public static DateTime? FocusUntil { get; private set; }

        public static HashSet<string> FocusApps() =>
            (S.FocusBlockApps ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);

        public static void StartFocus(int minutes)
        {
            FocusUntil = minutes > 0 ? DateTime.Now.AddMinutes(minutes) : null;
            Event("focus", minutes > 0 ? $"Фокус на {minutes} мин" : "Фокус остановлен");
            Changed?.Invoke();
        }

        /// <summary>Будильники: «07:30|Подъём;13:00|Обед».</summary>
        public static List<(string Time, string Label)> AlarmList() =>
            (S.Alarms ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries).Select(a => a.Split('|', 2))
                .Where(p => System.Text.RegularExpressions.Regex.IsMatch(p[0].Trim(), @"^\d{1,2}:\d{2}$"))
                .Select(p => (p[0].Trim().PadLeft(5, '0'), p.Length > 1 ? p[1].Trim() : "Будильник")).OrderBy(x => x.Item1).ToList();

        public static void SetAlarms(IEnumerable<(string Time, string Label)> list) =>
            S.Alarms = string.Join(";", list.Select(a => a.Time + "|" + a.Label.Replace(";", ",").Replace("|", "/")));

        static readonly List<(DateTime At, string Label)> _snoozed = new();

        public static void Snooze(string label, int minutes = 10) { lock (_snoozed) _snoozed.Add((DateTime.Now.AddMinutes(minutes), label)); }

        static void Alarms(DateTime now)
        {
            var hm = now.ToString("HH:mm");
            foreach (var (time, label) in AlarmList())
                if (time == hm && _alarmFired != hm + label) { _alarmFired = hm + label; Ring(label); }
            lock (_snoozed)
                foreach (var s in _snoozed.Where(s => now >= s.At).ToList()) { _snoozed.Remove(s); Ring(s.Label); }
        }

        static void Ring(string label)
        {
            Event("alarm", label);
            for (int i = 0; i < 4; i++)
            {
                int k = i;
                _ = Task.Delay(k * 2500).ContinueWith(_ => System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() => Sound.Play(SoundEvent.Alert))));
            }
            App.Island?.ShowActivity(new Activity
            {
                Kind = "alert", Glyph = "", Tint = System.Windows.Media.Color.FromRgb(0xFF, 0x9F, 0x0A), Title = "⏰ " + label, Sub = DateTime.Now.ToString("HH:mm"),
                Seconds = 60, Wide = true, ActionText = "Отложить 10 мин", Action = () => Snooze(label),
            });
            ToPhone?.Invoke("⏰ <b>" + H(label) + "</b> · " + DateTime.Now.ToString("HH:mm"));
        }

        // ---- «не засыпать» на время ----
        public static DateTime? AwakeUntil { get; private set; }

        /// <summary>Не давать ПК засыпать: hours = 0 — без срока.</summary>
        public static void SetAwake(bool on, int hours)
        {
            SystemService.I.KeepAwake = on;
            AwakeUntil = on && hours > 0 ? DateTime.Now.AddHours(hours) : null;
            Changed?.Invoke();
        }

        // ---- батарея: сколько осталось до разряда и до полной ----
        static readonly List<(DateTime At, int Pct, bool Chg)> _batMin = new();

        /// <summary>Скорость заряда/разряда, % в час, по замерам раз в минуту (нужно ≥ 4 мин и изменение ≥ 1 %).</summary>
        static double? BatRate(bool charging)
        {
            lock (_batMin)
            {
                if (_batMin.Count < 2 || _batMin[^1].Chg != charging) return null;
                var a = _batMin[0]; var b = _batMin[^1];
                double h = (b.At - a.At).TotalHours, dp = Math.Abs(b.Pct - a.Pct);
                return h >= 4 / 60.0 && dp >= 1 ? dp / h : null;
            }
        }

        /// <summary>Сколько до полной зарядки (null — ещё не понятно).</summary>
        public static TimeSpan? ChargeLeft()
        {
            var sys = SystemService.I;
            if (!sys.HasBattery || !sys.Charging || sys.BatteryPercent >= 100) return null;
            return BatRate(true) is double r and > 0.5 ? TimeSpan.FromHours(Math.Min(24, (100 - sys.BatteryPercent) / r)) : null;
        }

        /// <summary>Сколько проработает от батареи: оценка Windows или своя по замерам.</summary>
        public static TimeSpan? DischargeLeft()
        {
            var sys = SystemService.I;
            if (!sys.HasBattery || sys.Charging) return null;
            if (sys.BatteryLeft is TimeSpan w && w.TotalMinutes > 0) return w;
            return BatRate(false) is double r and > 0.3 ? TimeSpan.FromHours(Math.Min(48, sys.BatteryPercent / r)) : null;
        }

        // ---- обои по кругу ----
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool SystemParametersInfo(uint action, uint p, string pv, uint winIni);
        static int _wallIdx = -1;

        public static string WallFolder
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(S.WallpaperFolder) && Directory.Exists(S.WallpaperFolder)) return S.WallpaperFolder;
                var pics = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                var w = Path.Combine(pics, "Wallpapers");
                return Directory.Exists(w) ? w : pics;
            }
        }

        public static List<string> WallImages()
        {
            try
            {
                return Directory.EnumerateFiles(WallFolder, "*.*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, MaxRecursionDepth = 2 })
                    .Where(f => Path.GetExtension(f).ToLowerInvariant() is ".jpg" or ".jpeg" or ".png" or ".bmp").Take(2000).OrderBy(f => f).ToList();
            }
            catch { return new(); }
        }

        /// <summary>Следующие обои: dir = 1 / −1, или случайные (0). Возвращает имя файла или текст ошибки.</summary>
        public static string NextWallpaper(int dir)
        {
            var list = WallImages();
            if (list.Count == 0) return null;
            _wallIdx = dir == 0 ? new Random().Next(list.Count) : ((_wallIdx < 0 ? 0 : _wallIdx + dir) % list.Count + list.Count) % list.Count;
            var f = list[_wallIdx];
            SystemParametersInfo(20 /*SPI_SETDESKWALLPAPER*/, 0, f, 3);
            return Path.GetFileName(f);
        }

        // ---- перезапуск Проводника ----
        public static string RestartExplorer()
        {
            try
            {
                foreach (var p in Process.GetProcessesByName("explorer")) { try { p.Kill(); } catch { } }
                Task.Delay(1500).ContinueWith(_ =>
                {
                    if (Process.GetProcessesByName("explorer").Length == 0) Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
                });
                return null;
            }
            catch (Exception e) { return e.Message; }
        }

        // ------------------------------------------------------------------
        //  Сводки
        // ------------------------------------------------------------------

        public static List<(string Name, long Bytes)> TopRam(int n)
        {
            try
            {
                return Process.GetProcesses().GroupBy(p => p.ProcessName)
                    .Select(g => { long s = 0; foreach (var p in g) { try { s += p.WorkingSet64; } catch { } p.Dispose(); } return (g.Key, s); })
                    .OrderByDescending(x => x.Item2).Take(n).ToList();
            }
            catch { return new(); }
        }

        /// <summary>Температура процессора по датчикам ACPI (без прав администратора). Пусто — ноутбук их не отдаёт.</summary>
        public static List<(string Zone, double C)> Temperatures()
        {
            var r = new List<(string, double)>();
            try
            {
                using var s = new System.Management.ManagementObjectSearcher(@"root\cimv2", "SELECT Name, HighPrecisionTemperature, Temperature FROM Win32_PerfFormattedData_Counters_ThermalZoneInformation");
                foreach (System.Management.ManagementObject o in s.Get())
                {
                    double k = o["HighPrecisionTemperature"] is uint hp && hp > 0 ? hp / 10.0 : o["Temperature"] is uint t ? t : 0;
                    if (k > 200) r.Add(((o["Name"] as string ?? "").Replace(@"\_TZ.", "").Trim('\\'), Math.Round(k - 273.15, 1)));
                }
            }
            catch { }
            return r;
        }

        public static void AddSpeed(string result)
        {
            var mbps = System.Text.RegularExpressions.Regex.Match(result ?? "", @"([\d.,]+)\s*Мбит");
            var ping = System.Text.RegularExpressions.Regex.Match(result ?? "", @"задержка\s*([\d.,]+)");
            if (!mbps.Success) return;
            lock (Gate) { Today.Speed.Add(new[] { DateTime.Now.ToString("HH:mm"), mbps.Groups[1].Value, ping.Success ? ping.Groups[1].Value : "" }); _dirty = true; }
        }

        public static string Dur(int sec) => sec >= 3600 ? $"{sec / 3600} ч {sec % 3600 / 60} мин" : $"{sec / 60} мин";

        public static string Bytes(long b) => b >= 1L << 30 ? $"{b / (double)(1L << 30):0.0} ГБ" : b >= 1 << 20 ? $"{b / (double)(1 << 20):0} МБ" : $"{b / 1024} КБ";

        /// <summary>Отчёт за день — для Telegram.</summary>
        public static string Report(DateTime day)
        {
            var t = Snap(day);
            var sb = new StringBuilder($"📊 <b>Отчёт за {day:dd.MM}</b>\n");
            sb.Append($"\n⏱ За ПК: <b>{Dur(t.ActiveSec)}</b>");
            foreach (var a in t.Apps.OrderByDescending(x => x.Value).Take(5)) sb.Append($"\n   • {H(a.Key)} — {Dur(a.Value)}");
            sb.Append($"\n🌐 Трафик: ↓ {Bytes(t.Down)} · ↑ {Bytes(t.Up)}");
            if (t.Bat.Count > 0) sb.Append($"\n🔋 Заряд: {t.Bat.Min(b => b[1])}–{t.Bat.Max(b => b[1])}%");
            int locks = t.Events.Count(e => e[1] == "lock"), sleeps = t.Events.Count(e => e[1] == "sleep");
            sb.Append($"\n🔒 Блокировок: {locks} · сон: {sleeps}");
            if (t.Notifs.Count > 0) sb.Append($"\n🔔 Уведомлений: {t.Notifs.Values.Sum()} (больше всего — {H(t.Notifs.OrderByDescending(x => x.Value).First().Key)})");
            if (t.Blocked > 0) sb.Append($"\n🎯 Фокус: отвлекающих окон свёрнуто {t.Blocked}");
            if (t.Speed.Count > 0) sb.Append($"\n🚀 Скорость: {t.Speed.Last()[1]} Мбит/с");
            return sb.ToString();
        }

        static string H(string s) => System.Net.WebUtility.HtmlEncode(s ?? "");
        static void Toast(string title, string sub) => System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() => App.Island?.Toast("", title, sub)));
    }
}
