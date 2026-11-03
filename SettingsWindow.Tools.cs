using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using NIsland.Core;
using NIsland.Pages;
using NIsland.Services;

namespace NIsland
{
    /// <summary>Разделы центра «Оптимизация» и «Журнал активности», общий блок переключателей и ИИ-объяснения.</summary>
    public partial class SettingsWindow
    {
        // ------------------------------------------------------------------
        //  Переключатели с резервной копией (защита от слежки и оптимизация)
        // ------------------------------------------------------------------

        UIElement[] ToggleRows(IEnumerable<PrivacyItem> items, TextBlock status)
        {
            var rows = new List<UIElement>();
            foreach (var item in items)
            {
                var it = item;
                var cb = new CheckBox { Style = UI.St("Switch"), IsChecked = Privacy.IsProtected(it), ToolTip = "Включено — настройка применена" };
                bool busy = false;
                async void Change(bool want)
                {
                    if (busy) return;
                    busy = true;
                    cb.IsEnabled = false;
                    status.Text = (want ? "Применяю: " : "Возвращаю: ") + it.Title + (it.Admin ? " — подтверди запрос Windows" : "");
                    status.Text = want ? await Privacy.Protect(new[] { it }) : await Privacy.Restore(new[] { it });
                    bool now = Privacy.IsProtected(it);
                    if (cb.IsChecked != now) cb.IsChecked = now;   // отказ в правах — переключатель возвращается назад
                    cb.IsEnabled = true;
                    busy = false;
                }
                cb.Checked += (_, __) => Change(true);
                cb.Unchecked += (_, __) => Change(false);
                rows.Add(Item(it.Title, it.Explain, it.Safety, cb, it.Effect, it.Admin));
            }
            return rows.ToArray();
        }

        // ------------------------------------------------------------------
        //  ИИ объясняет пункт простыми словами
        // ------------------------------------------------------------------

        static Window _aiWin;

        /// <summary>Небольшое окно, где выбранный в настройках ИИ объясняет пункт: что это, кому включать, риски и как вернуть.</summary>
        public static void Explain(string title, string explain, string effect = null)
        {
            var p = Ai.Current;
            if (!Ai.Ready(p, out var why)) { MessageBox.Show(I18n.T(why) + I18n.T("\n\nНастрой ИИ в «Настройки → ИИ» (бесплатный вариант работает без ключа)."), "N-Island"); return; }
            _aiWin?.Close();
            var lang = I18n.Current == "ru" ? "" : $" Ответь на языке: {I18n.Languages.First(l => l.Code == I18n.Current).Name}.";
            var prompt = $"Ты помощник внутри приложения N-Island для Windows. Объясни простыми словами для новичка пункт «{title}».\n" +
                         $"Описание в приложении: {explain}\n" + (effect != null ? $"Что изменится: {effect}\n" : "") +
                         "Расскажи коротко (5–8 предложений, без таблиц): что это и как работает; кому и когда это стоит включать; " +
                         "когда лучше не трогать и какие риски; как вернуть обратно." + lang;
            var text = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13.5, LineHeight = 20, Foreground = Brushes.White, Text = "Думаю…" };
            var cts = new CancellationTokenSource();
            var copy = UI.Pill("Копировать", () => ClipboardService.I.CopyBack(text.Text));
            Window w = null;
            var close = UI.Pill("Закрыть", () => w.Close(), true);
            var head = UI.Row(new TextBlock { Text = "✨", FontFamily = new FontFamily("Segoe UI Emoji"), FontSize = 15 }, UI.Text("ИИ объясняет: " + title, 14, true).M(8, 0, 0, 0));
            var body = new ScrollViewer { Content = text, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 12, 0, 12) };
            var foot = UI.Row(UI.Text(p.Name, 11.5, false, UI.Dim), copy.M(12, 0, 8, 0), close);
            foot.HorizontalAlignment = HorizontalAlignment.Right;
            w = new Window
            {
                Title = "N-Island — объяснение", Width = 520, Height = 420, WindowStyle = WindowStyle.None, AllowsTransparency = true,
                Background = Brushes.Transparent, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Topmost = true, ShowInTaskbar = false, FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
                Content = new Border
                {
                    CornerRadius = new CornerRadius(14), Margin = new Thickness(12), Padding = new Thickness(18),
                    Background = new SolidColorBrush(S.WindowTheme == "amoled" ? Colors.Black : Color.FromRgb(0x22, 0x22, 0x27)),
                    BorderBrush = UI.Hex("#30FFFFFF"), BorderThickness = new Thickness(1),
                    Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Opacity = 0.45 },
                    Child = UI.Grid(null, "a,*,a").Add(head, body.At(1, 0), foot.At(2, 0)),
                },
            };
            // тащим окно за любое место, кроме кнопок (иначе DragMove съедает клик по «Копировать» и «Закрыть»)
            w.MouseLeftButtonDown += (_, e) => { if (!IsInteractive(e.OriginalSource as DependencyObject)) try { w.DragMove(); } catch { } };
            w.KeyDown += (_, e) => { if (e.Key == Key.Escape) w.Close(); };
            w.Closed += (_, __) => { cts.Cancel(); if (_aiWin == w) _aiWin = null; };
            _aiWin = w;
            w.Show();
            _ = Run();
            async Task Run()
            {
                var sb = new System.Text.StringBuilder();
                try
                {
                    var answer = await Ai.Ask(new[] { new AiMessage("user", prompt) }, d =>
                    {
                        lock (sb) sb.Append(d);
                        w.Dispatcher.BeginInvoke(new Action(() => { lock (sb) text.Text = sb.ToString(); }));
                    }, cts.Token);
                    text.Text = string.IsNullOrWhiteSpace(answer) ? "ИИ ничего не ответил — попробуй ещё раз или выбери другого провайдера." : answer.Replace("**", "").Trim();
                }
                catch (OperationCanceledException) { }
                catch (Exception e) { text.Text = "Не получилось: " + e.Message; }
            }
        }

        // ------------------------------------------------------------------
        //  Оптимизация Windows и программ
        // ------------------------------------------------------------------

        static readonly Dictionary<string, (string Title, string Explain, string Power, string[] Keys)> OptimizeProfiles = new()
        {
            ["weak"] = ("Слабый ноутбук", "Лёгкий интерфейс, меньше фоновых программ и браузеров в памяти", "balanced",
                        new[] { "lightui", "quickaccess", "widgets", "edgebg", "chromebg", "bgapps", "storagesense", "delivery", "gamedvr" }),
            ["games"] = ("Игры", "Без фоновой записи и ускорения мыши, сеть без ограничений, режим «Производительность»", "perf",
                         new[] { "gamedvr", "mouseaccel", "netgame" }),
            ["battery"] = ("Батарея", "Меньше фоновой работы и режим «Экономия» — дольше без зарядки", "saver",
                           new[] { "bgapps", "widgets", "edgebg", "chromebg", "delivery" }),
        };

        UIElement OptimizeSec()
        {
            var page = new StackPanel();
            var status = Status();
            int on = Optimize.Items.Count(Privacy.IsProtected);
            page.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(14), Padding = new Thickness(16), Margin = new Thickness(0, 0, 0, 12),
                Background = new LinearGradientBrush(Color.FromArgb(0x2A, 0xFF, 0x9F, 0x0A), Color.FromArgb(0x12, 0x0A, 0x84, 0xFF), 20),
                BorderBrush = UI.Hex("#26FFFFFF"), BorderThickness = new Thickness(1),
                Child = UI.Col(UI.Text($"Применено: {on} из {Optimize.Items.Length}", 17, true),
                    new TextBlock { Text = "Настоящие параметры Windows, а не «магия». У каждого пункта написано, что он делает и чем платишь. " +
                                           "Перед изменением N-Island сохраняет старое значение — выключи переключатель, и всё вернётся. " +
                                           "Нажми ✨ рядом с названием, чтобы ИИ объяснил подробнее.",
                                    FontSize = 12, Foreground = UI.Dim, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) }),
            });

            // готовые профили
            var profiles = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 8) };
            foreach (var (key, (title, explain, power, keys)) in OptimizeProfiles)
            {
                var ks = keys; var pw = power; var t = title;
                var card = new Border
                {
                    CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 0, 8, 0), Cursor = Cursors.Hand,
                    Background = UI.Hex("#14FFFFFF"), BorderBrush = UI.Hex("#1AFFFFFF"), BorderThickness = new Thickness(1), ToolTip = explain,
                    Child = UI.Col(UI.Text("Профиль: " + title, 13.5, true),
                        new TextBlock { Text = explain, FontSize = 11, Foreground = UI.Dim, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) }),
                };
                card.MouseLeftButtonUp += async (_, __) =>
                {
                    if (!Confirm($"Применить профиль «{t}»?\n\nВключатся: " + string.Join(", ", Optimize.Items.Where(i => ks.Contains(i.Key)).Select(i => i.Title)) +
                                 ".\nВсё можно вернуть кнопкой «Вернуть всё как было».")) return;
                    status.Text = "Применяю профиль — Windows может спросить права администратора";
                    Laptop.PowerMode = pw;
                    status.Text = await Privacy.Protect(Optimize.Items.Where(i => ks.Contains(i.Key) && !Privacy.IsProtected(i)).ToList());
                    Open("optimize");
                };
                profiles.Children.Add(card);
            }
            page.Children.Add(profiles);
            page.Children.Add(Buttons(
                Act("Вернуть всё как было", async () =>
                {
                    var list = Optimize.Items.Where(i => Privacy.HasBackup(i) || i.State?.Invoke() == true && Privacy.HasBackup(i)).ToList();
                    if (list.Count == 0) return "Нечего возвращать — N-Island ещё ничего здесь не менял";
                    if (!Confirm("Вернуть все оптимизации к значениям, которые были до N-Island?")) return null;
                    var r = await Privacy.Restore(list);
                    Open("optimize");
                    return r;
                }, status),
                Act("Точка восстановления", RestorePoint, status, false, "Рекомендуем создать перед системными изменениями")));
            page.Children.Add(status);

            foreach (var g in Optimize.Items.GroupBy(i => i.Group))
                page.Children.Add(Group(g.Key, ToggleRows(g, status)));

            // тонкая настройка программ
            var apps = new StackPanel();
            void RenderApps()
            {
                apps.Children.Clear();
                var list = Optimize.Tuned();
                if (list.Count == 0) apps.Children.Add(Loading("Пока ни одной программы. Добавь игру или тяжёлую программу кнопкой выше."));
                foreach (var a in list) apps.Children.Add(AppTuneCard(a, status, RenderApps));
            }
            void AddApp(string exe)
            {
                if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) return;
                if (!Store.Data.TunedApps.Contains(exe, StringComparer.OrdinalIgnoreCase)) Store.Data.TunedApps.Add(exe);
                Store.SaveData();
                RenderApps();
            }
            var running = new WrapPanel { Margin = new Thickness(14, 0, 14, 8) };
            page.Children.Add(Group("Тонкая настройка программ",
                Item("Своя настройка для игры или программы",
                     "Какая видеокарта её запускает, с каким приоритетом процессора и с какими флагами совместимости. Это те же настройки, " +
                     "что в «Параметры → Дисплей → Графика» и во вкладке «Совместимость» — просто в одном месте.", Safety.Safe,
                     UI.Row(UI.Pill("Выбрать .exe", () =>
                     {
                         var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Программы|*.exe" };
                         if (dlg.ShowDialog(this) == true) AddApp(dlg.FileName);
                     }, true), UI.Pill("Из запущенных", () =>
                     {
                         running.Children.Clear();
                         foreach (var (name, path) in RunningApps().Take(24))
                         {
                             var pth = path;
                             running.Children.Add(UI.Pill(name, () => { AddApp(pth); running.Children.Clear(); }).M(0, 0, 6, 6));
                         }
                         if (running.Children.Count == 0) running.Children.Add(Loading("Не нашёл открытых окон программ"));
                     }).M(8, 0, 0, 0))),
                running, apps));
            RenderApps();
            page.Children.Add(Note("Приоритет процессора записывается для имени программы и действует при каждом её запуске (нужны права администратора). " +
                                   "Видеокарта и совместимость — настройки только твоей учётной записи."));
            return page;
        }

        /// <summary>Внутренние страницы браузера (chrome://…) открываются только через сам браузер.</summary>
        static void OpenBrowser(string exe, string url)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, url) { UseShellExecute = true }); }
            catch { MessageBox.Show($"Не нашёл {exe}. Открой браузер и введи в адресной строке: {url}", "N-Island"); }
        }

        static List<(string Name, string Path)> RunningApps()
        {
            var res = new List<(string, string)>();
            foreach (var p in System.Diagnostics.Process.GetProcesses())
            {
                using (p)
                {
                    try
                    {
                        if (p.MainWindowHandle == IntPtr.Zero || p.ProcessName is "explorer" or "NIsland" or "ApplicationFrameHost" or "TextInputHost") continue;
                        var path = p.MainModule?.FileName;
                        if (path != null && !path.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows), StringComparison.OrdinalIgnoreCase))
                            res.Add((p.ProcessName, path));
                    }
                    catch { }
                }
            }
            return res.GroupBy(r => r.Item2, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).OrderBy(r => r.Item1).ToList();
        }

        UIElement AppTuneCard(AppTune a, TextBlock status, Action refresh)
        {
            var exe = a.Exe;
            UIElement SegOf<T>(IEnumerable<(T Key, string Name)> opts, T cur, Action<T> set, string group)
            {
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                foreach (var (k, n) in opts)
                {
                    var kk = k;
                    var rb = new RadioButton { Style = UI.St("Segment"), Content = n, GroupName = group, IsChecked = Equals(k, cur), FontSize = 11.5 };
                    rb.Checked += (_, __) => set(kk);
                    sp.Children.Add(rb);
                }
                return new Border { Background = UI.Hex("#22FFFFFF"), CornerRadius = new CornerRadius(8), Padding = new Thickness(2), Child = sp, HorizontalAlignment = HorizontalAlignment.Left };
            }
            string id = Guid.NewGuid().ToString("N");
            var compat = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            var flags = Optimize.GetCompat(exe);
            foreach (var (flag, name, explain) in Optimize.CompatFlags)
            {
                var f = flag;
                var cb = new CheckBox { Content = name, IsChecked = flags.Contains(f), Foreground = Brushes.White, Margin = new Thickness(0, 0, 14, 4), ToolTip = explain, FontSize = 12 };
                cb.Checked += (_, __) => { Optimize.SetCompat(exe, f, true); status.Text = $"{Path.GetFileName(exe)}: {name} ✓"; };
                cb.Unchecked += (_, __) => { Optimize.SetCompat(exe, f, false); status.Text = $"{Path.GetFileName(exe)}: {name} — выключено"; };
                compat.Children.Add(cb);
            }
            var remove = UI.IconBtn("", () =>
            {
                Optimize.SetGpu(exe, "0");
                foreach (var (flag, _, _) in Optimize.CompatFlags) Optimize.SetCompat(exe, flag, false);
                if (Optimize.GetPriority(exe) != 0) _ = Optimize.SetPriority(exe, 0);
                Store.Data.TunedApps.RemoveAll(x => string.Equals(x, exe, StringComparison.OrdinalIgnoreCase));
                Store.SaveData();
                status.Text = Path.GetFileName(exe) + ": все настройки сброшены";
                refresh();
            }, 11, 28, "Сбросить настройки и убрать из списка");
            var col = UI.Col(
                UI.Grid("*,a").Add(UI.Col(UI.Text(Path.GetFileNameWithoutExtension(exe), 14, true), UI.Text(exe, 11, false, UI.Dim)), remove.At(0, 1)),
                UI.Grid("110,*").M(0, 8, 0, 0).Add(UI.Text("Видеокарта", 12, false, UI.Dim),
                    SegOf(Optimize.GpuModes, a.Gpu, v => { Optimize.SetGpu(exe, v); status.Text = $"{Path.GetFileName(exe)}: видеокарта — {Optimize.GpuModes.First(m => m.Key == v).Name.ToLower()} (со следующего запуска)"; }, "gpu" + id).At(0, 1)),
                UI.Grid("110,*").M(0, 6, 0, 0).Add(UI.Text("Процессор", 12, false, UI.Dim),
                    SegOf(Optimize.Priorities, a.Priority, async v =>
                    {
                        status.Text = "Приоритет: подтверди запрос Windows";
                        int code = await Optimize.SetPriority(exe, v);
                        status.Text = code < 0 ? "Нет разрешения администратора" : $"{Path.GetFileName(exe)}: приоритет — {Optimize.Priorities.First(p => p.Key == v).Name.ToLower()} ✓";
                    }, "prio" + id).At(0, 1)),
                compat);
            return new Border { Margin = new Thickness(14, 6, 14, 6), Padding = new Thickness(12), CornerRadius = new CornerRadius(10), Background = UI.Hex("#10FFFFFF"), Child = col };
        }

        // ------------------------------------------------------------------
        //  Журнал активности: что делали и что запрашивали программы и сайты
        // ------------------------------------------------------------------

        UIElement ActivitySec()
        {
            var page = new StackPanel();
            page.Children.Add(Note("Всё ниже Windows и браузеры уже хранят на этом компьютере — N-Island только собирает и показывает понятно. " +
                                   "Ничего никуда не отправляется.").M(4, 0, 4, 12));

            UIElement Line(string glyph, string title, string sub, string right, Brush rightBrush = null) =>
                UI.Grid("a,*,a").M(14, 6, 14, 6).Add(UI.Icon(glyph, 14, UI.Dim).VA(VerticalAlignment.Top).M(0, 2, 0, 0),
                    UI.Col(UI.Text(title, 13), sub == null ? null : new TextBlock { Text = sub, FontSize = 11, Foreground = UI.Dim, TextWrapping = TextWrapping.Wrap }).M(10, 0, 10, 0).At(0, 1),
                    UI.Text(right, 12, false, rightBrush ?? UI.Dim).VA(VerticalAlignment.Top).At(0, 2));

            // камера, микрофон, место
            var cam = new StackPanel();
            string Dur(ActivityRecord r) => r.End == null ? "идёт сейчас" : r.End == r.Start ? "" : Fmt.Time(r.End.Value - r.Start);
            var journal = Store.Data.ActivityLog.AsEnumerable().Reverse().Take(25).ToList();
            foreach (var r in journal)
            {
                var glyph = r.Kind == "камера" ? "" : r.Kind == "микрофон" ? "" : "";
                cam.Children.Add(Line(glyph, $"{r.App} · {r.Kind}", r.Start.ToString("d MMM, HH:mm", Fmt.Ru) + (Dur(r) is var d && d != "" && r.End != null ? " · " + d : ""),
                    r.End == null ? "сейчас" : Rel(r.Start), r.End == null ? UI.Hex("#FF9F0A") : null));
            }
            if (journal.Count == 0)
                foreach (var a in Privacy.AccessLog().Take(12))
                    cam.Children.Add(Line(a.Kind == "камера" ? "" : a.Kind == "микрофон" ? "" : "", $"{a.App} · {a.Kind}",
                        "последнее использование по данным Windows", a.InUse ? "сейчас" : Rel(a.Last)));
            if (cam.Children.Count == 0) cam.Children.Add(Loading("Записей пока нет"));
            page.Children.Add(Group("Камера, микрофон и местоположение — кто, когда и сколько",
                Item("Вести журнал", "N-Island записывает каждое включение камеры, микрофона и запрос местоположения — даже если программа делает это скрытно.",
                     Safety.Safe, Toggle(nameof(Settings.ActivityJournal))),
                cam));

            // сайты
            var sites = new StackPanel();
            sites.Children.Add(Loading("Читаю настройки браузеров…"));
            page.Children.Add(Group("Что разрешено сайтам в браузерах", sites));
            _ = Tracking.SitePermissions().ContinueWith(t => Dispatcher.Invoke(() =>
            {
                sites.Children.Clear();
                var list = t.Result;
                if (list.Count == 0) { sites.Children.Add(Loading("Сайты пока ничего не запрашивали (или браузер не поддерживается)")); return; }
                foreach (var g in list.GroupBy(x => (x.Browser, x.Site)).Take(40))
                {
                    var allowed = g.Where(x => x.Allowed).Select(x => x.Permission).Distinct().ToList();
                    var blocked = g.Where(x => !x.Allowed).Select(x => x.Permission).Distinct().ToList();
                    var sub = (allowed.Count > 0 ? "разрешено: " + string.Join(", ", allowed) : "") + (allowed.Count > 0 && blocked.Count > 0 ? " · " : "") +
                              (blocked.Count > 0 ? "запрещено: " + string.Join(", ", blocked) : "");
                    var when = g.Max(x => x.Changed);
                    sites.Children.Add(Line("", $"{g.Key.Site} · {g.Key.Browser}", sub, when is DateTime w ? Rel(w) : "",
                        allowed.Any(p => p is "камера" or "микрофон" or "местоположение") ? UI.Hex("#FF9F0A") : null));
                }
                sites.Children.Add(Item("Забрать разрешения", "В настройках браузера: Конфиденциальность → Настройки сайтов. Для Chrome и Edge откроется сразу нужная страница.", null,
                    UI.Row(UI.Pill("Chrome", () => OpenBrowser("chrome", "chrome://settings/content")),
                           UI.Pill("Edge", () => OpenBrowser("msedge", "edge://settings/content")).M(6, 0, 0, 0))));
            }));

            // запуски программ
            var runs = new StackPanel();
            runs.Children.Add(Loading());
            page.Children.Add(Group("Какие программы и когда запускались", runs));
            _ = Tracking.Launches().ContinueWith(t => Dispatcher.Invoke(() =>
            {
                runs.Children.Clear();
                foreach (var l in t.Result.Take(25))
                    runs.Children.Add(Line("", l.Name, $"запусков: {l.Count}" + (l.Focus.TotalMinutes >= 1 ? $" · в фокусе {Fmt.Time(l.Focus)}" : "") + " · " + l.Path,
                        l.Last is DateTime d ? Rel(d) : ""));
                if (t.Result.Count == 0) runs.Children.Add(Loading("Windows не ведёт историю запусков (выключено «Отслеживание запуска приложений»)"));
            }));

            // уведомления
            var notif = new StackPanel();
            notif.Children.Add(Loading());
            page.Children.Add(Group("Кто присылал уведомления за неделю", notif));
            _ = Tracking.NotificationStats().ContinueWith(t => Dispatcher.Invoke(() =>
            {
                notif.Children.Clear();
                foreach (var n in t.Result.Take(15)) notif.Children.Add(Line("", n.App, null, $"{n.Count} шт. · {Rel(n.Last)}"));
                if (t.Result.Count == 0) notif.Children.Add(Loading("Уведомлений за неделю не было"));
            }));

            // установки и флешки
            var inst = new StackPanel();
            var usb = new StackPanel();
            page.Children.Add(Group("Что устанавливалось за 3 месяца", inst));
            page.Children.Add(Group("Какие флешки и диски подключались", usb));
            _ = Tracking.RecentInstalls().ContinueWith(t => Dispatcher.Invoke(() =>
            {
                foreach (var i in t.Result.Take(25)) inst.Children.Add(Line("", i.Name, i.Publisher == "" ? null : i.Publisher, i.Date.ToString("d MMM yyyy", Fmt.Ru)));
                if (t.Result.Count == 0) inst.Children.Add(Loading("За 3 месяца ничего не устанавливалось"));
            }));
            _ = Tracking.UsbHistory().ContinueWith(t => Dispatcher.Invoke(() =>
            {
                foreach (var u in t.Result.Take(20)) usb.Children.Add(Line("", u.Name, null, u.Last is DateTime d ? Rel(d) : ""));
                if (t.Result.Count == 0) usb.Children.Add(Loading("Флешки не подключались"));
            }));
            page.Children.Add(Note("Подозрительно? Неизвестная программа включала камеру или сайт получил доступ к микрофону — забери разрешение в браузере " +
                                   "или запрети в «Защита от слежки → Настройки доступа Windows»."));
            return page;
        }
    }
}
