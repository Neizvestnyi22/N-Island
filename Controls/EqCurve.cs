using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using NIsland.Services;

namespace NIsland.Controls
{
    /// <summary>
    /// Кривая эквалайзера: 10 точек тянутся мышью вверх-вниз, колесо — по 0,5 дБ, двойной клик — в ноль.
    /// За кривой живой спектр того, что сейчас играет, — видно, что именно ты поднимаешь.
    /// </summary>
    public class EqCurve : FrameworkElement
    {
        double[] _gains = new double[Equalizer.Freqs.Length];
        int _drag = -1, _hover = -1;
        IDisposable _lease;
        readonly DispatcherTimer _anim = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(40) };
        readonly float[] _bands = new float[AudioCapture.BandCount];

        /// <summary>Значение меняется во время перетаскивания (можно сразу применять).</summary>
        public event Action<double[]> Changing;
        /// <summary>Отпустили мышь / покрутили колесо — окончательное значение.</summary>
        public event Action<double[]> Committed;

        public bool ShowSpectrum { get; set; } = true;
        public bool Enabled { get; set; } = true;
        public Color CurveColor { get; set; } = Color.FromRgb(0x0A, 0x84, 0xFF);

        public double[] Gains
        {
            get => (double[])_gains.Clone();
            set { _gains = value != null && value.Length == Equalizer.Freqs.Length ? (double[])value.Clone() : new double[Equalizer.Freqs.Length]; InvalidateVisual(); }
        }

        public EqCurve()
        {
            Cursor = Cursors.Hand;
            Focusable = false;
            ClipToBounds = true;
            _anim.Tick += (_, __) =>
            {
                AudioCapture.I.Read(_bands, null, null);
                InvalidateVisual();
            };
            IsVisibleChanged += (_, __) => Sync();
            Unloaded += (_, __) => { _anim.Stop(); _lease?.Dispose(); _lease = null; };
            Loaded += (_, __) => Sync();
        }

        void Sync()
        {
            bool on = IsVisible && IsLoaded && ShowSpectrum;
            if (on) { _lease ??= AudioCapture.I.Use(); _anim.Start(); }
            else { _anim.Stop(); _lease?.Dispose(); _lease = null; Array.Clear(_bands); }
        }

        const double PadX = 22, PadTop = 10, PadBottom = 20;
        double X(int i) => PadX + i * (ActualWidth - 2 * PadX) / (_gains.Length - 1);
        double Y(double g) => PadTop + (1 - (g + Equalizer.MaxGain) / (2 * Equalizer.MaxGain)) * (ActualHeight - PadTop - PadBottom);
        double G(double y) => Math.Clamp(Math.Round(((1 - (y - PadTop) / (ActualHeight - PadTop - PadBottom)) * 2 * Equalizer.MaxGain - Equalizer.MaxGain) * 2) / 2, -Equalizer.MaxGain, Equalizer.MaxGain);

        int Nearest(Point p)
        {
            int best = 0;
            double bd = double.MaxValue;
            for (int i = 0; i < _gains.Length; i++)
            {
                double d = Math.Abs(X(i) - p.X);
                if (d < bd) { bd = d; best = i; }
            }
            return bd < (ActualWidth - 2 * PadX) / (_gains.Length - 1) * 0.6 ? best : -1;
        }

        static string Hz(int f) => f >= 1000 ? (f / 1000) + "k" : f.ToString(CultureInfo.InvariantCulture);

        static readonly Typeface Face = new(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w < 40 || h < 40) return;
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));   // ловим мышь по всей площади
            double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            var grid = new Pen(new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)), 1);
            var zero = new Pen(new SolidColorBrush(Color.FromArgb(0x48, 0xFF, 0xFF, 0xFF)), 1);
            foreach (var g in new[] { -12.0, -6, 0, 6, 12 })
                dc.DrawLine(g == 0 ? zero : grid, new Point(PadX - 8, Y(g)), new Point(w - PadX + 8, Y(g)));
            var dim = new SolidColorBrush(Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF));
            for (int i = 0; i < _gains.Length; i++)
            {
                dc.DrawLine(grid, new Point(X(i), PadTop), new Point(X(i), h - PadBottom));
                var ft = new FormattedText(Hz(Equalizer.Freqs[i]), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, 9.5, dim, dpi);
                dc.DrawText(ft, new Point(X(i) - ft.Width / 2, h - PadBottom + 4));
            }

            // живой спектр за кривой
            if (ShowSpectrum && _lease != null)
            {
                var geo = new StreamGeometry();
                using (var ctx = geo.Open())
                {
                    double bottom = h - PadBottom;
                    ctx.BeginFigure(new Point(PadX, bottom), true, true);
                    int steps = 60;
                    for (int s = 0; s <= steps; s++)
                    {
                        double u = s / (double)steps;                                    // 31 Гц … 16 кГц по октавам
                        double f = 31 * Math.Pow(2, u * (_gains.Length - 1));
                        double b = Math.Log(f / 32) / Math.Log(16000.0 / 32) * AudioCapture.BandCount;
                        int bi = Math.Clamp((int)b, 0, AudioCapture.BandCount - 1);
                        double v = _bands[bi];
                        ctx.LineTo(new Point(PadX + u * (w - 2 * PadX), bottom - v * (bottom - PadTop) * 0.9), true, true);
                    }
                    ctx.LineTo(new Point(w - PadX, bottom), true, true);
                }
                geo.Freeze();
                var c = CurveColor;
                dc.DrawGeometry(new LinearGradientBrush(Color.FromArgb(0x50, c.R, c.G, c.B), Color.FromArgb(0x08, c.R, c.G, c.B), 90), null, geo);
            }

            // кривая (сглаженная) и заливка к нулю
            var col = Enabled ? CurveColor : Color.FromRgb(0x8E, 0x8E, 0x93);
            var curve = new StreamGeometry();
            var fill = new StreamGeometry();
            using (var c1 = curve.Open())
            using (var c2 = fill.Open())
            {
                c1.BeginFigure(new Point(PadX - 8, Y(_gains[0])), false, false);
                c2.BeginFigure(new Point(PadX - 8, Y(0)), true, true);
                c2.LineTo(new Point(PadX - 8, Y(_gains[0])), true, true);
                c1.LineTo(new Point(X(0), Y(_gains[0])), true, true);
                c2.LineTo(new Point(X(0), Y(_gains[0])), true, true);
                for (int i = 0; i < _gains.Length - 1; i++)
                {
                    var p0 = new Point(X(Math.Max(0, i - 1)), Y(_gains[Math.Max(0, i - 1)]));
                    var p1 = new Point(X(i), Y(_gains[i]));
                    var p2 = new Point(X(i + 1), Y(_gains[i + 1]));
                    var p3 = new Point(X(Math.Min(_gains.Length - 1, i + 2)), Y(_gains[Math.Min(_gains.Length - 1, i + 2)]));
                    // Catmull-Rom → Безье: кривая идёт ровно через точки
                    var b1 = new Point(p1.X + (p2.X - p0.X) / 6, p1.Y + (p2.Y - p0.Y) / 6);
                    var b2 = new Point(p2.X - (p3.X - p1.X) / 6, p2.Y - (p3.Y - p1.Y) / 6);
                    c1.BezierTo(b1, b2, p2, true, true);
                    c2.BezierTo(b1, b2, p2, true, true);
                }
                c1.LineTo(new Point(w - PadX + 8, Y(_gains[^1])), true, true);
                c2.LineTo(new Point(w - PadX + 8, Y(_gains[^1])), true, true);
                c2.LineTo(new Point(w - PadX + 8, Y(0)), true, true);
            }
            curve.Freeze();
            fill.Freeze();
            dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(0x30, col.R, col.G, col.B)), null, fill);
            dc.DrawGeometry(null, new Pen(new SolidColorBrush(col), 2.4) { LineJoin = PenLineJoin.Round }, curve);

            for (int i = 0; i < _gains.Length; i++)
            {
                bool hot = i == _drag || i == _hover;
                dc.DrawEllipse(Brushes.White, new Pen(new SolidColorBrush(col), 2), new Point(X(i), Y(_gains[i])), hot ? 6 : 4.5, hot ? 6 : 4.5);
                if (hot)
                {
                    var txt = (_gains[i] > 0 ? "+" : "") + _gains[i].ToString("0.#", CultureInfo.InvariantCulture) + " dB";
                    var ft = new FormattedText(txt, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, 10.5, Brushes.White, dpi);
                    double ty = Y(_gains[i]) - 22;
                    if (ty < 0) ty = Y(_gains[i]) + 9;
                    var r = new Rect(X(i) - ft.Width / 2 - 5, ty, ft.Width + 10, ft.Height + 2);
                    dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(0xD0, 0x20, 0x20, 0x24)), null, r, 5, 5);
                    dc.DrawText(ft, new Point(r.X + 5, r.Y + 1));
                }
            }
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            var p = e.GetPosition(this);
            int i = Nearest(p);
            if (i < 0) return;
            if (e.ClickCount == 2)
            {
                _gains[i] = 0;
                InvalidateVisual();
                Committed?.Invoke(Gains);
                e.Handled = true;
                return;
            }
            _drag = i;
            CaptureMouse();
            _gains[i] = G(p.Y);
            Changing?.Invoke(Gains);
            InvalidateVisual();
            e.Handled = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var p = e.GetPosition(this);
            if (_drag >= 0)
            {
                double g = G(p.Y);
                if (Math.Abs(g - _gains[_drag]) > 0.01) { _gains[_drag] = g; Changing?.Invoke(Gains); }
                InvalidateVisual();
                return;
            }
            int h = Nearest(p);
            if (h != _hover) { _hover = h; InvalidateVisual(); }
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            if (_drag < 0) return;
            _drag = -1;
            ReleaseMouseCapture();
            Committed?.Invoke(Gains);
            InvalidateVisual();
            e.Handled = true;
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            if (_drag < 0 && _hover >= 0) { _hover = -1; InvalidateVisual(); }
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);
            int i = Nearest(e.GetPosition(this));
            if (i < 0) return;
            _gains[i] = Math.Clamp(_gains[i] + (e.Delta > 0 ? 0.5 : -0.5), -Equalizer.MaxGain, Equalizer.MaxGain);
            _hover = i;
            InvalidateVisual();
            Committed?.Invoke(Gains);
            e.Handled = true;
        }
    }
}
