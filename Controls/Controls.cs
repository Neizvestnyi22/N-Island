using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace NIsland.Controls
{
    /// <summary>Кольцо прогресса (батарея, вода, помодоро, трек).</summary>
    public class StatusRing : FrameworkElement
    {
        public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(nameof(Progress), typeof(double), typeof(StatusRing),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty RingBrushProperty = DependencyProperty.Register(nameof(RingBrush), typeof(Brush), typeof(StatusRing),
            new FrameworkPropertyMetadata(Brushes.LimeGreen, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(nameof(TrackBrush), typeof(Brush), typeof(StatusRing),
            new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF)), FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(nameof(Thickness), typeof(double), typeof(StatusRing),
            new FrameworkPropertyMetadata(2.5, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(nameof(Glyph), typeof(string), typeof(StatusRing),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public double Progress { get => (double)GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }
        public Brush RingBrush { get => (Brush)GetValue(RingBrushProperty); set => SetValue(RingBrushProperty, value); }
        public Brush TrackBrush { get => (Brush)GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
        public double Thickness { get => (double)GetValue(ThicknessProperty); set => SetValue(ThicknessProperty, value); }
        public string Glyph { get => (string)GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }

        static readonly Typeface IconFace = new(new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

        protected override void OnRender(DrawingContext dc)
        {
            double size = Math.Min(ActualWidth, ActualHeight);
            if (size <= 0) return;
            double th = Thickness, r = size / 2 - th / 2;
            var c = new Point(ActualWidth / 2, ActualHeight / 2);
            dc.DrawEllipse(null, new Pen(TrackBrush, th), c, r, r);
            double p = Math.Clamp(Progress, 0, 1);
            var pen = new Pen(RingBrush, th) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            if (p >= 0.999) dc.DrawEllipse(null, pen, c, r, r);
            else if (p > 0.005)
            {
                double a = p * 2 * Math.PI;
                var start = new Point(c.X, c.Y - r);
                var end = new Point(c.X + r * Math.Sin(a), c.Y - r * Math.Cos(a));
                var g = new StreamGeometry();
                using (var ctx = g.Open())
                {
                    ctx.BeginFigure(start, false, false);
                    ctx.ArcTo(end, new Size(r, r), 0, p > 0.5, SweepDirection.Clockwise, true, false);
                }
                g.Freeze();
                dc.DrawGeometry(null, pen, g);
            }
            if (!string.IsNullOrEmpty(Glyph))
            {
                var ft = new FormattedText(Glyph, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, IconFace,
                    Math.Max(6, size * 0.42), RingBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
                dc.DrawText(ft, new Point(c.X - ft.Width / 2, c.Y - ft.Height / 2));
            }
        }
    }

    /// <summary>
    /// Полоски «эквалайзера» в свёрнутой чёлке. Пока играет музыка — пляшут по настоящему звуку (бас, середина, верха);
    /// если звук недоступен — мягко пляшут сами.
    /// </summary>
    public class EqBars : FrameworkElement
    {
        public static readonly DependencyProperty IsPlayingProperty = DependencyProperty.Register(nameof(IsPlaying), typeof(bool), typeof(EqBars),
            new FrameworkPropertyMetadata(false, (d, e) => ((EqBars)d).Update()));
        public static readonly DependencyProperty BarBrushProperty = DependencyProperty.Register(nameof(BarBrush), typeof(Brush), typeof(EqBars),
            new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

        public bool IsPlaying { get => (bool)GetValue(IsPlayingProperty); set => SetValue(IsPlayingProperty, value); }
        public Brush BarBrush { get => (Brush)GetValue(BarBrushProperty); set => SetValue(BarBrushProperty, value); }
        /// <summary>Слушать настоящий звук (иначе — просто анимация).</summary>
        public bool Live { get; set; } = true;

        const int N = 4;
        readonly double[] _h = { 0.3, 0.5, 0.4, 0.6 };
        readonly double[] _t = new double[N];
        readonly double[] _speed = { 0.35, 0.5, 0.42, 0.3 };
        static readonly (double From, double To)[] Ranges = { (40, 180), (180, 900), (900, 4000), (4000, 14000) };
        readonly Random _rnd = new();
        readonly DispatcherTimer _timer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(40) };
        IDisposable _lease;
        int _lastFrame, _stale;

        public EqBars()
        {
            _timer.Tick += (_, __) => Step();
            IsVisibleChanged += (_, __) => Update();
        }

        void Update()
        {
            if (IsVisible) _timer.Start(); else _timer.Stop();
            bool listen = IsVisible && IsPlaying && Live;
            if (listen && _lease == null) _lease = NIsland.Services.AudioCapture.I.Use();
            else if (!listen && _lease != null) { _lease.Dispose(); _lease = null; }
        }

        void Step()
        {
            bool settled = true;
            var cap = NIsland.Services.AudioCapture.I;
            int frame = cap.Frame;
            _stale = frame != _lastFrame ? 0 : _stale + 1;
            _lastFrame = frame;
            // звук есть и анализ идёт — полоски повторяют его; тишина при «играет» (видео без звука, ошибка) — своя анимация
            bool real = _lease != null && cap.Running && _stale < 5 && cap.Level > 0.02;
            for (int i = 0; i < N; i++)
            {
                if (IsPlaying && real) _t[i] = Math.Clamp(0.15 + cap.Range(Ranges[i].From, Ranges[i].To) * 1.05, 0.15, 1);
                else if (IsPlaying)
                {
                    if (Math.Abs(_h[i] - _t[i]) < 0.06) _t[i] = 0.2 + _rnd.NextDouble() * 0.8;
                }
                else _t[i] = 0.18;
                _h[i] += (_t[i] - _h[i]) * (real ? 0.6 : _speed[i]);
                if (Math.Abs(_h[i] - _t[i]) > 0.01) settled = false;
            }
            InvalidateVisual();
            if (!IsPlaying && settled) _timer.Stop();
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w <= 0 || h <= 0) return;
            double gap = 2, bw = (w - gap * (N - 1)) / N;
            for (int i = 0; i < N; i++)
            {
                double bh = Math.Max(bw, _h[i] * h);
                var rect = new Rect(i * (bw + gap), (h - bh) / 2, bw, bh);
                dc.DrawRoundedRectangle(BarBrush, null, rect, bw / 2, bw / 2);
            }
        }
    }
}
