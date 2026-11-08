using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using NIsland.Core;
using NIsland.Pages;
using NIsland.Services;
using WinForms = System.Windows.Forms;

namespace NIsland
{
    /// <summary>Настройки в стиле macOS: слева разделы с цветными иконками, справа карточки.</summary>
    public partial class SettingsWindow : Window
    {
        static Settings S => Store.Settings;

        record Section(string Key, string Title, string Glyph, string Color, Func<UIElement> Build, string Font = null);

        readonly List<Section> _sections;
        readonly StackPanel _nav = new();
        readonly ContentControl _body = new();
        readonly ScrollViewer _scroll;
        readonly Dictionary<string, Border> _navItems = new();
        string _current;
        readonly GradientStop _bgTop = new(Color.FromRgb(0x26, 0x26, 0x2B), 0), _bgBottom = new(Color.FromRgb(0x18, 0x18, 0x1B), 1);
        LinearGradientBrush _rootBg;

        void ApplyOpacity()
        {
            _rootBg ??= new LinearGradientBrush(new GradientStopCollection { _bgTop, _bgBottom }, 90);
            byte a = (byte)(255 * Math.Clamp(S.SettingsOpacity / 100.0, 0.5, 1));
            bool amoled = S.WindowTheme == "amoled";
            _bgTop.Color = amoled ? Color.FromArgb(a, 0, 0, 0) : Color.FromArgb(a, 0x26, 0x26, 0x2B);
            _bgBottom.Color = amoled ? Color.FromArgb(a, 0, 0, 0) : Color.FromArgb(a, 0x18, 0x18, 0x1B);
        }

        public SettingsWindow(bool center = false)
        {
            _center = center;
            ApplyOpacity();
            System.ComponentModel.PropertyChangedEventHandler h = (_, e) =>
            {
                if (e.PropertyName is nameof(Settings.SettingsOpacity) or nameof(Settings.WindowTheme)) ApplyOpacity();
            };
            S.PropertyChanged += h;
            Closed += (_, __) => S.PropertyChanged -= h;
            // поле ввода фиксирует значение при потере фокуса — уводим фокус перед закрытием, чтобы ничего не потерялось
            Closing += (_, __) => { if (Keyboard.FocusedElement is TextBox tb) tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource(); };
            Title = center ? "N-Island — центр управления ноутбуком" : "N-Island — настройки";
            Width = center ? 940 : 800;
            Height = center ? 680 : 600;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.CanMinimize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Foreground = Brushes.White;
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
            UseLayoutRounding = true;
            try { Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/Assets/island.ico")); } catch { }

            _sections = center ? CenterSections() : new List<Section>
            {
                new("general", "Основные", "", "#8E8E93", General),
                new("ai", "ИИ", "", "#BF5AF2", AiSec),
                new("agents", "ИИ-агенты", "", "#D97757", AgentsSec),
                new("mcp", "MCP для агентов", "", "#7D5BED", McpSec),
                new("modes", "Режимы и стиль", "", "#5E5CE6", ModesSec),
                new("appearance", "Внешний вид", "", "#636366", Appearance),
                new("custom", "Кастомизация", "", "#FF375F", CustomSec),
                new("opacity", "Прозрачность", "", "#64D2FF", OpacitySec),
                new("maint", "Обслуживание", "", "#30D158", MaintSec),
                new("ring", "Кольцо статуса", "◎", "#30D158", Ring, "Segoe UI Symbol"),
                new("display", "Экран", "", "#0A84FF", Display),
                new("music", "Музыка и тексты", "", "#FF375F", Music),
                new("eq", "Эквалайзер", "\uE9E9", "#FF9F0A", EqSec),
                new("visual", "Визуализатор", "\uE9D9", "#BF5AF2", VizSec),
                new("calls", "Звонки и уведомления", "", "#30D158", Calls),
                new("clip", "Буфер и заметки", "", "#5E5CE6", ClipSec),
                new("weather", "Погода и часы", "", "#0A84FF", WeatherSec),
                new("timer", "Таймер и здоровье", "", "#FF9F0A", TimerSec),
                new("launcher", "Запуск", "", "#FF375F", LauncherSec),
                new("todo", "Задачи", "", "#34C759", Todo),
                new("calendar", "Календарь", "", "#FF453A", Calendar),
                new("pomodoro", "Помодоро", "", "#FF6B35", Pomodoro),
                new("water", "Вода", "💧", "#0A84FF", WaterSec, "Segoe UI Emoji"),
                new("prayer", "Намаз", "", "#2EC4B6", Prayer),
                new("shelf", "Полка", "", "#FFB800", Shelf),
                new("sounds", "Звуки", "", "#FF9F0A", SoundsSec),
                new("hub", "Интеграции", "", "#32ADE6", HubSec),
                new("vpn", "VPN", "", "#30B0C7", VpnSec),
                new("spotlight", "Быстрый поиск", "", "#AF52DE", SpotlightSec),
                new("dedupe", "Без дублей", "", "#FF9500", DedupeSec),
                new("windows", "Windows", "\uE770", "#0078D4", WindowsSec),
                new("language", "Язык", "", "#0A84FF", LanguageSec),
                new("hud", "HUD и клавиатура","", "#64D2FF", Hud),
            };

            // каркас
            var root = new Border
            {
                CornerRadius = new CornerRadius(14),
                Background = _rootBg,
                BorderBrush = UI.Hex("#30FFFFFF"),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(14),
            };
            var grid = UI.Grid("216,*");

            var side = new Border { Background = UI.Hex("#12FFFFFF"), CornerRadius = new CornerRadius(14, 0, 0, 14) };
            var sideDock = new DockPanel { Margin = new Thickness(10, 14, 10, 12) };
            var lights = UI.Row(Light("#FF5F57", Close, "Закрыть"), Light("#FEBC2E", () => WindowState = WindowState.Minimized, "Свернуть").M(2, 0, 0, 0));
            lights.Margin = new Thickness(4, 0, 0, 12);
            DockPanel.SetDock(lights, Dock.Top);
            var about = center ? NavItem(new Section("about", "Как пользоваться", "", "#8E8E93", CenterHelp))
                               : NavItem(new Section("about", "О программе","", "#8E8E93", About));
            DockPanel.SetDock(about, Dock.Bottom);
            sideDock.Children.Add(lights);
            var search = SearchBox();
            DockPanel.SetDock(search, Dock.Top);
            sideDock.Children.Add(search);
            sideDock.Children.Add(about);
            sideDock.Children.Add(new ScrollViewer { Content = _nav, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            foreach (var s in _sections) _nav.Children.Add(NavItem(s));
            side.Child = sideDock;
            side.MouseLeftButtonDown += Drag;

            _scroll = new ScrollViewer { Content = _body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 12, 6, 12) };
            var right = new Grid { Background = Brushes.Transparent }.Add(_scroll);
            right.MouseLeftButtonDown += Drag;
            grid.Add(side, right.At(0, 1));
            root.Child = grid;
            // Тень — отдельным неподвижным слоем ПОД содержимым, а не эффектом на нём: окно прозрачное (layered) и рисуется
            // программно, и эффект на корне заставлял пересчитывать размытие всего окна на любую мелочь внутри —
            // это давало 25–35 % ядра, пока окно открыто и играет музыка.
            var shadow = new Border
            {
                CornerRadius = new CornerRadius(14), Margin = new Thickness(14), Background = Brushes.Black, IsHitTestVisible = false,
                Effect = S.LiteMode ? null : new DropShadowEffect { BlurRadius = 26, ShadowDepth = 6, Opacity = 0.45, RenderingBias = RenderingBias.Performance },
                CacheMode = new BitmapCache(),
            };
            // тень видна только снаружи окна: иначе чёрная подложка просвечивала бы сквозь полупрозрачный фон
            shadow.SizeChanged += (_, e) =>
            {
                var inner = new RectangleGeometry(new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), 14, 14);
                var outer = new RectangleGeometry(new Rect(-60, -60, e.NewSize.Width + 120, e.NewSize.Height + 120));
                var clip = new CombinedGeometry(GeometryCombineMode.Exclude, outer, inner);
                clip.Freeze();
                shadow.Clip = clip;
            };
            var frame = new Grid();
            frame.Children.Add(shadow);
            frame.Children.Add(root);
            Content = frame;

            KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
            // открываемся там, где закрыли в прошлый раз
            var last = center ? S.LastCenterSection : S.LastSettingsSection;
            Open(!string.IsNullOrEmpty(last) && _sections.Any(x => x.Key == last) ? last : center ? "overview" : "appearance");
            Closed += (_, __) => _live?.Stop();
        }

        void Drag(object s, MouseButtonEventArgs e)
        {
            if (e.Handled || e.ButtonState != MouseButtonState.Pressed || IsInteractive(e.OriginalSource as DependencyObject)) return;
            try { DragMove(); } catch { }
        }

        /// <summary>Нажали на кнопку, поле, переключатель или карточку — окно не тащим, чтобы клик дошёл.</summary>
        static bool IsInteractive(DependencyObject d)
        {
            while (d != null)
            {
                if (d is System.Windows.Controls.Primitives.ButtonBase or System.Windows.Controls.Primitives.TextBoxBase
                    or System.Windows.Controls.Primitives.Thumb or System.Windows.Controls.Primitives.ScrollBar) return true;
                if (d is FrameworkElement fe && fe.Cursor == Cursors.Hand) return true;
                d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            }
            return false;
        }

        /// <summary>«Светофор» как в macOS. Клик ловим на нажатии и гасим событие —
        /// иначе его перехватывает перетаскивание окна (DragMove), и кнопка не срабатывает.</summary>
        static FrameworkElement Light(string color, Action click, string tip)
        {
            var dot = new Ellipse { Width = 13, Height = 13, Fill = UI.Hex(color) };
            var hit = new Grid { Width = 22, Height = 22, Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = tip };
            hit.Children.Add(dot);
            hit.MouseEnter += (_, __) => dot.Opacity = 0.75;
            hit.MouseLeave += (_, __) => dot.Opacity = 1;
            hit.PreviewMouseLeftButtonDown += (_, a) => { a.Handled = true; click(); };
            return hit;
        }

        Border NavItem(Section s)
        {
            var baseColor = UI.Hex(s.Color).Color;
            var light = Color.FromRgb((byte)Math.Min(255, baseColor.R + 40), (byte)Math.Min(255, baseColor.G + 40), (byte)Math.Min(255, baseColor.B + 40));
            var icon = new Border
            {
                Width = 24, Height = 24, CornerRadius = new CornerRadius(7), Background = new LinearGradientBrush(light, baseColor, 90),
                Child = new TextBlock
                {
                    Text = s.Glyph, FontFamily = new FontFamily(s.Font ?? "Segoe Fluent Icons, Segoe MDL2 Assets"),
                    FontSize = s.Font == "Segoe UI Emoji" ? 12 : 13, Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                },
            };
            var b = new Border
            {
                CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 1, 0, 1),
                Background = Brushes.Transparent, Cursor = Cursors.Hand,
                Child = UI.Row(icon, UI.Text(s.Title, 13).M(10, 0, 0, 0)),
            };
            b.MouseEnter += (_, __) => { if (_current != s.Key) b.Background = UI.Hex("#14FFFFFF"); };
            b.MouseLeave += (_, __) => { if (_current != s.Key) b.Background = Brushes.Transparent; };
            b.MouseLeftButtonUp += (_, e) => { Open(s.Key); e.Handled = true; };
            b.Tag = s;
            _navItems[s.Key] = b;
            return b;
        }

        public string CurrentSection => _current;

        public void Open(string key)
        {
            var s = _sections.FirstOrDefault(x => x.Key == key) ?? (key == "about" ? (Section)_navItems["about"].Tag : _sections[0]);
            _current = s.Key;
            if (s.Key != "about") { if (_center) S.LastCenterSection = s.Key; else S.LastSettingsSection = s.Key; }
            var accent = (UI.Res("AccentBrush") as SolidColorBrush)?.Color ?? Colors.DodgerBlue;
            foreach (var (k, b) in _navItems)
                b.Background = k == s.Key ? new SolidColorBrush(ColorUtil.WithAlpha(accent, 0.28)) : Brushes.Transparent;
            var page = new StackPanel { Margin = new Thickness(24, 6, 22, 24) };
            page.Children.Add(UI.Text(s.Title, 24, true).M(0, 0, 0, 14));
            page.Children.Add(s.Build());
            _body.Content = page;
            _scroll.ScrollToTop();
            RevealMatch();
            // раздел мягко выплывает
            var tt = new TranslateTransform(0, 12);
            page.RenderTransform = tt;
            var ease = new QuinticEase { EasingMode = EasingMode.EaseOut };
            tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(420)) { EasingFunction = ease });
            page.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)) { EasingFunction = new SineEase() });
        }

        // ------------------------------------------------------------------
        //  Помощники: карточки, строки, контролы с привязкой к настройкам
        // ------------------------------------------------------------------

        static StackPanel Panel(params UIElement[] items) => UI.Col(items);

        static UIElement Group(string label, params UIElement[] rows)
        {
            rows = rows.Where(r => r != null).ToArray();   // необязательные строки (зависят от состояния) передаются как null
            var list = new StackPanel();
            for (int i = 0; i < rows.Length; i++)
            {
                if (i > 0) list.Children.Add(new Border { Height = 1, Background = UI.Res("LineBrush"), Margin = new Thickness(14, 0, 14, 0) });
                list.Children.Add(rows[i]);
            }
            var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
            if (label != null) sp.Children.Add(UI.Text(label, 12, true, UI.Dim).M(4, 0, 0, 6));
            sp.Children.Add(new Border
            {
                Background = UI.Hex("#14FFFFFF"), CornerRadius = new CornerRadius(12), Child = list,
                BorderBrush = new LinearGradientBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x08, 0xFF, 0xFF, 0xFF), 90),
                BorderThickness = new Thickness(1),
            });
            return sp;
        }

        static UIElement Row(string title, string desc, UIElement control)
        {
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = title, FontSize = 13.5, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            if (!string.IsNullOrEmpty(desc))
                text.Children.Add(new TextBlock { Text = desc, FontSize = 12, Foreground = UI.Dim, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
            var g = UI.Grid("*,a").M(14, 10, 14, 10).Add(text);
            if (control is FrameworkElement fe)
            {
                fe.VerticalAlignment = VerticalAlignment.Center;
                fe.Margin = new Thickness(16, 0, 0, 0);
                g.Add(fe.At(0, 1));
            }
            return g;
        }

        static TextBlock Note(string text) => new TextBlock
        {
            Text = text, FontSize = 12, Foreground = UI.Dim, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, -8, 4, 16),
        };

        static PropertyInfo P(string name) => typeof(Settings).GetProperty(name);

        static CheckBox Toggle(string prop)
        {
            var c = new CheckBox { Style = UI.St("Switch") };
            c.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, new Binding(prop) { Source = S, Mode = BindingMode.TwoWay });
            return c;
        }

        static UIElement Seg(string prop, params (object Value, string Label)[] opts)
        {
            var p = P(prop);
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            string group = prop + Guid.NewGuid().ToString("N");
            foreach (var (val, label) in opts)
            {
                var v = val;
                var rb = new RadioButton { Style = UI.St("Segment"), Content = label, GroupName = group, IsChecked = Equals(p.GetValue(S), v), Tag = v };
                rb.Checked += (_, __) => { if (!Equals(p.GetValue(S), v)) p.SetValue(S, v); };
                sp.Children.Add(rb);
            }
            // поменяли из острова или трея — переключатель тоже переедет
            Follow(sp, prop, () => { foreach (RadioButton rb in sp.Children) rb.IsChecked = Equals(p.GetValue(S), rb.Tag); });
            return new Border { Background = UI.Hex("#22FFFFFF"), CornerRadius = new CornerRadius(8), Padding = new Thickness(2), Child = sp };
        }

        static UIElement Stepper(string prop, int min, int max, int step, Func<int, string> fmt, Action after = null)
        {
            var p = P(prop);
            var label = UI.Text("", 13, true);
            label.MinWidth = 64;
            label.TextAlignment = TextAlignment.Center;
            void Upd() => label.Text = fmt((int)p.GetValue(S));
            void Change(int d)
            {
                int v = Math.Clamp((int)p.GetValue(S) + d, min, max);
                p.SetValue(S, v);
                Upd();
                after?.Invoke();
            }
            Upd();
            var minus = UI.IconBtn("", () => Change(-step), 10, 24);
            var plus = UI.IconBtn("", () => Change(step), 10, 24);
            minus.Background = plus.Background = UI.Hex("#2AFFFFFF");
            minus.Template = plus.Template = RoundTemplate();
            Follow(label, prop, Upd);   // поменяли из острова — значение в окне тоже обновится
            // колесо мыши над значением тоже меняет его — быстрее, чем кликать
            label.Background = Brushes.Transparent;
            label.MouseWheel += (_, e) => { Change(e.Delta > 0 ? step : -step); e.Handled = true; };
            label.ToolTip = I18n.T("Колесо мыши — изменить");
            return UI.Row(minus, label, plus);
        }

        static ControlTemplate RoundTemplate()
        {
            var t = new ControlTemplate(typeof(Button));
            var bd = new FrameworkElementFactory(typeof(Border));
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(12));
            bd.SetValue(Border.BackgroundProperty, UI.Hex("#2AFFFFFF"));
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            bd.AppendChild(cp);
            t.VisualTree = bd;
            return t;
        }

        static TextBox Input(string prop, double width = 180, Action after = null)
        {
            var t = UI.Box("", width);
            t.SetBinding(TextBox.TextProperty, new Binding(prop) { Source = S, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus });
            t.KeyDown += (_, e) => { if (e.Key == Key.Enter) { t.GetBindingExpression(TextBox.TextProperty)?.UpdateSource(); after?.Invoke(); } };
            t.LostFocus += (_, __) => after?.Invoke();
            return t;
        }

        /// <summary>Многострочное поле, привязанное к настройке.</summary>
        static TextBox InputMulti(string prop, double height = 90, string hint = "")
        {
            var t = new TextBox
            {
                AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = height, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Foreground = Brushes.White, CaretBrush = Brushes.White, FontSize = 12.5, Template = Ago.PlainBox(), Tag = hint,
            };
            t.SetBinding(TextBox.TextProperty, new Binding(prop) { Source = S, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus });
            return t;
        }

        static TextBox Field(string value, double width = 220, Action<string> commit = null, string hint = "")
        {
            var t = UI.Box(hint, width);
            t.Text = value ?? "";
            if (commit != null)
            {
                t.LostFocus += (_, __) => commit(t.Text);
                t.KeyDown += (_, e) => { if (e.Key == Key.Enter) commit(t.Text); };
            }
            return t;
        }

        static UIElement Wide(string title, string desc, UIElement control)
        {
            var sp = new StackPanel { Margin = new Thickness(14, 10, 14, 10) };
            sp.Children.Add(new TextBlock { Text = title, FontSize = 13.5, FontWeight = FontWeights.SemiBold });
            if (!string.IsNullOrEmpty(desc)) sp.Children.Add(new TextBlock { Text = desc, FontSize = 12, Foreground = UI.Dim, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 6) });
            if (control is FrameworkElement fe) fe.Margin = new Thickness(0, 4, 0, 0);
            sp.Children.Add(control);
            return sp;
        }

        // ------------------------------------------------------------------
        //  Разделы
        // ------------------------------------------------------------------

        UIElement General() => Panel(
            Group(null,
                Row("Запуск вместе с Windows", "Чёлка появляется сразу после входа в систему", Toggle(nameof(Settings.StartWithWindows))),
                Row("Состояние автозагрузки", AutoStart.Status(), UI.Pill("Проверить", () => { AutoStart.Apply(S.StartWithWindows); Open("general"); }, false,
                    "Перезаписать запись автозагрузки и включить её, если Windows её отключила")),
                Row("Горячие клавиши", "Ctrl+Alt+N — чёлка, A — ИИ, L — центр управления, H — скрыть, D — не беспокоить, C — пипетка, V — буфер, T — перевод", Toggle(nameof(Settings.Hotkey))),
                Row("Центр управления ноутбуком", "Глубокая очистка, защита от слежки, питание, сеть, программы и исправления — с объяснениями для новичков",
                    UI.Pill("Открыть", () => App.ShowCenter(), true)),
                Row("Звуки", "Короткий сигнал для напоминаний и таймера", Toggle(nameof(Settings.Sounds))),
                Row("Экономия памяти", "Чистить память, пока чёлка свёрнута. Обычно 25–60 МБ вместо 120+", Toggle(nameof(Settings.LowMemory))),
                Row("Лёгкий режим для слабых ПК", "Без стеклянного размытия, эквалайзера и теней — меньше нагрузки на процессор и видеокарту. " +
                    "Включается сам на ноутбуках с 4 ГБ памяти или без видеоускорения", Toggle(nameof(Settings.LiteMode)))),
            Group("Страницы чёлки",
                Row("Музыка", "Плеер для любого приложения и сайта", Toggle(nameof(Settings.PgMedia))),
                Row("ИИ", "Чат с нейросетью прямо в чёлке", Toggle(nameof(Settings.PgAi))),
                Row("Звук", "Громкость, устройство вывода и громкость каждого приложения", Toggle(nameof(Settings.PgSound))),
                Row("Уведомления", "Центр уведомлений — как шторка на iPhone", Toggle(nameof(Settings.PgCenter))),
                Row("Обслуживание", "Очистка, память, автозагрузка, батарея, диски", Toggle(nameof(Settings.PgMaint))),
                Row("Инструменты", "Скорость интернета, IP, QR, Википедия, крипта и др.", Toggle(nameof(Settings.PgTools))),
                Row("Режимы и стиль", "Быстрое переключение режимов и пресетов", Toggle(nameof(Settings.PgModes))),
                Row("Таймер", "Таймер, секундомер, сон для музыки", Toggle(nameof(Settings.PgTimer))),
                Row("Погода", "Погода на 5 дней и мировые часы", Toggle(nameof(Settings.PgWeather))),
                Row("Буфер обмена", "История скопированного текста", Toggle(nameof(Settings.PgClipboard))),
                Row("Заметки", "Быстрый блокнот, сохраняется сам", Toggle(nameof(Settings.PgNotes))),
                Row("Перевод", "Переводчик на 8 языков, язык оригинала определяется сам", Toggle(nameof(Settings.PgTranslate))),
                Row("Запуск", "Избранные программы, поиск, калькулятор, пипетка", Toggle(nameof(Settings.PgLauncher))),
                Row("Календарь", "Месяц и свои события с напоминаниями", Toggle(nameof(Settings.PgCalendar))),
                Row("Задачи", "Быстрый список дел", Toggle(nameof(Settings.PgTodo))),
                Row("Помодоро", "Таймер фокуса и перерывов", Toggle(nameof(Settings.PgPomodoro))),
                Row("Вода", "Трекер воды с напоминаниями", Toggle(nameof(Settings.PgWater))),
                Row("Намаз", "Время молитв по городу", Toggle(nameof(Settings.PgPrayer))),
                Row("Полка", "Временное место для файлов", Toggle(nameof(Settings.PgShelf))),
                Row("Пульт", "Громкость, яркость, быстрые действия", Toggle(nameof(Settings.PgQuick))),
                Row("Эквалайзер", "Кривая с живым спектром и пресеты для всего звука Windows", Toggle(nameof(Settings.PgEq))),
                Row("Wi-Fi", "Сигнал вживую, качество связи, сети рядом и устройства в сети", Toggle(nameof(Settings.PgWifi))),
                Row("VPN", "Включать и выключать установленные VPN, IP и страна", Toggle(nameof(Settings.PgVpn))),
                Row("Сервисы", "Home Assistant, Todoist, календарь, новости, курсы, посылки и другие интеграции", Toggle(nameof(Settings.PgHub)))),
            Group("Запоминать",
                Row("Последнюю страницу чёлки", "Чёлка открывается там, где ты был в прошлый раз — даже после перезапуска", Toggle(nameof(Settings.RememberPage))),
                Row("Закрепление чёлки", "Закрепил открытой — останется закреплённой после перезапуска", Toggle(nameof(Settings.RememberPin)))),
            BackupGroup());

        UIElement Ring() => Panel(
            Group(null,
                Row("Что показывает кольцо", "Слева в свёрнутой чёлке и в шапке",
                    Seg(nameof(Settings.RingSource), ("auto", "Авто"), ("battery", "Батарея"), ("water", "Вода"), ("pomodoro", "Таймер"), ("media", "Трек"), ("none", "Нет"))),
                Row("Кольцо в шапке", "Показывать рядом с датой в раскрытой чёлке", Toggle(nameof(Settings.RingInHeader))),
                Row("Заряд батареи", "Процент справа в чёлке", Toggle(nameof(Settings.ShowBattery)))),
            Note("«Авто»: идёт помодоро — красное кольцо таймера; иначе — прогресс воды за день; если вода выключена — батарея."));

        UIElement Display()
        {
            int n = WinForms.Screen.AllScreens.Length;
            return Panel(
                Group(null,
                    Row("Монитор", n > 1 ? $"Подключено мониторов: {n}" : "Основной монитор",
                        Stepper(nameof(Settings.Monitor), 0, Math.Max(0, n), 1, v => v == 0 ? "Основной" : "№ " + v)),
                    Row("Сдвиг по горизонтали", "Если чёлка мешает чему-то в центре", Stepper(nameof(Settings.OffsetX), -600, 600, 20, v => v + " px")),
                    Row("Прятать в полноэкранном режиме", "Игры, видео на весь экран, презентации", Toggle(nameof(Settings.HideInFullscreen)))),
                Group("Размер",
                    Row("Ширина свёрнутой чёлки", null, Stepper(nameof(Settings.NotchWidth), 140, 320, 10, v => v + " px")),
                    Row("Ширина раскрытой чёлки", null, Stepper(nameof(Settings.ExpandedWidth), 560, 860, 20, v => v + " px"))));
        }

        UIElement Appearance()
        {
            var cards = new UniformGrid2(2);
            cards.Add(StyleCard("classic", "Классика", "Сливается с верхним краем"));
            cards.Add(StyleCard("floating", "Парящая", "Висит чуть ниже края"));

            var colors = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var c in new[] { "#0A84FF", "#5E5CE6", "#BF5AF2", "#FF375F", "#FF9F0A", "#FFD60A", "#30D158", "#64D2FF", "#8E8E93" })
            {
                var col = c;
                var dot = new Ellipse
                {
                    Width = 22, Height = 22, Fill = UI.Hex(c), Margin = new Thickness(3), Cursor = Cursors.Hand,
                    Stroke = Brushes.White, StrokeThickness = string.Equals(S.Accent, c, StringComparison.OrdinalIgnoreCase) ? 2.5 : 0,
                };
                dot.MouseLeftButtonUp += (_, __) =>
                {
                    S.Accent = col;
                    foreach (Ellipse e in colors.Children) e.StrokeThickness = e == dot ? 2.5 : 0;
                };
                colors.Children.Add(dot);
            }

            return Panel(
                UI.Text("Форма чёлки", 12, true, UI.Dim).M(4, 0, 0, 6),
                cards.Panel.M(0, 0, 0, 16),
                Group(null,
                    Row("Стекло", "Матовое размытие под чёлкой", Toggle(nameof(Settings.Glass))),
                    Row("Живое стекло", "Обновлять размытый фон каждые 2–3 секунды", Toggle(nameof(Settings.LiveGlass))),
                    Row("Затемнение стекла", "Чем больше — тем темнее и читабельнее", Stepper(nameof(Settings.GlassTint), 0, 100, 5, v => v + "%")),
                    Row("Жидкое стекло", "Фон под стеклом чётче и ярче, а края его преломляют, как линза", Toggle(nameof(Settings.LiquidGlass))),
                    Row("Преломление по краям", null, Stepper(nameof(Settings.LiquidRefract), 0, 100, 10, v => v == 0 ? "нет" : v.ToString())),
                    Row("Яркость цветов под стеклом", null, Stepper(nameof(Settings.GlassVibrancy), 50, 220, 10, v => v + "%"))),
                Group("Живой остров",
                    Row("Персонаж", "Живёт в свёрнутой чёлке: танцует под музыку, спит ночью и в «Не беспокоить», удивляется уведомлениям, грустит при низком заряде",
                        Seg(nameof(Settings.Mascot), new (object, string)[] { ("none", "Нет") }.Concat(NIsland.Controls.Mascot.Skins.Select(x => ((object)x.Key, x.Name))).ToArray())),
                    Row("Цвет персонажа", null, Seg(nameof(Settings.MascotColor), ("art", "Обложка"), ("accent", "Акцент"), ("#FFFFFF", "Белый"), ("#FFD60A", "Жёлтый"), ("#FF6B9B", "Розовый"))),
                    Row("Настроение", "Реагирует на музыку, звонки, тревоги, батарею, ИИ и время суток. Выключи — будет просто улыбаться", Toggle(nameof(Settings.MascotMoods))),
                    Wide("Аксессуар", "«Авто» — наушники, когда играет музыка, колпак на праздник, шапка Деда Мороза зимой", Seg(nameof(Settings.MascotHat), NIsland.Controls.Mascot.Hats.Select(x => ((object)x.Key, x.Name)).ToArray())),
                    Row("Размер", null, Stepper(nameof(Settings.MascotSize), 70, 140, 10, v => v + "%")),
                    Row("Скорость анимаций", null, Stepper(nameof(Settings.MascotSpeed), 50, 200, 25, v => v + "%")),
                    Row("Живёт сам", "В покое подмигивает, машет, зевает вечером, оглядывается", Toggle(nameof(Settings.MascotIdle))),
                    Row("Танцует под музыку", "Покачивается в такт, на голове наушники, вокруг ноты", Toggle(nameof(Settings.MascotDance))),
                    Row("Реагирует на события", "Уведомления, звонки, таймеры, задачи агентов, копирование, флешки, трек в избранное", Toggle(nameof(Settings.MascotReactEvents))),
                    Row("Реагирует на компьютер", "Жарко при высокой нагрузке, грустит и плачет при низком заряде, радуется зарядке, мёрзнет в мороз, крутой в режиме игры", Toggle(nameof(Settings.MascotReactPc))),
                    Row("Спит ночью", "С полуночи до 6 утра, если ничего не играет", Toggle(nameof(Settings.MascotNight))),
                    Row("Тыкать в персонажа", "Клик по нему — реакция (подмигнёт, закружится, обрадуется); если тыкать часто — закружится голова", Toggle(nameof(Settings.MascotPoke))),
                    Row("Блик за курсором", "Свет на стекле следует за мышью", Toggle(nameof(Settings.IslandGlint))),
                    Row("Ободок в такт музыке", "Край острова светится сильнее на громких местах", Toggle(nameof(Settings.BeatRim))),
                    Row("Отблеск на событиях", "По острову пробегает свет, когда приходит уведомление", Toggle(nameof(Settings.IslandShimmer)))),
                Group("Чёлка",
                    Row("Открывать чёлку", null, Seg(nameof(Settings.OpenMode), ("hover", "Наведением"), ("click", "Кликом"))),
                    Row("Кнопки сбоку", "Пузырёк рядом с чёлкой: скрыть, закрепить, не беспокоить, режимы", Toggle(nameof(Settings.SideBubble))),
                    Row("Иконки страниц", "Показывать иконки при наведении на точки страниц", Toggle(nameof(Settings.PageIcons)))),
                Group("Движение",
                    Row("Анимация", "«Обычная» — стандартная пружина iOS", Seg(nameof(Settings.AnimSpeed), ("smooth", "Мягкая"), ("normal", "Обычная"), ("fast", "Быстрая")))),
                Group("Цвет",
                    Row("Акцент", null, colors),
                    Row("Цвет обложки", "Подсветка плеера и полосок под цвет альбома", Toggle(nameof(Settings.ArtColors)))));
        }

        UIElement StyleCard(string value, string title, string desc)
        {
            bool sel = S.Style == value;
            var preview = new Grid { Height = 64, ClipToBounds = true };
            preview.Add(new Border
            {
                CornerRadius = new CornerRadius(8),
                Background = new LinearGradientBrush(Color.FromRgb(0x56, 0x4F, 0xD8), Color.FromRgb(0x2E, 0x6B, 0xE6), 30),
            });
            var notch = new Border
            {
                Width = 86, Height = 18, Background = Brushes.Black, VerticalAlignment = VerticalAlignment.Top,
                CornerRadius = value == "classic" ? new CornerRadius(0, 0, 9, 9) : new CornerRadius(9),
                Margin = new Thickness(0, value == "classic" ? 0 : 6, 0, 0),
                Child = UI.Grid("a,*").M(6, 0, 8, 0).Add(
                    new Ellipse { Width = 8, Height = 8, Stroke = UI.Hex("#30D158"), StrokeThickness = 2 },
                    UI.Text("10:08", 9, true).At(0, 1).HA(HorizontalAlignment.Right)),
            };
            if (value == "classic") preview.Add(new Border { Height = 2, Background = Brushes.Black, VerticalAlignment = VerticalAlignment.Top });
            preview.Add(notch);

            var check = UI.Icon("", 11, Brushes.White);
            var checkBox = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), Background = UI.Hex("#0A84FF"), Child = check, Visibility = sel ? Visibility.Visible : Visibility.Hidden };
            var card = new Border
            {
                CornerRadius = new CornerRadius(12), Padding = new Thickness(8), Margin = new Thickness(0, 0, 10, 0), Cursor = Cursors.Hand,
                Background = UI.Hex("#14FFFFFF"), BorderThickness = new Thickness(2), BorderBrush = sel ? UI.Hex("#0A84FF") : Brushes.Transparent,
                Child = UI.Col(preview,
                    UI.Grid("*,a").M(2, 8, 2, 0).Add(UI.Col(UI.Text(title, 13.5, true), UI.Text(desc, 11.5, false, UI.Dim)), checkBox.At(0, 1))),
            };
            card.MouseLeftButtonUp += (_, __) => { S.Style = value; Open("appearance"); };
            return card;
        }

        /// <summary>Выбор анимации появления строки: чипы в несколько рядов (вариантов больше, чем влезает в одну полосу).</summary>
        static UIElement LyricsAnimPick()
        {
            var wrap = new WrapPanel();
            foreach (var (key, name) in NIsland.Pages.LyricsAnim.Kinds)
            {
                var k = key;
                var rb = new RadioButton { Style = UI.St("Segment"), Content = name, GroupName = "lyranim", IsChecked = (S.LyricsAnim ?? "rise") == k, Margin = new Thickness(0, 0, 4, 4) };
                rb.Checked += (_, __) => S.LyricsAnim = k;
                wrap.Children.Add(rb);
            }
            return wrap;
        }

        UIElement Music() => Panel(
            Group(null,
                Row("Живой текст в чёлке", "Текущая строчка песни прямо в свёрнутой чёлке", Toggle(nameof(Settings.LiveLyrics))),
                Row("Колонка с текстом", "Показывать текст песни справа от плеера", Toggle(nameof(Settings.LyricsOn))),
                Row("Всплывашка при смене трека", "Обложка и название на пару секунд", Toggle(nameof(Settings.TrackPopup))),
                Row("Обложка в чёлке", "Маленькая обложка слева, пока играет музыка", Toggle(nameof(Settings.ArtInNotch))),
                Row("Полоски в чёлке", "Пляшут справа, пока играет музыка", Toggle(nameof(Settings.Visualizer))),
                Row("Полоски слушают звук", "Двигаются под настоящую музыку, а не случайно", Toggle(nameof(Settings.LiveBars))),
                Row("Эквалайзер и визуализатор", "Кривая эквалайзера для всего звука Windows и 13 визуализаторов", UI.Pill("Открыть", () => Open("eq")))),
            Group("Плеер",
                Row("Размер обложки", null, Stepper(nameof(Settings.PlayerArtSize), 40, 80, 4, v => v + " px")),
                Row("Подсветка под цвет обложки", "Мягкое свечение снизу плеера", Toggle(nameof(Settings.PlayerGlow))),
                Row("Полоса прогресса цвета обложки", null, Toggle(nameof(Settings.ProgressArtColor))),
                Row("Кнопки ±10 секунд", "Быстрая перемотка назад и вперёд", Toggle(nameof(Settings.PlayerSeekButtons))),
                Row("Показывать альбом", null, Toggle(nameof(Settings.PlayerShowAlbum))),
                Row("Оставшееся время", "Иначе — общая длительность трека", Toggle(nameof(Settings.ShowRemaining))),
                Row("Размер текста песни", null, Stepper(nameof(Settings.LyricsFontSize), 10, 24, 1, v => v + " pt")),
                Row("Полоска прогресса в чёлке", "Тонкая линия внизу свёрнутой чёлки показывает, сколько песни прошло", Toggle(nameof(Settings.NotchProgress))),
                Row("История прослушиваний", "Сколько последних треков помнить", Stepper(nameof(Settings.HistoryKeep), 20, 300, 20, v => v + " шт."))),
            Group("Как выглядит текст",
                Row("Стиль", "Apple — дальние строки бледнеют; классика — ровный список; неон — строка светится; минимал — только текущая и соседние; прыжок — строка упруго подпрыгивает",
                    Seg(nameof(Settings.LyricsStyle), ("apple", "Apple"), ("classic", "Классика"), ("neon", "Неон"), ("minimal", "Минимал"), ("bounce", "Прыжок"))),
                Row("Цвет текущей строки", null, Seg(nameof(Settings.LyricsColor), ("white", "Белый"), ("accent", "Акцент"), ("art", "Обложка"))),
                Row("Шрифт", null, Seg(nameof(Settings.LyricsFont), ("default", "Обычный"), ("light", "Тонкий"), ("serif", "С засечками"), ("mono", "Моно"), ("script", "Рукописный"))),
                Row("Выравнивание", null, Seg(nameof(Settings.LyricsAlign), ("left", "Слева"), ("center", "По центру"))),
                Row("Латиницей", "Под строкой — то же латинскими буквами: для песен на кириллице, греческом и корейском", Toggle(nameof(Settings.LyricsRoman))),
                Wide("Появление строки", "Как загорается новая строка — в плеере и в тексте на рабочем столе. Быстро сменить: правый клик по кнопке стиля в плеере", LyricsAnimPick()),
                Row("Скорость анимации", null, Stepper(nameof(Settings.LyricsAnimMs), 150, 1500, 50, v => v + " мс")),
                Row("Лесенкой при смене песни", "Строки нового текста въезжают по очереди", Toggle(nameof(Settings.LyricsCascade))),
                Row("Размер текущей строки", "Насколько она крупнее остальных (у «Классики» и «Минимала» — без увеличения)", Stepper(nameof(Settings.LyricsActiveScale), 100, 130, 5, v => v + "%")),
                Row("Расстояние между строками", null, Stepper(nameof(Settings.LyricsLineGap), 0, 24, 2, v => v + " px")),
                Row("Заглавными буквами", null, Toggle(nameof(Settings.LyricsUpper))),
                Row("Тень под текущей строкой", "Строка читается на любом фоне, даже при прозрачном острове", Toggle(nameof(Settings.LyricsShadow))),
                Row("Скопировать или сохранить", "Кнопки в заголовке текста: копировать (правый клик — сохранить .lrc в «Музыка\\Lyrics»)", null)),
            Group("Текст на рабочем столе",
                Row("Показывать", "Текущая строка крупно поверх всех окон, с караоке-заливкой", ToggleAct(nameof(Settings.DesktopLyrics), v => { S.DesktopLyrics = v; DesktopLyricsWindow.Apply(); })),
                Row("Закрепить", "Строка пропускает клики насквозь. Сними, чтобы перетащить её в другое место", ToggleAct(nameof(Settings.DeskLyricsLock), v => DesktopLyricsWindow.SetLocked(v))),
                Row("Размер", null, Stepper(nameof(Settings.DeskLyricsSize), 16, 72, 2, v => v + " pt", DesktopLyricsWindow.Apply)),
                Row("Следующая строка", "Под текущей — следующая, бледнее", ToggleAct(nameof(Settings.DeskLyricsNext), v => { S.DeskLyricsNext = v; DesktopLyricsWindow.Apply(); })),
                Row("Непрозрачность", null, Stepper(nameof(Settings.DeskLyricsOpacity), 20, 100, 10, v => v + "%", DesktopLyricsWindow.Apply)),
                Row("Прятать в играх и фильмах", "Когда окно открыто на весь экран", Toggle(nameof(Settings.DeskLyricsHideFull))),
                Row("Положение", "Вернуть строку вниз по центру экрана", UI.Pill("Сбросить", () => { S.DeskLyricsPos = ""; S.DesktopLyrics = false; DesktopLyricsWindow.Apply(); S.DesktopLyrics = true; DesktopLyricsWindow.Apply(); }))),
            Group("Текст песни",
                Row("Караоке", "Текущая строчка заливается цветом в такт песне", Toggle(nameof(Settings.LyricsKaraoke))),
                Row("Режим фокуса", "Текст крупно на всю чёлку вместо колонки", Toggle(nameof(Settings.LyricsFocus))),
                Row("Подсветка по словам", "Если у песни есть пословная синхронизация — каждое слово загорается, когда его поют", Toggle(nameof(Settings.LyricsWords))),
                Row("Сдвиг текста", "Если строчки спешат или опаздывают для всех песен. Для одной песни — кнопки ±0,5 с прямо в чёлке",
                    Stepper(nameof(Settings.LyricsOffsetMs), -5000, 5000, 250, v => (v > 0 ? "+" : "") + (v / 1000.0).ToString("0.##") + " с", () => MediaService.I.Resync())),
                Row("Опережение строки", "Насколько раньше подсвечивать строку, чтобы успеть её прочитать до того, как её споют",
                    Stepper(nameof(Settings.LyricsLeadMs), 0, 1000, 50, v => v == 0 ? "нет" : (v / 1000.0).ToString("0.##") + " с", () => MediaService.I.Resync())),
                Row("Задержка звука устройства", "Звук доходит до ушей позже, чем его сообщает плеер: у Bluetooth-наушников и колонок на 0,15–0,25 с. «Авто» учитывает 0,2 с, когда звук идёт по Bluetooth",
                    Stepper(nameof(Settings.LyricsLatencyMs), -50, 600, 50, v => v < 0 ? "авто" : v == 0 ? "нет" : (v / 1000.0).ToString("0.##") + " с", () => MediaService.I.Resync())),
                Row("Сдвиг для приложения", "Подвинул текст пару раз в Chrome — новые песни из Chrome сразу идут с этой поправкой", Toggle(nameof(Settings.LyricsAppOffset))),
                Row("Ловить начало трека по звуку", "Для приложений, которые не сообщают время (Firefox и др.): отсчёт начинается, когда после тишины пошёл звук", Toggle(nameof(Settings.LyricsAutoAlign))),
                Row("Перевод строчек", "Под каждой строкой — перевод (Google, MyMemory или ИИ острова). Язык — правый клик по кнопке перевода в плеере", Toggle(nameof(Settings.LyricsTranslate))),
                Row("Язык перевода", null, Seg(nameof(Settings.LyricsTranslateTo), ("ru", "RU"), ("uk", "UA"), ("en", "EN"), ("de", "DE"), ("es", "ES"), ("tr", "TR"))),
                Row("Хранить тексты на диске", "Знакомые песни открываются мгновенно и без интернета", Toggle(nameof(Settings.LyricsCache))),
                Row("Запасные источники текстов", "Если LRCLIB недоступна или не знает песню — искать синхронный текст в NetEase Cloud Music и Kugou " +
                    "(туда уходят только название и исполнитель)", Toggle(nameof(Settings.LyricsFallback))),
                Row("Кэш текстов", LyricsService.CacheInfo() is var ci ? $"{ci.Files} песен · {Maint.Size(ci.Bytes)}" : null,
                    UI.Pill("Очистить", () => { LyricsService.ClearCache(); Open("music"); }))),
            Note("Перемешивание, повтор и скорость появляются, если приложение-источник их поддерживает (Spotify, медиаплеер Windows, браузеры)."),
            Note("Работает с любым приложением, которое показывает музыку в системном регуляторе громкости Windows: Spotify, Яндекс Музыка, " +
                 "VK, YouTube и YouTube Music в Chrome/Edge/Firefox/Opera, Apple Music, Telegram, AIMP, foobar2000, медиаплеер Windows и др. " +
                 "Если играет сразу несколько — переключай источник кнопкой с названием приложения. Тексты песен — из открытой базы LRCLIB " +
                 "(запасные — NetEase и Kugou); если строчки идут не в такт, подвинь их кнопками ±0,5 с — сдвиг запомнится для этой песни. " +
                 "Если приложение не сообщает время трека (Firefox, часть плееров), N-Island считает его сам, а длительность берёт из каталога — она помечена «≈»."));

        UIElement Calls() => Panel(
            Group(null,
                Row("Ловить звонки", "Telegram, WhatsApp, Discord, Teams, Zoom, Skype, звонки на сайтах в браузере и «Связь с телефоном»", Toggle(nameof(Settings.CallDetect))),
                Row("Уведомления в чёлке", "Сообщения из любых приложений появляются в чёлке", Toggle(nameof(Settings.Notifications))),
                Row("Уведомления с сайтов", "Из Chrome, Edge, Firefox и других браузеров", Toggle(nameof(Settings.NotifyBrowsers))),
                Row("Только звонки", "Не показывать обычные уведомления", Toggle(nameof(Settings.NotifyOnlyCalls))),
                Row("Сколько показывать", null, Stepper(nameof(Settings.NotifySeconds), 2, 15, 1, v => v + " с"))),
            Group("Умные уведомления",
                Row("Коды подтверждения", "Нашёл код из SMS или 2FA — кнопка «Копировать» прямо в уведомлении", Toggle(nameof(Settings.NotifyCodes))),
                Row("Счётчик непрочитанных", "● 3 в свёрнутой чёлке, пока не откроешь центр уведомлений", Toggle(nameof(Settings.NotifyBadge))),
                Row("Скрывать текст", "Показывать только название приложения — для приватности", Toggle(nameof(Settings.NotifyHideText))),
                Row("Звук", "Короткий сигнал при уведомлении", Toggle(nameof(Settings.NotifySound))),
                Wide("Важные слова", "Уведомления с этими словами пробиваются даже через «Не беспокоить» и висят дольше. Через запятую.",
                    Input(nameof(Settings.NotifyKeywords), 420)),
                Wide("Чёрный список", "Приложения и сайты, которые не всплывают (остаются в центре уведомлений). Через запятую: Discord, vk.com…",
                    Input(nameof(Settings.NotifyBlock), 420))),
            Note("Идущий звонок определяется по тому, какое приложение или сайт сейчас использует микрофон или камеру: в чёлке появляется " +
                 "зелёный индикатор с таймером, а в раскрытой чёлке — кнопка возврата к звонку. Входящие звонки и сообщения берутся из " +
                 "системных уведомлений Windows, поэтому уведомления у нужных приложений и сайтов должны быть включены."),
            Group("Микрофон",
                Row("Выключить / включить микрофон", "Системный mute микрофона по умолчанию",
                    UI.Pill("Переключить", () => AudioService.I.ToggleMicMute()))));

        UIElement Todo() => Panel(
            Group(null,
                Row("Прятать выполненные", "Показывать только то, что осталось сделать", Toggle(nameof(Settings.TodoHideDone))),
                Row("Очистить список", "Удалить все задачи", UI.Pill("Очистить", () => { Store.Data.Todos.Clear(); Store.SaveData(); }))));

        UIElement Calendar() => Panel(
            Group(null,
                Row("Неделя с понедельника", null, Toggle(nameof(Settings.CalMondayFirst))),
                Row("Напоминания о событиях", "Всплывашка в чёлке перед событием", Toggle(nameof(Settings.CalRemind))),
                Row("Напоминать заранее", null, Stepper(nameof(Settings.CalRemindBefore), 0, 120, 5, v => v == 0 ? "в момент" : v + " мин"))));

        UIElement Pomodoro() => Panel(
            Group(null,
                Row("Фокус", null, Stepper(nameof(Settings.PomoWork), 5, 120, 5, v => v + " мин")),
                Row("Короткий перерыв", null, Stepper(nameof(Settings.PomoShort), 1, 30, 1, v => v + " мин")),
                Row("Длинный перерыв", null, Stepper(nameof(Settings.PomoLong), 5, 60, 5, v => v + " мин")),
                Row("Раундов до длинного перерыва", null, Stepper(nameof(Settings.PomoRounds), 2, 8, 1, v => v.ToString())),
                Row("Автостарт следующей фазы", null, Toggle(nameof(Settings.PomoAuto)))));

        UIElement WaterSec() => Panel(
            Group(null,
                Row("Цель на день", null, Stepper(nameof(Settings.WaterGoal), 500, 5000, 250, v => Fmt.Liters(v) + " л")),
                Row("Стакан", "Сколько добавляет кнопка «Выпить»", Stepper(nameof(Settings.WaterCup), 50, 1000, 50, v => v + " мл"))),
            Group("Напоминания",
                Row("Напоминать пить воду", null, Toggle(nameof(Settings.WaterRemind))),
                Row("Каждые", null, Stepper(nameof(Settings.WaterInterval), 15, 240, 15, v => v + " мин")),
                Row("С", null, Stepper(nameof(Settings.WaterFrom), 0, 23, 1, v => v + ":00")),
                Row("До", null, Stepper(nameof(Settings.WaterTo), 1, 24, 1, v => v + ":00"))));

        UIElement Prayer() => Panel(
            Group(null,
                Row("Город", "Латиницей, например Kazan, Almaty, Istanbul", Input(nameof(Settings.PrayerCity))),
                Row("Страна", "Например Russia, Kazakhstan, Turkey", Input(nameof(Settings.PrayerCountry))),
                Row("Метод расчёта", null, Stepper(nameof(Settings.PrayerMethod), 0, 14, 1, PrayerService.MethodName)),
                Row("Напоминать о намазе", null, Toggle(nameof(Settings.PrayerRemind)))),
            Note("Время берётся с aladhan.com. Не забудь включить страницу «Намаз» в разделе «Основные»."));

        UIElement Shelf() => Panel(
            Group(null,
                Row("Открывать полку при перетаскивании", "Тащи файл к чёлке — она сама раскроется на полке", Toggle(nameof(Settings.ShelfAutoOpen))),
                Row("Очистить полку", null, UI.Pill("Очистить", () => { Store.Data.Shelf.Clear(); Store.SaveData(); }))),
            Note("На полке хранятся только ссылки на файлы — сами файлы никуда не копируются."));

        UIElement Hud() => Panel(
            Group(null,
                Row("Громкость", "Показывать изменение громкости в чёлке", Toggle(nameof(Settings.HudVolume))),
                Row("Яркость", "Для ноутбуков", Toggle(nameof(Settings.HudBrightness))),
                Row("Зарядка", "Подключение и отключение зарядки", Toggle(nameof(Settings.HudBattery))),
                Row("Низкий заряд", "Предупреждать на 20% и 10%", Toggle(nameof(Settings.HudLowBattery))),
                Row("Длительность HUD", null, Stepper(nameof(Settings.HudSeconds), 1, 6, 1, v => v + " с"))),
            Group("Устройства",
                Row("Наушники и колонки", "Показывать смену устройства вывода звука", Toggle(nameof(Settings.HudOutput))),
                Row("Флешки", "Показывать подключение флешки с кнопкой «Открыть»", Toggle(nameof(Settings.HudUsb))),
                Row("Приоритет подключения", "Одна всплывашка о подключении — без дублей от Windows («Автозапуск», «Устройство подключено»), и её не перебивают обычные уведомления", Toggle(nameof(Settings.DevicePriority)))),
            Group("Клавиатура и сеть",
                Row("Раскладка", "Показывать смену языка ввода (RU ⇄ EN)", Toggle(nameof(Settings.HudKeyboard))),
                Row("Caps Lock", "Показывать включение Caps Lock", Toggle(nameof(Settings.HudCaps))),
                Row("Скорость сети в чёлке", "Скорость загрузки справа вместо заряда", Toggle(nameof(Settings.NetInNotch)))));

        UIElement ModesSec()
        {
            var modes = new StackPanel();
            foreach (var m in Modes.All)
            {
                var mode = m;
                bool on = S.Mode == m.Key;
                var row = new Border
                {
                    CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 0, 4), Cursor = Cursors.Hand,
                    Background = on ? UI.Hex("#2E0A84FF") : UI.Hex("#14FFFFFF"),
                    BorderBrush = on ? UI.Hex("#0A84FF") : Brushes.Transparent, BorderThickness = new Thickness(1.5),
                    Child = UI.Grid("a,*").Add(UI.Icon(m.Glyph, 16).M(0, 0, 12, 0),
                        UI.Col(UI.Text(m.Name, 13.5, true), UI.Text(m.Desc, 12, false, UI.Dim)).At(0, 1)),
                };
                row.MouseLeftButtonUp += (_, __) => { App.Island.SetMode(mode.Key); Open("modes"); };
                modes.Children.Add(row);
            }

            var gallery = new System.Windows.Controls.Primitives.UniformGrid { Columns = 4 };
            foreach (var p in Presets.All)
            {
                var preset = p;
                bool on = S.Preset == p.Key;
                var preview = new Grid { Height = 56 };
                preview.Add(new Border
                {
                    CornerRadius = new CornerRadius(8),
                    Background = new LinearGradientBrush(Color.FromRgb(0x6A, 0x6F, 0xD8), Color.FromRgb(0xE0, 0x8A, 0x6B), 20),
                });
                var c1 = ColorUtil.Parse(p.Bg1, Colors.Black);
                var c2 = ColorUtil.Parse(p.Bg2, Colors.Black);
                double a = p.Glass ? Math.Clamp(0.06 + 0.9 * p.Tint / 100.0, 0.05, 1) : 1;
                var pill = new Border
                {
                    Width = 92, Height = 24, CornerRadius = new CornerRadius(12), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 8, 0, 0),
                    Background = new LinearGradientBrush(ColorUtil.WithAlpha(c1, a), ColorUtil.WithAlpha(c2, a), 45),
                    BorderBrush = new SolidColorBrush(ColorUtil.WithAlpha(ColorUtil.Parse(p.RimColor, Colors.White), p.Rim)),
                    BorderThickness = new Thickness(p.Rim > 0 ? 1.2 : 0),
                    Child = UI.Grid("a,*").M(7, 0, 9, 0).Add(
                        new Ellipse { Width = 9, Height = 9, Stroke = new SolidColorBrush(ColorUtil.Parse(p.Accent, Colors.White)), StrokeThickness = 2 },
                        UI.Text("10:08", 9.5, true).At(0, 1).HA(HorizontalAlignment.Right)),
                };
                if (p.Glass) preview.Add(new Border { Width = 92, Height = 24, CornerRadius = new CornerRadius(12), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 8, 0, 0), Background = UI.Hex("#30FFFFFF") });
                preview.Add(pill);
                var card = new Border
                {
                    CornerRadius = new CornerRadius(12), Padding = new Thickness(6), Margin = new Thickness(0, 0, 8, 8), Cursor = Cursors.Hand,
                    Background = UI.Hex("#14FFFFFF"), BorderThickness = new Thickness(2), BorderBrush = on ? UI.Hex("#0A84FF") : Brushes.Transparent,
                    Child = UI.Col(preview, UI.Text(p.Name, 12, true).M(2, 6, 0, 0), UI.Text(p.Glass ? (p.GlassCollapsed ? "стекло везде" : "стекло") : "сплошной", 10.5, false, UI.Dim).M(2, 0, 0, 0)),
                };
                card.MouseLeftButtonUp += (_, __) => { Presets.Apply(preset); Open("modes"); };
                gallery.Children.Add(card);
            }

            return Panel(
                UI.Text("Режим", 12, true, UI.Dim).M(4, 0, 0, 6),
                modes.M(0, 0, 0, 14),
                Group("Расписание",
                    Row("Включать сам по вечерам", "В заданные часы режим включится автоматически, а утром выключится", Toggle(nameof(Settings.AutoNight))),
                    Row("Какой режим", null, Seg(nameof(Settings.AutoNightMode), ("night", "Ночь"), ("dnd", "Не беспокоить"))),
                    Row("С", null, Stepper(nameof(Settings.NightFrom), 0, 23, 1, v => v + ":00")),
                    Row("До", null, Stepper(nameof(Settings.NightTo), 0, 23, 1, v => v + ":00"))),
                UI.Text("Пресеты стиля — 14 штук", 12, true, UI.Dim).M(4, 0, 0, 6),
                gallery,
                Note("Пресет задаёт цвета, стекло, ободок, блик, форму и акцент. После выбора их можно подстроить в «Внешний вид»."));
        }

        UIElement ClipSec() => Panel(
            Group("Буфер обмена",
                Row("История буфера", "Запоминать скопированный текст (до 40 записей)", Toggle(nameof(Settings.ClipWatch))),
                Row("Хранить на диске", "Иначе история живёт только до выхода из программы", Toggle(nameof(Settings.ClipSave))),
                Row("Всплывашка «Скопировано»", null, Toggle(nameof(Settings.ClipHud))),
                Row("Очистить историю", "Закреплённые записи останутся", UI.Pill("Очистить", () => ClipboardService.I.Clear()))),
            Note("Пароли и другой секретный текст тоже попадают в историю — не включай «Хранить на диске», если копируешь их."),
            Group("Заметки",
                Row("Очистить заметки", null, UI.Pill("Очистить", () => { Store.Data.Notes = ""; Store.SaveData(); }))));

        UIElement WeatherSec() => Panel(
            Group("Погода",
                Row("Город", $"Пусто — по часовому поясу ({WeatherService.DefaultCity()})", Input(nameof(Settings.WeatherCity))),
                Row("Погода в чёлке", "Температура справа в свёрнутой чёлке", Toggle(nameof(Settings.WeatherInNotch))),
                Row("Предупреждать о дожде", "За 15–45 минут до дождя или снега — всплывашка «возьми зонт» (по прогнозу осадков на каждые 15 минут)", Toggle(nameof(Settings.WeatherRainAlert))),
                Row("Утренняя сводка", "Первый раз после 7 утра: погода на день и совет, что надеть", Toggle(nameof(Settings.WeatherMorning))),
                Row("Опасная погода", "Гроза в ближайшие 3 часа, штормовой ветер, ночной заморозок, жара, очень высокий УФ — всплывашка и сообщение на телефон", Toggle(nameof(Settings.WeatherAlerts))),
                Row("Другие города", "Через запятую: на странице погоды — их температура; клик — сделать основным", Input(nameof(Settings.WeatherCities), 260)),
                Row("Вид страницы погоды", null, Seg(nameof(Settings.WeatherView), ("hours", "По часам"), ("days", "7 дней")))),
            Group("Мировые часы",
                Row("Города", "Формат: Europe/Moscow|Москва;Asia/Tokyo|Токио", Input(nameof(Settings.WorldClocks), 300))),
            Note("Погода, осадки по 15 минутам и качество воздуха — open-meteo.com, без ключей и регистрации."));

        UIElement TimerSec() => Panel(
            Group("Здоровье",
                Row("Отдых для глаз", "Правило 20-20-20: напоминание посмотреть вдаль", Toggle(nameof(Settings.EyeCare))),
                Row("Каждые", null, Stepper(nameof(Settings.EyeInterval), 10, 90, 5, v => v + " мин")),
                Row("Размяться", "Напоминание встать и подвигаться", Toggle(nameof(Settings.StandUp))),
                Row("Каждые", null, Stepper(nameof(Settings.StandInterval), 20, 180, 10, v => v + " мин"))),
            Note("Таймер, секундомер и «сон» для музыки — на странице «Таймер» в чёлке. Пока таймер идёт, оставшееся время видно справа в свёрнутой чёлке."));

        UIElement LauncherSec() => Panel(
            Group(null,
                Row("Поисковик", null, Seg(nameof(Settings.SearchEngine), ("google", "Google"), ("yandex", "Яндекс"), ("bing", "Bing"))),
                Row("Сбросить избранное", "Вернуть стандартный набор значков", UI.Pill("Сбросить", () => { Store.Data.Launch = Store.DefaultLaunch(); Store.SaveData(); }))),
            Note("В строке «Запуск» можно считать примеры (12*(3+4), 2^10, 15%4), открывать сайты и искать в интернете. " +
                 "Перетащи программу или папку на открытую страницу «Запуск», чтобы добавить; правый клик по значку — убрать."));

        // ------------------------------------------------------------------
        //  ИИ — провайдеры, ключи, модели, параметры, свои провайдеры
        // ------------------------------------------------------------------

        // ------------------------------------------------------------------
        //  Кастомизация
        // ------------------------------------------------------------------

        UIElement CustomSec()
        {
            void Mine() { if (S.Preset != "custom") S.Preset = "custom"; }

            var order = new StackPanel();
            void RenderOrder()
            {
                order.Children.Clear();
                var pages = App.Island.PageList();
                for (int i = 0; i < pages.Count; i++)
                {
                    int idx = i;
                    var (key, title, on) = pages[i];
                    void Move(int d)
                    {
                        var keys = App.Island.PageList().Select(x => x.Key).ToList();
                        int j = Math.Clamp(idx + d, 0, keys.Count - 1);
                        (keys[idx], keys[j]) = (keys[j], keys[idx]);
                        S.PageOrder = string.Join(",", keys);
                        RenderOrder();
                    }
                    order.Children.Add(UI.Grid("*,a,a").M(14, 2, 10, 2).Add(
                        UI.Text((i + 1) + ". " + title + (on ? "" : "  (выключена)"), 12.5, on, on ? Brushes.White : UI.Faint),
                        UI.IconBtn("", () => Move(-1), 10, 24, "Выше").At(0, 1),
                        UI.IconBtn("", () => Move(1), 10, 24, "Ниже").At(0, 2)));
                }
            }
            RenderOrder();

            return Panel(
                Group("Свой стиль",
                    Row("Использовать свой стиль", S.Preset == "custom" ? "Включён ✓" : "Сейчас: " + Presets.Current.Name,
                        UI.Pill("Включить", () => { S.Preset = "custom"; Open("custom"); }, S.Preset != "custom")),
                    Row("Цвет 1", "HEX, например #121220", Input(nameof(Settings.CustomBg1), 120, Mine)),
                    Row("Цвет 2", "Градиент ко второму цвету", Input(nameof(Settings.CustomBg2), 120, Mine)),
                    Row("Ободок", "Яркость светлой кромки", Stepper(nameof(Settings.CustomRim), 0, 100, 5, v => v + "%", Mine)),
                    Row("Цвет ободка", null, Input(nameof(Settings.CustomRimColor), 120, Mine)),
                    Row("Блик", "Светлый отблеск сверху", Stepper(nameof(Settings.CustomSheen), 0, 60, 2, v => v + "%", Mine)),
                    Row("Стекло и в свёрнутой чёлке", null, Toggle(nameof(Settings.CustomGlassCollapsed)))),
                Group("Форма",
                    Row("Высота свёрнутой чёлки", null, Stepper(nameof(Settings.NotchHeight), 24, 44, 1, v => v + " px")),
                    Row("Скругление", "Максимальный радиус углов", Stepper(nameof(Settings.MaxRadius), 6, 40, 1, v => v + " px")),
                    Row("Размытие стекла", null, Stepper(nameof(Settings.BlurRadius), 0, 60, 2, v => v + " px"))),
                Group("Поведение",
                    Row("Задержка открытия", "Сколько держать курсор на чёлке", Stepper(nameof(Settings.HoverDelay), 0, 1500, 50, v => v + " мс")),
                    Row("Задержка закрытия", "Через сколько закрыться, когда курсор ушёл", Stepper(nameof(Settings.CloseDelay), 100, 3000, 100, v => v + " мс"))),
                Group("Часы",
                    Row("Секунды", null, Toggle(nameof(Settings.ClockSeconds))),
                    Row("12-часовой формат", "1:45 PM вместо 13:45", Toggle(nameof(Settings.Clock12h))),
                    Row("Дата в шапке", null, Toggle(nameof(Settings.HeaderDate)))),
                Group("Свёрнутая чёлка",
                    Row("Слева", null, Seg(nameof(Settings.LeftSlot), ("auto", "Авто"), ("ring", "Кольцо"), ("none", "Ничего"))),
                    Row("Справа", "Звонок и таймер всегда в приоритете",
                        Seg(nameof(Settings.RightSlot), ("auto", "Авто"), ("battery", "Заряд"), ("weather", "Погода"), ("net", "Сеть"), ("date", "Дата"), ("ram", "ОЗУ"), ("none", "Ничего")))),
                Group("Порядок страниц", order));
        }

        UIElement OpacitySec() => Panel(
            Group("Остров",
                Row("Прозрачность острова", "Вся чёлка целиком", Stepper(nameof(Settings.IslandOpacity), 20, 100, 5, v => v + "%")),
                Row("В покое", "Когда чёлка свёрнута и курсор не на ней — при наведении снова 100%", Stepper(nameof(Settings.IdleOpacity), 10, 100, 5, v => v + "%")),
                Row("Фон без стекла", "Для сплошных стилей (iPhone, Неон, Графит…)", Stepper(nameof(Settings.BgOpacity), 10, 100, 5, v => v + "%")),
                Row("Затемнение стекла", "Для стеклянных стилей: меньше — прозрачнее", Stepper(nameof(Settings.GlassTint), 0, 100, 5, v => v + "%"))),
            Group("Окна",
                Row("Окно настроек", null, Stepper(nameof(Settings.SettingsOpacity), 50, 100, 5, v => v + "%")),
                Row("Тема окон", "AMOLED — чистый чёрный: на OLED-экранах экономит заряд и выглядит глубже",
                    Seg(nameof(Settings.WindowTheme), ("dark", "Графит"), ("amoled", "AMOLED")))),
            Note("Чем прозрачнее чёлка, тем хуже читается текст на светлом фоне — для стекла подбирай затемнение 15–35%."));

        UIElement MaintSec() => Panel(
            Group("Обслуживание",
                Row("Временные файлы старше", "Что считать мусором при очистке", Stepper(nameof(Settings.TempAgeDays), 0, 30, 1, v => v == 0 ? "любые" : v + " дн."))),
            Note("Страница «Обслуживание» в чёлке: очистка временных файлов и корзины (с подтверждением), ускорение ОЗУ, сброс DNS, " +
                 "автозагрузка (как в Диспетчере задач), процессы, схемы питания, износ батареи, диски и их здоровье, Wi-Fi и сведения о системе."),
            Group("Инструменты",
                Row("Криптовалюты", "id монет CoinGecko через запятую", Input(nameof(Settings.Crypto), 300)),
                Row("Страна для праздников", "Код страны (UA, RU, KZ…). Пусто — из настроек Windows", Input(nameof(Settings.HolidayCountry), 80)),
                Wide("Лента новостей", "Адрес RSS-ленты для инструмента «Новости»", Input(nameof(Settings.NewsFeed), 420))),
            Group("Центр управления ноутбуком",
                Row("Открыть центр", "Очистка со строгой проверкой, защита от слежки, питание, сеть, программы, исправления. Ctrl+Alt+L",
                    UI.Pill("Открыть", () => App.ShowCenter(), true))),
            Note("Все инструменты работают без ключей: Cloudflare, ipwho.is, CoinGecko, Википедия, QR Server, is.gd, Nager.Date, Open-Meteo."));

        /// <summary>Выбор звука: ‹ Перезвон › и кнопка «Прослушать».</summary>
        static UIElement SoundPicker(string prop, bool repeat = false)
        {
            var p = P(prop);
            var name = UI.Text("", 13, true);
            name.MinWidth = 92;
            name.TextAlignment = TextAlignment.Center;
            void Upd() => name.Text = Sound.Themes.FirstOrDefault(t => t.Key == (string)p.GetValue(S)).Name ?? "—";
            void Move(int d)
            {
                int i = Array.FindIndex(Sound.Themes, t => t.Key == (string)p.GetValue(S));
                var next = Sound.Themes[((i < 0 ? 0 : i) + d + Sound.Themes.Length) % Sound.Themes.Length].Key;
                p.SetValue(S, next);
                Upd();
                Sound.Preview(next, repeat);
            }
            Upd();
            var prev = UI.IconBtn("", () => Move(-1), 10, 26);
            var nextBtn = UI.IconBtn("", () => Move(1), 10, 26);
            prev.Template = nextBtn.Template = RoundTemplate();
            return UI.Row(prev, name, nextBtn, UI.IconBtn("", () => Sound.Preview((string)p.GetValue(S), repeat), 12, 30, "Прослушать").M(8, 0, 0, 0));
        }

        UIElement LanguageSec()
        {
            var status = new TextBlock { FontSize = 12.5, Foreground = UI.Hex("#9FD0FF"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 0, 4, 12) };
            var restart = UI.Pill("Перезапустить сейчас", () => App.Restart("--settings language"), true);
            restart.Visibility = Visibility.Collapsed;
            restart.HorizontalAlignment = HorizontalAlignment.Left;
            var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 12) };
            var options = new[] { ("auto", "Как в Windows") }.Concat(I18n.Available).ToList();
            var accent = (UI.Res("AccentBrush") as SolidColorBrush)?.Color ?? Colors.DodgerBlue;
            foreach (var (code, name) in options)
            {
                var c = code;
                bool active = (S.Language ?? "auto") == code;
                var card = new Border
                {
                    CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 12, 14, 12), Margin = new Thickness(0, 0, 8, 8), Cursor = Cursors.Hand,
                    Background = active ? new SolidColorBrush(ColorUtil.WithAlpha(accent, 0.3)) : UI.Hex("#14FFFFFF"),
                    BorderBrush = UI.Hex(active ? "#60FFFFFF" : "#1AFFFFFF"), BorderThickness = new Thickness(1),
                    Child = UI.Col(new TextBlock { Text = name, FontSize = 14, FontWeight = FontWeights.SemiBold, Tag = "no-translate" },
                                   UI.Text(code == "auto" ? I18n.Resolve("auto").ToUpperInvariant() : code.ToUpperInvariant(), 11, false, UI.Dim)),
                };
                card.MouseLeftButtonUp += (_, __) =>
                {
                    if (S.Language == c) return;
                    S.Language = c;
                    status.Text = I18n.Resolve(c) == I18n.Current ? "Язык уже такой ✓" : "Язык применится после перезапуска N-Island";
                    restart.Visibility = I18n.Resolve(c) == I18n.Current ? Visibility.Collapsed : Visibility.Visible;
                    foreach (Border b in grid.Children) b.Background = UI.Hex("#14FFFFFF");
                    card.Background = new SolidColorBrush(ColorUtil.WithAlpha(accent, 0.3));
                };
                grid.Children.Add(card);
            }
            return Panel(grid, status, restart,
                Note("Переводы встроены в программу и работают без интернета. Ответы ИИ приходят на языке, на котором ты пишешь. " +
                     "Нашёл неточный перевод? Поправь его в Assets/i18n на GitHub — ссылка в «О программе»."));
        }

        UIElement SoundsSec() => Panel(
            Group(null,
                Row("Звуки", "Все звуки N-Island. В режиме «Ночь» и «Не беспокоить» звучат только таймер и важное", Toggle(nameof(Settings.Sounds))),
                Row("Громкость", null, Stepper(nameof(Settings.SoundVolume), 0, 100, 10, v => v + "%", () => Sound.Preview(S.SoundReminder)))),
            Group("Что звучит",
                Row("Таймер и помодоро", "Мелодия повторяется трижды, чтобы её точно услышали", SoundPicker(nameof(Settings.SoundTimer), true)),
                Row("Напоминания", "Вода, намаз и другие мягкие напоминания", SoundPicker(nameof(Settings.SoundReminder))),
                Row("Уведомления", "Если включён звук уведомлений в разделе «Звонки и уведомления»", SoundPicker(nameof(Settings.SoundNotify))),
                Row("Важное", "События календаря и уведомления с важными словами", SoundPicker(nameof(Settings.SoundAlert)))),
            Note("Звуки синтезируются самим N-Island — без файлов и интернета. Стрелки меняют звук и сразу его проигрывают."));

        UIElement About()
        {
            long mb;
            using (var p = Process.GetCurrentProcess()) mb = p.WorkingSet64 / (1024 * 1024);
            Image logo = null;
            try
            {
                // из .ico берём самый крупный кадр — иначе WPF растянет маленький и логотип будет мыльным
                var dec = System.Windows.Media.Imaging.BitmapDecoder.Create(new Uri("pack://application:,,,/Assets/island.ico"),
                    System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                logo = new Image { Source = dec.Frames.OrderByDescending(f => f.PixelWidth).First(), Width = 64, Height = 64 };
                RenderOptions.SetBitmapScalingMode(logo, BitmapScalingMode.HighQuality);
            }
            catch { }
            var hero = new Border
            {
                CornerRadius = new CornerRadius(16), Padding = new Thickness(18), Margin = new Thickness(0, 0, 0, 16),
                Background = new LinearGradientBrush(Color.FromArgb(0x30, 0x0A, 0x84, 0xFF), Color.FromArgb(0x18, 0xBF, 0x5A, 0xF2), 25),
                BorderBrush = UI.Hex("#26FFFFFF"), BorderThickness = new Thickness(1),
                Child = UI.Grid("a,*").Add(logo,
                    UI.Col(UI.Text("N-Island", 26, true), UI.Text("Версия " + AppInfo.Version + " · Dynamic Island для Windows", 12.5, false, UI.Dim),
                           new TextBlock { Text = AppInfo.Credits, FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 0),
                                           Foreground = new LinearGradientBrush(Color.FromRgb(0x64, 0xD2, 0xFF), Color.FromRgb(0xBF, 0x5A, 0xF2), 0) })
                      .M(18, 0, 0, 0).At(0, 1)),
            };
            var update = new TextBlock { FontSize = 12.5, Foreground = UI.Hex("#9FD0FF"), TextWrapping = TextWrapping.Wrap };
            var install = UI.Pill("Обновить", null, true);
            install.Visibility = Visibility.Collapsed;
            var notes = new TextBlock { FontSize = 12, Foreground = UI.Dim, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 6, 0, 0) };
            _ = CheckUpdates(update, install, notes);   // при открытии «О программе» сразу узнаём, есть ли новая версия
            return Panel(
                hero,
                Group("Что это",
                    Wide("Чёлка как на iPhone, только для Windows",
                         "Музыка из любых приложений и сайтов с синхронным текстом песен, звонки и уведомления, ИИ-чат, таймеры, задачи, календарь, " +
                         "погода, перевод, буфер обмена и полка для файлов. Плюс «Центр управления ноутбуком»: глубокая очистка со строгой проверкой, " +
                         "защита от слежки, тонкая оптимизация Windows, журнал активности приложений и сайтов, питание, сеть и исправления — " +
                         "всё с объяснениями для новичков и откатом изменений.", new TextBlock()),
                    Row("Сделано", "Идея, дизайн и тесты — @Ne1zvestny1i в Telegram. Код написан вместе с Claude (Anthropic). " +
                                   "WPF и .NET 8, без рекламы и телеметрии, данные хранятся только на этом компьютере.",
                        UI.Pill("Telegram", () => SystemService.Run(AppInfo.Telegram)))),
                Group("Версия и обновления",
                    Row("Версия", $"{AppInfo.Version} · {(Updater.IsLite ? "lite" : "полная")} {Updater.Arch} · .NET {Environment.Version} · {Maint.WindowsVersion}",
                        UI.Pill("Проверить", () => _ = CheckUpdates(update, install, notes))),
                    UpdatePanel(update, install, notes),
                    Row("Проверять обновления сами", "Раз в сутки спрашивать GitHub и показывать уведомление в чёлке. Ставится только по твоей кнопке",
                        Toggle(nameof(Settings.AutoUpdate))),
                    Row("Исходный код и скачивание", AppInfo.RepoUrl, UI.Pill("GitHub", () => SystemService.Run(AppInfo.RepoUrl)))),
                Group(null,
                    Row("Память сейчас", mb + " МБ", UI.Pill("Освободить", () => { MemoryTrim.Now(true); Open("about"); })),
                    Row("Папка с данными", Store.Dir, UI.Pill("Открыть", () => SystemService.Run(Store.Dir))),
                    Row("Выход", "Закрыть N-Island полностью", UI.Pill("Выйти", App.Quit))),
                Note("Подсказки: правый клик по чёлке — настройки; колесо мыши над раскрытой чёлкой — листать страницы; Esc — закрыть; " +
                     "перетащи файл на чёлку — он попадёт на полку."));
        }

        static UIElement UpdatePanel(TextBlock status, Button install, TextBlock notes)
        {
            var sp = new StackPanel { Margin = new Thickness(14, 0, 14, 10) };
            install.HorizontalAlignment = HorizontalAlignment.Left;
            install.Margin = new Thickness(0, 8, 0, 0);
            sp.Children.Add(status);
            sp.Children.Add(install);
            sp.Children.Add(notes);
            return sp;
        }

        UpdateInfo _found;

        /// <summary>Спросить GitHub о последнем релизе и показать кнопку «Обновить до …», если он новее.</summary>
        async Task CheckUpdates(TextBlock status, Button install, TextBlock notes)
        {
            status.Text = "Проверяю GitHub…";
            install.Visibility = Visibility.Collapsed;
            var (state, info, err) = await Updater.Check();
            S.LastUpdateCheck = DateTime.Now;
            _found = info;
            switch (state)
            {
                case UpdateState.UpToDate: status.Text = $"У тебя последняя версия ✓ (на GitHub — {info.Version})"; return;
                case UpdateState.NoReleases: status.Text = "На GitHub пока нет опубликованных версий"; return;
                case UpdateState.Error: status.Text = "Не удалось проверить обновления: " + err; return;
            }
            status.Text = $"Вышла версия {info.Version}" + (info.AssetUrl != null ? $" · {Maint.Size(info.Size)} · ставится за минуту, настройки сохранятся"
                                                                                 : " · для этой сборки файла нет — открою страницу загрузки");
            install.Content = info.AssetUrl != null ? $"Обновить до {info.Version}" : "Открыть страницу релиза";
            install.Visibility = Visibility.Visible;
            var text = System.Text.RegularExpressions.Regex.Replace(info.Notes ?? "", @"[#*`|>]|\[(.*?)\]\(.*?\)", "$1").Trim();
            notes.Text = text.Length > 700 ? text[..700] + "…" : text;
            notes.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            install.Click -= InstallClick;
            install.Click += InstallClick;

            async void InstallClick(object s, RoutedEventArgs e)
            {
                var u = _found;
                if (u?.AssetUrl == null) { SystemService.Run(u?.PageUrl ?? AppInfo.ReleasesUrl); return; }
                install.IsEnabled = false;
                status.Text = "Скачиваю…";
                var error = await Updater.Install(u, p => Dispatcher.BeginInvoke(new Action(() => status.Text = $"Скачиваю обновление… {p * 100:0}%")));
                if (error == null) { status.Text = "Готово ✓ Перезапускаю N-Island…"; return; }
                status.Text = error;
                install.IsEnabled = true;
            }
        }

        /// <summary>Простая сетка карточек в ряд.</summary>
        class UniformGrid2
        {
            public readonly System.Windows.Controls.Primitives.UniformGrid Panel;
            public UniformGrid2(int cols) => Panel = new System.Windows.Controls.Primitives.UniformGrid { Columns = cols };
            public void Add(UIElement e) => Panel.Children.Add(e);
        }
    }
}
