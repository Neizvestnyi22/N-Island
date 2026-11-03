using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using NIsland.Controls;
using NIsland.Core;
using NIsland.Services;

namespace NIsland.Pages
{
    // =====================================================================
    //  Календарь
    // =====================================================================
    public class CalendarPage : PageBase
    {
        DateTime _month = new(DateTime.Today.Year, DateTime.Today.Month, 1);
        DateTime _sel = DateTime.Today;
        readonly TextBlock _monthLabel = UI.Text("", 13, true);
        readonly UniformGrid _days = new() { Columns = 7, Rows = 6 };
        readonly UniformGrid _week = new() { Columns = 7 };
        readonly TextBlock _selLabel = UI.Text("", 13, true);
        readonly StackPanel _list = new();
        readonly TextBox _time = UI.Box("чч:мм", 58);
        readonly TextBox _title = UI.Box("Новое событие");

        public CalendarPage()
        {
            var head = UI.Grid("*,a,a").Add(
                _monthLabel,
                UI.IconBtn("", () => { _month = _month.AddMonths(-1); Render(); }, 10, 22).At(0, 1),
                UI.IconBtn("", () => { _month = _month.AddMonths(1); Render(); }, 10, 22).At(0, 2));

            var left = UI.Grid(null, "22,16,*").M(4, 0, 0, 0);
            left.Width = 230;
            left.Add(head, _week.At(1, 0), _days.At(2, 0));

            _list.Margin = new Thickness(0, 4, 0, 4);
            var scroll = new ScrollViewer { Content = _list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            _time.FontSize = _title.FontSize = 12;
            var add = UI.Grid("a,*,a").Add(
                _time,
                _title.At(0, 1).M(6, 0, 6, 0),
                UI.IconBtn("", AddEvent, 12, 28, "Добавить").At(0, 2));
            _title.KeyDown += (_, e) => { if (e.Key == Key.Enter) AddEvent(); };
            _time.KeyDown += (_, e) => { if (e.Key == Key.Enter) _title.Focus(); };

            var right = UI.Grid(null, "22,*,a").M(18, 0, 6, 4).Add(_selLabel, scroll.At(1, 0), add.At(2, 0));

            Content = UI.Grid("a,*").Add(left, right.At(0, 1));
            Store.Data.Events.CollectionChanged += (_, __) => { if (Visible) Render(); };
        }

        public override void OnShown() => Render();

        void Render()
        {
            var ru = Fmt.Ru;
            _monthLabel.Text = Fmt.Cap(ru.DateTimeFormat.MonthNames[_month.Month - 1]) + " " + _month.Year;
            _week.Children.Clear();
            var names = S.CalMondayFirst ? new[] { "Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Вс" } : new[] { "Вс", "Пн", "Вт", "Ср", "Чт", "Пт", "Сб" };
            foreach (var n in names)
                _week.Children.Add(UI.Text(n, 10, false, UI.Faint).HA(HorizontalAlignment.Center));

            _days.Children.Clear();
            int first = (int)_month.DayOfWeek;
            if (S.CalMondayFirst) first = (first + 6) % 7;
            var start = _month.AddDays(-first);
            var withEvents = Store.Data.Events.Select(e => e.Date.Date).ToHashSet();
            for (int i = 0; i < 42; i++)
            {
                var d = start.AddDays(i);
                bool cur = d.Month == _month.Month, today = d == DateTime.Today, sel = d == _sel;
                var tb = UI.Text(d.Day.ToString(), 11, today || sel, cur ? Brushes.White : UI.Faint).HA(HorizontalAlignment.Center);
                if (today) tb.SetResourceReference(TextBlock.ForegroundProperty, "AccentTextBrush");
                var cell = new Grid { Background = Brushes.Transparent, Cursor = Cursors.Hand };
                var bg = new Border
                {
                    Width = 20, Height = 17, CornerRadius = new CornerRadius(8.5),
                    Background = today ? UI.Res("AccentBrush") : sel ? UI.Hex("#33FFFFFF") : Brushes.Transparent,
                };
                cell.Add(bg, tb);
                if (withEvents.Contains(d) && !today)
                    cell.Add(new Ellipse { Width = 3, Height = 3, Fill = UI.Res("AccentBrush"), VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center });
                var day = d;
                cell.MouseLeftButtonUp += (_, e) =>
                {
                    _sel = day;
                    if (day.Month != _month.Month) _month = new DateTime(day.Year, day.Month, 1);
                    Render();
                    e.Handled = true;
                };
                _days.Children.Add(cell);
            }

            _selLabel.Text = _sel == DateTime.Today ? "Сегодня" : Fmt.Cap(_sel.ToString("dddd, d MMMM", ru));
            _list.Children.Clear();
            var evs = Store.Data.Events.Where(e => e.Date.Date == _sel).OrderBy(e => e.Time ?? "").ToList();
            if (evs.Count == 0)
                _list.Children.Add(UI.Text("Нет событий", 12, false, UI.Dim).M(0, 6, 0, 0));
            foreach (var ev in evs)
            {
                var item = ev;
                var del = UI.IconBtn("", () => { Store.Data.Events.Remove(item); Store.SaveData(); }, 9, 22, "Удалить");
                del.Opacity = 0.5;
                var row = UI.Grid("a,*,a").M(0, 2, 0, 2).Add(
                    new Border { Width = 3, Height = 26, CornerRadius = new CornerRadius(1.5), Background = UI.Res("AccentBrush") },
                    UI.Col(UI.Text(ev.Title, 12.5, true), UI.Text(ev.TimeLabel, 11, false, UI.Dim)).At(0, 1).M(8, 0, 4, 0),
                    del.At(0, 2));
                _list.Children.Add(row);
            }
        }

        void AddEvent()
        {
            var title = _title.Text.Trim();
            if (title == "") return;
            string time = null;
            var t = _time.Text.Trim().Replace('.', ':').Replace(' ', ':');
            if (t.Length > 0)
            {
                if (t.Length <= 2 && int.TryParse(t, out var hh)) t = hh + ":00";
                if (TimeSpan.TryParse(t, out var ts) && ts < TimeSpan.FromDays(1)) time = ts.ToString(@"hh\:mm");
            }
            var ev = new CalEvent { Date = _sel, Time = time, Title = title };
            if (ev.At is DateTime at && at < DateTime.Now) ev.Notified = true;
            Store.Data.Events.Add(ev);
            Store.SaveData();
            _title.Text = "";
            _time.Text = "";
        }
    }

    // =====================================================================
    //  Задачи
    // =====================================================================
    public class TodoPage : PageBase
    {
        readonly TextBox _input = UI.Box("Новая задача… (Enter)");
        readonly StackPanel _list = new();
        readonly TextBlock _footer = UI.Text("", 11.5, false);

        public TodoPage()
        {
            _footer.Foreground = UI.Dim;
            _input.KeyDown += (_, e) => { if (e.Key == Key.Enter) Add(); };
            var top = UI.Grid("*,a").Add(_input, UI.IconBtn("", Add, 13, 30, "Добавить").At(0, 1).M(6, 0, 0, 0));
            var scroll = new ScrollViewer { Content = _list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 6, 0, 0) };
            var foot = UI.Grid("*,a").Add(_footer,
                UI.Pill("Убрать выполненные", ClearDone).At(0, 1));
            ((Button)foot.Children[1]).Padding = new Thickness(10, 3, 10, 3);
            ((Button)foot.Children[1]).FontSize = 11;
            Content = UI.Grid(null, "a,*,a").M(8, 0, 8, 2).Add(top, scroll.At(1, 0), foot.At(2, 0));
        }

        public override void OnShown() => Render();

        void Add()
        {
            var t = _input.Text.Trim();
            if (t == "") return;
            Store.Data.Todos.Insert(0, new TodoItem { Text = t });
            Store.SaveData();
            _input.Text = "";
            Render();
        }

        void ClearDone()
        {
            foreach (var d in Store.Data.Todos.Where(x => x.Done).ToList()) Store.Data.Todos.Remove(d);
            Store.SaveData();
            Render();
        }

        void Render()
        {
            _list.Children.Clear();
            var items = Store.Data.Todos.Where(t => !S.TodoHideDone || !t.Done).OrderBy(t => t.Done).ToList();
            if (items.Count == 0)
                _list.Children.Add(UI.Text("Задач нет — можно отдыхать ✨", 12.5, false, UI.Dim).M(4, 8, 0, 0));
            foreach (var t in items)
            {
                var item = t;
                var chk = new CheckBox { Style = UI.St("RoundCheck"), IsChecked = t.Done, VerticalAlignment = VerticalAlignment.Center };
                chk.Click += (_, __) => { item.Done = chk.IsChecked == true; Store.SaveData(); Render(); };
                var text = UI.Text(t.Text, 13, false, t.Done ? UI.Faint : Brushes.White);
                if (t.Done) text.TextDecorations = TextDecorations.Strikethrough;
                var del = UI.IconBtn("", () => { Store.Data.Todos.Remove(item); Store.SaveData(); Render(); }, 11, 24, "Удалить");
                del.Visibility = Visibility.Hidden;
                var row = UI.Grid("a,*,a").Add(chk, text.At(0, 1).M(10, 0, 4, 0), del.At(0, 2));
                var box = new Border { Child = row, Padding = new Thickness(6, 3, 4, 3), CornerRadius = new CornerRadius(8), Background = Brushes.Transparent };
                box.MouseEnter += (_, __) => { del.Visibility = Visibility.Visible; box.Background = UI.Res("CardBrush"); };
                box.MouseLeave += (_, __) => { del.Visibility = Visibility.Hidden; box.Background = Brushes.Transparent; };
                _list.Children.Add(box);
            }
            int done = Store.Data.Todos.Count(x => x.Done);
            _footer.Text = $"{Store.Data.Todos.Count - done} осталось · {done} выполнено";
        }
    }

    // =====================================================================
    //  Помодоро
    // =====================================================================
    public class PomodoroPage : PageBase
    {
        static PomodoroService P => PomodoroService.I;
        readonly StatusRing _ring = new() { Width = 112, Height = 112, Thickness = 7 };
        readonly TextBlock _time = UI.Text("25:00", 26, true);
        readonly TextBlock _phase = UI.Text("Фокус", 12);
        readonly Button _start;
        readonly RadioButton[] _phases = new RadioButton[3];
        readonly TextBlock _stats = UI.Text("", 12);

        public PomodoroPage()
        {
            _time.HorizontalAlignment = _phase.HorizontalAlignment = HorizontalAlignment.Center;
            _phase.Foreground = UI.Dim;
            var ringBox = new Grid { Width = 112, Height = 112 }.Add(_ring, UI.Col(_time, _phase));

            var seg = new StackPanel { Orientation = Orientation.Horizontal };
            string[] names = { "Фокус", "Перерыв", "Длинный" };
            for (int i = 0; i < 3; i++)
            {
                int k = i;
                _phases[i] = new RadioButton { Style = UI.St("Segment"), Content = names[i], GroupName = "pomo" };
                _phases[i].Click += (_, __) => P.SetPhase((PomoPhase)k);
                seg.Children.Add(_phases[i]);
            }
            var segBox = new Border { Background = UI.Hex("#22FFFFFF"), CornerRadius = new CornerRadius(8), Padding = new Thickness(2), Child = seg, HorizontalAlignment = HorizontalAlignment.Left };

            _start = UI.Pill("Старт", P.Toggle, true);
            _start.Padding = new Thickness(22, 7, 22, 7);
            _start.FontSize = 13;
            var buttons = UI.Row(_start,
                UI.IconBtn("", P.Reset, 14, 34, "Сбросить").M(8, 0, 0, 0),
                UI.IconBtn("", P.Skip, 14, 34, "Пропустить фазу"));
            _stats.Foreground = UI.Dim;

            var right = UI.Col(segBox, buttons.M(0, 14, 0, 0), _stats.M(2, 12, 0, 0)).M(28, 0, 0, 0);
            Content = UI.Row(ringBox, right).M(18, 0, 0, 0);
            P.Changed += () => { if (Visible) Render(); };
        }

        public override void OnShown() => Render();

        void Render()
        {
            _time.Text = Fmt.Time(P.Remaining);
            _phase.Text = P.PhaseName;
            _ring.Progress = P.Progress;
            _ring.RingBrush = new SolidColorBrush(P.Phase == PomoPhase.Focus ? Color.FromRgb(0xFF, 0x45, 0x3A) : Color.FromRgb(0x30, 0xD1, 0x58));
            _start.Content = P.Running ? "Пауза" : P.Remaining < P.Total ? "Продолжить" : "Старт";
            _phases[(int)P.Phase].IsChecked = true;
            int r = Math.Max(1, S.PomoRounds);
            _stats.Text = $"🍅 Сегодня: {PomodoroService.TodayCount}   ·   раунд {P.Round % r + 1} из {r}";
        }
    }

    // =====================================================================
    //  Вода
    // =====================================================================
    public class WaterPage : PageBase
    {
        readonly TextBlock _big = UI.Text("0.0", 34, true);
        readonly TextBlock _goal = UI.Text("/ 2.0 л", 14);
        readonly TextBlock _info = UI.Text("", 12);
        readonly Border _barFill = new() { CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
        readonly Grid _chart = new() { Height = 92 };
        readonly Button _drink;

        public WaterPage()
        {
            _goal.Foreground = _info.Foreground = UI.Dim;
            _goal.VerticalAlignment = VerticalAlignment.Bottom;
            _goal.Margin = new Thickness(6, 0, 0, 7);
            _barFill.Background = new LinearGradientBrush(Color.FromRgb(0x0A, 0x84, 0xFF), Color.FromRgb(0x64, 0xD2, 0xFF), 0);
            var bar = new Grid { Height = 6, Width = 230, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) }
                .Add(new Border { CornerRadius = new CornerRadius(3), Background = UI.Hex("#26FFFFFF") }, _barFill);

            _drink = UI.Pill(UI.IconLabel("", "Выпить"), () => Water.Drink(S.WaterCup), true);
            var buttons = UI.Row(_drink,
                UI.IconBtn("", Water.Undo, 13, 30, "Отменить последний стакан").M(6, 0, 0, 0),
                UI.IconBtn("", () => App.ShowSettings("water"), 13, 30, "Настройки воды"));

            var left = UI.Col(
                UI.Row(_big, _goal),
                _info,
                bar,
                buttons.M(0, 12, 0, 0)).M(14, 0, 0, 0);
            left.Width = 300;

            Content = UI.Grid("a,*").Add(left, _chart.At(0, 1).M(16, 0, 10, 0).VA(VerticalAlignment.Center));
            Water.Changed += () => { if (Visible) Render(); };
        }

        public override void OnShown() => Render();

        void Render()
        {
            int ml = Water.TodayMl;
            _big.Text = Fmt.Liters(ml);
            _goal.Text = $"/ {Fmt.Liters(S.WaterGoal)} л";
            var next = Water.NextReminder;
            _info.Text = $"Сегодня · {Math.Round(Water.Progress * 100)}%" + (next != null && Water.Progress < 1 ? $" · Напомню в {next:HH:mm}" : Water.Progress >= 1 ? " · Цель выполнена 🎉" : "");
            _barFill.Width = 230 * Water.Progress;
            _drink.Content = UI.IconLabel("", $"Выпить {S.WaterCup} мл");

            // график по часам
            _chart.Children.Clear();
            _chart.ColumnDefinitions.Clear();
            _chart.RowDefinitions.Clear();
            _chart.RowDefinitions.Add(new RowDefinition());
            _chart.RowDefinitions.Add(new RowDefinition { Height = new GridLength(14) });
            var hours = Water.Hourly();
            int from = Math.Clamp(S.WaterFrom, 0, 23), to = Math.Clamp(S.WaterTo, from + 1, 24);
            int max = Math.Max(S.WaterCup * 2, hours.Max());
            for (int h = from; h < to; h++)
            {
                _chart.ColumnDefinitions.Add(new ColumnDefinition());
                int col = h - from;
                double frac = hours[h] / (double)max;
                var b = new Border
                {
                    Width = 9, CornerRadius = new CornerRadius(4.5), VerticalAlignment = VerticalAlignment.Bottom,
                    Height = Math.Max(4, frac * 74),
                    Background = hours[h] > 0 ? (h == DateTime.Now.Hour ? UI.Hex("#64D2FF") : UI.Hex("#0A84FF")) : UI.Hex("#22FFFFFF"),
                    ToolTip = $"{h}:00 — {hours[h]} мл",
                };
                _chart.Children.Add(b.At(0, col));
                if ((h - from) % 3 == 0)
                    _chart.Children.Add(UI.Text(h.ToString("00"), 9.5, false, UI.Faint).HA(HorizontalAlignment.Center).At(1, col));
            }
        }
    }

    // =====================================================================
    //  Намаз
    // =====================================================================
    public class PrayerPage : PageBase
    {
        readonly UniformGrid _cards = new() { Columns = 6, Rows = 1 };
        readonly TextBlock _place = UI.Text("", 12);
        readonly TextBlock _next = UI.Text("", 12.5, true);
        readonly DispatcherTimer _t = new() { Interval = TimeSpan.FromSeconds(20) };

        public PrayerPage()
        {
            _place.Foreground = UI.Dim;
            var head = UI.Grid("*,a,a").Add(UI.Col(_next, _place),
                UI.IconBtn("", () => Load(true), 12, 26, "Обновить").At(0, 1),
                UI.IconBtn("", () => App.ShowSettings("prayer"), 12, 26, "Город и метод").At(0, 2));
            Content = UI.Grid(null, "a,*").M(8, 0, 8, 4).Add(head, _cards.At(1, 0).M(0, 10, 0, 0));
            _t.Tick += (_, __) => Render();
        }

        public override void OnShown() { Load(false); _t.Start(); }
        public override void OnHidden() => _t.Stop();

        async void Load(bool force)
        {
            _place.Text = $"{S.PrayerCity}, {S.PrayerCountry} · {PrayerService.MethodName(S.PrayerMethod)}";
            if (PrayerService.Cached == null || force) _next.Text = "Загрузка…";
            try { await PrayerService.Get(force); }
            catch { _next.Text = "Не удалось загрузить — проверь город в настройках"; return; }
            Render();
        }

        void Render()
        {
            var list = PrayerService.Cached;
            if (list == null) return;
            var now = DateTime.Now.TimeOfDay;
            var nextIdx = list.FindIndex(p => p.At > now && p.Name != "Восход");
            _cards.Children.Clear();
            for (int i = 0; i < list.Count; i++)
            {
                var (name, at) = list[i];
                bool next = i == nextIdx, past = at <= now;
                var card = new Border
                {
                    CornerRadius = new CornerRadius(12), Margin = new Thickness(3, 0, 3, 0), Padding = new Thickness(4, 10, 4, 10),
                    Background = next ? UI.Res("AccentBrush") : UI.Res("CardBrush"),
                    Child = UI.Col(
                        UI.Text(name, 12, false, next ? Brushes.White : UI.Dim).HA(HorizontalAlignment.Center),
                        UI.Text(at.ToString(@"hh\:mm"), 17, true, past && !next ? UI.Faint : Brushes.White).HA(HorizontalAlignment.Center)),
                };
                if (next) foreach (var x in ((StackPanel)card.Child).Children.OfType<TextBlock>()) x.SetResourceReference(TextBlock.ForegroundProperty, "AccentTextBrush");
                _cards.Children.Add(card);
            }
            if (nextIdx >= 0)
            {
                var left = list[nextIdx].At - now;
                _next.Text = $"{list[nextIdx].Name} через {(left.Hours > 0 ? left.Hours + " ч " : "")}{left.Minutes} мин";
            }
            else _next.Text = "Все намазы на сегодня прошли";
        }
    }

    // =====================================================================
    //  Полка — временное место для файлов
    // =====================================================================
    public class ShelfPage : PageBase
    {
        readonly WrapPanel _tiles = new() { Orientation = Orientation.Vertical };
        readonly ScrollViewer _scroll;
        readonly Border _empty;
        readonly TextBlock _count = UI.Text("", 11.5);

        public static void Add(IEnumerable<string> paths)
        {
            foreach (var p in paths)
                if (!Store.Data.Shelf.Contains(p, StringComparer.OrdinalIgnoreCase)) Store.Data.Shelf.Add(p);
            Store.SaveData();
        }

        public ShelfPage()
        {
            _count.Foreground = UI.Dim;
            _scroll = new ScrollViewer
            {
                Content = _tiles,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            };
            _scroll.PreviewMouseWheel += (_, e) =>
            {
                _scroll.ScrollToHorizontalOffset(_scroll.HorizontalOffset - e.Delta / 2.0);
                e.Handled = _scroll.ScrollableWidth > 0;
            };
            _empty = new Border
            {
                CornerRadius = new CornerRadius(14),
                BorderBrush = UI.Hex("#40FFFFFF"),
                BorderThickness = new Thickness(1.5),
                Margin = new Thickness(10, 4, 10, 6),
                Child = UI.Col(
                    UI.Icon("", 24, UI.Dim),
                    UI.Text("Перетащи сюда файлы", 14, true).HA(HorizontalAlignment.Center).M(0, 8, 0, 2),
                    UI.Text("Они подождут на полке — потом вытащи их в любое окно", 12, false, UI.Dim).HA(HorizontalAlignment.Center)),
            };
            var dash = new Rectangle { StrokeDashArray = new DoubleCollection { 4, 3 }, Stroke = UI.Hex("#50FFFFFF"), StrokeThickness = 1.5, RadiusX = 14, RadiusY = 14 };
            _empty.BorderThickness = new Thickness(0);
            var emptyGrid = new Grid().Add(dash.M(10, 4, 10, 6), _empty);

            var allBtn = UI.Pill(UI.IconLabel("", "Перетащить все"), null, false, "Потяни эту кнопку в окно — уйдут все файлы");
            allBtn.PreviewMouseMove += (_, e) =>
            {
                if (e.LeftButton == MouseButtonState.Pressed && Store.Data.Shelf.Count > 0) DragOut(allBtn, Store.Data.Shelf.ToArray());
            };
            var foot = UI.Grid("*,a,a").Add(_count, allBtn.At(0, 1),
                UI.Pill(UI.IconLabel("", "Очистить"), () => { Store.Data.Shelf.Clear(); Store.SaveData(); }).At(0, 2).M(6, 0, 0, 0));
            foreach (Button b in foot.Children.OfType<Button>()) { b.Padding = new Thickness(10, 4, 10, 4); b.FontSize = 11; }

            var main = UI.Grid(null, "*,a").M(6, 0, 6, 2).Add(_scroll, foot.At(1, 0).M(4, 4, 4, 0));
            Content = new Grid().Add(emptyGrid, main);
            _emptyGrid = emptyGrid;
            _main = main;
            Store.Data.Shelf.CollectionChanged += (_, __) => { if (Visible) Render(); };
        }

        readonly Grid _emptyGrid, _main;

        public override void OnShown()
        {
            // файлы могли удалить/переместить
            foreach (var gone in Store.Data.Shelf.Where(p => !File.Exists(p) && !Directory.Exists(p)).ToList())
                Store.Data.Shelf.Remove(gone);
            Render();
        }

        public override void OnHidden() => _tiles.Children.Clear();

        void Render()
        {
            var items = Store.Data.Shelf.ToList();
            _emptyGrid.Show(items.Count == 0);
            _main.Show(items.Count > 0);
            _count.Text = items.Count + " " + Plural(items.Count, "файл", "файла", "файлов");
            _tiles.Children.Clear();
            foreach (var path in items)
            {
                var p = path;
                var icon = new Image { Source = FileIcons.Get(p), Width = 36, Height = 36 };
                var name = new TextBlock
                {
                    Text = Paths.ShortName(p), FontSize = 11, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis,
                    TextAlignment = TextAlignment.Center, MaxHeight = 30, Width = 76, Margin = new Thickness(0, 4, 0, 0),
                };
                var x = UI.IconBtn("", () => { Store.Data.Shelf.Remove(p); Store.SaveData(); }, 8, 18, "Убрать с полки");
                x.HorizontalAlignment = HorizontalAlignment.Right;
                x.VerticalAlignment = VerticalAlignment.Top;
                x.Background = UI.Hex("#60000000");
                x.Visibility = Visibility.Hidden;
                var tile = new Border
                {
                    Width = 86, Height = 86, CornerRadius = new CornerRadius(12), Margin = new Thickness(3), Background = Brushes.Transparent,
                    Cursor = Cursors.Hand, ToolTip = p,
                    Child = new Grid().Add(UI.Col(icon, name).VA(VerticalAlignment.Center), x),
                };
                tile.MouseEnter += (_, __) => { tile.Background = UI.Res("CardBrush"); x.Visibility = Visibility.Visible; };
                tile.MouseLeave += (_, __) => { tile.Background = Brushes.Transparent; x.Visibility = Visibility.Hidden; };
                Point down = default;
                tile.PreviewMouseLeftButtonDown += (_, e) =>
                {
                    down = e.GetPosition(tile);
                    if (e.ClickCount == 2) { SystemService.Run(p); e.Handled = true; }
                };
                tile.PreviewMouseMove += (_, e) =>
                {
                    if (e.LeftButton != MouseButtonState.Pressed) return;
                    var d = e.GetPosition(tile) - down;
                    if (Math.Abs(d.X) > 6 || Math.Abs(d.Y) > 6) DragOut(tile, new[] { p });
                };
                _tiles.Children.Add(tile);
            }
        }

        static void DragOut(DependencyObject src, string[] files)
        {
            var island = App.Island;
            island.Hold = true;
            try
            {
                var data = new DataObject(DataFormats.FileDrop, files);
                DragDrop.DoDragDrop(src, data, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
            }
            catch (Exception e) { Log.Error(e); }
            finally { island.Hold = false; island.Nudge(); }
        }

        static string Plural(int n, string one, string few, string many)
        {
            int m10 = n % 10, m100 = n % 100;
            if (m10 == 1 && m100 != 11) return one;
            if (m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14)) return few;
            return many;
        }
    }

    // =====================================================================
    //  Пульт — громкость, яркость, быстрые действия, нагрузка
    // =====================================================================
    public class QuickPage : PageBase
    {
        readonly Slider _vol = UI.Slider();
        readonly Slider _bri = UI.Slider(100);
        readonly TextBlock _volText = UI.Text("", 11.5);
        readonly TextBlock _briText = UI.Text("", 11.5);
        readonly Button _volIcon;
        readonly Grid _briRow;
        readonly TextBlock _stats = UI.Text("", 11.5);
        readonly DispatcherTimer _t = new() { Interval = TimeSpan.FromSeconds(2) };
        readonly DispatcherTimer _briApply = new() { Interval = TimeSpan.FromMilliseconds(90) };
        Border _awakeTile, _micTile;
        bool _setting;

        public QuickPage()
        {
            _volText.Foreground = _briText.Foreground = _stats.Foreground = UI.Dim;
            _volText.Width = _briText.Width = 36;
            _volText.TextAlignment = _briText.TextAlignment = TextAlignment.Right;
            _volIcon = UI.IconBtn("", () => AudioService.I.ToggleMute(), 14, 28, "Без звука");
            var volRow = UI.Grid("a,*,a").Add(_volIcon, _vol.At(0, 1).M(6, 0, 6, 0), _volText.At(0, 2));
            _briRow = UI.Grid("a,*,a").Add(UI.IconBtn("", null, 14, 28), _bri.At(0, 1).M(6, 0, 6, 0), _briText.At(0, 2));
            var sliders = UI.Col(volRow, _briRow.M(0, 2, 0, 0));
            sliders.Width = 250;

            _vol.ValueChanged += (_, __) => { if (!_setting) AudioService.I.SetVolume(_vol.Value); _volText.Text = Math.Round(_vol.Value * 100) + "%"; };
            _bri.ValueChanged += (_, __) =>
            {
                _briText.Text = Math.Round(_bri.Value) + "%";
                if (!_setting) { _briApply.Stop(); _briApply.Start(); }
            };
            _briApply.Tick += (_, __) => { _briApply.Stop(); SystemService.I.SetBrightness((int)Math.Round(_bri.Value)); };
            AudioService.I.Changed += (_, __, ___) => { if (Visible) Sync(); };

            var tiles = new UniformGrid { Columns = 4, Rows = 2, Margin = new Thickness(18, 0, 0, 0) };
            _awakeTile = Tile("", "Не спать", () => { SystemService.I.KeepAwake = !SystemService.I.KeepAwake; Sync(); });
            _micTile = Tile("", "Микрофон", () => { AudioService.I.ToggleMicMute(); Sync(); });
            tiles.Children.Add(_awakeTile);
            tiles.Children.Add(_micTile);
            tiles.Children.Add(Tile("", "Скриншот", () => { App.Island.Collapse(true); SystemService.Run("ms-screenclip:"); }));
            tiles.Children.Add(Tile("", "Тема", () => SystemService.I.ToggleTheme()));
            tiles.Children.Add(Tile("", "Диспетчер", () => SystemService.Run("taskmgr")));
            tiles.Children.Add(Tile("", "Блокировка", () => Native.LockWorkStation()));
            tiles.Children.Add(Tile("", "Пипетка", () => { App.Island.Collapse(true); ColorPicker.I.Start(); }));
            tiles.Children.Add(Tile("", "Скрыть", () => App.Island.HideIsland()));

            var top = UI.Grid("a,*").Add(sliders.VA(VerticalAlignment.Center), tiles.At(0, 1));
            Content = UI.Grid(null, "*,a").M(10, 0, 8, 2).Add(top, _stats.At(1, 0).HA(HorizontalAlignment.Center).M(0, 6, 0, 0));
            _t.Tick += (_, __) => Stats();
        }

        Border Tile(string glyph, string label, Action click)
        {
            var b = new Border
            {
                CornerRadius = new CornerRadius(12), Margin = new Thickness(3), Background = UI.Res("CardBrush"), Cursor = Cursors.Hand,
                Child = UI.Col(UI.Icon(glyph, 15), UI.Text(label, 10.5).HA(HorizontalAlignment.Center).M(0, 4, 0, 0)).VA(VerticalAlignment.Center),
                Padding = new Thickness(2, 6, 2, 6),
            };
            b.MouseEnter += (_, __) => b.Opacity = 0.85;
            b.MouseLeave += (_, __) => b.Opacity = 1;
            b.MouseLeftButtonUp += (_, e) => { click(); e.Handled = true; };
            return b;
        }

        public override void OnShown()
        {
            Sync();
            var bri = SystemService.I.GetBrightness();
            _briRow.Show(bri != null);
            if (bri != null) { _setting = true; _bri.Value = bri.Value; _setting = false; _briText.Text = bri + "%"; }
            SystemService.I.CpuPercent();
            Stats();
            _t.Start();
        }

        public override void OnHidden() => _t.Stop();

        void Sync()
        {
            var a = AudioService.I;
            _setting = true;
            _vol.Value = a.Muted ? 0 : a.Volume;
            _setting = false;
            _volText.Text = a.Muted ? "выкл" : Math.Round(a.Volume * 100) + "%";
            _volIcon.Content = a.Muted || a.Volume < 0.01 ? "" : "";
            _awakeTile.Background = SystemService.I.KeepAwake ? UI.Res("AccentBrush") : UI.Res("CardBrush");
            _awakeTile.OnAccent(SystemService.I.KeepAwake);
            bool micOff = a.MicMuted;
            _micTile.Background = micOff ? UI.Hex("#FF453A") : UI.Res("CardBrush");
            ((TextBlock)((StackPanel)_micTile.Child).Children[1]).Text = micOff ? "Микр. выкл" : "Микрофон";
        }

        void Stats()
        {
            var s = SystemService.I;
            long mb;
            using (var p = Process.GetCurrentProcess()) mb = p.WorkingSet64 / (1024 * 1024);
            NetSpeed.Sample();
            _stats.Text = $"ЦП {s.CpuPercent()}%  ·  ОЗУ {s.RamPercent()}%" +
                          (s.HasBattery ? $"  ·  Батарея {s.BatteryPercent}%{(s.Charging ? " ⚡" : s.BatteryLeft is TimeSpan left ? $" (~{(int)left.TotalHours} ч {left.Minutes} мин)" : "")}" : "") +
                          $"  ·  ↓{NetSpeed.Fmt(NetSpeed.Down)} ↑{NetSpeed.Fmt(NetSpeed.Up)}" +
                          $"  ·  N-Island {mb} МБ";
        }
    }
}
