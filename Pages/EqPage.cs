using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NIsland.Controls;
using NIsland.Core;
using NIsland.Services;

namespace NIsland.Pages
{
    /// <summary>Эквалайзер в чёлке: кривая с живым спектром, пресеты, отдельный профиль для каждого устройства вывода.</summary>
    public class EqPage : PageBase
    {
        readonly EqCurve _curve = new();
        readonly CheckBox _on = new() { Style = UI.St("Switch"), VerticalAlignment = VerticalAlignment.Center };
        readonly TextBlock _status = UI.Text("", 11);
        readonly TextBlock _device = UI.Text("", 11);
        readonly WrapPanel _presets = new();
        readonly Button _install;
        bool _syncing;

        public override double ExtraHeight => 30;

        public EqPage()
        {
            _status.Foreground = UI.Dim;
            _status.TextWrapping = TextWrapping.Wrap;
            _status.TextTrimming = TextTrimming.None;
            _status.MaxHeight = 44;
            _device.Foreground = UI.Faint;
            _on.Checked += (_, __) => { if (!_syncing) Equalizer.SetOn(true); };
            _on.Unchecked += (_, __) => { if (!_syncing) Equalizer.SetOn(false); };
            _install = UI.Pill(UI.IconLabel("\uE896", "Установить движок", 11), Install, true, "Equalizer APO — бесплатный открытый движок, через него эквалайзер меняет звук всех приложений");
            _install.HorizontalAlignment = HorizontalAlignment.Left;

            foreach (var p in Equalizer.Presets)
            {
                var key = p.Key;
                var b = UI.Pill(p.Name, () => { Equalizer.SetPreset(key); Sync(); });
                b.FontSize = 10.5;
                b.Padding = new Thickness(8, 2, 8, 2);
                b.Margin = new Thickness(0, 0, 4, 4);
                b.Tag = key;
                _presets.Children.Add(b);
            }

            var head = UI.Grid("a,*,a").Add(_on, UI.Text("Эквалайзер", 13.5, true).At(0, 1).M(8, 0, 0, 0),
                UI.IconBtn("\uE713", () => App.ShowSettings("eq"), 11, 24, "Настройки эквалайзера").At(0, 2));
            var left = UI.Grid(null, "a,a,a,*").Add(
                head,
                _status.At(1, 0).M(0, 4, 0, 2),
                UI.Col(_install, _device).At(2, 0).M(0, 2, 0, 4),
                new ScrollViewer { Content = _presets, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }.At(3, 0));
            left.Width = 200;

            // во время перетаскивания применяем не чаще 6 раз в секунду — APO перечитывает файл при каждом изменении
            var throttle = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(160) };
            throttle.Tick += (_, __) => { throttle.Stop(); Equalizer.Apply(); };
            _curve.Changing += g => { S.EqGains = Equalizer.Format(g); S.EqPreset = "custom"; if (!throttle.IsEnabled) throttle.Start(); };
            _curve.Committed += g => { Equalizer.SetGains(g, MatchPreset(g)); Sync(); };

            Content = UI.Grid("a,*").M(10, 0, 10, 0).Add(left, _curve.At(0, 1).M(10, 0, 0, 0));
            Equalizer.Changed += () => { if (Visible) Dispatcher.BeginInvoke(new Action(Sync)); };
            AudioService.I.OutputChanged += _ => { if (Visible) Dispatcher.BeginInvoke(new Action(Sync)); };
        }

        static string MatchPreset(double[] g) =>
            Equalizer.Presets.FirstOrDefault(p => p.Gains.Zip(g, (a, b) => Math.Abs(a - b) < 0.05).All(x => x))?.Key ?? "custom";

        public override void OnShown() => Sync();

        void Sync()
        {
            _syncing = true;
            _on.IsChecked = S.EqOn;
            _syncing = false;
            _curve.Gains = Equalizer.Gains;
            _curve.Enabled = S.EqOn;
            _curve.CurveColor = (UI.Res("ArtBrush") as SolidColorBrush)?.Color ?? Colors.DodgerBlue;
            _curve.InvalidateVisual();
            foreach (Button b in _presets.Children)
            {
                bool cur = (string)b.Tag == S.EqPreset && S.EqOn;
                b.Style = UI.St(cur ? "AccentPill" : "PillButton");
            }
            bool inst = Equalizer.Installed;
            _install.Show(!inst);
            var dev = AudioService.I.OutputName ?? "";
            _device.Text = S.EqPerDevice && dev != "" ? I18n.T("Профиль для: ") + dev : dev;
            _device.Tag = "no-translate";
            if (!inst) _status.Text = I18n.T("Нужен бесплатный движок Equalizer APO — без него Windows не даёт менять звук других приложений. Кривая уже сохраняется.");
            else if (Equalizer.LastError != null) _status.Text = I18n.T(Equalizer.LastError);
            else if (Equalizer.OnCurrentDevice() == false) _status.Text = I18n.T("APO не подключён к этому устройству. Открой «Настройки → Эквалайзер → Выбрать устройства».");
            else _status.Text = I18n.T(S.EqOn ? "Работает для всех приложений · " + Equalizer.PresetName(S.EqPreset) : "Выключен — звук без изменений");
        }

        async void Install()
        {
            _install.IsEnabled = false;
            var err = await Equalizer.Install(s => _status.Text = I18n.T(s));
            _install.IsEnabled = true;
            if (err != null) _status.Text = I18n.T(err);
            else App.Island?.Toast("\uE896", "Установщик Equalizer APO запущен", "Отметь свои наушники и колонки, затем перезагрузи ПК");
        }
    }
}
