using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using NIsland.Core;
using NIsland.Pages;
using NIsland.Services;

namespace NIsland
{
    /// <summary>
    /// «Центр управления ноутбуком»: то же окно, что и настройки, но со своими разделами —
    /// очистка, защита от слежки, питание, сеть, программы, исправления. У каждого пункта — метка безопасности
    /// и объяснение простыми словами (режим новичка).
    /// </summary>
    public partial class SettingsWindow
    {
        readonly bool _center;
        DispatcherTimer _live;
        Dictionary<string, string> _searchIndex;

        List<Section> CenterSections() => new()
        {
            new("overview", "Обзор", "", "#0A84FF", Overview),
            new("av", "Защитник N-Island", "", "#FF453A", AvSec),
            new("health", "Здоровье ПК", "", "#30D158", HealthSec),
            new("clean", "Глубокая очистка", "", "#30D158", CleanSec),
            new("privacy", "Защита от слежки", "", "#5E5CE6", PrivacySec),
            new("optimize", "Оптимизация", "", "#FF9F0A", OptimizeSec),
            new("activity", "Журнал активности", "", "#FF375F", ActivitySec),
            new("perf", "Скорость", "", "#FFB800", PerfSec),
            new("power", "Питание и батарея", "", "#34C759", PowerSec),
            new("files", "Файлы и диски", "", "#FFB800", FilesSec),
            new("net", "Интернет и Wi-Fi", "", "#64D2FF", NetSec),
            new("apps", "Программы", "", "#FF375F", AppsSec),
            new("tune", "Вид Windows", "", "#BF5AF2", TuneSec),
            new("fix", "Починить", "", "#FF453A", FixSec),
            new("system", "Паспорт ноутбука", "", "#8E8E93", SystemSec),
            new("auto", "Автоматизация", "", "#636366", AutoSec),
            new("wifi", "Wi-Fi: анализ", "", "#0A84FF", WifiSec),
            new("osint", "OSINT и проверки", "", "#5E5CE6", OsintSec),
            new("track", "Отслеживание и контроль", "\uE823", "#FF9F0A", TrackSec),
        };

        static readonly Dictionary<string, string> CenterKeywords = new()
        {
            ["overview"] = "здоровье индекс оптимизировать сценарии кино игры работа экономия презентация точка восстановления",
            ["clean"] = "мусор кэш браузер chrome edge яндекс opera firefox временные файлы корзина следы история discord telegram spotify",
            ["privacy"] = "слежка телеметрия реклама камера микрофон местоположение hosts трекеры copilot recall bing edge",
            ["perf"] = "быстродействие процессы память озу режим питания игровой процессор",
            ["optimize"] = "оптимизация ускорение игры fps мышь видеокарта приоритет совместимость гибернация виджеты фон sysmain индексирование профиль слабый",
            ["activity"] = "журнал активность камера микрофон сайты разрешения запуски программы уведомления флешки установки история",
            ["power"] = "батарея заряд износ сон экран крышка таймер выключения гибернация",
            ["files"] = "диск место дубликаты загрузки большие файлы пустые папки",
            ["net"] = "dns wi-fi wifi пароль скорость ip соединения adguard cloudflare сеть",
            ["apps"] = "автозагрузка удалить программы приложения",
            ["tune"] = "тема тёмная панель задач проводник обои мышь контекстное меню значки рабочий стол",
            ["fix"] = "звук принтер sfc dism dns сайт время интернет сеть обновления значки store магазин вирусы диск проводник панель задач",
            ["system"] = "модель процессор видеокарта серийный номер bios память",
            ["auto"] = "автоочистка напоминания сторож новичок уведомления",
            ["wifi"] = "wi-fi wifi вайфай сигнал канал роутер сети рядом устройства пинг качество скорость пароль qr хотспот 5 ггц",
            ["track"] = "экранное время лимиты фокус будильник отчёт трафик сессии блокировка загрузки обои сторож нагрузка ip процессы слежка",
            ["osint"] = "osint фишинг ссылка whois домен dns сертификат поддомены архив wayback ip пароль утечка exif gps фото virustotal хэш вирус",
        };

        // ------------------------------------------------------------------
        //  Поиск по разделам (и в настройках, и в центре)
        // ------------------------------------------------------------------

        UIElement SearchBox()
        {
            var box = UI.Box("Поиск", double.NaN);
            box.Margin = new Thickness(0, 0, 0, 10);
            box.TextChanged += (_, __) => Filter(box.Text.Trim());
            box.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    var first = _nav.Children.OfType<Border>().FirstOrDefault(b => b.Visibility == Visibility.Visible);
                    if (first?.Tag is Section s) Open(s.Key);
                }
                else if (e.Key == Key.Escape && box.Text != "") { box.Text = ""; e.Handled = true; }
            };
            return box;
        }

        void Filter(string q)
        {
            if (q.Length > 0 && _searchIndex == null) BuildIndex();
            _query = q;
            foreach (var b in _nav.Children.OfType<Border>())
            {
                if (b.Tag is not Section s) continue;
                // ищем и по-русски, и на языке интерфейса — иначе в английской и украинской версии поиск ничего не находил
                bool ok = q.Length == 0 || s.Title.Contains(q, StringComparison.OrdinalIgnoreCase) || I18n.T(s.Title).Contains(q, StringComparison.OrdinalIgnoreCase)
                          || (_searchIndex.TryGetValue(s.Key, out var text) && text.Contains(q, StringComparison.OrdinalIgnoreCase));
                b.Visibility = ok ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        string _query = "";

        /// <summary>Открыли раздел из поиска — прокручиваем к найденной настройке и коротко подсвечиваем её.</summary>
        void RevealMatch()
        {
            if (_query.Length < 2 || _body.Content is not FrameworkElement page) return;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
            {
                var hit = FindText(page, _query);
                if (hit == null) return;
                FrameworkElement row = hit;
                if (VisualTreeHelper.GetParent(hit) is Panel p && VisualTreeHelper.GetParent(p) is Grid g) row = g;
                row.BringIntoView(new Rect(0, -40, row.ActualWidth, row.ActualHeight + 80));
                if (row is Panel panel)
                {
                    var accent = (UI.Res("AccentBrush") as SolidColorBrush)?.Color ?? Colors.DodgerBlue;
                    var br = new SolidColorBrush(ColorUtil.WithAlpha(accent, 0.35));
                    panel.Background = br;
                    br.BeginAnimation(SolidColorBrush.ColorProperty, new System.Windows.Media.Animation.ColorAnimation(ColorUtil.WithAlpha(accent, 0), TimeSpan.FromSeconds(1.6)) { BeginTime = TimeSpan.FromSeconds(0.6) });
                }
            }));
        }

        static TextBlock FindText(DependencyObject d, string q)
        {
            if (d is TextBlock t && t.Text != null && t.Text.Contains(q, StringComparison.OrdinalIgnoreCase)) return t;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
                if (FindText(VisualTreeHelper.GetChild(d, i), q) is { } r) return r;
            return null;
        }

        /// <summary>Текст всех разделов — чтобы находить по названию любой настройки, а не только раздела.</summary>
        void BuildIndex()
        {
            _searchIndex = new();
            foreach (var s in _sections)
            {
                if (_center) { _searchIndex[s.Key] = CenterKeywords.TryGetValue(s.Key, out var k) ? k : ""; continue; }
                try { var sb = new StringBuilder(); Collect(s.Build(), sb, 0); _searchIndex[s.Key] = sb.ToString(); }
                catch { _searchIndex[s.Key] = ""; }
            }
        }

        static void Collect(object o, StringBuilder sb, int depth)
        {
            if (o == null || depth > 40) return;
            if (o is TextBlock t) sb.Append(t.Text).Append(' ').Append(I18n.T(t.Text)).Append(' ');
            else if (o is ContentControl { Content: string c }) sb.Append(c).Append(' ').Append(I18n.T(c)).Append(' ');
            if (o is DependencyObject d)
                foreach (var child in LogicalTreeHelper.GetChildren(d)) Collect(child, sb, depth + 1);
        }

        // ------------------------------------------------------------------
        //  Помощники центра
        // ------------------------------------------------------------------

        static Border Badge(Safety s, string text = null, string color = null)
        {
            var (label, hex) = s switch
            {
                Safety.Safe => ("Безопасно", "#30D158"),
                Safety.Caution => ("Осторожно", "#FF9F0A"),
                _ => ("Не рекомендуется", "#FF453A"),
            };
            var c = UI.Hex(color ?? hex).Color;
            return new Border
            {
                CornerRadius = new CornerRadius(6), Padding = new Thickness(6, 1, 6, 2), Margin = new Thickness(8, 1, 0, 1),
                Background = new SolidColorBrush(Color.FromArgb(0x30, c.R, c.G, c.B)), VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = text ?? label, FontSize = 10.5, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(c) },
            };
        }

        static UIElement AdminMark() => new Border
        {
            Margin = new Thickness(6, 1, 0, 1), VerticalAlignment = VerticalAlignment.Center, Background = Brushes.Transparent,
            ToolTip = "Нужны права администратора — Windows покажет запрос разрешения",
            Child = UI.Row(UI.Icon("", 11, UI.Dim), UI.Text("админ", 10.5, false, UI.Dim).M(3, 0, 0, 0)),
        };

        /// <summary>Строка пункта: название, метка безопасности, объяснение для новичка, «что изменится» и контрол справа.</summary>
        static UIElement Item(string title, string explain, Safety? safety, UIElement control, string effect = null, bool admin = false,
                              UIElement extra = null, string badgeText = null, string badgeColor = null)
        {
            var head = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
            head.Children.Add(new TextBlock { Text = title, FontSize = 13.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
            if (safety is Safety s) head.Children.Add(Badge(s, badgeText, badgeColor));
            if (admin) head.Children.Add(AdminMark());
            if (safety != null && !string.IsNullOrEmpty(explain))
            {
                // ✨ — ИИ объяснит пункт подробнее: что это, кому включать, риски, как вернуть
                var ai = UI.IconBtn("", () => Explain(title, explain, effect), 11, 24, "Спросить ИИ: что это и стоит ли включать");
                ai.Content = new TextBlock { Text = "✨", FontFamily = new FontFamily("Segoe UI Emoji"), FontSize = 11 };
                ai.Margin = new Thickness(4, 0, 0, 0);
                head.Children.Add(ai);
            }
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(head);
            bool full = S.CenterBeginner;
            if (!string.IsNullOrEmpty(explain))
            {
                if (full) text.Children.Add(new TextBlock { Text = explain, FontSize = 12, Foreground = UI.Dim, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
                else head.ToolTip = explain + (effect != null ? "\n\nЧто изменится: " + effect : "");
            }
            if (full && !string.IsNullOrEmpty(effect))
                text.Children.Add(new TextBlock { Text = "Что изменится: " + effect, FontSize = 11.5, Foreground = UI.Hex("#C09FD0FF"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
            if (extra != null) text.Children.Add(extra);
            var g = UI.Grid("*,a").M(14, 10, 14, 10).Add(text);
            if (control is FrameworkElement fe)
            {
                fe.VerticalAlignment = VerticalAlignment.Center;
                fe.Margin = new Thickness(16, 0, 0, 0);
                g.Add(fe.At(0, 1));
            }
            return g;
        }

        static TextBlock Status() => new()
        {
            FontSize = 12.5, Foreground = UI.Hex("#9FD0FF"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 0, 4, 12),
        };

        static TextBlock Loading(string text = "Загрузка…") => new() { Text = text, FontSize = 12, Foreground = UI.Dim, Margin = new Thickness(14, 10, 14, 10) };

        /// <summary>Кнопка для долгого действия: пока работает — «…», результат пишется в status (или всплывает в чёлке).</summary>
        static Button Act(string text, Func<Task<string>> work, TextBlock status = null, bool accent = false, string tip = null)
        {
            Button b = null;
            b = UI.Pill(text, async () =>
            {
                b.IsEnabled = false;
                var old = b.Content;
                b.Content = "…";
                try
                {
                    var r = await work();
                    if (r != null) { if (status != null) status.Text = r; else App.Island?.Toast("", r, null); }
                }
                catch (Exception e) { if (status != null) status.Text = "Не получилось: " + e.Message; Log.Info("center: " + e.Message); }
                finally { b.Content = old; b.IsEnabled = true; }
            }, accent, tip);
            b.Margin = new Thickness(0, 0, 8, 0);
            return b;
        }

        static WrapPanel Buttons(params UIElement[] items)
        {
            var w = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
            foreach (var i in items) if (i != null) { if (i is FrameworkElement fe) fe.Margin = new Thickness(0, 0, 8, 6); w.Children.Add(i); }
            return w;
        }

        /// <summary>Переключатель системной настройки (не из настроек N-Island).</summary>
        static CheckBox SysToggle(Func<bool> get, Action<bool> set)
        {
            bool cur;
            try { cur = get(); } catch { cur = false; }
            var c = new CheckBox { Style = UI.St("Switch"), IsChecked = cur };
            c.Checked += (_, __) => { try { set(true); } catch { } };
            c.Unchecked += (_, __) => { try { set(false); } catch { } };
            return c;
        }

        bool Confirm(string text) =>
            MessageBox.Show(this, I18n.T(text), "N-Island", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;

        static string Rel(DateTime t)
        {
            var d = DateTime.Now - t;
            return d.TotalMinutes < 1 ? "только что" : d.TotalHours < 1 ? $"{(int)d.TotalMinutes} мин назад"
                 : d.TotalDays < 1 ? $"{(int)d.TotalHours} ч назад" : d.TotalDays < 30 ? $"{(int)d.TotalDays} дн. назад" : t.ToString("d MMM yyyy", Fmt.Ru);
        }

        static Border Bar(double value, string hex)
        {
            var fill = new Border { CornerRadius = new CornerRadius(3), Background = UI.Hex(hex), HorizontalAlignment = HorizontalAlignment.Left };
            var track = new Border { Height = 6, CornerRadius = new CornerRadius(3), Background = UI.Hex("#22FFFFFF"), Child = fill, Margin = new Thickness(0, 6, 0, 0) };
            track.SizeChanged += (_, __) => fill.Width = Math.Max(0, track.ActualWidth * Math.Clamp(value, 0, 1));
            return track;
        }

        static async Task<string> RestorePoint()
        {
            int code = await Elevated.RestorePoint();
            return code < 0 ? "Не дано разрешение администратора" : "Точка восстановления создана ✓ Откатиться: Пуск → «Восстановление» → «Запуск восстановления системы»";
        }

        // ------------------------------------------------------------------
        //  Обзор
        // ------------------------------------------------------------------

        UIElement Overview()
        {
            var page = new StackPanel();

            // кольцо индекса здоровья
            const double size = 104, th = 9;
            var arc = new Ellipse
            {
                Width = size, Height = size, StrokeThickness = th, Stroke = UI.Hex("#30D158"), StrokeDashCap = PenLineCap.Round,
                RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform(-90), StrokeDashArray = new DoubleCollection { 0, 1000 },
            };
            var num = UI.Text("…", 30, true);
            num.HorizontalAlignment = HorizontalAlignment.Center;
            var ring = new Grid { Width = size, Height = size }.Add(
                new Ellipse { Width = size, Height = size, StrokeThickness = th, Stroke = UI.Hex("#1EFFFFFF") }, arc,
                UI.Col(num, UI.Text("из 100", 11, false, UI.Dim).HA(HorizontalAlignment.Center)).HA(HorizontalAlignment.Center).VA(VerticalAlignment.Center));
            var verdict = UI.Text("Проверяю ноутбук…", 19, true);
            var tips = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
            var heroText = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(20, 0, 0, 0) };
            heroText.Children.Add(UI.Text("Индекс здоровья ноутбука", 12, true, UI.Dim));
            heroText.Children.Add(verdict);
            heroText.Children.Add(tips);
            page.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(16), Padding = new Thickness(18), Margin = new Thickness(0, 0, 0, 12),
                Background = new LinearGradientBrush(Color.FromArgb(0x2A, 0x0A, 0x84, 0xFF), Color.FromArgb(0x14, 0x5E, 0x5C, 0xE6), 30),
                BorderBrush = UI.Hex("#26FFFFFF"), BorderThickness = new Thickness(1),
                Child = UI.Grid("a,*").Add(ring, heroText.At(0, 1)),
            });

            var status = Status();
            page.Children.Add(Buttons(
                Act("Оптимизировать в 1 клик", async () => { var r = await OneClick(); _ = LoadHealth(); return r; }, status, true,
                    "Безопасная очистка + освобождение памяти + сброс DNS. Ничего личного не удаляется."),
                Act("Освободить память", async () => "Освобождено " + Maint.Size(await Maint.OptimizeRam()), status),
                Act("Точка восстановления", RestorePoint, status, false, "Снимок системы, к которому можно откатиться, если что-то пойдёт не так")));
            page.Children.Add(status);

            // живые показатели
            var tiles = new System.Windows.Controls.Primitives.UniformGrid { Columns = 4, Margin = new Thickness(0, 0, 0, 14) };
            (TextBlock Value, Border Fill) Tile(string label, string glyph)
            {
                var v = UI.Text("—", 20, true);
                var fill = new Border { CornerRadius = new CornerRadius(2), Background = UI.Res("AccentBrush"), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
                var track = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = UI.Hex("#22FFFFFF"), Child = fill, Margin = new Thickness(0, 8, 0, 0) };
                tiles.Children.Add(new Border
                {
                    CornerRadius = new CornerRadius(12), Background = UI.Hex("#14FFFFFF"), Padding = new Thickness(12, 10, 12, 12), Margin = new Thickness(0, 0, 8, 0),
                    BorderBrush = UI.Hex("#1AFFFFFF"), BorderThickness = new Thickness(1),
                    Child = UI.Col(UI.Row(UI.Icon(glyph, 12, UI.Dim), UI.Text(label, 11.5, false, UI.Dim).M(6, 0, 0, 0)), v, track),
                });
                return (v, fill);
            }
            var cpu = Tile("Процессор", "");
            var ram = Tile("Память", "");
            var disk = Tile("Диск", "");
            var bat = Tile("Батарея", "");
            page.Children.Add(tiles);
            void Set((TextBlock Value, Border Fill) t, string text, double frac)
            {
                t.Value.Text = text;
                if (t.Fill.Parent is Border tr && tr.ActualWidth > 0)
                    t.Fill.BeginAnimation(WidthProperty, new DoubleAnimation(tr.ActualWidth * Math.Clamp(frac, 0, 1), TimeSpan.FromMilliseconds(500)) { EasingFunction = new QuadraticEase() });
            }
            void Update()
            {
                var sys = SystemService.I;
                int c = sys.CpuPercent(), r = sys.RamPercent();
                Set(cpu, c + "%", c / 100.0);
                Set(ram, r + "%", r / 100.0);
                var root = System.IO.Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\');
                var d = Maint.Disks().FirstOrDefault(x => string.Equals(x.Name, root, StringComparison.OrdinalIgnoreCase));
                if (d != null) Set(disk, "своб. " + Maint.Size(d.Free), 1 - (double)d.Free / Math.Max(1, d.Total));
                if (sys.HasBattery) Set(bat, (sys.Charging ? "⚡" : "") + sys.BatteryPercent + "%", sys.BatteryPercent / 100.0);
                else Set(bat, "от сети", 1);
            }
            _live?.Stop();
            _live = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(2) };
            _live.Tick += (_, __) => Update();
            page.Loaded += (_, __) => { Update(); _live.Start(); };
            page.Unloaded += (_, __) => _live.Stop();

            // сценарии
            page.Children.Add(UI.Text("Сценарии — несколько настроек одним нажатием", 12, true, UI.Dim).M(4, 0, 0, 6));
            var scen = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 6) };
            foreach (var (key, title, glyph, explain) in Laptop.Scenarios)
            {
                var k = key;
                bool active = S.Scenario == key || (key == "normal" && string.IsNullOrEmpty(S.Scenario));
                var card = new Border
                {
                    CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 0, 8, 8), Cursor = Cursors.Hand,
                    Background = active ? new SolidColorBrush(ColorUtil.WithAlpha((UI.Res("AccentBrush") as SolidColorBrush)?.Color ?? Colors.DodgerBlue, 0.3)) : UI.Hex("#14FFFFFF"),
                    BorderBrush = UI.Hex("#1AFFFFFF"), BorderThickness = new Thickness(1), ToolTip = explain,
                    Child = UI.Col(UI.Row(UI.Icon(glyph, 15), UI.Text(title, 13.5, true).M(8, 0, 0, 0)),
                                   new TextBlock { Text = explain, FontSize = 11, Foreground = UI.Dim, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0), MaxHeight = 30, TextTrimming = TextTrimming.CharacterEllipsis }),
                };
                card.MouseLeftButtonUp += (_, __) =>
                {
                    Laptop.ApplyScenario(k);
                    App.Island?.Toast(glyph, "Сценарий: " + title, null);
                    Open("overview");
                };
                scen.Children.Add(card);
            }
            page.Children.Add(scen);
            page.Children.Add(Note("Подсказка: Ctrl+Alt+L открывает этот центр из любого места. Все изменения можно отменить — подробнее в разделе «Как пользоваться»."));

            async Task LoadHealth()
            {
                var h = await Laptop.CheckHealth();
                num.Text = h.Score.ToString();
                var color = h.Score >= 80 ? "#30D158" : h.Score >= 55 ? "#FF9F0A" : "#FF453A";
                arc.Stroke = UI.Hex(color);
                double circ = Math.PI * (size - th) / th;
                arc.StrokeDashArray = new DoubleCollection { circ * h.Score / 100.0, 1000 };
                arc.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(500)));
                verdict.Text = h.Score >= 80 ? "Ноутбук в отличной форме" : h.Score >= 55 ? "Есть что улучшить" : "Ноутбуку нужна помощь";
                tips.Children.Clear();
                foreach (var tip in h.Tips.Take(5))
                {
                    var dot = new Ellipse { Width = 7, Height = 7, Margin = new Thickness(0, 5, 8, 0), VerticalAlignment = VerticalAlignment.Top,
                                            Fill = UI.Hex(tip.Level == Safety.Safe ? "#30D158" : tip.Level == Safety.Caution ? "#FF9F0A" : "#FF453A") };
                    var tb = new TextBlock { Text = tip.Text, FontSize = 12.5, TextWrapping = TextWrapping.Wrap };
                    var row = new DockPanel { Margin = new Thickness(0, 3, 0, 0), Background = Brushes.Transparent };
                    DockPanel.SetDock(dot, Dock.Left);
                    row.Children.Add(dot);
                    row.Children.Add(tb);
                    if (tip.Section != null)
                    {
                        var sec = tip.Section;
                        row.Cursor = Cursors.Hand;
                        tb.TextDecorations = null;
                        row.ToolTip = "Открыть раздел";
                        row.MouseLeftButtonUp += (_, __) => Open(sec);
                    }
                    tips.Children.Add(row);
                }
            }
            _ = LoadHealth();
            return page;
        }

        static async Task<string> OneClick()
        {
            var safe = Cleaner.Targets.Where(t => t.Safety == Safety.Safe && !t.Admin && !t.InfoOnly && t.CustomClean == null
                                                  && t.Group is "Система" or "Приложения" or "Браузеры" && t.Blocked == null).ToList();
            var r = await Cleaner.Clean(safe);
            long ram = await Maint.OptimizeRam();
            Maint.FlushDns();
            return $"Готово ✓ Диск: освобождено {Maint.Size(r.Freed)} · память: {Maint.Size(ram)}. Открытые браузеры и программы пропущены.";
        }

        // ------------------------------------------------------------------
        //  Глубокая очистка
        // ------------------------------------------------------------------

        UIElement CleanSec()
        {
            var page = new StackPanel();
            page.Children.Add(Note("Как мы проверяем безопасность: удаляем только содержимое заранее известных папок кэша и временных файлов; " +
                                   "не заходим в ярлыки-ссылки; не трогаем системные, занятые и свежие файлы; кэш программы чистим, только когда она закрыта. " +
                                   "Пароли, история браузера, закладки и твои документы не удаляются никогда.").M(4, 0, 4, 12));
            var status = Status();
            var checks = new Dictionary<CleanTarget, CheckBox>();
            var sizes = new Dictionary<CleanTarget, TextBlock>();
            var total = UI.Text("", 13, true);

            var blocked = new HashSet<CleanTarget>();
            bool admin = Elevated.IsAdmin;
            void UpdateTotal()
            {
                long b = checks.Where(kv => kv.Value.IsChecked == true && !blocked.Contains(kv.Key)).Sum(kv => kv.Key.Bytes);
                total.Text = checks.Keys.Any(t => t.Scanned) ? "Выбрано: " + Maint.Size(b) : "";
            }
            void ShowSize(CleanTarget t)
            {
                var busy = t.Blocked;
                if (busy != null) blocked.Add(t); else blocked.Remove(t);
                var size = t.Bytes > 0 ? Maint.Size(t.Bytes) : t.Files > 0 ? $"{t.Files} зап." : t.Admin && !admin ? "нужен админ" : "чисто";
                sizes[t].Text = !t.Scanned ? (busy != null ? $"закрой {busy}" : "") : busy != null ? $"{size} · закрой {busy}" : size;
                sizes[t].ToolTip = busy != null ? $"Кэш занят: программа {busy} открыта. Закрой её и нажми «Проанализировать» ещё раз."
                                 : t.Admin && !admin && t.Bytes == 0 ? "Без прав администратора Windows не показывает содержимое этой папки — оно станет видно при очистке" : null;
                sizes[t].Foreground = busy != null ? UI.Hex("#FF9F0A") : t.Bytes > 50L << 20 ? Brushes.White : UI.Dim;
            }

            async Task<string> Analyze()
            {
                status.Text = "Считаю… обычно 10–30 секунд — папки кэша бывают огромными";
                await Cleaner.Scan(checks.Keys.ToList());
                foreach (var t in checks.Keys) ShowSize(t);
                UpdateTotal();
                long all = checks.Keys.Where(t => !blocked.Contains(t)).Sum(t => t.Bytes);
                return $"Можно освободить {Maint.Size(all)}. Отметь, что почистить, и нажми «Очистить выбранное»." +
                       (blocked.Count > 0 ? $" Ещё {Maint.Size(blocked.Sum(t => t.Bytes))} в кэше открытых программ — закрой их, чтобы почистить." : "");
            }

            async Task<string> CleanSelected()
            {
                var sel = checks.Where(kv => kv.Value.IsChecked == true).Select(kv => kv.Key).ToList();
                if (sel.Count == 0) return "Ничего не выбрано";
                if (sel.Any(t => t.Key == "recycle") && !Confirm("Корзина будет очищена безвозвратно — файлы из неё нельзя будет вернуть. Продолжить?"))
                    sel.RemoveAll(t => t.Key == "recycle");
                if (sel.Any(t => t.Safety == Safety.Caution && t.Key != "recycle") &&
                    !Confirm("Среди выбранного есть пункты с меткой «Осторожно». Прочитай их описание. Продолжить?"))
                    return "Отменено";
                var r = await Cleaner.Clean(sel);
                await Cleaner.Scan(checks.Keys.ToList());
                foreach (var t in checks.Keys) ShowSize(t);
                UpdateTotal();
                return $"Готово ✓ Освобождено {Maint.Size(r.Freed)}, удалено файлов: {r.Deleted}" +
                       (r.Skipped > 0 ? $", пропущено занятых и защищённых: {r.Skipped}" : "") + (string.IsNullOrEmpty(r.Note) ? "" : ". " + r.Note);
            }

            page.Children.Add(Buttons(Act("Проанализировать", Analyze, status, true), Act("Очистить выбранное", CleanSelected, status), total));
            page.Children.Add(status);
            if (S.LastCleanup > DateTime.MinValue)
                page.Children.Add(Note($"Последняя очистка: {Rel(S.LastCleanup)} · всего освобождено с N-Island: {Maint.Size(S.CleanedTotal)}").M(4, -4, 4, 12));

            foreach (var group in new[] { "Система", "Браузеры", "Приложения", "Следы", "Не трогать" })
            {
                var rows = new List<UIElement>();
                foreach (var t in Cleaner.Targets.Where(x => x.Group == group))
                {
                    UIElement control;
                    if (t.InfoOnly) control = UI.Icon("", 14, UI.Hex("#FF453A"));
                    else
                    {
                        var cb = new CheckBox { Style = UI.St("Switch"), IsChecked = t.Safety == Safety.Safe && t.Group != "Следы" };
                        cb.Checked += (_, __) => UpdateTotal();
                        cb.Unchecked += (_, __) => UpdateTotal();
                        checks[t] = cb;
                        var size = UI.Text("", 12.5, true, UI.Dim);
                        size.MinWidth = 64;
                        size.MaxWidth = 220;
                        size.TextAlignment = TextAlignment.Right;
                        sizes[t] = size;
                        control = UI.Row(size, cb.M(10, 0, 0, 0));
                    }
                    rows.Add(Item(t.Title, t.Explain, t.Safety, control, null, t.Admin));
                }
                var label = group switch
                {
                    "Следы" => "Следы активности (приватность)",
                    "Не трогать" => "Не трогать — объясняем, почему (эти пункты мы не чистим)",
                    _ => group,
                };
                page.Children.Add(Group(label, rows.ToArray()));
            }
            page.Children.Add(Note("Права администратора нужны только для системных папок — Windows спросит разрешение один раз за очистку. " +
                                   "Хочешь, чтобы это происходило само? Включи автоочистку в разделе «Автоматизация»."));
            return page;
        }

        // ------------------------------------------------------------------
        //  Защита от слежки
        // ------------------------------------------------------------------

        UIElement PrivacySec()
        {
            var page = new StackPanel();
            var (on, total) = Privacy.Score();
            var status = Status();
            page.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(14), Padding = new Thickness(16), Margin = new Thickness(0, 0, 0, 12),
                Background = new LinearGradientBrush(Color.FromArgb(0x2A, 0x5E, 0x5C, 0xE6), Color.FromArgb(0x12, 0x30, 0xD1, 0x58), 20),
                BorderBrush = UI.Hex("#26FFFFFF"), BorderThickness = new Thickness(1),
                Child = UI.Col(UI.Text($"Защита включена: {on} из {total}", 17, true),
                    new TextBlock { Text = "Здесь только официальные настройки и политики Windows — без взлома и замены системных файлов. " +
                                           "Перед каждым изменением N-Island сохраняет, как было, — всё возвращается одной кнопкой.",
                                    FontSize = 12, Foreground = UI.Dim, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) },
                    Bar(total == 0 ? 0 : (double)on / total, on * 2 >= total ? "#30D158" : "#FF9F0A")),
            });
            page.Children.Add(Buttons(
                Act("Включить все безопасные", async () =>
                {
                    var list = Privacy.Items.Where(i => i.Safety == Safety.Safe && !Privacy.IsProtected(i)).ToList();
                    if (list.Count == 0) return "Все безопасные пункты уже включены ✓";
                    var r = await Privacy.Protect(list);
                    Open("privacy");
                    return r;
                }, status, true, "Только пункты с зелёной меткой. Windows один раз спросит права администратора для системных политик."),
                Act("Вернуть всё как было", async () =>
                {
                    var list = Privacy.Items.Where(Privacy.HasBackup).ToList();
                    if (list.Count == 0) return "Нечего возвращать — N-Island ещё ничего не менял";
                    if (!Confirm("Вернуть все настройки приватности к значениям, которые были до N-Island?")) return null;
                    var r = await Privacy.Restore(list);
                    Open("privacy");
                    return r;
                }, status),
                Act("Точка восстановления", RestorePoint, status, false, "Рекомендуем создать перед изменением системных политик")));
            page.Children.Add(status);

            foreach (var g in Privacy.Items.GroupBy(i => i.Group))
                page.Children.Add(Group(g.Key, ToggleRows(g, status)));

            // журнал камеры и микрофона
            var log = new StackPanel();
            void FillLog()
            {
                log.Children.Clear();
                var list = Privacy.AccessLog().Take(14).ToList();
                if (list.Count == 0) { log.Children.Add(Loading("Никто не пользовался камерой, микрофоном или местоположением")); return; }
                foreach (var a in list)
                {
                    var glyph = a.Kind == "камера" ? "" : a.Kind == "микрофон" ? "" : "";
                    log.Children.Add(UI.Grid("a,*,a").M(14, 7, 14, 7).Add(
                        UI.Icon(glyph, 14, a.InUse ? UI.Hex("#FF9F0A") : UI.Dim),
                        UI.Text($"{a.App} · {a.Kind}", 13).M(10, 0, 0, 0).At(0, 1),
                        UI.Text(a.InUse ? "сейчас используется" : Rel(a.Last), 12, a.InUse, a.InUse ? UI.Hex("#FF9F0A") : UI.Dim).At(0, 2)));
                }
            }
            FillLog();
            page.Children.Add(Group("Кто пользовался камерой, микрофоном и местоположением",
                Item("Сторож камеры", "Чёлка покажет, когда любая программа включит камеру или запросит местоположение — даже скрытно.",
                     Safety.Safe, Toggle(nameof(Settings.CamMicAlert))),
                log,
                Item("Настройки доступа Windows", "Запретить конкретным приложениям камеру или микрофон", null,
                     UI.Pill("Открыть", () => SystemService.Run("ms-settings:privacy-webcam")))));

            // hosts
            int blocked = Privacy.HostsBlocked();
            var hostsStatus = Status();
            page.Children.Add(Group("Файл hosts — блокировка трекеров на уровне системы",
                Item("Блокировать рекламные и следящие домены",
                     $"Добавляет в системный файл hosts {Privacy.TrackerDomains.Length} адресов счётчиков и рекламы (Google Analytics, DoubleClick, Яндекс Метрика, " +
                     "Facebook Pixel, Hotjar и др.) — они перестают работать во всех браузерах и программах. Перед изменением делается копия файла.",
                     Safety.Caution,
                     SysToggle(() => blocked > 0, v => _ = Task.Run(async () =>
                     {
                         bool ok = await Privacy.HostsBlock(v);
                         Dispatcher.Invoke(() => hostsStatus.Text = ok ? (v ? "Трекеры заблокированы ✓" : "Блокировка снята ✓") : "Не получилось — нет разрешения администратора");
                     })),
                     "Некоторые сайты могут перестать показывать рекламные ролики или счётчики. Отключается этим же переключателем.", true),
                Item("Другие записи в hosts", "Строки, которые добавили другие программы или ты сам. N-Island их не трогает.", null,
                     UI.Text(Privacy.HostsCustomLines().ToString(), 13, true)),
                Item("Вернуть файл hosts из копии", Privacy.LastHostsBackup() is string bk ? "Последняя копия: " + System.IO.Path.GetFileName(bk) : "Копий ещё нет",
                     null, Act("Вернуть", async () =>
                     {
                         var b = Privacy.LastHostsBackup();
                         if (b == null) return "Копий ещё нет";
                         if (!Confirm("Заменить текущий файл hosts последней резервной копией?")) return null;
                         return await Privacy.RestoreHosts(b) >= 0 ? "Файл hosts восстановлен ✓" : "Нет разрешения администратора";
                     }, hostsStatus), null, true)));
            page.Children.Add(hostsStatus);
            page.Children.Add(Note("Домены Microsoft в hosts намеренно не добавляются: Защитник Windows считает это вмешательством в систему, " +
                                   "а телеметрию надёжнее и безопаснее выключать политиками выше. Резервные копии лежат в папке данных N-Island → backup."));
            return page;
        }

        // ------------------------------------------------------------------
        //  Скорость
        // ------------------------------------------------------------------

        UIElement PerfSec()
        {
            var page = new StackPanel();
            var status = Status();
            // режим питания
            var modes = new StackPanel { Orientation = Orientation.Horizontal };
            var cur = Laptop.PowerMode;
            foreach (var m in Laptop.PowerModes)
            {
                var mm = m;
                var rb = new RadioButton { Style = UI.St("Segment"), Content = m.Name, GroupName = "pmode" + GetHashCode(), IsChecked = cur == m.Key, ToolTip = m.Explain };
                rb.Checked += (_, __) => { Laptop.PowerMode = mm.Key; status.Text = "Режим питания: " + mm.Name.ToLower() + " — " + mm.Explain.ToLower(); };
                modes.Children.Add(rb);
            }
            page.Children.Add(Group("Режим питания",
                Item("Производительность ↔ батарея", "То же, что ползунок «Режим питания» в параметрах Windows 11, только быстрее.", Safety.Safe,
                     new Border { Background = UI.Hex("#22FFFFFF"), CornerRadius = new CornerRadius(8), Padding = new Thickness(2), Child = modes },
                     "«Производительность» быстрее, но ноутбук сильнее греется и быстрее разряжается.")));
            page.Children.Add(status);
            page.Children.Add(Group("Ускорение Windows",
                Tweaks.Performance.Select(t => Item(t.Title, t.Desc, Safety.Safe, SysToggle(t.Get, t.Set), t.Note)).Append(
                Item("Анимации окон", "Выключи на слабом ноутбуке — окна будут открываться мгновенно.", Safety.Safe,
                     SysToggle(Tweaks.Theme[5].Get, Tweaks.Theme[5].Set), "Окна перестанут плавно сворачиваться.")).ToArray()));

            // процессы
            var procs = new StackPanel();
            void Fill()
            {
                procs.Children.Clear();
                foreach (var p in Maint.TopProcesses(10))
                {
                    var pp = p;
                    var kill = UI.Pill("Закрыть", () =>
                    {
                        if (!Confirm($"Закрыть «{pp.Name}»? Несохранённые данные в этой программе пропадут.")) return;
                        int n = Maint.Kill(pp.Name);
                        status.Text = n > 0 ? $"Закрыто: {pp.Name}" : "Не получилось — программа защищена или системная";
                        Fill();
                    });
                    procs.Children.Add(UI.Grid("*,a,a").M(14, 6, 14, 6).Add(
                        UI.Text(pp.Count > 1 ? $"{pp.Name} ×{pp.Count}" : pp.Name, 13),
                        UI.Text(Maint.Size(pp.Bytes), 12.5, true, UI.Dim).M(12, 0, 12, 0).At(0, 1), kill.At(0, 2)));
                }
            }
            Fill();
            procs.Live(TimeSpan.FromSeconds(5), () => { Fill(); return Task.CompletedTask; });   // список памяти обновляется сам
            page.Children.Add(Group("Кто занимает память", procs));
            page.Children.Add(Buttons(Act("Обновить список", () => { Fill(); return Task.FromResult<string>(null); }),
                                      Act("Освободить память", async () => "Освобождено " + Maint.Size(await Maint.OptimizeRam()), status)));
            page.Children.Add(Group("Сторож «прожорливых» программ",
                Item("Предупреждать о зависших программах", "Если программа полторы минуты подряд грузит процессор — чёлка подскажет, кто виноват.", Safety.Safe,
                     Toggle(nameof(Settings.HungryWatch))),
                Item("Порог нагрузки", "Сколько процентов всего процессора (как в Диспетчере задач) считать «слишком много». " +
                     $"У этого ноутбука {Environment.ProcessorCount} потоков — одна зависшая программа обычно грузит {100 / Math.Max(1, Environment.ProcessorCount)}–{200 / Math.Max(1, Environment.ProcessorCount)}%.", null,
                     Stepper(nameof(Settings.HungryCpu), 10, 90, 5, v => v + "%"))));
            return page;
        }

        // ------------------------------------------------------------------
        //  Питание и батарея
        // ------------------------------------------------------------------

        UIElement PowerSec()
        {
            var page = new StackPanel();
            var status = Status();
            var sys = SystemService.I;
            var batInfo = new StackPanel();
            batInfo.Children.Add(Loading());
            page.Children.Add(Group("Батарея", batInfo));
            async Task LoadBattery()
            {
                var b = await Maint.Battery();
                batInfo.Children.Clear();
                if (!sys.HasBattery) { batInfo.Children.Add(Loading("Батарея не найдена — ноутбук работает от сети")); return; }
                batInfo.Children.Add(Item("Заряд", sys.Charging ? "Заряжается" : "Работает от батареи", null, UI.Text(sys.BatteryPercent + "%", 15, true)));
                if (b != null && b.Design > 0)
                {
                    var wear = Math.Max(0, 100 - b.Percent);
                    batInfo.Children.Add(Item("Здоровье батареи", $"Держит {b.Percent}% от заводской ёмкости ({b.Full / 1000.0:0.#} из {b.Design / 1000.0:0.#} Вт·ч)" +
                        (b.Cycles > 0 ? $" · циклов зарядки: {b.Cycles}" : ""),
                        b.Percent >= 80 ? Safety.Safe : b.Percent >= 60 ? Safety.Caution : Safety.NotRecommended,
                        UI.Text($"износ {wear}%", 13, true), null, false, Bar(b.Percent / 100.0, b.Percent >= 80 ? "#30D158" : "#FF9F0A"),
                        b.Percent >= 80 ? "Отлично" : b.Percent >= 60 ? "Заметный износ" : "Сильный износ"));
                }
                batInfo.Children.Add(Item("Подробный отчёт Windows", "История зарядки и разрядки за последние дни", null,
                    Act("Открыть отчёт", async () => { await Tweaks.OpenBatteryReport(); return null; })));
            }
            _ = LoadBattery();
            batInfo.Live(TimeSpan.FromSeconds(15), LoadBattery);   // заряд и «заряжается» обновляются сами

            page.Children.Add(Group("Бережём батарею",
                Item("Сигнал при заряде", "Литий-ионные батареи медленнее изнашиваются, если не держать их на 100%. Чёлка подскажет, когда можно отключить зарядку.",
                     Safety.Safe, Toggle(nameof(Settings.BatteryAlertHigh))),
                Item("Сигналить при", null, null, Stepper(nameof(Settings.BatteryHigh), 60, 100, 5, v => v + "%")),
                Item("Сам переключать режим питания", "Подключил зарядку — производительность, отключил — экономия. Работает в фоне.", Safety.Safe,
                     Toggle(nameof(Settings.AutoPlan))),
                Item("От зарядки", null, null, Seg(nameof(Settings.PlanOnAc), ("perf", "Производ."), ("balanced", "Баланс"), ("saver", "Экономия"))),
                Item("От батареи", null, null, Seg(nameof(Settings.PlanOnDc), ("saver", "Экономия"), ("balanced", "Баланс"), ("perf", "Производ.")))));

            UIElement Times(string what, bool battery) => UI.Row(new[] { (1, "1 мин"), (5, "5"), (10, "10"), (15, "15"), (30, "30"), (0, "никогда") }
                .Select(x => (UIElement)UI.Pill(x.Item2, () => { Tweaks.SetTimeout(what, battery, x.Item1); status.Text = "Сохранено ✓"; }).M(0, 0, 4, 0)).ToArray());
            page.Children.Add(Group("Экран и сон (минуты)",
                Item("Экран гаснет — от батареи", null, null, Times("monitor", true)),
                Item("Экран гаснет — от сети", null, null, Times("monitor", false)),
                Item("Сон — от батареи", null, null, Times("standby", true)),
                Item("Сон — от сети", null, null, Times("standby", false)),
                Item("Когда закрываю крышку", "Для режима от батареи и от сети сразу", null,
                     UI.Row(new[] { (0, "Ничего"), (1, "Сон"), (2, "Гибернация"), (3, "Выключить") }
                        .Select(x => (UIElement)UI.Pill(x.Item2, () => { Tweaks.SetLidAction(x.Item1); status.Text = "Крышка: " + x.Item2.ToLower() + " ✓"; }).M(0, 0, 4, 0)).ToArray())),
                Item("Не давать уснуть", "Экран не гаснет и ноутбук не засыпает, пока включено — для загрузок и презентаций.", Safety.Safe,
                     SysToggle(() => sys.KeepAwake, v => sys.KeepAwake = v))));
            page.Children.Add(status);

            // таймер
            string action = Laptop.OffAction ?? "sleep";
            var acts = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var (key, name) in Laptop.OffActions)
            {
                var k = key;
                var rb = new RadioButton { Style = UI.St("Segment"), Content = name, GroupName = "offact" + GetHashCode(), IsChecked = action == key };
                rb.Checked += (_, __) => action = k;
                acts.Children.Add(rb);
            }
            var timerState = UI.Text(Laptop.OffAt is DateTime at ? $"Сработает в {at:HH:mm}" : "Не запущен", 12.5, false, UI.Dim);
            var minutes = UI.Row(new[] { 15, 30, 45, 60, 90, 120 }.Select(m => (UIElement)UI.Pill(m < 60 ? m + " мин" : m / 60.0 + " ч", () =>
            {
                Laptop.ScheduleOff(action, m);
                timerState.Text = $"{Laptop.OffActions.First(a => a.Key == action).Name} в {DateTime.Now.AddMinutes(m):HH:mm}";
            }).M(0, 0, 4, 0)).ToArray());
            page.Children.Add(Group("Таймер",
                Wide("Что сделать", "Например, уснуть после фильма или выключиться после загрузки. За минуту до срабатывания чёлка предупредит.",
                     new Border { Background = UI.Hex("#22FFFFFF"), CornerRadius = new CornerRadius(8), Padding = new Thickness(2), Child = acts, HorizontalAlignment = HorizontalAlignment.Left }),
                Wide("Через", null, minutes),
                Item("Состояние", null, null, UI.Row(timerState, UI.Pill("Отменить", () => { Laptop.CancelOff(); timerState.Text = "Отменён"; }).M(10, 0, 0, 0)))));
            return page;
        }

        // ------------------------------------------------------------------
        //  Файлы и диски
        // ------------------------------------------------------------------

        UIElement FilesSec()
        {
            var page = new StackPanel();
            var status = Status();
            page.Children.Add(Group("Диски", Maint.Disks().Select(d =>
            {
                double used = 1 - (double)d.Free / Math.Max(1, d.Total);
                return Item($"{d.Name} {d.Label}".Trim(), $"Свободно {Maint.Size(d.Free)} из {Maint.Size(d.Total)}", null,
                    UI.Text($"{used * 100:0}%", 13, true), null, false, Bar(used, used > 0.9 ? "#FF453A" : used > 0.8 ? "#FF9F0A" : "#0A84FF"));
            }).ToArray()));
            var health = new StackPanel();
            health.Children.Add(Loading("Проверяю здоровье дисков…"));
            page.Children.Add(Group("Здоровье дисков (SMART)", health));
            _ = Maint.DiskHealth().ContinueWith(t => Dispatcher.Invoke(() =>
            {
                health.Children.Clear();
                foreach (var h in t.Result.DefaultIfEmpty("Нет данных — Windows не сообщает состояние этих дисков"))
                    health.Children.Add(Loading(h));
            }));

            page.Children.Add(Note("Ниже — твои личные файлы. Мы только находим кандидатов; удаляешь ты сам, и всё уходит в корзину, откуда можно вернуть.").M(4, 0, 4, 10));
            page.Children.Add(status);
            var results = new StackPanel();

            UIElement FileRow(FileInfo f, Action after, string extra = null)
            {
                var path = f.FullName;
                return UI.Grid("*,a,a,a").M(14, 6, 14, 6).Add(
                    UI.Col(UI.Text(f.Name, 13), UI.Text(extra ?? System.IO.Path.GetDirectoryName(path), 11, false, UI.Dim)),
                    UI.Text(Maint.Size(f.Length), 12.5, true, UI.Dim).M(10, 0, 10, 0).At(0, 1),
                    UI.IconBtn("", () => Tweaks.ShowInFolder(path), 12, 28, "Показать в папке").At(0, 2),
                    UI.IconBtn("", () =>
                    {
                        if (Tweaks.ToRecycleBin(path)) { status.Text = "В корзине: " + f.Name; after(); }
                        else status.Text = "Файл занят другой программой";
                    }, 12, 28, "В корзину").At(0, 3));
            }

            void ShowList(string title, List<FileInfo> files, string empty)
            {
                results.Children.Clear();
                if (files.Count == 0) { results.Children.Add(Group(title, Loading(empty))); return; }
                var rows = new List<UIElement>();
                foreach (var f in files)
                {
                    UIElement row = null;
                    row = FileRow(f, () => row.Visibility = Visibility.Collapsed);
                    rows.Add(row);
                }
                results.Children.Add(Group($"{title} · {files.Count} шт., {Maint.Size(files.Sum(f => f.Length))}", rows.ToArray()));
            }

            page.Children.Add(Buttons(
                Act("Большие файлы", async () => { ShowList("Большие файлы", await Tweaks.LargeFiles(), "Файлов больше 200 МБ нет"); return null; }, status, false,
                    "Файлы от 200 МБ в Загрузках, на Рабочем столе, в Документах, Видео, Музыке и Изображениях"),
                Act("Старые загрузки", async () => { ShowList("В «Загрузках» дольше 3 месяцев", await Cleaner.OldDownloads(), "Старых загрузок нет ✓"); return null; }, status),
                Act("Дубликаты", async () =>
                {
                    var groups = await Cleaner.Duplicates();
                    results.Children.Clear();
                    if (groups.Count == 0) { results.Children.Add(Group("Дубликаты", Loading("Одинаковых файлов не найдено ✓"))); return null; }
                    var rows = new List<UIElement>();
                    foreach (var g in groups)
                    {
                        rows.Add(new TextBlock { Text = $"Оставим: {g[0].FullName}", FontSize = 11.5, Foreground = UI.Hex("#30D158"), Margin = new Thickness(14, 8, 14, 0), TextTrimming = TextTrimming.CharacterEllipsis });
                        foreach (var copy in g.Skip(1))
                        {
                            UIElement row = null;
                            row = FileRow(copy, () => row.Visibility = Visibility.Collapsed, "копия · " + System.IO.Path.GetDirectoryName(copy.FullName));
                            rows.Add(row);
                        }
                    }
                    long waste = groups.Sum(g => g[0].Length * (g.Count - 1));
                    results.Children.Add(Group($"Дубликаты · можно освободить {Maint.Size(waste)}", rows.ToArray()));
                    return "Самый старый файл в группе оставляем, копии можно отправить в корзину";
                }, status, false, "Одинаковые по содержимому файлы от 512 КБ в личных папках"),
                Act("Пустые папки", async () =>
                {
                    var dirs = await Cleaner.EmptyFolders();
                    results.Children.Clear();
                    if (dirs.Count == 0) { results.Children.Add(Group("Пустые папки", Loading("Пустых папок нет ✓"))); return null; }
                    var rows = dirs.Take(50).Select(d => (UIElement)UI.Text(d, 12, false, UI.Dim).M(14, 4, 14, 4)).ToList();
                    rows.Add(Item("Удалить все пустые", "Удаляются только папки, в которых точно ничего нет", Safety.Safe, Act("Удалить", () =>
                    {
                        int n = 0;
                        foreach (var d in dirs) try { if (!Directory.EnumerateFileSystemEntries(d).Any()) { Directory.Delete(d); n++; } } catch { }
                        results.Children.Clear();
                        return Task.FromResult($"Удалено пустых папок: {n}");
                    }, status)));
                    results.Children.Add(Group($"Пустые папки · {dirs.Count}", rows.ToArray()));
                    return null;
                }, status)));
            page.Children.Add(results);
            return page;
        }

        // ------------------------------------------------------------------
        //  Интернет и Wi-Fi
        // ------------------------------------------------------------------

        UIElement NetSec()
        {
            var page = new StackPanel();
            var status = Status();
            var wifi = UI.Text("…", 13, true);
            _ = Maint.WifiInfo().ContinueWith(t => Dispatcher.Invoke(() => wifi.Text = t.Result));
            var speed = UI.Text("", 12.5, false, UI.Dim);
            page.Children.Add(Group("Подключение",
                Item("Wi-Fi", null, null, wifi),
                Item("Скорость интернета", "Замер через Cloudflare, около 10 секунд", null,
                     UI.Row(speed, Act("Замерить", async () => { speed.Text = await ApiTools.SpeedTest(); return null; }).M(10, 0, 0, 0))),
                Item("Внешний IP и провайдер", null, null, Act("Узнать", ApiTools.PublicIp, status))));
            page.Children.Add(status);

            var dnsNow = UI.Text(Laptop.CurrentDns(), 12.5, false, UI.Dim);
            var dnsGrid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2, Margin = new Thickness(10, 4, 10, 10) };
            foreach (var p in Laptop.DnsPresets)
            {
                var pp = p;
                var card = new Border
                {
                    CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(4), Cursor = Cursors.Hand,
                    Background = UI.Hex("#14FFFFFF"), BorderBrush = UI.Hex("#1AFFFFFF"), BorderThickness = new Thickness(1),
                    Child = UI.Col(UI.Text(p.Name, 13, true), new TextBlock { Text = p.Explain, FontSize = 11, Foreground = UI.Dim, TextWrapping = TextWrapping.Wrap }),
                };
                card.MouseLeftButtonUp += async (_, __) =>
                {
                    status.Text = $"Ставлю DNS «{pp.Name}» — подтверди запрос Windows";
                    int code = await Laptop.SetDns(pp);
                    status.Text = code < 0 ? "Не дано разрешение администратора" : $"DNS «{pp.Name}» включён ✓";
                    dnsNow.Text = Laptop.CurrentDns();
                };
                dnsGrid.Children.Add(card);
            }
            page.Children.Add(Group("DNS — «адресная книга» интернета",
                Item("Сейчас", "DNS превращает названия сайтов в адреса. Другой DNS может ускорить открытие сайтов, вырезать рекламу или защитить от мошенников.",
                     Safety.Safe, dnsNow, "Меняется только для текущего подключения. Вернуть — карточка «Автоматически».", true),
                dnsGrid));

            var conns = new StackPanel();
            async Task<string> LoadConns()
            {
                conns.Children.Clear();
                conns.Children.Add(Loading());
                var list = await Laptop.Connections();
                conns.Children.Clear();
                foreach (var c in list.Take(15))
                    conns.Children.Add(UI.Grid("*,a").M(14, 6, 14, 6).Add(
                        UI.Col(UI.Text(c.Process, 13, true), UI.Text(c.Sample, 11, false, UI.Dim)),
                        UI.Text(c.Connections + " соед.", 12.5, false, UI.Dim).At(0, 1)));
                if (list.Count == 0) conns.Children.Add(Loading("Нет активных соединений"));
                return null;
            }
            page.Children.Add(Group("Кто сейчас пользуется интернетом",
                Item("Программы с открытыми соединениями", "Помогает найти программу, которая тайком качает или отправляет данные.", Safety.Safe,
                     Act("Обновить", LoadConns)),
                conns));
            _ = LoadConns();
            conns.Live(TimeSpan.FromSeconds(10), LoadConns);   // список программ с соединениями обновляется сам

            var nets = new StackPanel();
            nets.Children.Add(Loading());
            _ = Tweaks.WifiProfiles().ContinueWith(t => Dispatcher.Invoke(() =>
            {
                nets.Children.Clear();
                foreach (var name in t.Result.Take(30))
                {
                    var n = name;
                    var pass = UI.Text("", 12.5, true);
                    nets.Children.Add(UI.Grid("*,a,a").M(14, 6, 14, 6).Add(UI.Text(n, 13), pass.M(10, 0, 10, 0).At(0, 1),
                        UI.Pill("Пароль", async () =>
                        {
                            var p = await Tweaks.WifiPassword(n);
                            pass.Text = p ?? "без пароля / недоступен";
                            if (p != null) ClipboardService.I.CopyBack(p);
                        }, false, "Показать и скопировать пароль").At(0, 2)));
                }
                if (t.Result.Count == 0) nets.Children.Add(Loading("Сохранённых сетей нет"));
            }));
            page.Children.Add(Group("Сохранённые сети Wi-Fi", nets));
            page.Children.Add(Note("Пароль показывается только от сетей, к которым этот ноутбук уже подключался, и сразу копируется в буфер."));
            return page;
        }

        // ------------------------------------------------------------------
        //  Программы
        // ------------------------------------------------------------------

        UIElement AppsSec()
        {
            var page = new StackPanel();
            var status = Status();
            var rows = new List<UIElement>();
            foreach (var s in Maint.Startup())
            {
                var item = s;
                var adv = Laptop.Advise(s);
                UIElement control = s.CanToggle
                    ? SysToggle(() => item.Enabled, v => { Maint.SetStartup(item, v); status.Text = (v ? "Включено: " : "Отключено: ") + item.Name; })
                    : UI.Pill("Диспетчер", () => SystemService.Run("taskmgr.exe"), false, "Программа для всех пользователей — переключается в Диспетчере задач");
                rows.Add(Item(s.Name, adv.Why, adv.Level, control, null, false, null, adv.Verdict, adv.Level == Safety.NotRecommended ? "#64D2FF" : null));
            }
            page.Children.Add(Group("Автозагрузка — советник", rows.ToArray()));
            page.Children.Add(status);
            page.Children.Add(Note("Отключение не удаляет программу — она просто не запускается вместе с Windows. Включить обратно можно здесь же."));

            var list = new StackPanel();
            var search = UI.Box("Найти программу", 260);
            List<InstalledApp> all = null;
            void Render()
            {
                list.Children.Clear();
                if (all == null) { list.Children.Add(Loading()); return; }
                var q = search.Text.Trim();
                var shown = all.Where(a => q.Length == 0 || a.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || (a.Publisher ?? "").Contains(q, StringComparison.OrdinalIgnoreCase))
                               .OrderByDescending(a => q.Length == 0 ? a.SizeKb : 0).Take(40);
                foreach (var a in shown)
                {
                    var app = a;
                    list.Children.Add(UI.Grid("*,a,a").M(14, 6, 14, 6).Add(
                        UI.Col(UI.Text(a.Name, 13), UI.Text(string.Join(" · ", new[] { a.Publisher, a.Version }.Where(x => !string.IsNullOrEmpty(x))), 11, false, UI.Dim)),
                        UI.Text(a.SizeKb > 0 ? Maint.Size(a.SizeKb * 1024) : "", 12.5, false, UI.Dim).M(10, 0, 10, 0).At(0, 1),
                        UI.Pill("Удалить", () =>
                        {
                            if (Confirm($"Запустить удаление «{app.Name}»? Откроется официальный деинсталлятор программы — дальше следуй его шагам."))
                                Tweaks.Uninstall(app);
                        }).At(0, 2)));
                }
            }
            search.TextChanged += (_, __) => Render();
            Render();
            _ = Tweaks.InstalledApps().ContinueWith(t => Dispatcher.Invoke(() => { all = t.Result; Render(); }));
            page.Children.Add(Group("Установленные программы",
                Item("Поиск", "Самые большие — сверху. Удаление идёт через родной деинсталлятор программы, как в параметрах Windows.", null, search),
                list));
            return page;
        }

        // ------------------------------------------------------------------
        //  Вид Windows
        // ------------------------------------------------------------------

        const string ClassicMenuKey = @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}";

        static bool ClassicMenu
        {
            get { try { using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(ClassicMenuKey + @"\InprocServer32"); return k != null; } catch { return false; } }
            set
            {
                try
                {
                    if (value) { using var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(ClassicMenuKey + @"\InprocServer32"); k.SetValue("", ""); }
                    else Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(ClassicMenuKey, false);
                }
                catch { }
            }
        }

        UIElement TuneSec()
        {
            var page = new StackPanel();
            var status = Status();
            UIElement[] Rows(Tweak[] list) => list.Select(t => Item(t.Title, t.Desc, Safety.Safe, SysToggle(t.Get, t.Set), t.Note)).ToArray();
            page.Children.Add(Group("Тема", Rows(Tweaks.Theme.Take(5).ToArray())));
            page.Children.Add(Group("Панель задач", Rows(Tweaks.Taskbar)));
            page.Children.Add(Group("Проводник и рабочий стол", Rows(Tweaks.Explorer).Concat(new[]
            {
                Item("Классическое контекстное меню", "В Windows 11 правый клик сразу показывает полное меню, без «Показать дополнительные параметры».",
                     Safety.Safe, SysToggle(() => ClassicMenu, v => { ClassicMenu = v; status.Text = "Перезапусти проводник, чтобы увидеть изменения"; }),
                     "Меняется одна запись в твоём разделе реестра. Выключи — вернётся меню Windows 11."),
                Item("Значки на рабочем столе", "Спрятать все значки — для чистого рабочего стола или презентации. Файлы никуда не денутся.",
                     Safety.Safe, SysToggle(() => Laptop.DesktopIconsVisible, v => { if (Laptop.DesktopIconsVisible != v) Laptop.ToggleDesktopIcons(); })),
                Item("Перезапустить проводник", "Применяет изменения панели задач и меню сразу", null,
                     Act("Перезапустить", () => Task.Run(() => { Tweaks.RestartExplorer(); return "Проводник перезапущен ✓"; }), status)),
            }).ToArray()));
            page.Children.Add(status);

            var mouseVal = UI.Text(Tweaks.MouseSpeed.ToString(), 13, true);
            mouseVal.MinWidth = 30;
            mouseVal.TextAlignment = TextAlignment.Center;
            void Mouse(int d) { Tweaks.MouseSpeed = Tweaks.MouseSpeed + d; mouseVal.Text = Tweaks.MouseSpeed.ToString(); }
            var minus = UI.IconBtn("", () => Mouse(-1), 10, 24);
            var plus = UI.IconBtn("", () => Mouse(1), 10, 24);
            minus.Template = plus.Template = RoundTemplate();
            page.Children.Add(Group("Мышь и обои",
                Item("Скорость указателя", "1 — медленно, 20 — очень быстро, 10 — по умолчанию", null, UI.Row(minus, mouseVal, plus)),
                Item("Обои дня Bing в 4K", "Красивая фотография дня каждый день — нажми, когда захочешь новую", null,
                     Act("Поставить", Tweaks.BingWallpaper, status)),
                Item("Свои обои", null, null, UI.Pill("Выбрать файл", () =>
                {
                    var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Изображения|*.jpg;*.jpeg;*.png;*.bmp;*.webp" };
                    if (dlg.ShowDialog(this) == true) status.Text = Tweaks.SetWallpaper(dlg.FileName) ? "Обои установлены ✓" : "Windows не приняла картинку";
                }))));
            return page;
        }

        // ------------------------------------------------------------------
        //  Починить
        // ------------------------------------------------------------------

        UIElement FixSec()
        {
            var page = new StackPanel();
            var status = Status();
            page.Children.Add(Note("Выбери, что случилось, — N-Island запустит официальный способ исправления от Microsoft. Системные файлы меняются " +
                                   "только встроенными средствами Windows (SFC и DISM): они ставят правильные копии файлов, ничего лишнего не трогая.").M(4, 0, 4, 12));
            page.Children.Add(status);
            var rows = Laptop.Fixes.Select(f =>
            {
                var fix = f;
                return Item(f.Title, f.Explain, f.Safety, Act(f.Console ? "Запустить" : "Исправить", async () =>
                {
                    if (fix.Safety == Safety.Caution && !Confirm(fix.Explain + "\n\nПродолжить?")) return null;
                    return await Laptop.RunFix(fix);
                }, status), null, f.Admin);
            }).ToArray();
            page.Children.Add(Group("Частые проблемы", rows.Take(7).ToArray()));
            page.Children.Add(Group("Проверка и восстановление системы", rows.Skip(7).ToArray()));
            page.Children.Add(Note("Совет: перед «Осторожно»-пунктами создай точку восстановления на странице «Обзор»."));
            return page;
        }

        // ------------------------------------------------------------------
        //  Паспорт ноутбука
        // ------------------------------------------------------------------

        UIElement SystemSec()
        {
            var page = new StackPanel();
            var rows = new StackPanel();
            rows.Children.Add(Loading("Собираю сведения…"));
            page.Children.Add(Group(null, rows));
            List<(string Name, string Value)> data = null;
            page.Children.Add(Buttons(UI.Pill("Скопировать всё", () =>
            {
                if (data == null) return;
                ClipboardService.I.CopyBack(string.Join("\n", data.Select(d => $"{d.Name}: {d.Value}")));
                App.Island?.Toast("", "Паспорт скопирован", null);
            }), UI.Pill("Отчёт о батарее", () => _ = Tweaks.OpenBatteryReport())));
            _ = Laptop.Passport().ContinueWith(t => Dispatcher.Invoke(() =>
            {
                data = t.Result;
                rows.Children.Clear();
                foreach (var (name, value) in data)
                    rows.Children.Add(UI.Grid("170,*").M(14, 7, 14, 7).Add(UI.Text(name, 12.5, false, UI.Dim),
                        new TextBox { Text = value, IsReadOnly = true, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Foreground = Brushes.White,
                                      FontSize = 13, TextWrapping = TextWrapping.Wrap, Template = Ago.PlainBox() }.At(0, 1)));
            }));
            var tools = new WrapPanel { Margin = new Thickness(10, 8, 10, 8) };
            foreach (var (name, glyph, target) in Maint.WinTools)
            {
                var tg = target;
                tools.Children.Add(UI.Pill(UI.IconLabel(glyph, name), () => SystemService.Run(tg)).M(0, 0, 6, 6));
            }
            page.Children.Add(Group("Инструменты Windows", tools));
            page.Children.Add(Note("Серийный номер нужен для гарантии и поиска драйверов на сайте производителя."));
            return page;
        }

        // ------------------------------------------------------------------
        //  Автоматизация
        // ------------------------------------------------------------------

        UIElement AutoSec() => Panel(
            Group("Интерфейс",
                Item("Режим новичка", "Подробные объяснения под каждым пунктом. Выключи — объяснения будут во всплывающих подсказках.", null,
                     Toggle(nameof(Settings.CenterBeginner)))),
            Group("Сторожа в фоне",
                Item("Автоочистка", "Раз в несколько дней тихо чистит только безопасные пункты — временные файлы и кэш закрытых программ. Без прав администратора.",
                     Safety.Safe, Toggle(nameof(Settings.AutoClean))),
                Item("Как часто", null, null, Stepper(nameof(Settings.AutoCleanDays), 1, 30, 1, v => "раз в " + v + " дн.")),
                Item("Сторож камеры", "Всплывашка, когда программа включает камеру или запрашивает местоположение", Safety.Safe, Toggle(nameof(Settings.CamMicAlert))),
                Item("«Прожорливые» программы", "Подсказка, если программа долго грузит процессор", Safety.Safe, Toggle(nameof(Settings.HungryWatch))),
                Item("Мало места на диске", "Предупредить, если на системном диске меньше 10% свободного места", Safety.Safe, Toggle(nameof(Settings.DiskLowAlert))),
                Item("Напоминать о перезагрузке", "Если ноутбук долго не перезагружался", Safety.Safe, Toggle(nameof(Settings.RebootRemind))),
                Item("Через сколько дней", null, null, Stepper(nameof(Settings.RebootDays), 2, 30, 1, v => v + " дн.")),
                Item("Сигнал при заряде батареи", "Подсказка отключить зарядку — батарея прослужит дольше", Safety.Safe, Toggle(nameof(Settings.BatteryAlertHigh))),
                Item("Режим питания от зарядки", "Сам переключает производительность/экономию", Safety.Safe, Toggle(nameof(Settings.AutoPlan)))),
            Note("Все сторожа работают внутри N-Island одним лёгким таймером и почти не тратят память и заряд."));

        // ------------------------------------------------------------------
        //  Как пользоваться
        // ------------------------------------------------------------------

        UIElement CenterHelp()
        {
            UIElement Line(Safety s, string text) => UI.Grid("a,*").M(14, 8, 14, 8).Add(Badge(s).M(0, 0, 10, 0),
                new TextBlock { Text = text, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center }.At(0, 1));
            return Panel(
                Group("Что значат метки",
                    Line(Safety.Safe, "Можно смело включать: ничего не сломается, всё отменяется."),
                    Line(Safety.Caution, "Работает, но что-то может измениться — прочитай строку «Что изменится»."),
                    Line(Safety.NotRecommended, "Мы так делать не советуем. Такие пункты только объясняются — нажать на них нельзя.")),
                Group("Как отменить изменения",
                    Item("Защита от слежки", "Перед каждым изменением N-Island запоминает старое значение. Выключи переключатель или нажми «Вернуть всё как было».", null, null),
                    Item("Файл hosts", "Копия делается перед каждым изменением — «Вернуть из копии».", null, null),
                    Item("Очистка", "Удаляется только кэш и временные файлы — программы создадут их заново. Личные файлы всегда уходят в корзину.", null, null),
                    Item("Точка восстановления", "Самая надёжная страховка: снимок системы, к которому можно откатиться через «Восстановление системы».", null,
                         Act("Создать", RestorePoint))),
                Group("Права администратора",
                    Item("Зачем Windows спрашивает разрешение", "Системные политики, службы и папки Windows может менять только администратор. " +
                         "N-Island не просит права заранее: запрос появляется только на конкретное действие, и всё выполняется скриптом, который лежит в папке данных → admin — его можно открыть и прочитать.", null, null),
                    Item("Папка данных", Store.Dir, null, UI.Pill("Открыть", () => SystemService.Run(Store.Dir)))),
                Note("Горячая клавиша центра — Ctrl+Alt+L. Ещё его можно открыть из меню значка N-Island в трее и со страницы «Обслуживание» в чёлке."));
        }
    }
}
