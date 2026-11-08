using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using NIsland.Core;
using NIsland.Pages;
using NIsland.Services;

namespace NIsland
{
    /// <summary>Разделы «Интеграции», «VPN» и «Резервная копия настроек».</summary>
    public partial class SettingsWindow
    {
        static readonly HashSet<string> _openHub = new();

        // ------------------------------------------------------------------
        //  Интеграции
        // ------------------------------------------------------------------

        UIElement HubSec()
        {
            var groups = new (string Title, string[] Keys)[]
            {
                ("Телефон и уведомления", new[] { "telegram", "ntfy", "pushover", "pushbullet", "gotify", "discord", "slack", "matrix", "webhook" }),
                ("Украина", new[] { "novaposhta", "rates" }),
                ("Почта, работа и задачи", new[] { "mail", "ics", "todoist", "notion", "trello", "linear", "jira", "github", "gitlab", "releases", "toggl", "wakatime" }),
                ("Дом и сайты", new[] { "ha", "hue", "uptime" }),
                ("Новости, соцсети, рынки", new[] { "rss", "hn", "mastodon", "bluesky", "stocks", "onthisday" }),
                ("Игры", new[] { "games", "deals", "steam", "twitch" }),
                ("Наука и природа", new[] { "quakes", "kp", "apod" }),
                ("Музыка", new[] { "listenbrainz", "lastfm" }),
            };
            var panel = new StackPanel();
            int on = Integrations.All.Count(i => i.Enabled);
            panel.Children.Add(Group(null,
                Row("Страница «Сервисы» в острове", "Данные подключённых сервисов с кнопками: свет, задачи, события, курсы, посылки…", Toggle(nameof(Settings.PgHub)))));
            // поиск по сервисам: их больше 30
            var find = UI.Box(I18n.T("Найти сервис: почта, банк, свет, задачи…"), 360);
            panel.Children.Add(UI.Row(find, UI.Text($"{Integrations.All.Count} {I18n.T("сервисов")} · {I18n.T("подключено")}: {on}", 12, false, UI.Dim).M(12, 0, 0, 0)).M(4, 0, 0, 10));
            var cards = new List<(UIElement Card, UIElement Header, Integration It)>();
            foreach (var (title, keys) in groups)
            {
                var header = UI.Text(title, 12, true, UI.Dim).M(4, 6, 0, 6);
                panel.Children.Add(header);
                foreach (var k in keys)
                    if (Integrations.Get(k) is { } it) { var c = HubCard(it); panel.Children.Add(c); cards.Add((c, header, it)); }
            }
            // свои интеграции: шаблоны + карточки
            var myHeader = UI.Text("Свои интеграции", 12, true, UI.Dim).M(4, 10, 0, 6);
            panel.Children.Add(myHeader);
            var newName = UI.Box(I18n.T("Название (необязательно)"), 220);
            var tpl = new WrapPanel();
            foreach (var t in CustomHubs.Templates)
            {
                var tt = t;
                var b = UI.Pill(t.Name, () =>
                {
                    var hub = CustomHubs.Create(tt, newName.Text);
                    _openHub.Add(hub.Key);
                    Open("hub");
                }, t.Kind != "list" || t.Values.Length == 0, I18n.T(t.Note));
                b.Margin = new Thickness(0, 0, 6, 6);
                tpl.Children.Add(b);
            }
            panel.Children.Add(Group(null, Wide("Добавить свою", "Любой сервис с JSON API, число с порогами (курс, температура), изменение страницы сайта или проверка «сайт работает?». Выбери шаблон — появится карточка с полями ниже",
                UI.Col(newName, tpl.M(0, 8, 0, 0)))));
            foreach (var it in Integrations.All.OfType<CustomHub>().ToList())
            {
                var c = HubCard(it);
                panel.Children.Add(c);
                cards.Add((c, myHeader, it));
            }
            find.TextChanged += (_, __) =>
            {
                var q = find.Text.Trim();
                foreach (var (c, _, it) in cards)
                    c.Show(q == "" || (it.Name + " " + I18n.T(it.Name) + " " + it.Desc + " " + I18n.T(it.Desc) + " " + it.Key).Contains(q, StringComparison.OrdinalIgnoreCase));
                foreach (var h in cards.Select(x => x.Header).Distinct())
                    h.Show(cards.Any(x => x.Header == h && x.Card.Visibility == Visibility.Visible));
            };
            panel.Children.Add(Note("Ключи и токены хранятся только на этом компьютере в зашифрованном виде (DPAPI) и уходят только в свой сервис. " +
                                    "Пересылка событий работает и в режиме «Не беспокоить» — так уведомление о таймере или звонке дойдёт до телефона.").M(4, 8, 4, 0));
            return panel;
        }

        UIElement HubCard(Integration it)
        {
            var color = ColorUtil.Parse(it.Color, Colors.DodgerBlue);
            var icon = new Border
            {
                Width = 30, Height = 30, CornerRadius = new CornerRadius(9),
                Background = new LinearGradientBrush(ColorUtil.WithAlpha(color, 0.95), ColorUtil.WithAlpha(color, 0.65), 90),
                Child = UI.Icon(it.Glyph, 14, Brushes.White),
            };
            var status = new TextBlock { FontSize = 11, Foreground = UI.Dim, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) };
            void UpdStatus()
            {
                status.Text = !it.Enabled ? "" : !it.Ready ? I18n.T("Заполни поля ниже") : I18n.T(it.Status ?? "");
                status.Foreground = it.Enabled && it.Ready && !it.Ok && it.Status.StartsWith("Ошибка") ? UI.Hex("#FF9F0A") : UI.Dim;
                status.Visibility = status.Text == "" ? Visibility.Collapsed : Visibility.Visible;
            }
            UpdStatus();
            var enable = new CheckBox { Style = UI.St("Switch"), IsChecked = it.Enabled, VerticalAlignment = VerticalAlignment.Center };
            // статус сервиса («Обновлено 14:05», «Ошибка: …») меняется в фоне — карточка следит за ним сама
            status.Live(TimeSpan.FromSeconds(4), () => { UpdStatus(); enable.IsChecked = it.Enabled; return System.Threading.Tasks.Task.CompletedTask; });
            var body = new StackPanel { Margin = new Thickness(54, 0, 14, 12) };
            bool open = it.Enabled || _openHub.Contains(it.Key);
            body.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            var chevron = UI.Icon(open ? "" : "", 10, UI.Dim);
            var head = UI.Grid("a,*,a,a").M(12, 10, 14, 10).Add(
                icon,
                UI.Col(new TextBlock { Text = it.Name, FontSize = 13.5, FontWeight = FontWeights.SemiBold },
                       new TextBlock { Text = it.Desc, FontSize = 12, Foreground = UI.Dim, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) },
                       status).At(0, 1).M(12, 0, 10, 0),
                chevron.At(0, 2).M(0, 0, 12, 0),
                enable.At(0, 3));
            head.Background = Brushes.Transparent;
            head.Cursor = Cursors.Hand;
            void Toggle(bool show)
            {
                body.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                chevron.Text = show ? "" : "";
                if (show) _openHub.Add(it.Key); else _openHub.Remove(it.Key);
            }
            head.MouseLeftButtonUp += (_, e) => { if (e.OriginalSource is DependencyObject d && FindUp<CheckBox>(d) == null) Toggle(body.Visibility != Visibility.Visible); };
            enable.Click += (_, __) =>
            {
                Integrations.SetEnabled(it, enable.IsChecked == true);
                if (enable.IsChecked == true) Toggle(true);
                UpdStatus();
            };

            if (it is CustomHub ch)
            {
                var nm = UI.Box("", 260);
                nm.Text = ch.Def.Name;
                nm.LostFocus += (_, __) => CustomHubs.Rename(ch, nm.Text);
                body.Children.Add(UI.Grid("*,a").M(0, 0, 0, 8).Add(UI.Col(UI.Text("Название", 12, true).M(0, 0, 0, 3), nm),
                    UI.Pill("Удалить интеграцию", () => { CustomHubs.Remove(ch); Open("hub"); }).At(0, 1).VA(VerticalAlignment.Bottom)));
            }
            if (it.Help != null) body.Children.Add(new TextBlock { Text = it.Help, FontSize = 12, Foreground = UI.Hex("#9FD0FF"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
            foreach (var f in it.Fields) body.Children.Add(FieldRow(it, f));

            if (it.CanSend != null)
            {
                body.Children.Add(UI.Text("Что пересылать", 12, true, UI.Dim).M(0, 6, 0, 4));
                var wrap = new WrapPanel();
                var on = Integrations.Events(it);
                foreach (var (key, name) in Integrations.EventKinds)
                {
                    var k = key;
                    var cb = new CheckBox { Content = name, IsChecked = on.Contains(key), Foreground = Brushes.White, Margin = new Thickness(0, 0, 14, 6), FontSize = 12, VerticalContentAlignment = VerticalAlignment.Center };
                    cb.Click += (_, __) => Integrations.SetEvent(it, k, cb.IsChecked == true);
                    wrap.Children.Add(cb);
                }
                body.Children.Add(wrap);
                if (it.Key is "telegram" or "ntfy" or "discord" or "webhook")
                    body.Children.Add(new TextBlock { Text = "«Уведомления приложений» отправляют наружу текст чужих уведомлений — включай, только если тебе это нужно.", FontSize = 11, Foreground = UI.Dim, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });
            }

            var result = new TextBlock { FontSize = 12, Foreground = UI.Dim, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
            var test = UI.Pill(UI.IconLabel("", "Проверить", 11), null, true);
            test.Click += async (_, __) =>
            {
                if (!it.Ready) { result.Text = I18n.T("Сначала заполни обязательные поля"); return; }
                test.IsEnabled = false;
                result.Foreground = UI.Dim;
                result.Text = I18n.T("Проверяю…");
                try
                {
                    var msg = await it.Test();
                    it.Ok = true;
                    it.Status = msg;
                    result.Foreground = UI.Hex("#30D158");
                    result.Text = I18n.T(msg);
                    if (!it.Enabled) { Integrations.SetEnabled(it, true); enable.IsChecked = true; }
                }
                catch (Exception e)
                {
                    it.Ok = false;
                    result.Foreground = UI.Hex("#FF9F0A");
                    result.Text = I18n.T("Не получилось: ") + Integrations.Reason(e);
                }
                test.IsEnabled = true;
                UpdStatus();
                Integrations.RaiseChanged();
            };
            var buttons = UI.Row(test, it.HelpUrl != null ? UI.Pill(UI.IconLabel("", "Где взять ключ", 11), () => SystemService.Run(it.HelpUrl)).M(8, 0, 0, 0) : null);
            buttons.Margin = new Thickness(0, 8, 0, 0);
            body.Children.Add(buttons);
            body.Children.Add(result);

            return new Border
            {
                Background = UI.Hex("#14FFFFFF"), CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 0, 0, 8),
                BorderBrush = new LinearGradientBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x08, 0xFF, 0xFF, 0xFF), 90), BorderThickness = new Thickness(1),
                Child = UI.Col(head, body),
            };
        }

        static T FindUp<T>(DependencyObject d) where T : DependencyObject
        {
            while (d != null) { if (d is T t) return t; d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d); }
            return null;
        }

        static UIElement FieldRow(Integration it, HubField f)
        {
            var label = new TextBlock { Text = f.Label + (f.Required ? "" : I18n.T(" · необязательно")), FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 3) };
            FrameworkElement input;
            switch (f.Kind)
            {
                case "bool":
                {
                    var cb = new CheckBox { Style = UI.St("Switch"), IsChecked = it.V(f.Key) == "1", VerticalAlignment = VerticalAlignment.Center };
                    cb.Click += (_, __) => Integrations.SetValue(it, f.Key, cb.IsChecked == true ? "1" : "0");
                    return UI.Grid("*,a").M(0, 4, 0, 4).Add(UI.Col(new TextBlock { Text = f.Label, FontSize = 12, FontWeight = FontWeights.SemiBold },
                        string.IsNullOrEmpty(f.Hint) ? null : new TextBlock { Text = f.Hint, FontSize = 11, Foreground = UI.Dim }), cb.At(0, 1));
                }
                case "secret":
                {
                    var pb = new PasswordBox
                    {
                        Width = 360, Foreground = Brushes.White, CaretBrush = Brushes.White, Background = UI.Hex("#1FFFFFFF"), BorderThickness = new Thickness(0),
                        Padding = new Thickness(8, 5, 8, 5), FontSize = 12.5, HorizontalAlignment = HorizontalAlignment.Left,
                    };
                    var state = new TextBlock { FontSize = 11, Foreground = UI.Dim, Margin = new Thickness(0, 3, 0, 0) };
                    void Upd() => state.Text = it.V(f.Key) != "" ? I18n.T("Сохранено (зашифровано) ✓ Введи новое значение, чтобы заменить") : I18n.T(f.Hint ?? "");
                    Upd();
                    void Commit() { if (pb.Password.Length == 0) return; Integrations.SetValue(it, f.Key, pb.Password); pb.Clear(); Upd(); }
                    pb.LostFocus += (_, __) => Commit();
                    pb.KeyDown += (_, e) => { if (e.Key == Key.Enter) Commit(); };
                    var clear = UI.IconBtn("", () => { Integrations.SetValue(it, f.Key, ""); Upd(); }, 10, 26, "Удалить");
                    input = UI.Col(UI.Row(pb, clear.M(4, 0, 0, 0)), state);
                    break;
                }
                case "multi":
                {
                    var tb = new TextBox
                    {
                        AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 64, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                        Foreground = Brushes.White, CaretBrush = Brushes.White, FontSize = 12, Template = Ago.PlainBox(), Tag = f.Hint, Text = it.V(f.Key),
                    };
                    tb.LostFocus += (_, __) => Integrations.SetValue(it, f.Key, tb.Text);
                    input = tb;
                    break;
                }
                default:
                {
                    var tb = UI.Box(f.Hint, 360);
                    tb.HorizontalAlignment = HorizontalAlignment.Left;
                    tb.Text = it.V(f.Key);
                    tb.LostFocus += (_, __) => Integrations.SetValue(it, f.Key, tb.Text);
                    tb.KeyDown += (_, e) => { if (e.Key == Key.Enter) Integrations.SetValue(it, f.Key, tb.Text); };
                    input = tb;
                    break;
                }
            }
            return UI.Col(label, input).M(0, 0, 0, 4);
        }

        // ------------------------------------------------------------------
        //  VPN
        // ------------------------------------------------------------------

        UIElement VpnSec()
        {
            var found = new StackPanel();
            void Render()
            {
                found.Children.Clear();
                if (Vpn.DetectedAt == default) { found.Children.Add(Row("Ищу установленные VPN…", null, null)); return; }
                if (Vpn.Items.Count == 0)
                    found.Children.Add(Row("VPN-программы не найдены", "Поставь любой из поддерживаемых VPN — он появится здесь и в острове сам", null));
                foreach (var i in Vpn.Items)
                {
                    var item = i;
                    found.Children.Add(Row(i.Name, (i.CanControl ? I18n.T("Включается прямо из острова") : I18n.T("Остров показывает состояние, управление — в приложении"))
                                                   + (i.Note != null ? " · " + I18n.T(i.Note) : "") + " · " + I18n.T(i.State switch { VpnState.On => "сейчас включён", VpnState.Off => "сейчас выключен", _ => "состояние неизвестно" }),
                        UI.Pill("Открыть", () => Vpn.Open(item))));
                }
            }
            Render();
            if (Vpn.DetectedAt == default || (DateTime.Now - Vpn.DetectedAt).TotalMinutes > 2) _ = Rescan();
            found.Live(TimeSpan.FromSeconds(10), async () => { await Vpn.RefreshAll(); Render(); });   // включили VPN в другой программе — видно и здесь
            async System.Threading.Tasks.Task Rescan() { await Vpn.Detect(); Render(); }

            return Panel(
                Group(null,
                    Row("Страница VPN в острове", "Состояние, внешний IP и страна, переключатели VPN", Toggle(nameof(Settings.PgVpn))),
                    Row("Сообщать о включении и выключении", "Всплывашка, когда VPN подключился или отвалился — в любой программе", Toggle(nameof(Settings.VpnNotify))),
                    Row("Замочек в острове", "🔒 справа в свёрнутом острове, пока VPN включён", Toggle(nameof(Settings.VpnInNotch)))),
                UI.Grid("*,a").M(4, 0, 0, 6).Add(UI.Text("Найдено на этом компьютере", 12, true, UI.Dim),
                    UI.Pill(UI.IconLabel("", "Искать заново", 11), () => _ = Rescan()).At(0, 1)),
                Group(null, found),
                Note("Включаются из острова: Cloudflare WARP, NordVPN, Mullvad, Windscribe, Tailscale, Private Internet Access, конфиги OpenVPN GUI, " +
                     "туннели WireGuard (спросит права администратора) и VPN-подключения Windows. Proton VPN, Surfshark, ExpressVPN, AdGuard VPN, Amnezia, " +
                     "Hotspot Shield, Outline и другие — остров видит, включены ли они, и открывает приложение. Управление идёт только через официальные " +
                     "команды самих программ — N-Island не меняет их настройки."));
        }

        // ------------------------------------------------------------------
        //  Быстрый поиск
        // ------------------------------------------------------------------

        UIElement SpotlightSec() => Panel(
            Group(null,
                Row("Быстрый поиск", "Ctrl+Alt+Space — окно поиска по центру экрана, как Spotlight на Mac", Toggle(nameof(Settings.Spotlight))),
                Row("Открыть сейчас", IslandWindow.BusyHotkeys.Contains("Ctrl+Alt+Space") ? I18n.T("Ctrl+Alt+Space уже занята другой программой — открывай из трея") : null,
                    UI.Pill("Открыть", () => SpotlightWindow.Toggle(), true)),
                Row("Обновить список программ", $"{AppIndex.Apps.Count} программ и приложений из меню «Пуск» и Microsoft Store",
                    UI.Pill("Обновить", () => { AppIndex.Refresh(); App.Island.Toast("", "Список программ обновляется", null); })),
                Row("Поисковик", null, Seg(nameof(Settings.SearchEngine), ("google", "Google"), ("yandex", "Яндекс"), ("bing", "Bing")))),
            Group("Что умеет",
                Wide("Программы и приложения", "Пиши часть названия, первые буквы («vsc» — Visual Studio Code) или даже в другой раскладке («ntktuhfv» — Telegram). Часто запускаемое поднимается выше.", new TextBlock()),
                Wide("Посчитать и перевести", "«2+2*3», «15% от 200» в калькуляторе, «100 usd в грн», «10 км в мили», «72 f в c», «пер hello» — перевод, Enter копирует результат.", new TextBlock()),
                Wide("Команды острова", "«таймер 25», «заметка купить хлеб», «задача позвонить маме», «режим ночь», названия страниц острова, «пипетка», «блокировка».", new TextBlock()),
                Wide("Параметры Windows и файлы", "Wi-Fi, Bluetooth, дисплей, звук, обновления, автозагрузка… и недавние файлы.", new TextBlock()),
                Wide("ИИ прямо в окне", "Tab или «? вопрос» — ответ появится тут же, без открытия чата.", new TextBlock())));

        // ------------------------------------------------------------------
        //  Без дублей
        // ------------------------------------------------------------------

        UIElement DedupeSec()
        {
            string osdNote = Dedupe.OsdSupported == false
                ? "В этой версии Windows окно громкости встроено в оболочку — его нельзя скрыть со стороны. Остров всё равно показывает свою громкость"
                : "Остров показывает свою громкость и яркость, а системное окно Windows больше не всплывает. Сработает после первого нажатия клавиши громкости";
            return Panel(
                Group(null,
                    Row("Баннеры уведомлений Windows", "Сообщения показывает остров, а Windows кладёт их только в центр уведомлений (Win+N) — без двойных всплывашек. " +
                        "Системные предупреждения (безопасность, батарея, обновления) остаются" + (Dedupe.BannersHidden > 0 ? " · " + I18n.T("выключено у приложений: ") + Dedupe.BannersHidden : ""),
                        Toggle(nameof(Settings.DedupeBanners))),
                    Row("Окно «Автозапуск» для флешек", "При подключении флешки остров сам предлагает «Открыть», окно Windows «Выберите действие» не появляется", Toggle(nameof(Settings.DedupeAutoplay))),
                    Row("Системное окно громкости и яркости", osdNote, Toggle(nameof(Settings.DedupeOsd))),
                    Row("Только одна всплывашка о подключении", "Наушники, флешки, Bluetooth: дубли Windows о подключении не показываются в острове", Toggle(nameof(Settings.DevicePriority)))),
                (S.DedupeBanners && (!S.Notifications || S.NotifyOnlyCalls))
                    ? Note("Уведомления в острове сейчас выключены, поэтому баннеры Windows оставлены как есть — иначе сообщения пропали бы совсем.")
                    : null,
                Note("Всё обратимо: исходные значения сохраняются, а когда N-Island закрывается, Windows снова показывает свои баннеры и окна — " +
                     "без острова ничего не пропадёт. Выключи переключатель — и всё вернётся как было."));
        }

        // ------------------------------------------------------------------
        //  Резервная копия и сброс настроек
        // ------------------------------------------------------------------

        /// <summary>Что не трогаем при импорте и сбросе: служебное и привязанное к этому ПК.</summary>
        static readonly HashSet<string> KeepOnReset = new()
        {
            nameof(Settings.FirstRun), nameof(Settings.PresetMigrated), nameof(Settings.LastUpdateCheck), nameof(Settings.SkippedVersion),
            nameof(Settings.LastCleanup), nameof(Settings.CleanedTotal), nameof(Settings.AiKeys), nameof(Settings.Integrations), nameof(Settings.Language),
            nameof(Settings.StartWithWindows), nameof(Settings.LiteModeSetting),
        };

        static IEnumerable<PropertyInfo> CopyableProps() => typeof(Settings).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite && p.GetCustomAttribute<JsonIgnoreAttribute>() == null);

        /// <summary>Переносим значения по одному — каждое изменение сразу применяется островом.</summary>
        static int ApplyFrom(Settings src, bool keepLocal)
        {
            int n = 0;
            foreach (var p in CopyableProps())
            {
                if (keepLocal && KeepOnReset.Contains(p.Name)) continue;
                var v = p.GetValue(src);
                if (Equals(v, p.GetValue(S))) continue;
                try { p.SetValue(S, v); n++; } catch { }
            }
            Store.SaveSettings();
            return n;
        }

        UIElement BackupGroup() => Group("Резервная копия настроек",
            Row("Сохранить в файл", "Все настройки острова — чтобы перенести на другой ПК или вернуть после переустановки. Ключи ИИ и сервисов не переносятся: они зашифрованы для этого ПК",
                UI.Pill("Сохранить…", ExportSettings)),
            Row("Загрузить из файла", "Применится сразу, без перезапуска", UI.Pill("Загрузить…", ImportSettings)),
            Row("Сбросить настройки", "Вернуть всё как после установки. Ключи, сервисы, язык и автозапуск останутся", UI.Pill("Сбросить…", ResetSettings)));

        void ExportSettings()
        {
            var dlg = new Microsoft.Win32.SaveFileDialog { FileName = $"N-Island-settings-{DateTime.Now:yyyy-MM-dd}.json", Filter = "JSON|*.json" };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                var copy = JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(S));
                copy.AiKeys = new();
                copy.Integrations = new();
                File.WriteAllText(dlg.FileName, JsonSerializer.Serialize(copy, new JsonSerializerOptions { WriteIndented = true }));
                App.Island.Toast("", "Настройки сохранены", Path.GetFileName(dlg.FileName));
            }
            catch (Exception e) { MessageBox.Show(this, e.Message, "N-Island"); }
        }

        void ImportSettings()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "JSON|*.json" };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                var src = JsonSerializer.Deserialize<Settings>(File.ReadAllText(dlg.FileName)) ?? throw new InvalidDataException("пустой файл");
                int n = ApplyFrom(src, true);
                App.Island.Toast("", "Настройки загружены", I18n.T("Изменено пунктов: ") + n);
                Open(_current);
            }
            catch (Exception e) { MessageBox.Show(this, I18n.T("Не удалось прочитать файл настроек: ") + e.Message, "N-Island"); }
        }

        void ResetSettings()
        {
            if (MessageBox.Show(this, I18n.T("Вернуть все настройки как после установки? Ключи ИИ, сервисы, язык и автозапуск останутся."), "N-Island",
                    MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            int n = ApplyFrom(new Settings(), true);
            Presets.Apply(Presets.Get("liquid"));
            App.Island.Toast("", "Настройки сброшены", I18n.T("Изменено пунктов: ") + n);
            Open(_current);
        }

        // ------------------------------------------------------------------
        //  Контролы следят за настройкой: поменяли из острова или трея — значение в окне тоже обновится
        // ------------------------------------------------------------------

        static void Follow(FrameworkElement owner, string prop, Action update)
        {
            System.ComponentModel.PropertyChangedEventHandler h = (_, e) => { if (e.PropertyName == prop) owner.Dispatcher.BeginInvoke(update); };
            owner.Loaded += (_, __) => { S.PropertyChanged -= h; S.PropertyChanged += h; };
            owner.Unloaded += (_, __) => S.PropertyChanged -= h;
        }
    }
}
