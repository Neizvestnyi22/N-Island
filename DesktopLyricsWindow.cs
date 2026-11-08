using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using NIsland.Core;
using NIsland.Pages;
using NIsland.Services;

namespace NIsland
{
    /// <summary>
    /// Текст песни на рабочем столе: строка поверх всех окон с караоке-заливкой. Закреплённая — пропускает клики насквозь;
    /// открепить можно в плеере, в трее или в настройках — тогда её можно перетащить и поменять размер.
    /// </summary>
    public class DesktopLyricsWindow : Window
    {
        static DesktopLyricsWindow _win;
        static Settings S => Store.Settings;
        static MediaService M => MediaService.I;

        /// <summary>Показать или убрать по настройке (и применить изменения стиля).</summary>
        public static void Apply()
        {
            if (S.DesktopLyrics)
            {
                if (_win == null) { _win = new DesktopLyricsWindow(); _win.Closed += (_, __) => _win = null; _win.Show(); }
                else _win.Restyle();
            }
            else { _win?.Close(); _win = null; }
        }

        public static void Toggle() { S.DesktopLyrics = !S.DesktopLyrics; Apply(); }

        public static void SetLocked(bool locked) { S.DeskLyricsLock = locked; _win?.ApplyLock(); }

        readonly TextBlock _cur = new() { TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.Bold, Tag = "no-translate" };
        readonly TextBlock _next = new() { TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold, Tag = "no-translate", Margin = new Thickness(0, 4, 0, 0) };
        readonly Border _card = new() { CornerRadius = new CornerRadius(18), Padding = new Thickness(22, 10, 22, 12) };
        readonly StackPanel _tools = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
        readonly StackPanel _body = new();
        readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
        GradientStop _k1, _k2;
        int _line = -2;
        IntPtr _hwnd;

        DesktopLyricsWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.Height;
            Title = "N-Island — текст песни";
            var wa = SystemParameters.WorkArea;
            Width = Math.Min(1100, wa.Width * 0.72);

            _tools.Children.Add(UI.Pill("🔒 Закрепить", () => SetLocked(true), true, "Строка перестанет мешать кликам"));
            _tools.Children.Add(UI.Pill("A−", () => { S.DeskLyricsSize = Math.Max(16, S.DeskLyricsSize - 2); Restyle(); }).M(6, 0, 0, 0));
            _tools.Children.Add(UI.Pill("A+", () => { S.DeskLyricsSize = Math.Min(72, S.DeskLyricsSize + 2); Restyle(); }).M(6, 0, 0, 0));
            _tools.Children.Add(UI.Pill("✕", () => { S.DesktopLyrics = false; Apply(); }, false, "Убрать с рабочего стола").M(6, 0, 0, 0));
            _body.Children.Add(_cur);
            _body.Children.Add(_next);
            _body.Children.Add(_tools);
            _card.Child = _body;
            Content = _card;

            MouseLeftButtonDown += (_, e) =>
            {
                if (S.DeskLyricsLock) return;
                try { DragMove(); } catch { }
                S.DeskLyricsPos = $"{(int)Left},{(int)Top}";
            };
            SourceInitialized += (_, __) =>
            {
                _hwnd = new WindowInteropHelper(this).Handle;
                ApplyLock();
            };
            Loaded += (_, __) => { Place(); Restyle(); Update(); };
            M.LyricIndexChanged += OnLine;
            M.LyricsChanged += OnLine;
            M.Changed += OnLine;
            M.OffsetChanged += OnLine;
            _tick.Tick += (_, __) => CheckFullscreen();
            _tick.Start();
            Closed += (_, __) =>
            {
                M.LyricIndexChanged -= OnLine; M.LyricsChanged -= OnLine; M.Changed -= OnLine; M.OffsetChanged -= OnLine;
                _tick.Stop();
            };
        }

        void OnLine() => Dispatcher.BeginInvoke(new Action(Update));

        void Place()
        {
            var wa = SystemParameters.WorkArea;
            var p = (S.DeskLyricsPos ?? "").Split(',');
            if (p.Length == 2 && double.TryParse(p[0], out var x) && double.TryParse(p[1], out var y)
                && x > SystemParameters.VirtualScreenLeft - 200 && x < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 100
                && y > SystemParameters.VirtualScreenTop - 50 && y < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 40)
            { Left = x; Top = y; return; }
            Left = wa.Left + (wa.Width - Width) / 2;
            Top = wa.Bottom - Math.Max(ActualHeight, S.DeskLyricsSize * 2.6) - 70;
        }

        // ------------------------------------------------------------------
        //  Закрепление: окно пропускает мышь насквозь
        // ------------------------------------------------------------------

        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);

        void ApplyLock()
        {
            if (_hwnd == IntPtr.Zero) return;
            int ex = GetWindowLong(_hwnd, -20) | 0x80 /*TOOLWINDOW*/ | 0x08000000 /*NOACTIVATE*/;
            ex = S.DeskLyricsLock ? ex | 0x20 /*TRANSPARENT*/ : ex & ~0x20;
            SetWindowLong(_hwnd, -20, ex);
            Restyle();
        }

        // ------------------------------------------------------------------
        //  Вид
        // ------------------------------------------------------------------

        public static Color Highlight()
        {
            return S.LyricsColor switch
            {
                "accent" => (UI.Res("AccentBrush") as SolidColorBrush)?.Color ?? Colors.White,
                "art" => M.Art != null ? Lighten(M.ArtColor) : Colors.White,
                _ => Colors.White,
            };
        }

        static Color Lighten(Color c) => Color.FromRgb((byte)(c.R + (255 - c.R) * 0.35), (byte)(c.G + (255 - c.G) * 0.35), (byte)(c.B + (255 - c.B) * 0.35));

        public static FontFamily Font(string key) => key switch
        {
            "serif" => new FontFamily("Georgia, Cambria, Times New Roman"),
            "mono" => new FontFamily("Cascadia Mono, Consolas"),
            "script" => new FontFamily("Segoe Script, Segoe Print"),
            "light" => new FontFamily("Segoe UI Light, Segoe UI"),
            _ => new FontFamily("Segoe UI Variable Display, Segoe UI"),
        };

        void Restyle()
        {
            double size = Math.Clamp(S.DeskLyricsSize, 16, 72);
            var ff = Font(S.LyricsFont);
            _cur.FontSize = size;
            _next.FontSize = size * 0.6;
            _cur.FontFamily = _next.FontFamily = ff;
            // обводка-тень: текст читается на любом фоне
            _cur.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = size / 3.5, ShadowDepth = 0, Opacity = 0.95, RenderingBias = RenderingBias.Quality };
            _next.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = size / 5, ShadowDepth = 0, Opacity = 0.9 };
            _next.Foreground = new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF));
            _next.Visibility = S.DeskLyricsNext ? Visibility.Visible : Visibility.Collapsed;
            bool unlocked = !S.DeskLyricsLock;
            _tools.Visibility = unlocked ? Visibility.Visible : Visibility.Collapsed;
            _card.Background = unlocked ? UI.Hex("#99101014") : Brushes.Transparent;
            _card.BorderBrush = unlocked ? UI.Hex("#55FFFFFF") : Brushes.Transparent;
            _card.BorderThickness = new Thickness(unlocked ? 1 : 0);
            Cursor = unlocked ? Cursors.SizeAll : null;
            Opacity = Math.Clamp(S.DeskLyricsOpacity, 20, 100) / 100.0;
            _line = -2;
            Update();
        }

        void Update()
        {
            var lines = M.Lyrics;
            int idx = M.LyricIndex;
            string cur = null, next = null;
            if (M.HasSession && lines != null && M.LyricsSynced)
            {
                if (idx >= 0 && idx < lines.Count) cur = lines[idx].Text == "♪" ? "♪ ♪ ♪" : lines[idx].Text;
                for (int i = Math.Max(0, idx + 1); i < lines.Count; i++) if (lines[i].Text != "♪" && lines[i].Text.Trim() != "") { next = lines[i].Text; break; }
                if (idx < 0) cur = "♪ " + M.TrackLine;
            }
            else if (M.HasSession && !S.DeskLyricsLock) cur = M.LyricsLoading ? "Ищу текст песни…" : "♪ " + M.TrackLine;
            bool show = cur != null;
            _card.Visibility = show || !S.DeskLyricsLock ? Visibility.Visible : Visibility.Hidden;
            if (!show) { _cur.Text = I18n.T("Текст песни появится здесь"); _next.Text = ""; return; }
            if (_cur.Text != cur || idx != _line)
            {
                _cur.Text = cur;
                _next.Text = next ?? "";
                _cur.BeginAnimation(OpacityProperty, new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(220)));
                var tt = new TranslateTransform(0, 10);
                _cur.RenderTransform = tt;
                tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(320)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                _line = idx;
                _k1 = null;
            }
            // караоке: заливка строки цветом до конца строки
            var hi = Highlight();
            if (!S.LyricsKaraoke || idx < 0) { _cur.Foreground = new SolidColorBrush(hi); return; }
            if (_k1 == null)
            {
                _k1 = new GradientStop(hi, 0);
                _k2 = new GradientStop(Color.FromArgb(0xC0, 0xFF, 0xFF, 0xFF), 0);
                _cur.Foreground = new LinearGradientBrush(new GradientStopCollection { _k1, _k2 }, new Point(0, 0), new Point(1, 0));
            }
            var (p, left) = M.LineProgress();
            _k1.BeginAnimation(GradientStop.OffsetProperty, null);
            _k2.BeginAnimation(GradientStop.OffsetProperty, null);
            _k1.Offset = _k2.Offset = p;
            if (M.IsPlaying && left.TotalSeconds > 0.05)
            {
                var a = new DoubleAnimation(p, 1, left);
                _k1.BeginAnimation(GradientStop.OffsetProperty, a);
                _k2.BeginAnimation(GradientStop.OffsetProperty, a);
            }
        }

        // ------------------------------------------------------------------
        //  Не мешать играм и фильмам на весь экран
        // ------------------------------------------------------------------

        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, System.Text.StringBuilder sb, int n);
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }

        void CheckFullscreen()
        {
            if (!S.DeskLyricsHideFull) { if (Visibility != Visibility.Visible) Show(); return; }
            var fg = GetForegroundWindow();
            bool full = false;
            if (fg != IntPtr.Zero && fg != _hwnd && GetWindowRect(fg, out var r))
            {
                var sb = new System.Text.StringBuilder(64);
                GetClassName(fg, sb, 64);
                var cls = sb.ToString();
                if (cls is not ("Progman" or "WorkerW" or "Shell_TrayWnd"))
                {
                    var scr = System.Windows.Forms.Screen.FromHandle(fg).Bounds;
                    full = r.L <= scr.Left && r.T <= scr.Top && r.R >= scr.Right && r.B >= scr.Bottom;
                }
            }
            if (full && IsVisible) Hide();
            else if (!full && !IsVisible) Show();
        }
    }
}
