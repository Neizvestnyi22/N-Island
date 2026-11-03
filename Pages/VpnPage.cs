using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NIsland.Core;
using NIsland.Services;

namespace NIsland.Pages
{
    /// <summary>VPN прямо в острове: состояние, внешний IP и страна, переключатели установленных VPN.</summary>
    public class VpnPage : PageBase
    {
        readonly Border _shield = new() { Width = 46, Height = 46, CornerRadius = new CornerRadius(14) };
        readonly TextBlock _shieldGlyph = UI.Icon("", 20, Brushes.White);
        readonly TextBlock _state = UI.Text("", 15, true);
        readonly TextBlock _via = UI.Text("", 11.5);
        readonly TextBlock _ip = UI.Text("", 11.5);
        readonly TextBlock _isp = UI.Text("", 10.5);
        readonly StackPanel _list = new();
        bool _busy;

        public VpnPage()
        {
            _shield.Child = _shieldGlyph;
            _via.Foreground = _isp.Foreground = UI.Dim;
            _via.TextWrapping = _ip.TextWrapping = TextWrapping.Wrap;
            var left = UI.Col(
                UI.Row(_shield, UI.Col(_state, _via).M(10, 0, 0, 0)),
                _ip.M(0, 10, 0, 0),
                _isp.M(0, 1, 0, 0),
                UI.Row(
                    UI.Pill(UI.IconLabel("", "Обновить", 11), () => _ = Reload(true)),
                    UI.Pill(UI.IconLabel("", "VPN Windows", 11), () => SystemService.Run("ms-settings:network-vpn"), false, "Параметры → Сеть → VPN").M(6, 0, 0, 0)).M(0, 10, 0, 0));
            left.VerticalAlignment = VerticalAlignment.Top;
            left.Width = 230;

            var right = UI.Grid(null, "a,*").Add(
                UI.Text("Подключения", 11, true, UI.Dim),
                new ScrollViewer { Content = _list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }.At(1, 0).M(0, 4, 0, 0));
            Content = UI.Grid("a,*").M(10, 2, 8, 2).Add(left, right.At(0, 1).M(16, 0, 0, 0));
            Vpn.Changed += () => { if (Visible) Render(); };
        }

        public override void OnShown()
        {
            Render();
            // искать программы заново раз в 10 минут, состояние — при каждом открытии
            _ = Reload((DateTime.Now - Vpn.DetectedAt).TotalMinutes > 10);
        }

        async System.Threading.Tasks.Task Reload(bool detect)
        {
            if (_busy) return;
            _busy = true;
            try
            {
                if (detect) await Vpn.Detect(); else await Vpn.RefreshAll();
                await Vpn.LoadIp();
            }
            catch (Exception e) { Log.Info("vpn page: " + e.Message); }
            finally { _busy = false; }
            if (Visible) Render();
        }

        void Render()
        {
            bool on = Vpn.Active;
            var green = Color.FromRgb(0x30, 0xD1, 0x58);
            _shield.Background = on ? new LinearGradientBrush(Color.FromRgb(0x5B, 0xE5, 0x7E), green, 90) : UI.Hex("#2CFFFFFF");
            _shieldGlyph.Text = on ? "" : "";
            _state.Text = on ? "VPN включён" : "VPN выключен";
            _via.Text = on ? Vpn.ActiveName : Vpn.Items.Count == 0 && Vpn.DetectedAt != default ? "VPN-программы не найдены" : "Соединение идёт напрямую";
            var ip = Vpn.PublicIp;
            _ip.Text = string.IsNullOrEmpty(ip.Ip) ? (_busy ? "Узнаю внешний IP…" : "") : $"{ip.Flag} {ip.Ip} · {string.Join(", ", new[] { ip.Country, ip.City }.Where(s => !string.IsNullOrEmpty(s)))}";
            _isp.Text = string.IsNullOrEmpty(ip.Isp) ? "" : ip.Isp;

            _list.Children.Clear();
            if (Vpn.Items.Count == 0)
            {
                _list.Children.Add(new TextBlock
                {
                    Text = Vpn.DetectedAt == default
                        ? "Ищу установленные VPN…"
                        : "Поддерживаются Cloudflare WARP, NordVPN, Mullvad, Windscribe, Tailscale, Private Internet Access, OpenVPN, WireGuard " +
                          "и VPN-подключения Windows — их можно включать прямо здесь. Proton VPN, Surfshark, ExpressVPN, AdGuard VPN, Amnezia и др. " +
                          "показывают состояние и открываются отсюда.",
                    TextWrapping = TextWrapping.Wrap, Foreground = UI.Dim, FontSize = 11.5,
                });
                return;
            }
            foreach (var it in Vpn.Items)
            {
                var item = it;
                var dot = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(8), Background = UI.Hex(it.Color), Child = UI.Text(it.Name[..1].ToUpperInvariant(), 12, true).HA(HorizontalAlignment.Center) };
                var name = new TextBlock { Text = it.Name, FontSize = 12, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Tag = "no-translate" };
                var detail = UI.Text(item.State switch
                {
                    VpnState.On => "● " + (it.Detail == "" ? "подключён" : it.Detail),
                    VpnState.Connecting => it.Detail == "" ? "подключается…" : it.Detail,
                    _ => it.Detail == "" ? "состояние неизвестно" : it.Detail,
                }, 10.5, false, it.State == VpnState.On ? new SolidColorBrush(green) : UI.Dim);
                if (it.Note != null) detail.ToolTip = I18n.T(it.Note);
                FrameworkElement control;
                if (it.CanControl)
                {
                    var sw = new CheckBox { Style = UI.St("Switch"), IsChecked = it.State == VpnState.On, IsEnabled = it.State != VpnState.Connecting, ToolTip = it.NeedsAdmin ? I18n.T("Попросит права администратора") : null };
                    sw.Click += async (_, __) =>
                    {
                        bool want = sw.IsChecked == true;
                        sw.IsEnabled = false;
                        var err = await Vpn.Set(item, want);
                        if (err != null) App.Island.Toast("", item.Name, err);
                        else if (item.State == (want ? VpnState.On : VpnState.Off))
                            App.Island.Toast(want ? "" : "", want ? "VPN подключён" : "VPN отключён", item.Name);
                        _ = Vpn.LoadIp();
                    };
                    control = sw;
                }
                else control = UI.Pill("Открыть", () => Vpn.Open(item), false, I18n.T(it.Note ?? ""));
                var open = UI.IconBtn("", () => Vpn.Open(item), 11, 26, "Открыть приложение");
                _list.Children.Add(new Border
                {
                    CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 0, 4, 4), Background = UI.Res("CardBrush"),
                    Child = UI.Grid("a,*,a,a").Add(dot, UI.Col(name, detail).At(0, 1).M(8, 0, 6, 0), it.CanControl ? open.At(0, 2) : null, control.At(0, 3)),
                });
            }
        }
    }
}
