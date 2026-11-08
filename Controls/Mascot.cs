using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using NIsland.Services;

namespace NIsland.Controls
{
    /// <summary>
    /// Персонаж острова. Рисуется сам (без картинок), 30 кадров в секунду и только пока виден.
    /// Настроение задаёт остров: музыка — танцует в такт, ночь и «Не беспокоить» — спит, уведомление — удивляется,
    /// низкий заряд — грустит, тревога — пугается, ИИ отвечает — думает, звонок — слушает, курсор рядом — смотрит на него.
    /// </summary>
    public class Mascot : FrameworkElement
    {
        public static readonly (string Key, string Name)[] Skins =
        {
            ("blob", "Капля"), ("cat", "Котик"), ("ghost", "Привидение"), ("robot", "Робот"), ("bunny", "Зайчик"),
        };

        public static readonly (string Key, string Name)[] MoodNames =
        {
            ("idle", "спокоен"), ("happy", "рад"), ("dance", "танцует"), ("sleep", "спит"), ("surprised", "удивлён"), ("sad", "грустит"),
            ("love", "влюблён"), ("focus", "сосредоточен"), ("alert", "испуган"), ("listen", "слушает"), ("think", "думает"),
        };

        string _skin = "blob";
        public string Skin { get => _skin; set { _skin = value ?? "blob"; InvalidateVisual(); } }
        public Color Tint { get; set; } = Color.FromRgb(0x7A, 0xB8, 0xFF);
        /// <summary>Постоянное настроение (из состояния острова).</summary>
        public string Mood { get; set; } = "idle";
        /// <summary>Куда смотреть: −1…1 по горизонтали.</summary>
        public double LookX { get; set; }
        public double Level { get; set; }
        public double Bpm { get; set; }

        string _flash;
        DateTime _flashUntil;
        double _t, _blinkAt = 2, _lookCur, _jump;
        TimeSpan _last;
        bool _hooked;
        readonly Random _rnd = new();

        public string Current => _flash != null && DateTime.Now < _flashUntil ? _flash : Mood;

        /// <summary>Короткая эмоция поверх основной (удивление, любовь, радость).</summary>
        public void Flash(string mood, double seconds = 2.2, bool jump = true)
        {
            _flash = mood;
            _flashUntil = DateTime.Now.AddSeconds(seconds);
            if (jump) _jump = 1;
        }

        public Mascot()
        {
            IsHitTestVisible = false;
            Loaded += (_, __) => Sync();
            Unloaded += (_, __) => Sync(false);
            IsVisibleChanged += (_, __) => Sync();
        }

        void Sync(bool? force = null)
        {
            bool on = force ?? (IsLoaded && IsVisible);
            if (on && !_hooked) { _hooked = true; _last = TimeSpan.Zero; CompositionTarget.Rendering += OnFrame; }
            else if (!on && _hooked) { _hooked = false; CompositionTarget.Rendering -= OnFrame; }
        }

        void OnFrame(object sender, EventArgs e)
        {
            var now = ((RenderingEventArgs)e).RenderingTime;
            if (_last != TimeSpan.Zero && (now - _last).TotalSeconds < 1 / 30.0) return;
            double dt = _last == TimeSpan.Zero ? 1 / 30.0 : Math.Min(0.1, (now - _last).TotalSeconds);
            _last = now;
            _t += dt;
            _lookCur += (LookX - _lookCur) * Math.Min(1, dt * 8);
            _jump = Math.Max(0, _jump - dt * 2.4);
            InvalidateVisual();
        }

        static Brush B(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
        static Color A(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);

        protected override void OnRender(DrawingContext dc)
        {
            double W = ActualWidth, H = ActualHeight, s = Math.Min(W, H);
            if (s < 6) return;
            string mood = Current;
            double cx = W / 2, by = H * 0.62;

            // движение: танец в такт, подпрыжка, дыхание
            double bob = 0, squash = 1;
            if (mood == "dance")
            {
                double beat = (Bpm > 50 ? Bpm : 110) / 60.0;
                double ph = _t * beat * Math.PI * 2;
                bob = -Math.Abs(Math.Sin(ph / 2)) * s * (0.06 + Level * 0.12);
                squash = 1 + Math.Cos(ph) * 0.05 * (0.5 + Level);
            }
            else if (mood == "sleep") squash = 1 + Math.Sin(_t * 1.6) * 0.035;
            else if (mood == "alert") bob = Math.Sin(_t * 40) * s * 0.02;   // дрожит
            else squash = 1 + Math.Sin(_t * 2.2) * 0.02;
            if (_jump > 0) bob -= Math.Sin(_jump * Math.PI) * s * 0.18;

            var body = Tint;
            var dark = Color.FromRgb(0x14, 0x14, 0x1A);
            dc.PushTransform(new TranslateTransform(0, bob));
            dc.PushTransform(new ScaleTransform(1 / squash, squash, cx, by + s * 0.34));

            double bw = s * 0.86, bh = s * 0.74, top = by - bh / 2;
            switch (_skin)
            {
                case "cat":
                    Ears(dc, cx, top, bw, s, body, true);
                    dc.DrawEllipse(B(body), null, new Point(cx, by), bw / 2, bh / 2);
                    break;
                case "bunny":
                    var earL = new RectangleGeometry(new Rect(cx - bw * 0.3, top - s * 0.42, bw * 0.17, s * 0.55), bw * 0.08, bw * 0.08);
                    var earR = new RectangleGeometry(new Rect(cx + bw * 0.13, top - s * 0.42, bw * 0.17, s * 0.55), bw * 0.08, bw * 0.08);
                    dc.PushTransform(new RotateTransform(mood == "sad" ? -25 : -8 + Math.Sin(_t * 3) * 4, cx - bw * 0.2, top));
                    dc.DrawGeometry(B(body), null, earL);
                    dc.Pop();
                    dc.PushTransform(new RotateTransform(mood == "sad" ? 25 : 8 - Math.Sin(_t * 3) * 4, cx + bw * 0.2, top));
                    dc.DrawGeometry(B(body), null, earR);
                    dc.Pop();
                    dc.DrawEllipse(B(body), null, new Point(cx, by), bw / 2, bh / 2);
                    break;
                case "ghost":
                {
                    var g = new StreamGeometry();
                    using (var ctx = g.Open())
                    {
                        double l = cx - bw / 2, r = cx + bw / 2, bot = by + bh * 0.55;
                        ctx.BeginFigure(new Point(l, bot), true, true);
                        ctx.LineTo(new Point(l, by), true, false);
                        ctx.ArcTo(new Point(r, by), new Size(bw / 2, bh * 0.6), 0, false, SweepDirection.Clockwise, true, false);
                        ctx.LineTo(new Point(r, bot), true, false);
                        int waves = 3;
                        for (int i = 0; i < waves; i++)
                        {
                            double x0 = r - bw * i / waves, x1 = r - bw * (i + 1) / waves, wob = Math.Sin(_t * 4 + i) * s * 0.04;
                            ctx.QuadraticBezierTo(new Point((x0 + x1) / 2, bot - s * 0.1 + wob), new Point(x1, bot), true, false);
                        }
                    }
                    g.Freeze();
                    dc.DrawGeometry(B(A(body, 235)), null, g);
                    break;
                }
                case "robot":
                {
                    dc.DrawLine(new Pen(B(body), Math.Max(1, s * 0.05)), new Point(cx, top), new Point(cx, top - s * 0.16));
                    bool lamp = mood is "think" or "alert" ? (int)(_t * 4) % 2 == 0 : (int)(_t * 0.8) % 2 == 0;
                    dc.DrawEllipse(B(lamp ? Color.FromRgb(0xFF, 0x45, 0x3A) : A(body, 160)), null, new Point(cx, top - s * 0.18), s * 0.06, s * 0.06);
                    dc.DrawRoundedRectangle(B(body), null, new Rect(cx - bw / 2, top, bw, bh), s * 0.16, s * 0.16);
                    break;
                }
                default:
                {
                    // капля: мягкий «желейный» силуэт
                    double wob = Math.Sin(_t * 3.1) * s * 0.02;
                    dc.DrawEllipse(B(body), null, new Point(cx, by + wob * 0.5), bw / 2 + wob, bh / 2 - wob * 0.5);
                    dc.DrawEllipse(B(A(Colors.White, 60)), null, new Point(cx - bw * 0.22, top + bh * 0.22), bw * 0.1, bh * 0.07);
                    break;
                }
            }

            // глаза
            double ex = bw * 0.2, ey = by - bh * 0.06, er = s * 0.085;
            double look = _lookCur * s * 0.05;
            bool blink = false;
            if (_t > _blinkAt) { blink = true; if (_t > _blinkAt + 0.13) _blinkAt = _t + 2.5 + _rnd.NextDouble() * 4; }
            var eyePen = new Pen(B(dark), Math.Max(1, s * 0.055)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            for (int side = -1; side <= 1; side += 2)
            {
                double x = cx + side * ex + look, y = ey;
                switch (mood)
                {
                    case "sleep":
                        Arc(dc, eyePen, x, y, er, false);
                        break;
                    case "happy":
                    case "dance":
                        if (blink) dc.DrawLine(eyePen, new Point(x - er, y), new Point(x + er, y));
                        else Arc(dc, eyePen, x, y + er * 0.3, er, true);
                        break;
                    case "love":
                        Heart(dc, x, y, er * 1.5, Color.FromRgb(0xFF, 0x37, 0x5F));
                        break;
                    case "focus":
                        dc.DrawLine(eyePen, new Point(x - er, y), new Point(x + er, y));
                        break;
                    default:
                    {
                        double ry = blink ? er * 0.12 : mood is "surprised" or "alert" ? er * 1.35 : mood == "sad" ? er * 0.8 : er;
                        double rx = mood is "surprised" or "alert" ? er * 1.15 : er;
                        dc.DrawEllipse(B(dark), null, new Point(x, y), rx, ry);
                        if (!blink) dc.DrawEllipse(B(Colors.White), null, new Point(x - rx * 0.35 + look * 0.2, y - ry * 0.4), rx * 0.32, rx * 0.32);
                        if (mood == "sad") dc.DrawLine(eyePen, new Point(x - er * 1.1, y - er * (side < 0 ? 1.0 : 1.6)), new Point(x + er * 1.1, y - er * (side < 0 ? 1.6 : 1.0)));
                        break;
                    }
                }
            }

            // рот
            double my = by + bh * 0.2, mw = s * 0.12;
            var mouthPen = new Pen(B(dark), Math.Max(1, s * 0.045)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            switch (mood)
            {
                case "surprised":
                case "alert":
                    dc.DrawEllipse(B(dark), null, new Point(cx + look, my), mw * 0.45, mw * 0.6);
                    break;
                case "sad":
                    Arc(dc, mouthPen, cx + look, my + mw * 0.5, mw * 0.7, true);
                    break;
                case "focus":
                case "listen":
                    dc.DrawLine(mouthPen, new Point(cx - mw * 0.5 + look, my), new Point(cx + mw * 0.5 + look, my));
                    break;
                case "sleep":
                    dc.DrawEllipse(B(dark), null, new Point(cx + look, my), mw * 0.22, mw * 0.18);
                    break;
                case "dance":
                case "happy":
                case "love":
                    dc.DrawGeometry(B(dark), null, Smile(cx + look, my - mw * 0.2, mw * 0.8, mw * 0.65));
                    break;
                default:
                    if (_skin is "cat" or "bunny")
                    {
                        Arc(dc, mouthPen, cx - mw * 0.32 + look, my - mw * 0.1, mw * 0.32, false);
                        Arc(dc, mouthPen, cx + mw * 0.32 + look, my - mw * 0.1, mw * 0.32, false);
                    }
                    else Arc(dc, mouthPen, cx + look, my - mw * 0.25, mw * 0.55, false);
                    break;
            }
            // щёчки
            if (mood is "happy" or "dance" or "love")
                for (int side = -1; side <= 1; side += 2)
                    dc.DrawEllipse(B(Color.FromArgb(110, 0xFF, 0x6B, 0x8B)), null, new Point(cx + side * bw * 0.32 + look, my - s * 0.04), s * 0.07, s * 0.045);

            dc.Pop();
            dc.Pop();

            // украшения поверх
            switch (mood)
            {
                case "sleep":
                {
                    double p = _t * 0.5 % 1;
                    Text(dc, "z", cx + s * 0.38 + p * s * 0.2, top - p * s * 0.5 + bob, s * (0.22 + p * 0.12), (byte)(255 * (1 - p)));
                    break;
                }
                case "dance":
                {
                    double p = _t * 0.6 % 1;
                    Text(dc, "♪", cx + s * 0.42, top + s * 0.1 - p * s * 0.45, s * 0.3, (byte)(220 * (1 - p)));
                    break;
                }
                case "think":
                    for (int i = 0; i < 3; i++)
                    {
                        double a = 0.35 + 0.65 * Math.Max(0, Math.Sin(_t * 5 - i * 0.9));
                        dc.DrawEllipse(B(Color.FromArgb((byte)(255 * a), 255, 255, 255)), null, new Point(cx - s * 0.18 + i * s * 0.18, top - s * 0.12 + bob), s * 0.05, s * 0.05);
                    }
                    break;
                case "alert":
                    dc.DrawEllipse(B(Color.FromRgb(0x64, 0xD2, 0xFF)), null, new Point(cx + bw * 0.45, top + s * 0.08 + bob), s * 0.05, s * 0.075);
                    break;
                case "love":
                {
                    double p = _t * 0.7 % 1;
                    Heart(dc, cx + s * 0.42, top - p * s * 0.35 + bob, s * 0.12, Color.FromArgb((byte)(255 * (1 - p)), 0xFF, 0x37, 0x5F));
                    break;
                }
            }
        }

        void Ears(DrawingContext dc, double cx, double top, double bw, double s, Color body, bool inner)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                var g = new StreamGeometry();
                using (var ctx = g.Open())
                {
                    double bx = cx + side * bw * 0.28;
                    ctx.BeginFigure(new Point(bx - bw * 0.17, top + s * 0.08), true, true);
                    ctx.LineTo(new Point(bx + side * bw * 0.06, top - s * 0.2 + Math.Sin(_t * 2 + side) * s * 0.01), true, false);
                    ctx.LineTo(new Point(bx + bw * 0.17, top + s * 0.08), true, false);
                }
                g.Freeze();
                dc.DrawGeometry(B(body), null, g);
            }
        }

        /// <summary>Дуга между (x−r, y) и (x+r, y). up — горбом вверх (∩), иначе чашей вниз (∪). Ось Y в WPF смотрит вниз: по часовой — это вверх.</summary>
        static void Arc(DrawingContext dc, Pen pen, double x, double y, double r, bool up)
        {
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(x - r, y), false, false);
                ctx.ArcTo(new Point(x + r, y), new Size(r, r * 0.8), 0, false, up ? SweepDirection.Clockwise : SweepDirection.Counterclockwise, true, false);
            }
            g.Freeze();
            dc.DrawGeometry(null, pen, g);
        }

        static Geometry Smile(double x, double y, double w, double h)
        {
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(x - w / 2, y), true, true);
                ctx.ArcTo(new Point(x + w / 2, y), new Size(w / 2, h), 0, false, SweepDirection.Counterclockwise, true, false);
            }
            g.Freeze();
            return g;
        }

        static void Heart(DrawingContext dc, double x, double y, double r, Color c)
        {
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(x, y + r * 0.8), true, true);
                ctx.BezierTo(new Point(x - r * 1.2, y), new Point(x - r * 0.6, y - r * 0.9), new Point(x, y - r * 0.3), true, false);
                ctx.BezierTo(new Point(x + r * 0.6, y - r * 0.9), new Point(x + r * 1.2, y), new Point(x, y + r * 0.8), true, false);
            }
            g.Freeze();
            dc.DrawGeometry(B(c), null, g);
        }

        void Text(DrawingContext dc, string t, double x, double y, double size, byte alpha)
        {
            if (size < 3) return;
            var ft = new FormattedText(t, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI Semibold"), size,
                B(Color.FromArgb(alpha, 255, 255, 255)), VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(ft, new Point(x - ft.Width / 2, y - ft.Height / 2));
        }
    }
}
