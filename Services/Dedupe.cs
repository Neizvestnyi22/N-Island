using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using NIsland.Core;

namespace NIsland.Services
{
    /// <summary>
    /// «Без дублей»: выключает то, что N-Island уже показывает сам, — баннеры уведомлений Windows (уведомления остаются
    /// в центре уведомлений, остров их читает), окно «Автозапуск» при подключении флешки и системное окно громкости и яркости.
    /// Всё обратимо: исходные значения сохраняются, при выходе из N-Island возвращаются, при запуске — снова выключаются.
    /// </summary>
    public static class Dedupe
    {
        static string NotifKey = @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings";
        static string AutoplayKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\AutoplayHandlers";
        static string BackupFile = Path.Combine(Store.Dir, "backup", "dedupe-backup.json");

        /// <summary>Самопроверка: те же действия, но в тестовой ветке реестра и с отдельным файлом копии.</summary>
        internal static void UseSandbox(string regRoot, string backupFile)
        {
            NotifKey = regRoot + @"\Notifications\Settings";
            AutoplayKey = regRoot + @"\AutoplayHandlers";
            BackupFile = backupFile;
            _b = null;
        }
        static Settings S => Store.Settings;

        /// <summary>Исходные значения: «ключ приложения» → ShowBanner (null — значения не было), и AutoPlay.</summary>
        class Backup
        {
            public Dictionary<string, int?> Banners { get; set; } = new();
            public int? Autoplay { get; set; }
            public bool AutoplaySaved { get; set; }
        }

        static Backup _b;
        static Backup B => _b ??= Load();

        static Backup Load()
        {
            try { if (File.Exists(BackupFile)) return JsonSerializer.Deserialize<Backup>(File.ReadAllText(BackupFile)) ?? new(); } catch { }
            return new();
        }

        static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(BackupFile));
                if (B.Banners.Count == 0 && !B.AutoplaySaved) { if (File.Exists(BackupFile)) File.Delete(BackupFile); return; }
                File.WriteAllText(BackupFile, JsonSerializer.Serialize(B));
            }
            catch (Exception e) { Log.Info("dedupe backup: " + e.Message); }
        }

        /// <summary>Системные уведомления (безопасность, батарея, обновления) не трогаем — их пусть показывает Windows.</summary>
        static bool Protected(string app) =>
            app.StartsWith("Windows.SystemToast.", StringComparison.OrdinalIgnoreCase) || app.Contains("SecHealthUI", StringComparison.OrdinalIgnoreCase)
            || app.Contains("Defender", StringComparison.OrdinalIgnoreCase) || app.Contains("WindowsUpdate", StringComparison.OrdinalIgnoreCase)
            || app.StartsWith("Microsoft.Windows.Shell", StringComparison.OrdinalIgnoreCase);

        // ------------------------------------------------------------------
        //  Баннеры уведомлений
        // ------------------------------------------------------------------

        /// <summary>Сколько приложений сейчас без баннеров благодаря N-Island.</summary>
        public static int BannersHidden => B.Banners.Count;

        static void HideBanner(RegistryKey root, string app)
        {
            if (Protected(app)) return;
            using var k = root.CreateSubKey(app, true);
            if (k == null) return;
            var cur = k.GetValue("ShowBanner") as int?;
            if (cur == 0 && !B.Banners.ContainsKey(app)) return;   // пользователь сам выключил — это не наше, не трогаем и не «возвращаем»
            if (!B.Banners.ContainsKey(app)) B.Banners[app] = cur;
            if (cur != 0) k.SetValue("ShowBanner", 0, RegistryValueKind.DWord);
        }

        public static void ApplyBanners(bool on)
        {
            try
            {
                using var root = Registry.CurrentUser.CreateSubKey(NotifKey, true);
                if (on)
                {
                    foreach (var app in root.GetSubKeyNames()) HideBanner(root, app);
                }
                else
                {
                    foreach (var (app, orig) in B.Banners.ToList())
                    {
                        using var k = root.OpenSubKey(app, true);
                        if (k != null)
                        {
                            if (orig is int v) k.SetValue("ShowBanner", v, RegistryValueKind.DWord);
                            else k.DeleteValue("ShowBanner", false);
                        }
                        B.Banners.Remove(app);
                    }
                }
            }
            catch (Exception e) { Log.Info("dedupe banners: " + e.Message); }
            Save();
        }

        /// <summary>Уведомление от приложения, которого ещё не было в списке, — сразу выключаем и его баннер.</summary>
        public static void OnApp(string appId)
        {
            if (!S.DedupeBanners || !S.Notifications || string.IsNullOrEmpty(appId) || B.Banners.ContainsKey(appId) || Protected(appId)) return;
            try
            {
                using var root = Registry.CurrentUser.CreateSubKey(NotifKey, true);
                HideBanner(root, appId);
                Save();
            }
            catch { }
        }

        // ------------------------------------------------------------------
        //  Автозапуск флешек (AutoPlay)
        // ------------------------------------------------------------------

        public static void ApplyAutoplay(bool on)
        {
            try
            {
                using var k = Registry.CurrentUser.CreateSubKey(AutoplayKey, true);
                if (on)
                {
                    if (!B.AutoplaySaved) { B.Autoplay = k.GetValue("DisableAutoplay") as int?; B.AutoplaySaved = true; }
                    k.SetValue("DisableAutoplay", 1, RegistryValueKind.DWord);
                }
                else if (B.AutoplaySaved)
                {
                    if (B.Autoplay is int v) k.SetValue("DisableAutoplay", v, RegistryValueKind.DWord); else k.DeleteValue("DisableAutoplay", false);
                    B.AutoplaySaved = false;
                    B.Autoplay = null;
                }
            }
            catch (Exception e) { Log.Info("dedupe autoplay: " + e.Message); }
            Save();
        }

        // ------------------------------------------------------------------
        //  Системное окно громкости и яркости (OSD)
        // ------------------------------------------------------------------

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);

        static IntPtr _osd;
        /// <summary>null — ещё не проверяли; false — в этой версии Windows окно громкости встроено в оболочку и не отключается.</summary>
        public static bool? OsdSupported { get; private set; }

        /// <summary>Классическое окно громкости Windows: NativeHWNDHost с DirectUIHWND внутри (Windows 10 и ранние сборки 11).
        /// Создаётся при первом нажатии клавиши громкости, поэтому проверяем после него.</summary>
        static IntPtr FindOsd()
        {
            IntPtr h = IntPtr.Zero;
            while ((h = FindWindowEx(IntPtr.Zero, h, "NativeHWNDHost", "")) != IntPtr.Zero)
                if (FindWindowEx(h, IntPtr.Zero, "DirectUIHWND", "") != IntPtr.Zero) return h;
            return IntPtr.Zero;
        }

        /// <summary>Вызывать после изменения громкости или яркости: окно уже создано, его можно спрятать.</summary>
        public static void TouchOsd()
        {
            if (!S.DedupeOsd) return;
            if (_osd != IntPtr.Zero && IsWindow(_osd)) return;
            _osd = FindOsd();
            OsdSupported = _osd != IntPtr.Zero;
            if (_osd != IntPtr.Zero) ShowWindow(_osd, 6);   // SW_MINIMIZE — так окно больше не появляется
        }

        public static void ApplyOsd(bool on)
        {
            if (on) { _osd = IntPtr.Zero; TouchOsd(); return; }
            var h = _osd != IntPtr.Zero && IsWindow(_osd) ? _osd : FindOsd();
            if (h != IntPtr.Zero) { ShowWindow(h, 9); ShowWindow(h, 0); }   // вернуть как было: восстановить и спрятать до следующего нажатия
            _osd = IntPtr.Zero;
        }

        // ------------------------------------------------------------------
        //  Жизненный цикл
        // ------------------------------------------------------------------

        public static void Start()
        {
            if (S.DedupeBanners && S.Notifications) ApplyBanners(true);
            if (S.DedupeAutoplay) ApplyAutoplay(true);
            if (S.DedupeOsd) TouchOsd();
            S.PropertyChanged += (_, e) =>
            {
                switch (e.PropertyName)
                {
                    case nameof(Settings.DedupeBanners): ApplyBanners(S.DedupeBanners && S.Notifications); break;
                    case nameof(Settings.Notifications):
                    case nameof(Settings.NotifyOnlyCalls):
                        // остров перестал показывать уведомления — баннеры Windows возвращаем, иначе они пропадут совсем
                        ApplyBanners(S.DedupeBanners && S.Notifications && !S.NotifyOnlyCalls);
                        break;
                    case nameof(Settings.DedupeAutoplay): ApplyAutoplay(S.DedupeAutoplay); break;
                    case nameof(Settings.DedupeOsd): ApplyOsd(S.DedupeOsd); break;
                }
            };
        }

        /// <summary>Выход из N-Island: возвращаем Windows её окна, чтобы без острова ничего не пропадало.</summary>
        public static void Suspend()
        {
            if (B.Banners.Count > 0) ApplyBanners(false);
            if (B.AutoplaySaved) ApplyAutoplay(false);
            if (S.DedupeOsd) ApplyOsd(false);
        }
    }
}
