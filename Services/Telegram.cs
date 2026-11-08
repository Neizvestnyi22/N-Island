using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using NIsland.Core;

namespace NIsland.Services
{
    // =====================================================================
    //  Telegram: события на телефон + полный пульт ПК (меню кнопками, команды, ИИ)
    // =====================================================================

    public partial class TelegramHub : Integration
    {
        public override string Key => "telegram";
        public override string Name => "Telegram";
        public override string Glyph => "";
        public override string Color => "#2AABEE";
        public override string Desc => "Пульт ПК в телефоне: меню кнопками — музыка, звук, экран, окна, клавиатура и мышь, файлы, питание, сеть; события острова приходят сообщениями, " +
                                       "можно спросить ИИ. Сообщение боту — всплывашка в острове";
        public override string Help => "Создай бота у @BotFather, вставь токен, напиши боту любое сообщение и нажми «Проверить» — чат найдётся сам. " +
                                       "Бот слушается только твоего чата. В Telegram: /menu — пульт, /help — все команды. Опасные разрешения включаются только здесь, на ПК";
        public override string HelpUrl => "https://t.me/BotFather";
        public override HubField[] Fields => new[]
        {
            new HubField("token", "Токен бота", "123456:ABC-DEF…", "secret"),
            new HubField("chat", "ID чата", "Найдётся сам после «Проверить»", Required: false),
            new HubField("control", "Управление с телефона", "музыка, громкость, яркость, режимы, заметки, таймер, буфер обмена", "bool", Required: false, Default: "1"),
            new HubField("apps", "Программы и окна", "список окон, свернуть / развернуть / закрыть, процессы, запуск программ", "bool", Required: false, Default: "0"),
            new HubField("input", "Клавиатура и мышь", "нажатия клавиш, ввод текста, мышь, листание презентаций", "bool", Required: false, Default: "0"),
            new HubField("files", "Файлы", "папки и файлы с ПК, поиск, скачать, в корзину; присланный боту файл сохранится в «Загрузки\\N-Island»", "bool", Required: false, Default: "0"),
            new HubField("screen", "Снимок экрана", "всех мониторов или активного окна", "bool", Required: false, Default: "0"),
            new HubField("camera", "Камера и местоположение", "найти пропавший ноутбук: фото с камеры и точка на карте (на ПК появится уведомление)", "bool", Required: false, Default: "0"),
            new HubField("power", "Питание", "блокировка, сон, гибернация, выход, перезагрузка и выключение (с подтверждением)", "bool", Required: false, Default: "0"),
            new HubField("shell", "Команды Windows (опасно)", "/cmd и /ps — любая команда, как в консоли. Включай, только если токен бота никому не известен", "bool", Required: false, Default: "0"),
            new HubField("boot", "Сообщать, что ПК включился", "сообщение при запуске N-Island", "bool", Required: false, Default: "1"),
            new HubField("unlock", "Сообщать о входе, блокировке и пробуждении", "видно, если ноутбуком пользуется кто-то другой", "bool", Required: false, Default: "0"),
            new HubField("aitext", "Обычный текст — вопрос ИИ", "иначе текст показывается в острове; ИИ — /ai вопрос", "bool", Required: false, Default: "0"),
            new HubField("kbd", "Кнопки внизу чата", "Пульт, Статус, Музыка, Экран, Окна, Блок", "bool", Required: false, Default: "1"),
            new HubField("quiet", "Тихие часы", "например 23-8 — сообщения в это время приходят без звука", Required: false),
        };
        public override string[] CanSend => Integrations.EventKinds.Select(e => e.Key).ToArray();
        public override int PollSeconds => 15;

        static readonly HttpClient Long = new(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5), AutomaticDecompression = System.Net.DecompressionMethods.All })
        { Timeout = TimeSpan.FromSeconds(60) };
        static readonly HttpClient Big = new() { Timeout = TimeSpan.FromMinutes(6) };
        static readonly JsonSerializerOptions JsonOpts = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

        string Api(string method) => $"https://api.telegram.org/bot{V("token")}/{method}";
        CancellationTokenSource _loop;
        string _loopToken;
        bool _commandsSet;

        sealed class TgError : Exception
        {
            public readonly int Code;
            public TgError(string m, int code) : base(m) { Code = code; }
        }

        // ------------------------------------------------------------------
        //  Транспорт
        // ------------------------------------------------------------------

        /// <summary>Самопроверка: вместо Telegram запросы уходят сюда (метод, JSON) → JSON ответа.</summary>
        internal static Func<string, string, string> TestSink;
        internal Task<(string Html, object Keys)?> TestCommand(string cmd, string arg) => Command(cmd, arg);
        internal Task<string> TestCallback(string data) => Callback(data, 1);

        async Task<JsonElement> Call(string method, object body, CancellationToken ct = default)
        {
            if (TestSink != null)
            {
                var raw0 = TestSink(method, JsonSerializer.Serialize(body, JsonOpts));
                using var d0 = JsonDocument.Parse(raw0);
                if (d0.RootElement.TryGetProperty("ok", out var ok0) && !ok0.GetBoolean()) throw new TgError(Integrations.S(d0.RootElement, "description"), 400);
                return d0.RootElement.GetProperty("result").Clone();
            }
            using var content = new StringContent(JsonSerializer.Serialize(body, JsonOpts), Encoding.UTF8, "application/json");
            using var resp = await Long.PostAsync(Api(method), content, ct);
            var raw = await resp.Content.ReadAsStringAsync(ct);
            JsonDocument doc;
            try { doc = JsonDocument.Parse(raw); }
            catch { throw new TgError("Telegram: " + Integrations.Snip(raw), (int)resp.StatusCode); }
            using (doc)
            {
                if (!doc.RootElement.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
                    throw new TgError(Integrations.S(doc.RootElement, "description"), (int)resp.StatusCode);
                return doc.RootElement.TryGetProperty("result", out var r) ? r.Clone() : default;
            }
        }

        static string H(string s) => System.Net.WebUtility.HtmlEncode(s ?? "");
        static string T(string s) => I18n.T(s);
        /// <summary>Перевод интерфейсных строк построчно: чужой текст (песни, заметки) не совпадает со словарём и остаётся как есть.</summary>
        static string Tr(string html) => I18n.Active ? string.Join("\n", html.Split('\n').Select(I18n.T)) : html;
        static string Cut(string s, int n) => s == null ? "" : s.Length > n ? s[..n] + "…" : s;

        static List<string> Split(string s, int max)
        {
            var res = new List<string>();
            while (s.Length > max)
            {
                int cut = s.LastIndexOf('\n', max);
                if (cut < max / 2) cut = max;
                res.Add(s[..cut]);
                s = s[cut..].TrimStart('\n');
            }
            if (s.Length > 0) res.Add(s);
            return res;
        }

        async Task<long> Message(string html, object keys = null, bool silent = false, bool translate = true, long replyTo = 0)
        {
            if (string.IsNullOrEmpty(V("chat"))) return 0;
            if (string.IsNullOrWhiteSpace(html)) html = "✓";
            if (translate) html = Tr(html);
            silent |= QuietNow();
            long id = 0;
            var parts = Split(html, 4000);
            for (int i = 0; i < parts.Count; i++)
            {
                var markup = i == parts.Count - 1 ? keys : null;
                JsonElement r;
                try
                {
                    r = await Call("sendMessage", new { chat_id = V("chat"), text = parts[i], parse_mode = "HTML", link_preview_options = NoPreview, disable_notification = silent, reply_markup = markup,
                                                        reply_parameters = i == 0 && replyTo > 0 ? new { message_id = replyTo, allow_sending_without_reply = true } : null });
                }
                catch (TgError e) when (e.Message.Contains("parse", StringComparison.OrdinalIgnoreCase))
                {
                    // разметка разорвалась на границе куска — отправляем простым текстом
                    var plain = System.Net.WebUtility.HtmlDecode(Regex.Replace(parts[i], "<[^>]+>", ""));
                    r = await Call("sendMessage", new { chat_id = V("chat"), text = plain, link_preview_options = NoPreview, disable_notification = silent, reply_markup = markup });
                }
                id = r.ValueKind == JsonValueKind.Object && r.TryGetProperty("message_id", out var mid) ? mid.GetInt64() : 0;
            }
            return id;
        }

        async Task Edit(long msgId, string html, object keys)
        {
            html = Tr(html);
            if (html.Length > 4000) html = html[..4000];
            try { await Call("editMessageText", new { chat_id = V("chat"), message_id = msgId, text = html, parse_mode = "HTML", link_preview_options = NoPreview, reply_markup = keys }); }
            catch (TgError e) when (e.Message.Contains("not modified")) { }
            catch (TgError) { await Message(html, keys, translate: false); }   // старое сообщение или фото — присылаем новое
        }

        /// <summary>Отправить файл (фото, документ). Возвращает номер сообщения — нужен «живому экрану», чтобы менять картинку.</summary>
        async Task<long> Upload(string method, string field, HttpContent file, string fileName, string caption = null, object keys = null)
        {
            using var form = new MultipartFormDataContent { { new StringContent(V("chat")), "chat_id" } };
            if (caption != null) { form.Add(new StringContent(Tr(caption)), "caption"); form.Add(new StringContent("HTML"), "parse_mode"); }
            if (keys != null) form.Add(new StringContent(JsonSerializer.Serialize(keys, JsonOpts)), "reply_markup");
            if (QuietNow()) form.Add(new StringContent("true"), "disable_notification");
            form.Add(file, field, fileName);
            if (TestSink != null) { TestSink(method, JsonSerializer.Serialize(new { file = fileName, caption, reply_markup = keys }, JsonOpts)); return 7; }
            using var resp = await Big.PostAsync(Api(method), form);
            var raw = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode) throw new TgError(Integrations.Snip(raw), (int)resp.StatusCode);
            try { using var d = JsonDocument.Parse(raw); return d.RootElement.GetProperty("result").GetProperty("message_id").GetInt64(); } catch { return 0; }
        }

        static ByteArrayContent Bytes(byte[] data, string mime)
        {
            var c = new ByteArrayContent(data);
            c.Headers.ContentType = new MediaTypeHeaderValue(mime);
            return c;
        }

        Task Action(string action) => Call("sendChatAction", new { chat_id = V("chat"), action }).ContinueWith(_ => { });

        bool QuietNow()
        {
            var m = Regex.Match(V("quiet") ?? "", @"^\s*(\d{1,2})(?:[:.](\d\d))?\s*[-–—]\s*(\d{1,2})(?:[:.](\d\d))?\s*$");
            if (!m.Success) return false;
            int from = int.Parse(m.Groups[1].Value) % 24 * 60 + (m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0);
            int to = int.Parse(m.Groups[3].Value) % 24 * 60 + (m.Groups[4].Success ? int.Parse(m.Groups[4].Value) : 0);
            int now = DateTime.Now.Hour * 60 + DateTime.Now.Minute;
            return from <= to ? now >= from && now < to : now >= from || now < to;
        }

        static readonly object NoPreview = new { is_disabled = true };

        /// <summary>Кнопка «скопировать в буфер телефона» (Bot API 7.11+, до 256 символов).</summary>
        static object Copy(string label, string text) => string.IsNullOrEmpty(text) || text.Length > 256 ? null : new { text = T(label), copy_text = new { text } };

        /// <summary>Реакция на сообщение (Bot API 7.0+): видно, что команда принята.</summary>
        Task React(long msgId, string emoji) => msgId <= 0 ? Task.CompletedTask
            : Call("setMessageReaction", new { chat_id = V("chat"), message_id = msgId, reaction = emoji == null ? Array.Empty<object>() : new object[] { new { type = "emoji", emoji } } }).ContinueWith(_ => { });

        /// <summary>Длинный вывод — в сворачиваемую цитату (Bot API 7.4+).</summary>
        static string Fold(string html) => html.Length > 700 ? "<blockquote expandable>" + html + "</blockquote>" : html;

        static object B(string text, string data) => new { text = T(text), callback_data = data };
        static object Url(string text, string url) => new { text = T(text), url };
        static object K(params object[][] rows) => new { inline_keyboard = rows.Where(r => r != null && r.Length > 0).ToArray() };
        static object[] Row(params object[] b) => b.Where(x => x != null).ToArray();
        static object[][] Grid(IEnumerable<object> buttons, int perRow) =>
            buttons.Select((b, i) => (b, i)).GroupBy(x => x.i / perRow).Select(g => g.Select(x => x.b).ToArray()).ToArray();
        static object[] Back(string to = "main") => new[] { B("⬅️ Назад", "nav:" + to) };

        // короткие ключи для кнопок: путь к файлу или имя устройства не влезают в 64 байта callback_data
        static readonly Dictionary<string, string> Tok = new();
        static int _tokN;
        static string Put(string v)
        {
            lock (Tok)
            {
                foreach (var p in Tok) if (p.Value == v) return p.Key;
                if (Tok.Count > 4000) Tok.Clear();
                var k = (++_tokN).ToString("x");
                Tok[k] = v;
                return k;
            }
        }
        static string Get(string k) { lock (Tok) return Tok.TryGetValue(k, out var v) ? v : null; }
        const string Stale = "Кнопка устарела — открой меню заново: /menu";

        static void OnUi(System.Action a) => System.Windows.Application.Current?.Dispatcher.BeginInvoke(a);
        static Task<T> Ui<T>(Func<T> f) => System.Windows.Application.Current.Dispatcher.InvokeAsync(f).Task;
        static Task Ui(System.Action a) => System.Windows.Application.Current.Dispatcher.InvokeAsync(a).Task;
        static void Toast(string title, string sub) => OnUi(() => App.Island?.Toast("", title, sub));

        // ------------------------------------------------------------------
        //  Проверка, команды в меню Telegram, события острова
        // ------------------------------------------------------------------

        public override async Task<string> Test()
        {
            var me = await Call("getMe", new { });
            var bot = Integrations.S(me, "username");
            if (string.IsNullOrEmpty(V("chat")))
            {
                StopLoop();
                JsonElement up;
                try { up = await Call("getUpdates", new { limit = 20 }); }
                catch (TgError e) when (e.Code == 409)
                {
                    return $"Бот @{bot} уже подключён к другому сервису (вебхук или другая программа). Создай для N-Island отдельного бота у @BotFather";
                }
                var last = up.EnumerateArray().LastOrDefault();
                if (last.ValueKind == JsonValueKind.Undefined || !last.TryGetProperty("message", out var msg))
                    return $"Бот @{bot} работает. Напиши ему любое сообщение в Telegram и нажми «Проверить» ещё раз";
                Integrations.SetValue(this, "chat", msg.GetProperty("chat").GetProperty("id").ToString());
            }
            _commandsSet = false;
            await SetCommands();
            try
            {
                await Call("setMyShortDescription", new { short_description = T("Пульт ПК N-Island: музыка, экран, окна, файлы, питание и ИИ") });
                await Call("setMyDescription", new { description = T("Личный пульт для компьютера с N-Island. Отвечает только владельцу. /menu — пульт кнопками") });
            }
            catch { }
            await Message("✅ <b>N-Island подключён</b>\nСюда будут приходить события острова.\n/menu — пульт ПК · /help — все команды", Flag("kbd") ? ReplyKeys() : null);
            return $"Работает ✓ Бот @{bot}, сообщение отправлено в чат {V("chat")}";
        }

        static readonly (string Cmd, string Desc)[] Commands =
        {
            ("menu", "Пульт ПК кнопками"), ("status", "Состояние ПК"), ("music", "Музыка и кнопки"), ("vol", "Громкость: /vol 40"),
            ("screenshot", "Снимок экрана"), ("windows", "Открытые окна"), ("keys", "Клавиши и мышь"), ("type", "Напечатать текст на ПК"),
            ("files", "Файлы на ПК"), ("search", "Найти файл: /search отчёт"), ("top", "Что грузит ПК"), ("power", "Питание: сон, выключение"),
            ("lock", "Заблокировать ПК"), ("sysinfo", "О компьютере"), ("ip", "IP, Wi-Fi и VPN"), ("weather", "Погода"),
            ("ttn", "Посылки Новой почты"), ("note", "В заметки: /note текст"), ("todo", "Задача: /todo текст"),
            ("remind", "Напомнить: /remind 30 текст"), ("timer", "Таймер: /timer 10"), ("clip", "Буфер обмена ПК"), ("copy", "Скопировать на ПК"),
            ("say", "Сообщение в чёлку"), ("speak", "Сказать вслух на ПК"), ("ai", "Спросить ИИ"), ("find", "Найти ноутбук (звук)"),
            ("cam", "Фото с камеры"), ("where", "Где ноутбук"), ("model", "Нейросеть и модель"), ("agents", "ИИ-агенты на ПК"), ("agent", "Задание ИИ-агенту"), ("settings", "Настройки бота"), ("help", "Все команды"),
        };

        async Task SetCommands()
        {
            if (_commandsSet) return;
            try
            {
                await Call("setMyCommands", new { commands = Commands.Concat(Commands2).Select(c => new { command = c.Cmd, description = T(c.Desc) }).ToArray() });
                _commandsSet = true;
            }
            catch { }
        }

        public override async Task Send(HubEvent e)
        {
            if (string.IsNullOrEmpty(V("chat"))) return;
            var text = $"{Emoji(e.Kind)} <b>{H(e.Title)}</b>" + (e.Text != "" ? "\n" + H(e.Text) : "");
            await Message(text, e.Kind == "track" ? MusicKeys() : null, silent: e.Kind is "track" or "notify");
            Status = "Отправлено " + DateTime.Now.ToString("HH:mm");
        }

        static string Emoji(string kind) => kind switch
        {
            "timer" => "⏰", "reminder" => "📅", "call" => "📞", "notify" => "💬", "track" => "🎵", "vpn" => "🔒", "battery" => "🔋",
            "alert" => "⚠️", "net" => "🌐", "parcel" => "📦", "mail" => "✉️", "test" => "✅", _ => "🔔",
        };

        static readonly (string Label, string Cmd)[] ReplyButtons =
        {
            ("🎛 Пульт", "menu"), ("📊 Статус", "status"), ("🎵 Музыка", "music"),
            ("📸 Экран", "screenshot"), ("🪟 Окна", "windows"), ("🔒 Блок", "lock"),
        };

        static object ReplyKeys() => new
        {
            keyboard = new[]
            {
                ReplyButtons.Take(3).Select(b => new { text = T(b.Label) }).ToArray(),
                ReplyButtons.Skip(3).Select(b => new { text = T(b.Label) }).ToArray(),
            },
            resize_keyboard = true,
            is_persistent = true,
        };

        // ------------------------------------------------------------------
        //  Длинный опрос: ответ на команду за секунду
        // ------------------------------------------------------------------

        public override Task Poll()
        {
            if (string.IsNullOrEmpty(V("chat"))) return Task.CompletedTask;
            if (_loop == null || _loop.IsCancellationRequested || _loopToken != V("token")) StartLoop();
            return Task.CompletedTask;
        }

        void StartLoop()
        {
            StopLoop();
            _loop = new CancellationTokenSource();
            _loopToken = V("token");
            var ct = _loop.Token;
            _ = Task.Run(async () =>
            {
                int fails = 0;
                await SetCommands();
                Greet();
                while (!ct.IsCancellationRequested && Enabled && Ready && V("chat") != "" && V("token") == _loopToken)
                {
                    try
                    {
                        long.TryParse(St("offset"), out var offset);
                        var res = await Call("getUpdates", new { timeout = 25, limit = 30, offset, allowed_updates = new[] { "message", "callback_query" } }, ct);
                        foreach (var u in res.EnumerateArray())
                        {
                            offset = Math.Max(offset, u.GetProperty("update_id").GetInt64() + 1);
                            var upd = u.Clone();
                            // команды выполняются параллельно: долгий /find или /cmd не задерживает остальные кнопки
                            _ = Task.Run(async () =>
                            {
                                try { await Handle(upd); }
                                catch (Exception e) { Log.Info("telegram cmd: " + e.Message); try { await Message("⚠️ " + H(Cut(e.Message, 300))); } catch { } }
                            });
                        }
                        if (offset.ToString() != St("offset")) OnUi(() => SetSt("offset", offset.ToString()));
                        fails = 0;
                        Ok = true;
                        if (Status.StartsWith("Ошибка")) Status = "На связи";
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
                    catch (Exception e)
                    {
                        Ok = false;
                        Status = "Ошибка: " + (e is TgError { Code: 409 } ? "этот бот уже опрашивается в другой программе или у него вебхук"
                                             : e is TgError { Code: 401 } ? "токен бота отозван или неверный" : Integrations.Reason(e));
                        await Task.Delay(TimeSpan.FromSeconds(Math.Min(300, 5 * Math.Pow(2, Math.Min(++fails, 6)))), ct).ContinueWith(_ => { });
                    }
                }
            });
        }

        void StopLoop() { try { _loop?.Cancel(); } catch { } _loop = null; }

        static bool _greeted, _hooked;

        void Greet()
        {
            if (!_hooked)
            {
                _hooked = true;
                SystemEvents.SessionSwitch += (_, e) =>
                {
                    var what = e.Reason switch
                    {
                        SessionSwitchReason.SessionUnlock => "🔓 ПК разблокирован",
                        SessionSwitchReason.SessionLock => "🔒 ПК заблокирован",
                        SessionSwitchReason.SessionLogon => "👤 Вход в Windows",
                        SessionSwitchReason.RemoteConnect => "🖧 Удалённое подключение к ПК",
                        _ => null,
                    };
                    if (what != null) Notice(what);
                };
                SystemEvents.PowerModeChanged += (_, e) => { if (e.Mode == PowerModes.Resume) Notice("☀️ ПК проснулся"); };
            }
            if (_greeted) return;
            _greeted = true;
            if (!Flag("boot")) return;
            var up = Maint.Uptime;
            _ = Message($"💻 <b>{H(Environment.MachineName)}</b> " + (up.TotalMinutes < 15 ? "включён" : "на связи") + $" · {DateTime.Now:HH:mm}\n/menu — пульт ПК",
                        Flag("kbd") ? ReplyKeys() : null, silent: true).ContinueWith(_ => { });
        }

        void Notice(string what)
        {
            if (!Enabled || !Ready || !Flag("unlock")) return;
            _ = Message(what + " · " + DateTime.Now.ToString("HH:mm"), silent: true).ContinueWith(_ => { });
        }

        // ------------------------------------------------------------------
        //  Входящие сообщения и кнопки
        // ------------------------------------------------------------------

        string _await;
        DateTime _awaitUntil;
        static readonly AsyncLocal<long> _msgId = new();
        static long _lastMsgId { get => _msgId.Value; set => _msgId.Value = value; }
        void Await(string kind) { _await = kind; _awaitUntil = DateTime.Now.AddMinutes(3); }
        string TakeAwait() { var k = _await; _await = null; return k != null && DateTime.Now < _awaitUntil ? k : null; }

        async Task Handle(JsonElement u)
        {
            if (u.TryGetProperty("callback_query", out var cq))
            {
                if (!cq.TryGetProperty("message", out var cm) || cm.GetProperty("chat").GetProperty("id").ToString() != V("chat")) return;
                var data = Integrations.S(cq, "data");
                App.Island?.MascotReact("curious", 1.2);   // нажали кнопку в Telegram
                long msgId = cm.TryGetProperty("message_id", out var mi) ? mi.GetInt64() : 0;
                string answer;
                try { answer = await Callback(data, msgId); }
                catch (Exception e) { answer = "⚠️ " + Cut(e.Message, 180); Log.Info("telegram button: " + e.Message); }
                try { await Call("answerCallbackQuery", new { callback_query_id = Integrations.S(cq, "id"), text = T(answer ?? ""), show_alert = (answer?.Length ?? 0) > 70 }); } catch { }
                return;
            }
            if (!u.TryGetProperty("message", out var m) || m.GetProperty("chat").GetProperty("id").ToString() != V("chat")) return;   // чужие чаты не слушаем
            var text = Integrations.S(m, "text").Trim();
            App.Island?.MascotReact("wave", 1.6);   // пришла команда из Telegram — персонаж машет
            _lastMsgId = m.TryGetProperty("message_id", out var lm) ? lm.GetInt64() : 0;
            if (text == "")
            {
                var caption = Integrations.S(m, "caption").Trim();
                if (caption.StartsWith("/wall", StringComparison.OrdinalIgnoreCase)) { await Reply(await Wallpaper(m)); return; }
                // голосовое без подписи — звучит на ПК; с подписью /save — сохраняется файлом
                if (!caption.StartsWith("/save", StringComparison.OrdinalIgnoreCase) && await PlayVoice(m)) return;
                await ReceiveFile(m, caption);
                return;
            }
            if (text.StartsWith("/"))
            {
                _await = null;
                var sp = text.IndexOfAny(new[] { ' ', '\n' });
                var cmd = (sp > 0 ? text[1..sp] : text[1..]).ToLowerInvariant();
                if (cmd.Contains('@')) cmd = cmd[..cmd.IndexOf('@')];
                await Reply(await Command(cmd, sp > 0 ? text[(sp + 1)..].Trim() : ""));
                return;
            }
            if (TakeAwait() is { } kind)
            {
                if (text.ToLowerInvariant() is "отмена" or "cancel" or "скасувати" or "-") { await Message("Отменено"); return; }
                await Reply(await Command(kind, text));
                return;
            }
            var btn = ReplyButtons.FirstOrDefault(b => b.Label == text || T(b.Label) == text);
            if (btn.Cmd != null) { await Reply(await Command(btn.Cmd, "")); return; }
            if (Flag("aitext")) { await Reply(await AskAi(text)); return; }

            // обычный текст с телефона → в остров (ссылку можно открыть, текст — скопировать)
            bool link = text.StartsWith("http://") || text.StartsWith("https://");
            OnUi(() =>
            {
                Rows.Insert(0, new HubRow
                {
                    Title = Cut(text, 120), Sub = "из Telegram · " + DateTime.Now.ToString("HH:mm"), Url = link ? text : null,
                    ActionText = "Копировать", Action = () => { ClipboardService.I.CopyBack(text); return Task.FromResult<string>("Скопировано"); },
                });
                if (Rows.Count > 8) Rows.RemoveAt(Rows.Count - 1);
                Notify("Telegram", text, link ? text : null);
                Integrations.RaiseChanged();
            });
        }

        Task Reply((string Html, object Keys)? r) => r?.Html != null ? Message(r.Value.Html, r.Value.Keys) : Task.CompletedTask;

        static readonly Dictionary<string, string> FlagNames = new()
        {
            ["control"] = "Управление с телефона", ["apps"] = "Программы и окна", ["input"] = "Клавиатура и мышь", ["files"] = "Файлы",
            ["screen"] = "Снимок экрана", ["camera"] = "Камера и местоположение", ["power"] = "Питание", ["shell"] = "Команды Windows",
        };

        /// <summary>null — можно; иначе понятный отказ. Разрешения меняются только на самом ПК.</summary>
        string Need(string flag) => Flag(flag) ? null
            : $"🔐 «{FlagNames[flag]}» не разрешено. Включить можно только на ПК: N-Island → Настройки → Интеграции → Telegram.";

        // ------------------------------------------------------------------
        //  Команды
        // ------------------------------------------------------------------

        async Task<(string Html, object Keys)?> Command(string cmd, string arg)
        {
            var m = MediaService.I;
            string no;
            switch (cmd)
            {
                case "start":
                    if (Flag("kbd")) await Message("👋 Привет! Кнопки внизу — быстрые действия.", ReplyKeys());
                    return await Menu("main");
                case "menu": case "panel": case "pult": return await Menu("main");
                case "help": return (HelpText(), K(Row(B("🎛 Открыть пульт", "nav:main"))));
                case "settings": case "set": return await Menu("set");
                case "keyboard":
                    await Message("Кнопки внизу чата включены", ReplyKeys());
                    return null;
                case "nokeyboard":
                    await Message("Кнопки убраны", new { remove_keyboard = true });
                    return null;

                // --- состояние
                case "status": return (await Task.Run(StatusText), K(Row(B("🔄 Обновить", "status!"), B("🎛 Пульт", "nav:main"))));
                case "top": return await Task.Run(TopMenu);
                case "disk": case "disks": return (DisksText(), null);
                case "sysinfo": case "info": case "pc": return (await Task.Run(PcControl.SysInfo), null);
                case "uptime": var up = Maint.Uptime; return ($"⏱ Работает {(up.TotalDays >= 1 ? (int)up.TotalDays + " дн " : "")}{up.Hours} ч {up.Minutes} мин", null);
                case "battery": return (await BatteryText(), null);
                case "wifi": return (await WifiText(), null);
                case "ip": case "net": return await Menu("net");
                case "ping": return (await PingText(arg), null);

                // --- музыка и звук
                case "music": case "now": return await Menu("music");
                case "lyrics": case "text":
                    return (m.Lyrics is { Count: > 0 } ly
                        ? $"🎤 <b>{H(m.TrackLine)}</b>\n\n" + H(Cut(string.Join("\n", ly.Select(l => l.Text)), 3800))
                        : "Текст песни не найден", null);
                case "play": case "pause": case "next": case "prev":
                    if ((no = Need("control")) != null) return (no, null);
                    await Ui(() => { if (cmd == "next") m.Next(); else if (cmd == "prev") m.Prev(); else if (cmd == "play") m.Play(); else m.Pause(); });
                    return (cmd switch { "next" => "⏭ Следующий трек", "prev" => "⏮ Предыдущий трек", "play" => "▶ Играет", _ => "⏸ Пауза" }, null);
                case "vol": case "volume":
                    if (arg == "") return await Menu("sound");
                    if ((no = Need("control")) != null) return (no, null);
                    if (!int.TryParse(arg.TrimEnd('%'), out var v)) return ("Напиши так: /vol 40", null);
                    v = Math.Clamp(v, 0, 100);
                    await Ui(() => AudioService.I.SetVolume(v / 100.0));
                    return ($"🔊 Громкость {v}%", null);
                case "mute":
                    if ((no = Need("control")) != null) return (no, null);
                    return (await Ui(() => { bool was = AudioService.I.Muted; AudioService.I.ToggleMute(); return was ? "🔊 Звук включён" : "🔇 Звук выключен"; }), null);
                case "mic":
                    if ((no = Need("control")) != null) return (no, null);
                    return (await Ui(() => AudioService.I.ToggleMicMute()) ? "🎙 Микрофон выключен" : "🎙 Микрофон включён", null);
                case "sound": case "output": return await Menu("sound");
                case "eq":
                    if (arg == "") return await Menu("eq");
                    if ((no = Need("control")) != null) return (no, null);
                    if (arg != "off" && !Equalizer.Presets.Any(p => p.Key == arg)) return ("Нет такого пресета. Есть: " + string.Join(", ", Equalizer.Presets.Select(p => p.Key)) + ", off", null);
                    await Ui(() => { if (arg == "off") Equalizer.SetOn(false); else Equalizer.SetPreset(arg); });
                    return (Equalizer.Installed ? "🎚 Готово: " + H(arg == "off" ? "выключен" : Equalizer.PresetName(arg)) : "🎚 Сохранено, но на ПК не установлен движок Equalizer APO", null);

                // --- экран
                case "screenshot": case "screen": case "shot":
                    if ((no = Need("screen")) != null) return (no, null);
                    await Screenshot(arg is "win" or "window" or "окно" ? -1 : int.TryParse(arg, out var scr) ? scr : 0, arg == "png");
                    return null;
                case "brightness": case "bri":
                    if (arg == "") return await Menu("screen");
                    if ((no = Need("control")) != null) return (no, null);
                    if (!int.TryParse(arg.TrimEnd('%'), out var bri)) return ("Напиши так: /brightness 70", null);
                    SystemService.I.SetBrightness(Math.Clamp(bri, 0, 100));
                    return ($"🔆 Яркость {Math.Clamp(bri, 0, 100)}%", null);
                case "monitor":
                    if ((no = Need("control")) != null) return (no, null);
                    if (arg is "on" or "вкл") { PcControl.MonitorOn(); return ("🖥 Экран включён", null); }
                    PcControl.MonitorOff();
                    return ("🌑 Экран выключен — проснётся от мыши или /monitor on", null);
                case "theme":
                    if ((no = Need("control")) != null) return (no, null);
                    await Ui(() => SystemService.I.ToggleTheme());
                    return (SystemService.I.IsLightTheme ? "☀️ Светлая тема Windows" : "🌙 Тёмная тема Windows", null);
                case "wallpaper": case "wall":
                    return ("🖼 Пришли картинку с подписью /wallpaper — она станет обоями рабочего стола", null);

                // --- окна, программы
                case "windows": case "win": return await Menu("win");
                case "apps":
                    if ((no = Need("apps")) != null) return (no, null);
                    return await Menu("win");
                case "kill":
                    if ((no = Need("apps")) != null) return (no, null);
                    return (await Task.Run(() => Kill(arg)), null);
                case "open": case "run":
                    if ((no = Need("apps")) != null) return (no, null);
                    if (arg == "") { Await("open"); return ("🚀 Что открыть? Ссылку, программу (notepad, calc) или путь к файлу", null); }
                    await Ui(() => { SystemService.Run(arg); App.Island?.Toast("", "Открыто из Telegram", arg); });
                    return ("🚀 Открываю: " + H(arg), null);
                case "url":
                    if ((no = Need("control")) != null) return (no, null);
                    var site = arg.StartsWith("http") ? arg : "https://" + arg;
                    await Ui(() => SystemService.Run(site));
                    return ("🌐 Открыто в браузере: " + H(site), null);

                // --- клавиатура и мышь
                case "keys": case "input": return await Menu("input");
                case "mouse": return await Menu("mouse");
                case "key": case "hotkey":
                    if ((no = Need("input")) != null) return (no, null);
                    if (arg == "") return ("Напиши так: /key ctrl+c  ·  /key alt+tab  ·  /key f5", null);
                    return (PcControl.Hotkey(arg) is { } kerr ? "⚠️ " + H(kerr) : "⌨️ Нажато: " + H(arg), null);
                case "type":
                    if ((no = Need("input")) != null) return (no, null);
                    if (arg == "") { Await("type"); return ($"⌨️ Напиши текст — он напечатается в активном окне ({H(Cut(PcControl.ActiveTitle(), 60))}). «отмена» — передумал", null); }
                    PcControl.Type(arg);
                    return ("⌨️ Напечатано в «" + H(Cut(PcControl.ActiveTitle(), 60)) + "»", null);

                // --- файлы
                case "files": case "file": return await Menu("files");
                case "ls":
                    if ((no = Need("files")) != null) return (no, null);
                    return Dir(Expand(arg), 0);
                case "get":
                    if ((no = Need("files")) != null) return (no, null);
                    return (await SendPath(Expand(arg)), null);
                case "search": case "findfile":
                    if ((no = Need("files")) != null) return (no, null);
                    if (arg == "") { Await("search"); return ("🔎 Какой файл найти? Напиши часть имени", null); }
                    return await Task.Run(() => SearchFiles(arg));

                // --- питание
                case "power": return await Menu("power");
                case "lock":
                    if ((no = Need("power")) != null) return (no, null);
                    LockWorkStation();
                    return ("🔒 ПК заблокирован", null);
                case "sleep": case "hibernate":
                    if ((no = Need("power")) != null) return (no, null);
                    Suspend(cmd == "hibernate");
                    return (cmd == "sleep" ? "😴 ПК засыпает" : "🛌 Гибернация", null);
                case "restart": case "reboot": case "shutdown": case "logoff": case "logout":
                {
                    if ((no = Need("power")) != null) return (no, null);
                    var what = cmd switch { "shutdown" => "shutdown", "logoff" or "logout" => "logoff", _ => "restart" };
                    if (cmd == "shutdown" && int.TryParse(arg, out var inMin) && inMin > 0) return (ShutdownIn(inMin), K(Row(B("✖ Отменить", "pw:cancel"))));
                    return (what switch { "shutdown" => "⚠️ Выключить ПК? Несохранённое пропадёт.", "logoff" => "⚠️ Выйти из Windows? Программы закроются.", _ => "⚠️ Перезагрузить ПК? Несохранённое пропадёт." },
                            K(Row(B("Да", "pw:" + what + "!"), B("Отмена", "pw:no"))));
                }
                case "cancel":
                    if ((no = Need("power")) != null) return (no, null);
                    Maint.RunCmd("shutdown.exe", "/a");
                    return ("✖ Выключение отменено", null);

                // --- система
                case "dnd":
                    if ((no = Need("control")) != null) return (no, null);
                    return (await Ui(() => { App.Island?.SetMode(Store.Settings.Mode == "dnd" ? "normal" : "dnd"); return Store.Settings.Mode == "dnd"; }) ? "🌙 «Не беспокоить» включён" : "🔔 «Не беспокоить» выключен", null);
                case "mode":
                    if (arg == "") return await Menu("modes");
                    if ((no = Need("control")) != null) return (no, null);
                    if (!Modes.All.Any(x => x.Key == arg)) return ("Нет такого режима. Есть: " + string.Join(", ", Modes.All.Select(x => x.Key)), null);
                    await Ui(() => App.Island?.SetMode(arg));
                    return ("Режим: " + H(T(Modes.All.First(x => x.Key == arg).Name)), null);
                case "awake":
                    if ((no = Need("control")) != null) return (no, null);
                    bool on = arg is "on" or "1" or "да" or "вкл" || (arg == "" && !SystemService.I.KeepAwake);
                    await Ui(() => SystemService.I.KeepAwake = on);
                    return (on ? "☕ ПК не будет засыпать" : "💤 ПК снова засыпает как обычно", null);
                case "ram":
                    if ((no = Need("control")) != null) return (no, null);
                    var freed = await Maint.OptimizeRam();
                    return ($"🧠 Освобождено {Maint.Size(freed)} памяти", null);
                case "dns":
                    if ((no = Need("control")) != null) return (no, null);
                    return (Maint.FlushDns() ? "🌐 Кеш DNS сброшен" : "Не удалось сбросить DNS", null);
                case "plan": case "powerplan": return await Menu("plans");

                // --- полезное
                case "note":
                    if ((no = Need("control")) != null) return (no, null);
                    if (arg == "") return (Store.Data.Notes is { Length: > 0 } notes ? "📝 <b>Заметки</b>\n" + H(Cut(notes, 3800)) : "📝 Заметок нет. Добавить: /note текст", null);
                    await Ui(() => { Store.Data.Notes = (Store.Data.Notes ?? "").TrimEnd() + (string.IsNullOrWhiteSpace(Store.Data.Notes) ? "" : "\n") + arg; Store.SaveData(); });
                    return ("📝 Добавлено в заметки", null);
                case "todo":
                    if (arg == "") return await Menu("todo");
                    if ((no = Need("control")) != null) return (no, null);
                    await Ui(() => { Store.Data.Todos.Insert(0, new TodoItem { Text = arg }); Store.SaveData(); });
                    return ("✅ Задача добавлена", K(Row(B("📋 Все задачи", "nav:todo"))));
                case "remind": return (await Remind(arg), null);
                case "timer":
                    if (arg == "") return await Menu("timer");
                    if ((no = Need("control")) != null) return (no, null);
                    if (!double.TryParse(arg.Replace(',', '.').TrimEnd('m', 'м'), NumberStyles.Float, CultureInfo.InvariantCulture, out var min) || min <= 0) return ("Напиши так: /timer 10  (минуты)", null);
                    await Ui(() => { var t = TimerService.I; t.Reset(); t.SetMinutes(min); t.Start(); });
                    return ($"⏱ Таймер на {min:0.#} мин запущен", K(Row(B("⏹ Остановить", "tm:stop"))));
                case "clip":
                    if ((no = Need("control")) != null) return (no, null);
                    var clip = await Ui(() => { try { return System.Windows.Clipboard.ContainsText() ? System.Windows.Clipboard.GetText() : null; } catch { return null; } });
                    return (clip == null ? "📋 В буфере обмена ПК нет текста" : "📋 <code>" + H(Cut(clip, 3500)) + "</code>", null);
                case "copy":
                    if ((no = Need("control")) != null) return (no, null);
                    if (arg == "") { Await("copy"); return ("📋 Пришли текст — он окажется в буфере обмена ПК", null); }
                    await Ui(() => { ClipboardService.I.CopyBack(arg); App.Island?.Toast("", "Скопировано с телефона", Cut(arg, 60)); });
                    return ("📋 Скопировано на ПК", null);
                case "say":
                    if (arg == "") { Await("say"); return ("💬 Что показать в чёлке?", null); }
                    await Ui(() =>
                    {
                        App.Island?.ShowActivity(new Activity { Kind = "hub", Glyph = Glyph, Tint = System.Windows.Media.Color.FromRgb(0x2A, 0xAB, 0xEE), Title = "Сообщение с телефона", Sub = arg, Seconds = 15, Wide = true });
                        Sound.Play(SoundEvent.Notification);
                    });
                    return ("💬 Показано в чёлке", null);
                case "speak": case "tts":
                    if ((no = Need("control")) != null) return (no, null);
                    if (arg == "") { Await("speak"); return ("🗣 Что сказать вслух на ПК?", null); }
                    Toast("Говорю с телефона", Cut(arg, 80));
                    await PcControl.Speak(Cut(arg, 1500));
                    return ("🗣 Сказано вслух", null);
                case "find":
                    if ((no = Need("control")) != null) return (no, null);
                    await Ui(() =>
                    {
                        AudioService.I.SetVolume(Math.Max(AudioService.I.Volume, 0.8));
                        App.Island?.ShowActivity(new Activity { Kind = "alert", Glyph = "", Tint = System.Windows.Media.Color.FromRgb(0xFF, 0x9F, 0x0A), Title = "Ищут этот ноутбук", Sub = "Сигнал из Telegram", Seconds = 10, Wide = true });
                    });
                    for (int i = 0; i < 6; i++) { await Ui(() => Sound.Play(SoundEvent.Alert)); await Task.Delay(1300); }
                    return ("🔔 Сигнал проигран", K(Row(B("🔔 Ещё раз", "do:find"), Flag("camera") ? B("📷 Фото с камеры", "do:cam") : null, Flag("camera") ? B("📍 Где он", "do:where") : null)));
                case "cam": case "camera": case "webcam":
                    if ((no = Need("camera")) != null) return (no, null);
                    await CameraShot();
                    return null;
                case "where": case "location": case "geo":
                    if ((no = Need("camera")) != null) return (no, null);
                    return (await Where(), null);
                case "weather": return (await WeatherText(), null);
                case "ttn": case "np": return (await ParcelsText(), null);
                case "ai": case "ask": case "gpt":
                    if (arg == "") { Await("ai"); return ($"🤖 Спроси что угодно — ответит {H(Ai.Current.Name)}. «отмена» — передумал", null); }
                    return await AskAi(arg);
                case "ainew": lock (AiHistory) AiHistory.Clear(); if (Store.Settings.AiShareTelegram) await Ui(Ai.ClearChat); return ("🤖 Новый диалог", null);
                case "model": case "models": case "provider": return await Menu("ai");

                // --- ИИ-агенты на ПК (Claude Code, Codex…)
                case "agents":
                {
                    var run = await Agents.Running();
                    var sb = new StringBuilder("🧑‍💻 <b>ИИ-агенты на ПК</b>\n");
                    sb.Append(run.Count == 0 ? "Сейчас никто не работает\n" : string.Join("\n", run.Select(r => $"▶ {H(r.Agent)}" + (r.Started > DateTime.MinValue ? $" · с {r.Started:HH:mm}" : ""))) + "\n");
                    var evs = (Store.Data.AgentEvents ?? new()).Take(6).ToList();
                    if (evs.Count > 0) sb.Append("\n<b>Последние события</b>\n" + string.Join("\n", evs.Select(e => $"{e.At:HH:mm} {H(e.Agent)}: {H(Cut(e.Message, 80))}" + (string.IsNullOrEmpty(e.Folder) ? "" : " · " + H(Path.GetFileName(e.Folder.TrimEnd('\\', '/')))))));
                    var f = Agents.Folders.FirstOrDefault();
                    sb.Append("\n\nЗадание агенту: /agent текст" + (f != null ? $" (папка {H(Path.GetFileName(f.TrimEnd('\\')))})" : ""));
                    return (sb.ToString(), null);
                }
                case "agent":
                {
                    if ((no = Need("shell")) != null) return (no, null);
                    var folder = Agents.Folders.FirstOrDefault();
                    if (folder == null) return ("Сначала запусти агента на ПК в папке проекта: N-Island → Настройки → ИИ-агенты", null);
                    if (arg == "") { Await("agent"); return ($"🧑‍💻 Что сделать в проекте «{H(Path.GetFileName(folder.TrimEnd('\\')))}»? Напиши задание", null); }
                    var list = await Agents.Detect();
                    var a = list.FirstOrDefault(x => x.Kind.Key == Store.Settings.AgentDefault) ?? list.FirstOrDefault();
                    if (a == null) return ("На ПК не найден ни один ИИ-агент (Claude Code, Codex, Gemini CLI…)", null);
                    Toast(a.Kind.Name + " получил задание из Telegram", Cut(arg, 80));
                    await Message($"🧑‍💻 {H(a.Kind.Name)} работает в «{H(Path.GetFileName(folder.TrimEnd('\\')))}»… Пришлю ответ, когда закончит (до 15 мин)");
                    var task = arg;
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var ans = await Agents.Headless(a, folder, task, TimeSpan.FromMinutes(15));
                            await Message($"🧑‍💻 <b>{H(a.Kind.Name)}</b> · {H(Path.GetFileName(folder.TrimEnd('\\')))}\n\n" + Md(Cut(ans, 3700)), K(Row(Copy("📱 Копировать", ans))), translate: false);
                        }
                        catch (Exception e) { try { await Message("⚠️ " + H(e.Message)); } catch { } }
                    });
                    return null;
                }

                // --- консоль
                case "cmd": case "ps":
                    if ((no = Need("shell")) != null) return (no, null);
                    if (arg == "") { Await(cmd); return ("Напиши команду, например: " + (cmd == "ps" ? "Get-Date" : "ipconfig"), null); }
                    await Action("typing");
                    return (await Shell(cmd == "ps", arg), null);
                default:
                    if (await Command2(cmd, arg) is { } r2) return r2;
                    if (Commands2.Any(c => c.Cmd == cmd) || cmd is "cover" or "dash" or "stream" or "scenario" or "clips" or "played" or "defender" or "translate" or "c" or "wol" or "speedtest" or "bt" or "bluetooth" or "airplane" or "autorun")
                        return null;   // команда 2.0 ответила сама (фото, живая панель)
                    return ("Не знаю такой команды. /help — список, /menu — пульт", null);
            }
        }

        string HelpText()
        {
            string Lock(string flag) => Flag(flag) ? "" : " 🔐";
            return "<b>N-Island — пульт ПК</b>\n/menu — всё кнопками\n\n" +
                   "<b>Состояние</b>\n/status · /top · /sysinfo · /disk · /battery · /wifi · /ip · /ping\n\n" +
                   "<b>Музыка и звук</b>" + Lock("control") + "\n/music · /play /pause /next /prev · /vol 40 · /mute · /mic · /eq bass · /lyrics\n\n" +
                   "<b>Экран</b>\n/screenshot (1, 2, win, png)" + Lock("screen") + " · /brightness 70 · /monitor off · /theme · картинка с подписью /wallpaper\n\n" +
                   "<b>Окна и программы</b>" + Lock("apps") + "\n/windows · /kill имя · /open notepad · /url сайт\n\n" +
                   "<b>Клавиатура и мышь</b>" + Lock("input") + "\n/keys · /mouse · /key ctrl+c · /type текст\n\n" +
                   "<b>Файлы</b>" + Lock("files") + "\n/files · /search имя · /ls папка · /get путь · пришли файл — сохранится на ПК\n\n" +
                   "<b>Питание</b>" + Lock("power") + "\n/lock · /sleep · /hibernate · /restart · /shutdown · /shutdown 30 · /logoff · /cancel\n\n" +
                   "<b>Полезное</b>\n/note · /todo · /remind 30 текст · /remind 18:30 текст · /timer 10 · /clip · /copy · /say · /speak · /find · /cam · /where" +
                   "\n/weather · /ttn · /ai вопрос · /mode · /dnd · /awake · /ram · /dns\n\n" +
                   "<b>Консоль и ИИ-агенты</b>" + Lock("shell") + "\n/cmd ipconfig · /ps Get-Date · /agents · /agent задание (Claude Code, Codex…)\n\n" +
                   "<b>✨ Новое</b>\n/do громкость 30 и выключи экран — умная команда обычными словами\n/now — трек с обложкой · /live — живая панель · /watch — живой экран" + Lock("screen") + " · /scene — сценарии\n" +
                   "/in 10m /lock · /at 23:30 /shutdown 1 · /jobs · /cliphist · /history · /health · /av\n" +
                   "/tr en текст · /calc 2*21 · /qr текст · /speed · /radio · /wake MAC · /startup · /pin · /seek 1:30 · /shuffle · /repeat\n" +
                   "🎙 Голосовое боту — прозвучит на ПК (с подписью /save — сохранится файлом)\n\n" +
                   "/settings — что присылать на телефон\n🔐 — выключено, включается только на ПК";
        }

        // ------------------------------------------------------------------
        //  Меню-пульт
        // ------------------------------------------------------------------

        async Task<(string, object)> Menu(string name)
        {
            var m = MediaService.I;
            var a = AudioService.I;
            switch (name)
            {
                case "main":
                    return (await Task.Run(() => Dashboard()),
                        K(Row(B("🎵 Музыка", "nav:music"), B("🔊 Звук", "nav:sound"), B("🖥 Экран", "nav:screen")),
                          Row(B("🪟 Окна", "nav:win"), B("⌨️ Клавиши", "nav:input"), B("📁 Файлы", "nav:files")),
                          Row(B("⚙️ Система", "nav:sys"), B("⚡ Питание", "nav:power"), B("🌐 Сеть", "nav:net")),
                          Row(B("🧰 Полезное", "nav:tools"), B("🧠 ИИ", "nav:ai"), B("🎬 Сценарии", "nav:scenes")),
                          Row(B("📡 Живая панель", "do2:live"), B("✨ Ещё", "nav:more"), B("🔧 Настройки", "nav:set")),
                          Row(B("🔄 Обновить", "nav:main"))));

                case "music":
                {
                    var sb = new StringBuilder();
                    if (m.HasSession)
                    {
                        sb.Append($"🎵 <b>{H(m.Title)}</b>\n{H(m.Artist)}\n{H(m.AppName)} · {(m.IsPlaying ? "▶ играет" : "⏸ пауза")}");
                        if (m.HasTime) sb.Append($"\n<code>{TrackBar(m.Position, m.Duration)}</code>");
                        if (m.CurrentLyric is { } line) sb.Append($"\n🎤 <i>{H(line)}</i>");
                    }
                    else sb.Append("🎵 Сейчас ничего не играет");
                    sb.Append($"\n🔊 {Math.Round(a.Volume * 100)}%{(a.Muted ? " (без звука)" : "")}");
                    return (sb.ToString(), K(
                        Row(B("⏮", "mu:prev|music"), B(m.IsPlaying ? "⏸" : "▶", "mu:play|music"), B("⏭", "mu:next|music")),
                        Row(B("⏪ 10 с", "mu:back|music"), B(m.IsFavorite ? "💖" : "🤍", "mu:fav|music"), B("⏩ 10 с", "mu:fwd|music")),
                        Row(B("🔉 −10", "au:vol-|music"), B(a.Muted ? "🔈" : "🔇", "au:mute|music"), B("🔊 +10", "au:vol+|music")),
                        Row(B("📝 Текст песни", "do:lyrics"), B("🖼 С обложкой", "do2:now"), B("🎚 Эквалайзер", "nav:eq")),
                        m.CanShuffle || m.CanRepeat ? Row(m.CanShuffle ? B(m.Shuffle ? "🔀 Перемешано" : "🔀 Перемешать", "do2:shuffle") : null, m.CanRepeat ? B(m.Repeat == 0 ? "🔁 Без повтора" : m.Repeat == 1 ? "🔂 Повтор трека" : "🔁 Повтор всего", "do2:repeat") : null) : null,
                        Row(B("🔄", "nav:music"), B("⬅️ Назад", "nav:main"))));
                }

                case "eq":
                {
                    var s = Store.Settings;
                    var text = "🎚 <b>Эквалайзер</b>: " + (s.EqOn ? H(T(Equalizer.PresetName(s.EqPreset))) : "выключен") +
                               (Equalizer.Installed ? "" : "\n⚠️ На ПК не установлен движок Equalizer APO — настройка сохранится, но звук не изменится");
                    var btns = Equalizer.Presets.Select(p => B((s.EqOn && s.EqPreset == p.Key ? "✅ " : "") + p.Name, "eq:" + p.Key + "|eq")).ToList();
                    btns.Add(B(s.EqOn ? "⏻ Выключить" : "⏻ Включить", "eq:" + (s.EqOn ? "off" : "on") + "|eq"));
                    return (text, K(Grid(btns, 3).Append(Back("music")).ToArray()));
                }

                case "sound":
                {
                    var outs = a.Outputs();
                    var text = $"🔊 <b>Звук</b>: {Math.Round(a.Volume * 100)}%{(a.Muted ? " (без звука)" : "")}\n🎧 {H(a.OutputName)}\n🎙 Микрофон {(a.MicMuted ? "выключен" : "включён")}";
                    return (text, K(
                        Row(B("🔉 −10", "au:vol-|sound"), B(a.Muted ? "🔈 Вкл. звук" : "🔇 Без звука", "au:mute|sound"), B("🔊 +10", "au:vol+|sound")),
                        Row(B("0", "au:v0|sound"), B("25", "au:v25|sound"), B("50", "au:v50|sound"), B("75", "au:v75|sound"), B("100", "au:v100|sound")),
                        Row(B(a.MicMuted ? "🎙 Включить микрофон" : "🎙 Выключить микрофон", "au:mic|sound")),
                        outs.Count > 1 ? Row(B("🎧 Устройство вывода", "nav:outs")) : null,
                        Row(B("🎛 Громкость программ", "nav:mixer")),
                        Back()));
                }

                case "outs":
                {
                    var outs = a.Outputs();
                    var btns = outs.Select(o => (object)new[] { B((o.IsDefault ? "✅ " : "") + Cut(o.Name, 40), "out:" + Put(o.Id)) }).Cast<object[]>().ToList();
                    btns.Add(Back("sound"));
                    return ("🎧 <b>Куда выводить звук</b>", K(btns.ToArray()));
                }

                case "mixer":
                {
                    var apps = a.Apps().Take(10).ToList();
                    var rows = new List<object[]>();
                    foreach (var x in apps)
                    {
                        var t = Put(x.Name);
                        rows.Add(Row(B($"{(x.Muted ? "🔇" : "🔈")} {Cut(x.Name, 18)} {Math.Round(x.Volume * 100)}%", "mx:" + t + ":m|mixer"),
                                     B("−", "mx:" + t + ":-|mixer"), B("+", "mx:" + t + ":+|mixer")));
                    }
                    rows.Add(Row(B("🔄", "nav:mixer"), B("⬅️ Назад", "nav:sound")));
                    return (apps.Count == 0 ? "🎛 Сейчас никакая программа не издаёт звук" : "🎛 <b>Громкость программ</b>\nНажми на имя — без звука", K(rows.ToArray()));
                }

                case "screen":
                {
                    var bri = SystemService.I.GetBrightness();
                    var text = $"🖥 <b>Экран</b>\n🔆 Яркость {(bri?.ToString() ?? "—")}%{(bri == null ? " (у внешнего монитора не меняется)" : "")}\n" +
                               $"{(SystemService.I.IsLightTheme ? "☀️ Светлая" : "🌙 Тёмная")} тема · мониторов: {PcControl.ScreenCount}";
                    var shots = PcControl.ScreenCount > 1
                        ? Row(B("📸 Все экраны", "sh:0"), B("1️⃣", "sh:1"), B("2️⃣", "sh:2"), B("🪟 Окно", "sh:-1"))
                        : Row(B("📸 Снимок", "sh:0"), B("🪟 Активное окно", "sh:-1"), B("🖼 PNG", "sh:png"));
                    return (text, K(
                        Row(B("🔅 −20", "br:-|screen"), B("🔆 +20", "br:+|screen")),
                        Row(B("10%", "br:10|screen"), B("40%", "br:40|screen"), B("70%", "br:70|screen"), B("100%", "br:100|screen")),
                        shots,
                        Row(B("🌗 Сменить тему", "do:theme|screen"), B("🌑 Выключить экран", "do:monoff")),
                        Row(B("🖼 Обои", "do:wallhelp")),
                        Back()));
                }

                case "win":
                {
                    if (Need("apps") is { } no) return (no, K(Back()));
                    var wins = await Task.Run(PcControl.ListWindows);
                    var rows = wins.Take(14).Select(w => Row(B((w.Active ? "▶ " : w.Minimized ? "▫️ " : "🪟 ") + Cut(w.Title, 34), "w:" + w.Handle.ToInt64()))).ToList();
                    rows.Add(Row(B("⬇ Свернуть всё", "kb:win+d|win"), B("🔁 Alt+Tab", "kb:alt+tab|win")));
                    rows.Add(Row(B("🔥 Что грузит ПК", "nav:top"), B("🔄", "nav:win"), B("⬅️ Назад", "nav:main")));
                    return ($"🪟 <b>Открытые окна</b> ({wins.Count})\n▶ — активное, ▫️ — свёрнутое. Нажми на окно — действия", K(rows.ToArray()));
                }

                case "top": return await Task.Run(TopMenu);

                case "input":
                {
                    if (Need("input") is { } no) return (no, K(Back()));
                    return ($"⌨️ <b>Клавиши</b> — нажимаются в активном окне:\n«{H(Cut(PcControl.ActiveTitle(), 60))}»", K(
                        Row(B("Esc", "kb:esc|input"), B("Tab", "kb:tab|input"), B("⌫", "kb:backspace|input"), B("Enter ↵", "kb:enter|input")),
                        Row(B("⬅", "kb:left|input"), B("⬆", "kb:up|input"), B("⬇", "kb:down|input"), B("➡", "kb:right|input")),
                        Row(B("␣ Пробел", "kb:space|input"), B("PgUp", "kb:pgup|input"), B("PgDn", "kb:pgdn|input"), B("F5", "kb:f5|input")),
                        Row(B("Ctrl+C", "kb:ctrl+c|input"), B("Ctrl+V", "kb:ctrl+v|input"), B("Ctrl+Z", "kb:ctrl+z|input"), B("Ctrl+S", "kb:ctrl+s|input")),
                        Row(B("Alt+Tab", "kb:alt+tab|input"), B("Win+D", "kb:win+d|input"), B("F11", "kb:f11|input"), B("Alt+F4", "kb:alt+f4|input")),
                        Row(B("🔉", "kb:voldown"), B("⏯", "kb:play"), B("🔊", "kb:volup")),
                        Row(B("⌨️ Ввести текст", "ask:type"), B("🖱 Мышь", "nav:mouse"), B("🎞 Презентация", "nav:slides")),
                        Row(B("🔄", "nav:input"), B("⬅️ Назад", "nav:main"))));
                }

                case "slides":
                    if (Need("input") is { } nos) return (nos, K(Back()));
                    return ("🎞 <b>Презентация</b>\nPowerPoint, PDF, Google Slides — листай с телефона", K(
                        Row(B("⏮ Назад", "kb:left"), B("Вперёд ⏭", "kb:right")),
                        Row(B("▶ Начать (F5)", "kb:f5"), B("⬛ Чёрный экран (B)", "kb:b"), B("⏹ Выйти (Esc)", "kb:esc")),
                        Row(B("⬅️ Назад", "nav:input"))));

                case "mouse":
                {
                    if (Need("input") is { } no) return (no, K(Back()));
                    int st = _mouseStep;
                    return ($"🖱 <b>Мышь</b> · шаг {st} px", K(
                        Row(B("↖", $"ms:{-st}:{-st}"), B("⬆", $"ms:0:{-st}"), B("↗", $"ms:{st}:{-st}")),
                        Row(B("⬅", $"ms:{-st}:0"), B("🖱 Клик", "mc:l"), B("➡", $"ms:{st}:0")),
                        Row(B("↙", $"ms:{-st}:{st}"), B("⬇", $"ms:0:{st}"), B("↘", $"ms:{st}:{st}")),
                        Row(B("Правый клик", "mc:r"), B("Двойной", "mc:d"), B("⊙ В центр", "mc:c")),
                        Row(B("🔼 Прокрутка", "mc:up"), B("🔽 Прокрутка", "mc:down")),
                        Row(B(st == 20 ? "✅ 20" : "20", "mstep:20"), B(st == 100 ? "✅ 100" : "100", "mstep:100"), B(st == 400 ? "✅ 400" : "400", "mstep:400")),
                        Row(B("⬅️ Назад", "nav:input"))));
                }

                case "files":
                {
                    if (Need("files") is { } no) return (no, K(Back()));
                    var places = PcControl.Places.Select(p => B(p.Name, "d:" + Put(p.Path()))).ToList();
                    places.AddRange(DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType is DriveType.Fixed or DriveType.Removable)
                        .Select(d => B("💽 " + d.Name.TrimEnd('\\'), "d:" + Put(d.RootDirectory.FullName))));
                    var rows = Grid(places, 2).ToList();
                    rows.Add(Row(B("🔎 Найти файл", "ask:search"), B("⬇️ Новое в Загрузках", "do:recent")));
                    rows.Add(Back());
                    return ("📁 <b>Файлы</b>\nПришли файл или фото боту — сохранится в «Загрузки\\N-Island»", K(rows.ToArray()));
                }

                case "sys":
                {
                    var text = await Task.Run(StatusText);
                    return (text, K(
                        Row(B("🔥 Что грузит", "nav:top"), B("ℹ️ О ПК", "do:sysinfo"), B("💾 Диски", "do:disk")),
                        Row(B("🧠 Освободить память", "do:ram"), B("🌐 Сбросить DNS", "do:dns")),
                        Row(B(SystemService.I.KeepAwake ? "☕ Не засыпать: вкл" : "💤 Не засыпать: выкл", "do:awake|sys"), B(Store.Settings.Mode == "dnd" ? "🌙 Не беспокоить: вкл" : "🔔 Не беспокоить: выкл", "do:dnd|sys")),
                        Row(B("🌙 Режим острова", "nav:modes"), B("⚡ Электропитание", "nav:plans")),
                        Row(B("🔄", "nav:sys"), B("⬅️ Назад", "nav:main"))));
                }

                case "modes":
                {
                    var btns = Modes.All.Select(x => B((Store.Settings.Mode == x.Key ? "✅ " : "") + x.Name, "mode:" + x.Key + "|modes")).ToList();
                    return ("🌙 <b>Режим острова</b>\n" + H(T(Modes.Current.Desc)), K(Grid(btns, 2).Append(Back("sys")).ToArray()));
                }

                case "plans":
                {
                    var plans = await Task.Run(Maint.PowerPlans);
                    var rows = plans.Select(p => Row(B((p.Active ? "✅ " : "") + p.Name, "pp:" + p.Guid + "|plans"))).ToList();
                    rows.Add(Back("sys"));
                    return ("⚡ <b>Схема электропитания</b>", K(rows.ToArray()));
                }

                case "power":
                    if (Need("power") is { } nop) return (nop, K(Back()));
                    return ("⚡ <b>Питание</b>" + (SystemService.I.HasBattery ? $"\n🔋 {SystemService.I.BatteryPercent}%{(SystemService.I.Charging ? " ⚡" : "")}" : ""), K(
                        Row(B("🔒 Заблокировать", "pw:lock"), B("😴 Сон", "pw:sleep"), B("🛌 Гибернация", "pw:hibernate")),
                        Row(B("🔄 Перезагрузка", "pw:restart"), B("⏻ Выключить", "pw:shutdown"), B("🚪 Выйти", "pw:logoff")),
                        Row(B("⏲ Выкл. через 30 мин", "pw:in30"), B("1 ч", "pw:in60"), B("2 ч", "pw:in120")),
                        Row(B("✖ Отменить выключение", "pw:cancel")),
                        Back()));

                case "net":
                {
                    var text = new StringBuilder("🌐 <b>Сеть</b>\n");
                    try
                    {
                        if (Vpn.PublicIp.Ip == null) await Vpn.LoadIp();
                        var ip = Vpn.PublicIp;
                        if (ip.Ip != null) text.Append($"IP {H(ip.Ip)} · {ip.Flag} {H(ip.City)}{(string.IsNullOrEmpty(ip.City) ? "" : ", ")}{H(ip.Country)}\n{H(ip.Isp)}\n");
                    }
                    catch { }
                    text.Append(await WifiText());
                    if (Vpn.Active) text.Append("\n🔒 VPN включён");
                    return (text.ToString(), K(
                        Row(B("🏓 Пинг", "do:ping"), B("🔁 Переподключить Wi-Fi", "do:wifire")),
                        Row(B("🔒 VPN", "nav:vpn"), B("🔄", "nav:net")),
                        Back()));
                }

                case "vpn":
                {
                    if (Vpn.Items.Count == 0) await Vpn.Detect();
                    await Vpn.RefreshAll();
                    var rows = Vpn.Items.Select(v => Row(B($"{(v.State == VpnState.On ? "🟢" : v.State == VpnState.Connecting ? "🟡" : "⚪")} {v.Name}",
                        v.CanControl ? "vpn:" + Put(v.Key) + "|vpn" : "noop"))).ToList();
                    rows.Add(Row(B("🔄", "nav:vpn"), B("⬅️ Назад", "nav:net")));
                    return (Vpn.Items.Count == 0 ? "🔒 VPN-программы не найдены" : "🔒 <b>VPN</b> — нажми, чтобы включить или выключить", K(rows.ToArray()));
                }

                case "tools":
                    return ("🧰 <b>Полезное</b>", K(
                        Row(B("🌦 Погода", "do:weather"), B("📦 Посылки", "do:ttn")),
                        Row(B("📝 Заметки", "do:notes"), B("✅ Задачи", "nav:todo"), B("⏱ Таймер", "nav:timer")),
                        Row(B("📋 Буфер ПК", "do:clip"), B("📥 На ПК в буфер", "ask:copy"), B("💬 В чёлку", "ask:say")),
                        Row(B("🗣 Сказать вслух", "ask:speak"), B("🤖 Спросить ИИ", "ask:ai")),
                        Row(B("🔔 Найти ноутбук", "do:find"), B("📷 Камера", "do:cam"), B("📍 Где он", "do:where")),
                        Back()));

                case "todo":
                {
                    var todos = Store.Data.Todos.Where(t => !t.Done).Take(12).ToList();
                    var rows = todos.Select(t => Row(B("⬜ " + Cut(t.Text, 40), "td:" + t.Id))).ToList();
                    rows.Add(Row(B("➕ Добавить", "ask:todo"), B("🔄", "nav:todo")));
                    rows.Add(Back("tools"));
                    return (todos.Count == 0 ? "✅ Все задачи сделаны" : $"✅ <b>Задачи</b> ({todos.Count}) — нажми, чтобы отметить", K(rows.ToArray()));
                }

                case "timer":
                {
                    var t = TimerService.I;
                    var text = t.Running ? $"⏱ <b>Таймер</b>: осталось {Fmt.Time(t.Left)}" : t.Left > TimeSpan.Zero && t.Left < t.Set ? $"⏱ Таймер на паузе: {Fmt.Time(t.Left)}" : "⏱ <b>Таймер</b> — на сколько минут?";
                    return (text, K(
                        Row(B("1", "tm:1"), B("3", "tm:3"), B("5", "tm:5"), B("10", "tm:10")),
                        Row(B("15", "tm:15"), B("25", "tm:25"), B("45", "tm:45"), B("60", "tm:60")),
                        Row(B(t.Running ? "⏸ Пауза" : "▶ Продолжить", "tm:toggle|timer"), B("⏹ Сбросить", "tm:stop|timer"), B("🔄", "nav:timer")),
                        Back("tools")));
                }

                case "set":
                {
                    var ev = Integrations.Events(this);
                    var rows = Grid(Integrations.EventKinds.Select(k => B((ev.Contains(k.Key) ? "✅ " : "▫️ ") + k.Name, "ev:" + k.Key)), 2).ToList();
                    rows.Add(Row(B((Flag("boot") ? "✅" : "▫️") + " ПК включился", "fl:boot"), B((Flag("unlock") ? "✅" : "▫️") + " Вход и разблокировка", "fl:unlock")));
                    rows.Add(Row(B((Flag("aitext") ? "✅" : "▫️") + " Текст → ИИ", "fl:aitext"), B((Flag("kbd") ? "✅" : "▫️") + " Кнопки внизу", "fl:kbd")));
                    rows.Add(Row(B("🔐 Разрешения", "nav:perm"), B("⬅️ Назад", "nav:main")));
                    return ("🔧 <b>Что присылать на телефон</b>\nНажми, чтобы включить или выключить" +
                            (V("quiet") is { Length: > 0 } q ? $"\n🌙 Тихие часы: {H(q)}" : ""), K(rows.ToArray()));
                }

                case "perm":
                    return ("🔐 <b>Разрешения</b>\n" + string.Join("\n", FlagNames.Select(f => (Flag(f.Key) ? "✅ " : "⛔ ") + T(f.Value))) +
                            "\n\nМеняются только на ПК: N-Island → Настройки → Интеграции → Telegram. Так никто не включит опасное, даже узнав токен бота.",
                        K(Back("set")));

                case "ai":
                {
                    var cur = Ai.Current;
                    var ready = Ai.All.Where(p => Ai.Ready(p, out _) && (p.Key is "free" or "llm7" || Ai.HasKey(p) || p.Custom || p.Group == "На этом ПК")).ToList();
                    var rows = Grid(ready.Select(p => B((p.Key == cur.Key ? "✅ " : "") + p.Name, "aip:" + p.Key)), 2).ToList();
                    rows.Add(Row(B("📋 Выбрать модель", "aimodels:0"), B("🆕 Новый диалог", "ainew")));
                    rows.Add(Row(B("💬 Спросить", "ask:ai"), B("⬅️ Назад", "nav:main")));
                    int count;
                    lock (Ai.Chat) count = Ai.Chat.Count;
                    return ($"🧠 <b>Нейросеть</b>: {H(cur.Name)}\nМодель: <code>{H(Ai.Model(cur))}</code>\n" +
                            (Store.Settings.AiShareTelegram ? $"Диалог общий с чатом острова · сообщений {count}" : "Свой диалог бота") +
                            "\n\nСпросить: /ai вопрос" + (Flag("aitext") ? " или просто напиши текст" : "") +
                            "\nНовые провайдеры и ключи добавляются на ПК: Настройки → ИИ.", K(rows.ToArray()));
                }
            }
            if (await Menu2(name) is { } m2) return m2;
            return ("Нет такого раздела", K(Back()));
        }

        int _mouseStep = 100;

        // ------------------------------------------------------------------
        //  Кнопки
        // ------------------------------------------------------------------

        async Task<string> Callback(string data, long msgId)
        {
            if (string.IsNullOrEmpty(data) || data == "noop") return "";
            string back = null;
            int bar = data.IndexOf('|');
            if (bar > 0) { back = data[(bar + 1)..]; data = data[..bar]; }
            int colon = data.IndexOf(':');
            string head = colon > 0 ? data[..colon] : data, rest = colon > 0 ? data[(colon + 1)..] : "";
            string res = await Do(head, rest, msgId);
            if (back != null)
            {
                await Task.Delay(head is "au" or "mx" or "br" ? 350 : head is "mu" ? 700 : 100);   // громкость и плеер обновляются не мгновенно
                var (h, k) = await Menu(back);
                await Edit(msgId, h, k);
            }
            return res;
        }

        async Task<string> Do(string head, string rest, long msgId)
        {
            var m = MediaService.I;
            var a = AudioService.I;
            string no;
            switch (head)
            {
                case "nav":
                {
                    var (h, k) = await Menu(rest);
                    await Edit(msgId, h, k);
                    return "";
                }
                case "status!":
                    await Edit(msgId, await Task.Run(StatusText), K(Row(B("🔄 Обновить", "status!"), B("🎛 Пульт", "nav:main"))));
                    return "";
                case "ask":
                {
                    var flag = rest switch { "type" => "input", "search" => "files", "copy" or "speak" or "todo" => "control", _ => null };
                    if (flag != null && (no = Need(flag)) != null) return no;
                    if (rest is "todo" or "note")
                    {
                        Await(rest);
                        await Message(rest == "todo" ? "✅ Напиши задачу — она появится в списке на ПК" : "📝 Напиши заметку");
                        return "";
                    }
                    var r = await Command(rest, "");
                    if (r?.Html != null) await Message(r.Value.Html, r.Value.Keys);
                    return "";
                }
                case "do":
                {
                    var cmd = rest switch
                    {
                        "monoff" => "monitor", "wallhelp" => "wallpaper", "recent" => null, "notes" => "note", "wifire" => null, _ => rest,
                    };
                    if (rest == "recent") { if ((no = Need("files")) != null) return no; var (h, k) = Dir(PcControl.Places[1].Path(), 0, true); await Message(h, k); return ""; }
                    if (rest == "wifire")
                    {
                        if ((no = Need("control")) != null) return no;
                        return await WifiTools.Reconnect() ?? "Wi-Fi переподключается";
                    }
                    if (rest is "lyrics" or "sysinfo" or "disk" or "weather" or "ttn" or "notes" or "clip" or "find" or "cam" or "where" or "ping" or "wallhelp" or "ram" or "dns")
                    {
                        if (rest == "find") _ = Task.Run(async () => { try { await Reply(await Command("find", "")); } catch { } });
                        else await Reply(await Command(cmd, ""));
                        return rest == "find" ? "🔔 Звоню…" : "";
                    }
                    var r = await Command(cmd, "");
                    // короткий ответ — всплывающим уведомлением, без нового сообщения
                    return r?.Html == null ? "" : System.Net.WebUtility.HtmlDecode(Regex.Replace(r.Value.Html, "<[^>]+>", ""));
                }
                case "mu":
                    if ((no = Need("control")) != null) return no;
                    return await Ui(() =>
                    {
                        switch (rest)
                        {
                            case "prev": m.Prev(); return "⏮";
                            case "next": m.Next(); return "⏭";
                            case "play": bool was = m.IsPlaying; m.TogglePlay(); return was ? "⏸ Пауза" : "▶ Играет";
                            case "back": m.SeekBy(-10); return "⏪ −10 с";
                            case "fwd": m.SeekBy(10); return "⏩ +10 с";
                            case "fav": return m.ToggleFavorite() ? "💖 В избранном" : "Убрано из избранного";
                        }
                        return "";
                    });
                case "au":
                    if ((no = Need("control")) != null) return no;
                    if (rest == "mic") return await Ui(() => a.ToggleMicMute()) ? "🎙 Микрофон выключен" : "🎙 Микрофон включён";
                    return await Ui(() =>
                    {
                        if (rest == "mute") { bool was = a.Muted; a.ToggleMute(); return was ? "🔊 Звук включён" : "🔇 Без звука"; }
                        double to = rest switch { "vol+" => a.Volume + 0.1, "vol-" => a.Volume - 0.1, _ => int.TryParse(rest.TrimStart('v'), out var p) ? p / 100.0 : a.Volume };
                        to = Math.Clamp(Math.Round(to * 20) / 20, 0, 1);   // ровные шаги по 5%
                        a.SetVolume(to);
                        return $"🔊 {Math.Round(to * 100)}%";
                    });
                case "out":
                {
                    if ((no = Need("control")) != null) return no;
                    var id = Get(rest);
                    if (id == null) return Stale;
                    bool ok = a.SetDefaultOutput(id);
                    await Task.Delay(400);
                    var (h, k) = await Menu("outs");
                    await Edit(msgId, h, k);
                    return ok ? "🎧 Звук переключён" : "Не получилось переключить";
                }
                case "mx":
                {
                    if ((no = Need("control")) != null) return no;
                    var p = rest.Split(':');
                    var name = p.Length == 2 ? Get(p[0]) : null;
                    if (name == null) return Stale;
                    var app = a.Apps().FirstOrDefault(x => x.Name == name);
                    if (app == null) return "Программа больше не издаёт звук";
                    if (p[1] == "m") a.SetAppVolume(name, -1, !app.Muted);
                    else a.SetAppVolume(name, Math.Clamp(app.Volume + (p[1] == "+" ? 0.1f : -0.1f), 0, 1));
                    return "";
                }
                case "eq":
                    if ((no = Need("control")) != null) return no;
                    await Ui(() => { if (rest == "off") Equalizer.SetOn(false); else if (rest == "on") Equalizer.SetOn(true); else Equalizer.SetPreset(rest); });
                    return Equalizer.Installed ? "🎚 Готово" : "🎚 Сохранено, но Equalizer APO не установлен";
                case "br":
                {
                    if ((no = Need("control")) != null) return no;
                    int cur = SystemService.I.GetBrightness() ?? -1;
                    if (cur < 0) return "Яркость этого экрана Windows не меняет (внешний монитор)";
                    int to = rest switch { "+" => cur + 20, "-" => cur - 20, _ => int.TryParse(rest, out var v) ? v : cur };
                    to = Math.Clamp(to, 0, 100);
                    SystemService.I.SetBrightness(to);
                    return $"🔆 {to}%";
                }
                case "sh":
                    if ((no = Need("screen")) != null) return no;
                    await Screenshot(rest == "png" ? 0 : int.TryParse(rest, out var which) ? which : 0, rest == "png");
                    return "📸";
                case "w":
                {
                    if ((no = Need("apps")) != null) return no;
                    if (!long.TryParse(rest, out var hv) || !PcControl.Exists((IntPtr)hv)) return "Окно уже закрыто";
                    var win = (await Task.Run(PcControl.ListWindows)).FirstOrDefault(x => x.Handle.ToInt64() == hv);
                    var title = win?.Title ?? "Окно";
                    await Edit(msgId, $"🪟 <b>{H(Cut(title, 200))}</b>\n{H(win?.Process)}{(win?.Minimized == true ? " · свёрнуто" : "")}", K(
                        Row(B("👁 Показать", "wa:f" + hv), B("➖ Свернуть", "wa:m" + hv), B("⬜ Развернуть", "wa:x" + hv)),
                        Row(B("✕ Закрыть окно", "wa:c" + hv), Flag("screen") ? B("📸 Снимок окна", "wa:s" + hv) : null),
                        Row(B("⬅️ К окнам", "nav:win"))));
                    return "";
                }
                case "wa":
                {
                    if ((no = Need("apps")) != null) return no;
                    if (rest.Length < 2 || !long.TryParse(rest[1..], out var hv)) return "";
                    var h = (IntPtr)hv;
                    if (rest[0] == 's')
                    {
                        if ((no = Need("screen")) != null) return no;
                        PcControl.Focus(h);
                        await Task.Delay(400);
                        await Screenshot(-1, false);
                        return "📸";
                    }
                    var r = PcControl.WindowAction(h, rest[0]);
                    if (rest[0] == 'c') { await Task.Delay(700); var (mh, mk) = await Menu("win"); await Edit(msgId, mh, mk); }
                    return r;
                }
                case "kill":
                    if ((no = Need("apps")) != null) return no;
                    var killed = await Task.Run(() => Kill(rest));
                    var (th, tk) = await Task.Run(TopMenu);
                    await Edit(msgId, th, tk);
                    return System.Net.WebUtility.HtmlDecode(Regex.Replace(killed, "<[^>]+>", ""));
                case "kb":
                    if ((no = Need("input")) != null) return no;
                    return PcControl.Hotkey(rest) ?? "⌨️ " + rest;
                case "ms":
                {
                    if ((no = Need("input")) != null) return no;
                    var p = rest.Split(':');
                    if (p.Length == 2 && int.TryParse(p[0], out var dx) && int.TryParse(p[1], out var dy)) PcControl.MouseMove(dx, dy);
                    return "";
                }
                case "mc":
                    if ((no = Need("input")) != null) return no;
                    switch (rest)
                    {
                        case "l": PcControl.Click(); return "🖱 Клик";
                        case "r": PcControl.Click(right: true); return "🖱 Правый клик";
                        case "d": PcControl.Click(dbl: true); return "🖱 Двойной клик";
                        case "c": PcControl.MouseCenter(); return "⊙";
                        case "up": PcControl.Wheel(3); return "🔼";
                        case "down": PcControl.Wheel(-3); return "🔽";
                    }
                    return "";
                case "mstep":
                {
                    if (int.TryParse(rest, out var st)) _mouseStep = Math.Clamp(st, 5, 1000);
                    var (h, k) = await Menu("mouse");
                    await Edit(msgId, h, k);
                    return $"Шаг {_mouseStep} px";
                }
                case "d":
                case "dp":
                {
                    if ((no = Need("files")) != null) return no;
                    var p = rest.Split(':');
                    var path = Get(p[0]);
                    if (path == null) return Stale;
                    var (h, k) = Dir(path, p.Length > 1 && int.TryParse(p[1], out var pg) ? pg : 0);
                    await Edit(msgId, h, k);
                    return "";
                }
                case "f":
                {
                    if ((no = Need("files")) != null) return no;
                    var path = Get(rest);
                    if (path == null) return Stale;
                    if (!File.Exists(path)) return "Файла уже нет";
                    var fi = new FileInfo(path);
                    var t = Put(path);
                    await Edit(msgId, $"📄 <b>{H(fi.Name)}</b>\n{Maint.Size(fi.Length)} · изменён {fi.LastWriteTime.ToString("d MMM yyyy, HH:mm", Fmt.Ru)}\n<code>{H(fi.DirectoryName)}</code>", K(
                        Row(B("⬇️ Скачать", "fg:" + t), B("▶ Открыть на ПК", "fo:" + t)),
                        Row(B("🗑 В корзину", "fd:" + t), B("📋 Путь в буфер ПК", "fc:" + t)),
                        Row(B("⬅️ В папку", "d:" + Put(fi.DirectoryName)))));
                    return "";
                }
                case "fg": case "fo": case "fd": case "fdok": case "fc": case "z":
                {
                    if ((no = Need("files")) != null) return no;
                    var path = Get(rest);
                    if (path == null) return Stale;
                    switch (head)
                    {
                        case "fg": case "z":
                            _ = Task.Run(async () => { await Action("upload_document"); var err = await SendPath(path); if (err != null) await Message(err); });
                            return "⬇️ Отправляю…";
                        case "fo":
                            if ((no = Need("apps")) != null) return no;
                            await Ui(() => SystemService.Run(path));
                            Toast("Открыто из Telegram", Path.GetFileName(path));
                            return "▶ Открыто на ПК";
                        case "fc":
                            await Ui(() => ClipboardService.I.CopyBack(path));
                            return "📋 Путь скопирован на ПК";
                        case "fd":
                            await Edit(msgId, $"🗑 Переместить в корзину?\n<code>{H(path)}</code>\nВосстановить можно из корзины Windows.",
                                K(Row(B("Да, в корзину", "fdok:" + rest), B("Отмена", "f:" + rest))));
                            return "";
                        default:
                            var dir = Path.GetDirectoryName(path.TrimEnd('\\'));
                            await Task.Run(() => PcControl.Recycle(path));
                            Toast("Удалено из Telegram в корзину", Path.GetFileName(path));
                            if (dir != null) { var (h, k) = Dir(dir, 0); await Edit(msgId, h, k); }
                            return "🗑 В корзине";
                    }
                }
                case "pw":
                    if ((no = Need("power")) != null) return no;
                    return await PowerButton(rest, msgId);
                case "mode":
                    if ((no = Need("control")) != null) return no;
                    await Ui(() => App.Island?.SetMode(rest));
                    return "🌙 " + T(Modes.All.FirstOrDefault(x => x.Key == rest)?.Name ?? rest);
                case "pp":
                    if ((no = Need("control")) != null) return no;
                    await Task.Run(() => Maint.SetPowerPlan(rest));
                    return "⚡ Схема изменена";
                case "vpn":
                {
                    if ((no = Need("control")) != null) return no;
                    var key = Get(rest);
                    var v = Vpn.Items.FirstOrDefault(x => x.Key == key);
                    if (v == null) return Stale;
                    var err = await Vpn.Set(v, v.State != VpnState.On);
                    return err ?? (v.State == VpnState.On ? "🟢 " + v.Name + " включён" : "⚪ " + v.Name + " выключен");
                }
                case "td":
                {
                    if ((no = Need("control")) != null) return no;
                    var done = await Ui(() =>
                    {
                        var t = Store.Data.Todos.FirstOrDefault(x => x.Id == rest);
                        if (t == null) return null;
                        t.Done = true;
                        Store.SaveData();
                        return t.Text;
                    });
                    var (h, k) = await Menu("todo");
                    await Edit(msgId, h, k);
                    return done == null ? "Задачи уже нет" : "✅ " + Cut(done, 60);
                }
                case "tm":
                {
                    if ((no = Need("control")) != null) return no;
                    var t = TimerService.I;
                    if (rest == "stop") { await Ui(t.Reset); return "⏹ Таймер сброшен"; }
                    if (rest == "toggle") { await Ui(t.Toggle); return t.Running ? "▶" : "⏸"; }
                    if (!int.TryParse(rest, out var min)) return "";
                    await Ui(() => { t.Reset(); t.SetMinutes(min); t.Start(); });
                    var (h, k) = await Menu("timer");
                    await Edit(msgId, h, k);
                    return $"⏱ {min} мин";
                }
                case "ev":
                {
                    bool on = !Integrations.Events(this).Contains(rest);
                    await Ui(() => Integrations.SetEvent(this, rest, on));
                    var (h, k) = await Menu("set");
                    await Edit(msgId, h, k);
                    return on ? "✅ Будет приходить" : "Не будет приходить";
                }
                case "fl":
                {
                    if (rest is not ("boot" or "unlock" or "aitext" or "kbd")) return "Это меняется только на ПК";
                    bool on = !Flag(rest);
                    await Ui(() => Integrations.SetValue(this, rest, on ? "1" : "0"));
                    if (rest == "kbd") await Message(on ? "Кнопки внизу включены" : "Кнопки внизу убраны", on ? ReplyKeys() : new { remove_keyboard = true });
                    var (h, k) = await Menu("set");
                    await Edit(msgId, h, k);
                    return on ? "✅ Включено" : "Выключено";
                }
                case "ainew":
                    lock (AiHistory) AiHistory.Clear();
                    if (Store.Settings.AiShareTelegram) await Ui(Ai.ClearChat);
                    return "🤖 Новый диалог";
                case "aicp":
                    if ((no = Need("control")) != null) return no;
                    if (_lastAi == null) return "Ответа ещё нет";
                    await Ui(() => ClipboardService.I.CopyBack(_lastAi));
                    return "💻 Ответ в буфере обмена ПК";
                case "airetry":
                    if (_lastQuestion == null) return "Сначала задай вопрос";
                    _ = Task.Run(async () => { try { await Reply(await AskAi(_lastQuestion, retry: true)); } catch (Exception e) { Log.Info("tg ai retry: " + e.Message); } });
                    return "🔁 Спрашиваю ещё раз…";
                case "aip":
                {
                    // переключать нейросеть можно только между уже настроенными на ПК (ключи с телефона не вводятся)
                    var p = Ai.Find(rest);
                    if (p == null || !Ai.Ready(p, out var why)) return "Этот провайдер не настроен на ПК";
                    await Ui(() => Store.Settings.AiProvider = p.Key);
                    var (h, k) = await Menu("ai");
                    await Edit(msgId, h, k);
                    return "🧠 " + p.Name;
                }
                case "aim":
                {
                    var model = Get(rest);
                    if (model == null) return Stale;
                    await Ui(() => Ai.SetModel(Ai.Current, model));
                    var (h, k) = await Menu("ai");
                    await Edit(msgId, h, k);
                    return "🧠 " + model;
                }
                case "aimodels":
                {
                    var p = Ai.Current;
                    List<string> models;
                    try { models = await Ai.Models(p); } catch (Exception e) { return Cut(e.Message, 150); }
                    var cur = Ai.Model(p);
                    int page = int.TryParse(rest, out var pg) ? pg : 0;
                    var shown = models.Skip(page * 20).Take(20).ToList();
                    var rows = Grid(shown.Select(id => B((id == cur ? "✅ " : "") + Cut(id, 30), "aim:" + Put(id))), 2).ToList();
                    var nav = new List<object>();
                    if (page > 0) nav.Add(B("◀", "aimodels:" + (page - 1)));
                    if ((page + 1) * 20 < models.Count) nav.Add(B("▶", "aimodels:" + (page + 1)));
                    if (nav.Count > 0) rows.Add(nav.ToArray());
                    rows.Add(Back("ai"));
                    await Edit(msgId, $"🧠 <b>Модели {H(p.Name)}</b>: {models.Count}" + (models.Count > 20 ? $" · стр. {page + 1}/{(models.Count + 19) / 20}" : ""), K(rows.ToArray()));
                    return "";
                }
            }
            return await Do2(head, rest, msgId) ?? "";
        }

        async Task<string> PowerButton(string what, long msgId)
        {
            switch (what)
            {
                case "lock": LockWorkStation(); return "🔒 Заблокирован";
                case "sleep": Suspend(false); return "😴 Засыпает";
                case "hibernate": Suspend(true); return "🛌 Гибернация";
                case "cancel": Maint.RunCmd("shutdown.exe", "/a"); Toast("Выключение отменено", "Команда из Telegram"); return "✖ Отменено";
                case "no": await Edit(msgId, "Отменено", null); return "";
                case "restart": case "shutdown": case "logoff":
                    await Edit(msgId, what switch { "shutdown" => "⚠️ Выключить ПК? Несохранённое пропадёт.", "logoff" => "⚠️ Выйти из Windows? Программы закроются.", _ => "⚠️ Перезагрузить ПК? Несохранённое пропадёт." },
                        K(Row(B("Да", "pw:" + what + "!"), B("Отмена", "pw:no"))));
                    return "";
                case "shutdown!":
                case "restart!":
                {
                    bool off = what == "shutdown!";
                    Maint.RunCmd("shutdown.exe", (off ? "/s" : "/r") + " /t 60");
                    Toast(off ? "ПК выключится через минуту" : "ПК перезагрузится через минуту", "Команда из Telegram · /cancel — отменить");
                    await Edit(msgId, (off ? "⏻ ПК выключится" : "🔄 ПК перезагрузится") + " через минуту.", K(Row(B("✖ Отменить", "pw:cancel"))));
                    return off ? "Выключаю" : "Перезагружаю";
                }
                case "logoff!":
                    await Edit(msgId, "🚪 Выход из Windows…", null);
                    _ = Task.Run(async () => { await Task.Delay(1500); PcControl.LogOff(); });
                    return "🚪";
            }
            if (what.StartsWith("in") && int.TryParse(what[2..], out var min))
            {
                await Edit(msgId, ShutdownIn(min), K(Row(B("✖ Отменить", "pw:cancel"))));
                return "⏲ " + min + " мин";
            }
            return "";
        }

        string ShutdownIn(int min)
        {
            min = Math.Clamp(min, 1, 24 * 60);
            Maint.RunCmd("shutdown.exe", "/a");   // заменить прежний таймер
            Maint.RunCmd("shutdown.exe", $"/s /t {min * 60}");
            var at = DateTime.Now.AddMinutes(min);
            Toast($"ПК выключится в {at:HH:mm}", "Таймер из Telegram");
            return $"⏲ ПК выключится в {at:HH:mm} (через {min} мин)";
        }

        static void Suspend(bool hibernate) => _ = Task.Run(async () =>
        {
            await Task.Delay(1500);
            System.Windows.Forms.Application.SetSuspendState(hibernate ? System.Windows.Forms.PowerState.Hibernate : System.Windows.Forms.PowerState.Suspend, false, false);
        });

        [DllImport("user32.dll")] static extern bool LockWorkStation();

        static object MusicKeys() => K(
            Row(B("⏮", "mu:prev"), B("⏯", "mu:play"), B("⏭", "mu:next")),
            Row(B("🔉 −10", "au:vol-"), B("🔇", "au:mute"), B("🔊 +10", "au:vol+")));
    }
}
