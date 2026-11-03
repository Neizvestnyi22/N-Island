using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Threading;
using System.Xml.Linq;
using Microsoft.Win32;
using NIsland.Core;

namespace NIsland.Services
{
    /// <summary>Уведомление из любого приложения или сайта (из системной базы уведомлений Windows).</summary>
    public class Toast
    {
        public string AppId;
        public string App;
        public string Title;
        public string Body;
        public string Site;
        public bool IsCall;
    }

    /// <summary>
    /// Звонки и уведомления:
    ///  • входящие звонки и сообщения — из базы уведомлений Windows (Telegram, WhatsApp, Discord, Teams,
    ///    «Связь с телефоном», уведомления сайтов из Chrome/Edge/Firefox…);
    ///  • идущий звонок — по тому, какое приложение (или браузер) сейчас держит микрофон/камеру.
    /// Всё опрашивается раз в 1.5 с и почти не тратит память.
    /// </summary>
    public sealed class CallService
    {
        public static CallService I { get; } = new();

        readonly DispatcherTimer _t = new() { Interval = TimeSpan.FromMilliseconds(1500) };
        IntPtr _db;
        long _lastId = -1;
        int _dbFails;
        DateTime _dbRetryAt;

        public string MicApp { get; private set; }       // кто сейчас использует микрофон
        public string MicAppPath { get; private set; }
        public bool CameraOn { get; private set; }
        public DateTime MicSince { get; private set; }
        public bool InCall => MicApp != null;

        public event Action CallChanged;
        public event Action<Toast> ToastArrived;

        static readonly Regex CallWords = new(@"incoming call|входящий|звонок|звонит|вызов|calling you|is calling|video call|voice call|видеозвонок",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public void Start()
        {
            _t.Tick += (_, __) => Poll();
            _t.Start();
            Poll();
        }

        public void Stop()
        {
            _t.Stop();
            if (_db != IntPtr.Zero) { Sqlite.sqlite3_close(_db); _db = IntPtr.Zero; }
        }

        int _tick;

        void Poll()
        {
            var s = Store.Settings;
            _tick++;
            // микрофон: во время звонка — каждые 1.5 с, иначе — каждые 3 с (меньше нагрузки)
            if (s.CallDetect) { if (InCall || _tick % 2 == 0) PollMic(); }
            else if (MicApp != null) { MicApp = null; CallChanged?.Invoke(); }
            if (s.Notifications || s.CallDetect) PollToasts();
        }

        // ---------- микрофон / камера ----------

        const string Consent = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\";

        void PollMic()
        {
            // 1) звуковая система: кто прямо сейчас пишет с микрофона
            string app = null, path = null;
            int self = Environment.ProcessId;
            foreach (var pid in AudioService.I.ActiveCapturePids())
            {
                if (pid == self) continue;
                try
                {
                    using var p = Process.GetProcessById((int)pid);
                    app = ExeName(p.ProcessName);
                    path = "proc:" + p.ProcessName;
                    break;
                }
                catch { }
            }
            // 2) запасной вариант — журнал доступа к микрофону в реестре
            if (app == null) (app, path) = ActiveUser("microphone");
            var (cam, _) = ActiveUser("webcam");
            bool camOn = cam != null;
            if (app != MicApp || camOn != CameraOn)
            {
                if (app != null && MicApp == null) MicSince = DateTime.Now;
                MicApp = app;
                MicAppPath = path;
                CameraOn = camOn;
                CallChanged?.Invoke();
            }
        }

        static (string App, string Path) ActiveUser(string cap)
        {
            try
            {
                using var root = Registry.CurrentUser.OpenSubKey(Consent + cap);
                if (root == null) return (null, null);
                foreach (var name in root.GetSubKeyNames())
                {
                    if (name == "NonPackaged")
                    {
                        using var np = root.OpenSubKey(name);
                        if (np == null) continue;
                        foreach (var exe in np.GetSubKeyNames())
                        {
                            using var k = np.OpenSubKey(exe);
                            if (InUse(k) && !exe.EndsWith("NIsland.exe", StringComparison.OrdinalIgnoreCase))
                                return (ExeName(exe), exe.Replace('#', '\\'));
                        }
                    }
                    else
                    {
                        using var k = root.OpenSubKey(name);
                        if (InUse(k)) return (MediaService.FriendlyName(name), name);
                    }
                }
            }
            catch { }
            return (null, null);
        }

        static bool InUse(RegistryKey k)
        {
            if (k == null) return false;
            var start = k.GetValue("LastUsedTimeStart") as long? ?? 0;
            var stop = k.GetValue("LastUsedTimeStop") as long? ?? 0;
            return start > 0 && stop == 0;
        }

        static string ExeName(string regName)
        {
            var file = Path.GetFileNameWithoutExtension(regName.Split('#').Last());
            return file.ToLowerInvariant() switch
            {
                "telegram" => "Telegram",
                "discord" => "Discord",
                "whatsapp" or "whatsapp.root" => "WhatsApp",
                "ms-teams" or "teams" => "Teams",
                "zoom" => "Zoom",
                "skype" => "Skype",
                "chrome" => "Chrome",
                "msedge" => "Edge",
                "firefox" => "Firefox",
                "browser" => "Яндекс Браузер",
                "opera" => "Opera",
                "viber" => "Viber",
                "obs64" => "OBS",
                _ => Fmt.Cap(file),
            };
        }

        public static bool IsBrowser(string app) => app is "Chrome" or "Edge" or "Firefox" or "Яндекс Браузер" or "Opera" or "Brave" or "Comet";

        /// <summary>Вывести окно приложения со звонком на передний план.</summary>
        public void FocusCallApp()
        {
            if (MicAppPath == null) return;
            if (MicAppPath.StartsWith("proc:")) { Focus(MicAppPath[5..]); return; }
            if (!MicAppPath.Contains('\\')) { SystemService.Run("shell:AppsFolder\\" + MicAppPath); return; }
            Focus(Path.GetFileNameWithoutExtension(MicAppPath));
        }

        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);

        public static void Focus(string processName)
        {
            try
            {
                foreach (var p in Process.GetProcessesByName(processName))
                {
                    using (p)
                    {
                        var h = p.MainWindowHandle;
                        if (h == IntPtr.Zero) continue;
                        if (IsIconic(h)) ShowWindow(h, 9);
                        SetForegroundWindow(h);
                        return;
                    }
                }
            }
            catch { }
        }

        public static void OpenApp(string aumid)
        {
            if (string.IsNullOrEmpty(aumid)) return;
            var name = MediaService.FriendlyName(aumid);
            var proc = name switch { "Chrome" => "chrome", "Edge" => "msedge", "Firefox" => "firefox", "Telegram" => "Telegram", "Discord" => "Discord", _ => null };
            if (proc != null) Focus(proc);
            else SystemService.Run("shell:AppsFolder\\" + aumid);
        }

        // ---------- уведомления ----------

        void PollToasts()
        {
            if (_db == IntPtr.Zero)
            {
                if (DateTime.Now < _dbRetryAt) return;
                var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"Microsoft\Windows\Notifications\wpndatabase.db");
                if (!File.Exists(path) || Sqlite.sqlite3_open_v2(path, out _db, 0x1 /*READONLY*/, IntPtr.Zero) != 0)
                {
                    if (_db != IntPtr.Zero) { Sqlite.sqlite3_close(_db); _db = IntPtr.Zero; }
                    _dbRetryAt = DateTime.Now.AddSeconds(Math.Min(300, 10 * ++_dbFails));
                    return;
                }
                Sqlite.sqlite3_busy_timeout(_db, 200);
            }

            try
            {
                long max = Sqlite.ScalarLong(_db, "SELECT IFNULL(MAX(Id),0) FROM Notification");
                // первый запуск или Windows пересоздала базу (номера начались заново)
                if (_lastId < 0 || max < _lastId)
                {
                    _lastId = max;
                    return;
                }
                if (max == _lastId) return; // ничего нового — не трогаем таблицы
                var rows = Sqlite.Rows(_db,
                    "SELECT n.Id, h.PrimaryId, n.Payload FROM Notification n JOIN NotificationHandler h ON h.RecordId = n.HandlerId " +
                    $"WHERE n.Type = 'toast' AND n.Id > {_lastId} ORDER BY n.Id LIMIT 20");
                foreach (var (id, appId, payload) in rows)
                {
                    _lastId = Math.Max(_lastId, id);
                    var t = Parse(appId, payload);
                    if (t != null) ToastArrived?.Invoke(t);
                }
                _dbFails = 0;
            }
            catch (Exception e)
            {
                Log.Info("toasts: " + e.Message);
                Sqlite.sqlite3_close(_db);
                _db = IntPtr.Zero;
                _dbRetryAt = DateTime.Now.AddSeconds(15);
            }
        }

        static Toast Parse(string appId, string payload)
        {
            if (string.IsNullOrEmpty(payload) || appId.Contains("NIsland")) return null;
            try
            {
                var x = XDocument.Parse(payload);
                var root = x.Root;
                if (root == null) return null;
                var texts = root.Descendants("text").ToList();
                var main = texts.Where(t => (string)t.Attribute("placement") == null && (string)t.Attribute("hint-callScenarioCallerId") == null)
                    .Select(t => t.Value.Trim()).Where(v => v != "").ToList();
                var site = texts.FirstOrDefault(t => (string)t.Attribute("placement") == "attribution")?.Value.Trim();
                var t = new Toast
                {
                    AppId = appId,
                    App = MediaService.FriendlyName(appId),
                    Title = main.ElementAtOrDefault(0) ?? "",
                    Body = string.Join(" · ", main.Skip(1).Take(2)),
                    Site = string.IsNullOrEmpty(site) ? null : site,
                };
                var scenario = (string)root.Attribute("scenario") ?? "";
                t.IsCall = scenario.Equals("incomingCall", StringComparison.OrdinalIgnoreCase)
                           || CallWords.IsMatch(t.Title + " " + t.Body);
                if (t.Title == "" && t.Body == "") return null;
                return t;
            }
            catch { return null; }
        }
    }

    /// <summary>Минимальная обёртка над winsqlite3.dll, которая уже есть в Windows — без лишних библиотек.</summary>
    static class Sqlite
    {
        const string Dll = "winsqlite3.dll";
        [DllImport(Dll, CharSet = CharSet.Ansi)] static extern int sqlite3_open_v2(byte[] file, out IntPtr db, int flags, IntPtr vfs);
        [DllImport(Dll)] public static extern int sqlite3_close(IntPtr db);
        [DllImport(Dll)] public static extern int sqlite3_busy_timeout(IntPtr db, int ms);
        [DllImport(Dll)] static extern int sqlite3_prepare_v2(IntPtr db, byte[] sql, int n, out IntPtr stmt, IntPtr tail);
        [DllImport(Dll)] static extern int sqlite3_step(IntPtr stmt);
        [DllImport(Dll)] static extern int sqlite3_finalize(IntPtr stmt);
        [DllImport(Dll)] static extern long sqlite3_column_int64(IntPtr stmt, int col);
        [DllImport(Dll)] static extern IntPtr sqlite3_column_blob(IntPtr stmt, int col);
        [DllImport(Dll)] static extern int sqlite3_column_bytes(IntPtr stmt, int col);
        [DllImport(Dll)] static extern IntPtr sqlite3_column_text(IntPtr stmt, int col);

        static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s + "\0");

        public static int sqlite3_open_v2(string file, out IntPtr db, int flags, IntPtr vfs) => sqlite3_open_v2(Utf8(file), out db, flags, vfs);

        static IntPtr Prepare(IntPtr db, string sql)
        {
            int rc = sqlite3_prepare_v2(db, Utf8(sql), -1, out var st, IntPtr.Zero);
            if (rc != 0) throw new InvalidOperationException("sqlite prepare " + rc);
            return st;
        }

        public static long ScalarLong(IntPtr db, string sql)
        {
            var st = Prepare(db, sql);
            try { return sqlite3_step(st) == 100 ? sqlite3_column_int64(st, 0) : 0; }
            finally { sqlite3_finalize(st); }
        }

        public static List<(long, string, string)> Rows(IntPtr db, string sql)
        {
            var list = new List<(long, string, string)>();
            var st = Prepare(db, sql);
            try
            {
                int rc;
                while ((rc = sqlite3_step(st)) == 100)
                {
                    long id = sqlite3_column_int64(st, 0);
                    string app = Marshal.PtrToStringUTF8(sqlite3_column_text(st, 1)) ?? "";
                    int n = sqlite3_column_bytes(st, 2);
                    string payload = "";
                    if (n > 0)
                    {
                        var buf = new byte[n];
                        Marshal.Copy(sqlite3_column_blob(st, 2), buf, 0, n);
                        payload = Encoding.UTF8.GetString(buf);
                    }
                    list.Add((id, app, payload));
                }
                if (rc != 101) throw new InvalidOperationException("sqlite step " + rc);
            }
            finally { sqlite3_finalize(st); }
            return list;
        }
    }

    /// <summary>Держим потребление памяти низким: сборка мусора + сброс рабочего набора, когда чёлка простаивает.</summary>
    public static class MemoryTrim
    {
        [DllImport("psapi.dll")] static extern bool EmptyWorkingSet(IntPtr h);
        static DateTime _last;

        public static void Now(bool force = false)
        {
            if (!force && (DateTime.Now - _last).TotalSeconds < 20) return;
            _last = DateTime.Now;
            try
            {
                System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(2, GCCollectionMode.Forced, true, true);
                GC.WaitForPendingFinalizers();
                using var p = Process.GetCurrentProcess();
                EmptyWorkingSet(p.Handle);
            }
            catch { }
        }
    }
}
