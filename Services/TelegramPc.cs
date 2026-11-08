using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using NIsland.Core;

namespace NIsland.Services
{
    /// <summary>
    /// Управление ПК для Telegram-бота: окна, клавиатура и мышь, экран, обои, камера, местоположение, голос, файлы.
    /// Всё вызывается только из команд владельца бота и только если это разрешено в настройках интеграции.
    /// </summary>
    static class PcControl
    {
        // ------------------------------------------------------------------
        //  Окна
        // ------------------------------------------------------------------

        delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr l);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder sb, int n);
        [DllImport("user32.dll")] static extern int GetWindowTextLength(IntPtr h);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint cmd);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int idx);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int v, int size);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }

        public record Win(IntPtr Handle, string Title, string Process, bool Minimized, bool Active);

        static readonly string[] HiddenProcs = { "nisland", "textinputhost", "shellexperiencehost", "searchhost", "startmenuexperiencehost", "lockapp" };

        public static List<Win> ListWindows()
        {
            var list = new List<Win>();
            var fg = GetForegroundWindow();
            EnumWindows((h, _) =>
            {
                try
                {
                    if (!IsWindowVisible(h) || GetWindow(h, 4 /*GW_OWNER*/) != IntPtr.Zero) return true;
                    if ((GetWindowLong(h, -20) & 0x80 /*WS_EX_TOOLWINDOW*/) != 0) return true;
                    if (DwmGetWindowAttribute(h, 14 /*DWMWA_CLOAKED*/, out int cloaked, 4) == 0 && cloaked != 0) return true;
                    int len = GetWindowTextLength(h);
                    if (len == 0) return true;
                    var sb = new StringBuilder(len + 1);
                    GetWindowText(h, sb, sb.Capacity);
                    var title = sb.ToString();
                    if (title == "Program Manager") return true;
                    GetWindowThreadProcessId(h, out var pid);
                    string proc = "";
                    try { using var p = Process.GetProcessById((int)pid); proc = p.ProcessName; } catch { }
                    if (HiddenProcs.Contains(proc.ToLowerInvariant())) return true;
                    list.Add(new Win(h, title, proc, IsIconic(h), h == fg));
                }
                catch { }
                return true;
            }, IntPtr.Zero);
            return list;
        }

        public static string ActiveTitle()
        {
            var h = GetForegroundWindow();
            int len = GetWindowTextLength(h);
            if (len == 0) return "рабочий стол";
            var sb = new StringBuilder(len + 1);
            GetWindowText(h, sb, sb.Capacity);
            return sb.ToString();
        }

        public static bool Exists(IntPtr h) => IsWindow(h);

        /// <summary>Окно на передний план. Windows не даёт фоновой программе забирать фокус — нажатие Alt снимает запрет.</summary>
        public static void Focus(IntPtr h)
        {
            if (IsIconic(h)) ShowWindow(h, 9 /*SW_RESTORE*/);
            Key(0x12, false); Key(0x12, true);
            SetForegroundWindow(h);
        }

        public static string WindowAction(IntPtr h, char act)
        {
            if (!IsWindow(h)) return "Окно уже закрыто";
            switch (act)
            {
                case 'f': Focus(h); return "Окно на переднем плане";
                case 'm': ShowWindow(h, 6 /*SW_MINIMIZE*/); return "Свёрнуто";
                case 'x':
                    bool max = IsZoomed(h);
                    ShowWindow(h, max ? 9 : 3 /*SW_MAXIMIZE*/);
                    return max ? "Обычный размер" : "Развёрнуто";
                case 'c': PostMessage(h, 0x0010 /*WM_CLOSE*/, IntPtr.Zero, IntPtr.Zero); return "Закрываю окно";
            }
            return null;
        }

        // ------------------------------------------------------------------
        //  Клавиатура и мышь
        // ------------------------------------------------------------------

        [StructLayout(LayoutKind.Sequential)] struct INPUT { public int type; public InputUnion u; }
        [StructLayout(LayoutKind.Explicit)] struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
        [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr extra; }
        [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr extra; }
        [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint n, INPUT[] inputs, int size);
        [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }

        static void Send(params INPUT[] inputs) => SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());

        static bool Extended(ushort vk) => vk is >= 0x21 and <= 0x2E or 0x5B or 0x5C or 0x5D or 0x6F or 0x90;

        static INPUT KeyInput(ushort vk, bool up) => new()
        {
            type = 1,
            u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = (up ? 2u : 0u) | (Extended(vk) ? 1u : 0u) } },
        };

        static void Key(ushort vk, bool up) => Send(KeyInput(vk, up));

        static readonly Dictionary<string, ushort> Vk = new(StringComparer.OrdinalIgnoreCase)
        {
            ["ctrl"] = 0x11, ["control"] = 0x11, ["ctl"] = 0x11, ["alt"] = 0x12, ["shift"] = 0x10, ["win"] = 0x5B, ["cmd"] = 0x5B,
            ["enter"] = 0x0D, ["return"] = 0x0D, ["esc"] = 0x1B, ["escape"] = 0x1B, ["tab"] = 0x09, ["space"] = 0x20, ["пробел"] = 0x20,
            ["backspace"] = 0x08, ["bksp"] = 0x08, ["delete"] = 0x2E, ["del"] = 0x2E, ["insert"] = 0x2D, ["ins"] = 0x2D,
            ["up"] = 0x26, ["down"] = 0x28, ["left"] = 0x25, ["right"] = 0x27, ["home"] = 0x24, ["end"] = 0x23,
            ["pgup"] = 0x21, ["pageup"] = 0x21, ["pgdn"] = 0x22, ["pagedown"] = 0x22, ["prtsc"] = 0x2C, ["printscreen"] = 0x2C,
            ["capslock"] = 0x14, ["menu"] = 0x5D, ["apps"] = 0x5D,
            ["volup"] = 0xAF, ["voldown"] = 0xAE, ["mute"] = 0xAD, ["play"] = 0xB3, ["next"] = 0xB0, ["prev"] = 0xB1, ["stop"] = 0xB2,
            ["plus"] = 0xBB, ["minus"] = 0xBD, ["comma"] = 0xBC, ["period"] = 0xBE,
        };

        static ushort? Parse(string name)
        {
            name = name.Trim();
            if (Vk.TryGetValue(name, out var v)) return v;
            if (name.Length == 1)
            {
                char c = char.ToUpperInvariant(name[0]);
                if (c is >= 'A' and <= 'Z' or >= '0' and <= '9') return c;
            }
            if (name.Length >= 2 && (name[0] is 'f' or 'F') && int.TryParse(name[1..], out var f) && f is >= 1 and <= 24) return (ushort)(0x70 + f - 1);
            return null;
        }

        /// <summary>Сочетание клавиш: «ctrl+shift+esc», «alt+f4», «space». Возвращает null, если всё нажато.</summary>
        public static string Hotkey(string combo)
        {
            var parts = combo.Replace(" ", "").Split('+', StringSplitOptions.RemoveEmptyEntries);
            var keys = new List<ushort>();
            foreach (var p in parts)
            {
                var k = Parse(p);
                if (k == null) return "Не знаю клавишу «" + p + "». Есть: ctrl, alt, shift, win, enter, esc, tab, space, стрелки up/down/left/right, f1–f12, буквы и цифры";
                keys.Add(k.Value);
            }
            if (keys.Count == 0) return "Пустое сочетание";
            var seq = keys.Select(k => KeyInput(k, false)).Concat(Enumerable.Reverse(keys).Select(k => KeyInput(k, true))).ToArray();
            Send(seq);
            return null;
        }

        /// <summary>Напечатать текст в активное окно (любой язык — символами Юникода, не зависит от раскладки).</summary>
        public static void Type(string text)
        {
            var inputs = new List<INPUT>();
            foreach (var ch in text.Replace("\r\n", "\n"))
            {
                if (ch == '\n') { inputs.Add(KeyInput(0x0D, false)); inputs.Add(KeyInput(0x0D, true)); continue; }
                inputs.Add(new INPUT { type = 1, u = new InputUnion { ki = new KEYBDINPUT { wScan = ch, dwFlags = 4 /*UNICODE*/ } } });
                inputs.Add(new INPUT { type = 1, u = new InputUnion { ki = new KEYBDINPUT { wScan = ch, dwFlags = 4 | 2 } } });
            }
            // кусками: длинный текст одним вызовом некоторые программы теряют
            for (int i = 0; i < inputs.Count; i += 200)
            {
                Send(inputs.Skip(i).Take(200).ToArray());
                Thread.Sleep(15);
            }
        }

        public static void MouseMove(int dx, int dy)
        {
            GetCursorPos(out var p);
            SetCursorPos(p.X + dx, p.Y + dy);
        }

        public static void MouseCenter()
        {
            var b = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
            SetCursorPos(b.Left + b.Width / 2, b.Top + b.Height / 2);
        }

        static INPUT Mouse(uint flags, int data = 0) => new() { type = 0, u = new InputUnion { mi = new MOUSEINPUT { dwFlags = flags, mouseData = (uint)data } } };

        public static void Click(bool right = false, bool dbl = false)
        {
            uint down = right ? 0x8u : 0x2u, up = right ? 0x10u : 0x4u;
            Send(Mouse(down), Mouse(up));
            if (dbl) { Thread.Sleep(60); Send(Mouse(down), Mouse(up)); }
        }

        public static void Wheel(int clicks) => Send(Mouse(0x0800, clicks * 120));

        // ------------------------------------------------------------------
        //  Экран
        // ------------------------------------------------------------------

        /// <summary>Выключить экраны (проснутся от мыши или клавиатуры).</summary>
        public static void MonitorOff() => PostMessage((IntPtr)0xFFFF, 0x0112 /*WM_SYSCOMMAND*/, (IntPtr)0xF170 /*SC_MONITORPOWER*/, (IntPtr)2);

        /// <summary>Разбудить экран: короткое движение мыши.</summary>
        public static void MonitorOn() { MouseMove(1, 0); MouseMove(-1, 0); }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool SystemParametersInfo(int action, int param, string value, int flags);

        public static string SetWallpaper(byte[] image, string ext)
        {
            var dir = Path.Combine(Store.Dir, "wallpapers");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"tg-{DateTime.Now:yyyyMMdd-HHmmss}{ext}");
            File.WriteAllBytes(path, image);
            using (var k = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", true))
            {
                k?.SetValue("WallpaperStyle", "10");   // «Заполнение»
                k?.SetValue("TileWallpaper", "0");
            }
            return SystemParametersInfo(0x14 /*SPI_SETDESKWALLPAPER*/, 0, path, 3) ? null : "Windows не приняла картинку";
        }

        /// <summary>Снимок экрана: все мониторы, один монитор (1, 2…) или активное окно (-1). JPEG — быстро, PNG — без потерь.</summary>
        public static byte[] Screenshot(int which, bool png)
        {
            System.Drawing.Rectangle r;
            if (which == -1 && GetWindowRect(GetForegroundWindow(), out var wr) && wr.Right - wr.Left > 10)
                r = new System.Drawing.Rectangle(wr.Left, wr.Top, wr.Right - wr.Left, wr.Bottom - wr.Top);
            else if (which >= 1 && which <= System.Windows.Forms.Screen.AllScreens.Length)
                r = System.Windows.Forms.Screen.AllScreens[which - 1].Bounds;
            else
                r = System.Windows.Forms.SystemInformation.VirtualScreen;
            using var bmp = new System.Drawing.Bitmap(r.Width, r.Height);
            using (var g = System.Drawing.Graphics.FromImage(bmp)) g.CopyFromScreen(r.Left, r.Top, 0, 0, r.Size);
            using var ms = new MemoryStream();
            if (png) bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            else
            {
                var codec = System.Drawing.Imaging.ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == System.Drawing.Imaging.ImageFormat.Jpeg.Guid);
                using var ep = new System.Drawing.Imaging.EncoderParameters(1);
                ep.Param[0] = new System.Drawing.Imaging.EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 85L);
                bmp.Save(ms, codec, ep);
            }
            return ms.ToArray();
        }

        public static int ScreenCount => System.Windows.Forms.Screen.AllScreens.Length;

        // ------------------------------------------------------------------
        //  Камера и местоположение («найти ноутбук»)
        // ------------------------------------------------------------------

        public static async Task<byte[]> CameraPhoto()
        {
            var mc = new global::Windows.Media.Capture.MediaCapture();
            try
            {
                await mc.InitializeAsync(new global::Windows.Media.Capture.MediaCaptureInitializationSettings
                {
                    StreamingCaptureMode = global::Windows.Media.Capture.StreamingCaptureMode.Video,
                });
                await Task.Delay(900);   // камере нужно мгновение на экспозицию, иначе кадр тёмный
                using var ras = new global::Windows.Storage.Streams.InMemoryRandomAccessStream();
                await mc.CapturePhotoToStreamAsync(global::Windows.Media.MediaProperties.ImageEncodingProperties.CreateJpeg(), ras);
                return await Read(ras);
            }
            catch (UnauthorizedAccessException) { throw new InvalidOperationException("Windows запрещает программам доступ к камере (Параметры → Конфиденциальность → Камера)"); }
            catch (Exception e) when (e.HResult == unchecked((int)0xC00D36D5) || e.HResult == unchecked((int)0xC00DABE0))
            { throw new InvalidOperationException("Камера не найдена или занята другой программой"); }
            finally { mc.Dispose(); }
        }

        static async Task<byte[]> Read(Windows.Storage.Streams.IRandomAccessStream s)
        {
            var bytes = new byte[s.Size];
            using var reader = new global::Windows.Storage.Streams.DataReader(s.GetInputStreamAt(0));
            await reader.LoadAsync((uint)s.Size);
            reader.ReadBytes(bytes);
            return bytes;
        }

        public static async Task<(double Lat, double Lon, double Acc)?> Location()
        {
            try
            {
                var access = await global::Windows.Devices.Geolocation.Geolocator.RequestAccessAsync();
                if (access != global::Windows.Devices.Geolocation.GeolocationAccessStatus.Allowed) return null;
                var g = new global::Windows.Devices.Geolocation.Geolocator { DesiredAccuracyInMeters = 50 };
                var pos = await g.GetGeopositionAsync(TimeSpan.FromMinutes(2), TimeSpan.FromSeconds(20));
                var p = pos.Coordinate.Point.Position;
                return (p.Latitude, p.Longitude, pos.Coordinate.Accuracy);
            }
            catch (Exception e) { Log.Info("location: " + e.Message); return null; }
        }

        // ------------------------------------------------------------------
        //  Голос: прочитать текст вслух на ПК
        // ------------------------------------------------------------------

        public static async Task Speak(string text)
        {
            using var synth = new global::Windows.Media.SpeechSynthesis.SpeechSynthesizer();
            string lang = text.Any(c => c is 'і' or 'ї' or 'є' or 'ґ' or 'І' or 'Ї' or 'Є') ? "uk" : text.Any(c => c is >= 'А' and <= 'я') ? "ru" : "en";
            var voice = global::Windows.Media.SpeechSynthesis.SpeechSynthesizer.AllVoices.FirstOrDefault(v => v.Language.StartsWith(lang, StringComparison.OrdinalIgnoreCase))
                        ?? (lang == "uk" ? global::Windows.Media.SpeechSynthesis.SpeechSynthesizer.AllVoices.FirstOrDefault(v => v.Language.StartsWith("ru")) : null);
            if (voice != null) synth.Voice = voice;
            using var stream = await synth.SynthesizeTextToStreamAsync(text);
            var wav = await Read(stream);
            await Task.Run(() =>
            {
                using var ms = new MemoryStream(wav);
                using var player = new System.Media.SoundPlayer(ms);
                player.PlaySync();
            });
        }

        // ------------------------------------------------------------------
        //  Система
        // ------------------------------------------------------------------

        [DllImport("user32.dll")] static extern bool ExitWindowsEx(uint flags, uint reason);
        public static void LogOff() => ExitWindowsEx(0 /*EWX_LOGOFF*/, 0);

        public static string SysInfo()
        {
            var sb = new StringBuilder();
            try
            {
                using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
                var name = k?.GetValue("ProductName") as string ?? "Windows";
                if (Environment.OSVersion.Version.Build >= 22000) name = name.Replace("Windows 10", "Windows 11");
                sb.Append($"🪟 {name} {k?.GetValue("DisplayVersion")} · сборка {Environment.OSVersion.Version.Build}\n");
            }
            catch { }
            try
            {
                using var k = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                sb.Append($"⚙️ {(k?.GetValue("ProcessorNameString") as string)?.Trim()} · {Environment.ProcessorCount} потоков\n");
            }
            catch { }
            try
            {
                using var mos = new System.Management.ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
                foreach (var o in mos.Get()) sb.Append($"🎮 {o["Name"]}\n");
            }
            catch { }
            sb.Append($"🧠 Память {Maint.Size(Maint.TotalRam())}, свободно {Maint.Size(Maint.AvailableRam())}\n");
            foreach (var d in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
                sb.Append($"💾 {d.Name.TrimEnd('\\')} {Maint.Size(d.TotalSize)}, свободно {Maint.Size(d.AvailableFreeSpace)}\n");
            sb.Append($"💻 {Environment.MachineName} · {Environment.UserName}\n");
            var up = Maint.Uptime;
            sb.Append($"⏱ Работает {(up.TotalDays >= 1 ? (int)up.TotalDays + " дн " : "")}{up.Hours} ч {up.Minutes} мин");
            return sb.ToString();
        }

        // ------------------------------------------------------------------
        //  Файлы
        // ------------------------------------------------------------------

        public static readonly (string Name, Func<string> Path)[] Places =
        {
            ("🖥 Рабочий стол", () => Environment.GetFolderPath(Environment.SpecialFolder.Desktop)),
            ("⬇️ Загрузки", () => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")),
            ("📄 Документы", () => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
            ("🖼 Изображения", () => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)),
            ("🎬 Видео", () => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)),
            ("🎵 Музыка", () => Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)),
            ("🏠 Папка пользователя", () => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)),
        };

        /// <summary>Поиск файлов по части имени в папках пользователя (не глубже 6 уровней, не дольше 6 с).</summary>
        public static List<string> Search(string query, int max = 20)
        {
            var res = new List<string>();
            var sw = Stopwatch.StartNew();
            var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, MaxRecursionDepth = 6, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint };
            var roots = Places.Take(6).Select(p => p.Path()).Where(Directory.Exists).Distinct().ToList();
            foreach (var root in roots)
            {
                try
                {
                    foreach (var f in Directory.EnumerateFileSystemEntries(root, "*" + query + "*", opts))
                    {
                        res.Add(f);
                        if (res.Count >= max || sw.ElapsedMilliseconds > 6000) return res;
                    }
                }
                catch { }
                if (sw.ElapsedMilliseconds > 6000) break;
            }
            return res;
        }

        public static void Recycle(string path)
        {
            if (Directory.Exists(path))
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            else
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
        }

        /// <summary>Упаковать папку во временный zip (для отправки). null — папка слишком большая.</summary>
        public static string Zip(string dir, long maxBytes)
        {
            long total = 0;
            foreach (var f in Directory.EnumerateFiles(dir, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }))
            {
                try { total += new FileInfo(f).Length; } catch { }
                if (total > maxBytes * 3) return null;
            }
            var zip = Path.Combine(Path.GetTempPath(), "nisland-" + Path.GetFileName(dir.TrimEnd('\\')) + ".zip");
            if (File.Exists(zip)) File.Delete(zip);
            System.IO.Compression.ZipFile.CreateFromDirectory(dir, zip, System.IO.Compression.CompressionLevel.Fastest, false);
            if (new FileInfo(zip).Length > maxBytes) { File.Delete(zip); return null; }
            return zip;
        }
    }
}
