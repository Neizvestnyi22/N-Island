using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using NIsland.Services;

namespace NIsland.Controls
{
    /// <summary>
    /// Персонаж острова. Рисуется сам (без картинок) и только пока виден. Частота — по настроению:
    /// спит 6 кадров/с, спокоен 10, танцует и эмоции 30. Без CompositionTarget.Rendering: подписка на него
    /// заставляет WPF перерисовывать всё окно острова на частоте монитора — это и были подтормаживания 1.8.0.
    /// Настроение задаёт остров (музыка, звонки, батарея, нагрузка, погода, время суток, уведомления…),
    /// а в покое персонаж сам подмигивает, машет, зевает и оглядывается.
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
            ("wink", "подмигивает"), ("wave", "машет"), ("yawn", "зевает"), ("hot", "жарко"), ("cold", "мёрзнет"), ("cry", "плачет"),
            ("party", "празднует"), ("dizzy", "кружится голова"), ("cool", "крутой"), ("charging", "заряжается"), ("angry", "сердится"),
            ("curious", "любопытно"), ("spin", "кружится"),
        };

        /// <summary>Аксессуары: «авто» — наушники под музыку, колпак Деда Мороза зимой.</summary>
        public static readonly (string Key, string Name)[] Hats =
        {
            ("auto", "Авто"), ("none", "Нет"), ("cap", "Кепка"), ("crown", "Корона"), ("bow", "Бантик"), ("party", "Колпак"),
            ("headphones", "Наушники"), ("glasses", "Очки"), ("santa", "Шапка Деда Мороза"), ("flower", "Цветок"),
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
        /// <summary>Аксессуар (ключ из Hats).</summary>
        public string Hat { get; set; } = "auto";
        /// <summary>Скорость анимаций: 1 — обычная.</summary>
        public double Speed { get; set; } = 1;
        /// <summary>В покое сам подмигивает, машет, зевает, оглядывается.</summary>
        public bool IdleAnims { get; set; } = true;

        string _flash;
        DateTime _flashUntil;
        double _t, _blinkAt = 2, _lookCur, _jump, _spin, _nextIdle = 8, _idleLook;
        IDisposable _clock;
        readonly Random _rnd = new();
        int _pokes;
        DateTime _lastPoke;

        public string Current => _flash != null && DateTime.Now < _flashUntil ? _flash : Mood;

        /// <summary>Короткая эмоция поверх основной (удивление, любовь, радость).</summary>
        public void Flash(string mood, double seconds = 2.2, bool jump = true)
        {
            _flash = mood;
            _flashUntil = DateTime.Now.AddSeconds(seconds);
            if (jump) _jump = 1;
            if (mood == "spin") _spin = 1;
            Retime();
        }

        /// <summary>Персонажа «потыкали» курсором: каждый раз своя реакция, а если тыкать часто — у него кружится голова.</summary>
        public void Poke()
        {
            _pokes = (DateTime.Now - _lastPoke).TotalSeconds < 1.5 ? _pokes + 1 : 1;
            _lastPoke = DateTime.Now;
            if (_pokes >= 4) { Flash("dizzy", 2.5); _spin = 1; _pokes = 0; return; }
            var pick = new[] { "wink", "love", "spin", "party", "happy", "surprised", "wave" }[_rnd.Next(7)];
            Flash(pick, pick is "spin" ? 1.1 : 1.8);
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
            if (on && _clock == null) { _clock = NIsland.Core.FrameClock.Add(Step, 12); Retime(); }
            else if (!on && _clock != null) { _clock.Dispose(); _clock = null; }
        }

        /// <summary>Частота кадров по тому, что сейчас происходит.</summary>
        void Retime()
        {
            double fps = _jump > 0 || _spin > 0 ? 30 : Current switch
            {
                "dance" or "surprised" or "alert" or "love" or "happy" or "wave" or "party" or "dizzy" or "cry" or "angry" or "cold" or "spin" => 30,
                "sleep" => 6,
                "yawn" or "hot" or "charging" or "wink" or "curious" => 15,
                _ => 10,
            };
            if (_clock != null) NIsland.Core.FrameClock.SetFps(_clock, fps);
        }

        void Step(double dt)
        {
            NIsland.Core.Perf.Hit("mascot-draw");
            dt *= Math.Clamp(Speed, 0.3, 3);
            _t += dt;
            // в покое — оглядывается и иногда что-нибудь делает сам
            if (Current == "idle")
            {
                _idleLook = Math.Sin(_t * 0.37) * 0.6 + Math.Sin(_t * 0.11) * 0.3;
                if (IdleAnims && _t > _nextIdle)
                {
                    _nextIdle = _t + 9 + _rnd.NextDouble() * 14;
                    int h = DateTime.Now.Hour;
                    var pick = h >= 22 || h < 7 ? new[] { "yawn", "yawn", "wink", "curious" } : new[] { "wink", "wave", "curious", "happy", "yawn" };
                    Flash(pick[_rnd.Next(pick.Length)], 1.8, jump: false);
                }
            }
            else _idleLook = 0;
            double target = Math.Abs(LookX) > 0.05 ? LookX : _idleLook;
            _lookCur += (target - _lookCur) * Math.Min(1, dt * 8);
            _jump = Math.Max(0, _jump - dt * 2.4);
            _spin = Math.Max(0, _spin - dt * 1.6);
            InvalidateVisual();
            Retime();
        }

        static Brush B(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
        static Color A(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);
        static Color Mix(Color a, Color b, double t) => Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

        protected override void OnRender(DrawingContext dc)
        {
            double W = ActualWidth, H = ActualHeight, s = Math.Min(W, H);
            if (s < 6) return;
            string mood = Current;
            double cx = W / 2, by = H * 0.62;

            // движение: танец в такт, подпрыжка, дыхание, дрожь
            double bob = 0, squash = 1, tilt = 0;
            switch (mood)
            {
                case "dance":
                {
                    double beat = (Bpm > 50 ? Bpm : 110) / 60.0;
                    double ph = _t * beat * Math.PI * 2;
                    bob = -Math.Abs(Math.Sin(ph / 2)) * s * (0.06 + Level * 0.12);
                    squash = 1 + Math.Cos(ph) * 0.05 * (0.5 + Level);
                    tilt = Math.Sin(ph / 2) * 8;   // покачивается из стороны в сторону
                    break;
                }
                case "party": bob = -Math.Abs(Math.Sin(_t * 9)) * s * 0.12; tilt = Math.Sin(_t * 9) * 10; break;
                case "sleep": squash = 1 + Math.Sin(_t * 1.6) * 0.035; tilt = 6; break;
                case "alert": case "cold": bob = Math.Sin(_t * 40) * s * 0.02; break;   // дрожит
                case "angry": tilt = Math.Sin(_t * 30) * 3; break;
                case "yawn": squash = 1 + Math.Max(0, Math.Sin(_t * 2.4)) * 0.08; break;
                case "dizzy": tilt = Math.Sin(_t * 6) * 14; break;
                case "wave": tilt = -6; break;
                case "curious": tilt = 12; break;
                case "hot": squash = 1 + Math.Sin(_t * 8) * 0.03; break;
                default: squash = 1 + Math.Sin(_t * 2.2) * 0.02; break;
            }
            if (_jump > 0) bob -= Math.Sin(_jump * Math.PI) * s * 0.18;
            if (_spin > 0) tilt += (1 - _spin) * 360;

            // цвет: жарко — краснеет, холодно — синеет, сердится — краснеет сильнее
            var body = mood switch
            {
                "hot" => Mix(Tint, Color.FromRgb(0xFF, 0x5A, 0x3C), 0.45),
                "angry" => Mix(Tint, Color.FromRgb(0xFF, 0x30, 0x30), 0.55),
                "cold" => Mix(Tint, Color.FromRgb(0x9F, 0xD8, 0xFF), 0.55),
                _ => Tint,
            };
            var dark = Color.FromRgb(0x14, 0x14, 0x1A);
            dc.PushTransform(new TranslateTransform(0, bob));
            dc.PushTransform(new RotateTransform(tilt, cx, by));
            dc.PushTransform(new ScaleTransform(1 / squash, squash, cx, by + s * 0.34));

            double bw = s * 0.86, bh = s * 0.74, top = by - bh / 2;
            DrawBody(dc, mood, cx, by, bw, bh, top, s, body);

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
                    case "happy": case "dance": case "party": case "wave": case "charging":
                        if (blink) dc.DrawLine(eyePen, new Point(x - er, y), new Point(x + er, y));
                        else Arc(dc, eyePen, x, y + er * 0.3, er, true);
                        break;
                    case "wink":
                        if (side > 0) Arc(dc, eyePen, x, y + er * 0.3, er, true);
                        else Eye(dc, x, y, er, er, look, dark, false);
                        break;
                    case "love":
                        Heart(dc, x, y, er * 1.5, Color.FromRgb(0xFF, 0x37, 0x5F));
                        break;
                    case "focus": case "yawn":
                        dc.DrawLine(eyePen, new Point(x - er, y), new Point(x + er, y));
                        break;
                    case "dizzy":
                        Spiral(dc, x, y, er * 1.2, eyePen, _t * 8 * side);
                        break;
                    case "cry":
                        Arc(dc, eyePen, x, y, er, true);
                        break;
                    case "angry":
                        Eye(dc, x, y, er * 0.9, er * 0.7, look, dark, false);
                        dc.DrawLine(eyePen, new Point(x - er * 1.2, y - er * (side < 0 ? 1.8 : 1.1)), new Point(x + er * 1.2, y - er * (side < 0 ? 1.1 : 1.8)));
                        break;
                    default:
                    {
                        double ry = blink ? er * 0.12 : mood is "surprised" or "alert" ? er * 1.35 : mood is "sad" or "hot" ? er * 0.8 : er;
                        double rx = mood is "surprised" or "alert" ? er * 1.15 : er;
                        Eye(dc, x, y, rx, ry, look, dark, blink);
                        if (mood == "sad") dc.DrawLine(eyePen, new Point(x - er * 1.1, y - er * (side < 0 ? 1.0 : 1.6)), new Point(x + er * 1.1, y - er * (side < 0 ? 1.6 : 1.0)));
                        if (mood == "curious" && side > 0) dc.DrawLine(eyePen, new Point(x - er, y - er * 2), new Point(x + er, y - er * 2.4));   // бровь домиком
                        break;
                    }
                }
            }

            // солнечные очки — у «крутого»
            if (mood == "cool")
            {
                var lens = B(Color.FromRgb(0x0A, 0x0A, 0x10));
                dc.DrawRoundedRectangle(lens, null, new Rect(cx - ex - er * 1.6 + look, ey - er * 1.1, er * 3.2, er * 2.1), er * 0.6, er * 0.6);
                dc.DrawRoundedRectangle(lens, null, new Rect(cx + ex - er * 1.6 + look, ey - er * 1.1, er * 3.2, er * 2.1), er * 0.6, er * 0.6);
                dc.DrawLine(new Pen(lens, Math.Max(1, s * 0.04)), new Point(cx - ex + er * 1.6 + look, ey - er * 0.4), new Point(cx + ex - er * 1.6 + look, ey - er * 0.4));
                dc.DrawLine(new Pen(B(A(Colors.White, 160)), Math.Max(0.7, s * 0.025)), new Point(cx - ex - er * 0.8 + look, ey - er * 0.6), new Point(cx - ex - er * 0.1 + look, ey - er * 0.6));
            }

            // рот
            double my = by + bh * 0.2, mw = s * 0.12;
            var mouthPen = new Pen(B(dark), Math.Max(1, s * 0.045)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            switch (mood)
            {
                case "surprised": case "alert":
                    dc.DrawEllipse(B(dark), null, new Point(cx + look, my), mw * 0.45, mw * 0.6);
                    break;
                case "yawn":
                {
                    double open = 0.3 + 0.7 * Math.Max(0, Math.Sin(_t * 2.4));
                    dc.DrawEllipse(B(dark), null, new Point(cx + look, my), mw * 0.5, mw * 0.75 * open);
                    break;
                }
                case "hot":
                    dc.DrawEllipse(B(dark), null, new Point(cx + look, my), mw * 0.4, mw * (0.3 + 0.2 * Math.Abs(Math.Sin(_t * 8))));
                    dc.DrawEllipse(B(Color.FromRgb(0xFF, 0x6B, 0x8B)), null, new Point(cx + look, my + mw * 0.35), mw * 0.25, mw * 0.2);   // язык — пыхтит
                    break;
                case "sad": case "cry":
                    Arc(dc, mouthPen, cx + look, my + mw * 0.5, mw * 0.7, true);
                    break;
                case "angry":
                    Arc(dc, mouthPen, cx + look, my + mw * 0.4, mw * 0.55, true);
                    break;
                case "focus": case "listen":
                    dc.DrawLine(mouthPen, new Point(cx - mw * 0.5 + look, my), new Point(cx + mw * 0.5 + look, my));
                    break;
                case "sleep":
                    dc.DrawEllipse(B(dark), null, new Point(cx + look, my), mw * 0.22, mw * 0.18);
                    break;
                case "dizzy": case "cold":
                {
                    // волнистый рот
                    var g = new StreamGeometry();
                    using (var ctx = g.Open())
                    {
                        ctx.BeginFigure(new Point(cx - mw * 0.6 + look, my), false, false);
                        for (int i = 1; i <= 4; i++) ctx.LineTo(new Point(cx - mw * 0.6 + mw * 0.3 * i + look, my + (i % 2 == 0 ? 0 : mw * 0.25)), true, true);
                    }
                    g.Freeze();
                    dc.DrawGeometry(null, mouthPen, g);
                    break;
                }
                case "cool": case "wink": case "curious":
                    Arc(dc, mouthPen, cx + mw * 0.2 + look, my - mw * 0.15, mw * 0.45, false);   // ухмылка набок
                    break;
                case "dance": case "happy": case "love": case "party": case "wave": case "charging":
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
            if (mood is "happy" or "dance" or "love" or "party" or "wink" or "wave" or "charging")
                for (int side = -1; side <= 1; side += 2)
                    dc.DrawEllipse(B(Color.FromArgb(110, 0xFF, 0x6B, 0x8B)), null, new Point(cx + side * bw * 0.32 + look, my - s * 0.04), s * 0.07, s * 0.045);

            // ручка, которая машет
            if (mood == "wave")
            {
                double a = Math.Sin(_t * 14) * 25;
                dc.PushTransform(new RotateTransform(a, cx + bw * 0.45, by));
                dc.DrawEllipse(B(body), new Pen(B(A(dark, 90)), Math.Max(0.6, s * 0.02)), new Point(cx + bw * 0.55, by - bh * 0.35), s * 0.09, s * 0.12);
                dc.Pop();
            }

            DrawHat(dc, mood, cx, top, bw, bh, ey, ex, er, s, look);

            dc.Pop();
            dc.Pop();
            dc.Pop();

            DrawExtras(dc, mood, cx, top, bw, bh, s, bob);
        }

        void DrawBody(DrawingContext dc, string mood, double cx, double by, double bw, double bh, double top, double s, Color body)
        {
            switch (_skin)
            {
                case "cat":
                    Ears(dc, cx, top, bw, s, body);
                    dc.DrawEllipse(B(body), null, new Point(cx, by), bw / 2, bh / 2);
                    break;
                case "bunny":
                    var earL = new RectangleGeometry(new Rect(cx - bw * 0.3, top - s * 0.42, bw * 0.17, s * 0.55), bw * 0.08, bw * 0.08);
                    var earR = new RectangleGeometry(new Rect(cx + bw * 0.13, top - s * 0.42, bw * 0.17, s * 0.55), bw * 0.08, bw * 0.08);
                    bool droop = mood is "sad" or "cry" or "sleep";
                    dc.PushTransform(new RotateTransform(droop ? -25 : -8 + Math.Sin(_t * 3) * 4, cx - bw * 0.2, top));
                    dc.DrawGeometry(B(body), null, earL);
                    dc.Pop();
                    dc.PushTransform(new RotateTransform(droop ? 25 : 8 - Math.Sin(_t * 3) * 4, cx + bw * 0.2, top));
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
                    bool lamp = mood is "think" or "alert" or "angry" ? (int)(_t * 4) % 2 == 0 : (int)(_t * 0.8) % 2 == 0;
                    var lampC = mood == "charging" ? Color.FromRgb(0x30, 0xD1, 0x58) : Color.FromRgb(0xFF, 0x45, 0x3A);
                    dc.DrawEllipse(B(lamp ? lampC : A(body, 160)), null, new Point(cx, top - s * 0.18), s * 0.06, s * 0.06);
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
        }

        /// <summary>Аксессуар на голове (или очки на глазах).</summary>
        void DrawHat(DrawingContext dc, string mood, double cx, double top, double bw, double bh, double ey, double ex, double er, double s, double look)
        {
            var hat = Hat ?? "auto";
            if (hat == "auto")
            {
                var d = DateTime.Now;
                hat = mood == "dance" ? "headphones" : mood == "party" ? "party" : (d.Month == 12 && d.Day >= 15) || (d.Month == 1 && d.Day <= 10) ? "santa" : "none";
            }
            double ht = _skin is "cat" or "bunny" or "robot" ? top - s * 0.02 : top;
            switch (hat)
            {
                case "cap":
                {
                    var c = B(Color.FromRgb(0xFF, 0x45, 0x3A));
                    var g = new StreamGeometry();
                    using (var ctx = g.Open())
                    {
                        ctx.BeginFigure(new Point(cx - bw * 0.36, ht + s * 0.06), true, true);
                        ctx.ArcTo(new Point(cx + bw * 0.36, ht + s * 0.06), new Size(bw * 0.36, s * 0.22), 0, false, SweepDirection.Clockwise, true, false);
                    }
                    g.Freeze();
                    dc.DrawGeometry(c, null, g);
                    dc.DrawRoundedRectangle(c, null, new Rect(cx + bw * 0.1, ht + s * 0.03, bw * 0.48, s * 0.07), s * 0.03, s * 0.03);   // козырёк
                    break;
                }
                case "crown":
                {
                    var g = new StreamGeometry();
                    double w = bw * 0.56, l = cx - w / 2, b = ht + s * 0.04, t = b - s * 0.22;
                    using (var ctx = g.Open())
                    {
                        ctx.BeginFigure(new Point(l, b), true, true);
                        ctx.LineTo(new Point(l, t + s * 0.06), true, false);
                        ctx.LineTo(new Point(l + w * 0.25, t + s * 0.13), true, false);
                        ctx.LineTo(new Point(l + w * 0.5, t), true, false);
                        ctx.LineTo(new Point(l + w * 0.75, t + s * 0.13), true, false);
                        ctx.LineTo(new Point(l + w, t + s * 0.06), true, false);
                        ctx.LineTo(new Point(l + w, b), true, false);
                    }
                    g.Freeze();
                    dc.DrawGeometry(B(Color.FromRgb(0xFF, 0xD6, 0x0A)), null, g);
                    dc.DrawEllipse(B(Color.FromRgb(0xFF, 0x37, 0x5F)), null, new Point(cx, b - s * 0.06), s * 0.03, s * 0.03);
                    break;
                }
                case "bow":
                {
                    var pink = B(Color.FromRgb(0xFF, 0x5C, 0xA8));
                    double bx = cx + bw * 0.28, byy = ht + s * 0.02;
                    for (int side = -1; side <= 1; side += 2)
                    {
                        var g = new StreamGeometry();
                        using (var ctx = g.Open())
                        {
                            ctx.BeginFigure(new Point(bx, byy), true, true);
                            ctx.LineTo(new Point(bx + side * s * 0.16, byy - s * 0.09), true, false);
                            ctx.LineTo(new Point(bx + side * s * 0.16, byy + s * 0.09), true, false);
                        }
                        g.Freeze();
                        dc.DrawGeometry(pink, null, g);
                    }
                    dc.DrawEllipse(pink, null, new Point(bx, byy), s * 0.04, s * 0.04);
                    break;
                }
                case "party": case "santa":
                {
                    bool santa = hat == "santa";
                    var g = new StreamGeometry();
                    double b = ht + s * 0.05, w = bw * 0.42;
                    using (var ctx = g.Open())
                    {
                        ctx.BeginFigure(new Point(cx - w / 2, b), true, true);
                        ctx.LineTo(new Point(cx + (santa ? w * 0.7 : 0), b - s * (santa ? 0.32 : 0.38)), true, false);
                        ctx.LineTo(new Point(cx + w / 2, b), true, false);
                    }
                    g.Freeze();
                    dc.DrawGeometry(B(santa ? Color.FromRgb(0xE0, 0x20, 0x20) : Color.FromRgb(0x5E, 0x5C, 0xE6)), null, g);
                    if (santa) dc.DrawRoundedRectangle(B(Colors.White), null, new Rect(cx - w / 2 - s * 0.02, b - s * 0.04, w + s * 0.04, s * 0.07), s * 0.03, s * 0.03);
                    else dc.DrawLine(new Pen(B(Color.FromRgb(0xFF, 0xD6, 0x0A)), Math.Max(0.7, s * 0.03)), new Point(cx - w * 0.25, b - s * 0.1), new Point(cx + w * 0.2, b - s * 0.18));
                    dc.DrawEllipse(B(santa ? Colors.White : Color.FromRgb(0xFF, 0xD6, 0x0A)), null, new Point(cx + (santa ? w * 0.7 : 0), b - s * (santa ? 0.32 : 0.38)), s * 0.05, s * 0.05);
                    break;
                }
                case "headphones":
                {
                    var c = B(Color.FromRgb(0x2C, 0x2C, 0x34));
                    var g = new StreamGeometry();
                    using (var ctx = g.Open())
                    {
                        ctx.BeginFigure(new Point(cx - bw * 0.5, ey), false, false);
                        ctx.ArcTo(new Point(cx + bw * 0.5, ey), new Size(bw * 0.5, (ey - ht) + s * 0.12), 0, false, SweepDirection.Clockwise, true, false);
                    }
                    g.Freeze();
                    dc.DrawGeometry(null, new Pen(c, Math.Max(1, s * 0.06)), g);
                    for (int side = -1; side <= 1; side += 2)
                        dc.DrawRoundedRectangle(c, null, new Rect(cx + side * bw * 0.5 - s * 0.07, ey - s * 0.1, s * 0.14, s * 0.22), s * 0.05, s * 0.05);
                    break;
                }
                case "glasses":
                {
                    if (mood == "cool") break;   // уже в тёмных очках
                    var pen = new Pen(B(Color.FromRgb(0x14, 0x14, 0x1A)), Math.Max(0.8, s * 0.035));
                    for (int side = -1; side <= 1; side += 2) dc.DrawEllipse(null, pen, new Point(cx + side * ex + look, ey), er * 1.6, er * 1.5);
                    dc.DrawLine(pen, new Point(cx - ex + er * 1.6 + look, ey), new Point(cx + ex - er * 1.6 + look, ey));
                    break;
                }
                case "flower":
                {
                    double fx = cx - bw * 0.3, fy = ht + s * 0.02;
                    for (int i = 0; i < 5; i++)
                    {
                        double a = i * Math.PI * 2 / 5 + _t * 0.3;
                        dc.DrawEllipse(B(Color.FromRgb(0xFF, 0x9F, 0xD6)), null, new Point(fx + Math.Cos(a) * s * 0.07, fy + Math.Sin(a) * s * 0.07), s * 0.055, s * 0.055);
                    }
                    dc.DrawEllipse(B(Color.FromRgb(0xFF, 0xD6, 0x0A)), null, new Point(fx, fy), s * 0.045, s * 0.045);
                    break;
                }
            }
        }

        /// <summary>Значки вокруг: «z» во сне, ноты в танце, сердечки, капли пота, слёзы, конфетти, молния зарядки.</summary>
        void DrawExtras(DrawingContext dc, string mood, double cx, double top, double bw, double bh, double s, double bob)
        {
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
                    double p = _t * 0.6 % 1, q = (_t * 0.6 + 0.5) % 1;
                    Text(dc, "♪", cx + s * 0.42, top + s * 0.1 - p * s * 0.45, s * 0.3, (byte)(220 * (1 - p)));
                    Text(dc, "♫", cx - s * 0.44, top + s * 0.15 - q * s * 0.45, s * 0.26, (byte)(180 * (1 - q)));
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
                case "hot":
                {
                    double p = _t * 0.9 % 1;
                    dc.DrawEllipse(B(Color.FromArgb((byte)(230 * (1 - p)), 0x64, 0xD2, 0xFF)), null, new Point(cx + bw * 0.46, top + s * 0.1 + p * s * 0.3 + bob), s * 0.05, s * 0.075);
                    break;
                }
                case "cold":
                    Text(dc, "❄", cx + s * 0.42, top + bob, s * 0.26, 220);
                    break;
                case "cry":
                    // слёзы — светлые, с тёмной обводкой: видны на любом цвете персонажа
                    for (int side = -1; side <= 1; side += 2)
                    {
                        double p = (_t * 1.2 + (side > 0 ? 0.5 : 0)) % 1;
                        byte al = (byte)(250 * (1 - p));
                        dc.DrawEllipse(B(Color.FromArgb(al, 0xE8, 0xF6, 0xFF)), new Pen(B(Color.FromArgb((byte)(al / 2), 0x14, 0x14, 0x1A)), Math.Max(0.5, s * 0.015)),
                            new Point(cx + side * bw * 0.24, top + bh * 0.45 + p * s * 0.4 + bob), s * 0.045, s * 0.07);
                    }
                    break;
                case "love":
                {
                    double p = _t * 0.7 % 1;
                    Heart(dc, cx + s * 0.42, top - p * s * 0.35 + bob, s * 0.12, Color.FromArgb((byte)(255 * (1 - p)), 0xFF, 0x37, 0x5F));
                    break;
                }
                case "party":
                {
                    var cols = new[] { Color.FromRgb(0xFF, 0x45, 0x3A), Color.FromRgb(0xFF, 0xD6, 0x0A), Color.FromRgb(0x30, 0xD1, 0x58), Color.FromRgb(0x0A, 0x84, 0xFF), Color.FromRgb(0xBF, 0x5A, 0xF2) };
                    for (int i = 0; i < 8; i++)
                    {
                        double p = (_t * 0.8 + i * 0.13) % 1, x = cx + Math.Sin(i * 2.3) * s * 0.55;
                        dc.DrawRectangle(B(A(cols[i % cols.Length], (byte)(255 * (1 - p)))), null, new Rect(x, top - s * 0.3 + p * s * 0.9 + bob, s * 0.06, s * 0.06));
                    }
                    break;
                }
                case "charging":
                    Text(dc, "⚡", cx + s * 0.42, top + s * 0.02 + bob, s * 0.3, (byte)(180 + 75 * Math.Abs(Math.Sin(_t * 4))));
                    break;
                case "angry":
                    Text(dc, "💢", cx + s * 0.4, top + bob, s * 0.26, 255);
                    break;
                case "curious":
                    Text(dc, "?", cx + s * 0.42, top + s * 0.02 + bob - Math.Abs(Math.Sin(_t * 4)) * s * 0.06, s * 0.32, 230);
                    break;
                case "dizzy":
                    for (int i = 0; i < 3; i++)
                    {
                        double a = _t * 5 + i * Math.PI * 2 / 3;
                        Text(dc, "★", cx + Math.Cos(a) * s * 0.4, top - s * 0.05 + Math.Sin(a) * s * 0.1 + bob, s * 0.18, 230);
                    }
                    break;
                case "yawn":
                    Text(dc, "z", cx + s * 0.4, top + bob, s * 0.18, 150);
                    break;
            }
        }

        void Eye(DrawingContext dc, double x, double y, double rx, double ry, double look, Color dark, bool blink)
        {
            dc.DrawEllipse(B(dark), null, new Point(x, y), rx, ry);
            if (!blink) dc.DrawEllipse(B(Colors.White), null, new Point(x - rx * 0.35 + look * 0.2, y - ry * 0.4), rx * 0.32, rx * 0.32);
        }

        static void Spiral(DrawingContext dc, double x, double y, double r, Pen pen, double rot)
        {
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(x, y), false, false);
                for (int i = 1; i <= 16; i++)
                {
                    double a = rot + i * 0.75, rr = r * i / 16;
                    ctx.LineTo(new Point(x + Math.Cos(a) * rr, y + Math.Sin(a) * rr), true, true);
                }
            }
            g.Freeze();
            dc.DrawGeometry(null, new Pen(pen.Brush, Math.Max(0.7, pen.Thickness * 0.6)), g);
        }

        void Ears(DrawingContext dc, double cx, double top, double bw, double s, Color body)
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
