using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NIsland.Pages
{
    /// <summary>Короткие помощники, чтобы собирать интерфейс кодом.</summary>
    public static class UI
    {
        public static readonly FontFamily Icons = new("Segoe Fluent Icons, Segoe MDL2 Assets");

        public static Style St(string key) => (Style)Application.Current.Resources[key];
        public static Brush Res(string key) => (Brush)Application.Current.Resources[key];
        public static Brush Dim => Res("DimBrush");
        public static Brush Faint => Res("FaintBrush");
        public static SolidColorBrush Hex(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex);

        public static TextBlock Text(string s = "", double size = 13, bool bold = false, Brush fg = null)
        {
            var t = new TextBlock
            {
                Text = s,
                FontSize = size,
                FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            };
            if (fg != null) t.Foreground = fg;
            return t;
        }

        public static TextBlock Icon(string glyph, double size = 14, Brush fg = null)
        {
            var t = new TextBlock
            {
                Text = glyph,
                FontFamily = Icons,
                FontSize = size,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            if (fg != null) t.Foreground = fg;
            return t;
        }

        public static Button IconBtn(string glyph, Action click, double size = 15, double box = 32, string tip = null)
        {
            var b = new Button { Style = St("IconButton"), Content = glyph, FontSize = size, Width = box, Height = box, ToolTip = tip };
            if (click != null) b.Click += (_, __) => click();
            return b;
        }

        public static Button Pill(object content, Action click, bool accent = false, string tip = null)
        {
            var b = new Button { Style = St(accent ? "AccentPill" : "PillButton"), Content = content, ToolTip = tip, VerticalAlignment = VerticalAlignment.Center };
            if (click != null) b.Click += (_, __) => click();
            return b;
        }

        /// <summary>Иконка + подпись внутри кнопки.</summary>
        public static StackPanel IconLabel(string glyph, string text, double size = 12)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(Icon(glyph, size));
            sp.Children.Add(Text(text, size).M(6, 0, 0, 0));
            return sp;
        }

        public static TextBox Box(string hint, double width = double.NaN)
        {
            return new TextBox { Style = St("DarkBox"), Tag = hint, Width = width };
        }

        public static Slider Slider(double max = 1)
        {
            return new Slider { Style = St("ThinSlider"), Minimum = 0, Maximum = max, VerticalAlignment = VerticalAlignment.Center };
        }

        public static Grid Grid(string cols = null, string rows = null)
        {
            var g = new Grid();
            if (cols != null)
                foreach (var c in cols.Split(','))
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = Len(c.Trim()) });
            if (rows != null)
                foreach (var r in rows.Split(','))
                    g.RowDefinitions.Add(new RowDefinition { Height = Len(r.Trim()) });
            return g;
        }

        static GridLength Len(string s)
        {
            if (s == "a" || s == "auto") return GridLength.Auto;
            if (s.EndsWith("*")) return new GridLength(s == "*" ? 1 : double.Parse(s[..^1]), GridUnitType.Star);
            return new GridLength(double.Parse(s));
        }

        public static StackPanel Row(params UIElement[] items)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            foreach (var i in items) if (i != null) sp.Children.Add(i);
            return sp;
        }

        public static StackPanel Col(params UIElement[] items)
        {
            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            foreach (var i in items) if (i != null) sp.Children.Add(i);
            return sp;
        }

        public static T At<T>(this T el, int row, int col, int colSpan = 1) where T : UIElement
        {
            System.Windows.Controls.Grid.SetRow(el, row);
            System.Windows.Controls.Grid.SetColumn(el, col);
            if (colSpan > 1) System.Windows.Controls.Grid.SetColumnSpan(el, colSpan);
            return el;
        }

        public static T M<T>(this T el, double l, double t, double r, double b) where T : FrameworkElement
        {
            el.Margin = new Thickness(l, t, r, b);
            return el;
        }

        public static T HA<T>(this T el, HorizontalAlignment a) where T : FrameworkElement { el.HorizontalAlignment = a; return el; }
        public static T VA<T>(this T el, VerticalAlignment a) where T : FrameworkElement { el.VerticalAlignment = a; return el; }

        public static Grid Add(this Grid g, params UIElement[] items)
        {
            foreach (var i in items) if (i != null) g.Children.Add(i);
            return g;
        }

        public static Border Card(UIElement child, double radius = 12, double pad = 8)
        {
            return new Border { Background = Res("CardBrush"), CornerRadius = new CornerRadius(radius), Padding = new Thickness(pad), Child = child };
        }

        public static void Show(this UIElement e, bool v) => e.Visibility = v ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Автообновление экрана: пока элемент на экране и виден — раз в interval вызывает refresh. Запуски не накладываются,
        /// ошибка одного обновления пишется в журнал и не останавливает следующие, при закрытии экрана таймер останавливается.
        /// </summary>
        public static T Live<T>(this T owner, TimeSpan interval, Func<System.Threading.Tasks.Task> refresh, bool now = false) where T : FrameworkElement
        {
            var t = new System.Windows.Threading.DispatcherTimer { Interval = interval };
            bool busy = false;
            int fails = 0;
            async void Run()
            {
                if (busy || !owner.IsVisible) return;
                var win = Window.GetWindow(owner);
                if (win != null && win.WindowState == WindowState.Minimized) return;
                busy = true;
                try { await refresh(); fails = 0; }
                catch (Exception e) { if (fails++ == 0) NIsland.Core.Log.Info("auto refresh: " + e.Message); }
                finally { busy = false; }
            }
            t.Tick += (_, __) => Run();
            owner.Loaded += (_, __) => { t.Start(); if (now) Run(); };
            owner.Unloaded += (_, __) => t.Stop();
            owner.IsVisibleChanged += (_, e) => { if ((bool)e.NewValue) t.Start(); else t.Stop(); };
            return owner;
        }

        /// <summary>Элемент с акцентным фоном: текст внутри тёмный, если акцент светлый (белый, жёлтый), иначе белый.</summary>
        public static T OnAccent<T>(this T el, bool on = true) where T : FrameworkElement
        {
            if (on) el.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "AccentTextBrush");
            else el.ClearValue(System.Windows.Documents.TextElement.ForegroundProperty);
            return el;
        }
    }
}
