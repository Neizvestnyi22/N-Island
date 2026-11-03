using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using NIsland.Core;
using NIsland.Pages;
using NIsland.Services;

namespace NIsland
{
    /// <summary>
    /// Быстрый поиск (Ctrl+Alt+Space) — как Spotlight и Raycast: программы, параметры Windows, калькулятор, валюты и единицы,
    /// перевод, команды острова, недавние файлы, буфер обмена, ответ ИИ прямо в окне и поиск в интернете. Всё с клавиатуры.
    /// </summary>
    public class SpotlightWindow : Window
    {
        class Hit
        {
            public string Title, Sub, Glyph, Key;
            public ImageSource Icon;
            public double Score;
            public Action Run;
            public bool KeepOpen;
        }

        static Settings S => Store.Settings;
        readonly TextBox _box = new();
        readonly TextBlock _hint = UI.Text("", 18);
        readonly StackPanel _list = new();
        readonly Border _answer = new() { Visibility = Visibility.Collapsed };
        readonly TextBlock _answerText = new() { TextWrapping = TextWrapping.Wrap, FontSize = 13.5, Foreground = Brushes.White };
        readonly TextBlock _footer = UI.Text("", 11);
        readonly Border _card;
        readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(90) };
        List<Hit> _hits = new();
        int _sel;
        int _gen;
        CancellationTokenSource _aiCts;
        IntPtr _hwnd, _prevFg;

        public SpotlightWindow()
        {
            Title = "N-Island — быстрый поиск";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            Width = 720;
            SizeToContent = SizeToContent.Height;
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
            Foreground = Brushes.White;
            UseLayoutRounding = true;

            _box.FontSize = 21;
            _box.Foreground = Brushes.White;
            _box.CaretBrush = Brushes.White;
            _box.Background = Brushes.Transparent;
            _box.BorderThickness = new Thickness(0);
            _box.Template = Ago.PlainBox();
            _box.Tag = "Найти программу, настройку, посчитать, перевести, спросить ИИ…";
            _box.VerticalContentAlignment = VerticalAlignment.Center;
            _box.TextChanged += (_, __) => { _debounce.Stop(); _debounce.Start(); Search(true); };
            _box.PreviewKeyDown += OnKey;
            _debounce.Tick += (_, __) => { _debounce.Stop(); Search(false); };

            var head = UI.Grid("a,*").M(18, 12, 18, 12).Add(UI.Icon("", 19, UI.Hex("#B0FFFFFF")), _box.At(0, 1).M(12, 0, 0, 0));
            _answer.Child = new ScrollViewer { Content = _answerText, MaxHeight = 260, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(18, 4, 18, 10) };
            _footer.Foreground = UI.Hex("#80FFFFFF");
            var footer = UI.Grid("*,a").M(18, 6, 18, 10).Add(_footer,
                UI.Text("↑↓ выбрать · Enter открыть · Tab — спросить ИИ · Esc закрыть", 11, false, UI.Hex("#70FFFFFF")).At(0, 1));
            var amoled = S.WindowTheme == "amoled";
            _card = new Border
            {
                CornerRadius = new CornerRadius(18), Margin = new Thickness(24),
                Background = new LinearGradientBrush(amoled ? Color.FromArgb(0xF4, 0, 0, 0) : Color.FromArgb(0xEE, 0x22, 0x22, 0x28),
                                                     amoled ? Color.FromArgb(0xF4, 0, 0, 0) : Color.FromArgb(0xF2, 0x16, 0x16, 0x1B), 90),
                BorderBrush = new LinearGradientBrush(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF), 90),
                BorderThickness = new Thickness(1),
                Effect = S.LiteMode ? null : new DropShadowEffect { BlurRadius = 40, ShadowDepth = 10, Opacity = 0.55, RenderingBias = RenderingBias.Performance },
                Child = UI.Col(head, new Border { Height = 1, Background = UI.Hex("#18FFFFFF") }, _answer, _list.M(8, 6, 8, 0), footer),
            };
            Content = _card;
            Deactivated += (_, __) => Dismiss(false);
            SourceInitialized += (_, __) => _hwnd = new WindowInteropHelper(this).Handle;
        }

        // ------------------------------------------------------------------
        //  Показ и скрытие
        // ------------------------------------------------------------------

        static SpotlightWindow _inst;

        public static void Toggle(string text = null)
        {
            _inst ??= new SpotlightWindow();
            if (_inst.IsVisible && text == null) { _inst.Dismiss(true); return; }
            _inst.Open(text);
        }

        void Open(string text)
        {
            _ = AppIndex.Apps;   // начать загрузку списка программ, если ещё не было
            // по центру монитора, на котором курсор, чуть выше середины
            var pt = System.Windows.Forms.Cursor.Position;
            var scr = System.Windows.Forms.Screen.FromPoint(pt).WorkingArea;
            double dpi = _hwnd != IntPtr.Zero ? VisualTreeHelper.GetDpi(this).DpiScaleX : (App.Island != null ? VisualTreeHelper.GetDpi(App.Island).DpiScaleX : 1);
            Left = scr.Left / dpi + (scr.Width / dpi - Width) / 2;
            Top = scr.Top / dpi + scr.Height / dpi * 0.18;
            _answer.Visibility = Visibility.Collapsed;
            _aiCts?.Cancel();
            _box.Text = text ?? "";
            _box.CaretIndex = _box.Text.Length;
            if (!IsVisible) Show();
            if (_hwnd != IntPtr.Zero) { var prev = Native.ForceForeground(_hwnd); if (prev != IntPtr.Zero && prev != _hwnd) _prevFg = prev; }
            Activate();
            _box.Focus();
            Keyboard.Focus(_box);
            _box.SelectAll();
            Search(false);
            var tt = new ScaleTransform(0.97, 0.97);
            _card.RenderTransformOrigin = new Point(0.5, 0);
            _card.RenderTransform = tt;
            var ease = new QuinticEase { EasingMode = EasingMode.EaseOut };
            tt.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.97, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
            tt.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.97, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
            _card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
        }

        void Dismiss(bool restoreFocus)
        {
            if (!IsVisible) return;
            _aiCts?.Cancel();
            Hide();
            if (restoreFocus && _prevFg != IntPtr.Zero) Native.RestoreForeground(_prevFg);
            _prevFg = IntPtr.Zero;
            if (S.LowMemory) Dispatcher.BeginInvoke(new Action(() => MemoryTrim.Now(true)), DispatcherPriority.ApplicationIdle);
        }

        void OnKey(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Escape:
                    if (_answer.Visibility == Visibility.Visible) { _answer.Visibility = Visibility.Collapsed; _aiCts?.Cancel(); }
                    else if (_box.Text != "") _box.Text = "";
                    else Dismiss(true);
                    e.Handled = true;
                    break;
                case Key.Down: Move(1); e.Handled = true; break;
                case Key.Up: Move(-1); e.Handled = true; break;
                case Key.Tab: AskAi(_box.Text.Trim()); e.Handled = true; break;
                case Key.Enter:
                    if (_sel >= 0 && _sel < _hits.Count) Execute(_hits[_sel]);
                    e.Handled = true;
                    break;
            }
        }

        void Move(int d)
        {
            if (_hits.Count == 0) return;
            _sel = (_sel + d + _hits.Count) % _hits.Count;
            Render();
        }

        void Execute(Hit h)
        {
            if (h.Key != null)
            {
                var u = Store.Data.SpotlightUsage ??= new();
                u[h.Key] = (u.TryGetValue(h.Key, out var n) ? n : 0) + 1;
                if (u.Count > 400) u.Remove(u.OrderBy(x => x.Value).First().Key);
                Store.SaveData();
            }
            if (!h.KeepOpen) Dismiss(false);
            try { h.Run?.Invoke(); }
            catch (Exception ex) { Log.Info("spotlight: " + ex.Message); }
        }

        // ------------------------------------------------------------------
        //  Поиск
        // ------------------------------------------------------------------

        /// <param name="quick">Сразу по нажатию — только мгновенные результаты; через 90 мс — ещё и сетевые (валюты, перевод).</param>
        void Search(bool quick)
        {
            var q = _box.Text.Trim();
            int gen = ++_gen;
            var hits = new List<Hit>();
            if (q.Length == 0)
            {
                // пусто — частые программы и подсказки
                var usage = Store.Data.SpotlightUsage ?? new();
                foreach (var a in AppIndex.Apps.Where(a => usage.ContainsKey("app:" + a.Name)).OrderByDescending(a => usage["app:" + a.Name]).Take(5))
                    hits.Add(AppHit(a, 50));
                hits.Add(new Hit { Title = "Таймер на 5 минут", Sub = "Напиши «таймер 25» — на любое время", Glyph = "", Run = () => StartTimer(5), Score = 10 });
                hits.Add(new Hit { Title = "Открыть остров", Sub = "Ctrl+Alt+N", Glyph = "", Run = () => App.Island.Expand(null, true), Score = 9 });
                Show(hits, "Программы, настройки Windows, «2+2*3», «100 usd в грн», «10 км в мили», «пер привет», «? вопрос ИИ»");
                return;
            }
            string ql = q.ToLowerInvariant();

            // ИИ: «? вопрос»
            if (q.StartsWith("?") && q.Length > 1)
                hits.Add(new Hit { Title = "Спросить ИИ: " + q[1..].Trim(), Sub = "Ответ появится прямо здесь · Tab", Glyph = "", Score = 200, KeepOpen = true, Run = () => AskAi(q[1..].Trim()) });

            // калькулятор
            if (Calc.TryEval(q) is { } calc)
                hits.Add(new Hit { Title = "= " + calc, Sub = q + " · Enter — скопировать", Glyph = "", Score = 190, Run = () => Copy(calc) });

            // единицы
            if (Units.Convert(q) is { } unit)
                hits.Add(new Hit { Title = unit, Sub = "Перевод единиц · Enter — скопировать", Glyph = "", Score = 185, Run = () => Copy(unit.Split(" = ").Last()) });

            // команды острова
            hits.AddRange(Commands(q, ql));

            // программы
            foreach (var a in AppIndex.Apps)
            {
                double s = Match(a.Name, ql);
                if (s > 0) hits.Add(AppHit(a, s));
            }

            // параметры Windows и разделы N-Island
            foreach (var (name, target, glyph) in WinSettings)
            {
                double s = Match(name, ql);
                if (s > 0) hits.Add(new Hit { Title = name, Sub = "Параметры Windows", Glyph = glyph, Score = s - 6, Key = "set:" + target, Run = () => SystemService.Run(target) });
            }
            foreach (var (name, key) in IslandSections)
            {
                double s = Math.Max(Match(name, ql), Match(I18n.T(name), ql));
                if (s > 0) hits.Add(new Hit { Title = name, Sub = "Настройки N-Island", Glyph = "", Score = s - 8, Key = "nis:" + key, Run = () => App.ShowSettings(key) });
            }

            // недавние файлы
            if (q.Length >= 3)
                foreach (var f in Recent())
                {
                    double s = Match(Path.GetFileNameWithoutExtension(f), ql);
                    if (s > 0) hits.Add(new Hit { Title = Path.GetFileNameWithoutExtension(f), Sub = "Недавний файл", Icon = FileIcons.Get(f), Score = s - 12, Run = () => SystemService.Run(f) });
                }

            // буфер обмена
            if (q.Length >= 3 && S.ClipWatch)
                foreach (var c in ClipboardService.I.Items.Where(c => c.Text != null && c.Text.Contains(q, StringComparison.OrdinalIgnoreCase)).Take(2))
                {
                    var text = c.Text;
                    hits.Add(new Hit { Title = OneLine(text), Sub = "Из буфера обмена · Enter — скопировать", Glyph = "", Score = 30, Run = () => Copy(text) });
                }

            // в интернете
            var engine = S.SearchEngine switch { "yandex" => ("Яндекс", "https://yandex.ru/search/?text="), "bing" => ("Bing", "https://www.bing.com/search?q="), _ => ("Google", "https://www.google.com/search?q=") };
            bool url = Regex.IsMatch(q, @"^(https?://)?[\w\-]+(\.[\w\-]+)+(/\S*)?$") && !q.Contains(' ');
            if (url)
            {
                hits.Add(new Hit { Title = "Открыть " + q, Sub = "Сайт", Glyph = "", Score = 150, Run = () => SystemService.Run(q.StartsWith("http") ? q : "https://" + q) });
                hits.Add(new Hit { Title = "Проверить ссылку: " + q, Sub = "Фишинг, сокращатели, возраст домена — OSINT", Glyph = "", Score = 140, Run = () => { Osint.Pending = q; App.ShowCenter("osint"); } });
            }
            hits.Add(new Hit { Title = $"Искать в {engine.Item1}: {q}", Sub = "Интернет", Glyph = "", Score = 5, Run = () => SystemService.Run(engine.Item2 + Uri.EscapeDataString(q)) });
            hits.Add(new Hit { Title = "Найти на YouTube: " + q, Sub = "Интернет", Glyph = "", Score = 4, Run = () => SystemService.Run("https://www.youtube.com/results?search_query=" + Uri.EscapeDataString(q)) });
            if (!q.StartsWith("?")) hits.Add(new Hit { Title = "Спросить ИИ: " + q, Sub = "Tab — ответ прямо здесь", Glyph = "", Score = 3, KeepOpen = true, Run = () => AskAi(q) });

            Show(hits, null);
            if (quick) return;

            // сетевые результаты — догружаем, не мешая печатать
            if (Currency.Parse(q) is { } cur)
            {
                _ = Task.Run(async () =>
                {
                    var v = await Currency.Convert(cur.Amount, cur.From, cur.To);
                    if (v is not double val) return;
                    _ = Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (gen != _gen) return;
                        var text = $"{Currency.Fmt(cur.Amount, cur.From)} = {Currency.Fmt(val, cur.To)}";
                        Prepend(new Hit { Title = text, Sub = "Курс open.er-api.com · Enter — скопировать", Glyph = "", Score = 195, Run = () => Copy(Currency.Fmt(val, cur.To)) });
                    }));
                });
            }
            var tr = Regex.Match(q, @"^(пер|перевод|переведи|tr|translate)\s+(.+)$", RegexOptions.IgnoreCase);
            if (tr.Success)
            {
                var src = tr.Groups[2].Value;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var (res, from, to) = await TranslateService.Translate(src, S.TranslateTo);
                        _ = Dispatcher.BeginInvoke(new Action(() =>
                        {
                            if (gen != _gen) return;
                            Prepend(new Hit { Title = res, Sub = $"Перевод {from}→{to} · Enter — скопировать", Glyph = "", Score = 195, Run = () => Copy(res) });
                        }));
                    }
                    catch { }
                });
            }
        }

        void Prepend(Hit h)
        {
            _hits.Insert(0, h);
            _sel = 0;
            Render();
        }

        void Show(List<Hit> hits, string footer)
        {
            var usage = Store.Data.SpotlightUsage ?? new();
            foreach (var h in hits)
                if (h.Key != null && usage.TryGetValue(h.Key, out var n)) h.Score += Math.Min(25, 6 * Math.Log2(1 + n));   // чем чаще запускаешь — тем выше
            _hits = hits.GroupBy(h => h.Title + "|" + h.Sub).Select(g => g.OrderByDescending(x => x.Score).First())
                        .OrderByDescending(h => h.Score).Take(9).ToList();
            _sel = 0;
            _footer.Text = footer != null ? I18n.T(footer) : AppIndex.Apps.Count == 0 ? I18n.T("Загружаю список программ…") : "";
            Render();
        }

        void Render()
        {
            _list.Children.Clear();
            var accent = (UI.Res("AccentBrush") as SolidColorBrush)?.Color ?? Colors.DodgerBlue;
            // светлый акцент (белый) под белым текстом не читается — выделяем мягкой подсветкой
            if ((0.299 * accent.R + 0.587 * accent.G + 0.114 * accent.B) / 255 > 0.68) accent = Color.FromRgb(0x5A, 0x5A, 0x66);
            for (int i = 0; i < _hits.Count; i++)
            {
                var h = _hits[i];
                int idx = i;
                bool sel = i == _sel;
                FrameworkElement icon = h.Icon != null
                    ? new Image { Source = h.Icon, Width = 26, Height = 26 }
                    : new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(8), Background = sel ? new SolidColorBrush(ColorUtil.WithAlpha(Colors.White, 0.18)) : UI.Hex("#1CFFFFFF"), Child = UI.Icon(h.Glyph ?? "", 14, Brushes.White) };
                var title = new TextBlock { Text = h.Title, FontSize = 14, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
                var sub = new TextBlock { Text = h.Sub ?? "", FontSize = 11.5, Foreground = sel ? UI.Hex("#D0FFFFFF") : UI.Hex("#80FFFFFF"), TextTrimming = TextTrimming.CharacterEllipsis };
                var enter = UI.Text(sel ? "↵" : "", 13, true, UI.Hex("#A0FFFFFF"));
                var row = new Border
                {
                    CornerRadius = new CornerRadius(11), Padding = new Thickness(10, 6, 12, 6), Margin = new Thickness(0, 0, 0, 2), Cursor = Cursors.Hand,
                    Background = sel ? new SolidColorBrush(ColorUtil.WithAlpha(accent, 0.55)) : Brushes.Transparent,
                    Child = UI.Grid("38,*,a").Add(icon.HA(HorizontalAlignment.Center), UI.Col(title, sub).At(0, 1).M(8, 0, 8, 0), enter.At(0, 2)),
                };
                row.MouseEnter += (_, __) => { if (_sel != idx) { _sel = idx; Render(); } };
                row.MouseLeftButtonUp += (_, __) => Execute(h);
                _list.Children.Add(row);
            }
        }

        // ------------------------------------------------------------------
        //  ИИ прямо в окне
        // ------------------------------------------------------------------

        async void AskAi(string q)
        {
            if (string.IsNullOrWhiteSpace(q)) return;
            _aiCts?.Cancel();
            var cts = _aiCts = new CancellationTokenSource();
            _answer.Visibility = Visibility.Visible;
            _answerText.Text = I18n.T("Думаю…");
            var sb = new System.Text.StringBuilder();
            try
            {
                var res = await Ai.Ask(new[] { new AiMessage("user", q) }, delta =>
                {
                    sb.Append(delta);
                    Dispatcher.BeginInvoke(new Action(() => { if (!cts.IsCancellationRequested) _answerText.Text = sb.ToString(); }));
                }, cts.Token);
                if (cts.IsCancellationRequested) return;
                _answerText.Text = res.Trim();
                var answer = res.Trim();
                _hits.Insert(0, new Hit { Title = "Скопировать ответ", Sub = "Или продолжить разговор в острове — Ctrl+Alt+A", Glyph = "", Score = 999, Run = () => Copy(answer) });
                _sel = 0;
                Render();
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { if (!cts.IsCancellationRequested) _answerText.Text = I18n.T("Ошибка ИИ: ") + e.Message; }
        }

        // ------------------------------------------------------------------
        //  Источники
        // ------------------------------------------------------------------

        Hit AppHit(AppIndex.App a, double score) => new()
        {
            Title = a.Name, Sub = a.Target.StartsWith("shell:") ? I18n.T("Приложение") : I18n.T("Программа"), Icon = AppIndex.Icon(a),
            Glyph = "", Score = score + 4, Key = "app:" + a.Name, Run = () => SystemService.Run(a.Target),
        };

        static void Copy(string text)
        {
            ClipboardService.I.CopyBack(text);
            App.Island?.Toast("", "Скопировано", text.Length > 60 ? text[..60] + "…" : text);
        }

        static string OneLine(string s)
        {
            s = Regex.Replace(s, @"\s+", " ").Trim();
            return s.Length > 90 ? s[..90] + "…" : s;
        }

        static void StartTimer(double minutes)
        {
            var t = TimerService.I;
            if (t.Running) t.Reset();
            t.SetMinutes(minutes);
            t.Start();
            App.Island?.Toast("", "Таймер запущен", $"{minutes:0.#} мин");
        }

        IEnumerable<Hit> Commands(string q, string ql)
        {
            var m = Regex.Match(ql, @"^(таймер|timer|т|t)\s*(\d+(?:[.,]\d+)?)\s*(мин|m|min|м|с|сек|s|ч|h)?$");
            if (m.Success)
            {
                double v = double.Parse(m.Groups[2].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
                double min = m.Groups[3].Value is "с" or "сек" or "s" ? v / 60 : m.Groups[3].Value is "ч" or "h" ? v * 60 : v;
                yield return new Hit { Title = $"Таймер на {min:0.#} мин", Sub = "Запустить в острове", Glyph = "", Score = 180, Run = () => StartTimer(min) };
            }
            m = Regex.Match(q, @"^(заметка|note|з)\s+(.+)$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                var text = m.Groups[2].Value;
                yield return new Hit { Title = "Добавить заметку: " + text, Sub = "В «Заметки» острова", Glyph = "", Score = 180, Run = () =>
                {
                    Store.Data.Notes = (Store.Data.Notes ?? "").TrimEnd() + (string.IsNullOrWhiteSpace(Store.Data.Notes) ? "" : "\n") + text;
                    Store.SaveData();
                    App.Island?.Toast("", "Заметка добавлена", text);
                } };
            }
            m = Regex.Match(q, @"^(задача|todo|дело|сделать)\s+(.+)$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                var text = m.Groups[2].Value;
                yield return new Hit { Title = "Новая задача: " + text, Sub = "В «Задачи» острова", Glyph = "", Score = 180, Run = () =>
                {
                    Store.Data.Todos.Insert(0, new TodoItem { Text = text });
                    Store.SaveData();
                    App.Island?.Toast("", "Задача добавлена", text);
                } };
            }
            // страницы острова
            if (App.Island != null)
                foreach (var (key, title, on) in App.Island.PageList().Where(p => p.On))
                {
                    double s = Math.Max(Match(title, ql), Match(I18n.T(title), ql));
                    if (s > 0) yield return new Hit { Title = I18n.T(title), Sub = "Открыть в острове", Glyph = "", Score = s - 2, Key = "page:" + key, Run = () => App.Island.Expand(key, true) };
                }
            foreach (var mode in Modes.All)
            {
                double s = Math.Max(Match("режим " + mode.Name, ql), Match(mode.Name, ql) - 20);
                if (s > 0) yield return new Hit { Title = "Режим: " + mode.Name, Sub = mode.Desc, Glyph = mode.Glyph, Score = s - 5, Run = () => App.Island.SetMode(mode.Key) };
            }
            var actions = new (string Name, string Sub, string Glyph, Action Run)[]
            {
                ("Пипетка — цвет с экрана", "Ctrl+Alt+C", "", () => ColorPicker.I.Start()),
                ("Центр управления ноутбуком", "Очистка, защита, питание · Ctrl+Alt+L", "", () => App.ShowCenter()),
                ("Скрыть остров", "Ctrl+Alt+H", "", () => App.Island.HideIsland()),
                ("Не беспокоить", "Ctrl+Alt+D", "", () => App.Island.SetMode(Modes.Dnd ? "normal" : "dnd")),
                ("Пауза / воспроизведение", MediaService.I.TrackLine, "", () => MediaService.I.TogglePlay()),
                ("Следующий трек", MediaService.I.TrackLine, "", () => MediaService.I.Next()),
                ("Заблокировать компьютер", "Win+L", "", () => Native.LockWorkStation()),
                ("Быстрый поиск — настройки", "Горячая клавиша, что искать", "", () => App.ShowSettings("spotlight")),
                ("Очистить корзину", "Спросит подтверждение", "", () => SystemService.Run("shell:RecycleBinFolder")),
            };
            foreach (var a in actions)
            {
                double s = Match(a.Name, ql);
                if (s > 0) { var act = a; yield return new Hit { Title = act.Name, Sub = act.Sub, Glyph = act.Glyph, Score = s - 4, Key = "act:" + act.Name, Run = act.Run }; }
            }
        }

        static IEnumerable<string> Recent()
        {
            try
            {
                var dir = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
                return new DirectoryInfo(dir).GetFiles("*.lnk").OrderByDescending(f => f.LastWriteTime).Take(80).Select(f => f.FullName);
            }
            catch { return Array.Empty<string>(); }
        }

        // ------------------------------------------------------------------
        //  Сопоставление: начало слова, аббревиатура, «не та раскладка»
        // ------------------------------------------------------------------

        const string En = "qwertyuiop[]asdfghjkl;'zxcvbnm,.`";
        const string Ru = "йцукенгшщзхъфывапролджэячсмитьбюё";

        /// <summary>«ntktuhfv» → «телеграм»: набрали не в той раскладке.</summary>
        static string SwapLayout(string s)
        {
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (var c in s)
            {
                int i = En.IndexOf(c);
                if (i >= 0) { sb.Append(Ru[i]); continue; }
                i = Ru.IndexOf(c);
                sb.Append(i >= 0 ? En[i] : c);
            }
            return sb.ToString();
        }

        public static double Match(string name, string q)
        {
            if (string.IsNullOrEmpty(name) || q.Length == 0) return 0;
            var n = name.ToLowerInvariant();
            double s = Raw(n, q);
            if (s < 60 && q.Length >= 3)
            {
                var swapped = SwapLayout(q);
                // «ntktuhfv» → «телеграм» → «telegram»: и раскладка, и кириллица против латинского названия
                s = Math.Max(s, Math.Max(Raw(n, swapped), Math.Max(Raw(n, Translit(q)), Raw(n, Translit(swapped)))) * 0.95);
            }
            return s;
        }

        static readonly string[] Lat =
        {
            "a","b","v","g","d","e","zh","z","i","y","k","l","m","n","o","p","r","s","t","u","f","h","ts","ch","sh","sch","","y","","e","yu","ya",
        };

        static string Translit(string s)
        {
            var sb = new System.Text.StringBuilder(s.Length + 4);
            foreach (var c in s)
            {
                if (c >= 'а' && c <= 'я') sb.Append(Lat[c - 'а']);
                else if (c == 'ё') sb.Append('e');
                else if (c is 'і' or 'ї') sb.Append('i');
                else if (c == 'є') sb.Append("e");
                else sb.Append(c);
            }
            return sb.ToString();
        }

        static double Raw(string n, string q)
        {
            if (n == q) return 100;
            if (n.StartsWith(q)) return 92 - Math.Min(10, (n.Length - q.Length) * 0.15);
            if (Regex.IsMatch(n, @"(^|[\s\-_.(«""])" + Regex.Escape(q))) return 82;
            var initials = string.Concat(Regex.Matches(n, @"(?<=^|[\s\-_.])\w").Select(x => x.Value));
            if (q.Length >= 2 && initials.StartsWith(q)) return 74;
            if (q.Length >= 3 && n.Contains(q)) return 64;
            if (q.Length >= 3)
            {
                int j = 0;
                foreach (var c in n) if (j < q.Length && c == q[j]) j++;
                if (j == q.Length) return Math.Max(20, 44 - (n.Length - q.Length) * 0.6);
            }
            return 0;
        }

        static readonly (string Name, string Target, string Glyph)[] WinSettings =
        {
            ("Bluetooth и устройства", "ms-settings:bluetooth", ""), ("Wi-Fi", "ms-settings:network-wifi", ""), ("Сеть и интернет", "ms-settings:network", ""),
            ("VPN — параметры Windows", "ms-settings:network-vpn", ""), ("Дисплей и масштаб", "ms-settings:display", ""), ("Ночной свет", "ms-settings:nightlight", ""),
            ("Звук", "ms-settings:sound", ""), ("Уведомления Windows", "ms-settings:notifications", ""), ("Питание и батарея", "ms-settings:powersleep", ""),
            ("Память и очистка диска", "ms-settings:storagesense", ""), ("Установленные приложения", "ms-settings:appsfeatures", ""),
            ("Приложения по умолчанию", "ms-settings:defaultapps", ""), ("Автозагрузка приложений", "ms-settings:startupapps", ""),
            ("Персонализация", "ms-settings:personalization", ""), ("Фон рабочего стола", "ms-settings:personalization-background", ""),
            ("Цвета и тёмная тема", "ms-settings:colors", ""), ("Темы", "ms-settings:themes", ""), ("Панель задач", "ms-settings:taskbar", ""),
            ("Учётная запись", "ms-settings:yourinfo", ""), ("Варианты входа, PIN и пароль", "ms-settings:signinoptions", ""),
            ("Дата и время", "ms-settings:dateandtime", ""), ("Язык и регион", "ms-settings:regionlanguage", ""), ("Клавиатура и ввод", "ms-settings:typing", ""),
            ("Мышь", "ms-settings:mousetouchpad", ""), ("Сенсорная панель", "ms-settings:devices-touchpad", ""), ("Принтеры и сканеры", "ms-settings:printers", ""),
            ("Центр обновления Windows", "ms-settings:windowsupdate", ""), ("Безопасность Windows и антивирус", "windowsdefender:", ""),
            ("Конфиденциальность", "ms-settings:privacy", ""), ("Камера — разрешения", "ms-settings:privacy-webcam", ""), ("Микрофон — разрешения", "ms-settings:privacy-microphone", ""),
            ("Местоположение", "ms-settings:privacy-location", ""), ("О системе", "ms-settings:about", ""), ("Восстановление и сброс", "ms-settings:recovery", ""),
            ("Игровой режим", "ms-settings:gaming-gamemode", ""), ("Специальные возможности", "ms-settings:easeofaccess", ""), ("Буфер обмена Windows", "ms-settings:clipboard", ""),
            ("Многозадачность и окна", "ms-settings:multitasking", ""), ("Связь с телефоном", "ms-settings:mobile-devices", ""), ("Панель управления", "control", ""),
            ("Диспетчер устройств", "devmgmt.msc", ""), ("Управление дисками", "diskmgmt.msc", ""), ("Службы Windows", "services.msc", ""),
            ("Редактор реестра", "regedit", ""), ("Диспетчер задач", "taskmgr", ""), ("Командная строка", "cmd", ""), ("PowerShell", "powershell", ""),
            ("Терминал Windows", "wt", ""), ("Корзина", "shell:RecycleBinFolder", ""), ("Загрузки", "shell:Downloads", ""), ("Документы", "shell:Personal", ""),
        };

        static readonly (string Name, string Key)[] IslandSections =
        {
            ("Внешний вид острова", "appearance"), ("Музыка и тексты песен", "music"), ("Интеграции и сервисы", "hub"), ("VPN в острове", "vpn"),
            ("Звуки N-Island", "sounds"), ("Язык N-Island", "language"), ("ИИ — провайдеры и ключи", "ai"), ("Кастомизация острова", "custom"),
            ("Прозрачность", "opacity"), ("Звонки и уведомления", "calls"), ("Погода и часы", "weather"), ("Горячие клавиши и HUD", "hud"), ("О программе и обновления", "about"),
        };
    }

    /// <summary>Перевод единиц: «10 км в мили», «72 f в c», «5 kg to lb», «1.5 гб в мб».</summary>
    public static class Units
    {
        static readonly Dictionary<string, (string Kind, double Factor, string Name)> U = new(StringComparer.OrdinalIgnoreCase)
        {
            ["мм"] = ("len", 0.001, "мм"), ["mm"] = ("len", 0.001, "мм"), ["см"] = ("len", 0.01, "см"), ["cm"] = ("len", 0.01, "см"),
            ["м"] = ("len", 1, "м"), ["m"] = ("len", 1, "м"), ["км"] = ("len", 1000, "км"), ["km"] = ("len", 1000, "км"),
            ["дюйм"] = ("len", 0.0254, "дюйм"), ["дюймов"] = ("len", 0.0254, "дюйм"), ["дюйма"] = ("len", 0.0254, "дюйм"), ["in"] = ("len", 0.0254, "дюйм"), ["inch"] = ("len", 0.0254, "дюйм"),
            ["фут"] = ("len", 0.3048, "фут"), ["футов"] = ("len", 0.3048, "фут"), ["ft"] = ("len", 0.3048, "фут"), ["ярд"] = ("len", 0.9144, "ярд"), ["yd"] = ("len", 0.9144, "ярд"),
            ["миля"] = ("len", 1609.344, "миль"), ["мили"] = ("len", 1609.344, "миль"), ["миль"] = ("len", 1609.344, "миль"), ["mi"] = ("len", 1609.344, "миль"), ["mile"] = ("len", 1609.344, "миль"), ["miles"] = ("len", 1609.344, "миль"),
            ["г"] = ("mass", 0.001, "г"), ["g"] = ("mass", 0.001, "г"), ["кг"] = ("mass", 1, "кг"), ["kg"] = ("mass", 1, "кг"), ["т"] = ("mass", 1000, "т"), ["t"] = ("mass", 1000, "т"),
            ["фунт"] = ("mass", 0.45359237, "фунт"), ["фунтов"] = ("mass", 0.45359237, "фунт"), ["lb"] = ("mass", 0.45359237, "фунт"), ["lbs"] = ("mass", 0.45359237, "фунт"),
            ["унция"] = ("mass", 0.0283495, "унц."), ["oz"] = ("mass", 0.0283495, "унц."),
            ["мл"] = ("vol", 0.001, "мл"), ["ml"] = ("vol", 0.001, "мл"), ["л"] = ("vol", 1, "л"), ["l"] = ("vol", 1, "л"), ["галлон"] = ("vol", 3.78541, "галл."), ["gal"] = ("vol", 3.78541, "галл."),
            ["км/ч"] = ("speed", 1 / 3.6, "км/ч"), ["kmh"] = ("speed", 1 / 3.6, "км/ч"), ["м/с"] = ("speed", 1, "м/с"), ["mph"] = ("speed", 0.44704, "миль/ч"), ["узлов"] = ("speed", 0.514444, "узл."), ["kn"] = ("speed", 0.514444, "узл."),
            ["кб"] = ("data", 1024, "КБ"), ["kb"] = ("data", 1024, "КБ"), ["мб"] = ("data", 1048576, "МБ"), ["mb"] = ("data", 1048576, "МБ"),
            ["гб"] = ("data", 1073741824, "ГБ"), ["gb"] = ("data", 1073741824, "ГБ"), ["тб"] = ("data", 1099511627776, "ТБ"), ["tb"] = ("data", 1099511627776, "ТБ"),
            ["c"] = ("temp", 0, "°C"), ["с"] = ("temp", 0, "°C"), ["°c"] = ("temp", 0, "°C"), ["цельсий"] = ("temp", 0, "°C"),
            ["f"] = ("temp", 1, "°F"), ["ф"] = ("temp", 1, "°F"), ["°f"] = ("temp", 1, "°F"), ["фаренгейт"] = ("temp", 1, "°F"), ["k"] = ("temp", 2, "K"), ["к"] = ("temp", 2, "K"),
        };

        static readonly Regex Rx = new(@"^\s*(-?\d+(?:[.,]\d+)?)\s*([^\d\s]+?)\s*(?:в|to|in|->|=)\s*([^\d\s]+)\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static string Convert(string q)
        {
            var m = Rx.Match(q ?? "");
            if (!m.Success || !U.TryGetValue(m.Groups[2].Value, out var a) || !U.TryGetValue(m.Groups[3].Value, out var b) || a.Kind != b.Kind) return null;
            double v = double.Parse(m.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
            double r;
            if (a.Kind == "temp")
            {
                double c = a.Factor switch { 1 => (v - 32) * 5 / 9, 2 => v - 273.15, _ => v };
                r = b.Factor switch { 1 => c * 9 / 5 + 32, 2 => c + 273.15, _ => c };
            }
            else r = v * a.Factor / b.Factor;
            string F(double x) => Math.Abs(x) >= 1000 ? x.ToString("N0", Fmt.Ru) : x.ToString(Math.Abs(x) >= 10 ? "0.##" : "0.####", Fmt.Ru);
            return $"{F(v)} {a.Name} = {F(r)} {b.Name}";
        }
    }
}
