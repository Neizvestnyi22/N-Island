using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using NIsland.Core;
using NIsland.Services;

namespace NIsland.Pages
{
    /// <summary>Сервисы в острове: умный дом, задачи, календарь, новости, курсы, посылки, сайты, сообщения из Telegram и ntfy.</summary>
    public class HubPage : PageBase
    {
        static string _sel;
        readonly StackPanel _tabs = new() { Orientation = Orientation.Horizontal };
        readonly StackPanel _rows = new();
        readonly TextBlock _status = UI.Text("", 10.5);
        readonly ScrollViewer _scroll;
        readonly Grid _main, _empty;

        public HubPage()
        {
            _status.Foreground = UI.Dim;
            _scroll = new ScrollViewer { Content = _rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var head = UI.Grid("*,a,a").Add(
                new ScrollViewer { Content = _tabs, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled },
                UI.IconBtn("", () => Refresh(true), 11, 26, "Обновить").At(0, 1),
                UI.IconBtn("", () => App.ShowSettings("hub"), 11, 26, "Настроить сервисы").At(0, 2));
            _main = UI.Grid(null, "a,a,*").M(10, 0, 8, 2).Add(head, _status.At(1, 0).M(2, 4, 0, 4), _scroll.At(2, 0));

            // ничего не подключено — компактная подсказка с кнопкой, без пустой шапки
            var icon = new Border
            {
                Width = 52, Height = 52, CornerRadius = new CornerRadius(16),
                Background = new LinearGradientBrush(Color.FromRgb(0x5A, 0xC8, 0xFA), Color.FromRgb(0x32, 0x7A, 0xE6), 90),
                Child = UI.Icon("", 22, Brushes.White),
            };
            var text = new TextBlock
            {
                Text = "Умный дом, задачи Todoist, календарь, новости, курсы, посылки Новой пошты, GitHub, сообщения из Telegram и ntfy, " +
                       "мониторинг сайтов и скробблинг Last.fm — всё здесь, с кнопками.",
                TextWrapping = TextWrapping.Wrap, Foreground = UI.Dim, FontSize = 12, Margin = new Thickness(0, 3, 0, 8),
            };
            _empty = UI.Grid("a,*").M(14, 6, 14, 2).Add(icon.VA(VerticalAlignment.Top),
                UI.Col(UI.Text("Сервисы", 15, true), text, UI.Pill(UI.IconLabel("", "Подключить сервисы", 11), () => App.ShowSettings("hub"), true).HA(HorizontalAlignment.Left))
                  .At(0, 1).M(14, 0, 0, 0));
            _empty.VerticalAlignment = VerticalAlignment.Top;
            Content = new Grid().Add(_main, _empty);
            Integrations.Changed += () => { if (Visible) Render(); };
            // «5 мин назад» и свежие данные сервиса, пока страница открыта
            this.Live(TimeSpan.FromSeconds(30), () => { if (Visible) { Refresh(false); Render(); } return System.Threading.Tasks.Task.CompletedTask; });
        }

        public override void OnShown()
        {
            Render();
            Refresh(false);
        }

        /// <summary>Открыли страницу — освежаем выбранный сервис, если данным больше минуты.</summary>
        void Refresh(bool force)
        {
            var it = Integrations.Get(_sel);
            if (it == null || !it.Enabled || !it.Ready || it.PollSeconds == 0) return;
            if (force || DateTime.Now >= it.NextPoll.AddSeconds(-it.PollSeconds + 60))
            {
                it.NextPoll = DateTime.Now.AddSeconds(it.PollSeconds);
                _status.Text = I18n.T("Обновляю…");
                _ = Integrations.PollOne(it);
            }
        }

        void Render()
        {
            var active = Integrations.Active.Where(i => i.PollSeconds > 0 || i.Rows.Count > 0 || i.Scrobbler).ToList();
            _tabs.Children.Clear();
            _rows.Children.Clear();
            _empty.Show(active.Count == 0);
            _main.Show(active.Count > 0);
            if (active.Count == 0) return;
            if (active.All(i => i.Key != _sel)) _sel = active[0].Key;
            foreach (var it in active)
            {
                var item = it;
                bool on = it.Key == _sel;
                var tab = new Border
                {
                    CornerRadius = new CornerRadius(9), Padding = new Thickness(8, 3, 9, 3), Margin = new Thickness(0, 0, 4, 0), Cursor = Cursors.Hand,
                    Background = on ? new SolidColorBrush(ColorUtil.WithAlpha(ColorUtil.Parse(it.Color, Colors.White), 0.35)) : UI.Hex("#18FFFFFF"),
                    BorderBrush = on ? UI.Hex("#40FFFFFF") : Brushes.Transparent, BorderThickness = new Thickness(1),
                    Child = UI.Row(UI.Icon(it.Glyph, 11, new SolidColorBrush(ColorUtil.Parse(it.Color, Colors.White))),
                                   UI.Text(ShortName(it), 11.5, on).M(5, 0, 0, 0),
                                   it.Ok || it.Status == "" ? null : UI.Text(" ●", 10, false, UI.Hex("#FF9F0A"))),
                };
                tab.MouseLeftButtonUp += (_, __) => { _sel = item.Key; Render(); Refresh(false); };
                _tabs.Children.Add(tab);
            }

            var cur = Integrations.Get(_sel);
            _status.Text = cur.Status ?? "";
            if (cur.Rows.Count == 0)
                _rows.Children.Add(UI.Text(cur.Scrobbler ? "Включай музыку в любом плеере — треки уйдут в историю" : cur.Ok ? "Пока пусто" : "Жду данные…", 12, false, UI.Dim).M(2, 4, 0, 0));
            foreach (var r in cur.Rows.ToList())
            {
                var row = r;
                var title = new TextBlock { Text = r.Title, FontSize = 12, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Tag = "no-translate" };
                var sub = new TextBlock { Text = r.Sub ?? "", FontSize = 10.5, Foreground = UI.Dim, TextTrimming = TextTrimming.CharacterEllipsis, Tag = "no-translate" };
                FrameworkElement right = null;
                if (r.Toggle != null)
                {
                    var sw = new CheckBox { Style = UI.St("Switch"), IsChecked = r.On == true };
                    sw.Click += async (_, __) =>
                    {
                        sw.IsEnabled = false;
                        try { var msg = await row.Toggle(sw.IsChecked == true); if (msg != null) App.Island.Toast(cur.Glyph, cur.Name, msg); }
                        catch (Exception e) { App.Island.Toast("", cur.Name, Integrations.Reason(e)); }
                        Render();
                    };
                    right = sw;
                }
                else if (r.Action != null)
                {
                    var btn = UI.Pill(r.ActionText ?? "OK", null);
                    btn.Padding = new Thickness(9, 2, 9, 2);
                    btn.FontSize = 11;
                    btn.Click += async (_, __) =>
                    {
                        btn.IsEnabled = false;
                        try { var msg = await row.Action(); if (msg != null) App.Island.Toast(cur.Glyph, cur.Name, msg); }
                        catch (Exception e) { App.Island.Toast("", cur.Name, Integrations.Reason(e)); }
                        Render();
                    };
                    right = btn;
                }
                var card = new Border
                {
                    CornerRadius = new CornerRadius(9), Padding = new Thickness(9, 4, 8, 4), Margin = new Thickness(0, 0, 4, 3), Background = UI.Res("CardBrush"),
                    Child = UI.Grid("*,a").Add(UI.Col(title, sub), right?.At(0, 1).M(8, 0, 0, 0)),
                    Cursor = r.Url != null ? Cursors.Hand : null,
                };
                if (r.Url != null) card.MouseLeftButtonUp += (_, e) => { if (e.OriginalSource is not CheckBox) SystemService.Run(row.Url); };
                _rows.Children.Add(card);
            }
        }

        static string ShortName(Integration it) => it.Key switch
        {
            "ics" => "Календарь", "ntfy" => "ntfy", "webhook" => "Вебхук", "rates" => "Курсы", "alerts" => "Тривога", "uptime" => "Сайты", "rss" => "Новости",
            _ => it.Name,
        };
    }
}
