using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NIsland.Core;

namespace NIsland.Services
{
    // =====================================================================
    //  Мессенджеры как пульт ПК: Discord, Slack, Matrix.
    //  Команды те же, что у Telegram-бота (/status, /vol 40, /screenshot, /do …), выполняются тем же движком,
    //  с собственными разрешениями каждого мессенджера. Слушаем только сообщения ВЛАДЕЛЬЦА (его ID),
    //  разрешения меняются только на ПК, опасное выключено по умолчанию.
    // =====================================================================
    public static class MessengerRemote
    {
        /// <summary>Поля пульта — одинаковые для всех мессенджеров.</summary>
        public static HubField[] Fields(string ownerHint) => new[]
        {
            new HubField("remote", "Пульт ПК из чата", "команды как в Telegram: /status, /vol 40, /screenshot, /do …  — /help в чате покажет все", "bool", Required: false, Default: "0"),
            new HubField("owner", "Твой ID (только он может управлять)", ownerHint, Required: false),
            new HubField("control", "Управление", "музыка, громкость, яркость, режимы, заметки, таймер, буфер обмена", "bool", Required: false, Default: "1"),
            new HubField("apps", "Программы и окна", "список окон, свернуть / закрыть, процессы, запуск программ", "bool", Required: false, Default: "0"),
            new HubField("input", "Клавиатура и мышь", "нажатия клавиш, ввод текста", "bool", Required: false, Default: "0"),
            new HubField("files", "Файлы", "папки и файлы с ПК, поиск, скачать", "bool", Required: false, Default: "0"),
            new HubField("screen", "Снимок экрана", "присылать картинку экрана в чат", "bool", Required: false, Default: "0"),
            new HubField("camera", "Камера и местоположение", "фото с камеры и точка на карте", "bool", Required: false, Default: "0"),
            new HubField("power", "Питание", "блокировка, сон, перезагрузка, выключение", "bool", Required: false, Default: "0"),
            new HubField("shell", "Команды Windows (опасно)", "/cmd и /ps — любая команда. Включай, только если в канале никого, кроме тебя", "bool", Required: false, Default: "0"),
            new HubField("aitext", "Обычный текст — вопрос ИИ", "иначе текст без / показывается в острове", "bool", Required: false, Default: "0"),
        };

        public static bool On(Integration it) => it.Flag("remote") && !string.IsNullOrWhiteSpace(it.V("owner"));

        /// <summary>Разрешения: всё, кроме самого пульта, можно только если пульт включён.</summary>
        public static Func<string, bool> Flags(Integration it) => k => it.Flag("remote") && it.Flag(k);

        /// <summary>Похоже ли сообщение на команду: «/status», «!vol 40», или любой текст, если «текст — вопрос ИИ».</summary>
        public static bool IsCommand(string text) => text.StartsWith('/') || text.StartsWith('!');

        /// <summary>Выполнить команду и отдать ответы (текст + файлы) отправителю.</summary>
        public static async Task Run(Integration it, string text, Func<string, Task> sendText, Func<string, byte[], string, Task> sendFile)
        {
            text = (text ?? "").Trim();
            if (text == "") return;
            async Task Push(string html, (string Name, byte[] Data, string Caption)? file)
            {
                if (file is { } f) await sendFile(f.Name, f.Data, f.Caption);
                else if (!string.IsNullOrWhiteSpace(html)) await sendText(html);
            }
            var ctx = await TelegramHub.RunRemote(text, it.Name, Flags(it), Push, it.Flag("aitext"));
            foreach (var f in ctx.Files) await sendFile(f.Name, f.Data, f.Caption);
            foreach (var html in ctx.Out) await sendText(html);
            it.Status = "Команда выполнена " + DateTime.Now.ToString("HH:mm");
        }

        /// <summary>Разрезать длинный текст под лимит мессенджера.</summary>
        public static IEnumerable<string> Chunks(string s, int max)
        {
            while (s.Length > max)
            {
                int cut = s.LastIndexOf('\n', max);
                if (cut < max / 2) cut = max;
                yield return s[..cut];
                s = s[cut..].TrimStart('\n');
            }
            if (s.Length > 0) yield return s;
        }

        public static string Mime(string name) => System.IO.Path.GetExtension(name ?? "").ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg", ".png" => "image/png", ".gif" => "image/gif", ".mp4" => "video/mp4",
            ".ogg" => "audio/ogg", ".mp3" => "audio/mpeg", ".txt" or ".log" => "text/plain", ".zip" => "application/zip", _ => "application/octet-stream",
        };
    }

    // ---------------------------------------------------------------------
    //  Discord: вебхук (события) + бот (пульт)
    // ---------------------------------------------------------------------
    public class DiscordHub : Integration
    {
        public override string Key => "discord";
        public override string Name => "Discord";
        public override string Glyph => "";
        public override string Color => "#5865F2";
        public override string Desc => "События острова в канал Discord, а с ботом — пульт ПК из канала: те же команды, что в Telegram";
        public override string Help => "События: Настройки канала → Интеграции → Вебхуки → Копировать URL. " +
                                       "Пульт: discord.com/developers → New Application → Bot → Reset Token (это токен) и включи Message Content Intent; " +
                                       "пригласи бота (OAuth2 → bot, права «Читать/Отправлять сообщения», «Прикреплять файлы»). ID канала и свой ID — правый клик → Копировать ID (нужен режим разработчика).";
        public override string HelpUrl => "https://discord.com/developers/applications";
        public override HubField[] Fields => new[]
        {
            new HubField("url", "URL вебхука", "для событий острова (необязательно, если есть бот)", "secret", Required: false),
            new HubField("bot", "Токен бота", "для пульта ПК", "secret", Required: false),
            new HubField("channel", "ID канала", "куда бот пишет и где слушает команды", Required: false),
        }.Concat(MessengerRemote.Fields("правый клик по себе → Копировать ID пользователя; найдётся сам после «Проверить»")).ToArray();
        public override string[] CanSend => Integrations.EventKinds.Select(e => e.Key).ToArray();
        public override int PollSeconds => MessengerRemote.On(this) && HasBot ? 4 : 0;

        bool HasBot => V("bot") != "" && Regex.IsMatch(V("channel"), @"^\d{5,25}$");
        const string Api = "https://discord.com/api/v10";

        void Auth(HttpRequestMessage r) => r.Headers.TryAddWithoutValidation("Authorization", "Bot " + V("bot"));

        public override async Task<string> Test()
        {
            if (V("url") == "" && !HasBot) throw new InvalidOperationException("Нужен адрес вебхука или токен бота с ID канала");
            if (HasBot)
            {
                using (var me = await Integrations.GetJson(Api + "/users/@me", Auth)) { }
                if (V("owner") == "")
                {
                    // владелец — автор последнего сообщения человека в канале (как поиск чата у Telegram)
                    using var msgs = await Integrations.GetJson($"{Api}/channels/{V("channel")}/messages?limit=20", Auth);
                    // только если в канале писал ровно один человек — иначе управление мог бы получить чужой
                    var humans = msgs.RootElement.EnumerateArray()
                        .Where(x => x.TryGetProperty("author", out var a) && !(a.TryGetProperty("bot", out var b) && b.GetBoolean()))
                        .Select(x => Integrations.S(x.GetProperty("author"), "id")).Distinct().ToList();
                    if (humans.Count > 1) return "Бот работает ✓ В канале пишут несколько человек — впиши свой ID вручную (правый клик по себе → Копировать ID)";
                    if (humans.Count == 1)
                    {
                        Integrations.SetValue(this, "owner", humans[0]);
                        Store.SaveSettings();
                    }
                }
                await SendText("**N-Island подключён ✓**\n" + (MessengerRemote.On(this) ? "Пульт ПК включён — напиши `/help`" : "Пульт ПК можно включить в настройках интеграции"));
                if (V("owner") == "") return "Бот работает ✓ Напиши в канал любое сообщение и нажми «Проверить» ещё раз — так остров узнает твой ID";
                return "Работает ✓ Управлять может только пользователь " + V("owner");
            }
            await Send(new HubEvent("test", "N-Island подключён ✓", "Сюда будут приходить события острова"));
            return "Работает ✓ Сообщение отправлено в канал";
        }

        public override async Task Send(HubEvent e)
        {
            var text = $"**{e.Title}**" + (e.Text != "" ? "\n" + e.Text : "");
            if (V("url") != "")
            {
                if (!V("url").StartsWith("https://")) throw new InvalidOperationException("Нужен адрес вебхука, начинающийся с https://");
                using var _ = await Integrations.PostJson(V("url"), new { username = "N-Island", content = Cut(text, 1990), allowed_mentions = new { parse = Array.Empty<string>() } });
            }
            else if (HasBot) await SendText(text);
            Status = "Отправлено " + DateTime.Now.ToString("HH:mm");
        }

        static string Cut(string s, int n) => s.Length > n ? s[..n] + "…" : s;

        async Task SendText(string md)
        {
            foreach (var part in MessengerRemote.Chunks(md, 1900))
            {
                using var _ = await Integrations.PostJson($"{Api}/channels/{V("channel")}/messages",
                    new { content = part, allowed_mentions = new { parse = Array.Empty<string>() } }, Auth);
            }
        }

        async Task SendFile(string name, byte[] data, string caption)
        {
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(JsonSerializer.Serialize(new { content = TelegramHub.ToMarkdown(caption ?? "", false), allowed_mentions = new { parse = Array.Empty<string>() } }), Encoding.UTF8, "application/json"), "payload_json");
            var file = new ByteArrayContent(data);
            file.Headers.ContentType = new MediaTypeHeaderValue(MessengerRemote.Mime(name));
            form.Add(file, "files[0]", name);
            var req = new HttpRequestMessage(HttpMethod.Post, $"{Api}/channels/{V("channel")}/messages") { Content = form };
            Auth(req);
            using var _ = await Integrations.Json(req);
        }

        public override async Task Poll()
        {
            if (!MessengerRemote.On(this) || !HasBot) return;
            var after = St("after");
            if (after == null)
            {
                // первый запуск — запоминаем последнее сообщение, старые команды не выполняем
                using var last = await Integrations.GetJson($"{Api}/channels/{V("channel")}/messages?limit=1", Auth);
                SetSt("after", last.RootElement.GetArrayLength() > 0 ? Integrations.S(last.RootElement[0], "id") : "0");
                return;
            }
            using var doc = await Integrations.GetJson($"{Api}/channels/{V("channel")}/messages?after={after}&limit=20", Auth);
            var list = doc.RootElement.EnumerateArray().Select(x => x.Clone()).OrderBy(x => ulong.TryParse(Integrations.S(x, "id"), out var id) ? id : 0).ToList();
            if (list.Count == 0) { Status = "Слушаю канал · " + DateTime.Now.ToString("HH:mm"); return; }
            SetSt("after", Integrations.S(list[^1], "id"));
            foreach (var msg in list)
            {
                var author = msg.GetProperty("author");
                if (author.TryGetProperty("bot", out var isBot) && isBot.GetBoolean()) continue;
                if (Integrations.S(author, "id") != V("owner").Trim()) continue;          // чужие сообщения не выполняем
                var text = Integrations.S(msg, "content");
                await MessengerRemote.Run(this, text, h => SendText(TelegramHub.ToMarkdown(h, false)), SendFile);
            }
        }
    }

    // ---------------------------------------------------------------------
    //  Slack: вебхук (события) + бот (пульт)
    // ---------------------------------------------------------------------
    public class SlackHub : Integration
    {
        public override string Key => "slack";
        public override string Name => "Slack";
        public override string Glyph => "";
        public override string Color => "#E01E5A";
        public override string Desc => "События острова в канал Slack, а с ботом — пульт ПК из канала: те же команды, что в Telegram (пиши !status, !vol 40 …)";
        public override string Help => "События: api.slack.com/apps → Create App → Incoming Webhooks. Пульт: OAuth & Permissions → Bot Token Scopes: " +
                                       "chat:write, channels:history (groups:history для закрытых, im:history для личных), files:write → Install → Bot User OAuth Token (xoxb-…). " +
                                       "Пригласи бота в канал (/invite @бот). ID канала — в его свойствах, свой ID — профиль → ⋮ → Copy member ID. " +
                                       "В Slack «/» занят встроенными командами — пиши команды через «!»: !status, !screenshot";
        public override string HelpUrl => "https://api.slack.com/apps";
        public override HubField[] Fields => new[]
        {
            new HubField("url", "URL вебхука", "для событий острова (необязательно, если есть бот)", "secret", Required: false),
            new HubField("bot", "Токен бота (xoxb-…)", "для пульта ПК", "secret", Required: false),
            new HubField("channel", "ID канала", "C0123ABCD", Required: false),
        }.Concat(MessengerRemote.Fields("U0123ABCD — профиль → ⋮ → Copy member ID")).ToArray();
        public override string[] CanSend => Integrations.EventKinds.Select(e => e.Key).ToArray();
        public override int PollSeconds => MessengerRemote.On(this) && HasBot ? 5 : 0;

        bool HasBot => V("bot").StartsWith("xoxb-") && Regex.IsMatch(V("channel"), @"^[A-Z0-9]{8,14}$");
        void Auth(HttpRequestMessage r) => r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", V("bot"));

        async Task<JsonElement> Api(string method, object body = null, string query = null)
        {
            var req = new HttpRequestMessage(body == null ? HttpMethod.Get : HttpMethod.Post, $"https://slack.com/api/{method}{(query != null ? "?" + query : "")}");
            if (body != null) req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            Auth(req);
            using var doc = await Integrations.Json(req);
            var r = doc.RootElement.Clone();
            if (!(r.TryGetProperty("ok", out var ok) && ok.GetBoolean())) throw new InvalidOperationException("Slack: " + Integrations.S(r, "error"));
            return r;
        }

        public override async Task<string> Test()
        {
            if (V("url") == "" && !HasBot) throw new InvalidOperationException("Нужен адрес вебхука или токен бота (xoxb-…) с ID канала");
            if (HasBot)
            {
                await Api("auth.test", new { });
                await SendText("*N-Island подключён ✓*\n" + (MessengerRemote.On(this) ? "Пульт ПК включён — напиши `!help`" : "Пульт ПК можно включить в настройках интеграции"));
                return V("owner") == "" ? "Бот работает ✓ Впиши свой member ID, чтобы управлять ПК" : "Работает ✓ Управлять может только " + V("owner");
            }
            await Send(new HubEvent("test", "N-Island подключён ✓", "Сюда будут приходить события острова"));
            return "Работает ✓ Сообщение отправлено в канал";
        }

        public override async Task Send(HubEvent e)
        {
            var text = $"*{e.Title}*" + (e.Text != "" ? "\n" + e.Text : "");
            if (V("url") != "")
            {
                if (!V("url").StartsWith("https://")) throw new InvalidOperationException("Нужен адрес вебхука, начинающийся с https://");
                using var resp = await Http.PostAsync(V("url"), new StringContent(JsonSerializer.Serialize(new { text }), Encoding.UTF8, "application/json"));
                if (!resp.IsSuccessStatusCode) throw new HttpRequestException(Integrations.Snip(await resp.Content.ReadAsStringAsync()), null, resp.StatusCode);
            }
            else if (HasBot) await SendText(text);
            Status = "Отправлено " + DateTime.Now.ToString("HH:mm");
        }

        async Task SendText(string mrkdwn)
        {
            foreach (var part in MessengerRemote.Chunks(mrkdwn, 3800))
                await Api("chat.postMessage", new { channel = V("channel"), text = part, mrkdwn = true, unfurl_links = false });
        }

        async Task SendFile(string name, byte[] data, string caption)
        {
            // новый способ загрузки файлов Slack: адрес → загрузка → завершение
            var up = await Api("files.getUploadURLExternal", null, $"filename={Uri.EscapeDataString(name)}&length={data.Length}");
            using (var content = new ByteArrayContent(data))
            using (var resp = await Http.PostAsync(Integrations.S(up, "upload_url"), content))
                resp.EnsureSuccessStatusCode();
            await Api("files.completeUploadExternal", new
            {
                files = new[] { new { id = Integrations.S(up, "file_id"), title = name } },
                channel_id = V("channel"),
                initial_comment = TelegramHub.ToMarkdown(caption ?? "", true),
            });
        }

        public override async Task Poll()
        {
            if (!MessengerRemote.On(this) || !HasBot) return;
            var oldest = St("oldest");
            if (oldest == null) { SetSt("oldest", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture) + ".000000"); return; }
            var r = await Api("conversations.history", null, $"channel={V("channel")}&oldest={oldest}&limit=20");
            var msgs = r.GetProperty("messages").EnumerateArray().Select(x => x.Clone())
                .OrderBy(x => decimal.TryParse(Integrations.S(x, "ts"), NumberStyles.Float, CultureInfo.InvariantCulture, out var t) ? t : 0).ToList();
            if (msgs.Count == 0) { Status = "Слушаю канал · " + DateTime.Now.ToString("HH:mm"); return; }
            SetSt("oldest", Integrations.S(msgs[^1], "ts"));
            foreach (var m in msgs)
            {
                if (m.TryGetProperty("bot_id", out _) || m.TryGetProperty("subtype", out _)) continue;
                if (Integrations.S(m, "user") != V("owner").Trim()) continue;
                var text = System.Net.WebUtility.HtmlDecode(Integrations.S(m, "text"));   // Slack экранирует & < >
                await MessengerRemote.Run(this, text, h => SendText(TelegramHub.ToMarkdown(h, true)), SendFile);
            }
        }
    }

    // ---------------------------------------------------------------------
    //  Matrix / Element: события + пульт
    // ---------------------------------------------------------------------
    public class MatrixHub : Integration
    {
        public override string Key => "matrix";
        public override string Name => "Matrix / Element";
        public override string Glyph => "";
        public override string Color => "#0DBD8B";
        public override string Desc => "События острова в комнату Matrix (Element, свой сервер) и пульт ПК из этой комнаты — те же команды, что в Telegram";
        public override string Help => "Element → Настройки → Помощь и о программе → Токен доступа (лучше — отдельного бот-аккаунта). ID комнаты — в настройках комнаты (вида !abc:matrix.org). " +
                                       "Свой ID — вида @ты:matrix.org. Команды пиши через «!»: !status, !vol 40 (в Element «/» — встроенные команды). " +
                                       "Для пульта нужна комната без сквозного шифрования: зашифрованные сообщения остров прочитать не может";
        public override HubField[] Fields => new[]
        {
            new HubField("server", "Сервер", "https://matrix.org", Default: "https://matrix.org"),
            new HubField("token", "Токен доступа", "", "secret"),
            new HubField("room", "ID комнаты", "!abcdef:matrix.org"),
        }.Concat(MessengerRemote.Fields("@ты:matrix.org")).ToArray();
        public override string[] CanSend => Integrations.EventKinds.Select(e => e.Key).ToArray();
        public override int PollSeconds => MessengerRemote.On(this) ? 4 : 0;

        string Base => V("server").TrimEnd('/');
        void Auth(HttpRequestMessage r) => r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", V("token"));

        public override async Task<string> Test()
        {
            if (!V("server").StartsWith("https://")) throw new InvalidOperationException("Адрес сервера должен начинаться с https://");
            await Send(new HubEvent("test", "N-Island подключён ✓", MessengerRemote.On(this) ? "Пульт ПК включён — напиши !help" : "Проверка связи"));
            return MessengerRemote.On(this) ? "Работает ✓ Управлять может только " + V("owner") : "Работает ✓ Сообщение отправлено в комнату";
        }

        public override async Task Send(HubEvent e) =>
            await SendHtml("<b>" + System.Net.WebUtility.HtmlEncode(e.Title) + "</b>" + (e.Text != "" ? "\n" + System.Net.WebUtility.HtmlEncode(e.Text) : ""));

        async Task SendContent(object content)
        {
            var url = $"{Base}/_matrix/client/v3/rooms/{Esc(V("room"))}/send/m.room.message/nisland{DateTime.UtcNow.Ticks}";
            var req = new HttpRequestMessage(HttpMethod.Put, url) { Content = new StringContent(JsonSerializer.Serialize(content), Encoding.UTF8, "application/json") };
            Auth(req);
            using var _ = await Integrations.Json(req);
            Status = "Отправлено " + DateTime.Now.ToString("HH:mm");
        }

        // «io.nisland» помечает наши ответы: если токен — твой же аккаунт, остров не примет свои ответы за команды
        Task SendHtml(string html) => SendContent(new Dictionary<string, object>
        {
            ["msgtype"] = "m.text", ["body"] = TelegramHub.ToPlain(html), ["format"] = "org.matrix.custom.html",
            ["formatted_body"] = TelegramHub.ToMatrixHtml(html), ["io.nisland"] = true,
        });

        async Task SendFile(string name, byte[] data, string caption)
        {
            var mime = MessengerRemote.Mime(name);
            var req = new HttpRequestMessage(HttpMethod.Post, $"{Base}/_matrix/media/v3/upload?filename={Esc(name)}") { Content = new ByteArrayContent(data) };
            req.Content.Headers.ContentType = new MediaTypeHeaderValue(mime);
            Auth(req);
            string uri;
            using (var up = await Integrations.Json(req)) uri = Integrations.S(up.RootElement, "content_uri");
            await SendContent(new Dictionary<string, object>
            {
                ["msgtype"] = mime.StartsWith("image/") ? "m.image" : "m.file", ["body"] = name, ["url"] = uri,
                ["info"] = new { mimetype = mime, size = data.Length }, ["io.nisland"] = true,
            });
            if (!string.IsNullOrWhiteSpace(caption)) await SendHtml(caption);
        }

        public override async Task Poll()
        {
            if (!MessengerRemote.On(this)) return;
            var filter = Esc(JsonSerializer.Serialize(new { room = new { rooms = new[] { V("room") }, timeline = new { limit = 20, types = new[] { "m.room.message" } } }, presence = new { types = Array.Empty<string>() }, account_data = new { types = Array.Empty<string>() } }));
            var since = St("since");
            using var doc = await Integrations.GetJson($"{Base}/_matrix/client/v3/sync?timeout=0&filter={filter}" + (since != null ? "&since=" + Esc(since) : ""), Auth);
            var next = Integrations.S(doc.RootElement, "next_batch");
            if (next != "" && next != since) SetSt("since", next);
            if (since == null) return;   // первый запуск — только запоминаем, старое не выполняем
            if (!doc.RootElement.TryGetProperty("rooms", out var rooms) || !rooms.TryGetProperty("join", out var join) || !join.TryGetProperty(V("room"), out var room)) return;
            if (!room.TryGetProperty("timeline", out var tl) || !tl.TryGetProperty("events", out var events)) return;
            foreach (var ev in events.EnumerateArray().Select(x => x.Clone()).ToList())
            {
                if (Integrations.S(ev, "type") != "m.room.message" || Integrations.S(ev, "sender") != V("owner").Trim()) continue;
                if (!ev.TryGetProperty("content", out var c) || c.TryGetProperty("io.nisland", out _) || Integrations.S(c, "msgtype") != "m.text") continue;
                var text = Integrations.S(c, "body");
                if (!MessengerRemote.IsCommand(text) && !Flag("aitext")) continue;   // в комнате и так переписка — без «!» ничего не делаем
                await MessengerRemote.Run(this, text, SendHtml, SendFile);
            }
        }
    }
}
