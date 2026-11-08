using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using NIsland.Core;
using NIsland.Services;

namespace NIsland.Pages
{
    static class Txt
    {
        /// <summary>Тонкая светлая кромка сверху — «стеклянные» карточки.</summary>
        public static readonly Brush Rim = Freeze(new LinearGradientBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x06, 0xFF, 0xFF, 0xFF), 90));
        static Brush Freeze(Brush b) { b.Freeze(); return b; }

        /// <summary>Текст, который можно выделить и скопировать, но без рамок поля ввода.</summary>
        public static TextBox Selectable(string text, double size = 12.5, Brush fg = null)
        {
            var t = new TextBox
            {
                Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, FontSize = size, BorderThickness = new Thickness(0),
                Background = Brushes.Transparent, Foreground = fg ?? Brushes.White, CaretBrush = Brushes.White, Padding = new Thickness(0),
            };
            var tpl = new ControlTemplate(typeof(TextBox));
            tpl.VisualTree = new FrameworkElementFactory(typeof(ScrollViewer)) { Name = "PART_ContentHost" };
            t.Template = tpl;
            t.SelectionBrush = UI.Res("AccentBrush");
            return t;
        }

        public static Border Tile(string glyph, string label, Action click, string tip = null, bool horizontal = false)
        {
            FrameworkElement content = horizontal
                ? UI.Row(UI.Icon(glyph, 12), UI.Text(label, 10.5).M(5, 0, 0, 0))
                : UI.Col(UI.Icon(glyph, 15), new TextBlock { Text = label, FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
            content.HorizontalAlignment = HorizontalAlignment.Center;
            content.VerticalAlignment = VerticalAlignment.Center;
            var b = new Border
            {
                CornerRadius = new CornerRadius(10), Margin = new Thickness(2), Background = UI.Res("CardBrush"), Cursor = Cursors.Hand,
                Padding = new Thickness(4, horizontal ? 5 : 6, 4, horizontal ? 5 : 6), ToolTip = tip, Child = content,
                BorderBrush = Rim, BorderThickness = new Thickness(1),
            };
            b.MouseEnter += (_, __) => b.Background = UI.Res("CardHoverBrush");
            b.MouseLeave += (_, __) => b.Background = UI.Res("CardBrush");
            b.MouseLeftButtonUp += (_, e) => { click(); e.Handled = true; };
            return b;
        }

        public static string ClipText()
        {
            try { return Clipboard.ContainsText() ? Clipboard.GetText() : ""; } catch { return ""; }
        }
    }

    // =====================================================================
    //  ИИ-чат
    // =====================================================================
    public class AiPage : PageBase
    {
        static List<AiMessage> History => Ai.Chat;
        readonly StackPanel _chat = new();
        readonly ScrollViewer _scroll;
        readonly TextBox _input = UI.Box("Спроси что угодно… (Enter — отправить, Shift+Enter — новая строка)");
        readonly Button _send, _prov, _persona;
        readonly StackPanel _actions = new() { Orientation = Orientation.Horizontal };
        CancellationTokenSource _cts;
        TextBox _live;
        readonly StringBuilder _pending = new();
        readonly DispatcherTimer _flush = new() { Interval = TimeSpan.FromMilliseconds(60) };

        // чат живёт в обычной ширине острова — не раздувает его
        public override double ExtraWidth => 0;

        public AiPage()
        {
            _scroll = new ScrollViewer { Content = _chat, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            _input.AcceptsReturn = true;
            _input.MaxHeight = 60;
            _input.TextWrapping = TextWrapping.Wrap;
            _input.PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0) { e.Handled = true; Send(_input.Text); }
            };
            _send = UI.IconBtn("", () => { if (_cts != null) _cts.Cancel(); else Send(_input.Text); }, 14, 32, "Отправить / остановить");
            _prov = UI.Pill("", CycleProvider, false, "Нейросеть — клик переключает, настройки → ИИ");
            _persona = UI.Pill("", CyclePersona, false, "Роль ассистента");
            foreach (var b in new[] { _prov, _persona }) { b.Padding = new Thickness(9, 3, 9, 3); b.FontSize = 10.5; b.Margin = new Thickness(0, 0, 4, 0); }

            var actionsScroll = new ScrollViewer { Content = _actions, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
            actionsScroll.PreviewMouseWheel += (_, e) => { actionsScroll.ScrollToHorizontalOffset(actionsScroll.HorizontalOffset - e.Delta / 2.0); e.Handled = true; };
            var bar = UI.Grid("a,a,*,a,a").Add(_prov, _persona.At(0, 1), actionsScroll.At(0, 2).M(2, 0, 4, 0),
                UI.IconBtn("", () => App.ShowSettings("ai"), 12, 26, "Настройки ИИ").At(0, 3),
                UI.IconBtn("", NewChat, 12, 26, "Новый чат").At(0, 4));
            var input = UI.Grid("*,a").Add(_input, _send.At(0, 1).M(4, 0, 0, 0));

            Content = UI.Grid(null, "*,a,a").M(8, 0, 8, 0).Add(_scroll, bar.At(1, 0).M(0, 3, 0, 3), input.At(2, 0));
            _flush.Tick += (_, __) => Flush();
            // сообщения из Telegram-бота появляются и здесь
            Ai.ChatChanged += () => Dispatcher.BeginInvoke(new Action(() => { if (_cts == null) Rebuild(); }));
        }

        /// <summary>Вопрос, который нужно отправить при открытии (ключ запуска --ai).</summary>
        public static string PendingPrompt;

        public override void OnShown()
        {
            RenderBar();
            if (_chat.Children.Count == 0) Rebuild();
            if (PendingPrompt != null) { var q = PendingPrompt; PendingPrompt = null; Send(q); }
        }

        void RenderBar()
        {
            var p = Ai.Current;
            bool ready = Ai.Ready(p, out _);
            _prov.Content = (ready ? "● " : "○ ") + p.Name.Split(' ')[0];
            _prov.Foreground = ready ? Brushes.White : UI.Dim;
            _persona.Content = Ai.Personas.FirstOrDefault(x => x.Key == S.AiPersona)?.Name ?? "Обычный";
            _actions.Children.Clear();
            foreach (var a in Ai.Actions())
            {
                var act = a;
                var b = UI.Pill(a.Name, () => RunAction(act), false, "Применить к тексту из буфера обмена");
                b.Padding = new Thickness(8, 3, 8, 3);
                b.FontSize = 10.5;
                b.Margin = new Thickness(0, 0, 4, 0);
                b.Background = UI.Hex("#1EFFFFFF");
                _actions.Children.Add(b);
            }
        }

        void CycleProvider()
        {
            var all = Ai.All.ToList();
            int i = all.FindIndex(p => p.Key == S.AiProvider);
            S.AiProvider = all[(i + 1) % all.Count].Key;
            RenderBar();
        }

        void CyclePersona()
        {
            int i = Array.FindIndex(Ai.Personas, x => x.Key == S.AiPersona);
            S.AiPersona = Ai.Personas[(i + 1) % Ai.Personas.Length].Key;
            RenderBar();
        }

        void NewChat()
        {
            _cts?.Cancel();
            Ai.ClearChat();
            Rebuild();
        }

        void Rebuild()
        {
            _chat.Children.Clear();
            if (History.Count == 0)
            {
                var p = Ai.Current;
                _chat.Children.Add(UI.Text(Ai.Ready(p, out var why)
                    ? $"{p.Name} · {Ai.Model(p)}. Спроси что-нибудь или нажми быстрое действие — оно возьмёт текст из буфера обмена."
                    : why, 12, false, UI.Dim).M(2, 4, 0, 0));
                ((TextBlock)_chat.Children[0]).TextWrapping = TextWrapping.Wrap;
                return;
            }
            List<AiMessage> copy;
            lock (History) copy = History.ToList();
            foreach (var m in copy.Skip(Math.Max(0, copy.Count - 60))) AddBubble(m.Role, m.Text, m.Source);
            _scroll.ScrollToEnd();
        }

        TextBox AddBubble(string role, string text, string source = null)
        {
            bool me = role == "user";
            var t = Txt.Selectable(text, Math.Clamp(S.AiFontSize, 10, 18));
            var b = new Border
            {
                CornerRadius = me ? new CornerRadius(14, 14, 4, 14) : new CornerRadius(14, 14, 14, 4),
                Padding = new Thickness(10, 5, 10, 6), MaxWidth = 470,
                HorizontalAlignment = me ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                Background = me ? UI.Res("AccentBrush") : UI.Hex("#22FFFFFF"),
                BorderBrush = me ? null : UI.Hex("#26FFFFFF"), BorderThickness = new Thickness(me ? 0 : 1), Child = t,
            };
            if (me) t.SetResourceReference(Control.ForegroundProperty, "AccentTextBrush");   // светлый акцент — тёмный текст
            if (me)
            {
                b.Margin = new Thickness(50, 2, 0, 2);
                if (source == "tg") b.ToolTip = "Из Telegram";
                _chat.Children.Add(source == "tg" ? UI.Col(UI.Text("📱 Telegram", 9.5, false, UI.Dim).HA(HorizontalAlignment.Right), b) : b);
            }
            else
            {
                // значок ИИ слева от ответа
                var avatar = new Border
                {
                    Width = 20, Height = 20, CornerRadius = new CornerRadius(10), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 3, 6, 0),
                    Background = new LinearGradientBrush(Color.FromRgb(0xBF, 0x5A, 0xF2), Color.FromRgb(0x0A, 0x84, 0xFF), 45),
                    Child = UI.Icon("", 10, Brushes.White),
                };
                var copyBtn = UI.IconBtn("", () => { try { ClipboardService.I.CopyBack(t.Text); App.Island.Toast("", "Скопировано", null); } catch { } }, 10, 22, "Копировать ответ");
                copyBtn.Opacity = 0.0;
                var row = UI.Grid("a,*,a").M(0, 2, 6, 2).Add(avatar, b.At(0, 1), copyBtn.At(0, 2).M(2, 0, 0, 0));
                copyBtn.VerticalAlignment = VerticalAlignment.Bottom;
                row.MouseEnter += (_, __) => copyBtn.Opacity = 0.9;
                row.MouseLeave += (_, __) => copyBtn.Opacity = 0.0;
                _chat.Children.Add(row);
            }
            return t;
        }

        void RunAction(AiAction a)
        {
            var clip = Txt.ClipText();
            if (a.Prompt.Contains("{clip}") && string.IsNullOrWhiteSpace(clip)) { App.Island.Toast("", "Буфер пуст", "Скопируй текст и нажми ещё раз"); return; }
            Send(a.Prompt.Replace("{clip}", clip));
        }

        async void Send(string text)
        {
            text = text?.Trim();
            if (string.IsNullOrEmpty(text) || _cts != null) return;
            var p = Ai.Current;
            // переписку не стираем — просто подсказываем, что настроить
            if (!Ai.Ready(p, out var why)) { App.Island.Toast("", "ИИ не настроен", why); return; }
            _input.Text = "";
            if (History.Count == 0) _chat.Children.Clear();
            Ai.AddToChat(new AiMessage("user", text), notify: false);
            AddBubble("user", text.Length > 600 ? text[..600] + "…" : text);
            _live = AddBubble("assistant", "…");
            _scroll.ScrollToEnd();
            _send.Content = ""; // стоп
            _cts = new CancellationTokenSource();
            _pending.Clear();
            _flush.Start();
            string answer;
            bool failed = false;
            try
            {
                List<AiMessage> hist;
                lock (History) hist = History.ToList();
                answer = await Ai.Ask(hist, d => { lock (_pending) { if (d == "") { _pending.Clear(); _restart = true; } else _pending.Append(d); } }, _cts.Token);
                answer = Ai.StripThink(answer);
                if (string.IsNullOrWhiteSpace(answer)) answer = "(пустой ответ)";
            }
            catch (OperationCanceledException) { answer = (_live.Text == "…" ? "" : _live.Text) + " ⏹"; }
            catch (Exception e) { answer = "⚠ " + e.Message; failed = true; }
            _flush.Stop();
            Flush();
            _live.Text = answer;
            // ошибку в историю не кладём: иначе модель получит её как свой ответ, а вопрос можно задать заново
            if (failed) { lock (History) if (History.Count > 0 && History[^1].Role == "user") History.RemoveAt(History.Count - 1); }
            else Ai.AddToChat(new AiMessage("assistant", answer), notify: false);
            if (!failed && Ai.LastAnswered != null && Ai.LastAnswered != p.Name)
            {
                // основной провайдер не ответил — говорим прямо, кто ответил вместо него
                _live.ToolTip = I18n.T("Ответил: ") + Ai.LastAnswered;
                App.Island.Toast("", "Ответил запасной ИИ", Ai.LastAnswered);
            }
            _cts = null;
            _send.Content = "";
            _scroll.ScrollToEnd();
        }

        bool _restart;

        void Flush()
        {
            string add;
            bool restart;
            lock (_pending) { add = _pending.ToString(); _pending.Clear(); restart = _restart; _restart = false; }
            if (_live == null) return;
            if (restart) _live.Text = "…";   // основной провайдер не ответил — отвечает запасной
            if (add.Length == 0) return;
            _live.Text = (_live.Text == "…" ? "" : _live.Text) + add;
            _scroll.ScrollToEnd();
        }
    }

    // =====================================================================
    //  Обслуживание ноутбука
    // =====================================================================
    public class MaintPage : PageBase
    {
        readonly Grid _home;
        readonly ContentControl _view = new();
        readonly UniformGrid _stats = new() { Columns = 4 };
        readonly TextBlock _status = UI.Text("", 11.5);
        readonly StackPanel _confirm = new() { Orientation = Orientation.Horizontal };
        BatteryHealth _bat;

        public MaintPage()
        {
            _status.Foreground = UI.Dim;
            var tiles = new UniformGrid { Columns = 8 };
            tiles.Children.Add(Txt.Tile("", "Очистка", ScanTemp, "Найти и удалить старые временные файлы"));
            tiles.Children.Add(Txt.Tile("", "Корзина", RecycleBin, "Размер корзины и очистка"));
            tiles.Children.Add(Txt.Tile("", "Память", Ram, "Ускорить: выгрузить неиспользуемое из оперативной памяти"));
            tiles.Children.Add(Txt.Tile("", "Сброс DNS", Dns, "Очистить кеш DNS — помогает, когда сайты не открываются"));
            tiles.Children.Add(Txt.Tile("", "Автозапуск", () => Show(StartupView()), "Что запускается вместе с Windows"));
            tiles.Children.Add(Txt.Tile("", "Процессы", () => Show(ProcessView()), "Кто ест память — и закрыть"));
            tiles.Children.Add(Txt.Tile("", "Питание", () => Show(PowerView()), "Схема электропитания"));
            tiles.Children.Add(Txt.Tile("", "Центр", () => { App.Island.Collapse(true); App.ShowCenter(); },
                "Центр управления ноутбуком: глубокая очистка, защита от слежки, питание, исправления (Ctrl+Alt+L)"));
            var foot = UI.Grid("*,a").Add(_status, _confirm.At(0, 1));
            _home = UI.Grid(null, "a,a,*").Add(_stats, tiles.At(1, 0).M(0, 4, 0, 0), foot.At(2, 0).M(4, 4, 0, 0));
            _view.Content = _home;
            Content = new Grid { Margin = new Thickness(8, 0, 8, 0) }.Add(_view);
        }

        public override async void OnShown()
        {
            Show(_home);
            RenderStats();
            if (SystemService.I.HasBattery && _bat == null)
            {
                _bat = await Maint.Battery();
                if (Visible) RenderStats();
            }
        }

        void Show(UIElement v) { _view.Content = v; }

        UIElement Back(string title, UIElement body)
        {
            var head = UI.Row(UI.IconBtn("", () => Show(_home), 11, 24, "Назад"), UI.Text(title, 12.5, true).M(6, 0, 0, 0));
            var scroll = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 4, 0, 0) };
            return UI.Grid(null, "a,*").Add(head, scroll.At(1, 0));
        }

        Border Stat(string title, string value, string sub, double? frac, Action click = null, string tip = null)
        {
            var col = UI.Col(UI.Text(title, 10.5, false, UI.Dim), UI.Text(value, 14, true), UI.Text(sub, 10.5, false, UI.Dim));
            if (frac is double f)
            {
                var fill = new Border { Height = 4, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left,
                    Background = f > 0.9 ? UI.Hex("#FF453A") : f > 0.75 ? UI.Hex("#FF9F0A") : UI.Res("AccentBrush"), Width = 0 };
                var track = new Grid { Height = 4, Margin = new Thickness(0, 4, 0, 0) }.Add(new Border { CornerRadius = new CornerRadius(2), Background = UI.Hex("#22FFFFFF") }, fill);
                track.SizeChanged += (_, e) => fill.Width = e.NewSize.Width * Math.Clamp(f, 0, 1);
                col.Children.Add(track);
            }
            var b = new Border { CornerRadius = new CornerRadius(12), Background = UI.Res("CardBrush"), Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(2), Child = col, ToolTip = tip,
                BorderBrush = Txt.Rim, BorderThickness = new Thickness(1) };
            if (click != null) { b.Cursor = Cursors.Hand; b.MouseLeftButtonUp += (_, e) => { click(); e.Handled = true; }; }
            return b;
        }

        void RenderStats()
        {
            _stats.Children.Clear();
            var c = Maint.Disks().FirstOrDefault(d => d.Name.StartsWith("C", StringComparison.OrdinalIgnoreCase)) ?? Maint.Disks().FirstOrDefault();
            if (c != null)
                _stats.Children.Add(Stat($"Диск {c.Name}", $"{Maint.Size(c.Free)} своб.", $"из {Maint.Size(c.Total)}", 1 - c.Free / (double)c.Total, () => Show(DisksView()), "Все диски и их здоровье"));
            long total = Maint.TotalRam(), free = Maint.AvailableRam();
            _stats.Children.Add(Stat("Память", $"{Maint.Size(total - free)} занято", $"свободно {Maint.Size(free)}", total > 0 ? 1 - free / (double)total : 0));
            if (_bat != null && _bat.Design > 0)
                _stats.Children.Add(Stat("Батарея", $"здоровье {_bat.Percent}%", _bat.Cycles > 0 ? $"{_bat.Cycles} циклов зарядки" : $"{_bat.Full / 1000.0:0.#} из {_bat.Design / 1000.0:0.#} Вт·ч", null,
                    null, $"Ёмкость: {_bat.Full} мВт·ч из заводских {_bat.Design} мВт·ч"));
            else
                _stats.Children.Add(Stat("Батарея", SystemService.I.HasBattery ? $"{SystemService.I.BatteryPercent}%" : "нет", SystemService.I.HasBattery ? "проверяю износ…" : "питание от сети", null));
            var up = Maint.Uptime;
            _stats.Children.Add(Stat("Без перезагрузки", up.TotalDays >= 1 ? $"{(int)up.TotalDays} д {up.Hours} ч" : $"{up.Hours} ч {up.Minutes} мин",
                up.TotalDays >= 7 ? "пора перезагрузить" : "всё в порядке", null, () => Show(InfoView()), "Сведения о системе и Wi-Fi"));
        }

        void Ask(string text, string button, Func<Task> action)
        {
            _status.Text = text;
            _confirm.Children.Clear();
            var yes = UI.Pill(button, async () => { _confirm.Children.Clear(); await action(); }, true);
            var no = UI.Pill("Отмена", () => { _confirm.Children.Clear(); _status.Text = ""; });
            foreach (var b in new[] { yes, no }) { b.Padding = new Thickness(10, 3, 10, 3); b.FontSize = 11; b.Margin = new Thickness(4, 0, 0, 0); }
            _confirm.Children.Add(yes);
            _confirm.Children.Add(no);
        }

        async void ScanTemp()
        {
            _confirm.Children.Clear();
            _status.Text = "Ищу временные файлы…";
            var (bytes, files) = await Maint.ScanTemp();
            if (files == 0) { _status.Text = "Временных файлов старше " + S.TempAgeDays + " дн. нет ✓"; return; }
            Ask($"Найдено {Maint.Size(bytes)} ({files} файлов) во временных папках", "Удалить", async () =>
            {
                _status.Text = "Удаляю…";
                var (freed, del, skip) = await Maint.CleanTemp();
                _status.Text = $"Освобождено {Maint.Size(freed)} · удалено {del}" + (skip > 0 ? $" · {skip} заняты и пропущены" : "");
                RenderStats();
            });
        }

        void RecycleBin()
        {
            _confirm.Children.Clear();
            var (bytes, items) = Maint.RecycleBin();
            if (items == 0) { _status.Text = "Корзина пуста ✓"; return; }
            Ask($"В корзине {Maint.Size(bytes)} ({items} объектов). Удалить навсегда?", "Очистить", () =>
            {
                _status.Text = Maint.EmptyRecycleBin() ? $"Корзина очищена, освобождено {Maint.Size(bytes)}" : "Не удалось очистить корзину";
                RenderStats();
                return Task.CompletedTask;
            });
        }

        async void Ram()
        {
            _confirm.Children.Clear();
            _status.Text = "Освобождаю память…";
            var freed = await Maint.OptimizeRam();
            _status.Text = $"Свободно стало больше на {Maint.Size(freed)}";
            RenderStats();
        }

        void Dns()
        {
            _confirm.Children.Clear();
            _status.Text = Maint.FlushDns() ? "Кеш DNS очищен ✓" : "Не удалось очистить кеш DNS";
        }

        UIElement StartupView()
        {
            var list = new StackPanel();
            foreach (var it in Maint.Startup())
            {
                var item = it;
                var sw = new CheckBox { Style = UI.St("Switch"), IsChecked = it.Enabled, IsEnabled = it.CanToggle };
                sw.Click += (_, __) => { Maint.SetStartup(item, sw.IsChecked == true); };
                var src = it.Source switch { "user" => "для меня", "machine" => "для всех · только просмотр", _ => "папка автозагрузки" };
                list.Children.Add(UI.Grid("*,a").M(2, 2, 6, 2).Add(
                    UI.Col(UI.Text(it.Name, 12.5, true), UI.Text($"{src} · {it.Command}", 10.5, false, UI.Faint)), sw.At(0, 1)));
            }
            if (list.Children.Count == 0) list.Children.Add(UI.Text("В автозагрузке ничего нет", 12, false, UI.Dim));
            return Back("Автозагрузка — выключенное не запустится при входе в Windows", list);
        }

        UIElement ProcessView()
        {
            var list = new StackPanel();
            foreach (var p in Maint.TopProcesses(10))
            {
                var proc = p;
                Button kill = null;
                kill = UI.Pill("Закрыть", () =>
                {
                    if ((string)kill.Content == "Закрыть") { kill.Content = "Точно?"; kill.Background = UI.Hex("#FF453A"); return; }
                    int n = Maint.Kill(proc.Name);
                    App.Island.Toast("", $"Закрыто: {proc.Name}", $"{n} процесс(ов)");
                    Show(ProcessView());
                });
                kill.Padding = new Thickness(9, 2, 9, 2);
                kill.FontSize = 10.5;
                list.Children.Add(UI.Grid("*,a,a").M(2, 2, 6, 2).Add(
                    UI.Text(proc.Name + (proc.Count > 1 ? $" ×{proc.Count}" : ""), 12.5, true),
                    UI.Text(Maint.Size(proc.Bytes), 12, false, UI.Dim).At(0, 1).M(0, 0, 10, 0), kill.At(0, 2)));
            }
            return Back("Больше всего памяти (несохранённые данные в закрытой программе пропадут)", list);
        }

        UIElement PowerView()
        {
            var list = new StackPanel();
            foreach (var p in Maint.PowerPlans())
            {
                var plan = p;
                var row = Txt.Tile(p.Active ? "" : "", p.Name + (p.Active ? " — сейчас" : ""), () => { Maint.SetPowerPlan(plan.Guid); Show(PowerView()); }, null, true);
                row.HorizontalAlignment = HorizontalAlignment.Left;
                if (p.Active) { row.Background = UI.Res("AccentBrush"); row.OnAccent(); }
                list.Children.Add(row);
            }
            list.Children.Add(Txt.Tile("", "Открыть настройки питания Windows", () => SystemService.Run("ms-settings:powersleep"), null, true).HA(HorizontalAlignment.Left));
            return Back("Схема электропитания", list);
        }

        UIElement ToolsView()
        {
            var grid = new UniformGrid { Columns = 6 };
            foreach (var (name, glyph, target) in Maint.WinTools)
            {
                var t = target;
                grid.Children.Add(Txt.Tile(glyph, name, () => { SystemService.Run(t); App.Island.Collapse(true); }, t));
            }
            return Back("Инструменты Windows", grid);
        }

        UIElement DisksView()
        {
            var list = new StackPanel();
            foreach (var d in Maint.Disks())
                list.Children.Add(UI.Text($"{d.Name} {d.Label} — свободно {Maint.Size(d.Free)} из {Maint.Size(d.Total)} ({100 - 100 * d.Free / Math.Max(1, d.Total)}% занято)", 12.5).M(2, 2, 0, 2));
            var health = UI.Text("Проверяю состояние дисков…", 12, false, UI.Dim).M(2, 6, 0, 0);
            list.Children.Add(health);
            _ = Fill();
            async Task Fill()
            {
                var h = await Maint.DiskHealth();
                health.Text = h.Count == 0 ? "Состояние дисков недоступно" : string.Join("\n", h);
                health.TextWrapping = TextWrapping.Wrap;
            }
            return Back("Диски", list);
        }

        UIElement InfoView()
        {
            var up = Maint.Uptime;
            var list = UI.Col(
                UI.Text(Maint.WindowsVersion, 12.5, true),
                UI.Text($"Работает без перезагрузки: {(int)up.TotalDays} д {up.Hours} ч {up.Minutes} мин", 12, false, UI.Dim),
                UI.Text($"Процессор: {Environment.ProcessorCount} потоков · ОЗУ {Maint.Size(Maint.TotalRam())}", 12, false, UI.Dim));
            var wifi = UI.Text("Wi-Fi: проверяю…", 12, false, UI.Dim);
            list.Children.Add(wifi);
            _ = Fill();
            async Task Fill() { wifi.Text = "Wi-Fi: " + await Maint.WifiInfo(); }
            list.VerticalAlignment = VerticalAlignment.Top;
            return Back("Сведения о системе", list);
        }
    }

    // =====================================================================
    //  Инструменты (API + локальные)
    // =====================================================================
    public class ToolsPage : PageBase
    {
        readonly TextBox _in = UI.Box("Текст, адрес сайта или запрос — пусто = взять из буфера обмена");
        readonly TextBox _out = Txt.Selectable("Выбери инструмент ниже.", 12.5);
        readonly Image _qr = new() { Width = 96, Height = 96, Visibility = Visibility.Collapsed, Margin = new Thickness(8, 0, 0, 0) };
        int _req;

        readonly UniformGrid _tiles = new() { Columns = 7 };
        readonly StackPanel _cats = new() { Orientation = Orientation.Horizontal };
        string _cat = "net";

        // инструменты чуть выше остальных страниц — четыре категории
        public override double ExtraHeight => 30;

        record Tool(string Cat, string Glyph, string Name, Func<string, Task<string>> Run, string Tip);
        readonly List<Tool> _all = new();

        public ToolsPage()
        {
            _out.Foreground = UI.Dim;
            void T(string cat, string g, string n, Func<string, Task<string>> f, string tip) => _all.Add(new Tool(cat, g, n, f, tip));
            Task<string> R(string s) => Task.FromResult(s);
            // сеть
            T("net", "", "Скорость", _ => ApiTools.SpeedTest(), "Скорость интернета (Cloudflare)");
            T("net", "", "Мой IP", _ => ApiTools.PublicIp(), "Внешний IP, город и провайдер (ipwho.is)");
            T("net", "", "Пинг", q => ApiTools.PingHost(q), "Пинг адреса из поля (по умолчанию 1.1.1.1)");
            T("net", "", "IP-инфо", q => ApiTools2.IpInfo(Input(q)), "Где находится IP или домен");
            T("net", "", "DNS", q => ApiTools2.Dns(Input(q)), "DNS-записи домена (A, AAAA, MX, NS)");
            T("net", "", "Сайт", q => ApiTools.SiteCheck(Input(q)), "Проверить, отвечает ли сайт");
            T("net", "", "Ссылка", q => ApiTools.Shorten(Input(q)), "Сократить ссылку (is.gd)");
            // инфо
            T("info", "", "Крипта", _ => ApiTools.Crypto(), "Курсы криптовалют (CoinGecko)");
            T("info", "", "Курсы", _ => ApiTools2.Rates(), "Курсы валют к твоей валюте");
            T("info", "", "Вики", q => ApiTools.Wiki(Input(q)), "Кратко из Википедии");
            T("info", "", "Новости", _ => ApiTools2.News(), "Свежие новости (RSS, меняется в настройках)");
            T("info", "", "Факт", _ => ApiTools2.Fact(), "Случайный интересный факт");
            T("info", "", "Праздники", _ => ApiTools.Holidays(), "Ближайшие государственные праздники");
            T("info", "", "Воздух", _ => ApiTools.AirQuality(), "Качество воздуха в твоём городе");
            T("info", "", "Солнце", _ => ApiTools2.Sun(), "Восход, закат и длина дня");
            T("info", "", "Луна", _ => R(ApiTools2.Moon()), "Фаза луны");
            T("info", "", "Словарь", q => ApiTools2.Dictionary(Input(q)), "Значение английского слова");
            // текст
            T("text", "", "QR-код", q => Qr(Input(q)), "QR-код из текста или ссылки");
            T("text", "", "Пароль", _ => R(ApiTools.Password()), "Надёжный случайный пароль");
            T("text", "", "Хеш", q => R(ApiTools.Hash(Input(q))), "SHA-256 и MD5 текста");
            T("text", "", "Base64", q => R(ApiTools.Base64(Input(q))), "Закодировать/раскодировать Base64");
            T("text", "", "UUID", _ => R(ApiTools.Uuid()), "Случайный идентификатор");
            T("text", "", "Регистр", q => R(ApiTools2.Case(Input(q))), "ВЕРХНИЙ, нижний, Каждое Слово");
            T("text", "", "Транслит", q => R(ApiTools2.Translit(Input(q))), "Кириллица ⇄ латиница");
            T("text", "", "Статистика", q => R(ApiTools2.Stats(Input(q))), "Символы, слова, время чтения");
            T("text", "", "JSON", q => R(ApiTools2.Json(Input(q))), "Красиво отформатировать JSON");
            // конвертеры
            T("conv", "", "Единицы", q => R(ApiTools2.ConvertUnits(Input(q))), "«10 km в mi», «5 кг в фунты», «100 c в f»");
            T("conv", "", "Валюта", q => ConvertMoney(Input(q)), "«100 usd», «50 евро в рубли»");
            T("conv", "", "Unix-время", q => R(ApiTools2.Unix(q)), "Unix ⇄ дата (пусто — сейчас)");
            T("conv", "", "Цвет", q => R(ApiTools2.Color(Input(q))), "HEX ⇄ RGB ⇄ HSL");
            T("conv", "", "Пипетка", _ => { App.Island.Collapse(true); ColorPicker.I.Start(); return R("Кликни на любой цвет экрана — HEX скопируется"); }, "Взять цвет с экрана");

            foreach (var (key, name) in new[] { ("net", "Сеть"), ("info", "Инфо"), ("text", "Текст"), ("conv", "Конвертеры") })
            {
                var k = key;
                var rb = new RadioButton { Style = UI.St("Segment"), Content = name, GroupName = "toolcat", IsChecked = key == _cat, FontSize = 11 };
                rb.Checked += (_, __) => { _cat = k; RenderTiles(); };
                _cats.Children.Add(rb);
            }
            var catBox = new Border { Background = UI.Hex("#22FFFFFF"), CornerRadius = new CornerRadius(8), Padding = new Thickness(2), Child = _cats, HorizontalAlignment = HorizontalAlignment.Left };

            var copy = UI.IconBtn("", () =>
            {
                if (_out.Text != "") { ClipboardService.I.CopyBack(_out.Text); App.Island.Toast("", "Скопировано", null); }
            }, 12, 26, "Копировать результат");
            var result = new Border
            {
                CornerRadius = new CornerRadius(10), Background = UI.Hex("#10FFFFFF"), Padding = new Thickness(10, 6, 6, 6),
                BorderBrush = Txt.Rim, BorderThickness = new Thickness(1),
                Child = UI.Grid("*,a,a").Add(new ScrollViewer { Content = _out, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, _qr.At(0, 1), copy.At(0, 2).VA(VerticalAlignment.Top)),
            };
            _in.KeyDown += (_, e) =>
            {
                if (e.Key != Key.Enter) return;
                var t = _all.FirstOrDefault(x => x.Cat == _cat);
                if (t != null) Run(t.Name, t.Run);
            };
            var top = UI.Grid("a,*").Add(catBox, _in.At(0, 1).M(8, 0, 0, 0));
            Content = UI.Grid(null, "a,a,*").M(8, 0, 8, 0).Add(top, _tiles.At(1, 0).M(0, 4, 0, 4), result.At(2, 0));
            RenderTiles();
        }

        void RenderTiles()
        {
            _tiles.Children.Clear();
            foreach (var t in _all.Where(x => x.Cat == _cat))
            {
                var tool = t;
                _tiles.Children.Add(Txt.Tile(t.Glyph, t.Name, () => Run(tool.Name, tool.Run), t.Tip, true));
            }
        }

        static async Task<string> ConvertMoney(string q)
        {
            var c = Currency.Parse(q);
            if (c == null) return "Формат: «100 usd», «50 евро в рубли», «1000 грн в usd»";
            var v = await Currency.Convert(c.Value.Amount, c.Value.From, c.Value.To);
            return v is double d ? $"{Currency.Fmt(c.Value.Amount, c.Value.From)} ≈ {Currency.Fmt(d, c.Value.To)} (open.er-api.com)" : "Не удалось получить курс";
        }

        string Input(string q) => string.IsNullOrWhiteSpace(q) ? Txt.ClipText().Trim() : q.Trim();

        async Task<string> Qr(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "Введи текст или скопируй ссылку";
            _qr.Source = await ApiTools.Qr(text);
            _qr.Visibility = Visibility.Visible;
            return "QR-код для: " + (text.Length > 120 ? text[..120] + "…" : text) + "\nНаведи камеру телефона.";
        }

        async void Run(string name, Func<string, Task<string>> f)
        {
            int id = ++_req;
            if (name != "QR-код") { _qr.Visibility = Visibility.Collapsed; _qr.Source = null; }
            _out.Foreground = UI.Dim;
            _out.Text = name + ": выполняю…";
            try
            {
                var r = await f(_in.Text);
                if (id != _req) return;
                _out.Text = r;
                _out.Foreground = Brushes.White;
            }
            catch (Exception e)
            {
                if (id == _req) _out.Text = $"{name}: не получилось — {e.Message}";
            }
        }

        public override void OnHidden() { _qr.Source = null; }
    }
}
