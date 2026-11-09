using System;
using System.Collections.Generic;

namespace NIsland.Controls
{
    // =====================================================================
    //  Визуализаторы 2.2: 17 новых — 8 объёмных (3D) и 9 плоских (2D).
    //  Все держатся за звук: бас, середина, верха и удары (проверка — стенд --selftest viz).
    // =====================================================================
    public partial class Visualizer
    {
        const string G3 = "3D";
        const string G2 = "2D";

        static StyleInfo[] Styles4 => new StyleInfo[]
        {
            new("cube", "Куб в кубе", "Вложенные каркасные кубы вращаются, бас раздувает их, вершины выталкивает частотами", G3),
            new("torus", "Звуковой тор", "Бублик из частиц крутится, по кругу его разгоняет спектр", G3),
            new("warp", "Варп-прыжок", "Звёзды летят на тебя — громче быстрее, на ударе прыжок в гиперпространство", G3),
            new("gyro", "Гироскоп", "Кольца-орбиты наклонены в 3D, каждое вращается своей полосой частот", G3),
            new("tube3d", "Тоннель частиц", "Летишь сквозь закрученную трубу из точек, середина звука её крутит", G3),
            new("bars3d", "3D-столбики", "Поле столбиков в перспективе медленно поворачивается, высота — история спектра", G3),
            new("icosa", "Икосаэдр", "Двадцатигранник вращается, из вершин бьют лучи по частотам", G3),
            new("waves3d", "Звуковая гладь", "Поверхность воды в 3D: удары расходятся кругами, спектр поднимает волны", G3),
            new("mandala", "Мандала", "Симметричные лепестки раскрываются и вращаются по спектру", G2),
            new("fireworks", "Фейерверк", "На каждый удар взлетает ракета и рассыпается искрами", G2),
            new("pianoroll", "Пианоролл", "Частоты как клавиши: ноты бегут слева направо", G2),
            new("rainfall", "Звуковой ливень", "Капли падают там, где звучит частота, и разбиваются брызгами", G2),
            new("radar", "Радар", "Луч обходит экран, частоты оставляют отметки как цели", G2),
            new("polygons", "Многоугольники", "Вложенные треугольник, квадрат, шестиугольник крутятся и дышат", G2),
            new("bounce", "Мячики", "Каждая полоса частот подбрасывает свой мячик", G2),
            new("circlescope", "Круговой осциллограф", "Форма волны свёрнута в кольцо со светящимся следом", G2),
            new("constellation", "Созвездие", "Звёзды плывут под музыку и соединяются линиями, когда сближаются", G2),
        };

        void ResetState4()
        {
            _warp = null; _gyroA = null; _bars3d.Clear(); _fw.Clear(); _sparks.Clear(); _rain4.Clear(); _blips.Clear();
            _balls = null; _stars4 = null; _roll.Clear(); _waveT.Clear(); _tubeZ = 0; _polyA = 0; _radarA = 0; _mandA = 0;
        }

        bool Draw4(double dt)
        {
            switch (_style)
            {
                case "cube": Cube(); return true;
                case "torus": Torus(); return true;
                case "warp": Warp(dt); return true;
                case "gyro": Gyro(dt); return true;
                case "tube3d": Tube3D(dt); return true;
                case "bars3d": Bars3D(); return true;
                case "icosa": Icosa(); return true;
                case "waves3d": Waves3D(dt); return true;
                case "mandala": Mandala(dt); return true;
                case "fireworks": Fireworks(dt); return true;
                case "pianoroll": PianoRoll(); return true;
                case "rainfall": Rainfall(dt); return true;
                case "radar": Radar(dt); return true;
                case "polygons": Polygons(dt); return true;
                case "bounce": Bounce(dt); return true;
                case "circlescope": CircleScope(); return true;
                case "constellation": Constellation(dt); return true;
            }
            return false;
        }

        // ---------- общее для 3D: поворот и перспектива ----------
        (double X, double Y, double Z) Rot(double x, double y, double z, double ay, double ax)
        {
            double ca = Math.Cos(ay), sa = Math.Sin(ay), cb = Math.Cos(ax), sb = Math.Sin(ax);
            double x1 = x * ca + z * sa, z1 = -x * sa + z * ca;
            double y2 = y * cb - z1 * sb, z2 = y * sb + z1 * cb;
            return (x1, y2, z2);
        }

        (double X, double Y, double P) Proj(double x, double y, double z, double scale, double dist = 3.2)
        {
            double p = dist / (dist + z);
            return (_w / 2.0 + x * scale * p, _h / 2.0 + y * scale * p, p);
        }

        // ---------- 1. Куб в кубе ----------
        static readonly int[,] CubeEdges = { { 0, 1 }, { 1, 3 }, { 3, 2 }, { 2, 0 }, { 4, 5 }, { 5, 7 }, { 7, 6 }, { 6, 4 }, { 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 } };
        void Cube()
        {
            Fade(110);
            double ay = _t * (0.5 + _level * 1.5), ax = _t * 0.37 + Kick * 0.4;
            for (int layer = 0; layer < 3; layer++)
            {
                double s = (0.55 - layer * 0.15) * (1 + Bass * 0.5 + Kick * 0.18) * (layer == 1 ? 1 + Mids * 0.4 : 1);
                var pts = new (double X, double Y, double P)[8];
                for (int i = 0; i < 8; i++)
                {
                    double x = (i & 1) == 0 ? -1 : 1, y = (i & 2) == 0 ? -1 : 1, z = (i & 4) == 0 ? -1 : 1;
                    double v = Band(i / 8.0 * 0.9);
                    var r = Rot(x * s * (1 + v * 0.35), y * s * (1 + v * 0.35), z * s * (1 + v * 0.35), ay * (layer % 2 == 0 ? 1 : -1.3), ax);
                    pts[i] = Proj(r.X, r.Y, r.Z, Min * 0.42);
                }
                uint c = Pal(0.2 + layer * 0.3 + _level * 0.3);
                for (int e = 0; e < 12; e++)
                {
                    var a = pts[CubeEdges[e, 0]]; var b = pts[CubeEdges[e, 1]];
                    Line(a.X, a.Y, b.X, b.Y, c, (int)(150 + 105 * _level), 1.5 + Bass * 2);
                }
                foreach (var p in pts) Dot(p.X, p.Y, (2 + Highs * 5) * p.P, Pal(0.9), (int)(120 + 135 * Highs), true);
            }
        }

        // ---------- 2. Звуковой тор ----------
        void Torus()
        {
            Fade(70);
            int ring = Math.Clamp((int)(70 * Dens), 36, 120), tube = 14;
            double R = 0.62, ay = _t * 0.45, ax = 0.9 + Math.Sin(_t * 0.31) * 0.3;
            for (int i = 0; i < ring; i++)
            {
                double u = i / (double)ring * Math.PI * 2;
                double v = Band(Math.Abs(i / (double)ring * 2 - 1) * 0.9);
                double r = 0.18 + v * 0.22 + Kick * 0.05;
                for (int j = 0; j < tube; j++)
                {
                    double w = j / (double)tube * Math.PI * 2 + _t * (1 + _level * 3);
                    double x = (R + r * Math.Cos(w)) * Math.Cos(u), z = (R + r * Math.Cos(w)) * Math.Sin(u), y = r * Math.Sin(w);
                    var q = Rot(x, y, z, ay, ax);
                    var p = Proj(q.X, q.Y, q.Z, Min * 0.62);
                    double near = Math.Clamp(1 - (q.Z + 1) / 2, 0, 1);
                    Dot(p.X, p.Y, (1.2 + v * 2.5) * p.P * Min / 220, Pal(0.15 + v * 0.8), (int)((110 + 145 * v) * (0.45 + 0.55 * near)), true);
                }
            }
        }

        // ---------- 3. Варп-прыжок ----------
        double[] _warp;
        void Warp(double dt)
        {
            Fade(_beat ? 40 : 120);
            int n = Math.Clamp((int)(420 * Dens), 200, 900);
            if (_warp == null || _warp.Length != n * 3)
            {
                _warp = new double[n * 3];
                for (int i = 0; i < n; i++) { _warp[i * 3] = _rnd.NextDouble() * 2 - 1; _warp[i * 3 + 1] = _rnd.NextDouble() * 2 - 1; _warp[i * 3 + 2] = _rnd.NextDouble() * 4 + 0.1; }
            }
            double speed = (0.4 + _level * 3.2 + Kick * 6) * dt;
            double cx = _w / 2.0, cy = _h / 2.0, f = Min * 0.5;
            for (int i = 0; i < n; i++)
            {
                double x = _warp[i * 3], y = _warp[i * 3 + 1], z = _warp[i * 3 + 2];
                double z1 = z - speed;
                if (z1 < 0.05) { _warp[i * 3] = _rnd.NextDouble() * 2 - 1; _warp[i * 3 + 1] = _rnd.NextDouble() * 2 - 1; _warp[i * 3 + 2] = 4; continue; }
                _warp[i * 3 + 2] = z1;
                double sx0 = cx + x / z * f, sy0 = cy + y / z * f, sx1 = cx + x / z1 * f, sy1 = cy + y / z1 * f;
                double v = Band(Math.Min(1, Math.Sqrt(x * x + y * y) / 1.2));
                int a = (int)Math.Clamp(255 * (1 - z1 / 4), 30, 255);
                Line(sx0, sy0, sx1, sy1, Pal(0.3 + v * 0.7), a, 1 + (4 - z1) * 0.4);
            }
        }

        // ---------- 4. Гироскоп ----------
        double[] _gyroA;
        void Gyro(double dt)
        {
            Fade(90);
            int rings = 7;
            _gyroA ??= new double[rings];
            for (int k = 0; k < rings; k++)
            {
                double v = Group(k / (double)rings, (k + 1) / (double)rings);
                _gyroA[k] += dt * (0.3 + v * 4) * (k % 2 == 0 ? 1 : -1);
                double r = 0.25 + k * 0.1 + Kick * 0.03, tilt = 0.4 + k * 0.33;
                int seg = 72;
                (double X, double Y, double P) prev = default;
                for (int s = 0; s <= seg; s++)
                {
                    double th = s / (double)seg * Math.PI * 2;
                    var q = Rot(Math.Cos(th) * r, 0, Math.Sin(th) * r, _gyroA[k], tilt + Math.Sin(_t * 0.2) * 0.3);
                    var p = Proj(q.X, q.Y, q.Z, Min * 0.62);
                    if (s > 0) Line(prev.X, prev.Y, p.X, p.Y, Pal(k / (double)rings), (int)(70 + 185 * v), 1 + v * 2.5);
                    prev = p;
                }
                // планета на кольце
                var pq = Rot(Math.Cos(_gyroA[k] * 3) * r, 0, Math.Sin(_gyroA[k] * 3) * r, _gyroA[k], tilt);
                var pp = Proj(pq.X, pq.Y, pq.Z, Min * 0.62);
                Dot(pp.X, pp.Y, (3 + v * 7) * pp.P, Pal(0.95), 230, true);
            }
            Dot(_w / 2.0, _h / 2.0, Min * (0.03 + Bass * 0.05), Pal(0.9), (int)(150 + 100 * Bass), true);
        }

        // ---------- 5. Тоннель частиц ----------
        double _tubeZ;
        void Tube3D(double dt)
        {
            Fade(60);
            _tubeZ += dt * (0.8 + _level * 4 + Kick * 4);
            int rings = 24, per = Math.Clamp((int)(28 * Dens), 16, 48);
            double twist = _t * (0.3 + Mids * 2.5);
            for (int r = 0; r < rings; r++)
            {
                double z = ((r - _tubeZ % 1) / rings) * 6 + 0.25;
                if (z < 0.35) continue;
                double ringV = Band(r / (double)rings);
                for (int i = 0; i < per; i++)
                {
                    double th = i / (double)per * Math.PI * 2 + twist + r * 0.18;
                    double rad = 1 + ringV * 0.35 + Kick * 0.1;
                    double x = Math.Cos(th) * rad, y = Math.Sin(th) * rad * 0.8;
                    double sx = _w / 2.0 + x / z * Min * 0.5, sy = _h / 2.0 + y / z * Min * 0.5;
                    int a = (int)Math.Clamp(255 * (1 - z / 6.3), 0, 255);
                    Dot(sx, sy, Math.Max(0.8, 3.2 / z), Pal((r + _tubeZ) % rings / rings), a, true);
                }
            }
        }

        // ---------- 6. 3D-столбики ----------
        readonly List<float[]> _bars3d = new();
        void Bars3D()
        {
            Fade(0);
            int cols = Math.Clamp((int)(14 * Dens), 10, 22), rows = 10;
            var row = new float[cols];
            for (int i = 0; i < cols; i++) row[i] = Band(i / (double)(cols - 1) * 0.9);
            _bars3d.Insert(0, row);
            while (_bars3d.Count > rows) _bars3d.RemoveAt(_bars3d.Count - 1);
            double ay = Math.Sin(_t * 0.25) * 0.5, ax = 0.55;
            for (int r = _bars3d.Count - 1; r >= 0; r--)
                for (int i = 0; i < cols; i++)
                {
                    double x = (i / (double)(cols - 1) - 0.5) * 1.8, z = r / (double)rows * 1.6 - 0.4;
                    double hgt = _bars3d[r][i] * 0.9;
                    var b = Rot(x, 0.45, z, ay, ax); var t = Rot(x, 0.45 - hgt, z, ay, ax);
                    var pb = Proj(b.X, b.Y, b.Z, Min * 0.75); var pt = Proj(t.X, t.Y, t.Z, Min * 0.75);
                    int a = (int)(90 + 165 * (1 - r / (double)rows));
                    Line(pb.X, pb.Y, pt.X, pt.Y, Pal(0.15 + _bars3d[r][i] * 0.8), a, Math.Clamp(5 * pb.P * 14 / cols, 1.5, 5));
                    Dot(pt.X, pt.Y, 2.2 * pt.P, Pal(0.95), a, true);
                }
        }

        // ---------- 7. Икосаэдр ----------
        static readonly double[][] IcoV = MakeIco();
        static double[][] MakeIco()
        {
            double p = (1 + Math.Sqrt(5)) / 2;
            var v = new List<double[]>();
            foreach (var a in new[] { -1.0, 1.0 }) foreach (var b in new[] { -p, p }) { v.Add(new[] { 0, a, b }); v.Add(new[] { a, b, 0 }); v.Add(new[] { b, 0, a }); }
            foreach (var x in v) { double l = Math.Sqrt(x[0] * x[0] + x[1] * x[1] + x[2] * x[2]); x[0] /= l; x[1] /= l; x[2] /= l; }
            return v.ToArray();
        }
        void Icosa()
        {
            Fade(100);
            double ay = _t * (0.4 + _level), ax = _t * 0.23;
            double s = 0.42 * (1 + Bass * 0.35 + Kick * 0.12);
            var pts = new (double X, double Y, double P)[12];
            var raw = new (double X, double Y, double Z)[12];
            for (int i = 0; i < 12; i++) { raw[i] = Rot(IcoV[i][0] * s, IcoV[i][1] * s, IcoV[i][2] * s, ay, ax); pts[i] = Proj(raw[i].X, raw[i].Y, raw[i].Z, Min * 0.8); }
            for (int i = 0; i < 12; i++)
                for (int j = i + 1; j < 12; j++)
                {
                    double dx = IcoV[i][0] - IcoV[j][0], dy = IcoV[i][1] - IcoV[j][1], dz = IcoV[i][2] - IcoV[j][2];
                    if (dx * dx + dy * dy + dz * dz < 1.2) Line(pts[i].X, pts[i].Y, pts[j].X, pts[j].Y, Pal(0.35 + _level * 0.4), (int)(130 + 110 * Mids), 1.4);
                }
            for (int i = 0; i < 12; i++)
            {
                double v = Band(i / 12.0 * 0.95);
                var tip = Proj(raw[i].X * (1 + v * 1.2), raw[i].Y * (1 + v * 1.2), raw[i].Z * (1 + v * 1.2), Min * 0.8);
                Line(pts[i].X, pts[i].Y, tip.X, tip.Y, Pal(0.6 + v * 0.4), (int)(80 + 175 * v), 1 + v * 3);
                Dot(tip.X, tip.Y, 2 + v * 6, Pal(0.95), (int)(100 + 155 * v), true);
            }
        }

        // ---------- 8. Звуковая гладь ----------
        readonly List<double> _waveT = new();
        void Waves3D(double dt)
        {
            Fade(0);
            if (_beat) _waveT.Add(0);
            for (int i = _waveT.Count - 1; i >= 0; i--) { _waveT[i] += dt; if (_waveT[i] > 3) _waveT.RemoveAt(i); }
            int n = Math.Clamp((int)(30 * Dens), 18, 44);
            double ax = 0.95, ay = _t * 0.12;
            for (int r = 0; r < n; r++)
            {
                (double X, double Y, double P) prev = default;
                for (int c = 0; c < n; c++)
                {
                    double x = c / (double)(n - 1) * 2 - 1, z = r / (double)(n - 1) * 2 - 1;
                    double d = Math.Sqrt(x * x + z * z);
                    double y = Band(Math.Min(1, d)) * 0.25 * Math.Sin(d * 9 - _t * 3);
                    foreach (var t in _waveT) { double front = t * 0.9; y += Math.Exp(-Math.Pow((d - front) * 7, 2)) * 0.18 * (1 - t / 3); }
                    var q = Rot(x, -y, z, ay, ax);
                    var p = Proj(q.X, q.Y, q.Z, Min * 0.7);
                    if (c > 0) Line(prev.X, prev.Y, p.X, p.Y, Pal(0.3 + Math.Abs(y) * 3), (int)(60 + 160 * (1 - r / (double)n * 0.5)), 1);
                    prev = p;
                }
            }
        }

        // ---------- 9. Мандала ----------
        double _mandA;
        void Mandala(double dt)
        {
            Fade(80);
            _mandA += dt * (0.2 + Mids * 1.5);
            double cx = _w / 2.0, cy = _h / 2.0;
            int petals = 8, layers = 5;
            for (int l = 0; l < layers; l++)
            {
                double v = Group(l / (double)layers, (l + 1) / (double)layers);
                double R = Min * (0.1 + l * 0.08) * (1 + v * 0.8 + Kick * 0.1);
                for (int p = 0; p < petals * (l % 2 + 1); p++)
                {
                    double th = p / (double)(petals * (l % 2 + 1)) * Math.PI * 2 + _mandA * (l % 2 == 0 ? 1 : -1);
                    double prevX = cx, prevY = cy;
                    for (int s = 1; s <= 12; s++)
                    {
                        double k = s / 12.0, w = Math.Sin(k * Math.PI) * 0.35 * (0.5 + v);
                        double a = th + w, rr = R * k;
                        double x = cx + Math.Cos(a) * rr, y = cy + Math.Sin(a) * rr;
                        Line(prevX, prevY, x, y, Pal(l / (double)layers + v * 0.3), (int)(90 + 165 * v), 1.2);
                        prevX = x; prevY = y;
                    }
                    Dot(prevX, prevY, 1.5 + v * 4, Pal(0.9), (int)(120 + 135 * v), true);
                }
            }
        }

        // ---------- 10. Фейерверк ----------
        readonly List<(double X, double Y, double Vy, double Hue)> _fw = new();
        readonly List<(double X, double Y, double Vx, double Vy, double Life, double Hue)> _sparks = new();
        void Fireworks(double dt)
        {
            Fade(140);
            if (_beat || (_level > 0.55 && _rnd.NextDouble() < dt * 2)) _fw.Add((_w * (0.15 + _rnd.NextDouble() * 0.7), _h, -_h * (1.1 + _level), _rnd.NextDouble()));
            for (int i = _fw.Count - 1; i >= 0; i--)
            {
                var (x, y, vy, hue) = _fw[i];
                y += vy * dt; vy += _h * 1.6 * dt;
                Dot(x, y, 2.5, Pal(hue), 230, true);
                if (vy >= -_h * 0.05)
                {
                    int n = 40 + (int)(Bass * 60);
                    for (int k = 0; k < n; k++)
                    {
                        double a = k / (double)n * Math.PI * 2, sp = _h * (0.25 + _rnd.NextDouble() * 0.35) * (0.7 + _level);
                        _sparks.Add((x, y, Math.Cos(a) * sp, Math.Sin(a) * sp, 1, hue + _rnd.NextDouble() * 0.15));
                    }
                    _fw.RemoveAt(i);
                }
                else _fw[i] = (x, y, vy, hue);
            }
            for (int i = _sparks.Count - 1; i >= 0; i--)
            {
                var (x, y, vx, vy, life, hue) = _sparks[i];
                x += vx * dt; y += vy * dt; vy += _h * 0.5 * dt; vx *= 1 - dt * 1.2; life -= dt * (0.6 + Highs);
                if (life <= 0) { _sparks.RemoveAt(i); continue; }
                Dot(x, y, 1.6 + life * 1.5, Pal(hue % 1), (int)(255 * life), true);
                _sparks[i] = (x, y, vx, vy, life, hue);
            }
            if (_sparks.Count > 3000) _sparks.RemoveRange(0, _sparks.Count - 3000);
        }

        // ---------- 11. Пианоролл ----------
        readonly List<float[]> _roll = new();
        void PianoRoll()
        {
            Fade(0);
            int keys = Math.Clamp((int)(36 * Dens), 24, 60), cols = 90;
            var col = new float[keys];
            for (int k = 0; k < keys; k++) col[k] = Band(k / (double)(keys - 1));
            _roll.Insert(0, col);
            while (_roll.Count > cols) _roll.RemoveAt(_roll.Count - 1);
            double kh = _h / (double)keys, cw = _w * 0.88 / cols, keyW = _w * 0.1;
            for (int k = 0; k < keys; k++)
            {
                bool black = (k % 12) is 1 or 3 or 6 or 8 or 10;
                double y = _h - (k + 1) * kh;
                float v = col[k];
                FillRect(0, (int)y, (int)keyW, (int)(y + kh - 1), _ => v > 0.45 ? Pal(0.3 + v * 0.7) : black ? 0x181818u : 0x5A5A5Au, (int)(60 + 195 * v));
                for (int c = 0; c < _roll.Count; c++)
                {
                    float val = _roll[c][k];
                    if (val < 0.3) continue;
                    double x = keyW + 2 + c * cw;
                    FillRect((int)x, (int)y + 1, (int)(x + cw), (int)(y + kh - 1), _ => Pal(0.2 + k / (double)keys * 0.8), (int)(255 * (val - 0.3) * (1 - c / (double)cols * 0.6)));
                }
            }
        }

        // ---------- 12. Звуковой ливень ----------
        readonly List<(double X, double Y, double V, double Hue)> _rain4 = new();
        void Rainfall(double dt)
        {
            Fade(130);
            int lanes = Math.Clamp((int)(48 * Dens), 24, 96);
            for (int i = 0; i < lanes; i++)
            {
                double u = i / (double)(lanes - 1), v = Band(u);
                if (_rnd.NextDouble() < v * dt * 26 + (_beat ? 0.3 : 0)) _rain4.Add(((i + 0.5) * _w / lanes, 0, _h * (0.6 + v * 1.4), u));
            }
            for (int i = _rain4.Count - 1; i >= 0; i--)
            {
                var (x, y, vel, hue) = _rain4[i];
                double y1 = y + vel * dt;
                Line(x, y, x, Math.Max(y1, y + 6), Pal(0.2 + hue * 0.8), 235, 2.2);
                if (y1 >= _h - 2)
                {
                    for (int k = 0; k < 5; k++) _sparks.Add((x, _h - 2, (_rnd.NextDouble() - 0.5) * 120, -_rnd.NextDouble() * 140 * (0.5 + _level), 0.6, hue));
                    _rain4.RemoveAt(i);
                }
                else _rain4[i] = (x, y1, vel + _h * dt, hue);
            }
            for (int i = _sparks.Count - 1; i >= 0; i--)
            {
                var (x, y, vx, vy, life, hue) = _sparks[i];
                x += vx * dt; y += vy * dt; vy += 400 * dt; life -= dt * 1.5;
                if (life <= 0 || y > _h) { _sparks.RemoveAt(i); continue; }
                Dot(x, y, 1.2, Pal(0.2 + hue * 0.8), (int)(255 * life / 0.6), true);
                _sparks[i] = (x, y, vx, vy, life, hue);
            }
            if (_rain4.Count > 1500) _rain4.RemoveRange(0, _rain4.Count - 1500);
        }

        // ---------- 13. Радар ----------
        double _radarA;
        readonly List<(double A, double R, double Life, double Hue)> _blips = new();
        void Radar(double dt)
        {
            Fade(150);
            double cx = _w / 2.0, cy = _h / 2.0, R = Min * 0.46;
            _radarA += dt * (1.2 + _level * 3);
            double lit = 0.25 + 0.75 * Math.Min(1, _level * 1.4);
            for (int k = 1; k <= 4; k++) Circle(cx, cy, R * k / 4 * (1 + Kick * 0.03), Pal(0.3), (int)(50 * lit));
            Line(cx - R, cy, cx + R, cy, Pal(0.3), (int)(40 * lit)); Line(cx, cy - R, cx, cy + R, Pal(0.3), (int)(40 * lit));
            for (int s = 0; s < 18; s++)
            {
                double a = _radarA - s * 0.03;
                Line(cx, cy, cx + Math.Cos(a) * R, cy + Math.Sin(a) * R, Pal(0.6), (int)(220 * (1 - s / 18.0) * lit), 1.5 + Bass * 2);
            }
            // частота, звучащая сейчас, — цель на угле луча
            double u = (_radarA / (Math.PI * 2)) % 1, v = Band(u);
            if (v > 0.35) _blips.Add((_radarA, R * (1 - v * 0.8), 1, u));
            for (int i = _blips.Count - 1; i >= 0; i--)
            {
                var (a, r, life, hue) = _blips[i];
                life -= dt * 0.35;
                if (life <= 0) { _blips.RemoveAt(i); continue; }
                Dot(cx + Math.Cos(a) * r, cy + Math.Sin(a) * r, 2.5 + Kick * 4, Pal(0.3 + hue * 0.7), (int)(255 * life), true);
                _blips[i] = (a, r, life, hue);
            }
        }

        // ---------- 14. Многоугольники ----------
        double _polyA;
        void Polygons(double dt)
        {
            Fade(110);
            _polyA += dt * (0.3 + _level * 2);
            double cx = _w / 2.0, cy = _h / 2.0;
            for (int sides = 3; sides <= 9; sides++)
            {
                int k = sides - 3;
                double v = Group(k / 7.0, (k + 1) / 7.0);
                double R = Min * (0.07 + k * 0.055) * (1 + v * 0.6 + Kick * 0.08);
                double rot = _polyA * (k % 2 == 0 ? 1 : -1) * (1 + k * 0.15);
                for (int s = 0; s < sides; s++)
                {
                    double a0 = rot + s * Math.PI * 2 / sides, a1 = rot + (s + 1) * Math.PI * 2 / sides;
                    Line(cx + Math.Cos(a0) * R, cy + Math.Sin(a0) * R, cx + Math.Cos(a1) * R, cy + Math.Sin(a1) * R, Pal(k / 7.0), (int)(110 + 145 * v), 1.5 + v * 2.5);
                }
            }
        }

        // ---------- 15. Мячики ----------
        double[] _balls;   // [y, vy] на полосу
        void Bounce(double dt)
        {
            Fade(120);
            int n = Math.Clamp((int)(16 * Dens), 8, 32);
            if (_balls == null || _balls.Length != n * 2) _balls = new double[n * 2];
            double floor = _h * 0.92, g = _h * 2.4;
            Line(0, floor + 4, _w, floor + 4, Pal(0.3), 80);
            for (int i = 0; i < n; i++)
            {
                double v = Band(i / (double)(n - 1) * 0.92);
                ref double y = ref _balls[i * 2]; ref double vy = ref _balls[i * 2 + 1];
                if (y == 0) y = floor;
                vy += g * dt; y += vy * dt;
                if (y >= floor) { y = floor; vy = -Math.Sqrt(2 * g * _h * 0.85 * v * v) * (1 + Kick * 0.3); }
                double x = (i + 0.5) * _w / n, r = Math.Min(_w / n * 0.35, _h * 0.06);
                double squash = y >= floor - 1 ? 0.7 : 1;
                Dot(x, y - r * squash, r, Pal(i / (double)n), 235, false);
                Dot(x, y - r * squash, r * 2, Pal(i / (double)n), (int)(50 * v), true);
            }
        }

        // ---------- 16. Круговой осциллограф ----------
        void CircleScope()
        {
            Feedback(1.012 + Kick * 0.02, 0.004 + Mids * 0.01, 200);
            double cx = _w / 2.0, cy = _h / 2.0, R = Min * (0.26 + Bass * 0.08);
            int n = 256;
            double px = 0, py = 0;
            for (int i = 0; i <= n; i++)
            {
                double th = i / (double)n * Math.PI * 2;
                double r = R * (1 + Wave(i % n, n) * 0.6);
                double x = cx + Math.Cos(th) * r, y = cy + Math.Sin(th) * r;
                if (i > 0) Line(px, py, x, y, Pal(0.3 + _level * 0.7), 240, 1.5 + _level * 1.5);
                px = x; py = y;
            }
        }

        // ---------- 17. Созвездие ----------
        double[] _stars4;   // x, y, vx, vy
        void Constellation(double dt)
        {
            Fade(160);
            int n = Math.Clamp((int)(70 * Dens), 40, 140);
            if (_stars4 == null || _stars4.Length != n * 4)
            {
                _stars4 = new double[n * 4];
                for (int i = 0; i < n; i++) { _stars4[i * 4] = _rnd.NextDouble() * _w; _stars4[i * 4 + 1] = _rnd.NextDouble() * _h; _stars4[i * 4 + 2] = _rnd.NextDouble() - 0.5; _stars4[i * 4 + 3] = _rnd.NextDouble() - 0.5; }
            }
            double sp = (20 + _level * 140 + Kick * 160) * dt, link = Min * (0.12 + Bass * 0.12);
            for (int i = 0; i < n; i++)
            {
                ref double x = ref _stars4[i * 4]; ref double y = ref _stars4[i * 4 + 1];
                x += _stars4[i * 4 + 2] * sp; y += _stars4[i * 4 + 3] * sp;
                if (x < 0 || x > _w) _stars4[i * 4 + 2] *= -1;
                if (y < 0 || y > _h) _stars4[i * 4 + 3] *= -1;
                x = Math.Clamp(x, 0, _w); y = Math.Clamp(y, 0, _h);
            }
            for (int i = 0; i < n; i++)
            {
                double v = Band(i / (double)n);
                for (int j = i + 1; j < n; j++)
                {
                    double dx = _stars4[i * 4] - _stars4[j * 4], dy = _stars4[i * 4 + 1] - _stars4[j * 4 + 1], d = Math.Sqrt(dx * dx + dy * dy);
                    if (d < link) Line(_stars4[i * 4], _stars4[i * 4 + 1], _stars4[j * 4], _stars4[j * 4 + 1], Pal(0.3 + v * 0.6), (int)(160 * (1 - d / link) * (0.4 + _level)), 1);
                }
                Dot(_stars4[i * 4], _stars4[i * 4 + 1], 1.5 + v * 4, Pal(0.6 + v * 0.4), (int)(150 + 105 * v), true);
            }
        }
    }
}
