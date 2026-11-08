using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Microsoft.Win32;
using NIsland.Core;

namespace NIsland.Services
{
    /// <summary>
    /// Глубокая интеграция с Windows. Всё только для текущего пользователя (HKCU), без прав администратора и с полным откатом:
    /// пункты в меню Проводника, ссылки nisland:// для скриптов и ярлыков, «Не беспокоить» Windows ↔ режим острова,
    /// акцентный цвет Windows.
    /// </summary>
    public static class WinShell
    {
        static Settings S => Store.Settings;
        static string Exe => Environment.ProcessPath;
        const string Classes = @"Software\Classes\";

        public static void Start()
        {
            ApplyShellMenu(S.ShellMenu);
            ApplyProtocol(S.UrlProtocol);
            if (S.WindowsAccent) SyncAccent();
            SystemEvents.UserPreferenceChanged += (_, e) =>
            {
                if (S.WindowsAccent && e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle)
                    System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(SyncAccent));
            };
            _focus.Tick += (_, __) => CheckFocus();
            if (S.FollowFocusAssist) _focus.Start();
            S.PropertyChanged += (_, e) =>
            {
                switch (e.PropertyName)
                {
                    case nameof(Settings.ShellMenu): ApplyShellMenu(S.ShellMenu); break;
                    case nameof(Settings.UrlProtocol): ApplyProtocol(S.UrlProtocol); break;
                    case nameof(Settings.WindowsAccent): if (S.WindowsAccent) SyncAccent(); break;
                    case nameof(Settings.FollowFocusAssist): if (S.FollowFocusAssist) _focus.Start(); else { _focus.Stop(); RestoreMode(); } break;
                }
            };
        }

        // ------------------------------------------------------------------
        //  Меню Проводника (правый клик по файлу)
        // ------------------------------------------------------------------

        static readonly (string Key, string Path, string Title, string Arg)[] Menu =
        {
            ("NIsland.Check", @"*\shell\NIsland.Check", "Проверить файл в N-Island (подпись, тип, VirusTotal)", "--check-file"),
            ("NIsland.Strip", @"SystemFileAssociations\image\shell\NIsland.Strip", "Копия без метаданных и GPS (N-Island)", "--strip-photo"),
        };

        public static void ApplyShellMenu(bool on)
        {
            foreach (var m in Menu)
            {
                try
                {
                    if (!on) { Registry.CurrentUser.DeleteSubKeyTree(Classes + m.Path, false); continue; }
                    using var k = Registry.CurrentUser.CreateSubKey(Classes + m.Path);
                    k.SetValue("MUIVerb", I18n.T(m.Title));
                    k.SetValue("Icon", "\"" + Exe + "\",0");
                    using var c = k.CreateSubKey("command");
                    c.SetValue("", $"\"{Exe}\" {m.Arg} \"%1\"");
                }
                catch (Exception e) { Log.Info("shell menu: " + e.Message); }
            }
        }

        public static bool ShellMenuInstalled
        {
            get { try { using var k = Registry.CurrentUser.OpenSubKey(Classes + Menu[0].Path); return k != null; } catch { return false; } }
        }

        /// <summary>Правый клик по фото → «копия без метаданных»: сохраняем рядом и показываем в Проводнике.</summary>
        public static void StripPhoto(string path)
        {
            try
            {
                var meta = Osint.Photo(path);
                var outp = Osint.StripPhoto(path, meta.Orientation);
                App.Island?.Toast("\uE91B", "Чистая копия без метаданных", Path.GetFileName(outp) + (meta.Lat != null ? " · GPS удалён" : ""));
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{outp}\"")?.Dispose();
            }
            catch (Exception e) { App.Island?.Toast("\uE783", "Не получилось очистить фото", e.Message); }
        }

        // ------------------------------------------------------------------
        //  Ссылки nisland://
        // ------------------------------------------------------------------

        public static void ApplyProtocol(bool on)
        {
            try
            {
                if (!on) { Registry.CurrentUser.DeleteSubKeyTree(Classes + "nisland", false); return; }
                using var k = Registry.CurrentUser.CreateSubKey(Classes + "nisland");
                k.SetValue("", "URL:N-Island");
                k.SetValue("URL Protocol", "");
                using (var i = k.CreateSubKey("DefaultIcon")) i.SetValue("", "\"" + Exe + "\",0");
                using var c = k.CreateSubKey(@"shell\open\command");
                c.SetValue("", $"\"{Exe}\" --url \"%1\"");
            }
            catch (Exception e) { Log.Info("protocol: " + e.Message); }
        }

        public static readonly (string Url, string What)[] UrlExamples =
        {
            ("nisland://play", "пауза / воспроизведение"), ("nisland://next", "следующий трек"), ("nisland://open/weather", "открыть страницу чёлки"),
            ("nisland://notify?title=Готово&text=Сборка завершена", "всплывашка в чёлке"), ("nisland://timer/25", "таймер на 25 минут"),
            ("nisland://mode/dnd", "режим «Не беспокоить»"), ("nisland://eq/bass", "пресет эквалайзера"), ("nisland://visualizer", "окно визуализатора"),
        };

        /// <summary>Команда по ссылке. Только безобидные действия: музыка, страницы, всплывашки, таймер, режимы.</summary>
        public static void HandleUrl(string url)
        {
            try
            {
                var u = url.Trim().TrimEnd('/');
                if (u.StartsWith("nisland:", StringComparison.OrdinalIgnoreCase)) u = u[8..].TrimStart('/');
                var q = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                int qi = u.IndexOf('?');
                if (qi >= 0)
                {
                    foreach (var kv in u[(qi + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var p = kv.Split('=', 2);
                        q[Uri.UnescapeDataString(p[0])] = p.Length > 1 ? Uri.UnescapeDataString(p[1].Replace('+', ' ')) : "";
                    }
                    u = u[..qi];
                }
                var parts = u.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.UnescapeDataString).ToArray();
                if (parts.Length == 0) { App.Island?.Expand(null, true); return; }
                string cmd = parts[0].ToLowerInvariant(), arg = parts.Length > 1 ? parts[1] : "";
                var m = MediaService.I;
                string Clip(string s, int n) => s == null ? "" : s.Length > n ? s[..n] + "…" : s;
                switch (cmd)
                {
                    case "play": case "pause": case "toggle": m.TogglePlay(); break;
                    case "next": m.Next(); break;
                    case "prev": case "previous": m.Prev(); break;
                    case "open": App.Island?.Expand(arg == "" ? null : arg, true); break;
                    case "settings": App.ShowSettings(arg == "" ? null : arg); break;
                    case "center": App.ShowCenter(arg == "" ? null : arg); break;
                    case "visualizer": VisualizerWindow.Open(); break;
                    case "spotlight": SpotlightWindow.Toggle(q.TryGetValue("q", out var sq) ? sq : null); break;
                    case "mode": if (arg != "") App.Island?.SetMode(arg); break;
                    case "eq": if (arg is "off") Equalizer.SetOn(false); else if (arg is "on") Equalizer.SetOn(true); else Equalizer.SetPreset(arg); break;
                    case "timer":
                        if (double.TryParse(arg, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var min) && min > 0)
                        {
                            TimerService.I.Reset();
                            TimerService.I.SetMinutes(min);
                            TimerService.I.Start();
                            App.Island?.Toast("\uE916", "Таймер запущен", $"{min} мин");
                        }
                        break;
                    case "notify":
                        App.Island?.ShowActivity(new Activity
                        {
                            Kind = "hub", Glyph = "\uE7E7", Tint = System.Windows.Media.Color.FromRgb(0x0A, 0x84, 0xFF),
                            Title = Clip(q.TryGetValue("title", out var t) ? t : arg, 80), Sub = "🔗 " + Clip(q.TryGetValue("text", out var x) ? x : "", 150), Seconds = 6, Wide = true,   // видно, что всплывашка пришла по ссылке, а не от системы
                        });
                        break;
                    default: App.Island?.Toast("\uE783", "Неизвестная команда", Clip(cmd, 40)); break;
                }
            }
            catch (Exception e) { Log.Info("url: " + e.Message); }
        }

        // ------------------------------------------------------------------
        //  «Не беспокоить» Windows → режим острова
        // ------------------------------------------------------------------

        [DllImport("ntdll.dll")]
        static extern int NtQueryWnfStateData(ref ulong name, IntPtr type, IntPtr scope, out uint stamp, byte[] buffer, ref uint size);
        const ulong WnfQuietHours = 0x0D83063EA3BF1C75;   // WNF_SHEL_QUIETHOURS_ACTIVE_PROFILE_CHANGED

        static readonly DispatcherTimer _focus = new() { Interval = TimeSpan.FromSeconds(3) };
        static string _modeBefore;

        /// <summary>0 — выключено, 1 — только приоритетные, 2 — только будильники; null — не удалось узнать.</summary>
        public static int? FocusAssist()
        {
            try
            {
                var n = WnfQuietHours;
                var b = new byte[16];
                uint size = (uint)b.Length;
                if (NtQueryWnfStateData(ref n, IntPtr.Zero, IntPtr.Zero, out _, b, ref size) != 0 || size < 4) return null;
                return BitConverter.ToInt32(b, 0);
            }
            catch { return null; }
        }

        static void CheckFocus()
        {
            var v = FocusAssist();
            if (v == null) return;
            if (v > 0 && S.Mode != "dnd" && _modeBefore == null)
            {
                _modeBefore = S.Mode;
                App.Island?.SetMode("dnd");
            }
            else if (v == 0) RestoreMode();
        }

        static void RestoreMode()
        {
            if (_modeBefore == null) return;
            if (S.Mode == "dnd") App.Island?.SetMode(_modeBefore);
            _modeBefore = null;
        }

        // ------------------------------------------------------------------
        //  Акцентный цвет Windows
        // ------------------------------------------------------------------

        public static string WindowsAccentHex()
        {
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
                if (k?.GetValue("AccentColor") is int abgr)
                {
                    uint v = unchecked((uint)abgr);
                    return $"#{v & 0xFF:X2}{(v >> 8) & 0xFF:X2}{(v >> 16) & 0xFF:X2}";
                }
            }
            catch { }
            return null;
        }

        static void SyncAccent()
        {
            var hex = WindowsAccentHex();
            if (hex == null || string.Equals(S.Accent, hex, StringComparison.OrdinalIgnoreCase)) return;
            S.Accent = hex;
        }
    }
}
