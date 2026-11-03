using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shell;
using System.Windows.Threading;
using NIsland.Controls;
using NIsland.Core;
using NIsland.Pages;
using NIsland.Services;

namespace NIsland
{
    /// <summary>
    /// Визуализатор в отдельном окне — можно на весь экран (F11 или двойной клик). Стили — стрелками ← →, палитра — C,
    /// автосмена стилей — A, текст песни — L, название трека — T, пауза — пробел. Размер и стиль запоминаются.
    /// </summary>
    public class VisualizerWindow : Window
    {
        static VisualizerWindow _win;
        static Settings S => Store.Settings;
        static MediaService M => MediaService.I;

        public static void Toggle()
        {
            if (_win != null) { _win.Close(); return; }
            Open();
        }

        public static void Open()
        {
            if (_win == null) { _win = new VisualizerWindow(); _win.Closed += (_, __) => _win = null; _win.Show(); }
            if (_win.WindowState == WindowState.Minimized) _win.WindowState = WindowState.Normal;
            _win.Activate();
        }

        public static bool IsOpen => _win != null;

        readonly Visualizer _viz = new();
        readonly Border _info = new() { CornerRadius = new CornerRadius(14), Padding = new Thickness(10), Background = new SolidColorBrush(Color.FromArgb(0x70, 0, 0, 0)) };
        readonly Border _art = new() { Width = 54, Height = 54, CornerRadius = new CornerRadius(9) };
        readonly ImageBrush _artBrush = new() { Stretch = Stretch.UniformToFill };
        readonly TextBlock _title = new() { FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis, Tag = "no-translate" };
        readonly TextBlock _artist = new() { FontSize = 13.5, Foreground = new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF)), TextTrimming = TextTrimming.CharacterEllipsis, Tag = "no-translate" };
        readonly TextBlock _lyric = new()
        {
            FontSize = 30, FontWeight = FontWeights.Bold, Foreground = Brushes.White, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(40, 0, 40, 70), Tag = "no-translate",
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 0, Opacity = 0.9, Color = Colors.Black },
        };
        readonly Border _bar = new() { CornerRadius = new CornerRadius(16), Padding = new Thickness(8, 4, 8, 4), Background = new SolidColorBrush(Color.FromArgb(0xB0, 0x14, 0x14, 0x18)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 16) };
        readonly TextBlock _styleName = new() { FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, MinWidth = 150, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        readonly TextBlock _hint = new() { FontSize = 11.5, Foreground = new SolidColorBrush(Color.FromArgb(0x90, 0xFF, 0xFF, 0xFF)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 14, 0, 0) };
        readonly Button _play, _auto, _lyr, _pal;
        readonly DispatcherTimer _idle = new() { Interval = TimeSpan.FromSeconds(3) };
        readonly DispatcherTimer _cycle = new() { Interval = TimeSpan.FromSeconds(30) };
        Rect _restore;
        bool _full;

        VisualizerWindow()
        {
            Title = "N-Island — визуализатор";
            Background = Brushes.Black;
            Foreground = Brushes.White;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.CanResize;
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
            MinWidth = 360; MinHeight = 220;
            WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 0, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0), UseAeroCaptionButtons = false });
            try { Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/Assets/island.ico")); } catch { }
            RestoreBounds0();

            _viz.VisualStyle = S.VizWinStyle;
            _viz.Palette = S.VizWinPalette;
            _viz.Fps = 50;
            ApplyQuality();

            _art.Background = _artBrush;
            _info.Child = UI.Row(_art, UI.Col(_title, _artist).M(12, 0, 0, 0));
            ((StackPanel)((StackPanel)_info.Child).Children[1]).MaxWidth = 420;
            _info.HorizontalAlignment = HorizontalAlignment.Left;
            _info.VerticalAlignment = VerticalAlignment.Top;
            _info.Margin = new Thickness(18);

            Button B(string glyph, Action a, string tip, double size = 13) { var b = UI.IconBtn(glyph, a, size, 34, tip); b.Foreground = Brushes.White; return b; }
            _play = B("\uE768", () => M.TogglePlay(), "Пауза / воспроизведение (пробел)", 15);
            _auto = B("\uE895", () => { S.VizAuto = !S.VizAuto; SyncButtons(); Flash(S.VizAuto ? "Автосмена стилей каждые 30 с" : "Автосмена выключена"); }, "Автосмена стилей (A)");
            _lyr = B("\uE8D2", () => { S.VizLyrics = !S.VizLyrics; SyncButtons(); UpdateLyric(); }, "Текст песни (L)");
            _pal = B("\uE790", NextPalette, "Палитра (C)");
            _bar.Child = UI.Row(
                B("\uE892", () => M.Prev(), "Предыдущий трек"), _play, B("\uE893", () => M.Next(), "Следующий трек"),
                new Border { Width = 1, Height = 20, Background = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)), Margin = new Thickness(6, 0, 6, 0) },
                B("\uE76B", () => Step(-1), "Предыдущий стиль (←)"), _styleName, B("\uE76C", () => Step(1), "Следующий стиль (→)"),
                new Border { Width = 1, Height = 20, Background = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)), Margin = new Thickness(6, 0, 6, 0) },
                _pal, _auto, _lyr,
                B("\uE946", () => { S.VizTrack = !S.VizTrack; ShowInfo(); }, "Название трека (T)"),
                B("\uE7F4", CycleQuality, "Качество картинки (Q)"),
                B("\uE740", ToggleFull, "Во весь экран (F11)"),
                B("\uE711", Close, "Закрыть (Esc)"));

            var root = new Grid { Background = Brushes.Black };
            root.Children.Add(_viz);
            root.Children.Add(_lyric);
            root.Children.Add(_info);
            root.Children.Add(_hint);
            root.Children.Add(_bar);
            Content = root;

            MouseMove += (_, __) => Wake();
            MouseLeftButtonDown += (_, e) =>
            {
                if (e.ClickCount == 2) { ToggleFull(); return; }
                if (!_full && e.OriginalSource is not Button && FindButton(e.OriginalSource as DependencyObject) == null) { try { DragMove(); } catch { } }
            };
            MouseWheel += (_, e) => Step(e.Delta > 0 ? -1 : 1);
            KeyDown += OnKey;
            _idle.Tick += (_, __) => { _idle.Stop(); Hide(_bar); Hide(_hint); if (!_full) Mouse.OverrideCursor = null; else Cursor = Cursors.None; };
            _cycle.Tick += (_, __) => { if (S.VizAuto) Step(1, quiet: true); };
            _cycle.Start();

            M.Changed += OnTrack;
            M.PlaybackChanged += SyncButtons;
            M.LyricIndexChanged += UpdateLyric;
            M.LyricsChanged += UpdateLyric;
            Closed += (_, __) =>
            {
                M.Changed -= OnTrack;
                M.PlaybackChanged -= SyncButtons;
                M.LyricIndexChanged -= UpdateLyric;
                M.LyricsChanged -= UpdateLyric;
                _cycle.Stop(); _idle.Stop();
                SaveBounds();
                if (S.LowMemory) Dispatcher.BeginInvoke(new Action(() => MemoryTrim.Now(true)));
            };
            Loaded += (_, __) =>
            {
                OnTrack();
                SyncButtons();
                UpdateLyric();
                Wake();
                Flash("← → стиль · C палитра · A автосмена · L текст · F11 весь экран · Esc выход");
            };
        }

        static Button FindButton(DependencyObject d)
        {
            while (d != null) { if (d is Button b) return b; d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d); }
            return null;
        }

        void OnKey(object s, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Left: Step(-1); break;
                case Key.Right: Step(1); break;
                case Key.C: NextPalette(); break;
                case Key.A: _auto.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); break;
                case Key.L: S.VizLyrics = !S.VizLyrics; SyncButtons(); UpdateLyric(); break;
                case Key.T: S.VizTrack = !S.VizTrack; ShowInfo(); break;
                case Key.Q: CycleQuality(); break;
                case Key.F: case Key.F11: ToggleFull(); break;
                case Key.Space: M.TogglePlay(); break;
                case Key.N: case Key.MediaNextTrack: M.Next(); break;
                case Key.P: case Key.MediaPreviousTrack: M.Prev(); break;
                case Key.Escape: if (_full) ToggleFull(); else Close(); break;
                default: return;
            }
            e.Handled = true;
            Wake();
        }

        void Step(int dir, bool quiet = false)
        {
            _viz.VisualStyle = Visualizer.Next(_viz.VisualStyle, dir);
            S.VizWinStyle = _viz.VisualStyle;
            SyncButtons();
            if (!quiet) Flash(Visualizer.Styles.First(x => x.Key == _viz.VisualStyle).Desc);
            _cycle.Stop(); _cycle.Start();
        }

        void NextPalette()
        {
            int i = Array.FindIndex(Visualizer.Palettes, p => p.Key == _viz.Palette);
            var p = Visualizer.Palettes[(i + 1) % Visualizer.Palettes.Length];
            _viz.Palette = p.Key;
            S.VizWinPalette = p.Key;
            Flash(I18n.T("Палитра: ") + I18n.T(p.Name));
        }

        void ApplyQuality()
        {
            int q = Math.Clamp(S.VizQuality, 1, 3);
            _viz.MaxPixels = q switch { 1 => 360 * 200, 2 => 640 * 360, _ => 960 * 540 };
        }

        void CycleQuality()
        {
            S.VizQuality = S.VizQuality % 3 + 1;
            ApplyQuality();
            Flash(I18n.T("Качество: ") + I18n.T(S.VizQuality switch { 1 => "экономное", 2 => "обычное", _ => "высокое" }));
        }

        void SyncButtons()
        {
            _styleName.Text = I18n.T(Visualizer.NameOf(_viz.VisualStyle));
            _play.Content = M.IsPlaying ? "" : "";
            _auto.Foreground = S.VizAuto ? UI.Res("AccentBrush") : Brushes.White;
            _lyr.Foreground = S.VizLyrics ? UI.Res("AccentBrush") : Brushes.White;
        }

        void OnTrack()
        {
            _title.Text = string.IsNullOrEmpty(M.Title) ? I18n.T("Сейчас ничего не играет") : M.Title;
            _artist.Text = M.Artist;
            _artBrush.ImageSource = M.Art;
            _art.Show(M.Art != null);
            _viz.BaseColor = M.Art != null ? M.ArtColor : (UI.Res("AccentBrush") as SolidColorBrush)?.Color ?? Colors.DodgerBlue;
            ShowInfo();
        }

        void ShowInfo()
        {
            if (!S.VizTrack) { Hide(_info); return; }
            Show(_info);
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
            t.Tick += (_, __) => { t.Stop(); if (!_bar.IsMouseOver && _bar.Opacity < 0.5) Hide(_info); };
            t.Start();
        }

        void UpdateLyric()
        {
            var line = S.VizLyrics ? M.CurrentLyric : null;
            if (_lyric.Text == (line ?? "")) return;
            _lyric.Text = line ?? "";
            _lyric.FontSize = Math.Clamp(ActualWidth / 28, 18, 40);
            _lyric.BeginAnimation(OpacityProperty, new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(260)));
        }

        void Wake()
        {
            Cursor = null;
            Show(_bar);
            if (S.VizTrack) Show(_info);
            _idle.Stop();
            _idle.Start();
        }

        void Flash(string text)
        {
            _hint.Text = I18n.T(text);
            Show(_hint);
            _idle.Stop(); _idle.Start();
        }

        static void Show(UIElement e) => e.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)));
        static void Hide(UIElement e) => e.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(500)));

        void ToggleFull()
        {
            if (!_full)
            {
                _restore = new Rect(Left, Top, Width, Height);
                var scr = System.Windows.Forms.Screen.FromHandle(new System.Windows.Interop.WindowInteropHelper(this).Handle).Bounds;
                double k = VisualTreeHelper.GetDpi(this).DpiScaleX;
                Left = scr.Left / k; Top = scr.Top / k; Width = scr.Width / k; Height = scr.Height / k;
                Topmost = true;
                _full = true;
            }
            else
            {
                Topmost = false;
                Left = _restore.Left; Top = _restore.Top; Width = _restore.Width; Height = _restore.Height;
                _full = false;
                Cursor = null;
            }
            UpdateLyric();
        }

        void RestoreBounds0()
        {
            var p = (S.VizBounds ?? "").Split(',');
            if (p.Length == 4 && p.All(x => double.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out _)))
            {
                var v = p.Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray();
                var r = new Rect(v[0], v[1], Math.Max(360, v[2]), Math.Max(220, v[3]));
                // окно должно попасть хотя бы на один экран
                if (System.Windows.Forms.Screen.AllScreens.Any(s => new Rect(s.WorkingArea.Left, s.WorkingArea.Top, s.WorkingArea.Width, s.WorkingArea.Height).IntersectsWith(r)))
                {
                    WindowStartupLocation = WindowStartupLocation.Manual;
                    Left = r.Left; Top = r.Top; Width = r.Width; Height = r.Height;
                    return;
                }
            }
            Width = 900; Height = 520;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        void SaveBounds()
        {
            var r = _full ? _restore : new Rect(Left, Top, Width, Height);
            S.VizBounds = string.Join(",", new[] { r.Left, r.Top, r.Width, r.Height }.Select(x => Math.Round(x).ToString(CultureInfo.InvariantCulture)));
        }
    }
}
