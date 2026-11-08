using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using NIsland.Core;
using NIsland.Pages;
using NIsland.Services;

namespace NIsland
{
    // Настройки → ИИ: провайдеры по группам, ключи, модели из обновляемого списка, запасной провайдер, свои провайдеры
    public partial class SettingsWindow
    {
        static string _aiFilter = "";

        UIElement AiSec()
        {
            var cur = Ai.Current;

            // ---------------- список провайдеров с поиском ----------------
            var list = new StackPanel();
            var search = UI.Box("Найти провайдера…", 260);
            search.Text = _aiFilter;
            void FillList()
            {
                list.Children.Clear();
                var q = search.Text.Trim();
                foreach (var g in Ai.All.Where(p => q == "" || p.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || p.Key.Contains(q, StringComparison.OrdinalIgnoreCase))
                                        .GroupBy(p => p.Group))
                {
                    list.Children.Add(UI.Text(g.Key, 11, true, UI.Dim).M(4, 8, 0, 4));
                    var wrap = new WrapPanel();
                    foreach (var p in g)
                    {
                        var prov = p;
                        bool on = p.Key == cur.Key;
                        bool ready = Ai.Ready(p, out _);
                        var dot = new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center,
                            Fill = ready ? UI.Hex("#30D158") : p.NeedsKey ? UI.Hex("#FF9F0A") : UI.Hex("#8E8E93") };
                        var card = new Border
                        {
                            CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 0, 5, 5), Cursor = Cursors.Hand, Width = 196,
                            Background = on ? UI.Hex("#2E0A84FF") : UI.Hex("#14FFFFFF"),
                            BorderBrush = on ? UI.Hex("#0A84FF") : UI.Hex("#14FFFFFF"), BorderThickness = new Thickness(1.5),
                            ToolTip = ready ? I18n.T("Готов к работе") : I18n.T(p.NeedsKey ? "Нужен ключ" : "Не настроен"),
                            Child = UI.Grid("a,*").Add(dot, UI.Col(
                                new TextBlock { Text = p.Name, FontSize = 12.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis },
                                new TextBlock { Text = Ai.Model(p), FontSize = 10.5, Foreground = UI.Dim, TextTrimming = TextTrimming.CharacterEllipsis }).At(0, 1)),
                        };
                        card.MouseLeftButtonUp += (_, __) => { _aiFilter = search.Text; S.AiProvider = prov.Key; Open("ai"); };
                        wrap.Children.Add(card);
                    }
                    list.Children.Add(wrap);
                }
            }
            search.TextChanged += (_, __) => FillList();
            FillList();

            // ---------------- ключ ----------------
            var keyBox = new PasswordBox
            {
                Width = 300, Foreground = Brushes.White, CaretBrush = Brushes.White, Background = UI.Hex("#1FFFFFFF"), BorderThickness = new Thickness(0),
                Padding = new Thickness(8, 5, 8, 5), FontSize = 12.5,
            };
            var keyState = UI.Text(Ai.HasKey(cur) ? "Ключ сохранён (зашифрован Windows). Введи новый, чтобы заменить." : "Ключ хранится только на этом ПК, в зашифрованном виде.", 11, false, UI.Dim);
            void SaveKey()
            {
                if (keyBox.Password.Length == 0) return;
                Ai.SetKey(cur, keyBox.Password);
                keyBox.Clear();
                keyState.Text = I18n.T("Ключ сохранён ✓ Загружаю список моделей…");
                Dispatcher.BeginInvoke(new Action(() => Open("ai")));   // перерисуем — список моделей подгрузится сам
            }
            keyBox.LostFocus += (_, __) => SaveKey();
            keyBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) SaveKey(); };
            var keyRow = UI.Col(UI.Row(keyBox,
                    UI.Pill("Удалить", () => { Ai.SetKey(cur, null); keyState.Text = I18n.T("Ключ удалён"); }).M(6, 0, 0, 0),
                    cur.KeyUrl != null ? UI.Pill("Получить ключ", () => SystemService.Run(cur.KeyUrl)).M(6, 0, 0, 0) : null),
                keyState.M(0, 4, 0, 0));

            // ---------------- модель: обновляемый список с поиском ----------------
            // поле + отфильтрованные «чипсы» моделей: печатаешь часть имени — список сужается, клик — выбрать
            var modelBox = Field(Ai.Model(cur), 320, null, cur.DefaultModel);
            var modelInfo = UI.Text("", 11, false, UI.Dim);
            var chips = new WrapPanel();
            var chipsScroll = new ScrollViewer { Content = chips, MaxHeight = 150, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            List<string> all = Ai.CachedModels(cur);
            void FillModels(string filter)
            {
                chips.Children.Clear();
                var sel = Ai.Model(cur);
                var items = all.Where(m => string.IsNullOrEmpty(filter) || filter == sel || m.Contains(filter, StringComparison.OrdinalIgnoreCase)).Take(120).ToList();
                foreach (var id in items)
                {
                    var mid = id;
                    var chip = UI.Pill(id, () => { Ai.SetModel(cur, mid); modelBox.Text = mid; FillModels(null); Info(); });
                    chip.Padding = new Thickness(8, 2, 8, 2); chip.FontSize = 10.5; chip.Margin = new Thickness(0, 3, 4, 0);
                    if (id == sel) { chip.Background = UI.Res("AccentBrush"); chip.SetResourceReference(Control.ForegroundProperty, "AccentTextBrush"); }
                    chips.Children.Add(chip);
                }
            }
            void Info()
            {
                var at = Store.Data.AiModelCacheAt.TryGetValue(cur.Key, out var t) ? t : (DateTime?)null;
                modelInfo.Text = all.Count == 0
                    ? I18n.T("Список моделей ещё не загружен — нажми «Обновить список» или впиши имя модели вручную.")
                    : I18n.T("Моделей: ") + all.Count + (at != null ? I18n.T(" · обновлено ") + at.Value.ToString("d MMM, HH:mm", Fmt.Ru) : "") +
                      (all.Contains(Ai.Model(cur)) ? "" : I18n.T(" · ⚠ выбранной модели нет в списке"));
            }
            async System.Threading.Tasks.Task RefreshModels(bool force)
            {
                modelInfo.Text = I18n.T("Загружаю список моделей…");
                try
                {
                    all = await Ai.Models(cur, force);
                    FillModels(null);
                    Info();
                }
                catch (Exception e) { modelInfo.Text = I18n.T("Не удалось загрузить: ") + e.Message; }
            }
            FillModels(null);
            Info();
            modelBox.TextChanged += (_, __) => { if (modelBox.IsKeyboardFocused) FillModels(modelBox.Text.Trim()); };
            void CommitModel() { if (modelBox.Text.Trim() != "" && modelBox.Text.Trim() != Ai.Model(cur)) { Ai.SetModel(cur, modelBox.Text); FillModels(null); Info(); } }
            modelBox.LostFocus += (_, __) => CommitModel();
            modelBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) CommitModel(); };
            // список устарел — тихо обновим при открытии
            if (Ai.Ready(cur, out _) && Ai.ModelsStale(cur) && cur.Group != "На этом ПК") _ = RefreshModels(true);
            var modelRow = UI.Col(UI.Row(modelBox,
                    UI.Pill("Обновить список", () => _ = RefreshModels(true)).M(6, 0, 0, 0),
                    UI.Pill("По умолчанию", () => { Ai.SetModel(cur, ""); modelBox.Text = cur.DefaultModel; Info(); }).M(6, 0, 0, 0)),
                modelInfo.M(0, 4, 0, 0), chipsScroll.M(0, 2, 0, 0));

            // ---------------- проверка ----------------
            var test = UI.Text("", 11.5, false, UI.Dim);
            test.TextWrapping = TextWrapping.Wrap;
            var testBtn = UI.Pill("Проверить", async () =>
            {
                test.Foreground = UI.Dim;
                test.Text = I18n.T("Отправляю тестовый запрос…");
                var sw = Stopwatch.StartNew();
                try
                {
                    var ans = await Ai.Ask(new[] { new AiMessage("user", "Ответь одним коротким предложением: ты на связи?") }, null, default, cur);
                    test.Foreground = UI.Hex("#30D158");
                    test.Text = $"{I18n.T("Работает ✓")} {sw.Elapsed.TotalSeconds:0.0} {I18n.T("с")} · {Ai.Model(cur)}\n{ans.Trim()}";
                }
                catch (Exception e) { test.Foreground = UI.Hex("#FF9F0A"); test.Text = I18n.T("Ошибка: ") + e.Message; }
            }, true);
            var testAll = UI.Pill("Проверить все настроенные", async () =>
            {
                test.Foreground = UI.Dim;
                var lines = new List<string>();
                foreach (var p in Ai.All.Where(x => Ai.Ready(x, out _) && (x.Key is "free" or "llm7" || Ai.HasKey(x) || x.Custom)).ToList())
                {
                    test.Text = string.Join("\n", lines.Append(p.Name + " …"));
                    var sw = Stopwatch.StartNew();
                    try
                    {
                        using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(40));
                        await Ai.Ask(new[] { new AiMessage("user", "Ответь одним словом: да") }, null, cts.Token, p);
                        lines.Add($"✓ {p.Name} — {sw.Elapsed.TotalSeconds:0.0} {I18n.T("с")}");
                    }
                    catch (Exception e) { lines.Add($"✕ {p.Name} — {(e.Message.Length > 90 ? e.Message[..90] + "…" : e.Message)}"); }
                    test.Text = string.Join("\n", lines);
                }
            });

            // ---------------- подключение выбранного ----------------
            var conn = new List<UIElement> { Row(cur.Name, cur.Note, null) };
            if (cur.NeedsKey || cur.Custom || cur.Key == "llm7") conn.Add(Wide("API-ключ", cur.Key == "llm7" ? "Необязательно: без ключа работает модель default." : null, keyRow));
            conn.Add(Wide("Модель", cur.Kind == "anthropic"
                ? "По умолчанию claude-opus-5-5. Можно выбрать claude-sonnet-5-5 или claude-haiku-5-5 — быстрее и дешевле."
                : "Начни печатать — список отфильтруется. Список обновляется сам раз в 3 дня.", modelRow));
            if (!cur.Custom)
                conn.Add(Wide("Адрес сервера", cur.Kind == "anthropic" ? "Пусто — официальный api.anthropic.com. Можно указать свой прокси." : "Базовый адрес API (…/v1). Можно вставить и полный адрес с /chat/completions — он поправится сам.",
                    Field(Ai.Url(cur), 420, v => { Ai.SetUrl(cur, v); }, cur.BaseUrl)));
            conn.Add(Wide("Проверка", null, UI.Col(UI.Row(testBtn, testAll.M(6, 0, 0, 0)), test.M(0, 6, 0, 0))));

            // ---------------- редактирование своего провайдера ----------------
            UIElement edit = null;
            if (cur.Custom)
            {
                var c = S.AiCustom.First(x => "c:" + x.Id == cur.Key);
                var eName = Field(c.Name, 260, v => Ai.UpdateCustom(cur.Key, x => x.Name = v.Trim() == "" ? x.Name : v.Trim()));
                var eUrl = Field(c.BaseUrl, 420, v => Ai.UpdateCustom(cur.Key, x => x.BaseUrl = v), "https://api.example.com/v1");
                var eHeaders = new TextBox { AcceptsReturn = true, Height = 54, Template = NIsland.Pages.Ago.PlainBox(), Foreground = Brushes.White, CaretBrush = Brushes.White, FontSize = 12, Text = c.Headers ?? "" };
                eHeaders.LostFocus += (_, __) => Ai.UpdateCustom(cur.Key, x => x.Headers = eHeaders.Text);
                var eKind = new StackPanel { Orientation = Orientation.Horizontal };
                foreach (var (k, label) in new[] { ("openai", "OpenAI-совместимый"), ("anthropic", "Anthropic (Claude)") })
                {
                    var kk = k;
                    var rb = new RadioButton { Style = UI.St("Segment"), Content = label, GroupName = "editkind", IsChecked = c.Kind == k };
                    rb.Checked += (_, __) => Ai.UpdateCustom(cur.Key, x => x.Kind = kk);
                    eKind.Children.Add(rb);
                }
                var eNeed = new CheckBox { Style = UI.St("Switch"), IsChecked = c.NeedsKey };
                eNeed.Click += (_, __) => Ai.UpdateCustom(cur.Key, x => x.NeedsKey = eNeed.IsChecked == true);
                edit = Group("Этот провайдер",
                    Wide("Название", null, eName),
                    Wide("Протокол", null, eKind),
                    Wide("Адрес API", "Вставь как в документации — «/chat/completions» и лишний «/» уберутся сами", eUrl),
                    Wide("Доп. заголовки", "По строке «Имя: значение». {key} — подставить ключ, например «api-key: {key}» для Azure (тогда «Bearer» не добавляется)", eHeaders),
                    Row("Нужен API-ключ", null, eNeed),
                    Row("Удалить этого провайдера", null, UI.Pill("Удалить", () => { Ai.RemoveCustom(cur.Key); Open("ai"); })));
            }

            // ---------------- новый свой провайдер ----------------
            var nName = Field("", 260, null, "Название, например «Мой сервер»");
            var nUrl = Field("", 420, null, "https://api.example.com/v1");
            var nModel = Field("", 260, null, "имя модели (можно выбрать позже из списка)");
            var nHeaders = new TextBox { AcceptsReturn = true, Height = 54, Template = NIsland.Pages.Ago.PlainBox(), Foreground = Brushes.White, CaretBrush = Brushes.White, FontSize = 12 };
            var nKey = new PasswordBox { Width = 300, Foreground = Brushes.White, CaretBrush = Brushes.White, Background = UI.Hex("#1FFFFFFF"), BorderThickness = new Thickness(0), Padding = new Thickness(8, 5, 8, 5), FontSize = 12.5 };
            string kind = "openai";
            var kindSeg = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var (k, label) in new[] { ("openai", "OpenAI-совместимый"), ("anthropic", "Anthropic (Claude)") })
            {
                var kk = k;
                var rb = new RadioButton { Style = UI.St("Segment"), Content = label, GroupName = "newkind", IsChecked = k == "openai" };
                rb.Checked += (_, __) => kind = kk;
                kindSeg.Children.Add(rb);
            }
            var addState = UI.Text("", 11.5, false, UI.Dim);
            addState.TextWrapping = TextWrapping.Wrap;
            var add = UI.Pill("Добавить и проверить", async () =>
            {
                if (string.IsNullOrWhiteSpace(nUrl.Text)) { nUrl.Focus(); addState.Text = I18n.T("Укажи адрес API"); return; }
                var name = string.IsNullOrWhiteSpace(nName.Text) ? new Uri(Ai.Normalize(nUrl.Text, kind)).Host : nName.Text.Trim();
                var c = Ai.AddCustom(new AiCustomProvider
                {
                    Name = name, Kind = kind, BaseUrl = nUrl.Text.Trim(), Model = nModel.Text.Trim(), Headers = nHeaders.Text, NeedsKey = nKey.Password.Length > 0,
                });
                var p = Ai.Find("c:" + c.Id);
                if (nKey.Password.Length > 0) Ai.SetKey(p, nKey.Password);
                addState.Text = I18n.T("Добавлен. Ищу модели на сервере…");
                // модель не указана — берём первую из списка сервера
                try
                {
                    var models = await Ai.Models(p, true);
                    if (string.IsNullOrWhiteSpace(c.Model) && models.Count > 0) { c.Model = models[0]; Store.SaveSettings(); }
                    addState.Text = I18n.T("Моделей на сервере: ") + models.Count;
                }
                catch (Exception e) { addState.Text = I18n.T("Список моделей не загрузился: ") + e.Message; }
                if (string.IsNullOrWhiteSpace(c.Model)) { c.Model = "default"; Store.SaveSettings(); }
                S.AiProvider = "c:" + c.Id;
                Open("ai");
            }, true);

            var fallbackOpts = new List<(object, string)> { ("auto", "Авто (бесплатный)"), ("none", "Нет") };
            fallbackOpts.AddRange(Ai.All.Where(p => p.Key != cur.Key && (Ai.HasKey(p) || p.Custom)).Take(4).Select(p => ((object)p.Key, p.Name.Split(' ')[0])));

            return Panel(
                UI.Grid("*,a").M(4, 0, 0, 2).Add(UI.Text("Нейросеть", 12, true, UI.Dim), search.At(0, 1)),
                list.M(0, 0, 0, 12),
                Group("Подключение", conn.ToArray()),
                edit,
                Group("Надёжность",
                    Row("Запасной провайдер", "Если основной не ответил (нет связи, лимит, ошибка) — ответит этот. «Авто» — бесплатный Pollinations или LLM7",
                        Seg(nameof(Settings.AiFallback), fallbackOpts.ToArray())),
                    Row("Сам подбирать модель", "Если провайдер убрал выбранную модель — взять похожую из его списка и запомнить", Toggle(nameof(Settings.AiAutoModel))),
                    Row("Тайм-аут", null, Stepper(nameof(Settings.AiTimeout), 10, 900, 10, v => v + " с"))),
                Group("Параметры ответа",
                    Row("Температура", "Выше — креативнее, ниже — точнее. «Авто» — как решит модель (для Claude не используется)",
                        Stepper(nameof(Settings.AiTemperature), -10, 200, 10, v => v < 0 ? "авто" : (v / 100.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture))),
                    Row("Максимум токенов ответа", "0 — по умолчанию", Stepper(nameof(Settings.AiMaxTokens), 0, 64000, 512, v => v == 0 ? "авто" : v.ToString())),
                    Row("Усилие (Claude)", "Насколько глубоко думать: low — быстро, max — тщательно",
                        Seg(nameof(Settings.AiEffort), ("default", "Авто"), ("low", "Low"), ("medium", "Med"), ("high", "High"), ("xhigh", "XHigh"), ("max", "Max"))),
                    Row("Память диалога", "Сколько последних сообщений отправлять", Stepper(nameof(Settings.AiHistory), 2, 100, 2, v => v + " сообщ.")),
                    Row("Потоковый ответ", "Текст появляется по мере генерации", Toggle(nameof(Settings.AiStream))),
                    Row("Показывать рассуждения", "Блок <think> у DeepSeek R1, Qwen и других «думающих» моделей", Toggle(nameof(Settings.AiShowReasoning)))),
                Group("Чат",
                    Row("Сохранять диалог", "Переписка остаётся после перезапуска", Toggle(nameof(Settings.AiKeepChat))),
                    Row("Общий диалог с Telegram-ботом", "Вопросы из бота (/ai) и ответы видны в чате острова, и наоборот — один разговор", Toggle(nameof(Settings.AiShareTelegram))),
                    Row("Размер текста", null, Stepper(nameof(Settings.AiFontSize), 10, 18, 1, v => v + " pt")),
                    Row("Очистить диалог", null, UI.Pill("Очистить", () => Ai.ClearChat()))),
                Group("Характер",
                    Row("Роль", null, Seg(nameof(Settings.AiPersona), Ai.Personas.Select(x => ((object)x.Key, x.Name)).ToArray())),
                    Wide("Свои инструкции", "Добавляются к системному промпту. Например: «Обращайся на ты», «Отвечай с эмодзи».",
                        InputMulti(nameof(Settings.AiSystem), 70)),
                    Wide("Быстрые действия", "Кнопки под чатом. Формат: «Название | текст запроса», {clip} — текст из буфера обмена. Каждое с новой строки.",
                        InputMulti(nameof(Settings.AiActions), 110))),
                Group("Свой провайдер",
                    Wide("Адрес API", "Любой OpenAI-совместимый сервер: vLLM, LocalAI, Jan, text-generation-webui, корпоративный прокси, Azure", nUrl),
                    Wide("Протокол", null, kindSeg),
                    Wide("Название", "Необязательно — возьмём имя сайта", nName),
                    Wide("API-ключ", "Необязательно", nKey),
                    Wide("Модель", "Необязательно — возьмём первую из списка сервера", nModel),
                    Wide("Доп. заголовки", "Необязательно. По строке: «Имя: значение», {key} — ключ", nHeaders),
                    Wide("", null, UI.Col(add.HA(HorizontalAlignment.Left), addState.M(0, 6, 0, 0)))),
                Note("Ctrl+Alt+A — открыть чат. Claude подключается через официальный SDK Anthropic. Для claude-opus-5-5 включена серверная резервная модель: " +
                     "если фильтр безопасности отклонит запрос, ответ продолжит подходящая модель в том же вызове."));
        }
    }
}
