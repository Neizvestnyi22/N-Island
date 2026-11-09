using System;
using System.Collections.Generic;
using System.Linq;

namespace NIsland.Controls
{
    // =====================================================================
    //  Визуализаторы 1.9: современные стили, которые держатся за звук.
    //  Каждый проверен стендом (--selftest viz): картинка в тишине и под громкий ритм должна заметно отличаться.
    // =====================================================================
    public partial class Visualizer
    {
        const string Neo = "Новые";

        static StyleInfo[] NewStyles => new StyleInfo[]
        {
            new("capsules", "Неоновые капсулы", "Скруглённые светящиеся столбики от центра вверх и вниз с отражением, как в современных плеерах", Neo),
            new("pulsering", "Пульс-кольцо", "Толстое кольцо дышит басом, частоты торчат шипами, на удар — ударная волна", Neo),
            new("sphere", "Сфера из частиц", "Объёмный шар из сотен точек вращается, каждая точка выталкивается своей частотой", Neo),
            new("wire", "Каркасный рельеф", "3D-сетка уходит к горизонту, холмы — история спектра", Neo),
            new("starburst", "Звёздный взрыв", "Лучи из центра длиной по частотам, на удар — вспышка искр", Neo),
            new("mesh", "Градиентные облака", "Мягкие цветные пятна плывут и разгораются от баса, середины и верхов", Neo),
            new("smoke", "Дым", "Звук поднимается вверх клубами светящегося дыма", Neo),
            new("iso", "Объёмный эквалайзер", "Изометрические 3D-столбики с гранями и тенями", Neo),
            new("dotmatrix", "Точечный экран", "Спектр на панели из круглых светодиодов со свечением", Neo),
            new("spiral", "Спиральный спектр", "Частоты разворачиваются по спирали и медленно вращаются", Neo),
            new("laser", "Лазерное шоу", "Лучи из углов качаются под музыку, на удар — стробоскоп", Neo),
            new("comets", "Кометы", "Кометы кружат вокруг центра, громче — быстрее, на удар появляются новые", Neo),
            new("cloth", "Звуковая ткань", "Сетка-полотно колышется волнами спектра", Neo),
            new("glitch", "Глитч", "Цифровые сбои: полосы сдвигаются и распадаются по каналам на ударах", Neo),
            new("beatgrid", "Сетка ударов", "Плитки загораются от ударов и частот, как танцпол", Neo),
            new("orbits", "Орбиты", "Каждое кольцо — своя полоса частот, планеты летят быстрее, когда она звучит", Neo),
            new("lines", "Потоки линий", "Десятки плавных линий изгибаются по спектру, как живой фон песни", Neo),
        };

        /// <summary>Стили, убранные в 1.9 (почти не реагировали на звук), — на что заменить сохранённый выбор.</summary>
        public static string Replacement(string key) => key switch
        {
            "cassette" => "capsules",
            "vinyl" => "spiral",
            "skyline" => "iso",
            _ => key,
        };

        // ---------- общее ----------

        double Bass => Group(0, 0.12);
        double Mids => Group(0.15, 0.5);
        double Highs => Group(0.55, 1);
        /// <summary>Импульс удара: 1 сразу после удара, плавно гаснет за ~0,3 с.</summary>
        double Kick => Math.Max(0, 1 - _sinceBeat / 0.3) * BeatK;

        void ResetState3()
        {
            _sph = null; _comets.Clear(); _wireHist.Clear(); _smokeSeed = 0; _tiles = null; _orb = null; _burst.Clear(); _glitchRows = null;
        }

        bool Draw3(double dt)
        {
            switch (_style)
            {
                case "capsules": Capsules(); return true;
                case "pulsering": PulseRing(); return true;
                case "sphere": Sphere(dt); return true;
                case "wire": Wire(); return true;
                case "starburst": Starburst(dt); return true;
                case "mesh": MeshGradient(); return true;
                case "smoke": Smoke(); return true;
                case "iso": Iso(); return true;
                case "dotmatrix": DotMatrix(); return true;
                case "spiral": Spiral(); return true;
                case "laser": Laser(); return true;
                case "comets": Comets(dt); return true;
                case "cloth": Cloth(); return true;
                case "glitch": Glitch(); return true;
                case "beatgrid": BeatGrid(dt); return true;
                case "orbits": Orbits(dt); return true;
                case "lines": Lines(); return true;
            }
            return false;
        }

        // ---------- 1. Неоновые капсулы ----------
        void Capsules()
        {
            Fade(90);
            int n = Math.Clamp((int)(24 * Dens), 10, 64);
            double slot = _w / (double)n, bw = slot * 0.56, cy = _h * 0.5;
            for (int i = 0; i < n; i++)
            {
                double u = (i + 0.5) / n;
                // симметрично от центра: басы посередине, как у современных плееров
                double v = Band(Math.Abs(u - 0.5) * 2 * 0.85);
                double half = Math.Max(bw * 0.5, v * _h * 0.42 * (1 + Kick * 0.15));
                double x = (i + 0.5) * slot;
                uint c = Pal(0.15 + v * 0.8);
                // капсула: столбик из кружков — края скруглены
                for (double y = cy - half; y <= cy + half; y += Math.Max(1, bw * 0.35))
                {
                    double edge = Math.Abs(y - cy) / Math.Max(1, half);
                    Dot(x, y, bw * 0.5, c, (int)(150 + 105 * (1 - edge * 0.5)), false);
                }
                // мягкое свечение вокруг
                Dot(x, cy, bw * 1.6 * (0.6 + v), c, (int)(40 * v), true);
            }
        }

        // ---------- 2. Пульс-кольцо ----------
        readonly List<(double R, double A)> _burst = new();
        void PulseRing()
        {
            Fade(150);
            double cx = _w / 2.0, cy = _h / 2.0;
            double R = Min * (0.2 + Bass * 0.16 + Kick * 0.05);
            double thick = 2 + _level * Min * 0.05;
            int spikes = Math.Clamp((int)(96 * Dens), 48, 220);
            for (int k = 0; k < spikes; k++)
            {
                double th = 2 * Math.PI * k / spikes - Math.PI / 2;
                double u = Math.Abs(k / (double)spikes * 2 - 1);   // зеркально слева и справа
                double v = Band(u * 0.9);
                double r0 = R, r1 = R + thick + v * Min * 0.28;
                uint c = Pal(0.2 + v * 0.75);
                Line(cx + Math.Cos(th) * r0, cy + Math.Sin(th) * r0, cx + Math.Cos(th) * r1, cy + Math.Sin(th) * r1, c, (int)(120 + 135 * v));
            }
            for (double t = 0; t < thick; t += 0.8) Circle(cx, cy, R - t, Pal(0.85), (int)(140 + 100 * _level));
            if (_beat) _burst.Add((R, 220));
            for (int i = _burst.Count - 1; i >= 0; i--)
            {
                var (r, a) = _burst[i];
                Circle(cx, cy, r, Pal(0.95), (int)a);
                r += Min * 0.025; a *= 0.88;
                if (a < 6 || r > Min) _burst.RemoveAt(i); else _burst[i] = (r, a);
            }
        }

        // ---------- 3. Сфера из частиц ----------
        double[] _sph;
        void Sphere(double dt)
        {
            Fade(60);
            int n = Math.Clamp((int)(700 * Dens), 300, 1600);
            if (_sph == null || _sph.Length != n * 3)
            {
                _sph = new double[n * 3];
                double ga = Math.PI * (3 - Math.Sqrt(5));   // точки по «подсолнуху» — ровно по сфере
                for (int i = 0; i < n; i++)
                {
                    double y = 1 - 2.0 * (i + 0.5) / n, r = Math.Sqrt(1 - y * y), th = ga * i;
                    _sph[i * 3] = Math.Cos(th) * r; _sph[i * 3 + 1] = y; _sph[i * 3 + 2] = Math.Sin(th) * r;
                }
            }
            double cx = _w / 2.0, cy = _h / 2.0, R = Min * (0.3 + Bass * 0.08 + Kick * 0.04);
            double ay = _t * (0.4 + _level * 1.2), ax = 0.35 + Math.Sin(_t * 0.3) * 0.2;
            double ca = Math.Cos(ay), sa = Math.Sin(ay), cb = Math.Cos(ax), sb = Math.Sin(ax);
            for (int i = 0; i < n; i++)
            {
                double x = _sph[i * 3], y = _sph[i * 3 + 1], z = _sph[i * 3 + 2];
                // частота точки — по широте: полюса — верха, экватор — басы
                double v = Band(Math.Abs(y));
                double push = 1 + v * 0.45;
                double x1 = (x * ca + z * sa) * push, z1 = (-x * sa + z * ca) * push, y1 = y * push;
                double y2 = y1 * cb - z1 * sb, z2 = y1 * sb + z1 * cb;
                double p = 2.6 / (2.6 + z2);   // перспектива
                double sx = cx + x1 * R * p, sy = cy + y2 * R * p;
                double near = 1 - (z2 + 1.4) / 2.8;
                int a = (int)((170 + 85 * v) * (0.5 + 0.5 * near));
                Dot(sx, sy, (1.6 + v * 2.4) * (0.6 + 0.6 * near) * Min / 200, Pal(0.2 + v * 0.75), a, true);
            }
        }

        // ---------- 4. Каркасный рельеф ----------
        readonly List<float[]> _wireHist = new();
        void Wire()
        {
            Fade(0);
            int cols = Math.Clamp((int)(28 * Dens), 16, 60), rows = 22;
            var row = new float[cols];
            for (int i = 0; i < cols; i++) row[i] = Band(Math.Abs(i / (double)(cols - 1) * 2 - 1) * 0.85);
            _wireHist.Insert(0, row);
            while (_wireHist.Count > rows) _wireHist.RemoveAt(_wireHist.Count - 1);
            double horizon = _h * 0.3, cx = _w / 2.0;
            (double, double) P(int r, int i)
            {
                double z = 1 + r * 0.32;                       // глубина: ближний ряд — снизу
                double x = (i / (double)(cols - 1) - 0.5) * _w * 1.6 / z;
                double hgt = _wireHist[r][i] * _h * 0.45 / z;
                double y = horizon + (_h - horizon) * 1.05 / z - hgt;
                return (cx + x, y);
            }
            for (int r = _wireHist.Count - 1; r >= 0; r--)
            {
                int a = (int)(60 + 195 * (1 - r / (double)rows));
                for (int i = 0; i < cols; i++)
                {
                    var (x0, y0) = P(r, i);
                    uint c = Pal(0.2 + _wireHist[r][i] * 0.8);
                    if (i + 1 < cols) { var (x1, y1) = P(r, i + 1); Line(x0, y0, x1, y1, c, a); }
                    if (r + 1 < _wireHist.Count) { var (x2, y2) = P(r + 1, i); Line(x0, y0, x2, y2, c, a / 2); }
                }
            }
        }

        // ---------- 5. Звёздный взрыв ----------
        void Starburst(double dt)
        {
            Feedback(1.02 + _level * 0.03, 0.004, 175);
            double cx = _w / 2.0, cy = _h / 2.0;
            int rays = Math.Clamp((int)(72 * Dens), 36, 180);
            double rot = _t * 0.15;
            for (int k = 0; k < rays; k++)
            {
                double th = rot + 2 * Math.PI * k / rays;
                double v = Band((k % (rays / 2)) / (double)(rays / 2));
                double r1 = Min * (0.04 + v * 0.55 + Kick * 0.1);
                Line(cx, cy, cx + Math.Cos(th) * r1, cy + Math.Sin(th) * r1, Pal(0.3 + v * 0.7), (int)(50 + 180 * v));
            }
            if (_beat)
                for (int i = 0; i < 60; i++)
                {
                    double th = _rnd.NextDouble() * Math.PI * 2, sp = Min * (0.01 + _rnd.NextDouble() * 0.03);
                    if (_parts.Count < 600) _parts.Add(new Spark(cx, cy, Math.Cos(th) * sp, Math.Sin(th) * sp, 1, 1, 1, 0.6 + _rnd.NextDouble() * 0.4));
                }
            StepParts(dt);
            Dot(cx, cy, Min * (0.05 + Bass * 0.08), Pal(1), (int)(120 + 135 * _level), true);
        }

        // ---------- 6. Градиентные облака ----------
        void MeshGradient()
        {
            Fade(0);
            // пять мягких пятен: позиции плывут, размер и яркость — от своих частот
            var blobs = new (double X, double Y, double R, double V, double Hue)[5];
            double[] g = { Bass, Group(0.1, 0.25), Mids, Group(0.45, 0.7), Highs };
            for (int i = 0; i < 5; i++)
            {
                double s = _t * (0.12 + i * 0.03) + i * 1.7;
                blobs[i] = (_w * (0.5 + 0.38 * Math.Sin(s * 1.1 + i)), _h * (0.5 + 0.36 * Math.Cos(s * 0.9 + i * 2)),
                            Min * (0.22 + g[i] * 0.42 + (i == 0 ? Kick * 0.12 : 0)), g[i], i / 5.0);
            }
            for (int y = 0; y < _h; y += 2)
                for (int x = 0; x < _w; x += 2)
                {
                    double r = 0, gg = 0, b = 0, wsum = 0.0001;
                    foreach (var bl in blobs)
                    {
                        double dx = x - bl.X, dy = y - bl.Y, w = Math.Max(0, 1 - (dx * dx + dy * dy) / (bl.R * bl.R));
                        w *= w * (0.25 + bl.V * 1.2);
                        uint c = Pal(0.1 + bl.Hue * 0.85);
                        r += ((c >> 16) & 255) * w; gg += ((c >> 8) & 255) * w; b += (c & 255) * w; wsum += w;
                    }
                    double k = Math.Min(1, wsum);
                    uint col = (uint)Math.Min(255, r / wsum) << 16 | (uint)Math.Min(255, gg / wsum) << 8 | (uint)Math.Min(255, b / wsum);
                    int a = (int)(255 * k * (0.35 + 0.65 * Math.Min(1, _level * 1.6)));
                    Put(x, y, col, a); Put(x + 1, y, col, a); Put(x, y + 1, col, a); Put(x + 1, y + 1, col, a);
                }
        }

        // ---------- 7. Дым ----------
        int _smokeSeed;
        void Smoke()
        {
            // эхо вверх с лёгким завихрением: клубы поднимаются
            Feedback(1.0, Math.Sin(_t * 0.7) * 0.003, 232, 0.5, 0.02);
            ShiftUp(2);
            _smokeSeed++;
            int n = Math.Clamp((int)(40 * Dens), 20, 90);
            for (int i = 0; i < n; i++)
            {
                double u = (i + 0.5) / n, v = Band(u);
                if (v < 0.04) continue;
                double x = u * _w + Math.Sin(_t * 2 + i) * 3, y = _h - 2 - Rnd01(i, _smokeSeed) * 4;
                Dot(x, y, 2 + v * Min * 0.08, Pal(0.2 + v * 0.8), (int)(60 + 160 * v), true);
            }
        }

        /// <summary>Сдвинуть картинку вверх на k пикселей (низ очищается).</summary>
        void ShiftUp(int k)
        {
            if (k <= 0 || k >= _h) return;
            Array.Copy(_px, k * _w, _px, 0, (_h - k) * _w);
            Array.Clear(_px, (_h - k) * _w, k * _w);
        }

        // ---------- 8. Объёмный эквалайзер ----------
        void Iso()
        {
            Fade(0);
            int n = Math.Clamp((int)(16 * Dens), 8, 32);
            double cell = _w / (n + 4.0), dx = cell * 0.5, dy = cell * 0.28;
            double baseY = _h * 0.86;
            for (int i = 0; i < n; i++)
            {
                double v = Band(i / (double)(n - 1));
                double hgt = Math.Max(cell * 0.2, v * _h * 0.62 * (1 + Kick * 0.1));
                double x = cell * 2 + i * cell, y = baseY - i * 0;
                uint top = Pal(0.35 + v * 0.65), side = Lerp(top, 0, 0.45), front = Lerp(top, 0, 0.25);
                // передняя грань, боковая грань и крышка — параллелограммы
                for (double yy = y - hgt; yy < y; yy++)
                {
                    for (double xx = x; xx < x + cell * 0.7; xx++) Over((int)xx, (int)yy, front, 235);
                    for (double s = 0; s < dx; s++) Over((int)(x + cell * 0.7 + s), (int)(yy - s * dy / dx), side, 235);
                }
                for (double s = 0; s < dx; s++)
                    for (double xx = x; xx < x + cell * 0.7; xx++) Over((int)(xx + s), (int)(y - hgt - s * dy / dx), top, 245);
            }
        }

        // ---------- 9. Точечный экран ----------
        void DotMatrix()
        {
            Fade(0);
            int cols = Math.Clamp((int)(32 * Dens), 16, 64);
            double step = _w / (double)cols;
            int rows = Math.Max(6, (int)(_h / step));
            double rad = step * 0.36;
            for (int i = 0; i < cols; i++)
            {
                double v = Band(i / (double)(cols - 1)), pk = Math.Min(1, _peaks[Math.Min(_peaks.Length - 1, 1 + i * 60 / cols)]);
                int lit = (int)Math.Round(v * rows), peak = (int)Math.Round(pk * rows);
                for (int r = 0; r < rows; r++)
                {
                    double x = (i + 0.5) * step, y = _h - (r + 0.5) * step;
                    bool on = r < lit, isPeak = r == peak - 1 && peak > lit;
                    uint c = Pal(0.15 + r / (double)rows * 0.85);
                    if (on || isPeak) { Dot(x, y, rad, c, 255, false); Dot(x, y, rad * 2, c, 35, true); }
                    else Dot(x, y, rad * 0.8, 0x303030, 90, false);   // погасшие диоды видны
                }
            }
        }

        // ---------- 10. Спиральный спектр ----------
        void Spiral()
        {
            Fade(120);
            double cx = _w / 2.0, cy = _h / 2.0;
            int n = Math.Clamp((int)(140 * Dens), 70, 300);
            double turns = 3.2, rot = _t * (0.2 + _level * 0.5);
            for (int k = 0; k < n; k++)
            {
                double u = k / (double)n, th = rot + u * turns * 2 * Math.PI;
                double r = Min * (0.05 + u * 0.42);
                double v = Band(u);
                double len = 2 + v * Min * 0.16 * (1 + Kick * 0.3);
                double nx = Math.Cos(th), ny = Math.Sin(th);
                Line(cx + nx * r, cy + ny * r, cx + nx * (r + len), cy + ny * (r + len), Pal(0.15 + u * 0.85), (int)(150 + 105 * v), 1.6 + v * 2.6);
            }
        }

        // ---------- 11. Лазерное шоу ----------
        void Laser()
        {
            Fade(110);
            int beams = Math.Clamp((int)(10 * Dens), 6, 24);
            double strobe = Kick;
            for (int side = 0; side < 2; side++)
            {
                double ox = side == 0 ? 0 : _w, oy = _h;
                for (int b = 0; b < beams; b++)
                {
                    double v = Band((b + side * 0.5) / beams);
                    double baseAng = side == 0 ? -0.25 : -Math.PI + 0.25;
                    double ang = baseAng + (side == 0 ? -1 : 1) * (0.15 + b * 0.1 + Math.Sin(_t * (0.8 + b * 0.13) + b) * 0.25 * (0.3 + v));
                    double len = Math.Sqrt(_w * _w + _h * _h) * (0.4 + v * 0.7);
                    Line(ox, oy, ox + Math.Cos(ang) * len, oy + Math.Sin(ang) * len, Pal(0.1 + b / (double)beams * 0.9), (int)(90 + 165 * v + 60 * strobe), 1.6 + v * 2.2);
                }
            }
            if (strobe > 0.6) for (int i = 0; i < _px.Length; i += 3) _px[i] |= 0x30000000;   // короткая белая вспышка на удар
        }

        // ---------- 12. Кометы ----------
        sealed class Comet { public double A, R, Sp, Hue; }
        readonly List<Comet> _comets = new();
        void Comets(double dt)
        {
            Fade(228);
            if (_comets.Count < 14 || (_beat && _comets.Count < 48))
                _comets.Add(new Comet { A = _rnd.NextDouble() * Math.PI * 2, R = Min * (0.12 + _rnd.NextDouble() * 0.36), Sp = 0.6 + _rnd.NextDouble() * 1.4, Hue = _rnd.NextDouble() });
            double cx = _w / 2.0, cy = _h / 2.0;
            foreach (var c in _comets)
            {
                double v = Band(c.Hue);
                c.A += dt * c.Sp * (0.4 + _level * 3 + v * 2);
                double x = cx + Math.Cos(c.A) * c.R, y = cy + Math.Sin(c.A) * c.R * 0.75;
                Dot(x, y, (2.5 + v * 5) * Min / 200, Pal(0.3 + c.Hue * 0.7), (int)(170 + 85 * v), true);
            }
            if (_comets.Count > 14 && !_beat && _rnd.NextDouble() < 0.02) _comets.RemoveAt(0);
            Dot(cx, cy, Min * (0.03 + Bass * 0.06), Pal(1), (int)(80 + 120 * _level), true);
        }

        // ---------- 13. Звуковая ткань ----------
        void Cloth()
        {
            Fade(0);
            int gx = Math.Clamp((int)(30 * Dens), 16, 60), gy = 14;
            double sx = _w / (double)(gx - 1), sy = _h / (double)(gy + 1);
            (double, double) P(int i, int j)
            {
                double u = i / (double)(gx - 1), v = Band(u);
                double wave = Math.Sin(u * 9 + _t * 2.2 + j * 0.5) * v * sy * 1.6 + Math.Sin(j * 0.8 - _t * 1.4) * Mids * sy * 0.6;
                return (i * sx, (j + 1) * sy + wave);
            }
            for (int j = 0; j < gy; j++)
                for (int i = 0; i < gx; i++)
                {
                    var (x0, y0) = P(i, j);
                    double v = Band(i / (double)(gx - 1));
                    uint c = Pal(0.15 + j / (double)gy * 0.6 + v * 0.25);
                    if (i + 1 < gx) { var (x1, y1) = P(i + 1, j); Line(x0, y0, x1, y1, c, (int)(70 + 160 * v)); }
                    if (j + 1 < gy) { var (x2, y2) = P(i, j + 1); Line(x0, y0, x2, y2, c, (int)(40 + 100 * v)); }
                }
        }

        // ---------- 14. Глитч ----------
        int[] _glitchRows;
        void Glitch()
        {
            Fade(0);
            int n = Math.Clamp((int)(40 * Dens), 20, 90);
            double bw = _w / (double)n;
            for (int i = 0; i < n; i++)
            {
                double v = Band(i / (double)(n - 1));
                int top = (int)(_h * (1 - v * 0.9));
                Block((int)(i * bw), top, (int)((i + 1) * bw) - 1, _h, Pal(0.2 + v * 0.8), 230);
            }
            // на удар — горизонтальные полосы съезжают, каналы расходятся
            double k = Kick + Highs * 0.4;
            if (_glitchRows == null || _glitchRows.Length != _h) _glitchRows = new int[_h];
            int seed = (int)(_t * 12);
            for (int y = 0; y < _h; y++)
                _glitchRows[y] = Rnd01(y / 6, seed) < k * 0.5 ? (int)((Rnd01(y / 6, seed + 1) - 0.5) * _w * 0.3 * k) : 0;
            Array.Copy(_px, _tmp, _px.Length);
            int split = (int)(2 + k * 8);
            for (int y = 0; y < _h; y++)
            {
                int off = _glitchRows[y], row = y * _w;
                for (int x = 0; x < _w; x++)
                {
                    int xs = x - off, xr = xs - split, xb = xs + split;
                    uint pr = (uint)xr < (uint)_w ? _tmp[row + xr] : 0, pg = (uint)xs < (uint)_w ? _tmp[row + xs] : 0, pb = (uint)xb < (uint)_w ? _tmp[row + xb] : 0;
                    uint a = Math.Max(pr >> 24, Math.Max(pg >> 24, pb >> 24));
                    _px[row + x] = a << 24 | (pr & 0xFF0000) | (pg & 0x00FF00) | (pb & 0x0000FF);
                }
            }
        }

        // ---------- 15. Сетка ударов ----------
        double[] _tiles;
        void BeatGrid(double dt)
        {
            Fade(0);
            int cols = 12, rows = Math.Max(4, (int)Math.Round(cols * _h / (double)_w));
            if (_tiles == null || _tiles.Length != cols * rows) _tiles = new double[cols * rows];
            double cw = _w / (double)cols, ch = _h / (double)rows;
            for (int i = 0; i < _tiles.Length; i++) _tiles[i] = Math.Max(0, _tiles[i] - dt * 2.2);
            // плитка загорается от своей частоты; на удар — вспышка случайного узора
            for (int c = 0; c < cols; c++)
            {
                double v = Band(c / (double)(cols - 1));
                for (int r = 0; r < rows; r++)
                    if (v > 0.25 && _rnd.NextDouble() < v * 0.25) _tiles[r * cols + c] = Math.Max(_tiles[r * cols + c], v);
            }
            if (_beat) for (int i = 0; i < _tiles.Length; i++) if (_rnd.NextDouble() < 0.3) _tiles[i] = 1;
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    double v = _tiles[r * cols + c];
                    uint col = Pal(0.1 + ((c + r) % 7) / 7.0 * 0.9);
                    int a = (int)(25 + 230 * v);
                    Block((int)(c * cw) + 1, (int)(r * ch) + 1, (int)((c + 1) * cw) - 1, (int)((r + 1) * ch) - 1, col, a);
                }
        }

        // ---------- 16. Орбиты ----------
        double[] _orb;
        void Orbits(double dt)
        {
            Fade(150);
            int rings = 7;
            _orb ??= new double[rings * 3];
            double cx = _w / 2.0, cy = _h / 2.0;
            for (int i = 0; i < rings; i++)
            {
                double v = Group(i / (double)rings, (i + 1) / (double)rings);
                double R = Min * (0.08 + i * 0.058);
                Circle(cx, cy, R * (1 + v * 0.06), Pal(0.1 + i / (double)rings * 0.8), (int)(15 + 200 * v));
                if (v > 0.5) Circle(cx, cy, R * (1 + v * 0.06) + 1, Pal(0.1 + i / (double)rings * 0.8), (int)(120 * (v - 0.5) * 2));
                for (int p = 0; p < 3; p++)
                {
                    _orb[i * 3 + p] += dt * (0.3 + v * 4) * (i % 2 == 0 ? 1 : -1);
                    double a = _orb[i * 3 + p] + p * 2 * Math.PI / 3;
                    Dot(cx + Math.Cos(a) * R, cy + Math.Sin(a) * R, (2.5 + v * 5) * Min / 200, Pal(0.3 + i / (double)rings * 0.7), (int)(150 + 105 * v), true);
                }
            }
            Dot(cx, cy, Min * (0.035 + Bass * 0.08 + Kick * 0.04), Pal(1), (int)(120 + 135 * Bass), true);
            if (_beat) _burst.Add((Min * 0.04, 200));
            for (int i = _burst.Count - 1; i >= 0; i--)
            {
                var (r, a) = _burst[i];
                Circle(cx, cy, r, Pal(0.95), (int)a);
                r += Min * 0.02; a *= 0.9;
                if (a < 6 || r > Min) _burst.RemoveAt(i); else _burst[i] = (r, a);
            }
        }

        // ---------- 17. Потоки линий ----------
        void Lines()
        {
            Fade(70);
            int n = Math.Clamp((int)(18 * Dens), 8, 40);
            int seg = 90;
            for (int l = 0; l < n; l++)
            {
                double off = (l - n / 2.0) / n;
                uint c = Pal(0.1 + l / (double)n * 0.9);
                double px = 0, py = 0;
                for (int s = 0; s <= seg; s++)
                {
                    double u = s / (double)seg, v = Band(u);
                    double y = _h * (0.5 + off * 0.55) + Math.Sin(u * 6 + _t * (1 + l * 0.05) + l * 0.4) * _h * 0.05 * (0.3 + v * 3)
                                + Math.Sin(u * 17 - _t * 2.5) * v * _h * 0.06;
                    double x = u * _w;
                    if (s > 0) Line(px, py, x, y, c, (int)(50 + 160 * v));
                    px = x; py = y;
                }
            }
        }

        /// <summary>Шаг искр взрыва: движение, затухание за ~1 с, отрисовка.</summary>
        void StepParts(double dt)
        {
            for (int i = _parts.Count - 1; i >= 0; i--)
            {
                var p = _parts[i];
                p.X += p.Vx * dt * 60; p.Y += p.Vy * dt * 60; p.Vx *= 0.97; p.Vy *= 0.97; p.Life -= dt;
                if (p.Life <= 0 || p.X < 0 || p.Y < 0 || p.X >= _w || p.Y >= _h) { _parts.RemoveAt(i); continue; }
                Splat(p.X, p.Y, Pal(p.C), (int)(255 * p.Life / p.Max));
                _parts[i] = p;
            }
        }
    }
}
