using System;
using System.Diagnostics;
using System.Management;
using System.Threading.Tasks;
using System.Windows.Forms;
using NIsland.Core;
using Microsoft.Win32;

namespace NIsland.Services
{
    public sealed class SystemService
    {
        public static SystemService I { get; } = new();

        long _idle, _kernel, _user;
        ManagementEventWatcher _briWatcher;
        DateTime _selfBrightness;

        public event Action<int> BrightnessChanged;

        // ---------- Батарея ----------
        public bool HasBattery => (SystemInformation.PowerStatus.BatteryChargeStatus & BatteryChargeStatus.NoSystemBattery) == 0
                                  && SystemInformation.PowerStatus.BatteryChargeStatus != BatteryChargeStatus.Unknown;
        public int BatteryPercent => (int)Math.Round(Math.Clamp(SystemInformation.PowerStatus.BatteryLifePercent, 0f, 1f) * 100);
        public bool Charging => SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online;
        /// <summary>Сколько ещё проработает от батареи (Windows оценивает сама; null — неизвестно или зарядка).</summary>
        public TimeSpan? BatteryLeft
        {
            get
            {
                int s = SystemInformation.PowerStatus.BatteryLifeRemaining;
                return s > 0 && !Charging ? TimeSpan.FromSeconds(s) : null;
            }
        }

        // ---------- CPU / RAM ----------
        public int CpuPercent()
        {
            if (!Native.GetSystemTimes(out var idle, out var kernel, out var user)) return 0;
            long di = idle - _idle, dk = kernel - _kernel, du = user - _user;
            _idle = idle; _kernel = kernel; _user = user;
            long total = dk + du;
            return total <= 0 ? 0 : (int)Math.Clamp(100.0 * (total - di) / total, 0, 100);
        }

        public int RamPercent()
        {
            var m = new Native.MEMORYSTATUSEX();
            return Native.GlobalMemoryStatusEx(m) ? (int)m.dwMemoryLoad : 0;
        }

        // ---------- Яркость (ноутбуки) ----------
        public int? GetBrightness()
        {
            try
            {
                using var s = new ManagementObjectSearcher("root\\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness");
                foreach (ManagementObject o in s.Get()) return Convert.ToInt32(o["CurrentBrightness"]);
            }
            catch { }
            return null;
        }

        public void SetBrightness(int v)
        {
            _selfBrightness = DateTime.Now;
            Task.Run(() =>
            {
                try
                {
                    using var c = new ManagementClass("root\\WMI", "WmiMonitorBrightnessMethods", null);
                    foreach (ManagementObject o in c.GetInstances())
                    {
                        o.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, (byte)Math.Clamp(v, 0, 100) });
                        break;
                    }
                }
                catch { }
            });
        }

        public void WatchBrightness()
        {
            try
            {
                _briWatcher = new ManagementEventWatcher(new ManagementScope("root\\WMI"),
                    new EventQuery("SELECT * FROM WmiMonitorBrightnessEvent"));
                _briWatcher.EventArrived += (_, e) =>
                {
                    int v = Convert.ToInt32(e.NewEvent["Brightness"]);
                    bool self = (DateTime.Now - _selfBrightness).TotalMilliseconds < 1500;
                    System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (!self) BrightnessChanged?.Invoke(v);
                    }));
                };
                _briWatcher.Start();
            }
            catch (Exception e) { Log.Info("brightness watcher: " + e.Message); }
        }

        public void Stop()
        {
            try { _briWatcher?.Stop(); _briWatcher?.Dispose(); } catch { }
            KeepAwake = false;
        }

        // ---------- Не спать ----------
        bool _awake;
        public bool KeepAwake
        {
            get => _awake;
            set
            {
                _awake = value;
                const uint CONTINUOUS = 0x80000000, SYSTEM = 0x1, DISPLAY = 0x2;
                Native.SetThreadExecutionState(value ? CONTINUOUS | SYSTEM | DISPLAY : CONTINUOUS);
            }
        }

        // ---------- Тема Windows ----------
        const string ThemeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

        public bool IsLightTheme
        {
            get
            {
                try
                {
                    using var k = Registry.CurrentUser.OpenSubKey(ThemeKey);
                    return k?.GetValue("AppsUseLightTheme") is int i && i == 1;
                }
                catch { return false; }
            }
        }

        public void ToggleTheme()
        {
            try
            {
                int light = IsLightTheme ? 0 : 1;
                using var k = Registry.CurrentUser.OpenSubKey(ThemeKey, true);
                k?.SetValue("AppsUseLightTheme", light, RegistryValueKind.DWord);
                k?.SetValue("SystemUsesLightTheme", light, RegistryValueKind.DWord);
                Native.BroadcastThemeChange();
            }
            catch (Exception e) { Log.Error(e); }
        }

        /// <summary>
        /// Открыть ссылку, пришедшую ИЗВНЕ (уведомления сервисов, RSS, ntfy, Telegram): только http(s) и mailto.
        /// Через Run такая «ссылка» могла бы оказаться путём к программе или file:// и запустить её по клику.
        /// </summary>
        public static void OpenWeb(string url)
        {
            url = url?.Trim() ?? "";
            if (Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeMailto))
                Run(u.AbsoluteUri);
            else Log.Info("blocked non-web link: " + (url.Length > 80 ? url[..80] : url));
        }

        public static void Run(string target)
        {
            try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
            catch (Exception e) { Log.Info("run " + target + ": " + e.Message); }
        }
    }
}
