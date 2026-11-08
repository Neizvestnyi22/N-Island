using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NIsland.Controls
{
    // Самопроверка: NIsland.exe --selftest viz <выход.txt> — каждый стиль рисуется 90 кадров с искусственным звуком,
    // замеряется время кадра, ловятся исключения и пустая картинка; рядом сохраняется лист превью всех стилей (PNG).
    public partial class Visualizer
    {
        internal static string Bench(int w, int h, string pngPath)
        {
            var sb = new StringBuilder();
            int cols = 6, rows = (Styles.Length + cols - 1) / cols;
            var sheet = new WriteableBitmap(cols * w, rows * h, 96, 96, PixelFormats.Pbgra32, null);
            var rnd = new Random(1);
            int idx = 0;
            foreach (var st in Styles)
            {
                var v = new Visualizer { _w = w, _h = h, _px = new uint[w * h], _tmp = new uint[w * h] };
                v._style = st.Key;
                v._palette = idx % 3 == 0 ? "art" : idx % 3 == 1 ? "neon" : "sunset";
                v.ResetState();
                var sw = new Stopwatch();
                double worst = 0;
                string err = null;
                uint[] last = v._px;
                for (int f = 0; f < 90; f++)
                {
                    double t = f / 45.0;
                    for (int i = 0; i < v._bands.Length; i++)
                    {
                        double u = i / (double)v._bands.Length;
                        v._bands[i] = (float)Math.Clamp((0.75 - u * 0.5) * (0.55 + 0.45 * Math.Sin(t * 6 + i * 0.3)) + rnd.NextDouble() * 0.1, 0, 1);
                        v._peaks[i] = Math.Min(1, v._bands[i] + 0.08f);
                    }
                    for (int i = 0; i < v._wave.Length; i++) v._wave[i] = (float)(0.5 * Math.Sin(i * 0.11 + t * 9) + 0.2 * Math.Sin(i * 0.37));
                    v._level = (float)(0.4 + 0.3 * Math.Sin(t * 3));
                    v._beat = f % 20 == 0;
                    v._sinceBeat = v._beat ? 0 : v._sinceBeat + 1 / 45.0;
                    v._t += 1 / 45.0;
                    v.BuildLut();
                    sw.Restart();
                    try { v.Draw(1 / 45.0); last = v.Post(); }
                    catch (Exception e) { err ??= e.GetType().Name + ": " + e.Message + " @ " + e.StackTrace?.Split('\n').FirstOrDefault()?.Trim(); }
                    sw.Stop();
                    if (f > 5) worst = Math.Max(worst, sw.Elapsed.TotalMilliseconds);
                }
                int lit = last.Count(p => (p >> 24) > 8);
                sb.AppendLine($"{st.Key,-12} worst {worst,6:0.0} ms  lit {lit * 100 / last.Length,3}%  {(err ?? "")}");
                sheet.WritePixels(new Int32Rect((idx % cols) * w, (idx / cols) * h, w, h), last, w * 4, 0);
                idx++;
            }
            sb.AppendLine();
            sb.AppendLine("отклик на музыку (доля яркости, которая меняется от звука, %; 100 — в тишине пусто, меньше 35 — звук почти не влияет):");
            foreach (var st in Styles)
                sb.AppendLine($"  {st.Key,-12} {Reactivity(st.Key, w, h),5:0.0}");
            var enc = new PngBitmapEncoder();
            // на чёрном фоне — как в плеере
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, sheet.PixelWidth, sheet.PixelHeight));
                dc.DrawImage(sheet, new Rect(0, 0, sheet.PixelWidth, sheet.PixelHeight));
                var tf = new Typeface("Segoe UI");
                for (int i = 0; i < Styles.Length; i++)
                    dc.DrawText(new FormattedText(Styles[i].Key, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, tf, 12, Brushes.White, 1),
                                new Point((i % cols) * w + 4, (i / cols) * h + 2));
            }
            var rtb = new RenderTargetBitmap(sheet.PixelWidth, sheet.PixelHeight, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using (var fs = File.Create(pngPath)) enc.Save(fs);
            return sb.ToString();
        }

        /// <summary>Насколько стиль меняется от звука: одни и те же 60 кадров в тишине и под громкий ритм, сравнение яркости.</summary>
        static double Reactivity(string key, int w, int h)
        {
            uint[] Run(bool loud)
            {
                var v = new Visualizer { _w = w, _h = h, _px = new uint[w * h], _tmp = new uint[w * h] };
                v._style = key;
                v._palette = "neon";
                v.ResetState();
                uint[] last = v._px;
                var rnd = new Random(7);
                for (int f = 0; f < 60; f++)
                {
                    double t = f / 45.0;
                    // громко: бас качается с ударами каждые 10 кадров, середина и верх живут своей жизнью
                    double kick = loud ? Math.Exp(-(f % 10) * 0.45) : 0;
                    for (int i = 0; i < v._bands.Length; i++)
                    {
                        double u = i / (double)v._bands.Length;
                        double val = loud ? Math.Clamp((u < 0.2 ? 0.35 + 0.6 * kick : 0.55 - u * 0.35) * (0.7 + 0.3 * Math.Sin(t * 7 + i * 0.4)) + rnd.NextDouble() * 0.08, 0, 1) : 0;
                        v._bands[i] = (float)val;
                        v._peaks[i] = (float)Math.Min(1, val + (loud ? 0.08 : 0));
                    }
                    for (int i = 0; i < v._wave.Length; i++) v._wave[i] = loud ? (float)((0.4 + 0.4 * kick) * Math.Sin(i * 0.11 + t * 9) + 0.2 * Math.Sin(i * 0.37)) : 0;
                    v._level = loud ? (float)(0.45 + 0.4 * kick) : 0;
                    v._beat = loud && f % 10 == 0;
                    v._sinceBeat = v._beat ? 0 : v._sinceBeat + 1 / 45.0;
                    v._t += 1 / 45.0;
                    v.BuildLut();
                    try { v.Draw(1 / 45.0); last = (uint[])v.Post().Clone(); } catch { }
                }
                return last;
            }
            var a = Run(false);
            var b = Run(true);
            // относительная разница: сколько яркости «переехало» между тишиной и музыкой от всей яркости картинки
            double diff = 0, total = 0;
            for (int i = 0; i < a.Length; i++)
            {
                uint p = a[i], q = b[i];
                double la = ((p >> 16) & 255) * 0.3 + ((p >> 8) & 255) * 0.59 + (p & 255) * 0.11;
                double lb = ((q >> 16) & 255) * 0.3 + ((q >> 8) & 255) * 0.59 + (q & 255) * 0.11;
                diff += Math.Abs(la - lb);
                total += Math.Max(la, lb);
            }
            return total < 1 ? 0 : diff / total * 100;
        }
    }
}
