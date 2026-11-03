using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using NIsland.Core;
using NIsland.Services;

namespace NIsland.Pages
{
    /// <summary>Wi-Fi в острове: сигнал вживую (найти лучшее место в квартире), качество связи, сети рядом, устройства в сети.</summary>
    public class WifiPage : PageBase
    {
        readonly TextBlock _bars = new() { FontSize = 22, FontWeight = FontWeights.Bold, Foreground = Brushes.White };
        readonly TextBlock _ssid = UI.Text("", 15, true);
        readonly TextBlock _sig = UI.Text("", 11.5);
        readonly TextBlock _band = UI.Text("", 11);
        readonly TextBlock _speed = UI.Text("", 11);
        readonly Polyline _graph = new() { StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round };
        readonly Canvas _canvas = new() { Height = 70, ClipToBounds = true };
        readonly TextBlock _graphLabel = UI.Text("", 10.5);
        readonly StackPanel _body = new();
        readonly Grid _signalView;
        readonly ScrollViewer _listView;
        readonly DispatcherTimer _t = new() { Interval = TimeSpan.FromSeconds(1) };
        readonly List<int> _history = new();
        string _tab = "signal";
        bool _busy;
        WifiLink _link;

        public WifiPage()
        {
            _sig.Foreground = _band.Foreground = _speed.Foreground = _graphLabel.Foreground = UI.Dim;
            _bars.FontFamily = new FontFamily("Segoe UI");
            var left = UI.Col(
                UI.Row(_bars, UI.Col(_ssid, _sig).M(10, 0, 0, 0)),
                _band.M(0, 8, 0, 0),
                _speed.M(0, 2, 0, 0),
                new WrapPanel { Margin = new Thickness(0, 10, 0, 0) }.With(
                    UI.Pill(UI.IconLabel("", "Переподключить", 11), Reconnect, false, "Отключиться и подключиться заново — помогает, когда Wi-Fi «подвис»").M(0, 0, 6, 4),
                    UI.Pill(UI.IconLabel("", "Роутер", 11), WifiTools.OpenRouter, false, "Открыть страницу настроек роутера").M(0, 0, 6, 4),
                    UI.Pill(UI.IconLabel("", "Всё о Wi-Fi", 11), () => App.ShowCenter("wifi")).M(0, 0, 6, 4)));
            left.VerticalAlignment = VerticalAlignment.Top;
            left.Width = 250;

            var tabs = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var (key, name) in new[] { ("signal", "Сигнал"), ("nets", "Сети рядом"), ("devices", "Устройства") })
            {
                var k = key;
                var rb = new RadioButton { Style = UI.St("Segment"), Content = name, GroupName = "wifitab" + GetHashCode(), IsChecked = key == _tab, FontSize = 11 };
                rb.Checked += (_, __) => { _tab = k; ShowTab(); };
                tabs.Children.Add(rb);
            }

            _graph.Stroke = new SolidColorBrush(Color.FromRgb(0x64, 0xD2, 0xFF));
            _canvas.Children.Add(_graph);
            _canvas.SizeChanged += (_, __) => DrawGraph();
            var test = UI.Pill(UI.IconLabel("", "Проверить качество", 11), TestQuality, true, "20 пингов до роутера и до интернета — покажет, где проблема");
            _signalView = UI.Grid(null, "a,a,a").Add(
                new Border { CornerRadius = new CornerRadius(10), Background = UI.Hex("#14FFFFFF"), Padding = new Thickness(6, 4, 6, 2), Child = _canvas },
                UI.Grid("*,a").M(2, 4, 0, 0).Add(_graphLabel, test.At(0, 1)).At(1, 0),
                _body.At(2, 0).M(2, 4, 0, 0));
            _listView = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Visibility = Visibility.Collapsed, Content = new StackPanel() };

            var right = UI.Grid(null, "a,*").Add(
                new Border { Background = UI.Hex("#22FFFFFF"), CornerRadius = new CornerRadius(8), Padding = new Thickness(2), Child = tabs, HorizontalAlignment = HorizontalAlignment.Left },
                new Grid().Add(_signalView, _listView).At(1, 0).M(0, 6, 0, 0));
            Content = UI.Grid("a,*").M(10, 0, 8, 2).Add(left, right.At(0, 1).M(16, 0, 0, 0));
            _t.Tick += async (_, __) => await Sample();
        }

        public override void OnShown()
        {
            _t.Start();
            _ = Sample();
            ShowTab();
        }

        public override void OnHidden() => _t.Stop();

        async System.Threading.Tasks.Task Sample()
        {
            _link = await WifiTools.Current();
            if (_link == null)
            {
                _bars.Text = "✕";
                _ssid.Text = WifiTools.HasAdapter ? "Wi-Fi не подключён" : "Нет Wi-Fi адаптера";
                _sig.Text = _band.Text = _speed.Text = "";
                return;
            }
            _bars.Text = WifiTools.Bars(_link.Signal);
            _bars.Foreground = new SolidColorBrush(_link.Rssi >= -67 ? Color.FromRgb(0x30, 0xD1, 0x58) : _link.Rssi >= -75 ? Color.FromRgb(0xFF, 0xD6, 0x0A) : Color.FromRgb(0xFF, 0x45, 0x3A));
            _ssid.Text = _link.Ssid;
            _sig.Text = $"{_link.Signal}% · {_link.Rssi} dBm · {I18n.T(WifiTools.SignalWord(_link.Rssi))}";
            _band.Text = $"{_link.Band} · канал {_link.Channel} · {_link.Standard}";
            _speed.Text = $"↓ {_link.RxMbps:0} · ↑ {_link.TxMbps:0} Мбит/с · {_link.Security}";
            _history.Add(_link.Rssi);
            if (_history.Count > 90) _history.RemoveAt(0);
            DrawGraph();
            _graphLabel.Text = I18n.T("Сигнал за последние") + $" {_history.Count} " + I18n.T("с · походи с ноутбуком — график покажет, где лучше");
        }

        void DrawGraph()
        {
            double w = _canvas.ActualWidth, h = _canvas.ActualHeight;
            if (w <= 0 || _history.Count == 0) return;
            var pts = new PointCollection();
            int n = 90;
            for (int i = 0; i < _history.Count; i++)
            {
                double x = w - (_history.Count - 1 - i) * (w / (n - 1));
                double v = Math.Clamp((_history[i] + 95) / 65.0, 0, 1);   // −95 dBm внизу, −30 dBm вверху
                pts.Add(new Point(x, h - 4 - v * (h - 8)));
            }
            _graph.Points = pts;
        }

        void ShowTab()
        {
            _signalView.Show(_tab == "signal");
            _listView.Show(_tab != "signal");
            if (_tab == "nets") LoadNets();
            if (_tab == "devices") LoadDevices();
        }

        async void TestQuality()
        {
            if (_busy) return;
            _busy = true;
            _body.Children.Clear();
            var status = UI.Text(I18n.T("Проверяю…"), 12, false, UI.Dim);
            _body.Children.Add(status);
            var q = await WifiTools.TestQuality(s => Dispatcher.BeginInvoke(new Action(() => status.Text = I18n.T(s))));
            _body.Children.Clear();
            _body.Children.Add(UI.Text(q.Verdict, 13, true));
            _body.Children.Add(new TextBlock
            {
                Text = $"{I18n.T("Роутер")}: {q.GwMs:0} {I18n.T("мс")}, {I18n.T("потери")} {q.GwLoss:0}% · {I18n.T("Интернет")}: {q.NetMs:0} {I18n.T("мс")}, {I18n.T("потери")} {q.NetLoss:0}%" +
                       (q.DnsMs >= 0 ? $" · DNS {q.DnsMs:0} {I18n.T("мс")}" : ""),
                FontSize = 11, Foreground = UI.Dim, TextWrapping = TextWrapping.Wrap,
            });
            _busy = false;
        }

        async void Reconnect()
        {
            App.Island.Toast("", "Переподключаю Wi-Fi…", null);
            var msg = await WifiTools.Reconnect();
            App.Island.Toast("", "Wi-Fi", msg);
        }

        StackPanel List => (StackPanel)_listView.Content;

        async void LoadNets()
        {
            List.Children.Clear();
            List.Children.Add(UI.Text(I18n.T("Сканирую эфир…"), 12, false, UI.Dim));
            var aps = await WifiTools.Nearby();
            if (_tab != "nets") return;
            List.Children.Clear();
            var (now, best) = WifiTools.ChannelAdvice(aps, _link);
            if (now != "") List.Children.Add(new TextBlock { Text = I18n.T(now) + " · " + I18n.T(best), FontSize = 11, Foreground = UI.Hex("#9FD0FF"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 2) });
            if (aps.Count > 0)
                List.Children.Add(new TextBlock { Text = I18n.T("Свободные каналы") + ": " + WifiTools.FreeChannels(WifiTools.ChannelTable(aps, _link)), FontSize = 11, Foreground = UI.Dim, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) });
            if (aps.Count == 0) List.Children.Add(UI.Text(I18n.T("Сетей не видно — Windows могла запретить доступ к местоположению"), 12, false, UI.Dim));
            foreach (var a in aps.Take(25))
            {
                bool weak = a.Security is "Открытая" or "WEP";
                var title = new TextBlock { Text = (a.Current ? "● " : "") + a.Ssid, FontSize = 12, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Tag = "no-translate" };
                var sub = UI.Text($"{a.Band} · {I18n.T("канал")} {a.Channel} · {a.Standard} · {I18n.T(a.Security)}", 10.5, false, weak ? UI.Hex("#FF9F0A") : UI.Dim);
                var sig = UI.Text($"{WifiTools.Bars(a.Signal)} {a.Rssi}", 11, true);
                List.Children.Add(new Border
                {
                    CornerRadius = new CornerRadius(9), Padding = new Thickness(9, 3, 8, 3), Margin = new Thickness(0, 0, 4, 3), Background = UI.Res("CardBrush"),
                    Child = UI.Grid("*,a").Add(UI.Col(title, sub), sig.At(0, 1)),
                    ToolTip = a.Bssid,
                });
            }
        }

        async void LoadDevices()
        {
            List.Children.Clear();
            var status = UI.Text(I18n.T("Ищу устройства в сети…"), 12, false, UI.Dim);
            List.Children.Add(status);
            var list = await WifiTools.Devices(s => Dispatcher.BeginInvoke(new Action(() => status.Text = I18n.T(s))));
            if (_tab != "devices") return;
            List.Children.Clear();
            List.Children.Add(UI.Text(I18n.T("Устройств в сети") + ": " + list.Count, 11, true, UI.Dim).M(0, 0, 0, 3));
            foreach (var d in list)
            {
                var title = new TextBlock { Text = string.IsNullOrEmpty(d.Name) ? d.Ip : d.Name, FontSize = 12, FontWeight = FontWeights.SemiBold, Tag = "no-translate" };
                var sub = UI.Text(string.Join(" · ", new[] { I18n.T(d.Kind), d.Name != "" ? d.Ip : null, d.Mac }.Where(s => !string.IsNullOrEmpty(s))), 10.5, false, UI.Dim);
                List.Children.Add(new Border
                {
                    CornerRadius = new CornerRadius(9), Padding = new Thickness(9, 3, 8, 3), Margin = new Thickness(0, 0, 4, 3), Background = UI.Res("CardBrush"),
                    Child = UI.Grid("*,a").Add(UI.Col(title, sub), UI.Text(d.Ms >= 0 ? $"{d.Ms:0} " + I18n.T("мс") : "", 11, false, UI.Dim).At(0, 1)),
                });
            }
        }
    }

    static class PanelExt
    {
        public static T With<T>(this T panel, params UIElement[] items) where T : Panel
        {
            foreach (var i in items) if (i != null) panel.Children.Add(i);
            return panel;
        }
    }
}
