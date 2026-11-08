using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using NIsland.Core;
using NIsland.Services;

namespace NIsland.Pages
{
    static class Ago
    {
        public static string Of(DateTime t)
        {
            var d = DateTime.Now - t;
            if (d.TotalSeconds < 60) return "сейчас";
            if (d.TotalMinutes < 60) return (int)d.TotalMinutes + " мин назад";
            if (t.Date == DateTime.Today) return t.ToString("HH:mm");
            return t.ToString("d MMM, HH:mm", Fmt.Ru);
        }

        public static ControlTemplate PlainBox()
        {
            var t = new ControlTemplate(typeof(TextBox));
            var bd = new FrameworkElementFactory(typeof(Border));
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
            bd.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF)));
            bd.SetValue(Border.PaddingProperty, new Thickness(8, 6, 8, 6));
            var host = new FrameworkElementFactory(typeof(ScrollViewer)) { Name = "PART_ContentHost" };
            bd.AppendChild(host);
            t.VisualTree = bd;
            return t;
        }
    }

    // =====================================================================
    //  Режимы и стиль — «пункт управления»
    // =====================================================================
    public class ModesPage : PageBase
    {
        readonly UniformGrid _modes = new() { Rows = 1 };
        readonly UniformGrid _presets = new() { Rows = 1 };
        readonly TextBlock _modeDesc = UI.Text("", 11.5);

        public ModesPage()
        {
            _modeDesc.Foreground = UI.Dim;
            Content = UI.Col(
                UI.Grid("a,*").Add(UI.Text("Режим", 12, true, UI.Dim), _modeDesc.At(0, 1).M(10, 0, 0, 0)),
                _modes.M(0, 4, 0, 8),
                UI.Text("Стиль острова", 12, true, UI.Dim),
                _presets.M(0, 4, 0, 0)).M(10, 0, 10, 0);
            S.PropertyChanged += (_, e) => { if (Visible && e.PropertyName is nameof(Settings.Mode) or nameof(Settings.Preset)) Render(); };
        }

        public override void OnShown() => Render();

        void Render()
        {
            _modes.Children.Clear();
            _modes.Columns = Modes.All.Length;
            foreach (var m in Modes.All)
            {
                var mode = m;
                bool on = S.Mode == m.Key;
                var tile = new Border
                {
                    CornerRadius = new CornerRadius(12), Margin = new Thickness(3), Padding = new Thickness(2, 7, 2, 7), Cursor = Cursors.Hand,
                    Background = on ? UI.Res("AccentBrush") : UI.Res("CardBrush"), ToolTip = m.Desc,
                    Child = UI.Col(UI.Icon(m.Glyph, 15), new TextBlock
                    {
                        Text = m.Key == "dnd" ? "Не беспок." : m.Name, FontSize = 10.5, HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness(0, 4, 0, 0),
                    }),
                };
                tile.OnAccent(on);
                tile.MouseLeftButtonUp += (_, e) => { App.Island.SetMode(mode.Key); e.Handled = true; };
                _modes.Children.Add(tile);
            }
            _modeDesc.Text = Modes.Current.Desc;

            _presets.Children.Clear();
            _presets.Columns = Presets.All.Length;
            foreach (var p in Presets.All)
            {
                var preset = p;
                bool on = S.Preset == p.Key;
                var fill = new LinearGradientBrush(ColorUtil.Parse(p.Bg1, Colors.Black), ColorUtil.Parse(p.Bg2, Colors.Black), 45);
                var dot = new Grid { Width = 30, Height = 30, Cursor = Cursors.Hand, ToolTip = p.Name + (p.Glass ? " · стекло" : "") };
                dot.Add(
                    new Ellipse { Fill = fill },
                    new Ellipse { Stroke = new SolidColorBrush(ColorUtil.WithAlpha(ColorUtil.Parse(p.RimColor, Colors.White), Math.Max(0.25, p.Rim))), StrokeThickness = 1.5 },
                    new Ellipse
                    {
                        Margin = new Thickness(4, 3, 4, 14),
                        Fill = new SolidColorBrush(Color.FromArgb((byte)(255 * Math.Max(p.Sheen, p.Glass ? 0.12 : 0)), 255, 255, 255)),
                    },
                    new Ellipse { Width = 8, Height = 8, Fill = new SolidColorBrush(ColorUtil.Parse(p.Accent, Colors.White)), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom });
                var ring = new Border
                {
                    Width = 38, Height = 38, CornerRadius = new CornerRadius(19), BorderThickness = new Thickness(2),
                    BorderBrush = on ? Brushes.White : Brushes.Transparent, Child = dot, HorizontalAlignment = HorizontalAlignment.Center,
                };
                ring.MouseLeftButtonUp += (_, e) => { Presets.Apply(preset); e.Handled = true; };
                var cell = UI.Col(ring, UI.Text(I18n.T(p.Name).Split(' ')[0], 9.5, false, on ? Brushes.White : UI.Faint).HA(HorizontalAlignment.Center).M(0, 2, 0, 0));
                _presets.Children.Add(cell);
            }
        }
    }

    // =====================================================================
    //  Центр уведомлений — «шторка»
    // =====================================================================
    public class CenterPage : PageBase
    {
        readonly StackPanel _list = new();
        readonly TextBlock _count = UI.Text("", 12, true);

        public CenterPage()
        {
            var head = UI.Grid("*,a").Add(_count,
                UI.Pill(UI.IconLabel("", "Очистить", 11), NotificationLog.Clear).At(0, 1));
            ((Button)head.Children[1]).Padding = new Thickness(10, 3, 10, 3);
            var scroll = new ScrollViewer { Content = _list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 6, 0, 0) };
            Content = UI.Grid(null, "a,*").M(8, 0, 8, 2).Add(head, scroll.At(1, 0));
            NotificationLog.Changed += () => { if (Visible) Render(); };
        }

        public override void OnShown() { NotificationLog.MarkRead(); Render(); }
        public override void OnHidden() => _list.Children.Clear();

        void Render()
        {
            var items = NotificationLog.Items;
            _count.Text = items.Count == 0 ? "Уведомлений нет" : $"Уведомления · {items.Count}";
            _list.Children.Clear();
            if (items.Count == 0)
                _list.Children.Add(UI.Text("Здесь будут сообщения и звонки из всех приложений и сайтов — даже в режиме «Не беспокоить».",
                    12, false, UI.Dim).M(2, 6, 0, 0));
            foreach (var it in items.Take(40))
            {
                var item = it;
                var icon = new Border
                {
                    Width = 30, Height = 30, CornerRadius = new CornerRadius(8),
                    Background = it.IsCall ? UI.Hex("#3330D158") : UI.Hex("#22FFFFFF"),
                    Child = UI.Icon(it.IsCall ? "" : "", 13, it.IsCall ? UI.Hex("#30D158") : Brushes.White),
                };
                var text = UI.Col(
                    UI.Grid("*,a").Add(UI.Text(it.App, 11, true, UI.Dim), UI.Text(Ago.Of(it.Time), 10.5, false, UI.Faint).At(0, 1)),
                    UI.Text(it.Title, 12.5, true),
                    string.IsNullOrEmpty(it.Body) ? null : UI.Text(it.Body, 11.5, false, UI.Dim));
                var del = UI.IconBtn("", () => NotificationLog.Remove(item), 9, 22, "Убрать");
                del.Opacity = 0;
                del.VerticalAlignment = VerticalAlignment.Top;
                var row = new Border
                {
                    CornerRadius = new CornerRadius(12), Background = UI.Res("CardBrush"), Padding = new Thickness(8, 6, 6, 6),
                    Margin = new Thickness(0, 0, 4, 5), Cursor = Cursors.Hand,
                    Child = UI.Grid("a,*,a").Add(icon.VA(VerticalAlignment.Top), text.At(0, 1).M(10, 0, 4, 0), del.At(0, 2)),
                };
                row.MouseEnter += (_, __) => del.Opacity = 1;
                row.MouseLeave += (_, __) => del.Opacity = 0;
                row.MouseLeftButtonUp += (_, e) => { if (!e.Handled) CallService.OpenApp(item.AppId); };
                _list.Children.Add(row);
            }
        }
    }

    // =====================================================================
    //  Буфер обмена
    // =====================================================================
    public class ClipboardPage : PageBase
    {
        readonly TextBox _search = UI.Box("Поиск по истории буфера…");
        readonly StackPanel _list = new();

        public ClipboardPage()
        {
            _search.TextChanged += (_, __) => Render();
            var head = UI.Grid("*,a").Add(_search, UI.IconBtn("", () => { ClipboardService.I.Clear(); Render(); }, 13, 30, "Очистить (кроме закреплённых)").At(0, 1).M(6, 0, 0, 0));
            var scroll = new ScrollViewer { Content = _list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 6, 0, 0) };
            Content = UI.Grid(null, "a,*").M(8, 0, 8, 2).Add(head, scroll.At(1, 0));
            ClipboardService.I.Copied += _ => { if (Visible) Render(); };
        }

        public override void OnShown() => Render();
        public override void OnHidden() => _list.Children.Clear();

        void Render()
        {
            _list.Children.Clear();
            if (!S.ClipWatch)
            {
                _list.Children.Add(UI.Text("История буфера выключена в настройках", 12, false, UI.Dim).M(2, 6, 0, 0));
                return;
            }
            var q = _search.Text.Trim();
            var items = ClipboardService.I.Items.Where(x => q == "" || x.Text.Contains(q, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.Pinned).ToList();
            if (items.Count == 0)
                _list.Children.Add(UI.Text(q == "" ? "Скопируй любой текст — он появится здесь. Клик по записи — скопировать снова." : "Ничего не найдено",
                    12, false, UI.Dim).M(2, 6, 0, 0));
            foreach (var it in items)
            {
                var item = it;
                var one = it.Text.Replace("\r", " ").Replace("\n", " ⏎ ").Trim();
                if (one.Length > 140) one = one[..140] + "…";
                var pin = UI.IconBtn(it.Pinned ? "" : "", () => { item.Pinned = !item.Pinned; Store.SaveData(); Render(); }, 11, 24, item.Pinned ? "Открепить" : "Закрепить");
                var del = UI.IconBtn("", () => { ClipboardService.I.Items.Remove(item); Store.SaveData(); Render(); }, 9, 24, "Удалить");
                pin.Opacity = it.Pinned ? 1 : 0;
                del.Opacity = 0;
                var row = new Border
                {
                    CornerRadius = new CornerRadius(10), Background = Brushes.Transparent, Padding = new Thickness(8, 4, 4, 4), Cursor = Cursors.Hand,
                    Child = UI.Grid("*,a,a,a").Add(
                        UI.Text(one, 12.5, it.Pinned),
                        UI.Text(Ago.Of(it.Time), 10.5, false, UI.Faint).At(0, 1).M(8, 0, 4, 0),
                        pin.At(0, 2), del.At(0, 3)),
                };
                row.MouseEnter += (_, __) => { row.Background = UI.Res("CardBrush"); pin.Opacity = 1; del.Opacity = 1; };
                row.MouseLeave += (_, __) => { row.Background = Brushes.Transparent; pin.Opacity = item.Pinned ? 1 : 0; del.Opacity = 0; };
                row.MouseLeftButtonUp += (_, e) =>
                {
                    if (e.Handled) return;
                    ClipboardService.I.CopyBack(item.Text);
                    App.Island.Toast("", "Скопировано", one.Length > 50 ? one[..50] + "…" : one);
                };
                _list.Children.Add(row);
            }
        }
    }

    // =====================================================================
    //  Заметки
    // =====================================================================
    public class NotesPage : PageBase
    {
        readonly TextBox _box = new()
        {
            AcceptsReturn = true, AcceptsTab = true, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.White, CaretBrush = Brushes.White,
            FontSize = 13, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, BorderThickness = new Thickness(0),
        };
        readonly TextBlock _info = UI.Text("", 11);

        public NotesPage()
        {
            _box.Template = Ago.PlainBox();
            _box.SelectionBrush = UI.Res("AccentBrush");
            _info.Foreground = UI.Faint;
            _box.TextChanged += (_, __) =>
            {
                Store.Data.Notes = _box.Text;
                Store.SaveData();
                _info.Text = $"{_box.Text.Length} симв. · сохраняется само";
            };
            var foot = UI.Grid("*,a,a").Add(_info,
                UI.IconBtn("", () => { if (_box.Text != "") { ClipboardService.I.CopyBack(_box.Text); App.Island.Toast("", "Заметка скопирована", null); } }, 12, 26, "Копировать всё").At(0, 1),
                UI.IconBtn("", () => _box.Clear(), 12, 26, "Очистить").At(0, 2));
            Content = UI.Grid(null, "*,a").M(8, 0, 8, 0).Add(_box, foot.At(1, 0).M(2, 2, 0, 0));
        }

        public override void OnShown()
        {
            if (_box.Text != Store.Data.Notes) _box.Text = Store.Data.Notes;
            _info.Text = $"{_box.Text.Length} симв. · сохраняется само";
        }
    }

    // =====================================================================
    //  Таймер, секундомер, сон для музыки
    // =====================================================================
    public class TimerPage : PageBase
    {
        static TimerService T => TimerService.I;
        readonly TextBlock _time = UI.Text("05:00", 32, true);
        readonly Button _start;
        readonly TextBlock _sw = UI.Text("00:00.0", 24, true);
        readonly Button _swBtn;
        readonly TextBlock _laps = UI.Text("", 11);
        readonly StackPanel _sleep = new() { Orientation = Orientation.Horizontal };
        readonly DispatcherTimer _ui = new() { Interval = TimeSpan.FromMilliseconds(100) };

        public TimerPage()
        {
            _laps.Foreground = UI.Dim;
            _laps.TextWrapping = TextWrapping.Wrap;
            var chips = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var m in new[] { 1, 3, 5, 10, 15, 25, 60 })
            {
                int mm = m;
                var b = UI.Pill(m + "м", () => { T.Reset(); T.SetMinutes(mm); });
                b.Padding = new Thickness(8, 3, 8, 3);
                b.FontSize = 11;
                b.Margin = new Thickness(0, 0, 4, 0);
                chips.Children.Add(b);
            }
            _start = UI.Pill("Старт", T.Toggle, true);
            _start.Padding = new Thickness(18, 5, 18, 5);
            var ctl = UI.Row(_start,
                UI.IconBtn("", () => T.Add(-1), 11, 28, "−1 мин").M(6, 0, 0, 0),
                UI.IconBtn("", () => T.Add(1), 11, 28, "+1 мин"),
                UI.IconBtn("", T.Reset, 12, 28, "Сбросить"));

            var left = UI.Col(_time, chips.M(0, 4, 0, 6), ctl, _sleep.M(0, 8, 0, 0)).M(12, 0, 0, 0);

            _swBtn = UI.Pill("Старт", T.SwToggle);
            _swBtn.Padding = new Thickness(14, 4, 14, 4);
            var right = UI.Col(
                UI.Text("Секундомер", 11.5, true, UI.Dim),
                _sw,
                UI.Row(_swBtn, UI.IconBtn("", T.SwLap, 12, 28, "Круг").M(4, 0, 0, 0), UI.IconBtn("", T.SwReset, 12, 28, "Сбросить")),
                _laps.M(0, 4, 0, 0));
            right.VerticalAlignment = VerticalAlignment.Top;
            right.Margin = new Thickness(0, 4, 10, 0);

            Content = UI.Grid("*,200").Add(left, right.At(0, 1));
            _ui.Tick += (_, __) => Render();
            T.Changed += () => { if (Visible) Render(); };
        }

        public override void OnShown() { Render(); _ui.Start(); }
        public override void OnHidden() => _ui.Stop();

        void Render()
        {
            _time.Text = Fmt.Time(T.Left);
            _time.Foreground = T.Running ? UI.Hex("#FF9F0A") : Brushes.White;
            _start.Content = T.Running ? "Пауза" : T.Paused ? "Дальше" : "Старт";
            var e = T.SwElapsed;
            _sw.Text = $"{(int)e.TotalMinutes:00}:{e.Seconds:00}.{e.Milliseconds / 100}";
            _swBtn.Content = T.SwRunning ? "Стоп" : e > TimeSpan.Zero ? "Дальше" : "Старт";
            _laps.Text = string.Join("   ", T.Laps.Take(4).Select((l, i) => $"#{T.Laps.Count - i} {(int)l.TotalMinutes:00}:{l.Seconds:00}.{l.Milliseconds / 100}"));

            _sleep.Children.Clear();
            _sleep.Children.Add(UI.Icon("", 12, UI.Dim).M(0, 0, 6, 0));
            _sleep.Children.Add(UI.Text(T.SleepAt is DateTime s ? $"Пауза музыки в {s:HH:mm}" : "Сон музыки:", 11.5, false, UI.Dim).M(0, 0, 6, 0));
            foreach (var m in new[] { 0, 15, 30, 60 })
            {
                int mm = m;
                var b = UI.Pill(m == 0 ? "выкл" : m + "м", () => T.SetSleep(mm));
                b.Padding = new Thickness(7, 2, 7, 2);
                b.FontSize = 10.5;
                b.Margin = new Thickness(0, 0, 3, 0);
                _sleep.Children.Add(b);
            }
            if (!T.Running && !T.SwRunning && T.SleepAt == null) _ui.Stop(); else if (Visible) _ui.Start();
        }
    }

    // =====================================================================
    //  Переводчик
    // =====================================================================
    public class TranslatePage : PageBase
    {
        readonly TextBox _in = new()
        {
            AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.White, CaretBrush = Brushes.White,
            FontSize = 13, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, BorderThickness = new Thickness(0),
        };
        readonly TextBlock _out = new() { FontSize = 13.5, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        readonly TextBlock _dir = UI.Text("", 11.5);
        readonly StackPanel _langs = new() { Orientation = Orientation.Horizontal };
        readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(650) };
        int _req;

        public TranslatePage()
        {
            _in.Template = Ago.PlainBox();
            _in.SelectionBrush = UI.Res("AccentBrush");
            _dir.Foreground = UI.Dim;
            _in.TextChanged += (_, __) => { _debounce.Stop(); _debounce.Start(); };
            _debounce.Tick += (_, __) => { _debounce.Stop(); Run(); };

            var head = UI.Grid("*,a,a").Add(_langs,
                UI.Pill(UI.IconLabel("", "Из буфера", 11), FromClipboard).At(0, 1),
                UI.IconBtn("", () => { if (_out.Text != "") { ClipboardService.I.CopyBack(_out.Text); App.Island.Toast("", "Перевод скопирован", null); } }, 12, 28, "Копировать перевод").At(0, 2).M(4, 0, 0, 0));
            ((Button)head.Children[1]).Padding = new Thickness(10, 3, 10, 3);

            var outBox = new Border
            {
                CornerRadius = new CornerRadius(10), Background = UI.Hex("#10FFFFFF"), Padding = new Thickness(10, 6, 10, 6),
                Child = new ScrollViewer { Content = _out, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            };
            var body = UI.Grid("*,*").M(0, 6, 0, 0).Add(_in.M(0, 0, 4, 0), outBox.At(0, 1).M(4, 0, 0, 0));
            Content = UI.Grid(null, "a,*,a").M(8, 0, 8, 0).Add(head, body.At(1, 0), _dir.At(2, 0).M(2, 3, 0, 0));
        }

        public override void OnShown()
        {
            RenderLangs();
            if (_in.Text == "") _dir.Text = "Пиши или вставь текст — переведу автоматически. Язык оригинала определяется сам.";
        }

        void RenderLangs()
        {
            _langs.Children.Clear();
            foreach (var (code, name) in TranslateService.Langs.Take(6))
            {
                var c = code;
                bool on = string.Equals(S.TranslateTo, code, StringComparison.OrdinalIgnoreCase);
                var b = UI.Pill(code.ToUpperInvariant()[..2], () => { S.TranslateTo = c; RenderLangs(); Run(); }, on, "Перевести на: " + name);
                b.Padding = new Thickness(9, 3, 9, 3);
                b.FontSize = 11;
                b.Margin = new Thickness(0, 0, 4, 0);
                if (!on) b.Background = UI.Hex("#1EFFFFFF");
                _langs.Children.Add(b);
            }
        }

        void FromClipboard()
        {
            try { if (Clipboard.ContainsText()) _in.Text = Clipboard.GetText(); } catch { }
        }

        async void Run()
        {
            var text = _in.Text;
            if (string.IsNullOrWhiteSpace(text)) { _out.Text = ""; return; }
            int id = ++_req;
            _dir.Text = "Перевожу…";
            try
            {
                var (res, from, to) = await TranslateService.Translate(text, S.TranslateTo);
                if (id != _req) return;
                _out.Text = res;
                _dir.Text = $"{TranslateService.Name(from)} → {TranslateService.Name(to)}";
            }
            catch
            {
                if (id == _req) _dir.Text = "Не удалось перевести — проверь интернет";
            }
        }
    }

    // =====================================================================
    //  Запуск: избранное, поиск, калькулятор, валюты, пипетка
    // =====================================================================
    public class LauncherPage : PageBase
    {
        readonly TextBox _q = UI.Box("Поиск в интернете, адрес сайта или пример: 12*(3+4)");
        readonly TextBlock _hint = UI.Text("", 12);
        readonly WrapPanel _tiles = new() { Orientation = Orientation.Vertical };

        public static void Add(IEnumerable<string> paths)
        {
            foreach (var p in paths)
            {
                if (Store.Data.Launch.Any(x => string.Equals(x.Target, p, StringComparison.OrdinalIgnoreCase))) continue;
                Store.Data.Launch.Add(new LaunchItem { Name = System.IO.Path.GetFileNameWithoutExtension(p.TrimEnd('\\')) is { Length: > 0 } n ? n : p, Target = p });
            }
            Store.SaveData();
        }

        public LauncherPage()
        {
            _hint.Foreground = UI.Dim;
            _q.TextChanged += (_, __) => Hint();
            _q.KeyDown += (_, e) => { if (e.Key == Key.Enter) Go(); };
            var head = UI.Grid("*,a,a").Add(_q,
                UI.IconBtn("", () => { App.Island.Collapse(true); ColorPicker.I.Start(); }, 14, 32, "Пипетка: кликни на любой цвет экрана — HEX скопируется").At(0, 1).M(6, 0, 0, 0),
                UI.IconBtn("", AddDialog, 13, 32, "Добавить программу, файл или папку").At(0, 2));
            var scroll = new ScrollViewer { Content = _tiles, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
            scroll.PreviewMouseWheel += (_, e) => { scroll.ScrollToHorizontalOffset(scroll.HorizontalOffset - e.Delta / 2.0); e.Handled = scroll.ScrollableWidth > 0; };
            Content = UI.Grid(null, "a,a,*").M(8, 0, 8, 0).Add(head, _hint.At(1, 0).M(4, 4, 0, 2), scroll.At(2, 0));
        }

        public override void OnShown() { Hint(); Render(); }
        public override void OnHidden() => _tiles.Children.Clear();

        string _money;
        int _moneyReq;

        async void Hint()
        {
            var t = _q.Text.Trim();
            var calc = Calc.TryEval(t);
            var cur = calc == null ? Currency.Parse(t) : null;
            _money = null;
            _hint.Text = t == "" ? "Пример, сумма в валюте (100 usd, 50 евро в рубли), сайт или поиск · правый клик по значку — убрать"
                : calc != null ? $"= {calc}   (Enter — скопировать)"
                : cur != null ? "Считаю по курсу…"
                : LooksLikeUrl(t) ? "Enter — открыть сайт"
                : $"Enter — искать в {(S.SearchEngine == "yandex" ? "Яндексе" : S.SearchEngine == "bing" ? "Bing" : "Google")}";
            if (cur is { } c)
            {
                int id = ++_moneyReq;
                var v = await Currency.Convert(c.Amount, c.From, c.To);
                if (id != _moneyReq) return;
                if (v is double val)
                {
                    _money = Currency.Fmt(val, c.To);
                    _hint.Text = $"{Currency.Fmt(c.Amount, c.From)} ≈ {_money}   (Enter — скопировать, курс open.er-api.com)";
                }
                else _hint.Text = "Не удалось получить курс — проверь интернет";
            }
        }

        static bool LooksLikeUrl(string t) => !t.Contains(' ') && (t.StartsWith("http") || System.Text.RegularExpressions.Regex.IsMatch(t, @"^[\w\-]+(\.[\w\-]+)+(/.*)?$"));

        void Go()
        {
            var t = _q.Text.Trim();
            if (t == "") return;
            var calc = Calc.TryEval(t);
            if (calc != null) { ClipboardService.I.CopyBack(calc); App.Island.Toast("", "Результат скопирован", calc); return; }
            if (_money != null) { ClipboardService.I.CopyBack(_money); App.Island.Toast("", "Сумма скопирована", _money); return; }
            if (Currency.Parse(t) != null) return; // курс ещё грузится
            string url = LooksLikeUrl(t) ? (t.StartsWith("http") ? t : "https://" + t)
                : S.SearchEngine switch
                {
                    "yandex" => "https://yandex.ru/search/?text=",
                    "bing" => "https://www.bing.com/search?q=",
                    _ => "https://www.google.com/search?q=",
                } + Uri.EscapeDataString(t);
            SystemService.Run(url);
            _q.Text = "";
            App.Island.Collapse(true);
        }

        void AddDialog()
        {
            var island = App.Island;
            island.Hold = true;
            try
            {
                var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Добавить в «Запуск»", Filter = "Программы и ярлыки|*.exe;*.lnk;*.url;*.bat|Все файлы|*.*" };
                if (dlg.ShowDialog() == true) { Add(new[] { dlg.FileName }); Render(); }
            }
            finally { island.Hold = false; island.Nudge(); }
        }

        void Render()
        {
            _tiles.Children.Clear();
            foreach (var it in Store.Data.Launch.ToList())
            {
                var item = it;
                FrameworkElement icon;
                var path = Resolve(it.Target);
                var img = path != null ? FileIcons.Get(path) : null;
                if (img != null) icon = new Image { Source = img, Width = 32, Height = 32 };
                else icon = UI.Icon(it.Target.StartsWith("http") ? "" : it.Target.StartsWith("ms-settings") ? "" : it.Target.StartsWith("shell:") ? "" : "", 24);
                var tile = new Border
                {
                    Width = 78, Height = 72, CornerRadius = new CornerRadius(12), Margin = new Thickness(2), Background = Brushes.Transparent, Cursor = Cursors.Hand,
                    ToolTip = it.Target,
                    Child = UI.Col(icon.HA(HorizontalAlignment.Center),
                        new TextBlock { Text = it.Name, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Center, Margin = new Thickness(2, 5, 2, 0) }),
                };
                tile.MouseEnter += (_, __) => tile.Background = UI.Res("CardBrush");
                tile.MouseLeave += (_, __) => tile.Background = Brushes.Transparent;
                tile.MouseLeftButtonUp += (_, __) => { SystemService.Run(item.Target); App.Island.Collapse(true); };
                tile.MouseRightButtonUp += (_, e) => { Store.Data.Launch.Remove(item); Store.SaveData(); Render(); e.Handled = true; };
                _tiles.Children.Add(tile);
            }
        }

        static string Resolve(string target)
        {
            try
            {
                if (target.Contains(':') && !target.Contains(":\\")) return null; // url / ms-settings / shell:
                if (File.Exists(target) || Directory.Exists(target)) return target;
                foreach (var dir in new[] { Environment.SystemDirectory, Environment.GetFolderPath(Environment.SpecialFolder.Windows) })
                {
                    var p = System.IO.Path.Combine(dir, target);
                    if (File.Exists(p)) return p;
                }
            }
            catch { }
            return null;
        }
    }
}
