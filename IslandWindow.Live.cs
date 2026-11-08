using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using NIsland.Controls;
using NIsland.Core;
using NIsland.Pages;
using NIsland.Services;

namespace NIsland
{
    // 1.8: «живой» остров — персонаж, жидкое стекло с преломлением, блик за курсором, ободок в такт, отблеск на уведомлениях
    public partial class IslandWindow
    {
        Mascot _mascot;
        Image _lens;
        Border _glint, _shine;
        RadialGradientBrush _lensMask;
        bool _liveHooked;
        TimeSpan _liveLast;
        double _mouseX = -1, _glintX, _liveT, _rimLevel;

        bool MascotOn => S.Mascot != "none" && S.Mode != "minimal" && !Modes.Hidden;

        void InitLive()
        {
            // персонаж — отдельной колонкой слева в свёрнутой чёлке
            CollapsedView.ColumnDefinitions.Insert(0, new ColumnDefinition { Width = GridLength.Auto });
            foreach (UIElement ch in CollapsedView.Children) Grid.SetColumn(ch, Grid.GetColumn(ch) + 1);
            Grid.SetColumn(ColProgress, 0);
            Grid.SetColumnSpan(ColProgress, 4);
            _mascot = new Mascot { Width = 22, Height = 22, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            CollapsedView.Children.Add(_mascot);

            // линза: тот же снимок под стеклом, чуть увеличенный и видимый только у краёв — так стекло «преломляет» фон
            _lensMask = new RadialGradientBrush { MappingMode = BrushMappingMode.Absolute };
            _lensMask.GradientStops.Add(new GradientStop(Colors.Transparent, 0));
            _lensMask.GradientStops.Add(new GradientStop(Colors.Transparent, 0.62));
            _lensMask.GradientStops.Add(new GradientStop(Color.FromArgb(0xF0, 0, 0, 0), 1));
            _lens = new Image
            {
                Stretch = Stretch.Fill, IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                Visibility = Visibility.Collapsed, OpacityMask = _lensMask, Effect = new BlurEffect { Radius = 6, RenderingBias = RenderingBias.Performance },
            };
            RenderOptions.SetBitmapScalingMode(_lens, BitmapScalingMode.Linear);
            Stage.Children.Insert(0, _lens);

            // блик, который следует за курсором
            var gb = new RadialGradientBrush();
            gb.GradientStops.Add(new GradientStop(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF), 0));
            gb.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 1));
            _glint = new Border { IsHitTestVisible = false, Width = 160, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Background = gb, RenderTransform = new TranslateTransform(), Opacity = 0 };
            Stage.Children.Insert(Math.Min(Stage.Children.Count, 3), _glint);

            // отблеск, пробегающий по острову при новом событии
            var sb = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            sb.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0));
            sb.GradientStops.Add(new GradientStop(Color.FromArgb(0x55, 255, 255, 255), 0.5));
            sb.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1));
            _shine = new Border
            {
                IsHitTestVisible = false, Width = 90, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Stretch, Background = sb,
                RenderTransform = new TransformGroup { Children = { new SkewTransform(-20, 0), new TranslateTransform(-200, 0) } }, Visibility = Visibility.Collapsed,
            };
            Stage.Children.Add(_shine);

            Island.MouseMove += (_, e) => _mouseX = e.GetPosition(Island).X;
            Island.MouseLeave += (_, __) => _mouseX = -1;
            Island.MouseEnter += (_, __) => { SyncLive(); if (MascotOn && S.MascotMoods) _mascot.Flash("happy", 1.2, jump: false); };
            MediaService.I.Changed += () => Dispatcher.BeginInvoke(new Action(() => { SyncLive(); UpdateMascot(); }));
            Ai.BusyChanged += () => Dispatcher.BeginInvoke(new Action(UpdateMascot));
            UpdateMascot();
        }

        // ------------------------------------------------------------------
        //  Персонаж
        // ------------------------------------------------------------------

        [StructLayout(LayoutKind.Sequential)] struct LASTINPUTINFO { public uint cbSize, dwTime; }
        [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO p);

        static double IdleMinutes()
        {
            var li = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
            return GetLastInputInfo(ref li) ? unchecked((uint)Environment.TickCount - li.dwTime) / 60000.0 : 0;
        }

        void UpdateMascot()
        {
            if (_mascot == null) return;
            bool on = MascotOn;
            _mascot.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            if (!on) return;
            var m = MediaService.I;
            var sys = SystemService.I;
            _mascot.Skin = S.Mascot;
            var accent = (UI.Res("AccentBrush") as SolidColorBrush)?.Color ?? Colors.DodgerBlue;
            _mascot.Tint = S.MascotColor switch
            {
                "accent" => accent,
                "art" => m.HasSession && m.Art != null ? Soft(m.ArtColor) : accent,
                _ => ColorUtil.Parse(S.MascotColor, accent),
            };
            if (!S.MascotMoods) { _mascot.Mood = "idle"; return; }
            int hour = DateTime.Now.Hour;
            _mascot.Mood =
                CallService.I.InCall ? "listen"
                : Ai.Busy > 0 ? "think"
                : S.Mode is "dnd" or "night" || (hour < 6 && !m.IsPlaying) || IdleMinutes() > 10 ? "sleep"
                : sys.HasBattery && sys.BatteryPercent <= 15 && !sys.Charging ? "sad"
                : m.HasSession && m.IsPlaying ? "dance"
                : (S.Mode == "focus" && PomodoroService.I.Active) || TimerService.I.Running ? "focus"
                : _hover ? "happy" : "idle";
        }

        /// <summary>Цвет обложки бывает слишком тёмным — персонаж должен быть виден на тёмной чёлке.</summary>
        static Color Soft(Color c)
        {
            double l = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255;
            double k = l < 0.45 ? 0.45 - l + 0.15 : 0.05;
            return Color.FromRgb((byte)(c.R + (255 - c.R) * k), (byte)(c.G + (255 - c.G) * k), (byte)(c.B + (255 - c.B) * k));
        }

        /// <summary>Событие острова → короткая эмоция и отблеск.</summary>
        void OnLiveActivity(Activity a)
        {
            if (a == null) return;
            if (_mascot != null && MascotOn && S.MascotMoods)
            {
                var mood = a.Kind switch
                {
                    "lowbat" => "sad",
                    "battery" => "happy",
                    "alert" => "alert",
                    "track" or "media" => "happy",
                    "incall" or "callstart" => "listen",
                    "timer" or "pomodoro" => "surprised",
                    _ => "surprised",
                };
                _mascot.Flash(mood);
            }
            Shimmer();
        }

        /// <summary>Сердечки, когда трек добавили в избранное.</summary>
        public void MascotLove() { if (_mascot != null && MascotOn) _mascot.Flash("love", 2.5); }

        // ------------------------------------------------------------------
        //  Отблеск
        // ------------------------------------------------------------------

        void Shimmer()
        {
            if (!S.IslandShimmer || _shine == null || S.LiteMode) return;
            var tr = (TranslateTransform)((TransformGroup)_shine.RenderTransform).Children[1];
            _shine.Visibility = Visibility.Visible;
            var a = new DoubleAnimation(-120, Math.Max(_tw, 300) + 60, TimeSpan.FromMilliseconds(900)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }, BeginTime = TimeSpan.FromMilliseconds(120) };
            a.Completed += (_, __) => _shine.Visibility = Visibility.Collapsed;
            tr.BeginAnimation(TranslateTransform.XProperty, a);
        }

        // ------------------------------------------------------------------
        //  Кадры: блик за курсором и ободок в такт
        // ------------------------------------------------------------------

        void SyncLive()
        {
            bool want = !S.LiteMode && (S.IslandGlint || S.BeatRim) && (_hover || _mode != Mode.Collapsed || (S.BeatRim && MediaService.I.IsPlaying));
            if (want && !_liveHooked) { _liveHooked = true; _liveLast = TimeSpan.Zero; CompositionTarget.Rendering += OnLiveFrame; }
            else if (!want && _liveHooked)
            {
                _liveHooked = false;
                CompositionTarget.Rendering -= OnLiveFrame;
                Rim.Opacity = 1;
                _glint.Opacity = 0;
            }
        }

        void OnLiveFrame(object sender, EventArgs e)
        {
            var now = ((RenderingEventArgs)e).RenderingTime;
            if (_liveLast != TimeSpan.Zero && (now - _liveLast).TotalSeconds < 1 / 40.0) return;
            double dt = _liveLast == TimeSpan.Zero ? 1 / 40.0 : Math.Min(0.1, (now - _liveLast).TotalSeconds);
            _liveLast = now;
            _liveT += dt;
            double w = Island.ActualWidth, h = Island.ActualHeight;

            if (S.IslandGlint)
            {
                double target = _mouseX >= 0 ? _mouseX : w / 2 + Math.Sin(_liveT * 0.35) * w * 0.35;
                _glintX += (target - _glintX) * Math.Min(1, dt * 7);
                _glint.Width = Math.Max(90, h * 2.4);
                _glint.Height = h * 1.3;
                ((TranslateTransform)_glint.RenderTransform).X = _glintX - _glint.Width / 2;
                ((TranslateTransform)_glint.RenderTransform).Y = -h * 0.45;
                double op = _mouseX >= 0 ? 0.9 : _mode == Mode.Expanded ? 0.35 : 0.5;
                _glint.Opacity += (op - _glint.Opacity) * Math.Min(1, dt * 5);
            }
            else _glint.Opacity = 0;

            if (S.BeatRim && MediaService.I.IsPlaying && AudioCapture.I.Running)
            {
                double lv = Math.Min(1, AudioCapture.I.Level * 1.4);
                _rimLevel = lv > _rimLevel ? lv : _rimLevel + (lv - _rimLevel) * Math.Min(1, dt * 4);
                Rim.Opacity = 0.55 + _rimLevel * 0.9;
                if (_mascot != null) { _mascot.Level = _rimLevel; _mascot.Bpm = AudioCapture.I.Bpm; }
            }
            else Rim.Opacity = 1;

            if (_mascot != null && _mascot.IsVisible)
                _mascot.LookX = _mouseX >= 0 && w > 0 ? Math.Clamp(_mouseX / w * 2 - 1, -1, 1) : Math.Sin(_liveT * 0.5) * 0.4;

            // музыка кончилась, курсор ушёл — перестаём рисовать
            if (!_hover && _mode == Mode.Collapsed && !(S.BeatRim && MediaService.I.IsPlaying)) SyncLive();
        }

        // ------------------------------------------------------------------
        //  Жидкое стекло
        // ------------------------------------------------------------------

        /// <summary>Линза по краям: подогнать под текущий размер острова (вызывается из Apply на каждом кадре анимации).</summary>
        void ApplyLens(double w, double h)
        {
            if (_lens == null) return;
            bool on = _blur && S.LiquidGlass && !S.LiteMode && GlassImg.Source != null && S.LiquidRefract > 0;
            _lens.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            if (!on) return;
            _lens.Source = GlassImg.Source;
            _lens.Width = GlassImg.Width;
            _lens.Height = GlassImg.Height;
            double islandLeft = (Root.ActualWidth - w) / 2, islandTop = Island.Margin.Top;
            double lx = GlassImg.Margin.Left - islandLeft, ly = -islandTop;
            _lens.Margin = new Thickness(lx, ly, 0, 0);
            double k = 1 + Math.Clamp(S.LiquidRefract, 0, 100) / 100.0 * 0.14;
            double cx = w / 2 - lx, cy = h / 2 - ly;
            _lens.RenderTransform = new ScaleTransform(k, k, cx, cy);
            _lensMask.Center = _lensMask.GradientOrigin = new Point(cx, cy);
            _lensMask.RadiusX = w / 2;
            _lensMask.RadiusY = Math.Max(h / 2, 8);
            if (_lens.Effect is BlurEffect be) be.Radius = Math.Clamp(S.BlurRadius * 0.45, 2, 30);
            _lens.Opacity = Math.Clamp(1.05 - _bgA * 0.7, 0.25, 1);
        }

        /// <summary>Яркость и насыщенность фона под стеклом — как «вибрантность» в Apple.</summary>
        static BitmapSource Vibrant(BitmapSource src, double sat, double lift)
        {
            try
            {
                var conv = new FormatConvertedBitmap(src, PixelFormats.Bgr32, null, 0);
                int w = conv.PixelWidth, h = conv.PixelHeight;
                var px = new int[w * h];
                conv.CopyPixels(px, w * 4, 0);
                for (int i = 0; i < px.Length; i++)
                {
                    int p = px[i];
                    double r = (p >> 16) & 255, g = (p >> 8) & 255, b = p & 255;
                    double l = 0.299 * r + 0.587 * g + 0.114 * b;
                    r = l + (r - l) * sat + lift; g = l + (g - l) * sat + lift; b = l + (b - l) * sat + lift;
                    px[i] = (int)(0xFF000000u | (uint)Math.Clamp(r, 0, 255) << 16 | (uint)Math.Clamp(g, 0, 255) << 8 | (uint)Math.Clamp(b, 0, 255));
                }
                var res = BitmapSource.Create(w, h, src.DpiX, src.DpiY, PixelFormats.Bgr32, null, px, w * 4);
                res.Freeze();
                return res;
            }
            catch { return src; }
        }
    }
}
