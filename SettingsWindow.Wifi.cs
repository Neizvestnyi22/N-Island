using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NIsland.Core;
using NIsland.Pages;
using NIsland.Services;

namespace NIsland
{
    /// <summary>Центр управления: разделы «Wi-Fi» (анализ и инструменты) и «OSINT и проверки».</summary>
    public partial class SettingsWindow
    {
        /// <summary>Таблица «название — значение» для результатов.</summary>
        static UIElement Table(IEnumerable<(string K, string V)> rows)
        {
            var sp = new StackPanel { Margin = new Thickness(14, 4, 14, 10) };
            foreach (var (k, v) in rows)
            {
                var g = UI.Grid("170,*").M(0, 3, 0, 3).Add(
                    new TextBlock { Text = I18n.T(k), FontSize = 12, Foreground = UI.Dim, TextWrapping = TextWrapping.Wrap },
                    new TextBox
                    {
                        Text = v ?? "", FontSize = 12.5, Foreground = Brushes.White, Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                        IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Padding = new Thickness(0), Tag = "no-translate",
                    }.At(0, 1));
                sp.Children.Add(g);
            }
            return sp;
        }

        static TextBlock Info(string text, string color = null) => new()
        {
            Text = I18n.T(text), FontSize = 12.5, Foreground = color != null ? UI.Hex(color) : UI.Dim, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(14, 6, 14, 6),
        };

        // ------------------------------------------------------------------
        //  Wi-Fi
        // ------------------------------------------------------------------

        /// <summary>Загрузка каналов полосками: свободный — зелёный, занятый — оранжевый/красный, текущий и лучший отмечены.</summary>
        static UIElement ChannelBars(List<ChannelLoad> table, string band)
        {
            var rows = table.Where(c => c.Band == band).ToList();
            double max = Math.Max(1, rows.Max(c => c.Load));
            var grid = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1, Margin = new Thickness(14, 4, 14, 2) };
            foreach (var c in rows)
            {
                double h = 6 + 54 * c.Load / max;
                var color = c.Load < 0.15 ? "#30D158" : c.Load < 1 ? "#FFD60A" : c.Load < 2 ? "#FF9F0A" : "#FF453A";
                var bar = new Border { Height = h, Width = 12, CornerRadius = new CornerRadius(3), Background = UI.Hex(color), VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center };
                var col = new Grid { Height = 92, ToolTip = $"{I18n.T("Канал")} {c.Channel}: {I18n.T("сетей")} {c.Networks}" + (c.Current ? " · " + I18n.T("твой") : "") + (c.Best ? " · " + I18n.T("лучший") : "") };
                col.RowDefinitions.Add(new RowDefinition { Height = new GridLength(62) });
                col.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                col.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                col.Add(bar,
                    UI.Text(c.Channel.ToString(), 10, c.Current || c.Best, c.Current ? Brushes.White : UI.Dim).HA(HorizontalAlignment.Center).At(1, 0),
                    UI.Text(c.Current ? "●" : c.Best ? "★" : "", 9, false, c.Current ? UI.Hex("#64D2FF") : UI.Hex("#30D158")).HA(HorizontalAlignment.Center).At(2, 0));
                grid.Children.Add(col);
            }
            return grid;
        }

        UIElement WifiSec()
        {
            var page = new StackPanel();
            var status = Status();

            // полная проверка сети с советами
            var health = new StackPanel();
            var channels = new StackPanel();
            async Task<string> CheckAll()
            {
                health.Children.Clear();
                var p = Loading("Проверяю сеть…");
                health.Children.Add(p);
                var (items, table, _) = await WifiTools.Health(s => Dispatcher.BeginInvoke(new Action(() => p.Text = I18n.T(s))));
                health.Children.Clear();
                int bad = items.Count(i => i.Level == 2), warn = items.Count(i => i.Level == 1);
                health.Children.Add(Info(bad > 0 ? $"Есть проблемы: {bad}, советов: {warn}" : warn > 0 ? $"Всё работает, советов по улучшению: {warn}" : "Сеть в отличном состоянии ✓",
                    bad > 0 ? "#FF453A" : warn > 0 ? "#FF9F0A" : "#30D158"));
                foreach (var it in items.OrderByDescending(i => i.Level))
                {
                    var (glyph, color) = it.Level switch { 2 => ("", "#FF453A"), 1 => ("", "#FF9F0A"), _ => ("", "#30D158") };
                    health.Children.Add(UI.Grid("a,*").M(14, 6, 14, 6).Add(
                        UI.Icon(glyph, 14, UI.Hex(color)).VA(VerticalAlignment.Top).M(0, 2, 10, 0),
                        UI.Col(new TextBlock { Text = I18n.T(it.Title), FontSize = 13, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap },
                               string.IsNullOrEmpty(it.Advice) ? null : new TextBlock { Text = I18n.T(it.Advice), FontSize = 12, Foreground = UI.Dim, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) }).At(0, 1)));
                }
                RenderChannels(table);
                return null;
            }
            void RenderChannels(List<ChannelLoad> table)
            {
                channels.Children.Clear();
                channels.Children.Add(Info(I18n.T("Свободные каналы") + " — " + WifiTools.FreeChannels(table), "#9FD0FF"));
                channels.Children.Add(UI.Text("2,4 ГГц", 12, true, UI.Dim).M(14, 4, 0, 0));
                channels.Children.Add(ChannelBars(table, "2,4 ГГц"));
                channels.Children.Add(UI.Text("5 ГГц", 12, true, UI.Dim).M(14, 6, 0, 0));
                channels.Children.Add(ChannelBars(table, "5 ГГц"));
                channels.Children.Add(Info("● — твой канал, ★ — самый свободный из рекомендуемых. На 2,4 ГГц выбирай только 1, 6 или 11 — остальные перекрываются. На 5 ГГц каналы 52–144 — «радарные» (DFS), там бывают короткие обрывы."));
            }
            page.Children.Add(Group("Проверка сети и советы",
                Item("Проверить всё", "Сигнал и скорость соединения, диапазон (есть ли у роутера 5 ГГц), загрузка канала, защита и WPS, стандарт роутера против адаптера, " +
                                      "задержки и потери до роутера и интернета, скорость твоего DNS против Cloudflare и Google, свежесть драйвера, лимитное подключение. По каждому пункту — совет.",
                     Safety.Safe, Act("Проверить всё", CheckAll, null, true)),
                health));
            page.Children.Add(Group("Загрузка каналов", Item("Сканировать каналы", "Сколько сетей на каждом канале и насколько они мешают. Свободный канал выставляется в настройках роутера.", Safety.Safe,
                Act("Сканировать", async () =>
                {
                    channels.Children.Clear();
                    channels.Children.Add(Loading("Сканирую эфир…"));
                    var aps = await WifiTools.Nearby();
                    RenderChannels(WifiTools.ChannelTable(aps, await WifiTools.Current()));
                    return null;
                })), channels));

            var speed = Info("");
            page.Children.Add(Group("Скорость интернета",
                Item("Замер скорости", "Скачивание и задержка через серверы Cloudflare — без рекламы и регистрации.", Safety.Safe,
                     Act("Замерить", async () => { speed.Text = I18n.T("Замеряю…"); speed.Text = I18n.T(await ApiTools.SpeedTest()); return null; })),
                speed));

            var link = new StackPanel();
            link.Children.Add(Loading());
            async Task LoadLink()
            {
                var l = await WifiTools.Current();
                link.Children.Clear();
                if (l == null) { link.Children.Add(Info(WifiTools.HasAdapter ? "Wi-Fi не подключён" : "Wi-Fi адаптер не найден")); return; }
                link.Children.Add(Table(new[]
                {
                    ("Сеть", l.Ssid), ("Сигнал", $"{l.Signal}% · {l.Rssi} dBm · {I18n.T(WifiTools.SignalWord(l.Rssi))}"),
                    ("Диапазон и канал", $"{l.Band} · {I18n.T("канал")} {l.Channel}"), ("Стандарт", l.Standard),
                    ("Скорость соединения", $"↓ {l.RxMbps:0} · ↑ {l.TxMbps:0} " + I18n.T("Мбит/с")), ("Защита", I18n.T(l.Security)),
                    ("Точка доступа (BSSID)", l.Bssid), ("Шлюз (роутер)", WifiTools.Gateway()?.ToString() ?? "—"), ("Адаптер", l.Adapter),
                }));
                if (l.Security is "Открытая" or "WEP")
                    link.Children.Add(Info("Сеть без надёжной защиты: всё, что не идёт по https, могут подсмотреть. Включай VPN.", "#FF9F0A"));
                if (l.Band.StartsWith("2") && l.Rssi > -60)
                    link.Children.Add(Info("Подключение на 2,4 ГГц при хорошем сигнале — если у роутера есть 5 ГГц, подключись к ней: будет в разы быстрее.", "#9FD0FF"));
            }
            _ = LoadLink();
            link.Live(TimeSpan.FromSeconds(5), LoadLink);   // сигнал и скорость меняются — таблица обновляется сама
            page.Children.Add(Group("Подключение", link,
                Item("Переподключиться", "Отключиться и подключиться к той же сети заново — помогает, когда интернет «подвис», а значок Wi-Fi есть.", Safety.Safe,
                     Act("Переподключить", async () => { var r = await WifiTools.Reconnect(); await LoadLink(); return r; }, status)),
                Item("Настройки роутера", "Открыть страницу роутера в браузере (адрес шлюза) — там меняется пароль, канал, имя сети.", Safety.Safe,
                     UI.Pill("Открыть", WifiTools.OpenRouter)),
                Item("Поделиться сетью по QR-коду", "Windows 11 показывает пароль и QR-код в свойствах сети — наведи камеру телефона, и он подключится.", Safety.Safe,
                     UI.Pill("Открыть", () => SystemService.Run("ms-settings:network-wifi"))),
                Item("Мобильный хот-спот", "Раздать интернет с ноутбука на телефон или другой ноутбук.", Safety.Safe,
                     UI.Pill("Открыть", () => SystemService.Run("ms-settings:network-mobilehotspot")))));
            page.Children.Add(status);

            // качество связи
            var quality = new StackPanel();
            page.Children.Add(Group("Качество связи — где проблема",
                Item("Проверить", "20 пингов до роутера и столько же до интернета одновременно, плюс время DNS. Покажет, виноват Wi-Fi в квартире или провайдер.", Safety.Safe,
                     Act("Проверить", async () =>
                     {
                         quality.Children.Clear();
                         var p = Loading("Проверяю…");
                         quality.Children.Add(p);
                         var q = await WifiTools.TestQuality(s => Dispatcher.BeginInvoke(new Action(() => p.Text = I18n.T(s))));
                         quality.Children.Clear();
                         quality.Children.Add(Info(q.Verdict, q.Verdict.StartsWith("Отлично") || q.Verdict.StartsWith("Хорошо") ? "#30D158" : "#FF9F0A"));
                         quality.Children.Add(Info(q.Advice));
                         quality.Children.Add(Table(new[]
                         {
                             ("До роутера", $"{q.GwMs:0} мс · разброс {q.GwJitter:0} мс · потери {q.GwLoss:0}%"),
                             ("До интернета (1.1.1.1)", $"{q.NetMs:0} мс · разброс {q.NetJitter:0} мс · потери {q.NetLoss:0}%"),
                             ("DNS", q.DnsMs >= 0 ? $"{q.DnsMs:0} мс" : "не ответил"),
                         }));
                         return null;
                     }, null, true)),
                quality));

            // сети рядом
            var nets = new StackPanel();
            async Task<string> Scan()
            {
                nets.Children.Clear();
                nets.Children.Add(Loading("Сканирую эфир…"));
                var aps = await WifiTools.Nearby();
                var l = await WifiTools.Current();
                nets.Children.Clear();
                var (now, best) = WifiTools.ChannelAdvice(aps, l);
                if (now != "") nets.Children.Add(Info(now + ". " + best, "#9FD0FF"));
                if (aps.Count == 0) nets.Children.Add(Info("Сетей не видно. Если Wi-Fi включён — разреши доступ к местоположению: Параметры → Конфиденциальность → Расположение."));
                int b24 = aps.Count(a => a.Channel <= 14), b5 = aps.Count(a => a.Channel > 14);
                if (aps.Count > 0) nets.Children.Add(Info($"Видно точек доступа: {aps.Count} (2,4 ГГц — {b24}, 5/6 ГГц — {b5})"));
                foreach (var a in aps.Take(40))
                {
                    bool weak = a.Security is "Открытая" or "WEP";
                    nets.Children.Add(UI.Grid("*,a").M(14, 5, 14, 5).Add(
                        UI.Col(new TextBlock { Text = (a.Current ? "● " : "") + a.Ssid, FontSize = 13, FontWeight = FontWeights.SemiBold, Tag = "no-translate" },
                               UI.Text($"{a.Band} · {I18n.T("канал")} {a.Channel} · {a.Standard} · {I18n.T(a.Security)} · {a.Bssid}", 11, false, weak ? UI.Hex("#FF9F0A") : UI.Dim)),
                        UI.Text($"{WifiTools.Bars(a.Signal)}  {a.Signal}% · {a.Rssi} dBm", 12, true).At(0, 1)));
                }
                return null;
            }
            page.Children.Add(Group("Сети рядом и выбор канала",
                Item("Сканировать", "Все сети вокруг: сила сигнала, канал, стандарт и защита. По загрузке каналов подскажу, какой канал выбрать в роутере.", Safety.Safe,
                     Act("Сканировать", Scan)),
                nets));

            // устройства
            var devs = new StackPanel();
            page.Children.Add(Group("Кто подключён к моей сети",
                Item("Найти устройства", "Опрашивает адреса твоей домашней сети и показывает телефоны, ноутбуки, телевизоры и умные устройства. Незнакомое устройство — повод сменить пароль Wi-Fi.", Safety.Safe,
                     Act("Найти", async () =>
                     {
                         devs.Children.Clear();
                         var p = Loading("Ищу устройства…");
                         devs.Children.Add(p);
                         var list = await WifiTools.Devices(s => Dispatcher.BeginInvoke(new Action(() => p.Text = I18n.T(s))));
                         devs.Children.Clear();
                         devs.Children.Add(Info($"Устройств в сети: {list.Count}"));
                         foreach (var d in list)
                             devs.Children.Add(UI.Grid("*,a").M(14, 5, 14, 5).Add(
                                 UI.Col(new TextBlock { Text = string.IsNullOrEmpty(d.Name) ? d.Ip : d.Name, FontSize = 13, FontWeight = FontWeights.SemiBold, Tag = "no-translate" },
                                        UI.Text(string.Join(" · ", new[] { I18n.T(d.Kind), d.Ip, d.Mac }.Where(s => !string.IsNullOrEmpty(s)).Distinct()), 11, false, UI.Dim)),
                                 UI.Text(d.Ms >= 0 ? $"{d.Ms:0} мс" : "", 12, false, UI.Dim).At(0, 1)));
                         return null;
                     })),
                devs));

            // сохранённые сети: пароль, забыть
            var saved = new StackPanel();
            saved.Children.Add(Loading());
            _ = Tweaks.WifiProfiles().ContinueWith(t => Dispatcher.Invoke(() =>
            {
                saved.Children.Clear();
                foreach (var name in t.Result.Take(40))
                {
                    var n = name;
                    var pass = UI.Text("", 12.5, true);
                    pass.Tag = "no-translate";
                    UIElement row = null;
                    row = UI.Grid("*,a,a,a").M(14, 5, 14, 5).Add(new TextBlock { Text = n, FontSize = 13, Tag = "no-translate", VerticalAlignment = VerticalAlignment.Center },
                        pass.M(10, 0, 10, 0).At(0, 1),
                        UI.Pill("Пароль", async () =>
                        {
                            var p = await Tweaks.WifiPassword(n);
                            pass.Text = p ?? I18n.T("без пароля / недоступен");
                            if (p != null) ClipboardService.I.CopyBack(p);
                        }, false, "Показать и скопировать пароль").At(0, 2),
                        UI.Pill("Забыть", async () =>
                        {
                            var r = await WifiTools.Forget(n);
                            App.Island?.Toast("", "Wi-Fi", r);
                            if (r.Contains("забыта") && row != null) saved.Children.Remove(row);
                        }, false, "Windows перестанет подключаться к этой сети и удалит её пароль").M(6, 0, 0, 0).At(0, 3));
                    saved.Children.Add(row);
                }
                if (t.Result.Count == 0) saved.Children.Add(Loading("Сохранённых сетей нет"));
            }));
            page.Children.Add(Group("Сохранённые сети", saved));
            page.Children.Add(Note("Сканирование эфира и устройств только читает данные и ничего не меняет. Wi-Fi-страница острова показывает сигнал вживую — " +
                                   "походи с ноутбуком по квартире, и график покажет, где связь лучше."));
            return page;
        }

        // ------------------------------------------------------------------
        //  OSINT и проверки
        // ------------------------------------------------------------------

        UIElement OsintSec()
        {
            var page = new StackPanel();
            page.Children.Add(Note("Разведка по открытым источникам: проверка ссылок, сайтов, доменов и IP, утечек пароля и того, что спрятано в фото. " +
                                   "Используются только публичные данные и сервисы без ключей. Инструменты — для своей безопасности и проверки сайтов; " +
                                   "поиска и слежки за людьми здесь нет.").M(4, 0, 4, 14));

            // проверка ссылки
            var linkBox = UI.Box("https://bit.ly/… или любой адрес", 440);
            if (Osint.Pending != null) { linkBox.Text = Osint.Pending; Osint.Pending = null; }
            var linkRes = new StackPanel();
            async Task<string> CheckLink()
            {
                if (string.IsNullOrWhiteSpace(linkBox.Text)) return I18n.T("Вставь ссылку");
                linkRes.Children.Clear();
                linkRes.Children.Add(Loading("Проверяю ссылку…"));
                var r = await Osint.CheckLink(linkBox.Text);
                linkRes.Children.Clear();
                linkRes.Children.Add(Info(r.Verdict, r.Risk >= 6 ? "#FF453A" : r.Risk >= 3 ? "#FF9F0A" : "#30D158"));
                linkRes.Children.Add(Table(new[] { ("Ведёт на", r.Final) }.Concat(r.Chain.Count > 2 ? new[] { ("Цепочка", string.Join("\n→ ", r.Chain)) } : Array.Empty<(string, string)>())));
                foreach (var (bad, text) in r.Signals)
                    linkRes.Children.Add(Info((bad ? "⚠ " : "✓ ") + text, bad ? "#FF9F0A" : null).M(14, 1, 14, 1));
                return null;
            }
            page.Children.Add(Group("Проверка ссылки — фишинг и сокращатели",
                Wide("Ссылка", "Раскрою короткую ссылку, не открывая её, и проверю признаки обмана: возраст домена, подмену букв, бренд в чужом адресе, историю в веб-архиве.",
                     UI.Row(linkBox, Act("Проверить", CheckLink, null, true).M(8, 0, 0, 0))),
                linkRes));

            // домен
            var domBox = UI.Box("example.com", 300);
            var domRes = new StackPanel();
            Func<Task<string>> Show(string title, Func<string, Task<UIElement>> work) => async () =>
            {
                if (string.IsNullOrWhiteSpace(domBox.Text)) return I18n.T("Введи домен или адрес сайта");
                if (!Osint.LooksLikeHost(domBox.Text))
                {
                    domRes.Children.Clear();
                    domRes.Children.Add(Info("Это не похоже на домен или адрес сайта — например, example.com или https://example.com/page", "#FF9F0A"));
                    return null;
                }
                domRes.Children.Clear();
                domRes.Children.Add(Loading(I18n.T(title) + "…"));
                try { var el = await work(domBox.Text.Trim()); domRes.Children.Clear(); domRes.Children.Add(el); }
                catch (Exception e) { domRes.Children.Clear(); domRes.Children.Add(Info(I18n.T("Не получилось: ") + Integrations.Reason(e), "#FF9F0A")); }
                return null;
            };
            var domButtons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            domButtons.Children.Add(Act("WHOIS", Show("Узнаю владельца домена", async d => Table(await Osint.Whois(d)))).M(0, 0, 6, 6));
            domButtons.Children.Add(Act("DNS-записи", Show("Читаю DNS", async d => Table(await Osint.DnsRecords(d)))).M(0, 0, 6, 6));
            domButtons.Children.Add(Act("Сертификат", Show("Проверяю сертификат", async d => Table(await Osint.Certificate(d)))).M(0, 0, 6, 6));
            domButtons.Children.Add(Act("Поддомены", Show("Ищу поддомены в журналах сертификатов", async d =>
            {
                var subs = await Osint.Subdomains(d);
                return Table(new[] { ("Найдено", subs.Count.ToString()), ("Поддомены", string.Join("\n", subs.Take(120))) });
            })).M(0, 0, 6, 6));
            domButtons.Children.Add(Act("Веб-архив", Show("Смотрю веб-архив", async d =>
            {
                var a = await Osint.Wayback(d);
                var sp = (StackPanel)Table(new[] { ("Первый снимок", a.First == "" ? "нет" : a.First), ("Последний снимок", a.Last == "" ? "нет" : a.Last), ("Снимков (по месяцам)", a.Count + (a.Count >= 500 ? "+" : "")) });
                sp.Children.Add(UI.Pill("Открыть историю сайта", () => SystemService.Run(a.Url)).HA(HorizontalAlignment.Left).M(0, 6, 0, 0));
                return sp;
            })).M(0, 0, 6, 6));
            domButtons.Children.Add(Act("Безопасность сайта", Show("Проверяю заголовки", async d => Table(await Osint.Headers(d)))).M(0, 0, 6, 6));
            page.Children.Add(Group("Домен и сайт",
                Wide("Домен", "Кто и когда зарегистрировал, DNS и почта, сертификат, поддомены (Certificate Transparency), как сайт выглядел раньше и насколько он защищён.",
                     UI.Col(domBox, domButtons)),
                domRes));

            // IP
            var ipBox = UI.Box("IP или домен — пусто: мой внешний IP", 300);
            var ipRes = new StackPanel();
            page.Children.Add(Group("IP-адрес",
                Wide("IP", "Страна и город, провайдер и сеть (AS), это дом, хостинг, VPN или мобильная сеть, обратное имя.",
                     UI.Row(ipBox, Act("Узнать", async () =>
                     {
                         ipRes.Children.Clear();
                         if (!string.IsNullOrWhiteSpace(ipBox.Text) && !Osint.LooksLikeHost(ipBox.Text))
                         {
                             ipRes.Children.Add(Info("Это не похоже на IP-адрес или домен — например, 8.8.8.8 или example.com", "#FF9F0A"));
                             return null;
                         }
                         ipRes.Children.Add(Loading());
                         try { var r = await Osint.Ip(ipBox.Text); ipRes.Children.Clear(); ipRes.Children.Add(Table(r)); }
                         catch (Exception e) { ipRes.Children.Clear(); ipRes.Children.Add(Info(I18n.T("Не получилось: ") + Integrations.Reason(e), "#FF9F0A")); }
                         return null;
                     }, null, true).M(8, 0, 0, 0))),
                ipRes));

            // пароль
            var pass = new PasswordBox { Width = 300, Foreground = Brushes.White, CaretBrush = Brushes.White, Background = UI.Hex("#1FFFFFFF"), BorderThickness = new Thickness(0), Padding = new Thickness(8, 5, 8, 5), FontSize = 12.5 };
            var passRes = Info("");
            page.Children.Add(Group("Утечка пароля",
                Wide("Пароль", "Проверю по базе Have I Been Pwned (миллиарды утёкших паролей). Пароль никуда не отправляется: наружу уходят только первые 5 символов его хэша.",
                     UI.Row(pass, Act("Проверить", async () =>
                     {
                         if (pass.Password.Length == 0) return I18n.T("Введи пароль");
                         try
                         {
                             long n = await Osint.PasswordLeaks(pass.Password);
                             pass.Clear();
                             passRes.Foreground = n > 0 ? UI.Hex("#FF453A") : UI.Hex("#30D158");
                             passRes.Text = n > 0 ? I18n.T("Этот пароль есть в утечках: ") + n.ToString("N0", Fmt.Ru) + I18n.T(" раз. Смени его везде, где он используется.")
                                                  : I18n.T("В известных утечках не найден ✓ (это не гарантия надёжности — используй длинные разные пароли)");
                         }
                         catch (Exception e) { passRes.Text = I18n.T("Не получилось: ") + Integrations.Reason(e); }
                         return null;
                     }, null, true).M(8, 0, 0, 0))),
                passRes));

            // фото
            var photoRes = new StackPanel();
            page.Children.Add(Group("Фото: метаданные и местоположение",
                Item("Что спрятано в фото", "Камера, дата и — главное — GPS-координаты места съёмки. Перед отправкой фото незнакомцам проверь и сохрани чистую копию.", Safety.Safe,
                     UI.Pill("Выбрать фото…", () =>
                     {
                         var dlg = new Microsoft.Win32.OpenFileDialog { Filter = I18n.T("Фото") + "|*.jpg;*.jpeg;*.tif;*.tiff;*.png;*.heic" };
                         if (dlg.ShowDialog(this) != true) return;
                         photoRes.Children.Clear();
                         try
                         {
                             var m = Osint.Photo(dlg.FileName);
                             photoRes.Children.Add(Table(m.Items));
                             if (m.Lat != null)
                             {
                                 photoRes.Children.Add(Info("В фото есть координаты места съёмки — любой, кто получит файл, увидит, где оно сделано.", "#FF9F0A"));
                                 var lat = m.Lat.Value.ToString("0.00000", System.Globalization.CultureInfo.InvariantCulture);
                                 var lon = m.Lon.Value.ToString("0.00000", System.Globalization.CultureInfo.InvariantCulture);
                                 photoRes.Children.Add(UI.Pill("Показать на карте", () => SystemService.Run($"https://www.openstreetmap.org/?mlat={lat}&mlon={lon}#map=16/{lat}/{lon}")).HA(HorizontalAlignment.Left).M(14, 0, 0, 6));
                             }
                             var file = dlg.FileName;
                             photoRes.Children.Add(UI.Pill("Сохранить копию без метаданных", () =>
                             {
                                 try { var outp = Osint.StripPhoto(file, m.Orientation); App.Island?.Toast("", "Чистая копия сохранена", System.IO.Path.GetFileName(outp)); System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{outp}\"")?.Dispose(); }
                                 catch (Exception e) { App.Island?.Toast("", "Не получилось", e.Message); }
                             }, true).HA(HorizontalAlignment.Left).M(14, 0, 0, 10));
                         }
                         catch (Exception e) { photoRes.Children.Add(Info(I18n.T("Не удалось прочитать файл: ") + e.Message, "#FF9F0A")); }
                     })),
                photoRes));

            // файл
            var fileRes = new StackPanel();
            page.Children.Add(Group("Файл: проверка на вирусы",
                Item("Хэш файла и VirusTotal", "Посчитаю отпечаток SHA-256 и открою отчёт VirusTotal по нему — если файл уже проверяли 70 антивирусов, увидишь результат. Сам файл никуда не загружается.", Safety.Safe,
                     UI.Pill("Выбрать файл…", async () =>
                     {
                         var dlg = new Microsoft.Win32.OpenFileDialog();
                         if (dlg.ShowDialog(this) != true) return;
                         fileRes.Children.Clear();
                         fileRes.Children.Add(Loading("Считаю хэш…"));
                         var path = dlg.FileName;
                         var hash = await Task.Run(() => Osint.Sha256(path));
                         fileRes.Children.Clear();
                         fileRes.Children.Add(Table(new[] { ("Файл", System.IO.Path.GetFileName(path)), ("SHA-256", hash) }));
                         fileRes.Children.Add(UI.Row(
                             UI.Pill("Открыть отчёт VirusTotal", () => SystemService.Run("https://www.virustotal.com/gui/file/" + hash), true),
                             UI.Pill("Скопировать хэш", () => { ClipboardService.I.CopyBack(hash); App.Island?.Toast("", "Скопировано", hash[..16] + "…"); }).M(8, 0, 0, 0)).M(14, 0, 0, 10));
                     })),
                fileRes));
            return page;
        }
    }
}
