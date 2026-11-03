using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Win32;
using NIsland.Core;

namespace NIsland.Services
{
    public record StartupItem(string Name, string Command, string Source, bool Enabled, bool CanToggle);
    public record ProcInfo(string Name, int Count, long Bytes);
    public record PowerPlan(string Guid, string Name, bool Active);
    public record DiskInfo(string Name, string Label, long Total, long Free);
    public record BatteryHealth(int Design, int Full, int Cycles)
    {
        public int Percent => Design > 0 ? (int)Math.Round(100.0 * Full / Design) : 0;
    }

    /// <summary>Обслуживание ноутбука: очистка, память, автозагрузка, батарея, диски, электропитание.</summary>
    public static class Maint
    {
        // ---------- Временные файлы ----------
        static IEnumerable<string> TempDirs()
        {
            yield return Path.GetTempPath();
            var winTemp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");
            if (Directory.Exists(winTemp)) yield return winTemp; // без прав админа часть файлов просто пропустится
            var crash = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrashDumps");
            if (Directory.Exists(crash)) yield return crash;
        }

        static IEnumerable<FileInfo> OldTempFiles()
        {
            var cutoff = DateTime.Now.AddDays(-Math.Max(0, Store.Settings.TempAgeDays));
            foreach (var dir in TempDirs())
            {
                IEnumerable<string> files;
                try { files = Directory.EnumerateFiles(dir, "*", new System.IO.EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }); }
                catch { continue; }
                foreach (var f in files)
                {
                    FileInfo fi;
                    try { fi = new FileInfo(f); if (fi.LastWriteTime > cutoff) continue; }
                    catch { continue; }
                    yield return fi;
                }
            }
        }

        public static Task<(long Bytes, int Files)> ScanTemp() => Task.Run(() =>
        {
            long b = 0; int n = 0;
            foreach (var f in OldTempFiles()) { try { b += f.Length; n++; } catch { } }
            return (b, n);
        });

        /// <summary>Удаляет старые временные файлы. Занятые и защищённые пропускаются.</summary>
        public static Task<(long Freed, int Deleted, int Skipped)> CleanTemp() => Task.Run(() =>
        {
            long freed = 0; int del = 0, skip = 0;
            foreach (var f in OldTempFiles().ToList())
            {
                try { long len = f.Length; f.Delete(); freed += len; del++; }
                catch { skip++; }
            }
            foreach (var dir in TempDirs())
            {
                try
                {
                    foreach (var d in Directory.EnumerateDirectories(dir, "*", new System.IO.EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
                                 .OrderByDescending(x => x.Length))
                        try { if (!Directory.EnumerateFileSystemEntries(d).Any()) Directory.Delete(d); } catch { }
                }
                catch { }
            }
            return (freed, del, skip);
        });

        // ---------- Корзина ----------
        // SHQUERYRBINFO упакована по-разному: в 32-битной Windows без выравнивания (20 байт), в 64-битной — по 8 (24 байта).
        // Раньше здесь была одна структура с Pack=4 — на x64 Windows отвечала ошибкой, и корзина всегда казалась пустой.
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHQueryRecycleBin(string root, IntPtr info);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHEmptyRecycleBin(IntPtr hwnd, string root, uint flags);

        public static (long Bytes, long Items) RecycleBin()
        {
            int size = IntPtr.Size == 8 ? 24 : 20, off = IntPtr.Size == 8 ? 8 : 4;
            var p = Marshal.AllocHGlobal(size);
            try
            {
                for (int i = 0; i < size; i += 4) Marshal.WriteInt32(p, i, 0);
                Marshal.WriteInt32(p, size);
                return SHQueryRecycleBin(null, p) == 0 ? (Marshal.ReadInt64(p, off), Marshal.ReadInt64(p, off + 8)) : (0, 0);
            }
            finally { Marshal.FreeHGlobal(p); }
        }

        /// <summary>Безвозвратно очищает корзину — вызывается только после подтверждения пользователя.</summary>
        public static bool EmptyRecycleBin() => SHEmptyRecycleBin(IntPtr.Zero, null, 0x1 | 0x2 | 0x4) == 0;

        // ---------- Память ----------
        [DllImport("psapi.dll")] static extern bool EmptyWorkingSet(IntPtr h);

        public static long AvailableRam()
        {
            var m = new Native.MEMORYSTATUSEX();
            return Native.GlobalMemoryStatusEx(m) ? (long)m.ullAvailPhys : 0;
        }

        public static long TotalRam()
        {
            var m = new Native.MEMORYSTATUSEX();
            return Native.GlobalMemoryStatusEx(m) ? (long)m.ullTotalPhys : 0;
        }

        /// <summary>Сжимает рабочие наборы всех доступных процессов — Windows выгрузит неиспользуемое из ОЗУ.</summary>
        public static Task<long> OptimizeRam() => Task.Run(() =>
        {
            long before = AvailableRam();
            foreach (var p in Process.GetProcesses())
            {
                using (p) { try { EmptyWorkingSet(p.Handle); } catch { } }
            }
            System.Threading.Thread.Sleep(400);
            return Math.Max(0, AvailableRam() - before);
        });

        // ---------- Сеть ----------
        [DllImport("dnsapi.dll")] static extern bool DnsFlushResolverCache();
        public static bool FlushDns() { try { return DnsFlushResolverCache(); } catch { return false; } }

        public static Task<string> WifiInfo() => Task.Run(() =>
        {
            var o = RunCmd("netsh", "wlan show interfaces");
            // ключ — альтернатива на разных языках Windows; группа значения всегда последняя
            string Get(string key) { var m = Regex.Match(o, @"^\s*(?:" + key + @")\s*:\s*(.+?)\s*$", RegexOptions.Multiline); return m.Success ? m.Groups[m.Groups.Count - 1].Value : ""; }
            var ssid = Get("SSID");
            if (ssid == "") return "Wi-Fi не подключён";
            var signal = Get("(Signal|Сигнал)");
            var rx = Get("(Receive rate \\(Mbps\\)|Скорость приема \\(Мбит/с\\)|Скорость приёма \\(Мбит/с\\))");
            return $"{ssid} · сигнал {signal}" + (rx != "" ? $" · {rx} Мбит/с" : "");
        });

        // ---------- Батарея ----------
        static BatteryHealth _battery;

        /// <summary>Износ батареи из отчёта powercfg (без прав администратора).</summary>
        public static Task<BatteryHealth> Battery() => Task.Run(() =>
        {
            if (_battery != null) return _battery;
            try
            {
                var file = Path.Combine(Path.GetTempPath(), "nisland-battery.xml");
                RunCmd("powercfg", $"/batteryreport /xml /output \"{file}\"");
                if (!File.Exists(file)) return null;
                var x = XDocument.Load(file);
                File.Delete(file);
                var b = x.Descendants().FirstOrDefault(e => e.Name.LocalName == "Battery");
                if (b == null) return null;
                int V(string n) => int.TryParse(b.Elements().FirstOrDefault(e => e.Name.LocalName == n)?.Value, out var v) ? v : 0;
                _battery = new BatteryHealth(V("DesignCapacity"), V("FullChargeCapacity"), V("CycleCount"));
                return _battery;
            }
            catch { return null; }
        });

        // ---------- Диски ----------
        public static List<DiskInfo> Disks()
        {
            var list = new List<DiskInfo>();
            foreach (var d in DriveInfo.GetDrives())
            {
                try
                {
                    if (d.DriveType is not (DriveType.Fixed or DriveType.Removable) || !d.IsReady) continue;
                    list.Add(new DiskInfo(d.Name.TrimEnd('\\'), d.VolumeLabel, d.TotalSize, d.AvailableFreeSpace));
                }
                catch { }
            }
            return list;
        }

        /// <summary>Состояние физических дисков (SMART через Storage WMI).</summary>
        public static Task<List<string>> DiskHealth() => Task.Run(() =>
        {
            var res = new List<string>();
            try
            {
                using var s = new ManagementObjectSearcher(@"root\Microsoft\Windows\Storage", "SELECT FriendlyName, HealthStatus, MediaType FROM MSFT_PhysicalDisk");
                foreach (ManagementObject o in s.Get())
                {
                    var health = Convert.ToInt32(o["HealthStatus"]) switch { 0 => "исправен ✓", 1 => "предупреждение ⚠", 2 => "неисправен ✕", _ => "неизвестно" };
                    var type = Convert.ToInt32(o["MediaType"]) switch { 3 => "HDD", 4 => "SSD", 5 => "SCM", _ => "диск" };
                    res.Add($"{type} {o["FriendlyName"]}: {health}");
                }
            }
            catch { }
            return res;
        });

        // ---------- Автозагрузка ----------
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ApprovedRun = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
        const string ApprovedFolder = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";

        static bool Approved(RegistryKey root, string key, string name)
        {
            try
            {
                using var k = root.OpenSubKey(key);
                return k?.GetValue(name) is not byte[] b || b.Length == 0 || b[0] % 2 == 0; // 02 — включено, 03 — выключено
            }
            catch { return true; }
        }

        public static List<StartupItem> Startup()
        {
            var list = new List<StartupItem>();
            void FromRun(RegistryKey root, string src, bool canToggle)
            {
                try
                {
                    using var k = root.OpenSubKey(RunKey);
                    if (k == null) return;
                    foreach (var n in k.GetValueNames())
                        list.Add(new StartupItem(n, k.GetValue(n)?.ToString() ?? "", src, Approved(root, ApprovedRun, n), canToggle));
                }
                catch { }
            }
            FromRun(Registry.CurrentUser, "user", true);
            FromRun(Registry.LocalMachine, "machine", false);
            try
            {
                var dir = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                foreach (var f in Directory.GetFiles(dir))
                {
                    var n = Path.GetFileName(f);
                    if (n.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                    list.Add(new StartupItem(Path.GetFileNameWithoutExtension(f), f, "folder", Approved(Registry.CurrentUser, ApprovedFolder, n), true));
                }
            }
            catch { }
            return list.OrderBy(x => x.Name).ToList();
        }

        /// <summary>Включает/выключает так же, как «Диспетчер задач → Автозагрузка» (запись не удаляется).</summary>
        public static void SetStartup(StartupItem item, bool on)
        {
            if (!item.CanToggle) return;
            var key = item.Source == "folder" ? ApprovedFolder : ApprovedRun;
            var name = item.Source == "folder" ? Path.GetFileName(item.Command) : item.Name;
            using var k = Registry.CurrentUser.CreateSubKey(key);
            var data = new byte[12];
            data[0] = on ? (byte)0x02 : (byte)0x03;
            if (!on) BitConverter.GetBytes(DateTime.Now.ToFileTimeUtc()).CopyTo(data, 4);
            k.SetValue(name, data, RegistryValueKind.Binary);
        }

        // ---------- Процессы ----------
        public static List<ProcInfo> TopProcesses(int n = 8)
        {
            var map = new Dictionary<string, (int Count, long Bytes)>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in Process.GetProcesses())
            {
                using (p)
                {
                    try
                    {
                        map.TryGetValue(p.ProcessName, out var v);
                        map[p.ProcessName] = (v.Count + 1, v.Bytes + p.WorkingSet64);
                    }
                    catch { }
                }
            }
            return map.Where(kv => kv.Key is not ("Idle" or "System" or "Memory Compression" or "Registry"))
                      .OrderByDescending(kv => kv.Value.Bytes).Take(n)
                      .Select(kv => new ProcInfo(kv.Key, kv.Value.Count, kv.Value.Bytes)).ToList();
        }

        public static int Kill(string name)
        {
            int n = 0;
            foreach (var p in Process.GetProcessesByName(name))
            {
                using (p) { try { p.Kill(); n++; } catch { } }
            }
            return n;
        }

        // ---------- Электропитание ----------
        public static List<PowerPlan> PowerPlans()
        {
            var list = new List<PowerPlan>();
            foreach (Match m in Regex.Matches(RunCmd("powercfg", "/list"), @"([0-9a-fA-F\-]{36})\s+\((.+?)\)\s*(\*)?"))
                list.Add(new PowerPlan(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Success));
            return list;
        }

        public static void SetPowerPlan(string guid) => RunCmd("powercfg", "/setactive " + guid);

        // ---------- Разное ----------
        public static TimeSpan Uptime => TimeSpan.FromMilliseconds(Environment.TickCount64);

        public static string WindowsVersion
        {
            get
            {
                try
                {
                    using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
                    return $"{k?.GetValue("ProductName")} {k?.GetValue("DisplayVersion")} (сборка {Environment.OSVersion.Version.Build})"
                        .Replace("Windows 10", Environment.OSVersion.Version.Build >= 22000 ? "Windows 11" : "Windows 10");
                }
                catch { return Environment.OSVersion.VersionString; }
            }
        }

        /// <summary>Системные инструменты Windows одним кликом.</summary>
        public static readonly (string Name, string Glyph, string Target)[] WinTools =
        {
            ("Очистка диска", "\uE74D", "cleanmgr.exe"),
            ("Контроль памяти", "\uEDA2", "ms-settings:storagesense"),
            ("Диспетчер устройств", "\uE772", "devmgmt.msc"),
            ("Обновления", "\uE895", "ms-settings:windowsupdate"),
            ("Защитник", "\uE83D", "windowsdefender:"),
            ("Монитор ресурсов", "\uE9D9", "resmon.exe"),
            ("Сведения о системе", "\uE946", "msinfo32.exe"),
            ("Электропитание", "\uE945", "ms-settings:powersleep"),
            ("Устранение неполадок", "\uE90F", "ms-settings:troubleshoot"),
            ("Приложения", "\uE71D", "ms-settings:appsfeatures"),
            ("Сеть", "\uE968", "ms-settings:network-status"),
            ("Bluetooth", "\uE702", "ms-settings:bluetooth"),
        };

        public static string RunCmd(string exe, string args)
        {
            try
            {
                var psi = new ProcessStartInfo(exe, args)
                {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
                    StandardOutputEncoding = System.Text.Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage),
                };
                using var p = Process.Start(psi);
                var o = p.StandardOutput.ReadToEnd();
                p.WaitForExit(15000);
                return o;
            }
            catch { return ""; }
        }

        public static string Size(long bytes) =>
            bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):0.#} ГБ" : bytes >= 1L << 20 ? $"{bytes / (double)(1L << 20):0} МБ" : $"{bytes / 1024.0:0} КБ";
    }
}
