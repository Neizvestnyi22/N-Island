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
    }
}
