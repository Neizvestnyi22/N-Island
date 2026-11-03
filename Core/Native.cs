using System;
using System.Runtime.InteropServices;
using System.Text;

namespace NIsland.Core
{
    public static class Native
    {
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TOOLWINDOW = 0x80;
        public const int WS_EX_NOACTIVATE = 0x08000000;
        public const int WS_EX_APPWINDOW = 0x40000;
        public const int WM_HOTKEY = 0x0312;
        public const int WM_SETTINGCHANGE = 0x001A;

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        // в 32-битной Windows функций *Ptr нет — там только GetWindowLong/SetWindowLong
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr64(IntPtr h, int i);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr64(IntPtr h, int i, IntPtr v);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] static extern int GetWindowLong32(IntPtr h, int i);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] static extern int SetWindowLong32(IntPtr h, int i, int v);

        public static IntPtr GetWindowLongPtr(IntPtr h, int i) => IntPtr.Size == 8 ? GetWindowLongPtr64(h, i) : new IntPtr(GetWindowLong32(h, i));
        public static IntPtr SetWindowLongPtr(IntPtr h, int i, IntPtr v) => IntPtr.Size == 8 ? SetWindowLongPtr64(h, i, v) : new IntPtr(SetWindowLong32(h, i, v.ToInt32()));
        [DllImport("user32.dll")] public static extern int SetWindowRgn(IntPtr h, IntPtr rgn, bool redraw);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int w, int h);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateRectRgn(int x1, int y1, int x2, int y2);
        [DllImport("gdi32.dll")] public static extern int CombineRgn(IntPtr dst, IntPtr a, IntPtr b, int mode);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr o);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern bool LockWorkStation();
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int RegisterWindowMessage(string name);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h, int id);
        [DllImport("kernel32.dll")] public static extern uint SetThreadExecutionState(uint flags);
        [DllImport("kernel32.dll")] public static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
        [DllImport("user32.dll")] static extern int SetWindowCompositionAttribute(IntPtr h, ref WinCompAttrData data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr SendMessageTimeout(IntPtr h, int msg, IntPtr w, string l, int flags, int timeout, out IntPtr result);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public class MEMORYSTATUSEX
        {
            public uint dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            public uint dwMemoryLoad;
            public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX m);

        [StructLayout(LayoutKind.Sequential)]
        struct AccentPolicy { public int AccentState; public int AccentFlags; public uint GradientColor; public int AnimationId; }

        [StructLayout(LayoutKind.Sequential)]
        struct WinCompAttrData { public int Attribute; public IntPtr Data; public int SizeOfData; }

        static readonly IntPtr HWND_TOPMOST = new(-1);
        const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10;

        [StructLayout(LayoutKind.Sequential)]
        struct MARGINS { public int L, R, T, B; }
        [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr h, ref MARGINS m);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int value, int size);

        /// <summary>
        /// Обычное (не layered) окно с прозрачным фоном через DWM: форму задаёт регион окна,
        /// а размытие и клики работают строго внутри формы.
        /// </summary>
        public static void MakeGlassSheet(IntPtr h)
        {
            var m = new MARGINS { L = -1, R = -1, T = -1, B = -1 };
            DwmExtendFrameIntoClientArea(h, ref m);
            int noRound = 1;               // DWMWCP_DONOTROUND
            DwmSetWindowAttribute(h, 33, ref noRound, 4);
            int noBorder = unchecked((int)0xFFFFFFFE); // DWMWA_COLOR_NONE
            DwmSetWindowAttribute(h, 34, ref noBorder, 4);
            int disable = 1;               // без анимаций появления окна
            DwmSetWindowAttribute(h, 3, ref disable, 4);
        }

        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr h);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, IntPtr pid);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] static extern bool AttachThreadInput(uint a, uint b, bool attach);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);

        /// <summary>Разрешить/запретить окну становиться активным (нужно для ввода текста).</summary>
        public static void SetNoActivate(IntPtr h, bool on)
        {
            long ex = GetWindowLongPtr(h, GWL_EXSTYLE).ToInt64();
            ex = on ? ex | WS_EX_NOACTIVATE : ex & ~WS_EX_NOACTIVATE;
            SetWindowLongPtr(h, GWL_EXSTYLE, new IntPtr(ex));
        }

        /// <summary>
        /// Сделать окно активным, чтобы оно получало ввод с клавиатуры. Windows не даёт фоновому
        /// процессу забирать фокус, поэтому на миг подключаемся к потоку текущего активного окна.
        /// Возвращает окно, которое было активным до этого (чтобы потом вернуть ему фокус).
        /// </summary>
        public static IntPtr ForceForeground(IntPtr h)
        {
            var prev = GetForegroundWindow();
            if (prev == h) return IntPtr.Zero;
            uint fgThread = GetWindowThreadProcessId(prev, IntPtr.Zero), me = GetCurrentThreadId();
            bool attached = fgThread != 0 && fgThread != me && AttachThreadInput(me, fgThread, true);
            BringWindowToTop(h);
            SetForegroundWindow(h);
            if (attached) AttachThreadInput(me, fgThread, false);
            return prev;
        }

        /// <summary>Вернуть фокус окну, которое было активным до чёлки.</summary>
        public static void RestoreForeground(IntPtr prev)
        {
            if (prev != IntPtr.Zero && IsWindow(prev)) SetForegroundWindow(prev);
        }

        public static void MakeToolWindow(IntPtr h, bool noActivate)
        {
            long ex = GetWindowLongPtr(h, GWL_EXSTYLE).ToInt64();
            ex |= WS_EX_TOOLWINDOW;
            ex &= ~WS_EX_APPWINDOW;
            if (noActivate) ex |= WS_EX_NOACTIVATE;
            SetWindowLongPtr(h, GWL_EXSTYLE, new IntPtr(ex));
        }

        public static void KeepTopmost(IntPtr h) =>
            SetWindowPos(h, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

        /// <summary>Акриловое размытие под окном (Windows 10 1803+ / 11). Форма берётся из региона окна.</summary>
        [DllImport("user32.dll")] static extern bool SetWindowDisplayAffinity(IntPtr h, uint affinity);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("gdi32.dll")] static extern int SetStretchBltMode(IntPtr dc, int mode);
        [DllImport("gdi32.dll")] static extern bool StretchBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, int sw, int sh, int rop);

        /// <summary>
        /// Уменьшенный снимок экрана под окном (само окно исключается из снимка).
        /// Из него делается «стекло» — дёшево по памяти и не ломает форму острова.
        /// </summary>
        public static System.Windows.Media.Imaging.BitmapSource CaptureBehind(IntPtr self, int x, int y, int w, int h, int down)
        {
            int sw = Math.Max(1, w / down), sh = Math.Max(1, h / down);
            using var bmp = new System.Drawing.Bitmap(sw, sh, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            SetWindowDisplayAffinity(self, 0x11); // WDA_EXCLUDEFROMCAPTURE
            try
            {
                using var g = System.Drawing.Graphics.FromImage(bmp);
                IntPtr dst = g.GetHdc();
                IntPtr scr = GetDC(IntPtr.Zero);
                SetStretchBltMode(dst, 4); // HALFTONE — усреднение пикселей
                StretchBlt(dst, 0, 0, sw, sh, scr, x, y, w, h, 0x00CC0020);
                ReleaseDC(IntPtr.Zero, scr);
                g.ReleaseHdc(dst);
            }
            finally { SetWindowDisplayAffinity(self, 0); }
            IntPtr hb = bmp.GetHbitmap();
            try
            {
                var src = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(hb, IntPtr.Zero, System.Windows.Int32Rect.Empty,
                    System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                return src;
            }
            finally { DeleteObject(hb); }
        }

        public static void SetBlur(IntPtr h, bool on)
        {
            var accent = new AccentPolicy
            {
                AccentState = on ? 4 : 0,   // ACCENT_ENABLE_ACRYLICBLURBEHIND : DISABLED
                AccentFlags = 2,
                GradientColor = 0x10000000,
            };
            int size = Marshal.SizeOf(accent);
            IntPtr ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(accent, ptr, false);
                var data = new WinCompAttrData { Attribute = 19, Data = ptr, SizeOfData = size };
                SetWindowCompositionAttribute(h, ref data);
            }
            finally { Marshal.FreeHGlobal(ptr); }
        }

        public static bool IsForegroundFullscreen(IntPtr self, System.Windows.Forms.Screen target)
        {
            var fg = GetForegroundWindow();
            if (fg == IntPtr.Zero || fg == self) return false;
            var sb = new StringBuilder(64);
            GetClassName(fg, sb, 64);
            var cls = sb.ToString();
            if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "XamlExplorerHostIslandWindow")
                return false;
            if (!GetWindowRect(fg, out var r)) return false;
            var scr = System.Windows.Forms.Screen.FromHandle(fg);
            if (target != null && scr.DeviceName != target.DeviceName) return false;
            var b = scr.Bounds;
            return r.Left <= b.Left && r.Top <= b.Top && r.Right >= b.Right && r.Bottom >= b.Bottom;
        }

        public static void BroadcastThemeChange()
        {
            SendMessageTimeout(new IntPtr(0xFFFF), WM_SETTINGCHANGE, IntPtr.Zero, "ImmersiveColorSet", 2, 200, out _);
        }
    }
}
