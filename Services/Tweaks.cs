using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Win32;
using NIsland.Core;

namespace NIsland.Services
{
    /// <summary>Переключатель системной настройки Windows.</summary>
    public record Tweak(string Title, string Desc, Func<bool> Get, Action<bool> Set, string Note = null);

    public record InstalledApp(string Name, string Version, string Publisher, long SizeKb, string Uninstall);

    /// <summary>
    /// Настройки Windows для ноутбука: тема, панель задач, проводник, конфиденциальность, производительность,
    /// питание, обои, Wi-Fi, приложения. Только параметры текущего пользователя — без прав администратора.
    /// </summary>
    public static class Tweaks
    {
        const string Personalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        const string Advanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
        const string Search = @"Software\Microsoft\Windows\CurrentVersion\Search";
        const string Dwm = @"Software\Microsoft\Windows\DWM";
        const string AdInfo = @"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo";
        const string Cdm = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";
        const string GameBar = @"Software\Microsoft\GameBar";
        const string Serialize = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize";
        const string Desktop = @"Control Panel\Desktop";

        static int Get(string key, string name, int def)
        {
            try { using var k = Registry.CurrentUser.OpenSubKey(key); return k?.GetValue(name) is int i ? i : def; }
            catch { return def; }
        }

        static void Set(string key, string name, int value)
        {
            try { using var k = Registry.CurrentUser.CreateSubKey(key); k.SetValue(name, value, RegistryValueKind.DWord); }
            catch (Exception e) { Log.Info($"tweak {name}: {e.Message}"); }
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessageTimeout(IntPtr h, int msg, IntPtr w, string l, int flags, int timeout, out IntPtr res);
        [DllImport("shell32.dll")] static extern void SHChangeNotify(int id, uint flags, IntPtr a, IntPtr b);
        [DllImport("user32.dll", SetLastError = true)] static extern bool SystemParametersInfo(uint action, uint param, IntPtr pv, uint flags);
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool SystemParametersInfo(uint action, uint param, string pv, uint flags);
        [DllImport("user32.dll", SetLastError = true)] static extern bool SystemParametersInfo(uint action, uint param, ref ANIMATIONINFO pv, uint flags);

        [StructLayout(LayoutKind.Sequential)] struct ANIMATIONINFO { public uint cbSize; public int iMinAnimate; }

        /// <summary>Сообщить Windows и проводнику, что настройки поменялись.</summary>
        static void Broadcast(string area)
        {
            SendMessageTimeout(new IntPtr(0xFFFF), 0x001A, IntPtr.Zero, area, 2, 300, out _);
        }

        /// <summary>Сообщить системе о смене политик и параметров панели задач.</summary>
        public static void Notify() { Broadcast("Policy"); Broadcast("TraySettings"); Broadcast("ImmersiveColorSet"); }

        static void RefreshExplorer()
        {
            SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero); // SHCNE_ASSOCCHANGED
            Broadcast("ShellState");
        }

        // ------------------------------------------------------------------
        //  Списки переключателей
        // ------------------------------------------------------------------

        public static readonly Tweak[] Theme =
        {
            new("Тёмная тема приложений", "Окна программ, проводник, параметры",
                () => Get(Personalize, "AppsUseLightTheme", 1) == 0, on => { Set(Personalize, "AppsUseLightTheme", on ? 0 : 1); Broadcast("ImmersiveColorSet"); }),
            new("Тёмная тема Windows", "Панель задач, Пуск, центр уведомлений",
                () => Get(Personalize, "SystemUsesLightTheme", 1) == 0, on => { Set(Personalize, "SystemUsesLightTheme", on ? 0 : 1); Broadcast("ImmersiveColorSet"); }),
            new("Эффекты прозрачности", "Полупрозрачные Пуск, панель задач и окна",
                () => Get(Personalize, "EnableTransparency", 1) == 1, on => { Set(Personalize, "EnableTransparency", on ? 1 : 0); Broadcast("ImmersiveColorSet"); }),
            new("Акцентный цвет на панели задач", "Работает с тёмной темой Windows",
                () => Get(Personalize, "ColorPrevalence", 0) == 1, on => { Set(Personalize, "ColorPrevalence", on ? 1 : 0); Broadcast("ImmersiveColorSet"); }),
            new("Акцентный цвет в заголовках окон", null,
                () => Get(Dwm, "ColorPrevalence", 0) == 1, on => { Set(Dwm, "ColorPrevalence", on ? 1 : 0); Broadcast("ImmersiveColorSet"); }),
            new("Анимации окон", "Сворачивание и разворачивание окон с анимацией",
                GetAnimations, SetAnimations),
        };

        public static readonly Tweak[] Taskbar =
        {
            new("Панель задач слева", "Значки по левому краю, как в Windows 10",
                () => Get(Advanced, "TaskbarAl", 1) == 0, on => { Set(Advanced, "TaskbarAl", on ? 0 : 1); Broadcast("TraySettings"); }),
            new("Скрыть поиск на панели задач", null,
                () => Get(Search, "SearchboxTaskbarMode", 1) == 0, on => { Set(Search, "SearchboxTaskbarMode", on ? 0 : 1); Broadcast("TraySettings"); }),
            new("Кнопка «Представление задач»", null,
                () => Get(Advanced, "ShowTaskViewButton", 1) == 1, on => { Set(Advanced, "ShowTaskViewButton", on ? 1 : 0); Broadcast("TraySettings"); }),
            new("Секунды в часах на панели задач", "Windows 11 23H2 и новее",
                () => Get(Advanced, "ShowSecondsInSystemClock", 0) == 1, on => { Set(Advanced, "ShowSecondsInSystemClock", on ? 1 : 0); Broadcast("TraySettings"); }),
            new("Рекомендации в меню «Пуск»", "Недавние файлы и предложения в Пуске",
                () => Get(Advanced, "Start_IrisRecommendations", 1) == 1, on => Set(Advanced, "Start_IrisRecommendations", on ? 1 : 0), "Применится после перезапуска проводника"),
        };

        public static readonly Tweak[] Explorer =
        {
            new("Показывать расширения файлов", "photo.jpg вместо photo",
                () => Get(Advanced, "HideFileExt", 1) == 0, on => { Set(Advanced, "HideFileExt", on ? 0 : 1); RefreshExplorer(); }),
            new("Показывать скрытые файлы", null,
                () => Get(Advanced, "Hidden", 2) == 1, on => { Set(Advanced, "Hidden", on ? 1 : 2); RefreshExplorer(); }),
            new("Проводник открывает «Этот компьютер»", "Вместо «Главной»",
                () => Get(Advanced, "LaunchTo", 2) == 1, on => Set(Advanced, "LaunchTo", on ? 1 : 2)),
            new("Флажки для выбора файлов", null,
                () => Get(Advanced, "AutoCheckSelect", 0) == 1, on => { Set(Advanced, "AutoCheckSelect", on ? 1 : 0); RefreshExplorer(); }),
        };

        public static readonly Tweak[] Privacy =
        {
            new("Персональная реклама", "Рекламный идентификатор для приложений",
                () => Get(AdInfo, "Enabled", 1) == 1, on => Set(AdInfo, "Enabled", on ? 1 : 0)),
            new("Советы и предложения Windows", "Подсказки, «рекомендуемые» приложения, реклама на экране блокировки",
                () => Get(Cdm, "SubscribedContent-338389Enabled", 1) == 1,
                on =>
                {
                    foreach (var n in new[] { "SubscribedContent-338389Enabled", "SubscribedContent-338388Enabled", "SubscribedContent-310093Enabled",
                                              "SoftLandingEnabled", "SystemPaneSuggestionsEnabled", "RotatingLockScreenOverlayEnabled" })
                        Set(Cdm, n, on ? 1 : 0);
                }),
            new("Отслеживание запуска приложений", "Для «часто используемых» в Пуске и поиске",
                () => Get(Advanced, "Start_TrackProgs", 1) == 1, on => Set(Advanced, "Start_TrackProgs", on ? 1 : 0)),
        };

        public static readonly Tweak[] Performance =
        {
            new("Игровой режим", "Windows отдаёт приоритет играм",
                () => Get(GameBar, "AutoGameModeEnabled", 1) == 1, on => Set(GameBar, "AutoGameModeEnabled", on ? 1 : 0)),
            new("Быстрый старт автозагрузки", "Без задержки в 10 секунд после входа",
                () => Get(Serialize, "StartupDelayInMSec", -1) == 0, on => Set(Serialize, "StartupDelayInMSec", on ? 0 : 10000)),
            new("Быстрые меню", "Подменю открываются без задержки (после повторного входа)",
                () => GetString(Desktop, "MenuShowDelay", "400") == "100", on => SetString(Desktop, "MenuShowDelay", on ? "100" : "400")),
        };

        static string GetString(string key, string name, string def)
        {
            try { using var k = Registry.CurrentUser.OpenSubKey(key); return k?.GetValue(name) as string ?? def; }
            catch { return def; }
        }

        static void SetString(string key, string name, string value)
        {
            try { using var k = Registry.CurrentUser.CreateSubKey(key); k.SetValue(name, value, RegistryValueKind.String); }
            catch { }
        }

        static bool GetAnimations()
        {
            var a = new ANIMATIONINFO { cbSize = (uint)Marshal.SizeOf<ANIMATIONINFO>() };
            return SystemParametersInfo(0x0048, a.cbSize, ref a, 0) && a.iMinAnimate != 0; // SPI_GETANIMATION
        }

        static void SetAnimations(bool on)
        {
            var a = new ANIMATIONINFO { cbSize = (uint)Marshal.SizeOf<ANIMATIONINFO>(), iMinAnimate = on ? 1 : 0 };
            SystemParametersInfo(0x0049, a.cbSize, ref a, 3); // SPI_SETANIMATION
        }

        // ------------------------------------------------------------------
        //  Мышь
        // ------------------------------------------------------------------
        public static int MouseSpeed
        {
            get
            {
                var p = Marshal.AllocHGlobal(4);
                try { return SystemParametersInfo(0x0070, 0, p, 0) ? Marshal.ReadInt32(p) : 10; }
                finally { Marshal.FreeHGlobal(p); }
            }
            set => SystemParametersInfo(0x0071, 0, new IntPtr(Math.Clamp(value, 1, 20)), 3);
        }

        // ------------------------------------------------------------------
        //  Проводник
        // ------------------------------------------------------------------
        public static void RestartExplorer()
        {
            foreach (var p in Process.GetProcessesByName("explorer")) using (p) { try { p.Kill(); } catch { } }
            System.Threading.Thread.Sleep(600);
            if (Process.GetProcessesByName("explorer").Length == 0) SystemService.Run("explorer.exe");
        }

        // ------------------------------------------------------------------
        //  Обои
        // ------------------------------------------------------------------
        static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

        public static bool SetWallpaper(string path) => SystemParametersInfo(0x0014, 0, path, 3); // SPI_SETDESKWALLPAPER

        /// <summary>Картинка дня Bing в 4K → обои. Возвращает описание снимка.</summary>
        public static async Task<string> BingWallpaper()
        {
            using var doc = JsonDocument.Parse(await Http.GetStringAsync("https://www.bing.com/HPImageArchive.aspx?format=js&idx=0&n=1&mkt=ru-RU"));
            var img = doc.RootElement.GetProperty("images")[0];
            var url = "https://www.bing.com" + img.GetProperty("urlbase").GetString() + "_UHD.jpg";
            var dir = Path.Combine(Store.Dir, "wallpapers");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, $"bing-{DateTime.Today:yyyyMMdd}.jpg");
            if (!File.Exists(file)) await File.WriteAllBytesAsync(file, await Http.GetByteArrayAsync(url));
            // старые картинки не копим
            foreach (var old in new DirectoryInfo(dir).GetFiles("bing-*.jpg").OrderByDescending(f => f.Name).Skip(7)) old.Delete();
            if (!SetWallpaper(file)) throw new InvalidOperationException("Windows не приняла обои");
            return img.TryGetProperty("copyright", out var c) ? c.GetString() : "Обои дня установлены";
        }

        // ------------------------------------------------------------------
        //  Питание
        // ------------------------------------------------------------------
        /// <summary>Время до отключения экрана / сна (минуты, 0 — никогда).</summary>
        public static void SetTimeout(string what, bool battery, int minutes) =>
            Maint.RunCmd("powercfg", $"/change {what}-timeout-{(battery ? "dc" : "ac")} {minutes}");

        /// <summary>Действие при закрытии крышки: 0 — ничего, 1 — сон, 2 — гибернация, 3 — выключение.</summary>
        public static void SetLidAction(int action)
        {
            Maint.RunCmd("powercfg", $"/setacvalueindex SCHEME_CURRENT SUB_BUTTONS LIDACTION {action}");
            Maint.RunCmd("powercfg", $"/setdcvalueindex SCHEME_CURRENT SUB_BUTTONS LIDACTION {action}");
            Maint.RunCmd("powercfg", "/setactive SCHEME_CURRENT");
        }

        public static Task OpenBatteryReport() => Task.Run(() =>
        {
            var file = Path.Combine(Path.GetTempPath(), "battery-report.html");
            Maint.RunCmd("powercfg", $"/batteryreport /output \"{file}\"");
            if (File.Exists(file)) SystemService.Run(file);
        });

        // ------------------------------------------------------------------
        //  Wi-Fi
        // ------------------------------------------------------------------
        public static Task<List<string>> WifiProfiles() => Task.Run(() =>
            Regex.Matches(Maint.RunCmd("netsh", "wlan show profiles"), @":\s*(.+?)\s*$", RegexOptions.Multiline)
                 .Select(m => m.Groups[1].Value).Where(n => n.Length > 0 && !n.Contains("<")).Distinct().ToList());

        /// <summary>Пароль сохранённой сети этого компьютера.</summary>
        public static Task<string> WifiPassword(string profile) => Task.Run(() =>
        {
            var o = Maint.RunCmd("netsh", $"wlan show profile name=\"{profile.Replace("\"", "")}\" key=clear");
            var m = Regex.Match(o, @"(Key Content|Содержимое ключа)\s*:\s*(.+)$", RegexOptions.Multiline);
            return m.Success ? m.Groups[2].Value.Trim() : null;
        });

        // ------------------------------------------------------------------
        //  Установленные программы
        // ------------------------------------------------------------------
        public static Task<List<InstalledApp>> InstalledApps() => Task.Run(() =>
        {
            var list = new Dictionary<string, InstalledApp>(StringComparer.OrdinalIgnoreCase);
            void Read(RegistryKey root, string path)
            {
                try
                {
                    using var k = root.OpenSubKey(path);
                    if (k == null) return;
                    foreach (var sub in k.GetSubKeyNames())
                    {
                        using var a = k.OpenSubKey(sub);
                        var name = a?.GetValue("DisplayName") as string;
                        if (string.IsNullOrWhiteSpace(name) || a.GetValue("SystemComponent") is int sc && sc == 1 || a.GetValue("ParentKeyName") != null) continue;
                        var un = a.GetValue("QuietUninstallString") as string ?? a.GetValue("UninstallString") as string;
                        long size = a.GetValue("EstimatedSize") is int s ? s : 0;
                        list[name] = new InstalledApp(name, a.GetValue("DisplayVersion") as string, a.GetValue("Publisher") as string, size,
                            a.GetValue("UninstallString") as string ?? un);
                    }
                }
                catch { }
            }
            const string U = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";
            Read(Registry.CurrentUser, U);
            Read(Registry.LocalMachine, U);
            Read(Registry.LocalMachine, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall");
            return list.Values.OrderBy(x => x.Name).ToList();
        });

        /// <summary>Запустить родной деинсталлятор программы (пользователь подтверждает удаление в нём).</summary>
        public static void Uninstall(InstalledApp app)
        {
            var cmd = app.Uninstall?.Trim();
            if (string.IsNullOrEmpty(cmd)) { SystemService.Run("ms-settings:appsfeatures"); return; }
            string exe, args;
            if (cmd.StartsWith("\""))
            {
                int end = cmd.IndexOf('"', 1);
                exe = cmd[1..end];
                args = cmd[(end + 1)..].Trim();
            }
            else
            {
                int exeEnd = cmd.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                exe = exeEnd > 0 ? cmd[..(exeEnd + 4)] : cmd;
                args = exeEnd > 0 ? cmd[(exeEnd + 4)..].Trim() : "";
            }
            try { Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = true }); }
            catch { SystemService.Run("ms-settings:appsfeatures"); }
        }

        // ------------------------------------------------------------------
        //  Большие файлы
        // ------------------------------------------------------------------
        public static Task<List<FileInfo>> LargeFiles(long minBytes = 200L << 20) => Task.Run(() =>
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var dirs = new[] { "Downloads", "Desktop", "Documents", "Videos", "Music", "Pictures" }.Select(d => Path.Combine(home, d)).Where(Directory.Exists);
            var res = new List<FileInfo>();
            foreach (var d in dirs)
            {
                try
                {
                    foreach (var f in Directory.EnumerateFiles(d, "*", new System.IO.EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.System }))
                    {
                        try { var fi = new FileInfo(f); if (fi.Length >= minBytes) res.Add(fi); } catch { }
                    }
                }
                catch { }
            }
            return res.OrderByDescending(f => f.Length).Take(25).ToList();
        });

        public static void ShowInFolder(string path) => Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });

        /// <summary>В корзину (обратимо), а не насовсем.</summary>
        public static bool ToRecycleBin(string path)
        {
            try
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                return true;
            }
            catch { return false; }
        }

        public static long FolderSize(string dir)
        {
            try
            {
                return Directory.EnumerateFiles(dir, "*", new System.IO.EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
                    .Sum(f => { try { return new FileInfo(f).Length; } catch { return 0; } });
            }
            catch { return 0; }
        }
    }
}
