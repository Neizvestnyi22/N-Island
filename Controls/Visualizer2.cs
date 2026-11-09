using System;
using System.Collections.Generic;
using System.Windows.Media;
using NIsland.Services;

namespace NIsland.Controls
{
    // 1.8: ещё 34 стиля (ностальгия и современные), тонкая настройка и пост-обработка
    public partial class Visualizer
    {
        static StyleInfo[] MoreStyles => new StyleInfo[]
        {
            // ностальгия
            new("classic", "Классика 2000-х", "Тонкие столбики с градиентом зелёный–жёлтый–красный и падающими точками, как в плеерах начала 2000-х", "Ностальгия"),
            new("fire", "Пламя", "Звук поджигает нижний край, языки огня поднимаются вверх", "Ностальгия"),
            new("stars", "Звёздное небо", "Полёт сквозь звёзды: скорость растёт с громкостью", "Ностальгия"),
            new("plasma", "Плазма", "Переливы демосцены 90-х, бас раскачивает узор", "Ностальгия"),
            new("vu", "Стрелочные индикаторы", "Два стрелочных VU-метра, как на кассетной деке", "Ностальгия"),
            new("lissajous", "Фигуры Лиссажу", "Осциллограф в режиме X-Y: звук рисует петли", "Ностальгия"),
            new("dotplane", "Точечная плоскость", "Спектр уходит вдаль сеткой светящихся точек", "Ностальгия"),
            new("vortex", "Вихрь", "Закручивающийся калейдоскоп-водоворот на басу", "Ностальгия"),
            new("lightmusic", "Цветомузыка", "Четыре лампы — бас, середина, верха, «тарелки» — как советская цветомузыка", "Ностальгия"),
            new("matrix", "Матрица", "Колонки «кода» падают быстрее там, где громче", "Ностальгия"),
            new("crt", "Экран ЭЛТ", "Осциллограф на ламповом экране: сетка, развёртка и послесвечение", "Ностальгия"),
            new("pixel", "8 бит", "Крупные пиксели, как в играх на приставке", "Ностальгия"),
            new("voice", "Голосовая волна", "Переплетающиеся мягкие волны, как у голосовых помощников", "Ностальгия"),
            // современные
            new("orb", "Сфера", "Светящаяся сфера дышит и деформируется по частотам", "Современные"),
            new("waveform", "Волна", "Плавная заливка формы звука, как в современных плеерах", "Современные"),
            new("circwave", "Кольца волны", "Форма звука по кругу в несколько колец", "Современные"),
            new("galaxy", "Галактика", "Спиральная галактика из тысяч звёзд вращается от баса", "Современные"),
            new("aurora", "Северное сияние", "Занавесы сияния колышутся под середину", "Современные"),
            new("ridges", "Горные гряды", "История спектра хребтами, как на знаменитой обложке альбома", "Современные"),
            new("synthwave", "Ретровейв", "Солнце, неоновая сетка и горы из спектра", "Современные"),
            new("bubbles", "Пузыри", "Пузыри поднимаются со дна по частотам", "Современные"),
            new("ripples", "Круги на воде", "Удары расходятся кругами", "Современные"),
            new("dna", "ДНК", "Двойная спираль, узлы растут по частотам", "Современные"),
            new("lightning", "Молнии", "Разряды бьют на каждый удар", "Современные"),
            new("lava", "Лава-лампа", "Мягкие капли сливаются и всплывают", "Современные"),
            new("kaleido", "Калейдоскоп", "Восьмикратная симметрия из частот", "Современные"),
            new("geometry", "Геометрия", "Вращающиеся вложенные многоугольники", "Современные"),
            new("fountain", "Фонтан", "Струи бьют вверх по частотам", "Современные"),
            new("ecg", "Пульс", "Кардиограмма: удар музыки — удар сердца", "Современные"),
            new("flower", "Цветок", "Лепестки-розы раскрываются под музыку", "Современные"),
            new("hex", "Соты", "Шестиугольники вспыхивают от центра к краям", "Современные"),
        };

        // ------------------------------------------------------------------
        //  Тонкая настройка
        // ------------------------------------------------------------------

        static float Sens => Math.Clamp(St.VizSens, 20, 400) / 100f;
        static double Dens => Math.Clamp(St.VizDensity, 30, 300) / 100.0;
        static double BeatK => Math.Clamp(St.VizBeatFx, 0, 200) / 100.0;
        readonly float[] _sm = new float[AudioCapture.BandCount];
        bool _scan;

        /// <summary>Чувствительность и плавность — до того, как стиль возьмёт полосы.</summary>
        void Tune()
        {
            float sens = Sens;
            float k = Math.Clamp(St.VizSmooth, 0, 90) / 100f;
            for (int i = 0; i < _bands.Length; i++)
            {
                float v = Math.Min(1, _bands[i] * sens);
                // вверх быстрее, вниз плавнее — так пики не «съедаются»
                float kk = v > _sm[i] ? k * 0.5f : k;
                _sm[i] = _sm[i] * kk + v * (1 - kk);
                _bands[i] = _sm[i];
                _peaks[i] = Math.Max(Math.Min(1, _peaks[i] * sens), _bands[i]);
            }
        }

        void ResetState2()
        {
            _heat = null; _stars = null; _hist.Clear(); _drops = null; _rip.Clear(); _gal = null; _scan = false; _ecgX = 0;
        }

        bool Draw2(double dt)
        {
            _scan = _style == "crt";
            switch (_style)
            {
                case "classic": Classic(); return true;
                case "fire": FireStyle(); return true;
                case "stars": Stars(dt); return true;
                case "plasma": Plasma(); return true;
                case "vu": Vu(dt); return true;
                case "lissajous": Lissajous(); return true;
                case "dotplane": DotPlane(); return true;
                case "vortex": Vortex(); return true;
                case "lightmusic": LightMusic(dt); return true;
                case "matrix": MatrixRain(dt); return true;
                case "crt": Crt(); return true;
                case "pixel": Pixel(); return true;
                case "voice": Voice(); return true;
                case "orb": Orb(); return true;
                case "waveform": Waveform(); return true;
                case "circwave": CircWave(); return true;
                case "galaxy": Galaxy(dt); return true;
                case "aurora": Aurora(); return true;
                case "ridges": Ridges(); return true;
                case "synthwave": Synthwave(); return true;
                case "bubbles": Bubbles(dt); return true;
                case "ripples": Ripples(dt); return true;
                case "dna": Dna(); return true;
                case "lightning": Lightning(); return true;
                case "lava": Lava(); return true;
                case "kaleido": Kaleido(); return true;
                case "geometry": Geometry(); return true;
                case "fountain": Fountain(dt); return true;
                case "ecg": Ecg(dt); return true;
                case "flower": Flower(); return true;
                case "hex": Hex(); return true;
            }
            return false;
        }

        // ------------------------------------------------------------------
        //  Пост-обработка: свечение, зеркало, вспышка, развёртка, фон
        // ------------------------------------------------------------------

        uint[] _out;
        int[] _bR, _bG, _bB, _tR, _tG, _tB;
        int[] _vigX, _vigY;

        uint[] Post()
        {
            int glow = Math.Clamp(St.VizGlow, 0, 100);
            bool mirror = St.VizMirror;
            string bg = St.VizBg ?? "none";
            double flash = St.VizBeatFlash ? Math.Max(0, 1 - _sinceBeat / 0.15) * BeatK : 0;
            if (glow < 3 && !mirror && bg == "none" && flash <= 0 && !_scan) return _px;
            if (_out == null || _out.Length != _px.Length) _out = new uint[_px.Length];
            int w = _w, h = _h;
            if (mirror)
                for (int y = 0; y < h; y++)
                {
                    int row = y * w;
                    for (int x = 0; x < w; x++) _out[row + x] = _px[row + (x < w / 2 ? x : w - 1 - x)];
                }
            else Array.Copy(_px, _out, _px.Length);

            if (glow >= 3) Bloom(glow / 100.0 * 1.4);

            if (flash > 0)
            {
                int add = (int)(flash * 46);
                for (int i = 0; i < _out.Length; i++) _out[i] = AddRgb(_out[i], add, add, add);
            }
            if (_scan)
                for (int y = 1; y < h; y += 2)
                {
                    int row = y * w;
                    for (int x = 0; x < w; x++) _out[row + x] = Scale(_out[row + x], 165);
                }
            if (bg != "none")
            {
                uint bgc = bg == "art" ? Mul(_lut[30], 120) : 0x0A0A0Fu;
                if (bg == "vignette" && (_vigX == null || _vigX.Length != w || _vigY.Length != h))
                {
                    _vigX = new int[w]; _vigY = new int[h];
                    for (int x = 0; x < w; x++) { double d = (x - w / 2.0) / (w / 2.0); _vigX[x] = (int)(d * d * 128); }
                    for (int y = 0; y < h; y++) { double d = (y - h / 2.0) / (h / 2.0); _vigY[y] = (int)(d * d * 128); }
                }
                uint br = (bgc >> 16) & 255, bgG = (bgc >> 8) & 255, bb = bgc & 255;
                for (int y = 0; y < h; y++)
                {
                    int row = y * w;
                    for (int x = 0; x < w; x++)
                    {
                        uint p = _out[row + x];
                        uint inv = 255 - (p >> 24);
                        uint r = ((p >> 16) & 255) + br * inv / 255, g = ((p >> 8) & 255) + bgG * inv / 255, b = (p & 255) + bb * inv / 255;
                        if (bg == "vignette")
                        {
                            int keep = 255 - Math.Min(200, _vigX[x] + _vigY[y]);
                            r = r * (uint)keep >> 8; g = g * (uint)keep >> 8; b = b * (uint)keep >> 8;
                        }
                        _out[row + x] = 0xFF000000u | Math.Min(255u, r) << 16 | Math.Min(255u, g) << 8 | Math.Min(255u, b);
                    }
                }
            }
            return _out;
        }

        static uint Scale(uint p, int k) =>
            ((p >> 24) * (uint)k >> 8) << 24 | ((((p >> 16) & 255) * (uint)k) >> 8) << 16 | ((((p >> 8) & 255) * (uint)k) >> 8) << 8 | ((p & 255) * (uint)k >> 8);

        static uint Mul(uint rgb, int k) => ((((rgb >> 16) & 255) * (uint)k) >> 8) << 16 | ((((rgb >> 8) & 255) * (uint)k) >> 8) << 8 | ((rgb & 255) * (uint)k >> 8);

        static uint AddRgb(uint p, int r, int g, int b)
        {
            int pr = Math.Min(255, (int)((p >> 16) & 255) + r), pg = Math.Min(255, (int)((p >> 8) & 255) + g), pb = Math.Min(255, (int)(p & 255) + b);
            int a = Math.Max((int)(p >> 24), Math.Max(pr, Math.Max(pg, pb)));
            return (uint)(a << 24 | pr << 16 | pg << 8 | pb);
        }

        /// <summary>Свечение: картинка в 4 раза меньше, размытая, прибавляется светом.</summary>
        void Bloom(double k)
        {
            int w = _w, h = _h, bw = (w + 3) / 4, bh = (h + 3) / 4, n = bw * bh;
            if (_bR == null || _bR.Length != n) { _bR = new int[n]; _bG = new int[n]; _bB = new int[n]; _tR = new int[n]; _tG = new int[n]; _tB = new int[n]; }
            for (int by = 0; by < bh; by++)
                for (int bx = 0; bx < bw; bx++)
                {
                    int r = 0, g = 0, b = 0, c = 0;
                    for (int yy = by * 4; yy < Math.Min(h, by * 4 + 4); yy += 2)
                        for (int xx = bx * 4; xx < Math.Min(w, bx * 4 + 4); xx += 2)
                        {
                            uint p = _out[yy * w + xx];
                            r += (int)((p >> 16) & 255); g += (int)((p >> 8) & 255); b += (int)(p & 255); c++;
                        }
                    int i = by * bw + bx;
                    // светится только яркое — тёмное не превращается в серую дымку
                    _bR[i] = Math.Max(0, r / Math.Max(1, c) - 40); _bG[i] = Math.Max(0, g / Math.Max(1, c) - 40); _bB[i] = Math.Max(0, b / Math.Max(1, c) - 40);
                }
            BoxBlur(_bR, _tR, bw, bh); BoxBlur(_bG, _tG, bw, bh); BoxBlur(_bB, _tB, bw, bh);
            int kk = (int)(k * 256);
            for (int y = 0; y < h; y++)
            {
                int by = Math.Min(bh - 1, y >> 2), row = y * w;
                for (int x = 0; x < w; x++)
                {
                    int i = by * bw + Math.Min(bw - 1, x >> 2);
                    int r = _bR[i] * kk >> 8, g = _bG[i] * kk >> 8, b = _bB[i] * kk >> 8;
                    if ((r | g | b) != 0) _out[row + x] = AddRgb(_out[row + x], r, g, b);
                }
            }
        }

        static void BoxBlur(int[] a, int[] t, int w, int h)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        int s = 0, c = 0;
                        for (int d = -2; d <= 2; d++) { int xx = x + d; if ((uint)xx < (uint)w) { s += a[y * w + xx]; c++; } }
                        t[y * w + x] = s / c;
                    }
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        int s = 0, c = 0;
                        for (int d = -2; d <= 2; d++) { int yy = y + d; if ((uint)yy < (uint)h) { s += t[yy * w + x]; c++; } }
                        a[y * w + x] = s / c;
                    }
            }
        }

        // ------------------------------------------------------------------
        //  Помощники
        // ------------------------------------------------------------------

        void Put(int x, int y, uint rgb, int a)
        {
            if ((uint)x >= (uint)_w || (uint)y >= (uint)_h) return;
            a = Math.Clamp(a, 0, 255);
            _px[y * _w + x] = (uint)(a << 24) | (((rgb >> 16) & 255) * (uint)a / 255) << 16 | (((rgb >> 8) & 255) * (uint)a / 255) << 8 | (rgb & 255) * (uint)a / 255;
        }

        void Block(int x0, int y0, int x1, int y1, uint rgb, int a)
        {
            x0 = Math.Max(0, x0); y0 = Math.Max(0, y0); x1 = Math.Min(_w, x1); y1 = Math.Min(_h, y1);
            for (int y = y0; y < y1; y++) for (int x = x0; x < x1; x++) Over(x, y, rgb, a);
        }

        void Circle(double cx, double cy, double r, uint rgb, int a)
        {
            int n = Math.Clamp((int)(r * 6.3), 12, 900);
            for (int k = 0; k < n; k++) { double t = 2 * Math.PI * k / n; Splat(cx + Math.Cos(t) * r, cy + Math.Sin(t) * r, rgb, a); }
        }

        static uint Lerp(uint a, uint b, double t)
        {
            t = Math.Clamp(t, 0, 1);
            uint C(int sh) => (uint)(((a >> sh) & 255) + (((b >> sh) & 255) - (double)((a >> sh) & 255)) * t);
            return C(16) << 16 | C(8) << 8 | C(0);
        }

        static int Hash(int x, int y) { unchecked { int h = x * 374761393 + y * 668265263; h = (h ^ (h >> 13)) * 1274126177; return h ^ (h >> 16); } }
        static double Rnd01(int x, int y) => (Hash(x, y) & 0xFFFF) / 65535.0;

        // ------------------------------------------------------------------
        //  Ностальгия
        // ------------------------------------------------------------------

        static uint ClassicColor(double t) => t < 0.55 ? Lerp(0x00B000, 0x40E000, t / 0.55) : t < 0.8 ? Lerp(0x40E000, 0xFFE000, (t - 0.55) / 0.25) : Lerp(0xFFE000, 0xFF2000, (t - 0.8) / 0.2);

        void Classic()
        {
            Fade(0);
            int n = (int)Math.Clamp(_w / 6.0 * Dens, 12, 160);
            double cw = _w / (double)n;
            int bw = Math.Max(1, (int)Math.Round(cw * 0.72));
            bool classic = _palette == "art";
            for (int i = 0; i < n; i++)
            {
                double u = (i + 0.5) / n;
                double v = Band(u), pk = Band(u, _peaks);
                int x0 = (int)(i * cw + (cw - bw) / 2), top = (int)(_h * (1 - v));
                for (int y = Math.Max(0, top); y < _h; y++)
                {
                    double t = 1 - y / (double)_h;
                    uint c = classic ? ClassicColor(t) : Pal(0.15 + 0.85 * t);
                    for (int x = x0; x < x0 + bw; x++) Over(x, y, c, 255);
                }
                int py = Math.Max(0, (int)(_h * (1 - pk)) - 2);
                for (int x = x0; x < x0 + bw; x++) { Over(x, py, classic ? 0xC8C8C8u : Pal(1), 255); Over(x, py + 1, classic ? 0x909090u : Pal(0.9), 255); }
            }
        }

        float[] _heat;
        int _frame;

        void FireStyle()
        {
            int w = _w, h = _h;
            if (_heat == null || _heat.Length != w * h) _heat = new float[w * h];
            _frame++;
            float boost = _sinceBeat < 0.1 ? (float)(0.35 * BeatK) : 0;
            for (int x = 0; x < w; x++)
            {
                double u = Math.Abs(x - w / 2.0) / (w / 2.0);
                float v = Band(u * 0.85) * (1.25f + boost);
                float jitter = (float)Rnd01(x, _frame) * 0.35f * v;
                _heat[(h - 1) * w + x] = v + jitter;
                _heat[(h - 2) * w + x] = (v + jitter) * 0.95f;
            }
            float cool = 0.9f / h + 0.004f;
            for (int y = 0; y < h - 2; y++)
            {
                int r1 = (y + 1) * w, r2 = (y + 2) * w, r0 = y * w;
                for (int x = 0; x < w; x++)
                {
                    int xl = x > 0 ? x - 1 : x, xr = x < w - 1 ? x + 1 : x;
                    float s = _heat[r1 + xl] + _heat[r1 + x] + _heat[r1 + xr] + _heat[r2 + x];
                    float c = cool * (0.5f + (float)Rnd01(x + _frame * 3, y));
                    _heat[r0 + x] = Math.Max(0, s / 4.03f - c);
                }
            }
            bool art = _palette == "art";
            for (int i = 0; i < _px.Length; i++)
            {
                float v = _heat[i];
                if (v < 0.01f) { _px[i] = 0; continue; }
                uint c = art ? FireColor(v) : Pal(Math.Min(1, v));
                int a = (int)Math.Min(255, v * 420);
                _px[i] = (uint)(a << 24) | (((c >> 16) & 255) * (uint)a / 255) << 16 | (((c >> 8) & 255) * (uint)a / 255) << 8 | (c & 255) * (uint)a / 255;
            }
        }

        static uint FireColor(float v) => v < 0.3f ? Lerp(0x400000, 0xD02000, v / 0.3) : v < 0.65f ? Lerp(0xD02000, 0xFF9A10, (v - 0.3) / 0.35) : Lerp(0xFF9A10, 0xFFFFD0, (v - 0.65) / 0.35);

        struct Star { public double X, Y, Z, C; }
        Star[] _stars;

        void Stars(double dt)
        {
            Fade(105);
            int n = (int)(320 * Dens);
            if (_stars == null || _stars.Length != n)
            {
                _stars = new Star[n];
                for (int i = 0; i < n; i++) _stars[i] = new Star { X = _rnd.NextDouble() * 2 - 1, Y = _rnd.NextDouble() * 2 - 1, Z = _rnd.NextDouble(), C = _rnd.NextDouble() };
            }
            double speed = (0.12 + _level * 1.5 + (_sinceBeat < 0.25 ? 0.9 * BeatK : 0)) * dt;
            double cx = _w / 2.0, cy = _h / 2.0, f = Math.Max(_w, _h) * 0.5;
            for (int i = 0; i < n; i++)
            {
                ref var s = ref _stars[i];
                double pz = s.Z;
                s.Z -= speed;
                double sx = cx + s.X / s.Z * f, sy = cy + s.Y / s.Z * f;
                if (s.Z <= 0.03 || sx < 0 || sy < 0 || sx >= _w || sy >= _h)
                {
                    s.X = _rnd.NextDouble() * 2 - 1; s.Y = _rnd.NextDouble() * 2 - 1; s.Z = 1; s.C = _rnd.NextDouble();
                    continue;
                }
                double px0 = cx + s.X / pz * f, py0 = cy + s.Y / pz * f;
                double b = 1 - s.Z;
                Line(px0, py0, sx, sy, Pal(0.35 + 0.65 * s.C), (int)(255 * b * b + 30));
            }
        }

        void Plasma()
        {
            double bass = Group(0, 0.15), mid = Group(0.2, 0.5);
            // 1.9: бас сильнее «дышит» масштабом, а в тишине плазма почти гаснет — иначе узор жил своей жизнью
            double sc = 7.0 / Min * (1 + bass * 1.4), t = _t;
            double cxs = _w / 2.0 * sc + Math.Sin(t * 0.4) * 2, cys = _h / 2.0 * sc + Math.Cos(t * 0.3) * 2;
            int a = (int)(35 + Math.Min(1, _level * 1.5) * 200 + Math.Max(0, 1 - _sinceBeat / 0.25) * 20 * BeatK);
            for (int y = 0; y < _h; y += 2)
                for (int x = 0; x < _w; x += 2)
                {
                    double fx = x * sc, fy = y * sc;
                    double v = Math.Sin(fx + t) + Math.Sin(fy * 1.3 - t * 1.1) + Math.Sin((fx + fy) * 0.7 + t * 0.6)
                             + Math.Sin(Math.Sqrt((fx - cxs) * (fx - cxs) + (fy - cys) * (fy - cys)) * 1.4 - t * 1.7);
                    double u = (v + 4) / 8 + mid * 0.7 + t * 0.03;
                    u -= Math.Floor(u);
                    u = u < 0.5 ? u * 2 : 2 - u * 2;   // туда-обратно по палитре — без резкой границы
                    uint c = Pal(u);
                    Put(x, y, c, a); Put(x + 1, y, c, a); Put(x, y + 1, c, a); Put(x + 1, y + 1, c, a);
                }
        }

        readonly double[] _needle = new double[2], _needleV = new double[2];

        void Vu(double dt)
        {
            Fade(0);
            double[] target = { Math.Min(1.05, Group(0, 0.45) * 1.15), Math.Min(1.05, Group(0.2, 1) * 1.35) };
            bool wide = _w >= _h * 1.3;
            bool art = _palette == "art";
            for (int m = 0; m < 2; m++)
            {
                // стрелка с инерцией и лёгким перелётом, как настоящая
                double acc = (target[m] - _needle[m]) * 120 - _needleV[m] * 14;
                _needleV[m] += acc * Math.Min(dt, 0.05);
                _needle[m] = Math.Clamp(_needle[m] + _needleV[m] * Math.Min(dt, 0.05), 0, 1.08);
                double x0 = wide ? m * _w / 2.0 : 0, y0 = wide ? 0 : m * _h / 2.0, mw = wide ? _w / 2.0 : _w, mh = wide ? _h : _h / 2.0;
                double pad = Math.Min(mw, mh) * 0.06;
                uint face = art ? 0xEBDDB0u : Mul(Pal(0.2), 140), ink = art ? 0x1A1A1Au : Pal(0.95);
                // 2.2: подсветка шкалы живёт от громкости — в тишине тусклая, на пиках тёплая и яркая
                double glow = 0.3 + 0.7 * Math.Min(1, _needle[m] * 1.1);
                Block((int)(x0 + pad), (int)(y0 + pad), (int)(x0 + mw - pad), (int)(y0 + mh - pad), face, (int)(255 * glow));
                int leds = 12;
                for (int k = 0; k < leds; k++)
                {
                    double t = (k + 0.5) / leds;
                    bool on = t <= _needle[m];
                    double lx = x0 + pad * 2 + t * (mw - pad * 4);
                    Dot(lx, y0 + mh - pad * 1.1, pad * 0.32, t > 0.75 ? 0xFF3030u : t > 0.55 ? 0xFFC020u : 0x30E060u, on ? 255 : 25, false);
                }
                double px = x0 + mw / 2, py = y0 + mh - pad * 2.2, R = Math.Min(mw * 0.42, (mh - pad * 3) * 0.95);
                for (int k = 0; k <= 40; k++)
                {
                    double t = k / 40.0, a = -Math.PI / 2 + (-0.85 + 1.7 * t);
                    bool red = t > 0.72;
                    double r1 = R * (k % 4 == 0 ? 0.84 : 0.9);
                    Line(px + Math.Cos(a) * r1, py + Math.Sin(a) * r1, px + Math.Cos(a) * R, py + Math.Sin(a) * R, red ? 0xE02020u : ink, 230, red ? 2 : 1);
                }
                double na = -Math.PI / 2 + (-0.85 + 1.7 * Math.Min(1, _needle[m]));
                Line(px, py, px + Math.Cos(na) * R * 0.98, py + Math.Sin(na) * R * 0.98, art ? 0x101010u : 0xFFFFFFu, 255, Math.Max(1.5, R / 60));
                Dot(px, py, R * 0.07, art ? 0x303030u : Pal(0.5), 255, false);
                if (_needle[m] > 0.92) Dot(x0 + mw - pad * 3, y0 + pad * 3, pad * 0.9, 0xFF3030, 255, false);
            }
        }

        void Lissajous()
        {
            Fade(160);
            UpdateGain();
            int n = AudioCapture.WaveCount, d = n / 4;
            double cx = _w / 2.0, cy = _h / 2.0, r = Min * 0.44;
            double px = 0, py = 0;
            for (int i = 0; i < n - d; i++)
            {
                double x = cx + Math.Clamp(Wave(i, n), -1.2, 1.2) * r, y = cy - Math.Clamp(Wave(i + d, n), -1.2, 1.2) * r;
                if (i > 0) Line(px, py, x, y, Pal(i / (double)n), 170);
                px = x; py = y;
            }
        }

        readonly List<float[]> _hist = new();

        void PushHist(int cols, int max)
        {
            var row = new float[cols];
            for (int c = 0; c < cols; c++) row[c] = Band(c / (double)(cols - 1));
            _hist.Insert(0, row);
            while (_hist.Count > max) _hist.RemoveAt(_hist.Count - 1);
        }

        void DotPlane()
        {
            Fade(0);
            int cols = (int)Math.Clamp(40 * Dens, 16, 96);
            if (_hist.Count == 0 || _hist[0].Length != cols) _hist.Clear();
            PushHist(cols, 36);
            double cx = _w / 2.0;
            for (int r = _hist.Count - 1; r >= 0; r--)
            {
                double z = 1 + r * 0.11, s = 1 / z;
                var row = _hist[r];
                for (int c = 0; c < cols; c++)
                {
                    double xw = (c / (double)(cols - 1) - 0.5) * 2;
                    double x = cx + xw * _w * 0.5 * s, yb = _h * 0.3 + _h * 0.62 * s, y = yb - row[c] * _h * 0.38 * s;
                    Dot(x, y, Math.Max(1.2, Min / 70 * s), Pal(0.2 + row[c] * 0.8), (int)(255 * Math.Min(1, s * 1.3)), true);
                }
            }
        }

        void Vortex()
        {
            float bass = Group(0, 0.15);
            Feedback(1.03 + bass * 0.045, 0.025 + bass * 0.09 * BeatK, 238, 0.5 + 0.06 * Math.Sin(_t * 0.7), 0.5 + 0.06 * Math.Cos(_t * 0.9));
            double cx = _w / 2.0, cy = _h / 2.0;
            int n = 120;
            double px = 0, py = 0;
            for (int k = 0; k <= n; k++)
            {
                double a = 2 * Math.PI * k / n + _t * 0.8, r = Min * (0.06 + Band((k % n) / (double)n) * 0.26);
                double x = cx + Math.Cos(a) * r, y = cy + Math.Sin(a) * r;
                if (k > 0) Line(px, py, x, y, Pal((k / (double)n + _t * 0.1) % 1), 200);
                px = x; py = y;
            }
        }

        readonly double[] _lamp = new double[4];

        void LightMusic(double dt)
        {
            Fade(0);
            double[] v = { Group(0, 0.1), Group(0.12, 0.35), Group(0.35, 0.65), Group(0.65, 1) };
            uint[] classic = { 0xFF2020, 0xFFC000, 0x20E040, 0x3070FF };
            bool art = _palette == "art";
            bool row = _w >= _h;
            for (int i = 0; i < 4; i++)
            {
                double target = Math.Pow(Math.Min(1, v[i] * 1.3), 1.3);
                // как лампа накаливания: вспыхивает сразу, гаснет плавно
                _lamp[i] = target > _lamp[i] ? target : _lamp[i] + (target - _lamp[i]) * Math.Min(1, dt * 5);
                double x = row ? (i + 0.5) * _w / 4 : _w / 2.0, y = row ? _h / 2.0 : (i + 0.5) * _h / 4;
                double r = (row ? Math.Min(_w / 8.5, _h * 0.42) : Math.Min(_h / 8.5, _w * 0.42)) * (0.9 + _lamp[i] * 0.3);
                uint c = art ? classic[i] : Pal(0.15 + i * 0.27);
                Dot(x, y, r * 1.6, c, (int)(30 + 140 * _lamp[i]), true);
                Dot(x, y, r, c, (int)(25 + 230 * _lamp[i]), false);
                Dot(x, y, r * 0.4, Lerp(c, 0xFFFFFF, 0.6), (int)(200 * _lamp[i]), true);
            }
        }

        double[] _drops;

        void MatrixRain(double dt)
        {
            Fade(212);
            int cell = (int)Math.Max(4, Min / (20 * Dens));
            int cols = Math.Max(4, _w / cell);
            if (_drops == null || _drops.Length != cols) { _drops = new double[cols]; for (int c = 0; c < cols; c++) _drops[c] = -_rnd.NextDouble() * _h / cell; }
            int s = Math.Max(1, cell / 4);
            for (int c = 0; c < cols; c++)
            {
                double v = Band(c / (double)cols);
                _drops[c] += (0.35 + v * 2.2) * dt * 22;
                if (_drops[c] * cell > _h + cell * 6) _drops[c] = -_rnd.NextDouble() * 12;
                int row = (int)_drops[c];
                int bits = Hash(c, row);
                int x0 = c * cell, y0 = row * cell;
                uint head = _palette == "art" ? 0xD8FFD8u : Pal(1), body = _palette == "art" ? 0x30FF60u : Pal(0.7);
                for (int by = 0; by < 4; by++)
                    for (int bx = 0; bx < 3; bx++)
                        if ((bits >> (by * 3 + bx) & 1) != 0) Block(x0 + bx * s, y0 + by * s, x0 + bx * s + s, y0 + by * s + s, head, 255);
                // предыдущий символ подкрашиваем в цвет «хвоста»
                int pb = Hash(c, row - 1);
                for (int by = 0; by < 4; by++)
                    for (int bx = 0; bx < 3; bx++)
                        if ((pb >> (by * 3 + bx) & 1) != 0) Block(x0 + bx * s, y0 - cell + by * s, x0 + bx * s + s, y0 - cell + by * s + s, body, 200);
            }
        }

        void Crt()
        {
            Fade(170);
            UpdateGain();
            uint c = _palette == "art" ? 0x40FF70u : Pal(0.85);
            uint grid = _palette == "art" ? 0x103018u : Mul(Pal(0.4), 80);
            for (int k = 1; k < 10; k++) { int x = k * _w / 10; for (int y = 0; y < _h; y += 2) Plus(x, y, grid, 40); }
            for (int k = 1; k < 8; k++) { int y = k * _h / 8; for (int x = 0; x < _w; x += 2) Plus(x, y, grid, 40); }
            double px = 0, py = _h / 2.0, th = Math.Max(1.5, Min / 60);
            for (int x = 0; x < _w; x++)
            {
                double y = _h / 2.0 - Wave(x, _w) * _h * 0.4;
                if (x > 0) { Line(px, py, x, y, c, 50, th * 2.5); Line(px, py, x, y, Lerp(c, 0xFFFFFF, 0.5), 220); }
                px = x; py = y;
            }
        }

        void Pixel()
        {
            Fade(0);
            int cell = (int)Math.Max(4, Min / (11 * Dens));
            int cols = Math.Max(4, _w / cell), rows = Math.Max(4, _h / cell);
            for (int c = 0; c < cols; c++)
            {
                double u = (c + 0.5) / cols;
                int lit = (int)Math.Round(Band(u) * rows), pk = (int)Math.Round(Band(u, _peaks) * rows);
                for (int r = 0; r < lit; r++)
                {
                    double t = Math.Floor(r / (double)rows * 4) / 3;   // четыре ступени цвета, как у старых приставок
                    Block(c * cell + 1, _h - (r + 1) * cell + 1, (c + 1) * cell - 1, _h - r * cell - 1, Pal(0.2 + 0.8 * t), 255);
                }
                if (pk > lit) Block(c * cell + 1, _h - pk * cell + 1, (c + 1) * cell - 1, _h - (pk - 1) * cell - 1, Pal(1), 255);
            }
        }

        void Voice()
        {
            Fade(0);
            double cy = _h / 2.0;
            for (int k = 0; k < 5; k++)
            {
                double amp = (0.06 + Group(k * 0.2, (k + 1) * 0.2) * 1.3) * _h * 0.42, freq = 1.3 + k * 0.55, ph = _t * (1.4 + k * 0.45) + k * 1.7;
                uint c = Pal(0.2 + k / 5.0);
                for (int x = 0; x < _w; x++)
                {
                    double u = x / (double)_w * 2 - 1, env = Math.Pow(Math.Max(0, 1 - u * u), 2);
                    double y = cy + Math.Sin(u * Math.PI * freq + ph) * amp * env;
                    int y0 = (int)Math.Min(y, cy), y1 = (int)Math.Max(y, cy);
                    for (int yy = y0; yy <= y1; yy++) Plus(x, yy, c, 26);
                    Splat(x, y, c, 220);
                }
            }
        }

        // ------------------------------------------------------------------
        //  Современные
        // ------------------------------------------------------------------

        void Orb()
        {
            Fade(0);
            double cx = _w / 2.0, cy = _h / 2.0;
            // 1.9: сфера заметно растёт с громкостью и вздрагивает на удар, край сильнее деформируется частотами
            double R = Min * (0.15 + Math.Min(1, _level * 1.3) * 0.15 + Math.Max(0, 1 - _sinceBeat * 4) * 0.04 * BeatK);
            var g = new double[8];
            for (int j = 0; j < 8; j++) g[j] = Group(j / 8.0, (j + 1) / 8.0);
            // искажение края — таблицей по углу (раньше 8 синусов на каждый пиксель)
            const int AN = 512;
            var edge = new double[AN + 1];
            for (int i = 0; i <= AN; i++)
            {
                double th0 = -Math.PI + 2 * Math.PI * i / AN, e0 = 1;
                for (int j = 0; j < 8; j++) e0 += g[j] * 0.2 * Math.Sin(th0 * (j + 3) + _t * (1 + j * 0.3) + j);
                edge[i] = e0;
            }
            double box = R * 2;
            int x0 = (int)Math.Max(0, cx - box), x1 = (int)Math.Min(_w - 1, cx + box), y0 = (int)Math.Max(0, cy - box), y1 = (int)Math.Min(_h - 1, cy + box);
            double lx = cx - R * 0.35, ly = cy - R * 0.4;
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    double dx = x - cx, dy = y - cy, d = Math.Sqrt(dx * dx + dy * dy) / R, th = Math.Atan2(dy, dx);
                    double e = edge[(int)((th + Math.PI) / (2 * Math.PI) * AN)];
                    double dd = d / e;
                    if (dd < 1)
                    {
                        double shade = 1 - dd * dd;
                        double sx = x - lx, sy = y - ly, spec = Math.Max(0, 1 - Math.Sqrt(sx * sx + sy * sy) / (R * 0.55));
                        uint c = Lerp(Pal(0.25 + 0.6 * shade), 0xFFFFFF, spec * spec * 0.7);
                        Over(x, y, c, 255);
                    }
                    else
                    {
                        double halo = Math.Exp(-(dd - 1) * 5) * (0.35 + _level * 0.8);
                        if (halo > 0.02) Plus(x, y, Pal(0.85), (int)(200 * halo));
                    }
                }
        }

        void Waveform()
        {
            Fade(0);
            UpdateGain();
            double cy = _h / 2.0;
            int n = AudioCapture.WaveCount;
            for (int x = 0; x < _w; x++)
            {
                int i0 = x * n / _w, i1 = Math.Max(i0 + 1, (x + 1) * n / _w + 2);
                double a = 0;
                for (int i = i0; i < Math.Min(n, i1); i++) a = Math.Max(a, Math.Abs(Wave(i, n)));
                int hgt = (int)(Math.Min(1, a) * _h * 0.46);
                for (int dy = -hgt; dy <= hgt; dy++)
                {
                    double t = hgt == 0 ? 0 : Math.Abs(dy) / (double)hgt;
                    Over(x, (int)(cy + dy), Pal(0.35 + 0.65 * (1 - t)), (int)(235 * (1 - t * 0.5)));
                }
            }
        }

        void CircWave()
        {
            Fade(140);
            UpdateGain();
            double cx = _w / 2.0, cy = _h / 2.0, r0 = Min * 0.22;
            int n = 200;
            for (int ring = 0; ring < 3; ring++)
            {
                double px = 0, py = 0, rr = r0 * (1 + ring * 0.3);
                uint c = Pal((ring / 3.0 + _t * 0.04) % 1);
                for (int k = 0; k <= n; k++)
                {
                    double a = 2 * Math.PI * k / n + _t * (0.2 + ring * 0.15) * (ring % 2 == 0 ? 1 : -1);
                    double r = rr * (1 + Math.Clamp(Wave((k + ring * n / 3) % n, n), -1, 1) * 0.28);
                    double x = cx + Math.Cos(a) * r, y = cy + Math.Sin(a) * r;
                    if (k > 0) Line(px, py, x, y, c, 190);
                    px = x; py = y;
                }
            }
        }

        struct GalP { public double R, A, C; }
        GalP[] _gal;

        void Galaxy(double dt)
        {
            Fade(140);
            int n = (int)(700 * Dens);
            if (_gal == null || _gal.Length != n)
            {
                _gal = new GalP[n];
                for (int i = 0; i < n; i++)
                {
                    double r = Math.Pow(_rnd.NextDouble(), 0.7);
                    int arm = _rnd.Next(3);
                    _gal[i] = new GalP { R = r, A = arm * 2 * Math.PI / 3 + r * 4.5 + (_rnd.NextDouble() - 0.5) * 0.6, C = _rnd.NextDouble() };
                }
            }
            double bass = Group(0, 0.15);
            double cx = _w / 2.0, cy = _h / 2.0, sx = _w * 0.47, sy = _h * 0.44;
            for (int i = 0; i < n; i++)
            {
                ref var p = ref _gal[i];
                p.A += dt * (0.25 + bass * 1.6) * (1.25 - p.R);
                double b = Band(p.R);
                double x = cx + Math.Cos(p.A) * p.R * sx, y = cy + Math.Sin(p.A) * p.R * sy;
                Dot(x, y, 0.6 + b * 1.8 * Min / 200, Pal(p.R * 0.75 + b * 0.25), (int)(90 + 165 * b), true);
            }
            Dot(cx, cy, Min * 0.08, Pal(0.95), (int)(70 + _level * 160), true);
        }

        void Aurora()
        {
            Fade(150);
            for (int k = 0; k < 30; k++) Plus((int)(Rnd01(k, 3) * _w), (int)(Rnd01(k, 4) * _h * 0.9), 0xFFFFFF, 60);
            for (int x = 0; x < _w; x++)
            {
                double u = x / (double)_w;
                double yc = _h * (0.32 + 0.1 * Math.Sin(x * 0.013 + _t * 0.4) + 0.06 * Math.Sin(x * 0.031 - _t * 0.7));
                double I = Band(u) * 1.25 + 0.12, L = _h * (0.22 + I * 0.38);
                int ys = (int)(yc - L * 0.2), ye = (int)(yc + L);
                for (int y = Math.Max(0, ys); y < Math.Min(_h, ye); y++)
                {
                    double t = (y - ys) / (L * 1.2);
                    double a = I * Math.Pow(1 - t, 1.6) * (t < 0.12 ? t / 0.12 : 1);
                    Plus(x, y, Pal(0.25 + 0.65 * (1 - t)), (int)(a * 95));
                }
            }
        }

        int _histTick;

        void Ridges()
        {
            Fade(0);
            int cols = (int)Math.Clamp(64 * Dens, 24, 140);
            if (_hist.Count == 0 || _hist[0].Length != cols) _hist.Clear();
            if (++_histTick % 2 == 0 || _hist.Count == 0) PushHist(cols, 30);
            int rows = _hist.Count;
            double spacing = _h * 0.72 / 30;
            uint line = _palette == "art" ? 0xF2F2F2u : Pal(0.9);
            for (int r = rows - 1; r >= 0; r--)
            {
                double baseY = _h * 0.2 + (rows - 1 - r) * spacing + (30 - rows) * spacing;
                var row = _hist[r];
                double px = 0, py = baseY;
                for (int x = 0; x < _w; x++)
                {
                    double u = x / (double)(_w - 1);
                    double center = 1 - Math.Abs(u - 0.5) * 2;
                    double env = Math.Pow(Math.Max(0, center), 1.4);
                    double f = (Math.Abs(u - 0.5) * 2) * (cols - 1);
                    int i0 = (int)f; double k = f - i0;
                    double v = row[Math.Min(i0, cols - 1)] * (1 - k) + row[Math.Min(i0 + 1, cols - 1)] * k;
                    double y = baseY - v * _h * 0.22 * env - env * _h * 0.01;
                    for (int yy = (int)y + 1; yy <= (int)baseY + 1; yy++) Put(x, yy, 0x000000, 255);   // закрываем хребты позади
                    if (x > 0) Line(px, py, x, y, line, (int)(70 + 185 * Math.Min(1, v * 2.5 + env * 0.15)));   // 2.2: хребты горят там, где звучит
                    px = x; py = y;
                }
            }
        }

        double _synthMove;

        void Synthwave()
        {
            double horizon = _h * 0.56;
            int sky = (int)(70 + 130 * Math.Min(1, _level * 1.5));   // небо светлеет с громкостью
            for (int y = 0; y < (int)horizon; y++)
            {
                uint c = Mul(Pal(0.05 + 0.4 * y / horizon), sky);
                for (int x = 0; x < _w; x++) _px[y * _w + x] = 0xFF000000u | c;
            }
            double bass = Group(0, 0.12);
            // 1.9: солнце пульсирует басом, сетка разгоняется громкостью, горы выше
            double kick = Math.Max(0, 1 - _sinceBeat / 0.3) * BeatK;
            double sx = _w / 2.0, R = Min * 0.3 * (0.85 + bass * 0.35 + kick * 0.08), scy = horizon - R * 0.15;
            for (int y = (int)Math.Max(0, scy - R); y < (int)horizon; y++)
            {
                double dy = y - scy, t = (y - (scy - R)) / (R * 1.15);
                double half = Math.Sqrt(Math.Max(0, R * R - dy * dy));
                bool gap = t > 0.45 && ((y - scy + _t * 8) % (R / 7) + R / 7) % (R / 7) < R / 7 * (t - 0.4) * 0.9;   // полосы внизу солнца
                if (gap) continue;
                uint c = Lerp(_palette == "art" ? 0xFFE36Au : Pal(1), _palette == "art" ? 0xFF2A8Au : Pal(0.5), t);
                for (int x = (int)(sx - half); x < (int)(sx + half); x++) Put(x, y, c, 255);
            }
            for (int x = 0; x < _w; x++)
            {
                double u = Math.Abs(x / (double)_w - 0.5) * 2;
                int mh = (int)((Band(0.05 + u * 0.7) * 1.3 + 0.03 + 0.03 * Math.Sin(x * 0.07)) * _h * 0.3);
                for (int y = (int)horizon - mh; y < (int)horizon; y++) Put(x, y, Mul(Pal(0.1), 90), 255);
            }
            uint floor = 0x07020F, grid = _palette == "art" ? 0xFF3AD0u : Pal(0.9);
            for (int y = (int)horizon; y < _h; y++) for (int x = 0; x < _w; x++) _px[y * _w + x] = 0xFF000000u | floor;
            int lines = 14;
            _synthMove += (0.004 + Math.Min(1, _level * 1.5) * 0.02);
            double move = _synthMove;
            for (int k = 0; k < lines; k++)
            {
                double p = (k / (double)lines + move) % 1;
                int y = (int)(horizon + (_h - horizon) * p * p);
                for (int x = 0; x < _w; x++) Plus(x, y, grid, (int)((50 + 160 * p) * (0.5 + 0.5 * Math.Min(1, _level * 1.5)) + kick * 60));
            }
            for (int k = -12; k <= 12; k++)
            {
                double xb = sx + k * _w / 9.0;
                Line(sx + (xb - sx) * 0.04, horizon, xb, _h, grid, 150);
            }
        }

        void Bubbles(double dt)
        {
            Fade(0);
            double scale = Min / 200.0;
            for (int b = 0; b < 12; b++)
            {
                double v = Band(b / 12.0);
                if (_rnd.NextDouble() < v * 0.4 * Dens && _parts.Count < 400)
                    _parts.Add(new Spark((b + _rnd.NextDouble()) / 12 * _w, _h + 6, 0, -(0.5 + _rnd.NextDouble() * 0.8) * scale, 1, 1, (2 + v * 9) * scale, b / 12.0));
            }
            for (int i = _parts.Count - 1; i >= 0; i--)
            {
                var p = _parts[i];
                p.Y += p.Vy * dt * 60;
                p.X += Math.Sin(_t * 2 + i) * 0.3 * scale;
                if (p.Y < -p.Size * 2) { _parts.RemoveAt(i); continue; }
                _parts[i] = p;
                uint c = Pal(0.3 + p.C * 0.7);
                Dot(p.X, p.Y, p.Size, c, 45, true);
                Circle(p.X, p.Y, p.Size, c, 200);
                Dot(p.X - p.Size * 0.35, p.Y - p.Size * 0.35, Math.Max(0.8, p.Size * 0.22), 0xFFFFFF, 200, true);
            }
        }

        readonly List<(double X, double Y, double R, double Life, double C)> _rip = new();

        void Ripples(double dt)
        {
            Fade(110);
            if (_beat || _rnd.NextDouble() < _level * 0.05)
            {
                int k = _beat ? 1 + (int)(BeatK * 2) : 1;
                for (int i = 0; i < k && _rip.Count < 40; i++) _rip.Add((_rnd.NextDouble() * _w, _rnd.NextDouble() * _h, 1, 1, _rnd.NextDouble()));
            }
            for (int i = _rip.Count - 1; i >= 0; i--)
            {
                var r = _rip[i];
                r.R += dt * Min * 0.35 * (1 + _level);
                r.Life -= dt * 0.55;
                if (r.Life <= 0) { _rip.RemoveAt(i); continue; }
                _rip[i] = r;
                Circle(r.X, r.Y, r.R, Pal(r.C), (int)(220 * r.Life));
                if (r.R > 8) Circle(r.X, r.Y, r.R * 0.7, Pal(r.C), (int)(110 * r.Life));
            }
        }

        void Dna()
        {
            Fade(80);
            int n = (int)Math.Clamp(28 * Dens, 10, 80);
            double cy = _h / 2.0, r = Math.Max(1.5, Min / 50);
            double p1x = 0, p1y = 0, p2x = 0, p2y = 0;
            for (int i = 0; i < n; i++)
            {
                double x = (i + 0.5) / n * _w, ph = i * 0.45 + _t * 1.6, b = Band(i / (double)n);
                double amp = _h * 0.3 * (0.55 + b * 0.6);
                double y1 = cy + Math.Sin(ph) * amp, y2 = cy - Math.Sin(ph) * amp, z = Math.Cos(ph);
                Line(x, y1, x, y2, Pal(0.5), 50);
                Dot(x, y1, r * (1 + 0.35 * z + b), Pal(i / (double)n), (int)(170 + 80 * z), true);
                Dot(x, y2, r * (1 - 0.35 * z + b), Pal(1 - i / (double)n), (int)(170 - 80 * z), true);
                if (i > 0) { Line(p1x, p1y, x, y1, Pal(0.7), 120); Line(p2x, p2y, x, y2, Pal(0.3), 120); }
                p1x = x; p1y = y1; p2x = x; p2y = y2;
            }
        }

        void Lightning()
        {
            Fade(190);
            // 1.9: между ударами небо не пустое — облака подсвечены громкостью, разряды чаще на громких местах
            for (int x = 0; x < _w; x += 8)
            {
                double v = Band(x / (double)_w);
                Dot(x, _h * 0.04, Min * (0.06 + v * 0.12), Pal(0.15 + v * 0.3), (int)(10 + 50 * Math.Min(1, _level * 1.5)), true);
            }
            if (!(_beat || _rnd.NextDouble() < _level * 0.08)) return;
            var pts = new List<(double X, double Y)> { (_rnd.NextDouble() * _w, 0), (_rnd.NextDouble() * _w, _h) };
            double disp = _w * 0.25;
            for (int depth = 0; depth < 7; depth++)
            {
                var next = new List<(double, double)>();
                for (int i = 0; i < pts.Count - 1; i++)
                {
                    next.Add(pts[i]);
                    next.Add(((pts[i].X + pts[i + 1].X) / 2 + (_rnd.NextDouble() - 0.5) * disp, (pts[i].Y + pts[i + 1].Y) / 2));
                }
                next.Add(pts[^1]);
                pts = next;
                disp *= 0.55;
            }
            uint c = Pal(0.6 + _rnd.NextDouble() * 0.4);
            for (int i = 1; i < pts.Count; i++)
            {
                Line(pts[i - 1].X, pts[i - 1].Y, pts[i].X, pts[i].Y, c, 90, Math.Max(3, Min / 45));
                Line(pts[i - 1].X, pts[i - 1].Y, pts[i].X, pts[i].Y, 0xFFFFFF, 255);
            }
            // ответвление
            int from = _rnd.Next(pts.Count / 3, pts.Count * 2 / 3);
            double bx = pts[from].X, by = pts[from].Y;
            for (int k = 0; k < 12; k++)
            {
                double nx = bx + (_rnd.NextDouble() - 0.3) * _w * 0.05, ny = by + _h * 0.03;
                Line(bx, by, nx, ny, c, 180);
                bx = nx; by = ny;
            }
        }

        readonly double[] _lavaPh = { 0, 1.3, 2.6, 3.9, 5.2, 0.7 };

        void Lava()
        {
            int n = 6;
            var bx = new double[n]; var by = new double[n]; var br = new double[n];
            for (int i = 0; i < n; i++)
            {
                _lavaPh[i] += 0.004 * (1 + _level * 3) * (1 + i * 0.15);
                bx[i] = _w * (0.2 + 0.6 * (0.5 + 0.5 * Math.Sin(_lavaPh[i] * 0.7 + i)));
                by[i] = _h * (0.5 + 0.42 * Math.Sin(_lavaPh[i] + i * 1.1));
                br[i] = Min * (0.09 + Group(i / (double)n, (i + 1) / (double)n) * 0.14);
            }
            for (int y = 0; y < _h; y += 2)
                for (int x = 0; x < _w; x += 2)
                {
                    double f = 0;
                    for (int i = 0; i < n; i++) { double dx = x - bx[i], dy = y - by[i]; f += br[i] * br[i] / (dx * dx + dy * dy + 1); }
                    uint c; int a;
                    if (f > 1) { c = Pal(Math.Min(1, 0.35 + (f - 1) * 0.35)); a = 255; }
                    else if (f > 0.55) { c = Pal(0.3); a = (int)((f - 0.55) / 0.45 * 200); }
                    else { c = 0; a = 0; }
                    Put(x, y, c, a); Put(x + 1, y, c, a); Put(x, y + 1, c, a); Put(x + 1, y + 1, c, a);
                }
        }

        void Kaleido()
        {
            Fade(200);
            double cx = _w / 2.0, cy = _h / 2.0;
            int sym = 8;
            for (int j = 0; j < 12; j++)
            {
                double b = Band(j / 12.0);
                if (b < 0.05) continue;
                double r = Min * 0.46 * (0.12 + b * 0.85), a0 = _t * 0.3 * (j % 2 == 0 ? 1 : -1) + j * 0.37;
                uint c = Pal(j / 12.0);
                for (int s = 0; s < sym; s++)
                    foreach (var sign in new[] { 1, -1 })
                    {
                        double a = s * 2 * Math.PI / sym + sign * a0;
                        Dot(cx + Math.Cos(a) * r, cy + Math.Sin(a) * r, 0.8 + b * Min / 55, c, (int)(110 + 145 * b), true);
                    }
            }
        }

        void Geometry()
        {
            Fade(130);
            double cx = _w / 2.0, cy = _h / 2.0;
            for (int k = 0; k < 6; k++)
            {
                int sides = 3 + k;
                double R = Min * (0.08 + k * 0.065) * (1 + Group(k / 6.0, (k + 1) / 6.0) * 0.7);
                double rot = _t * (0.3 + k * 0.1) * (k % 2 == 0 ? 1 : -1);
                uint c = Pal(k / 5.0);
                for (int s = 0; s < sides; s++)
                {
                    double a1 = rot + s * 2 * Math.PI / sides, a2 = rot + (s + 1) * 2 * Math.PI / sides;
                    Line(cx + Math.Cos(a1) * R, cy + Math.Sin(a1) * R, cx + Math.Cos(a2) * R, cy + Math.Sin(a2) * R, c, 200, 1.6);
                    if (_sinceBeat < 0.3 && sides > 4)
                    {
                        double a3 = rot + (s + 2) * 2 * Math.PI / sides;
                        Line(cx + Math.Cos(a1) * R, cy + Math.Sin(a1) * R, cx + Math.Cos(a3) * R, cy + Math.Sin(a3) * R, c, (int)(120 * (1 - _sinceBeat / 0.3) * BeatK));
                    }
                }
            }
        }

        void Fountain(double dt)
        {
            Fade(150);
            double scale = Min / 200.0, g = Min / 2600.0;
            int jets = 7;
            for (int j = 0; j < jets; j++)
            {
                double v = Band(j / (double)jets);
                int count = (int)(v * 6 * Dens) + (_beat ? (int)(4 * BeatK) : 0);
                for (int k = 0; k < count && _parts.Count < 1200; k++)
                    _parts.Add(new Spark((j + 0.5) / jets * _w, _h, (_rnd.NextDouble() - 0.5) * scale * 0.8, -(0.6 + v * 1.5 + _rnd.NextDouble() * 0.3) * Min / 55, 3, 3, (0.7 + _rnd.NextDouble()) * scale, j / (double)jets));
            }
            for (int i = _parts.Count - 1; i >= 0; i--)
            {
                var p = _parts[i];
                p.Vy += g * dt * 60;
                p.X += p.Vx * dt * 60; p.Y += p.Vy * dt * 60;
                p.Life -= dt;
                if (p.Y > _h + 4 || p.Life <= 0) { _parts.RemoveAt(i); continue; }
                _parts[i] = p;
                Dot(p.X, p.Y, p.Size, Pal(p.C), 220, true);
            }
        }

        double _ecgX, _ecgPrevY;

        void Ecg(double dt)
        {
            Fade(250);
            double cy = _h * 0.55, amp = _h * 0.36 * (0.6 + Group(0, 0.15) * 0.8);
            // «стираем» полоску перед лучом, как на мониторе
            int gap = Math.Max(4, _w / 40);
            for (int x = (int)_ecgX; x < Math.Min(_w, (int)_ecgX + gap); x++) for (int y = 0; y < _h; y++) _px[y * _w + x] = 0;
            double nx = _ecgX + _w / 3.0 * dt;
            double s = _sinceBeat / 0.45;
            double v = s < 0.15 ? 0.12 * Math.Sin(Math.PI * s / 0.15)
                     : s < 0.25 ? 0
                     : s < 0.3 ? -0.15 * (s - 0.25) / 0.05
                     : s < 0.37 ? -0.15 + 1.15 * (s - 0.3) / 0.07
                     : s < 0.44 ? 1 - 1.35 * (s - 0.37) / 0.07
                     : s < 0.5 ? -0.35 + 0.35 * (s - 0.44) / 0.06
                     : s < 0.9 ? 0.25 * Math.Sin(Math.PI * (s - 0.5) / 0.4) : 0;
            double ny = cy - v * amp - (_rnd.NextDouble() - 0.5) * _level * 3;
            uint c = _palette == "art" ? 0x30FF60u : Pal(0.85);
            if (nx < _w) { Line(_ecgX, _ecgPrevY == 0 ? cy : _ecgPrevY, nx, ny, c, 70, Math.Max(2.5, Min / 50)); Line(_ecgX, _ecgPrevY == 0 ? cy : _ecgPrevY, nx, ny, Lerp(c, 0xFFFFFF, 0.5), 255); Dot(nx, ny, Math.Max(1.5, Min / 70), 0xFFFFFF, 255, true); }
            _ecgX = nx >= _w ? 0 : nx;
            _ecgPrevY = nx >= _w ? 0 : ny;
        }

        void Flower()
        {
            Fade(160);
            double cx = _w / 2.0, cy = _h / 2.0;
            for (int layer = 0; layer < 3; layer++)
            {
                double k = 3 + layer * 2 + Math.Round(Math.Sin(_t * 0.07 + layer) * 1);
                double R = Min * 0.44 * (0.35 + Group(layer / 3.0, (layer + 1) / 3.0) * 0.85);
                double rot = _t * 0.2 * (layer + 1) * (layer % 2 == 0 ? 1 : -1);
                double px = 0, py = 0;
                int n = 360;
                for (int i = 0; i <= n; i++)
                {
                    double th = 2 * Math.PI * i / n, r = R * Math.Abs(Math.Cos(k * th / 2));
                    double x = cx + Math.Cos(th + rot) * r, y = cy + Math.Sin(th + rot) * r;
                    if (i > 0) Line(px, py, x, y, Pal((layer / 3.0 + i / (double)n * 0.3) % 1), 150);
                    px = x; py = y;
                }
            }
        }

        void Hex()
        {
            Fade(0);
            double s = Math.Max(5, Min / (9 * Dens));
            double hx = s * 1.5, hy = s * Math.Sqrt(3);
            double cx = _w / 2.0, cy = _h / 2.0, maxD = Math.Sqrt(cx * cx + cy * cy);
            for (int col = -1; col * hx < _w + s; col++)
                for (int row = -1; row * hy < _h + s; row++)
                {
                    double x = col * hx, y = row * hy + (col % 2 != 0 ? hy / 2 : 0);
                    double u = Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / maxD;
                    double v = Band(u);
                    int a = (int)(30 + 225 * v);
                    uint c = Pal(0.15 + 0.85 * v);
                    double r = s * 0.9;
                    for (int dy = (int)-r; dy <= (int)r; dy++)
                    {
                        double ady = Math.Abs(dy);
                        if (ady > r * 0.866) continue;
                        double half = r - ady / Math.Sqrt(3);
                        for (int dx = (int)-half; dx <= (int)half; dx++) Over((int)(x + dx), (int)(y + dy), c, a);
                    }
                }
        }
    }
}
