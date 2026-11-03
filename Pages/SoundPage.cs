using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using NIsland.Services;

namespace NIsland.Pages
{
    /// <summary>Звук: общая громкость, устройство вывода и микшер по приложениям.</summary>
    public class SoundPage : PageBase
    {
        readonly Slider _master = UI.Slider();
        readonly TextBlock _masterText = UI.Text("", 11.5);
        readonly StackPanel _outputs = new();
        readonly StackPanel _apps = new();
        readonly DispatcherTimer _t = new() { Interval = TimeSpan.FromSeconds(2) };
        bool _setting, _dragging;

        public SoundPage()
        {
            _masterText.Foreground = UI.Dim;
            _masterText.Width = 36;
            _masterText.TextAlignment = TextAlignment.Right;
            _master.ValueChanged += (_, __) => { if (!_setting) AudioService.I.SetVolume(_master.Value); _masterText.Text = Math.Round(_master.Value * 100) + "%"; };
            var master = UI.Grid("a,*,a").Add(UI.IconBtn("", () => AudioService.I.ToggleMute(), 14, 28, "Без звука"), _master.At(0, 1).M(6, 0, 6, 0), _masterText.At(0, 2));
            var left = UI.Grid(null, "a,a,*").Add(
                UI.Text("Общая громкость", 11, true, UI.Dim),
                master.At(1, 0).M(0, 2, 0, 6),
                new ScrollViewer { Content = _outputs, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }.At(2, 0));
            left.Width = 250;

            var right = UI.Grid(null, "a,*").Add(
                UI.Text("Громкость приложений", 11, true, UI.Dim),
                new ScrollViewer { Content = _apps, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }.At(1, 0).M(0, 4, 0, 0));
            Content = UI.Grid("a,*").M(8, 0, 8, 2).Add(left, right.At(0, 1).M(16, 0, 0, 0));
            _t.Tick += (_, __) => { if (!_dragging) RenderApps(); };
            AudioService.I.Changed += (_, __, ___) => { if (Visible) SyncMaster(); };
        }

        public override void OnShown()
        {
            SyncMaster();
            RenderOutputs();
            RenderApps();
            _t.Start();
        }

        public override void OnHidden() { _t.Stop(); _apps.Children.Clear(); _outputs.Children.Clear(); }

        void SyncMaster()
        {
            var a = AudioService.I;
            _setting = true;
            _master.Value = a.Muted ? 0 : a.Volume;
            _setting = false;
            _masterText.Text = a.Muted ? "выкл" : Math.Round(a.Volume * 100) + "%";
        }

        void RenderOutputs()
        {
            _outputs.Children.Clear();
            _outputs.Children.Add(UI.Text("Вывод звука", 11, true, UI.Dim).M(0, 0, 0, 3));
            foreach (var d in AudioService.I.Outputs())
            {
                var dev = d;
                var b = new Border
                {
                    CornerRadius = new CornerRadius(9), Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 0, 0, 3), Cursor = System.Windows.Input.Cursors.Hand,
                    Background = d.IsDefault ? UI.Res("AccentBrush") : UI.Res("CardBrush"), BorderBrush = Txt.Rim, BorderThickness = new Thickness(1),
                    Child = UI.Row(UI.Icon(d.Name.Contains("Head", StringComparison.OrdinalIgnoreCase) || d.Name.Contains("Науш", StringComparison.OrdinalIgnoreCase) ? "" : "", 12),
                        UI.Text(d.Name, 11.5, d.IsDefault).M(6, 0, 0, 0)),
                    ToolTip = d.IsDefault ? "Сейчас играет здесь" : "Переключить звук сюда",
                };
                ((TextBlock)((StackPanel)b.Child).Children[1]).MaxWidth = 200;
                b.OnAccent(d.IsDefault);
                b.MouseLeftButtonUp += (_, __) =>
                {
                    if (AudioService.I.SetDefaultOutput(dev.Id)) App.Island.Toast("", "Звук переключён", dev.Name);
                    RenderOutputs();
                };
                _outputs.Children.Add(b);
            }
        }

        void RenderApps()
        {
            var apps = AudioService.I.Apps();
            _apps.Children.Clear();
            if (apps.Count == 0) { _apps.Children.Add(UI.Text("Сейчас ни одно приложение не выводит звук", 12, false, UI.Dim)); return; }
            foreach (var a in apps)
            {
                var app = a;
                var s = UI.Slider();
                s.Value = a.Volume;
                var pct = UI.Text(a.Muted ? "выкл" : Math.Round(a.Volume * 100) + "%", 11, false, UI.Dim);
                pct.Width = 34;
                pct.TextAlignment = TextAlignment.Right;
                s.PreviewMouseLeftButtonDown += (_, __) => _dragging = true;
                s.PreviewMouseLeftButtonUp += (_, __) => _dragging = false;
                s.ValueChanged += (_, __) => { AudioService.I.SetAppVolume(app.Name, (float)s.Value); pct.Text = Math.Round(s.Value * 100) + "%"; };
                var mute = UI.IconBtn(a.Muted ? "" : "", () => { AudioService.I.SetAppVolume(app.Name, -1, !app.Muted); RenderApps(); }, 12, 26, "Выключить звук приложения");
                if (a.Muted) mute.Foreground = UI.Hex("#FF453A");
                var name = UI.Text(a.Name, 12, true);
                name.Width = 110;
                _apps.Children.Add(UI.Grid("a,a,*,a").M(0, 1, 4, 1).Add(mute, name.At(0, 1).M(4, 0, 6, 0), s.At(0, 2), pct.At(0, 3)));
            }
        }
    }
}
