using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace NIsland.Core
{
    public static class Fmt
    {
        static readonly CultureInfo RuCulture = new("ru-RU"), EnCulture = new("en-US"), UkCulture = new("uk-UA");

        /// <summary>Культура для дат и чисел — на языке интерфейса (исторически называется Ru: по умолчанию русский).</summary>
        public static CultureInfo Ru => I18n.Current switch { "en" => EnCulture, "uk" => UkCulture, _ => RuCulture };

        public static string Time(TimeSpan t)
        {
            if (t < TimeSpan.Zero) t = TimeSpan.Zero;
            return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes:00}:{t.Seconds:00}";
        }

        public static string Date(DateTime d)
        {
            var s = d.ToString("ddd, d MMM yyyy", Ru).Replace(".", "");
            return char.ToUpper(s[0]) + s[1..];
        }

        public static string Liters(int ml) => (ml / 1000.0).ToString(ml % 100 == 0 ? "0.0" : "0.00", CultureInfo.InvariantCulture);

        public static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s[1..];
    }

    public static class ColorUtil
    {
        public static Color Parse(string hex, Color fallback)
        {
            try { return (Color)ColorConverter.ConvertFromString(hex); } catch { return fallback; }
        }

        public static Color WithAlpha(Color c, double a) => Color.FromArgb((byte)Math.Clamp(a * 255, 0, 255), c.R, c.G, c.B);

        /// <summary>Яркий доминирующий цвет обложки — для подсветки плеера.</summary>
        public static Color Dominant(BitmapSource src)
        {
            try
            {
                double scale = 32.0 / Math.Max(src.PixelWidth, src.PixelHeight);
                BitmapSource small = new TransformedBitmap(src, new ScaleTransform(scale, scale));
                var conv = new FormatConvertedBitmap(small, PixelFormats.Bgra32, null, 0);
                int w = conv.PixelWidth, h = conv.PixelHeight, stride = w * 4;
                var px = new byte[h * stride];
                conv.CopyPixels(px, stride, 0);
                double r = 0, g = 0, b = 0, wsum = 0;
                for (int i = 0; i < px.Length; i += 4)
                {
                    double bb = px[i] / 255.0, gg = px[i + 1] / 255.0, rr = px[i + 2] / 255.0;
                    double max = Math.Max(rr, Math.Max(gg, bb)), min = Math.Min(rr, Math.Min(gg, bb));
                    double sat = max == 0 ? 0 : (max - min) / max;
                    double wgt = sat * sat * max + 0.02;
                    r += rr * wgt; g += gg * wgt; b += bb * wgt; wsum += wgt;
                }
                r /= wsum; g /= wsum; b /= wsum;
                ToHsv(r, g, b, out var hh, out var ss, out var vv);
                ss = Math.Clamp(ss * 1.25, 0.45, 0.95);
                vv = Math.Clamp(vv * 1.2, 0.6, 0.95);
                FromHsv(hh, ss, vv, out r, out g, out b);
                return Color.FromRgb((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
            }
            catch { return Color.FromRgb(0x0A, 0x84, 0xFF); }
        }

        static void ToHsv(double r, double g, double b, out double h, out double s, out double v)
        {
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
            v = max; s = max == 0 ? 0 : d / max;
            if (d == 0) h = 0;
            else if (max == r) h = 60 * (((g - b) / d) % 6);
            else if (max == g) h = 60 * ((b - r) / d + 2);
            else h = 60 * ((r - g) / d + 4);
            if (h < 0) h += 360;
        }

        static void FromHsv(double h, double s, double v, out double r, out double g, out double b)
        {
            double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
            (r, g, b) = h switch
            {
                < 60 => (c, x, 0.0),
                < 120 => (x, c, 0.0),
                < 180 => (0.0, c, x),
                < 240 => (0.0, x, c),
                < 300 => (x, 0.0, c),
                _ => (c, 0.0, x),
            };
            r += m; g += m; b += m;
        }
    }

    public static class FileIcons
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct SHFILEINFO
        {
            public IntPtr hIcon; public int iIcon; public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
        }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SHGetFileInfo(string path, uint attr, ref SHFILEINFO info, uint size, uint flags);
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);

        public static ImageSource Get(string path)
        {
            try
            {
                var info = new SHFILEINFO();
                SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf(info), 0x100 /* ICON */ | 0x0 /* LARGE */);
                if (info.hIcon == IntPtr.Zero) return null;
                var img = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                img.Freeze();
                DestroyIcon(info.hIcon);
                return img;
            }
            catch { return null; }
        }
    }

    public static class AutoStart
    {
        const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string Approved = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

        /// <summary>Прописывает запуск при входе в Windows (тихо, с ключом --autostart) и снимает запрет из Диспетчера задач.</summary>
        public static void Apply(bool on)
        {
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(Key, true);
                if (k == null) return;
                if (on)
                {
                    var cmd = $"\"{Environment.ProcessPath}\" --autostart";
                    if (k.GetValue("NIsland") as string != cmd) k.SetValue("NIsland", cmd);
                    using var a = Registry.CurrentUser.CreateSubKey(Approved);
                    if (a.GetValue("NIsland") is byte[] b && b.Length > 0 && b[0] % 2 == 1)
                        a.SetValue("NIsland", new byte[12] { 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, RegistryValueKind.Binary);
                }
                else k.DeleteValue("NIsland", false);
            }
            catch (Exception e) { Log.Error(e); }
        }

        /// <summary>Состояние для настроек: работает ли автозагрузка на самом деле.</summary>
        public static string Status()
        {
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(Key);
                var cmd = k?.GetValue("NIsland") as string;
                if (cmd == null) return "выключена";
                using var a = Registry.CurrentUser.OpenSubKey(Approved);
                if (a?.GetValue("NIsland") is byte[] b && b.Length > 0 && b[0] % 2 == 1) return "запрещена в Диспетчере задач — включи переключатель ещё раз";
                return cmd.Contains(Environment.ProcessPath ?? "?", StringComparison.OrdinalIgnoreCase) ? "работает ✓" : "указывает на другую копию — включи ещё раз";
            }
            catch { return "неизвестно"; }
        }
    }

    public static class Paths
    {
        public static string ShortName(string path)
        {
            var n = Path.GetFileName(path.TrimEnd('\\', '/'));
            return string.IsNullOrEmpty(n) ? path : n;
        }
    }
}
