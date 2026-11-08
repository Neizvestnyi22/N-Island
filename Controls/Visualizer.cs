using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NIsland.Services;

namespace NIsland.Controls
{
    /// <summary>
    /// Визуализатор музыки под то, что сейчас звучит. Рисует сам в свой битмап (пиксели, сложение света, «эхо» прошлого кадра),
    /// поэтому дерево WPF не нагружается. Стили и палитры переключаются на лету; звук слушается, только пока визуализатор виден.
    /// </summary>
    public partial class Visualizer : FrameworkElement
    {
        public record StyleInfo(string Key, string Name, string Desc, string Group = "Классика");

        public static readonly StyleInfo[] Styles = new StyleInfo[]
        {
            new("bars", "Полосы", "Спектр столбиками с падающими пиками"),
            new("barswave", "Полосы и волна", "Классика старых плееров: спектр с отражением и волна сверху"),
            new("scope", "Осциллограф", "Форма звуковой волны со светящимся следом"),
            new("leds", "Светодиоды", "Шкала как у музыкального центра: зелёный, жёлтый, красный"),
            new("ambience", "Амбиент", "Кольцо звука закручивается в спираль"),
            new("alchemy", "Алхимия", "Калейдоскоп из лучей, которые тянутся к центру"),
            new("tunnel", "Тоннель", "Кольца летят на тебя на каждый удар"),
            new("radial", "Круговой спектр", "Лучи вокруг круга, который дышит в такт"),
            new("mirror", "Зеркало", "Плавный симметричный спектр, как в Apple Music"),
            new("particles", "Частицы", "Искры разлетаются на каждый удар баса"),
            new("spectrogram", "Спектрограмма", "Бегущая карта частот: видно, как меняется музыка"),
            new("neon", "Неоновые волны", "Бас, середина и верха — отдельными светящимися волнами"),
            new("blob", "Капля", "Жидкая капля меняет форму под музыку"),
        }.Concat(MoreStyles).Concat(NewStyles).ToArray();

        public static readonly (string Key, string Name)[] Palettes =
        {
            ("art", "Цвет обложки"), ("accent", "Акцент"), ("rainbow", "Радуга"), ("fire", "Огонь"), ("ocean", "Океан"), ("neon", "Неон"), ("mono", "Белый"),
            ("sunset", "Закат"), ("aurora", "Сияние"), ("vapor", "Ретровейв"), ("matrix", "Матрица"), ("ice", "Лёд"), ("lava", "Лава"), ("candy", "Конфеты"),
            ("gold", "Золото"), ("amber", "Янтарный ЭЛТ"), ("cga", "CGA"), ("pastel", "Пастель"), ("forest", "Лес"), ("custom", "Свои цвета"),
        };

        /// <summary>Следующий стиль. Если есть избранные — ходим только по ним.</summary>
        public static string Next(string current, int dir)
        {
            var fav = (St.VizFavorites ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                             .Where(k => Array.Exists(Styles, s => s.Key == k)).ToArray();
            var keys = fav.Length >= 2 ? fav : Styles.Select(s => s.Key).ToArray();
            int i = Array.IndexOf(keys, current);
            if (i < 0) i = dir > 0 ? -1 : 0;
            return keys[((i + dir) % keys.Length + keys.Length) % keys.Length];
        }

        public static string NameOf(string key) => Array.Find(Styles, s => s.Key == key)?.Name ?? key;

        string _style = "bars", _palette = "art";
        Color _base = Color.FromRgb(0x0A, 0x84, 0xFF);

        public string VisualStyle
        {
            get => _style;
            set { value = Replacement(value ?? "bars"); if (_style == value) return; _style = value; ResetState(); }
        }

        public string Palette { get => _palette; set { value ??= "art"; if (_palette != value) { _palette = value; _lutKey = null; } } }
        public Color BaseColor { get => _base; set { if (_base != value) { _base = value; _lutKey = null; } } }
        /// <summary>Сколько пикселей рисовать (дальше WPF плавно растягивает): маленькое окно — мало, большое — больше.</summary>
        public int MaxPixels { get => _maxPx; set { if (_maxPx != value) { _maxPx = value; _bmp = null; } } }
        int _maxPx = 360 * 200;
        double _fps;
        /// <summary>Кадров в секунду: своё значение или из настроек.</summary>
        public double Fps { get => _fps > 0 ? _fps : Math.Clamp(St.VizFps, 20, 60); set => _fps = value; }
        static NIsland.Core.Settings St => NIsland.Core.Store.Settings;

        WriteableBitmap _bmp;
        int _w, _h;
        uint[] _px, _tmp;
        IDisposable _lease;
        bool _hooked;
        readonly float[] _bands = new float[AudioCapture.BandCount], _peaks = new float[AudioCapture.BandCount], _wave = new float[AudioCapture.WaveCount];
        int _lastBeats;
        bool _beat;
        double _t, _lastT, _gain = 2, _sinceBeat = 9;
        float _level;
        readonly Random _rnd = new();
        readonly uint[] _lut = new uint[256];
        string _lutKey;

        public Visualizer()
        {
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.Linear);
            IsHitTestVisible = true;
            Loaded += (_, __) => Sync();
            Unloaded += (_, __) => Sync(false);
            IsVisibleChanged += (_, __) => Sync();
        }

        void Sync(bool? force = null)
        {
            bool on = force ?? (IsLoaded && IsVisible);
            if (on && !_hooked)
            {
                _hooked = true;
                _lease ??= AudioCapture.I.Use(60);
                _lastT = 0;
                CompositionTarget.Rendering += OnFrame;
            }
            else if (!on && _hooked)
            {
                _hooked = false;
                CompositionTarget.Rendering -= OnFrame;
                _lease?.Dispose();
                _lease = null;
            }
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo info)
        {
            base.OnRenderSizeChanged(info);
            Allocate();
        }

        void Allocate()
        {
            double W = ActualWidth, H = ActualHeight;
            if (W < 4 || H < 4) return;
            double k = Math.Min(1.5, Math.Sqrt(MaxPixels / (W * H)));
            int w = Math.Max(32, (int)(W * k)), h = Math.Max(24, (int)(H * k));
            if (_bmp != null && w == _w && h == _h) return;
            _w = w; _h = h;
            _px = new uint[w * h];
            _tmp = new uint[w * h];
            _bmp = new WriteableBitmap(w, h, 96, 96, PixelFormats.Pbgra32, null);
            ResetState();
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            if (_bmp != null) dc.DrawImage(_bmp, new Rect(0, 0, ActualWidth, ActualHeight));
        }

        void OnFrame(object sender, EventArgs e)
        {
            if (_bmp == null) { Allocate(); if (_bmp == null) return; }
            double now = ((RenderingEventArgs)e).RenderingTime.TotalSeconds;
            if (_lastT > 0 && now - _lastT < 1 / Fps - 0.002) return;
            double dt = _lastT == 0 ? 1 / Fps : Math.Min(0.1, now - _lastT);
            _lastT = now;
            double speed = Math.Clamp(St.VizSpeed, 10, 400) / 100.0;
            _t += dt * speed;

            var cap = AudioCapture.I;
            cap.Read(_bands, _peaks, _wave);
            Tune();
            _level = (float)Math.Min(1, cap.Level * Sens);
            int beats = cap.Beats;
            _beat = beats != _lastBeats && cap.Running && St.VizBeatFx > 0;
            _lastBeats = beats;
            _sinceBeat = _beat ? 0 : _sinceBeat + dt;
            BuildLut();

            try { Draw(dt * speed); }
            catch (Exception ex) { NIsland.Core.Log.Info("visualizer: " + ex.Message); }
            _bmp.WritePixels(new Int32Rect(0, 0, _w, _h), Post(), _w * 4, 0);
        }

        void Draw(double dt)
        {
            switch (_style)
            {
                case "barswave": BarsWave(); break;
                case "scope": Scope(); break;
                case "leds": Leds(); break;
                case "ambience": Ambience(); break;
                case "alchemy": Alchemy(); break;
                case "tunnel": Tunnel(); break;
                case "radial": Radial(); break;
                case "mirror": Mirror(); break;
                case "particles": Particles(dt); break;
                case "spectrogram": Spectrogram(); break;
                case "neon": Neon(); break;
                case "blob": Blob(); break;
                default: if (!Draw2(dt) && !Draw3(dt)) Bars(); break;
            }
        }

        void ResetState()
        {
            if (_px != null) Array.Clear(_px);
            _parts.Clear();
            ResetState2();
            ResetState3();
        }

        // ------------------------------------------------------------------
        //  Палитры
        // ------------------------------------------------------------------

        void BuildLut()
        {
            var accent = (Application.Current?.Resources["AccentBrush"] as SolidColorBrush)?.Color ?? Colors.DodgerBlue;
            int hue = St.VizHue > 0 ? (int)(_t * St.VizHue * 0.9) % 360 / 2 * 2 : 0;   // перелив: тон медленно идёт по кругу
            string key = _palette + "|" + _base + "|" + accent + "|" + hue + "|" + St.VizSat + "|" + St.VizBright + "|" + St.VizColorA + St.VizColorB;
            if (key == _lutKey) return;
            _lutKey = key;
            var ca = NIsland.Core.ColorUtil.Parse(St.VizColorA, Color.FromRgb(255, 45, 149));
            var cb = NIsland.Core.ColorUtil.Parse(St.VizColorB, Color.FromRgb(0, 229, 255));
            double sat = Math.Clamp(St.VizSat, 0, 150) / 100.0, bri = Math.Clamp(St.VizBright, 40, 160) / 100.0;
            for (int i = 0; i < 256; i++)
            {
                double t = i / 255.0;
                Color c = _palette switch
                {
                    "sunset" => Grad(t, (0, 45, 10, 90), (0.35, 200, 30, 110), (0.65, 255, 110, 60), (1, 255, 220, 120)),
                    "aurora" => Grad(t, (0, 5, 40, 60), (0.35, 0, 200, 140), (0.7, 90, 240, 200), (1, 200, 120, 255)),
                    "vapor" => Grad(t, (0, 60, 0, 120), (0.4, 255, 40, 180), (0.7, 255, 150, 220), (1, 0, 240, 255)),
                    "matrix" => Grad(t, (0, 0, 30, 5), (0.5, 0, 180, 40), (0.85, 80, 255, 120), (1, 220, 255, 220)),
                    "ice" => Grad(t, (0, 20, 40, 90), (0.5, 90, 170, 255), (1, 240, 250, 255)),
                    "lava" => Grad(t, (0, 30, 0, 0), (0.3, 150, 0, 20), (0.6, 255, 60, 0), (0.85, 255, 170, 0), (1, 255, 255, 160)),
                    "candy" => Grad(t, (0, 255, 90, 160), (0.33, 255, 200, 80), (0.66, 120, 220, 255), (1, 200, 140, 255)),
                    "gold" => Grad(t, (0, 60, 35, 0), (0.5, 210, 150, 30), (0.8, 255, 215, 90), (1, 255, 250, 210)),
                    "amber" => Grad(t, (0, 40, 15, 0), (0.6, 255, 150, 0), (1, 255, 225, 150)),
                    "cga" => t < 0.33 ? Color.FromRgb(85, 255, 255) : t < 0.66 ? Color.FromRgb(255, 85, 255) : Color.FromRgb(255, 255, 255),
                    "pastel" => Hsv(200 + t * 160, 0.35, 1),
                    "forest" => Grad(t, (0, 10, 40, 10), (0.5, 60, 160, 60), (0.8, 180, 220, 80), (1, 240, 255, 200)),
                    "custom" => Grad(t, (0, ca.R / 3, ca.G / 3, ca.B / 3), (0.45, ca.R, ca.G, ca.B), (0.85, cb.R, cb.G, cb.B), (1, Math.Min(255, cb.R + 90), Math.Min(255, cb.G + 90), Math.Min(255, cb.B + 90))),
                    "rainbow" => Hsv(t * 300, 0.85, 1),
                    "fire" => Grad(t, (0, 40, 0, 0), (0.35, 210, 25, 0), (0.65, 255, 140, 0), (0.85, 255, 220, 60), (1, 255, 255, 225)),
                    "ocean" => Grad(t, (0, 0, 25, 70), (0.4, 0, 95, 210), (0.75, 0, 205, 235), (1, 215, 255, 255)),
                    "neon" => Grad(t, (0, 125, 0, 255), (0.5, 255, 0, 185), (1, 0, 240, 255)),
                    "mono" => Grad(t, (0, 110, 110, 125), (1, 255, 255, 255)),
                    "accent" => Around(accent, t),
                    _ => Around(_base, t),
                };
                if (hue != 0 || sat != 1 || bri != 1)
                {
                    RgbToHsv(c, out double h, out double s, out double v);
                    c = Hsv(h + hue, Math.Clamp(s * sat, 0, 1), Math.Clamp(v * bri, 0, 1));
                }
                _lut[i] = (uint)(c.R << 16 | c.G << 8 | c.B);
            }
        }

        /// <summary>Оттенки вокруг цвета: от глубокого к светлому со сдвигом тона — так одна обложка даёт живую гамму.</summary>
        static Color Around(Color c, double t)
        {
            RgbToHsv(c, out double h, out double s, out double v);
            if (s < 0.12) return Hsv(h, s, 0.45 + 0.55 * t);   // белый/серый акцент — просто от серого к белому
            return Hsv(h - 25 + 70 * t, Math.Clamp(s * (1.05 - 0.45 * t), 0, 1), Math.Clamp(0.55 + 0.5 * t + (v - 0.8) * 0.3, 0.35, 1));
        }

        static Color Grad(double t, params (double At, int R, int G, int B)[] stops)
        {
            for (int i = 1; i < stops.Length; i++)
            {
                if (t > stops[i].At) continue;
                var a = stops[i - 1]; var b = stops[i];
                double k = (t - a.At) / Math.Max(1e-6, b.At - a.At);
                return Color.FromRgb((byte)(a.R + (b.R - a.R) * k), (byte)(a.G + (b.G - a.G) * k), (byte)(a.B + (b.B - a.B) * k));
            }
            var l = stops[^1];
            return Color.FromRgb((byte)l.R, (byte)l.G, (byte)l.B);
        }

        static Color Hsv(double h, double s, double v)
        {
            h = ((h % 360) + 360) % 360;
            double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
            (double r, double g, double b) = h < 60 ? (c, x, 0d) : h < 120 ? (x, c, 0d) : h < 180 ? (0d, c, x) : h < 240 ? (0d, x, c) : h < 300 ? (x, 0d, c) : (c, 0d, x);
            return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
        }

        static void RgbToHsv(Color c, out double h, out double s, out double v)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
            v = max;
            s = max == 0 ? 0 : d / max;
            if (d == 0) h = 0;
            else if (max == r) h = 60 * (((g - b) / d) % 6);
            else if (max == g) h = 60 * ((b - r) / d + 2);
            else h = 60 * ((r - g) / d + 4);
            if (h < 0) h += 360;
        }

        uint Pal(double t) => _lut[(int)(Math.Clamp(t, 0, 1) * 255)];

        // ------------------------------------------------------------------
        //  Растр: пиксели в формате BGRA с предумноженной прозрачностью
        // ------------------------------------------------------------------

        /// <summary>Длина следа из настроек: 50 — как задумал стиль, меньше — короче, больше — длиннее.</summary>
        static int TrailKeep(int keep)
        {
            int tr = Math.Clamp(St.VizTrail, 0, 100);
            if (tr == 50) return keep;
            if (keep <= 0) return tr > 50 ? (tr - 50) * 4 : 0;
            double k = tr < 50 ? keep * (0.35 + 0.65 * tr / 50.0) : keep + (252 - keep) * (tr - 50) / 50.0 * 0.85;
            return (int)Math.Clamp(k, 0, 252);
        }

        void Fade(int keep)
        {
            keep = TrailKeep(keep);
            if (keep <= 0) { Array.Clear(_px); return; }
            var px = _px;
            for (int i = 0; i < px.Length; i++)
            {
                uint p = px[i];
                if (p == 0) continue;
                uint a = ((p >> 24) * (uint)keep) >> 8, r = (((p >> 16) & 255) * (uint)keep) >> 8, g = (((p >> 8) & 255) * (uint)keep) >> 8, b = ((p & 255) * (uint)keep) >> 8;
                px[i] = a << 24 | r << 16 | g << 8 | b;
            }
        }

        /// <summary>«Эхо»: прошлый кадр чуть увеличенным/повёрнутым и притушенным — отсюда спирали и тоннели.</summary>
        void Feedback(double zoom, double rot, int keep, double cx = 0.5, double cy = 0.5)
        {
            keep = TrailKeep(keep);
            Array.Copy(_px, _tmp, _px.Length);
            double ox = _w * cx, oy = _h * cy, cos = Math.Cos(-rot) / zoom, sin = Math.Sin(-rot) / zoom;
            var px = _px; var src = _tmp;
            for (int y = 0; y < _h; y++)
            {
                double dy = y - oy;
                double bx = ox - dy * sin, by = oy + dy * cos;
                int row = y * _w;
                for (int x = 0; x < _w; x++)
                {
                    double dx = x - ox;
                    int sx = (int)(bx + dx * cos), sy = (int)(by + dx * sin);
                    uint p = (uint)sx < (uint)_w && (uint)sy < (uint)_h ? src[sy * _w + sx] : 0;
                    if (p != 0)
                    {
                        uint a = ((p >> 24) * (uint)keep) >> 8, r = (((p >> 16) & 255) * (uint)keep) >> 8, g = (((p >> 8) & 255) * (uint)keep) >> 8, b = ((p & 255) * (uint)keep) >> 8;
                        p = a << 24 | r << 16 | g << 8 | b;
                    }
                    px[row + x] = p;
                }
            }
        }

        /// <summary>Сложение света (как свечение): яркое на ярком становится белее.</summary>
        void Plus(int x, int y, uint rgb, int a)
        {
            if ((uint)x >= (uint)_w || (uint)y >= (uint)_h || a <= 0) return;
            if (a > 255) a = 255;
            int i = y * _w + x;
            uint p = _px[i];
            int pa = (int)(p >> 24) + a;
            int pr = (int)((p >> 16) & 255) + (int)((rgb >> 16) & 255) * a / 255;
            int pg = (int)((p >> 8) & 255) + (int)((rgb >> 8) & 255) * a / 255;
            int pb = (int)(p & 255) + (int)(rgb & 255) * a / 255;
            _px[i] = (uint)(Math.Min(255, pa) << 24 | Math.Min(255, pr) << 16 | Math.Min(255, pg) << 8 | Math.Min(255, pb));
        }

        /// <summary>Обычное наложение «поверх» с прозрачностью.</summary>
        void Over(int x, int y, uint rgb, int a)
        {
            if ((uint)x >= (uint)_w || (uint)y >= (uint)_h || a <= 0) return;
            if (a > 255) a = 255;
            int i = y * _w + x;
            uint p = _px[i];
            int inv = 255 - a;
            int pa = a + (int)(p >> 24) * inv / 255;
            int pr = (int)((rgb >> 16) & 255) * a / 255 + (int)((p >> 16) & 255) * inv / 255;
            int pg = (int)((rgb >> 8) & 255) * a / 255 + (int)((p >> 8) & 255) * inv / 255;
            int pb = (int)(rgb & 255) * a / 255 + (int)(p & 255) * inv / 255;
            _px[i] = (uint)(pa << 24 | pr << 16 | pg << 8 | pb);
        }

        /// <summary>Точка с субпиксельной точностью (размазывается на 4 соседних пикселя).</summary>
        void Splat(double x, double y, uint rgb, int a)
        {
            int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y);
            double fx = x - ix, fy = y - iy;
            Plus(ix, iy, rgb, (int)(a * (1 - fx) * (1 - fy)));
            Plus(ix + 1, iy, rgb, (int)(a * fx * (1 - fy)));
            Plus(ix, iy + 1, rgb, (int)(a * (1 - fx) * fy));
            Plus(ix + 1, iy + 1, rgb, (int)(a * fx * fy));
        }

        void Line(double x0, double y0, double x1, double y1, uint rgb, int a, double width = 1)
        {
            double dx = x1 - x0, dy = y1 - y0;
            int steps = (int)Math.Ceiling(Math.Max(Math.Abs(dx), Math.Abs(dy))) + 1;
            if (steps > 4000) return;
            for (int i = 0; i <= steps; i++)
            {
                double x = x0 + dx * i / steps, y = y0 + dy * i / steps;
                if (width <= 1.3) Splat(x, y, rgb, a);
                else Dot(x, y, width / 2, rgb, a / 2, true);
            }
        }

        void Dot(double cx, double cy, double rad, uint rgb, int a, bool soft)
        {
            if (rad < 0.7) { Splat(cx, cy, rgb, a); return; }
            int x0 = (int)(cx - rad), x1 = (int)(cx + rad) + 1, y0 = (int)(cy - rad), y1 = (int)(cy + rad) + 1;
            double r2 = rad * rad;
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    double ddx = x - cx, ddy = y - cy, d2 = ddx * ddx + ddy * ddy;
                    if (d2 >= r2) continue;
                    double d = Math.Sqrt(d2) / rad;
                    double w = soft ? (1 - d) * (1 - d) : d < 0.75 ? 1 : (1 - d) * 4;
                    Plus(x, y, rgb, (int)(a * w));
                }
        }

        void FillRect(int x0, int y0, int x1, int y1, Func<int, uint> colorOfRow, int a)
        {
            x0 = Math.Max(0, x0); x1 = Math.Min(_w, x1); y0 = Math.Max(0, y0); y1 = Math.Min(_h, y1);
            for (int y = y0; y < y1; y++)
            {
                uint c = colorOfRow(y);
                for (int x = x0; x < x1; x++) Over(x, y, c, a);
            }
        }

        // ------------------------------------------------------------------
        //  Данные звука в удобном виде
        // ------------------------------------------------------------------

        /// <summary>Уровень на позиции 0…1 вдоль спектра (басы слева), с плавной интерполяцией между полосами.</summary>
        float Band(double u, float[] src = null)
        {
            src ??= _bands;
            double f = 1 + Math.Clamp(u, 0, 1) * 60;   // самые первые полосы (32–40 Гц) почти всегда гудят — начинаем со второй
            int i = (int)f;
            double k = f - i;
            float a = src[Math.Min(i, src.Length - 1)], b = src[Math.Min(i + 1, src.Length - 1)];
            return (float)(a + (b - a) * (0.5 - 0.5 * Math.Cos(k * Math.PI)));
        }

        float Group(double from, double to)
        {
            float s = 0; int n = 0;
            for (double u = from; u < to; u += 0.02) { s += Band(u); n++; }
            return n == 0 ? 0 : s / n;
        }

        double Wave(int i, int count)
        {
            // автоусиление: тихие треки тоже дают заметную волну
            return _wave[Math.Clamp(i * AudioCapture.WaveCount / Math.Max(1, count), 0, AudioCapture.WaveCount - 1)] * _gain;
        }

        void UpdateGain()
        {
            float max = 0.02f;
            foreach (var v in _wave) max = Math.Max(max, Math.Abs(v));
            double want = Math.Clamp(0.85 / max, 1, 12);
            _gain += (want - _gain) * (want < _gain ? 0.3 : 0.04);
        }

        double Min => Math.Min(_w, _h);

        // ------------------------------------------------------------------
        //  Стили
        // ------------------------------------------------------------------

        void Bars()
        {
            Fade(0);
            int n = Math.Clamp(_w / 8, 12, 64);
            double gap = Math.Max(1, _w / 260.0), bw = (_w - gap * (n + 1)) / n;
            for (int i = 0; i < n; i++)
            {
                double u = (i + 0.5) / n;
                double v = Band(u), pk = Band(u, _peaks);
                int x0 = (int)(gap + i * (bw + gap)), x1 = (int)(gap + i * (bw + gap) + bw);
                int top = (int)(_h - 1 - v * (_h - 4));
                FillRect(x0, top, x1, _h, y => Pal(0.12 + 0.85 * (_h - y) / (double)_h), 235);
                int py = (int)(_h - 3 - pk * (_h - 4));
                FillRect(x0, py, x1, py + Math.Max(2, _h / 90), _ => Pal(1), 255);
            }
        }

        void BarsWave()
        {
            Fade(105);
            UpdateGain();
            int n = Math.Clamp(_w / 5, 24, 96);
            double gap = Math.Max(1, _w / 400.0), bw = (_w - gap * (n + 1)) / n;
            double baseY = _h * 0.66;
            for (int i = 0; i < n; i++)
            {
                double u = (i + 0.5) / n, v = Band(u);
                int x0 = (int)(gap + i * (bw + gap)), x1 = Math.Max(x0 + 1, (int)(gap + i * (bw + gap) + bw));
                int top = (int)(baseY - v * _h * 0.58);
                FillRect(x0, top, x1, (int)baseY, y => Pal(0.2 + 0.8 * (baseY - y) / (_h * 0.58)), 230);
                // отражение «на полу»
                int refl = (int)(v * _h * 0.3);
                FillRect(x0, (int)baseY + 1, x1, (int)baseY + 1 + refl, y => Pal(0.25), 60);
            }
            uint wc = Pal(0.95);
            double prevX = 0, prevY = _h * 0.28;
            for (int x = 0; x < _w; x += 2)
            {
                double y = _h * 0.28 - Wave(x, _w) * _h * 0.2;
                if (x > 0) Line(prevX, prevY, x, y, wc, 200);
                prevX = x; prevY = y;
            }
        }

        void Scope()
        {
            Fade(150);
            UpdateGain();
            uint c = Pal(0.45 + _level * 0.55);
            double prevX = 0, prevY = _h / 2.0;
            double th = Math.Max(1.5, Min / 70);
            for (int x = 0; x < _w; x++)
            {
                double y = _h / 2.0 - Wave(x, _w) * _h * 0.42;
                if (x > 0)
                {
                    Line(prevX, prevY, x, y, c, 45, th * 2.2);
                    Line(prevX, prevY, x, y, 0xFFFFFF, 140);
                    Line(prevX, prevY, x, y, c, 200);
                }
                prevX = x; prevY = y;
            }
        }

        void Leds()
        {
            Fade(0);
            int cols = Math.Clamp(_w / 11, 10, 32), rows = Math.Clamp(_h / 9, 6, 16);
            double cw = _w / (double)cols, ch = _h / (double)rows;
            double rad = Math.Max(1, Math.Min(cw, ch) * 0.36);
            bool classic = _palette == "art";
            for (int c = 0; c < cols; c++)
            {
                double u = (c + 0.5) / cols;
                int lit = (int)Math.Round(Band(u) * rows), peak = (int)Math.Round(Band(u, _peaks) * rows) - 1;
                for (int r = 0; r < rows; r++)
                {
                    double rr = r / (double)(rows - 1);
                    uint col = classic ? (rr < 0.6 ? 0x30D158u : rr < 0.85 ? 0xFFD60Au : 0xFF453Au) : Pal(0.2 + 0.8 * rr);
                    bool on = r < lit || r == peak;
                    Dot((c + 0.5) * cw, _h - (r + 0.5) * ch, rad, col, on ? 255 : 26, false);
                }
            }
        }

        void Ambience()
        {
            float bass = Group(0, 0.15);
            Feedback(1.012 + _level * 0.025, 0.012 + bass * 0.035, 236);
            UpdateGain();
            double cx = _w / 2.0, cy = _h / 2.0, r0 = Min * 0.17 * (1 + _level * 0.5);
            int n = 180;
            double px = 0, py = 0;
            for (int k = 0; k <= n; k++)
            {
                double a = 2 * Math.PI * k / n + _t * 0.6;
                double r = r0 * (1 + Wave(k % n, n) * 0.55);
                double x = cx + Math.Cos(a) * r * (_w / (double)_h > 1.4 ? 1.25 : 1), y = cy + Math.Sin(a) * r;
                if (k > 0) Line(px, py, x, y, Pal((k / (double)n + _t * 0.07) % 1), 150);
                px = x; py = y;
            }
            if (_beat) Dot(cx, cy, r0 * 0.6, Pal(0.9), 120, true);
        }

        void Alchemy()
        {
            Feedback(0.982, -0.007, 234);
            double cx = _w / 2.0, cy = _h / 2.0;
            int sym = 6;
            for (int j = 0; j < 14; j++)
            {
                double v = Band(j / 14.0);
                if (v < 0.08) continue;
                double len = v * Min * 0.5, inner = len * 0.15;
                double baseA = _t * 0.35 + j * 0.23;
                uint c = Pal(j / 14.0);
                for (int s = 0; s < sym; s++)
                {
                    foreach (var sign in new[] { 1, -1 })
                    {
                        double a = sign * (baseA + s * 2 * Math.PI / sym);
                        Line(cx + Math.Cos(a) * inner, cy + Math.Sin(a) * inner, cx + Math.Cos(a) * len, cy + Math.Sin(a) * len, c, (int)(60 + v * 140));
                    }
                }
            }
        }

        void Tunnel()
        {
            Feedback(1.045, 0.004, 228);
            double cx = _w / 2.0, cy = _h / 2.0, r0 = Min * 0.07;
            int n = 64;
            for (int k = 0; k < n; k++)
            {
                double a = 2 * Math.PI * k / n + _t * 0.4;
                double r = r0 * (1 + Band(k / (double)n) * 1.8);
                Splat(cx + Math.Cos(a) * r, cy + Math.Sin(a) * r, Pal(k / (double)n), 230);
            }
            if (_beat)
            {
                uint c = Pal(_rnd.NextDouble());
                double r = Min * 0.12;
                for (int k = 0; k < 160; k++)
                {
                    double a = 2 * Math.PI * k / 160;
                    Dot(cx + Math.Cos(a) * r, cy + Math.Sin(a) * r, Math.Max(1, Min / 120), c, 230, true);
                }
            }
        }

        void Radial()
        {
            Fade(70);
            double cx = _w / 2.0, cy = _h / 2.0;
            double r0 = Min * 0.2 * (1 + _level * 0.14), maxLen = Min * 0.29;
            int n = 64;
            double width = Math.Max(1.2, Math.PI * r0 / n * 0.75);
            for (int i = 0; i < n; i++)
            {
                double u = (i + 0.5) / n, v = Band(u);
                uint c = Pal(u);
                foreach (var side in new[] { 1, -1 })
                {
                    double a = -Math.PI / 2 + side * u * Math.PI;
                    double ca = Math.Cos(a), sa = Math.Sin(a);
                    Line(cx + ca * r0, cy + sa * r0, cx + ca * (r0 + 2 + v * maxLen), cy + sa * (r0 + 2 + v * maxLen), c, 230, width);
                }
            }
            double glow = Math.Max(0, 1 - _sinceBeat * 3);
            if (glow > 0) Dot(cx, cy, r0 * 0.95, Pal(0.15), (int)(110 * glow), true);
            for (int k = 0; k < 120; k++)
            {
                double a = 2 * Math.PI * k / 120;
                Splat(cx + Math.Cos(a) * (r0 - 2), cy + Math.Sin(a) * (r0 - 2), Pal(0.3), 120);
            }
        }

        void Mirror()
        {
            Fade(0);
            double cx = _w / 2.0, cy = _h / 2.0, half = _h * 0.46;
            for (int x = 0; x < _w; x++)
            {
                double u = Math.Abs(x - cx) / cx;
                double v = Band(0.02 + u * 0.82) * (1 - u * 0.2);
                int hgt = (int)(v * half);
                for (int dy = -hgt; dy <= hgt; dy++)
                {
                    double t = Math.Abs(dy) / half;
                    Over(x, (int)(cy + dy), Pal(0.15 + 0.85 * t + u * 0.1), (int)(235 * (1 - t * 0.55)));
                }
                Plus(x, (int)cy, Pal(0.9), 60);
            }
        }

        record struct Spark(double X, double Y, double Vx, double Vy, double Life, double Max, double Size, double C);
        readonly List<Spark> _parts = new();

        void Particles(double dt)
        {
            Fade(165);
            double cx = _w / 2.0, cy = _h / 2.0, scale = Min / 200.0;
            int emit = (int)(_level * 7) + (_beat ? 70 : 0);
            for (int i = 0; i < emit && _parts.Count < 900; i++)
            {
                double a = _rnd.NextDouble() * Math.PI * 2, sp = (0.6 + _rnd.NextDouble() * (1.5 + _level * 4) + (_beat ? 2 : 0)) * scale;
                double life = 0.8 + _rnd.NextDouble() * 1.4;
                _parts.Add(new Spark(cx, cy, Math.Cos(a) * sp, Math.Sin(a) * sp, life, life, (0.6 + _rnd.NextDouble() * 1.6) * scale, _rnd.NextDouble()));
            }
            for (int i = _parts.Count - 1; i >= 0; i--)
            {
                var p = _parts[i];
                p.X += p.Vx * dt * 60; p.Y += p.Vy * dt * 60;
                p.Vx *= 0.985; p.Vy *= 0.985;
                p.Life -= dt;
                if (p.Life <= 0 || p.X < -5 || p.Y < -5 || p.X > _w + 5 || p.Y > _h + 5) { _parts.RemoveAt(i); continue; }
                _parts[i] = p;
                Dot(p.X, p.Y, p.Size, Pal(p.C), (int)(230 * p.Life / p.Max), true);
            }
            Dot(cx, cy, Min * 0.06 * (1 + Group(0, 0.15) * 1.5), Pal(0.2), 70, true);
        }

        void Spectrogram()
        {
            int step = _w > 500 ? 2 : 1;
            for (int y = 0; y < _h; y++) Array.Copy(_px, y * _w + step, _px, y * _w, _w - step);
            for (int y = 0; y < _h; y++)
            {
                double u = 1 - y / (double)(_h - 1);
                double v = Band(u);
                uint c = Pal(v);
                int a = (int)(255 * Math.Pow(v, 0.8));
                uint p = (uint)(a << 24) | (uint)((((c >> 16) & 255) * a / 255) << 16) | (uint)((((c >> 8) & 255) * a / 255) << 8) | (uint)((c & 255) * a / 255);
                for (int s = 0; s < step; s++) _px[y * _w + _w - 1 - s] = p;
            }
        }

        void Neon()
        {
            Fade(118);
            double cy = _h / 2.0;
            var groups = new[] { Group(0, 0.12), Group(0.12, 0.35), Group(0.35, 0.6), Group(0.6, 1) };
            for (int k = 0; k < 4; k++)
            {
                double amp = groups[k] * _h * 0.4 + _h * 0.015, freq = 1.2 + k * 0.9, ph = _t * (0.9 + k * 0.55) + k;
                uint c = Pal(k / 3.0);
                double px = 0, py = cy;
                for (int x = 0; x <= _w; x += 2)
                {
                    double env = Math.Sin(Math.PI * x / _w);
                    double y = cy + Math.Sin(x / (double)_w * 2 * Math.PI * freq + ph) * amp * env;
                    if (x > 0)
                    {
                        Line(px, py, x, y, c, 40, Math.Max(2, Min / 50));
                        Line(px, py, x, y, c, 210);
                    }
                    px = x; py = y;
                }
            }
        }

        readonly double[] _blobR = new double[128];

        void Blob()
        {
            Fade(0);
            // 1.9: капля сильнее слушает звук — размер и деформация от громкости, удар раздувает её
            double kick = Math.Max(0, 1 - _sinceBeat / 0.3) * Math.Clamp(St.VizBeatFx, 0, 200) / 100.0;
            double cx = _w / 2.0, cy = _h / 2.0, r0 = Min * (0.15 + Math.Min(1, _level * 1.3) * 0.15 + kick * 0.04);
            var g = new double[6];
            for (int j = 0; j < 6; j++) g[j] = Group(j / 6.0, (j + 1) / 6.0);
            for (int k = 0; k < _blobR.Length; k++)
            {
                double th = 2 * Math.PI * k / _blobR.Length, r = 1;
                for (int j = 0; j < 6; j++) r += g[j] * 0.32 * Math.Sin(th * (j + 2) + _t * (0.7 + j * 0.3) + j);
                _blobR[k] = r0 * r;
            }
            double box = r0 * 1.9;
            int x0 = (int)Math.Max(0, cx - box), x1 = (int)Math.Min(_w - 1, cx + box), y0 = (int)Math.Max(0, cy - box), y1 = (int)Math.Min(_h - 1, cy + box);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    double dx = x - cx, dy = y - cy, d = Math.Sqrt(dx * dx + dy * dy);
                    double th = Math.Atan2(dy, dx);
                    if (th < 0) th += 2 * Math.PI;
                    double f = th / (2 * Math.PI) * _blobR.Length;
                    int i0 = (int)f % _blobR.Length, i1 = (i0 + 1) % _blobR.Length;
                    double r = _blobR[i0] + (_blobR[i1] - _blobR[i0]) * (f - Math.Floor(f));
                    if (d < r)
                    {
                        double t = d / r;
                        Over(x, y, Pal(0.1 + t * 0.85), (int)(255 * (0.3 + 0.7 * t) * (0.45 + 0.55 * Math.Min(1, _level * 1.5))));
                    }
                    else if (d < r + 7)
                        Plus(x, y, Pal(1), (int)(90 * (1 - (d - r) / 7)));
                }
        }
    }
}
