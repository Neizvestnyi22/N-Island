using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Win32;
using NIsland.Core;

namespace NIsland.Services
{
    public record SitePermission(string Browser, string Site, string Permission, bool Allowed, DateTime? Changed);
    public record AppLaunch(string Name, string Path, int Count, TimeSpan Focus, DateTime? Last);
    public record UsbDevice(string Name, DateTime? Last);
    public record InstallRecord(string Name, string Publisher, DateTime Date);
    public record NotifyStat(string App, int Count, DateTime Last);

    /// <summary>
    /// Журнал активности: что делали и что запрашивали программы и сайты. Всё читается из того, что Windows и браузеры
    /// и так хранят на этом компьютере, — ничего никуда не отправляется.
    /// </summary>
    public static class Tracking
    {
        static string Local => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        static string Roaming => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        // ------------------------------------------------------------------
        //  Камера, микрофон, место — собственный журнал (ведёт LaptopService)
        // ------------------------------------------------------------------

        /// <summary>Обновить журнал по текущему состоянию «кто сейчас использует». Возвращает true, если что-то поменялось.</summary>
        public static bool Track(IEnumerable<AccessUse> now)
        {
            var log = Store.Data.ActivityLog;
            var active = now.Where(a => a.InUse).Select(a => (a.App, a.Kind)).ToHashSet();
            bool changed = false;
            foreach (var r in log.Where(r => r.End == null))
            {
                // запись осталась открытой с прошлого запуска N-Island — когда закончилась, уже не узнать
                if (r.Start < App.Started) { r.End = r.Start; changed = true; }
                else if (!active.Contains((r.App, r.Kind))) { r.End = DateTime.Now; changed = true; }
            }
            foreach (var (app, kind) in active)
                if (!log.Any(r => r.End == null && r.App == app && r.Kind == kind))
                {
                    log.Add(new ActivityRecord { App = app, Kind = kind, Start = DateTime.Now });
                    changed = true;
                }
            if (log.Count > 400) log.RemoveRange(0, log.Count - 400);
            if (changed) Store.SaveData();
            return changed;
        }

        // ------------------------------------------------------------------
        //  Разрешения сайтов в браузерах
        // ------------------------------------------------------------------

        static readonly Dictionary<string, string> PermNames = new()
        {
            ["media_stream_camera"] = "камера", ["media_stream_mic"] = "микрофон", ["geolocation"] = "местоположение",
            ["notifications"] = "уведомления", ["clipboard"] = "буфер обмена", ["automatic_downloads"] = "много загрузок",
            ["popups"] = "всплывающие окна", ["midi_sysex"] = "MIDI-устройства", ["usb_guard"] = "USB", ["serial_guard"] = "COM-порты",
            ["hid_guard"] = "HID-устройства", ["bluetooth_guard"] = "Bluetooth", ["file_system_write_guard"] = "запись файлов",
            ["sensors"] = "датчики", ["idle_detection"] = "отслеживание бездействия", ["window_placement"] = "управление окнами",
            ["local_fonts"] = "шрифты", ["protocol_handler"] = "открытие программ", ["storage_access"] = "сторонние cookie",
            ["auto_picture_in_picture"] = "картинка в картинке", ["vr"] = "VR", ["ar"] = "AR", ["camera_pan_tilt_zoom"] = "поворот камеры",
        };

        static readonly (string Name, string Dir)[] Chromium =
        {
            ("Chrome", @"Google\Chrome\User Data"), ("Edge", @"Microsoft\Edge\User Data"), ("Яндекс", @"Yandex\YandexBrowser\User Data"),
            ("Brave", @"BraveSoftware\Brave-Browser\User Data"), ("Vivaldi", @"Vivaldi\User Data"),
        };

        public static Task<List<SitePermission>> SitePermissions() => Task.Run(() =>
        {
            var res = new List<SitePermission>();
            var files = new List<(string Browser, string File)>();
            foreach (var (name, dir) in Chromium)
            {
                var root = Path.Combine(Local, dir);
                if (!Directory.Exists(root)) continue;
                foreach (var p in Directory.GetDirectories(root).Where(d => Path.GetFileName(d) is "Default" || Path.GetFileName(d).StartsWith("Profile ")))
                    files.Add((name, Path.Combine(p, "Preferences")));
            }
            files.Add(("Opera", Path.Combine(Roaming, "Opera Software", "Opera Stable", "Preferences")));
            files.Add(("Opera GX", Path.Combine(Roaming, "Opera Software", "Opera GX Stable", "Preferences")));
            foreach (var (browser, file) in files.Where(f => File.Exists(f.File)))
            {
                try
                {
                    // файл может быть занят браузером — читаем с общим доступом
                    using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var doc = JsonDocument.Parse(fs);
                    if (!doc.RootElement.TryGetProperty("profile", out var prof) || !prof.TryGetProperty("content_settings", out var cs)
                        || !cs.TryGetProperty("exceptions", out var ex)) continue;
                    foreach (var perm in ex.EnumerateObject())
                    {
                        if (!PermNames.TryGetValue(perm.Name, out var label) || perm.Value.ValueKind != JsonValueKind.Object) continue;
                        foreach (var site in perm.Value.EnumerateObject())
                        {
                            if (!site.Value.TryGetProperty("setting", out var st) || st.ValueKind != JsonValueKind.Number) continue;
                            int v = st.GetInt32();
                            if (v is not (1 or 2)) continue;   // 1 — разрешено, 2 — заблокировано; остальное — «спрашивать»
                            DateTime? when = null;
                            if (site.Value.TryGetProperty("last_modified", out var lm) && long.TryParse(lm.GetString(), out var us) && us > 0)
                                when = DateTime.FromFileTimeUtc(us * 10).ToLocalTime();   // микросекунды с 1601 года
                            res.Add(new SitePermission(browser, CleanSite(site.Name), label, v == 1, when));
                        }
                    }
                }
                catch (Exception e) { Log.Info($"site perms {browser}: {e.Message}"); }
            }
            res.AddRange(FirefoxPermissions());
            return res.Where(r => r.Site != "").OrderByDescending(r => r.Changed ?? DateTime.MinValue).ToList();
        });

        static string CleanSite(string pattern)
        {
            var s = pattern.Split(',')[0].Replace("[*.]", "").Replace("https://", "").Replace("http://", "");
            if (s.EndsWith(":443")) s = s[..^4];
            return s == "*" ? "" : s.TrimEnd('/');
        }

        static IEnumerable<SitePermission> FirefoxPermissions()
        {
            var res = new List<SitePermission>();
            var root = Path.Combine(Roaming, "Mozilla", "Firefox", "Profiles");
            if (!Directory.Exists(root)) return res;
            var map = new Dictionary<string, string> { ["camera"] = "камера", ["microphone"] = "микрофон", ["geo"] = "местоположение", ["desktop-notification"] = "уведомления",
                                                       ["popup"] = "всплывающие окна", ["autoplay-media"] = "автовоспроизведение", ["xr"] = "VR" };
            foreach (var p in Directory.GetDirectories(root))
            {
                var db = Path.Combine(p, "permissions.sqlite");
                if (!File.Exists(db)) continue;
                IntPtr h = IntPtr.Zero;
                try
                {
                    // immutable=1 — читаем, не мешая работающему Firefox
                    if (Sqlite.sqlite3_open_v2("file:///" + db.Replace('\\', '/') + "?immutable=1", out h, 0x1 | 0x40, IntPtr.Zero) != 0) continue;
                    foreach (var (perm, origin, type) in Sqlite.Rows(h, "SELECT permission, origin, type FROM moz_perms"))
                        if (map.TryGetValue(type, out var label) && perm is 1 or 2)
                            res.Add(new SitePermission("Firefox", CleanSite(origin), label, perm == 1, null));
                }
                catch { }
                finally { if (h != IntPtr.Zero) Sqlite.sqlite3_close(h); }
            }
            return res;
        }

        // ------------------------------------------------------------------
        //  Какие программы и когда запускались (UserAssist — его ведёт сама Windows)
        // ------------------------------------------------------------------

        static readonly Dictionary<string, string> KnownFolders = new(StringComparer.OrdinalIgnoreCase)
        {
            ["{6D809377-6AF0-444B-8957-A3773F02200E}"] = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            ["{7C5A40EF-A0FB-4BFC-874A-C0F2E0B9FA8E}"] = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            ["{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}"] = Environment.SystemDirectory,
            ["{F38BF404-1D43-42F2-9305-67DE0B28FC23}"] = Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            ["{D65231B0-B2F1-4857-A4CE-A8E7C6EA7D27}"] = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SysWOW64"),
        };

        static string Rot13(string s) => new(s.Select(c => c is >= 'a' and <= 'z' ? (char)('a' + (c - 'a' + 13) % 26)
                                                         : c is >= 'A' and <= 'Z' ? (char)('A' + (c - 'A' + 13) % 26) : c).ToArray());

        public static Task<List<AppLaunch>> Launches() => Task.Run(() =>
        {
            var res = new Dictionary<string, AppLaunch>(StringComparer.OrdinalIgnoreCase);
            const string Root = @"Software\Microsoft\Windows\CurrentVersion\Explorer\UserAssist";
            foreach (var guid in new[] { "{CEBFF5CD-ACE2-4F4F-9178-9926F41749EA}", "{F4E57C4B-2036-45F0-A9AB-443BCFE33D9F}" })
            {
                try
                {
                    using var k = Registry.CurrentUser.OpenSubKey($@"{Root}\{guid}\Count");
                    if (k == null) continue;
                    foreach (var raw in k.GetValueNames())
                    {
                        if (k.GetValue(raw) is not byte[] d || d.Length < 68) continue;
                        var path = Rot13(raw);
                        foreach (var (g, dir) in KnownFolders) if (path.StartsWith(g, StringComparison.OrdinalIgnoreCase)) path = dir + path[g.Length..];
                        if (path.StartsWith("UEME_", StringComparison.OrdinalIgnoreCase)) continue;
                        int count = BitConverter.ToInt32(d, 4);
                        int focusMs = BitConverter.ToInt32(d, 12);
                        long ft = BitConverter.ToInt64(d, 60);
                        DateTime? last = ft > 0 ? DateTime.FromFileTime(ft) : null;
                        if (count <= 0 && last == null) continue;
                        var name = System.IO.Path.GetFileNameWithoutExtension(path);
                        if (name.Contains('!')) name = name[(name.LastIndexOf('!') + 1)..];   // приложения из Store: «Microsoft.WindowsCalculator_…!App»
                        if (name.Length == 0) continue;
                        var item = new AppLaunch(name, path, count, TimeSpan.FromMilliseconds(Math.Max(0, focusMs)), last);
                        // ярлык и exe одной программы — одна строка
                        if (!res.TryGetValue(name, out var prev) || (item.Last ?? DateTime.MinValue) > (prev.Last ?? DateTime.MinValue))
                            res[name] = prev == null ? item : item with { Count = Math.Max(item.Count, prev.Count), Focus = item.Focus + prev.Focus };
                    }
                }
                catch { }
            }
            return res.Values.OrderByDescending(a => a.Last ?? DateTime.MinValue).Take(60).ToList();
        });

        // ------------------------------------------------------------------
        //  Подключавшиеся флешки и диски
        // ------------------------------------------------------------------

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        static extern int RegQueryInfoKey(Microsoft.Win32.SafeHandles.SafeRegistryHandle key, StringBuilder cls, IntPtr clsLen, IntPtr reserved,
            IntPtr subKeys, IntPtr maxSubKey, IntPtr maxClass, IntPtr values, IntPtr maxValueName, IntPtr maxValue, IntPtr secDesc, out long lastWrite);

        public static Task<List<UsbDevice>> UsbHistory() => Task.Run(() =>
        {
            var res = new List<UsbDevice>();
            try
            {
                using var root = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\USBSTOR");
                foreach (var dev in root?.GetSubKeyNames() ?? Array.Empty<string>())
                {
                    using var d = root.OpenSubKey(dev);
                    foreach (var serial in d?.GetSubKeyNames() ?? Array.Empty<string>())
                    {
                        using var s = d.OpenSubKey(serial);
                        var name = s?.GetValue("FriendlyName") as string ?? dev.Replace('_', ' ');
                        DateTime? last = null;
                        if (s != null && RegQueryInfoKey(s.Handle, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                                                         IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, out var ft) == 0 && ft > 0)
                            last = DateTime.FromFileTime(ft);
                        res.Add(new UsbDevice(name.Replace(" USB Device", ""), last));
                    }
                }
            }
            catch { }
            return res.OrderByDescending(r => r.Last ?? DateTime.MinValue).ToList();
        });

        // ------------------------------------------------------------------
        //  Что и когда устанавливалось
        // ------------------------------------------------------------------

        public static Task<List<InstallRecord>> RecentInstalls(int days = 90) => Task.Run(() =>
        {
            var res = new Dictionary<string, InstallRecord>(StringComparer.OrdinalIgnoreCase);
            var since = DateTime.Today.AddDays(-days);
            void Read(RegistryKey hive, string path)
            {
                try
                {
                    using var k = hive.OpenSubKey(path);
                    foreach (var sub in k?.GetSubKeyNames() ?? Array.Empty<string>())
                    {
                        using var a = k.OpenSubKey(sub);
                        var name = a?.GetValue("DisplayName") as string;
                        if (string.IsNullOrWhiteSpace(name) || a.GetValue("SystemComponent") is int sc && sc == 1) continue;
                        if (a.GetValue("InstallDate") is not string ds || !DateTime.TryParseExact(ds, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var date)) continue;
                        if (date >= since) res[name] = new InstallRecord(name, a.GetValue("Publisher") as string ?? "", date);
                    }
                }
                catch { }
            }
            const string U = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";
            Read(Registry.CurrentUser, U);
            Read(Registry.LocalMachine, U);
            Read(Registry.LocalMachine, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall");
            return res.Values.OrderByDescending(r => r.Date).ToList();
        });

        // ------------------------------------------------------------------
        //  Кто присылал уведомления (база уведомлений Windows)
        // ------------------------------------------------------------------

        public static Task<List<NotifyStat>> NotificationStats(int days = 7) => Task.Run(() =>
        {
            var res = new List<NotifyStat>();
            var path = Path.Combine(Local, @"Microsoft\Windows\Notifications\wpndatabase.db");
            if (!File.Exists(path)) return res;
            IntPtr db = IntPtr.Zero;
            try
            {
                if (Sqlite.sqlite3_open_v2(path, out db, 0x1, IntPtr.Zero) != 0) return res;
                Sqlite.sqlite3_busy_timeout(db, 300);
                long since = DateTime.UtcNow.AddDays(-days).ToFileTimeUtc();
                foreach (var (count, app, last) in Sqlite.Rows(db,
                    "SELECT COUNT(*), h.PrimaryId, CAST(MAX(n.ArrivalTime) AS TEXT) FROM Notification n JOIN NotificationHandler h ON h.RecordId = n.HandlerId " +
                    $"WHERE n.ArrivalTime > {since} GROUP BY h.PrimaryId ORDER BY 1 DESC LIMIT 25"))
                {
                    if (app.Contains("NIsland")) continue;
                    DateTime when = long.TryParse(last, out var ft) && ft > 0 ? DateTime.FromFileTimeUtc(ft).ToLocalTime() : DateTime.MinValue;
                    res.Add(new NotifyStat(MediaService.FriendlyName(app), (int)count, when));
                }
            }
            catch (Exception e) { Log.Info("notify stats: " + e.Message); }
            finally { if (db != IntPtr.Zero) Sqlite.sqlite3_close(db); }
            return res;
        });
    }
}
