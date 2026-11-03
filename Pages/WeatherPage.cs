using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using NIsland.Core;
using NIsland.Services;

namespace NIsland.Pages
{
    /// <summary>
    /// Погода: сейчас, детали (ветер с порывами, UV, воздух, восход и закат), осадки на 2 часа вперёд и совет, что надеть;
    /// справа — график на сутки или 7 дней (выбор запоминается), снизу — мировые часы.
    /// </summary>
    public class WeatherPage : PageBase
    {
        readonly TextBlock _icon = new() { FontFamily = new FontFamily("Segoe UI Emoji"), FontSize = 30, VerticalAlignment = VerticalAlignment.Center };
        readonly TextBlock _temp = UI.Text("—°", 30, true);
        readonly TextBlock _desc = UI.Text("Загрузка…", 12.5, true);
        readonly TextBlock _meta = UI.Text("", 11);
        readonly WrapPanel _chips = new();
        readonly TextBlock _advice = UI.Text("", 11.5, true);
        readonly HourlyChart _chart = new() { Height = 108 };
        readonly UniformGrid _days = new() { Rows = 1 };
        readonly UniformGrid _clocks = new() { Rows = 1 };
        readonly Border _hoursTab, _daysTab;

        public override double ExtraHeight => 30;

        public WeatherPage()
        {
            _meta.Foreground = UI.Dim;
            _advice.TextWrapping = TextWrapping.Wrap;
            _advice.TextTrimming = TextTrimming.None;
            _advice.MaxHeight = 34;
            _advice.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
            var now = UI.Row(_icon, _temp.M(6, 0, 0, 0), UI.Col(_desc, _meta).M(10, 0, 0, 0));
            var left = UI.Col(now, _chips.M(0, 4, 0, 0), _advice.M(0, 3, 0, 0));
            left.Width = 250;
            left.VerticalAlignment = VerticalAlignment.Top;

            _hoursTab = Tab("По часам", "hours");
            _daysTab = Tab("7 дней", "days");
            var tabs = UI.Row(_hoursTab, _daysTab.M(4, 0, 0, 0));
            tabs.HorizontalAlignment = HorizontalAlignment.Right;
            var right = UI.Grid(null, "a,*").Add(tabs, new Grid().Add(_chart, _days).At(1, 0).M(0, 2, 0, 0));

            var top = UI.Grid("a,*").Add(left.M(10, 0, 0, 0), right.At(0, 1).M(10, 0, 10, 0));
            Content = UI.Grid(null, "*,a").Add(top, _clocks.At(1, 0).M(12, 2, 12, 0));
            WeatherService.Updated += () => { if (Visible) Render(); };
        }

        Border Tab(string text, string key)
        {
            var b = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 2, 8, 2), Cursor = System.Windows.Input.Cursors.Hand, Child = UI.Text(text, 10.5, true), Tag = key };
            b.MouseLeftButtonUp += (_, e) => { S.WeatherView = key; Render(); e.Handled = true; };
            return b;
        }

        public override async void OnShown()
        {
            Render();
            var w = await WeatherService.Get();
            if (w == null && WeatherService.Cached == null)
            {
                _desc.Text = "Не удалось загрузить погоду";
                _meta.Text = WeatherService.LastError ?? $"Город: {WeatherService.City}. Проверь интернет или название города в настройках.";
                _meta.TextWrapping = TextWrapping.Wrap;
            }
            if (Visible) Render();
        }

        UIElement Chip(string text, string tip = null, string color = null)
        {
            var t = UI.Text(text, 10.5, false, color != null ? UI.Hex(color) : null);
            return new Border { CornerRadius = new CornerRadius(7), Background = UI.Res("CardBrush"), Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(0, 0, 4, 3), Child = t, ToolTip = tip };
        }

        void Render()
        {
            bool hours = S.WeatherView != "days";
            foreach (var t in new[] { _hoursTab, _daysTab })
            {
                bool on = (string)t.Tag == (hours ? "hours" : "days");
                t.Background = on ? UI.Res("AccentBrush") : UI.Hex("#22FFFFFF");
                t.OnAccent(on);
            }
            _chart.Show(hours);
            _days.Show(!hours);

            var w = WeatherService.Cached;
            if (w != null)
            {
                var (icon, text) = WeatherService.Describe(w.Code);
                _icon.Text = !w.IsDay && w.Code <= 1 ? "🌙" : icon;
                _temp.Text = $"{Math.Round(w.Temp)}°";
                _desc.Text = text;
                _meta.Text = $"{w.City} · ощущается {Math.Round(w.Feels)}°";
                _chips.Children.Clear();
                _chips.Children.Add(Chip($"💨 {Math.Round(w.Wind)}{(w.Gusts > w.Wind + 2 ? "–" + Math.Round(w.Gusts) : "")} м/с", "Ветер и порывы"));
                _chips.Children.Add(Chip($"💧 {Math.Round(w.Humidity)}%", "Влажность"));
                if (w.DayUv.Count > 0 && w.DayUv[0] >= 1) _chips.Children.Add(Chip($"☀ UV {w.DayUv[0]:0}", "Ультрафиолет сегодня (3+ — нужна защита)"));
                if (w.Aqi is int a) { var (at, ac) = WeatherService.AqiText(a); _chips.Children.Add(Chip($"🌫 {at}", $"Качество воздуха: индекс {a}" + (w.Pm25 is double pm ? $", PM2.5 {pm:0} мкг/м³" : ""), ac)); }
                if (w.Sunrise is DateTime sr && w.Sunset is DateTime ss) _chips.Children.Add(Chip($"🌅 {sr:HH:mm} – {ss:HH:mm}", "Восход и закат"));
                if (w.Pressure > 0) _chips.Children.Add(Chip($"⏲ {Math.Round(w.Pressure)}", "Давление, мм рт. ст."));
                var adv = WeatherService.Advice(w);
                _advice.Text = string.Join("  ", adv.Take(2));
                _advice.Tag = "no-translate";
                _advice.Text = I18n.T(_advice.Text);
                _chart.Data = w.Hours;
                _chart.InvalidateVisual();

                _days.Children.Clear();
                _days.Columns = Math.Max(1, w.Days.Count);
                for (int i = 0; i < w.Days.Count; i++)
                {
                    var (day, max, min, code) = w.Days[i];
                    var name = day.Date == DateTime.Today ? "Сегодня" : Fmt.Cap(day.ToString("ddd", Fmt.Ru));
                    int rain = i < w.DayRain.Count ? w.DayRain[i] : 0;
                    _days.Children.Add(new Border
                    {
                        CornerRadius = new CornerRadius(10), Background = UI.Res("CardBrush"), Margin = new Thickness(2), Padding = new Thickness(1, 4, 1, 4),
                        Child = UI.Col(
                            UI.Text(name, 10.5, false, UI.Dim).HA(HorizontalAlignment.Center),
                            new TextBlock { Text = WeatherService.Describe(code).Icon, FontFamily = new FontFamily("Segoe UI Emoji"), FontSize = 17, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 1) },
                            UI.Text($"{Math.Round(max)}°", 12, true).HA(HorizontalAlignment.Center),
                            UI.Text($"{Math.Round(min)}°", 11, false, UI.Dim).HA(HorizontalAlignment.Center),
                            UI.Text(rain >= 20 ? $"💧{rain}%" : " ", 9.5, false, UI.Hex("#64D2FF")).HA(HorizontalAlignment.Center)),
                    });
                }
            }
            else if (_desc.Text == "") _desc.Text = "Загрузка…";

            _clocks.Children.Clear();
            var clocks = WeatherService.WorldClocks();
            _clocks.Columns = Math.Max(1, clocks.Count);
            foreach (var (name, time) in clocks)
            {
                int diff = (int)Math.Round((time - DateTime.Now).TotalHours);
                _clocks.Children.Add(UI.Row(
                    UI.Text(name, 11, false, UI.Dim),
                    UI.Text(time.ToString("HH:mm"), 11.5, true).M(6, 0, 0, 0),
                    UI.Text(diff == 0 ? "" : (diff > 0 ? "+" : "") + diff + "ч", 10, false, UI.Faint).M(4, 0, 0, 0)).HA(HorizontalAlignment.Center));
            }
        }
    }

    /// <summary>График на сутки: температура линией, вероятность осадков столбиками, значки погоды каждые 3 часа.</summary>
    public class HourlyChart : FrameworkElement
    {
        public List<(DateTime T, double Temp, int Rain, int Code)> Data { get; set; } = new();

        static readonly Typeface Face = new(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        static readonly Typeface Emoji = new(new FontFamily("Segoe UI Emoji"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

        protected override void OnRender(DrawingContext dc)
        {
            var pts = Data?.Where(d => d.T >= DateTime.Now.AddMinutes(-59)).Take(24).ToList();
            double w = ActualWidth, h = ActualHeight;
            if (pts == null || pts.Count < 2 || w < 60) return;
            double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            double top = 30, bottom = h - 16, step = (w - 16) / (pts.Count - 1);
            double min = pts.Min(p => p.Temp), max = pts.Max(p => p.Temp);
            if (max - min < 4) { double mid = (max + min) / 2; min = mid - 2; max = mid + 2; }
            double X(int i) => 8 + i * step;
            double Y(double t) => top + (1 - (t - min) / (max - min)) * (bottom - top - 18);

            // осадки
            var rainBrush = new SolidColorBrush(Color.FromArgb(0x70, 0x64, 0xD2, 0xFF));
            for (int i = 0; i < pts.Count; i++)
                if (pts[i].Rain > 0)
                {
                    double bh = pts[i].Rain / 100.0 * 22;
                    dc.DrawRoundedRectangle(rainBrush, null, new Rect(X(i) - step * 0.3, bottom - bh, step * 0.6, bh), 1.5, 1.5);
                }

            // температура
            var accent = (Application.Current.Resources["AccentBrush"] as SolidColorBrush)?.Color ?? Colors.Orange;
            var geo = new StreamGeometry();
            using (var c = geo.Open())
            {
                c.BeginFigure(new Point(X(0), Y(pts[0].Temp)), false, false);
                for (int i = 1; i < pts.Count; i++)
                {
                    var p0 = new Point(X(i - 1), Y(pts[i - 1].Temp)); var p1 = new Point(X(i), Y(pts[i].Temp));
                    c.BezierTo(new Point(p0.X + step / 2, p0.Y), new Point(p1.X - step / 2, p1.Y), p1, true, true);
                }
            }
            geo.Freeze();
            dc.DrawGeometry(null, new Pen(new SolidColorBrush(accent), 2) { LineJoin = PenLineJoin.Round }, geo);

            var dim = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF));
            for (int i = 0; i < pts.Count; i += 3)
            {
                var p = pts[i];
                dc.DrawEllipse(Brushes.White, null, new Point(X(i), Y(p.Temp)), 2.4, 2.4);
                var tt = new FormattedText($"{Math.Round(p.Temp)}°", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, 10.5, Brushes.White, dpi);
                dc.DrawText(tt, new Point(X(i) - tt.Width / 2, Y(p.Temp) - 17));
                var ic = new FormattedText(WeatherService.Describe(p.Code).Icon, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Emoji, 12, Brushes.White, dpi);
                dc.DrawText(ic, new Point(X(i) - ic.Width / 2, 0));
                var label = i == 0 ? I18n.T("Сейчас") : p.T.ToString("HH");
                var hh = new FormattedText(label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, 9.5, dim, dpi);
                dc.DrawText(hh, new Point(X(i) - hh.Width / 2, h - 13));
                if (p.Rain >= 30)
                {
                    var rp = new FormattedText(p.Rain + "%", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, 8.5, new SolidColorBrush(Color.FromRgb(0x64, 0xD2, 0xFF)), dpi);
                    dc.DrawText(rp, new Point(X(i) - rp.Width / 2, bottom - 34));
                }
            }
        }
    }
}
