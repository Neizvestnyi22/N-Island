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
    //  Пуши: Pushover, Pushbullet, Matrix
    // =====================================================================

    public class PushoverHub : Integration
    {
        public override string Key => "pushover";
        public override string Name => "Pushover";
        public override string Glyph => "";
        public override string Color => "#249DF1";
        public override string Desc => "События острова — пушами на телефон, часы и планшет через Pushover";
        public override string Help => "pushover.net: твой User Key на главной, токен — Create an Application/API Token";
        public override string HelpUrl => "https://pushover.net/apps/build";
        public override HubField[] Fields => new[] { new HubField("token", "API-токен приложения", "", "secret"), new HubField("user", "User Key", "", "secret") };
        public override string[] CanSend => Integrations.EventKinds.Select(e => e.Key).ToArray();

        public override async Task<string> Test() { await Send(new HubEvent("test", "N-Island подключён ✓", "Проверка связи")); return "Работает ✓ Пуш отправлен"; }

        public override async Task Send(HubEvent e)
        {
            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["token"] = V("token"), ["user"] = V("user"), ["title"] = e.Title, ["message"] = e.Text == "" ? e.Title : e.Text,
                ["priority"] = e.Kind is "alert" or "call" ? "1" : e.Kind is "track" ? "-1" : "0",
            });
            using var resp = await Http.PostAsync("https://api.pushover.net/1/messages.json", form);
            if (!resp.IsSuccessStatusCode) throw new HttpRequestException(Integrations.Snip(await resp.Content.ReadAsStringAsync()), null, resp.StatusCode);
            Status = "Отправлено " + DateTime.Now.ToString("HH:mm");
        }
    }

    public class PushbulletHub : Integration
    {
        public override string Key => "pushbullet";
        public override string Name => "Pushbullet";
        public override string Glyph => "";
        public override string Color => "#4AB367";
        public override string Desc => "События острова — на телефон; заметки и ссылки, отправленные с телефона в Pushbullet, появляются в острове";
        public override string Help => "pushbullet.com → Settings → Account → Create Access Token";
        public override string HelpUrl => "https://www.pushbullet.com/#settings/account";
        public override HubField[] Fields => new[] { new HubField("token", "Access Token", "o.…", "secret") };
        public override string[] CanSend => Integrations.EventKinds.Select(e => e.Key).ToArray();
        public override int PollSeconds => 30;

        void Auth(HttpRequestMessage r) => r.Headers.TryAddWithoutValidation("Access-Token", V("token"));

        public override async Task<string> Test()
        {
            using var me = await Integrations.GetJson("https://api.pushbullet.com/v2/users/me", Auth);
            await Send(new HubEvent("test", "N-Island подключён ✓", "Проверка связи"));
            return "Работает ✓ " + Integrations.S(me.RootElement, "name");
        }

        public override async Task Send(HubEvent e)
        {
            using var doc = await Integrations.PostJson("https://api.pushbullet.com/v2/pushes", new { type = "note", title = e.Title, body = e.Text }, Auth);
            SetSt("mine", Integrations.S(doc.RootElement, "iden"));
            Status = "Отправлено " + DateTime.Now.ToString("HH:mm");
        }

        public override async Task Poll()
        {
            double.TryParse(St("after"), NumberStyles.Float, CultureInfo.InvariantCulture, out var after);
            bool first = after == 0;
            if (first) after = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            using var doc = await Integrations.GetJson($"https://api.pushbullet.com/v2/pushes?active=true&limit=10&modified_after={after.ToString(CultureInfo.InvariantCulture)}", Auth);
            double max = after;
            foreach (var p in doc.RootElement.GetProperty("pushes").EnumerateArray())
            {
                max = Math.Max(max, p.GetProperty("modified").GetDouble());
                if (Integrations.S(p, "iden") == St("mine") || Integrations.S(p, "dismissed") == "True") continue;
                var title = Integrations.S(p, "title"); var body = Integrations.S(p, "body"); var url = Integrations.S(p, "url");
                Rows.Insert(0, new HubRow { Title = title != "" ? title : body != "" ? body : url, Sub = (title != "" ? body : "") + " · " + DateTime.Now.ToString("HH:mm"), Url = url != "" ? url : null });
                if (Rows.Count > 8) Rows.RemoveAt(Rows.Count - 1);
                Notify(title != "" ? title : "Pushbullet", body != "" ? body : url, url != "" ? url : null);
            }
            SetSt("after", max.ToString(CultureInfo.InvariantCulture));
            Status = "Обновлено " + DateTime.Now.ToString("HH:mm");
        }
    }

    public class MatrixHub : Integration
    {
        public override string Key => "matrix";
        public override string Name => "Matrix / Element";
        public override string Glyph => "";
        public override string Color => "#0DBD8B";
        public override string Desc => "События острова в комнату Matrix (Element, свой сервер)";
        public override string Help => "Element → Настройки → Помощь и о программе → Токен доступа. ID комнаты — в настройках комнаты (вида !abc:matrix.org)";
        public override HubField[] Fields => new[]
        {
            new HubField("server", "Сервер", "https://matrix.org", Default: "https://matrix.org"),
            new HubField("token", "Токен доступа", "", "secret"),
            new HubField("room", "ID комнаты", "!abcdef:matrix.org"),
        };
        public override string[] CanSend => Integrations.EventKinds.Select(e => e.Key).ToArray();

        public override async Task<string> Test() { await Send(new HubEvent("test", "N-Island подключён ✓", "Проверка связи")); return "Работает ✓ Сообщение отправлено в комнату"; }

        public override async Task Send(HubEvent e)
        {
            var url = $"{V("server").TrimEnd('/')}/_matrix/client/v3/rooms/{Esc(V("room"))}/send/m.room.message/nisland{DateTime.UtcNow.Ticks}";
            var body = e.Title + (e.Text != "" ? "\n" + e.Text : "");
            var req = new HttpRequestMessage(HttpMethod.Put, url)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    msgtype = "m.text", body, format = "org.matrix.custom.html",
                    formatted_body = "<b>" + System.Net.WebUtility.HtmlEncode(e.Title) + "</b>" + (e.Text != "" ? "<br>" + System.Net.WebUtility.HtmlEncode(e.Text) : ""),
                }), Encoding.UTF8, "application/json"),
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", V("token"));
            using var _ = await Integrations.Json(req);
            Status = "Отправлено " + DateTime.Now.ToString("HH:mm");
        }
    }

    // =====================================================================
    //  Работа: Notion, Trello, Linear, Jira, GitLab
    // =====================================================================

    /// <summary>Общая часть для «списков задач с сайта»: строки, всплывашка о новых, статус.</summary>
    public abstract class TaskListHub : Integration
    {
        protected void Apply(List<(string Id, string Title, string Sub, string Url)> items, string what)
        {
            var seen = (St("seen") ?? "").Split(',').ToHashSet();
            if (St("seen") != null)
                foreach (var it in items.Where(i => !seen.Contains(i.Id)).Take(3)) Notify(Name + " · " + what, it.Title, it.Url);
            SetSt("seen", string.Join(",", items.Select(i => i.Id).Take(60)));
            Rows = items.Take(10).Select(i => new HubRow { Title = i.Title, Sub = i.Sub, Url = i.Url }).ToList();
            Status = items.Count == 0 ? "Пусто ✓" : $"{what}: {items.Count}";
        }
    }

    public class NotionHub : TaskListHub
    {
        public override string Key => "notion";
        public override string Name => "Notion";
        public override string Glyph => "";
        public override string Color => "#FFFFFF";
        public override string Desc => "Записи из базы Notion (задачи, заметки, чтение) в острове — последние изменённые сверху";
        public override string Help => "notion.so/my-integrations → New integration → скопируй токен. Открой базу → ⋯ → Connections → добавь интеграцию. ID базы — 32 символа в ссылке на неё";
        public override string HelpUrl => "https://www.notion.so/my-integrations";
        public override HubField[] Fields => new[] { new HubField("token", "Токен интеграции", "ntn_…", "secret"), new HubField("db", "ID базы данных", "ссылка или 32 символа") };
        public override int PollSeconds => 300;

        public override async Task Poll()
        {
            var id = Regex.Match(V("db"), "[0-9a-fA-F]{32}").Value;
            if (id == "") throw new InvalidOperationException("ID базы — 32 символа из ссылки на неё");
            using var doc = await Integrations.PostJson($"https://api.notion.com/v1/databases/{id}/query",
                new { page_size = 10, sorts = new[] { new { timestamp = "last_edited_time", direction = "descending" } } },
                r => { r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", V("token")); r.Headers.Add("Notion-Version", "2022-06-28"); });
            var items = new List<(string, string, string, string)>();
            foreach (var p in doc.RootElement.GetProperty("results").EnumerateArray())
            {
                string title = "", status = "";
                foreach (var prop in p.GetProperty("properties").EnumerateObject())
                {
                    var t = Integrations.S(prop.Value, "type");
                    if (t == "title") title = string.Concat(prop.Value.GetProperty("title").EnumerateArray().Select(x => Integrations.S(x, "plain_text")));
                    else if ((t == "status" || t == "select") && prop.Value.GetProperty(t).ValueKind == JsonValueKind.Object) status = Integrations.S(prop.Value.GetProperty(t), "name");
                }
                DateTime.TryParse(Integrations.S(p, "last_edited_time"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var ed);
                items.Add((Integrations.S(p, "id"), title == "" ? "Без названия" : title, string.Join(" · ", new[] { status, ed.ToString("d MMM HH:mm", Fmt.Ru) }.Where(x => x != "")), Integrations.S(p, "url")));
            }
            Apply(items, "Записей");
        }
    }

    public class TrelloHub : TaskListHub
    {
        public override string Key => "trello";
        public override string Name => "Trello";
        public override string Glyph => "";
        public override string Color => "#0079BF";
        public override string Desc => "Твои карточки Trello со сроками: ближайшие дедлайны сверху, всплывашка о новых карточках";
        public override string Help => "trello.com/power-ups/admin → создай Power-Up → API key, затем по ссылке «Token» рядом с ключом получи токен";
        public override string HelpUrl => "https://trello.com/power-ups/admin";
        public override HubField[] Fields => new[] { new HubField("key", "API Key"), new HubField("token", "Token", "", "secret") };
        public override int PollSeconds => 300;

        public override async Task Poll()
        {
            using var doc = await Integrations.GetJson($"https://api.trello.com/1/members/me/cards?filter=open&fields=name,due,dueComplete,shortUrl&key={Esc(V("key"))}&token={Esc(V("token"))}");
            var items = doc.RootElement.EnumerateArray().Where(c => Integrations.S(c, "dueComplete") != "True")
                .Select(c => (Id: Integrations.S(c, "id"), Name: Integrations.S(c, "name"), Due: DateTime.TryParse(Integrations.S(c, "due"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : (DateTime?)null, Url: Integrations.S(c, "shortUrl")))
                .OrderBy(c => c.Due ?? DateTime.MaxValue)
                .Select(c => (c.Id, c.Name, c.Due is DateTime d ? (d < DateTime.Now ? "просрочено · " : "срок ") + d.ToString("d MMM HH:mm", Fmt.Ru) : "без срока", c.Url)).ToList();
            Apply(items, "Карточек");
        }
    }

    public class LinearHub : TaskListHub
    {
        public override string Key => "linear";
        public override string Name => "Linear";
        public override string Glyph => "";
        public override string Color => "#5E6AD2";
        public override string Desc => "Открытые задачи Linear, назначенные на тебя, и всплывашка о новых";
        public override string Help => "Linear → Settings → Security & access → Personal API keys";
        public override string HelpUrl => "https://linear.app/settings/account/security";
        public override HubField[] Fields => new[] { new HubField("key", "API key", "lin_api_…", "secret") };
        public override int PollSeconds => 300;

        public override async Task Poll()
        {
            const string q = "{ viewer { assignedIssues(first: 15, filter: { state: { type: { nin: [\"completed\", \"canceled\"] } } }) { nodes { id identifier title url state { name } } } } }";
            using var doc = await Integrations.PostJson("https://api.linear.app/graphql", new { query = q }, r => r.Headers.TryAddWithoutValidation("Authorization", V("key")));
            if (doc.RootElement.TryGetProperty("errors", out var err)) throw new InvalidOperationException(Integrations.S(err[0], "message"));
            var items = doc.RootElement.GetProperty("data").GetProperty("viewer").GetProperty("assignedIssues").GetProperty("nodes").EnumerateArray()
                .Select(i => (Integrations.S(i, "id"), Integrations.S(i, "identifier") + " " + Integrations.S(i, "title"), Integrations.S(i.GetProperty("state"), "name"), Integrations.S(i, "url"))).ToList();
            Apply(items, "Задач");
        }
    }

    public class JiraHub : TaskListHub
    {
        public override string Key => "jira";
        public override string Name => "Jira";
        public override string Glyph => "";
        public override string Color => "#2684FF";
        public override string Desc => "Нерешённые задачи Jira, назначенные на тебя, — последние обновлённые сверху";
        public override string Help => "Сайт — твой адрес вида company.atlassian.net. Токен: id.atlassian.com → Security → API tokens";
        public override string HelpUrl => "https://id.atlassian.com/manage-profile/security/api-tokens";
        public override HubField[] Fields => new[]
        {
            new HubField("site", "Сайт", "company.atlassian.net"), new HubField("email", "Почта"), new HubField("token", "API-токен", "", "secret"),
        };
        public override int PollSeconds => 300;

        public override async Task Poll()
        {
            var site = "https://" + Regex.Replace(V("site"), @"^https?://", "").TrimEnd('/');
            const string jql = "assignee = currentUser() AND resolution = Unresolved ORDER BY updated DESC";
            void Auth(HttpRequestMessage r) => r.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(V("email") + ":" + V("token"))));
            JsonDocument doc;
            try { doc = await Integrations.GetJson($"{site}/rest/api/3/search/jql?jql={Esc(jql)}&fields=summary,status&maxResults=15", Auth); }
            catch (HttpRequestException e) when ((int?)e.StatusCode is 404 or 410) { doc = await Integrations.GetJson($"{site}/rest/api/2/search?jql={Esc(jql)}&fields=summary,status&maxResults=15", Auth); }
            using (doc)
            {
                var items = doc.RootElement.GetProperty("issues").EnumerateArray().Select(i =>
                {
                    var key = Integrations.S(i, "key");
                    var f = i.GetProperty("fields");
                    return (key, key + " " + Integrations.S(f, "summary"), f.TryGetProperty("status", out var st) ? Integrations.S(st, "name") : "", site + "/browse/" + key);
                }).ToList();
                Apply(items, "Задач");
            }
        }
    }

    public class GitLabHub : TaskListHub
    {
        public override string Key => "gitlab";
        public override string Name => "GitLab";
        public override string Glyph => "";
        public override string Color => "#FC6D26";
        public override string Desc => "Список дел GitLab: упоминания, назначенные merge requests и issues, ревью";
        public override string Help => "GitLab → Preferences → Access tokens, права read_api. Свой сервер — укажи его адрес";
        public override string HelpUrl => "https://gitlab.com/-/user_settings/personal_access_tokens";
        public override HubField[] Fields => new[] { new HubField("token", "Токен", "glpat-…", "secret"), new HubField("server", "Сервер", "https://gitlab.com", Required: false, Default: "https://gitlab.com") };
        public override int PollSeconds => 180;

        public override async Task Poll()
        {
            using var doc = await Integrations.GetJson((V("server") == "" ? "https://gitlab.com" : V("server").TrimEnd('/')) + "/api/v4/todos?state=pending&per_page=15",
                r => r.Headers.TryAddWithoutValidation("PRIVATE-TOKEN", V("token")));
            var items = doc.RootElement.EnumerateArray().Select(t => (Integrations.S(t, "id"), Integrations.S(t.GetProperty("target"), "title"),
                Integrations.S(t, "action_name").Replace('_', ' ') + " · " + (t.TryGetProperty("project", out var p) && p.ValueKind == JsonValueKind.Object ? Integrations.S(p, "name_with_namespace") : ""),
                Integrations.S(t, "target_url"))).ToList();
            Apply(items, "Дел");
        }
    }

    // =====================================================================
    //  Развлечения и соцсети: Twitch, Steam, Mastodon, Bluesky, Hacker News
    // =====================================================================

    public class TwitchHub : Integration
    {
        public override string Key => "twitch";
        public override string Name => "Twitch";
        public override string Glyph => "";
        public override string Color => "#9146FF";
        public override string Desc => "Любимые стримеры: всплывашка, когда кто-то начал стрим, и кто сейчас в эфире — с игрой и числом зрителей";
        public override string Help => "dev.twitch.tv/console → Register Your Application (OAuth Redirect: http://localhost) → Client ID и New Secret. Каналы — логины через запятую";
        public override string HelpUrl => "https://dev.twitch.tv/console/apps";
        public override HubField[] Fields => new[]
        {
            new HubField("id", "Client ID"), new HubField("secret", "Client Secret", "", "secret"), new HubField("channels", "Каналы", "shroud, ninja", "multi"),
        };
        public override int PollSeconds => 120;

        string _token;
        DateTime _tokenUntil;

        async Task<string> Token()
        {
            if (_token != null && DateTime.Now < _tokenUntil) return _token;
            using var resp = await Http.PostAsync($"https://id.twitch.tv/oauth2/token?client_id={Esc(V("id"))}&client_secret={Esc(V("secret"))}&grant_type=client_credentials", null);
            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode) throw new HttpRequestException(Integrations.Snip(body), null, resp.StatusCode);
            using var doc = JsonDocument.Parse(body);
            _token = Integrations.S(doc.RootElement, "access_token");
            _tokenUntil = DateTime.Now.AddSeconds(doc.RootElement.GetProperty("expires_in").GetInt32() - 300);
            return _token;
        }

        public override async Task Poll()
        {
            var logins = Lines("channels").Select(c => c.ToLowerInvariant().Replace("https://www.twitch.tv/", "").Trim('/')).Take(50).ToList();
            if (logins.Count == 0) { Rows = new(); return; }
            var tok = await Token();
            using var doc = await Integrations.GetJson("https://api.twitch.tv/helix/streams?first=50&" + string.Join("&", logins.Select(l => "user_login=" + Esc(l))),
                r => { r.Headers.TryAddWithoutValidation("Client-Id", V("id")); r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tok); });
            var live = doc.RootElement.GetProperty("data").EnumerateArray().Select(s => (Login: Integrations.S(s, "user_login"), Name: Integrations.S(s, "user_name"),
                Title: Integrations.S(s, "title"), Game: Integrations.S(s, "game_name"), Viewers: Integrations.S(s, "viewer_count"))).ToList();
            var was = (St("live") ?? "").Split(',').ToHashSet();
            if (St("live") != null)
                foreach (var s in live.Where(s => !was.Contains(s.Login))) Notify($"{s.Name} в эфире", (s.Game != "" ? s.Game + " · " : "") + s.Title, "https://www.twitch.tv/" + s.Login);
            SetSt("live", string.Join(",", live.Select(s => s.Login)));
            Rows = live.Select(s => new HubRow { Title = "🔴 " + s.Name + " · " + s.Title, Sub = $"{s.Game} · {s.Viewers} зрителей", Url = "https://www.twitch.tv/" + s.Login }).ToList();
            Status = live.Count == 0 ? "Никто не в эфире" : $"В эфире: {live.Count}";
        }
    }

    public class SteamHub : Integration
    {
        public override string Key => "steam";
        public override string Name => "Steam";
        public override string Glyph => "";
        public override string Color => "#66C0F4";
        public override string Desc => "Кто из друзей в сети и во что играет — и всплывашка, когда друг запускает игру";
        public override string Help => "Ключ: steamcommunity.com/dev/apikey. SteamID64 — 17 цифр (найди свой профиль на steamid.io). Профиль и список друзей должны быть открыты";
        public override string HelpUrl => "https://steamcommunity.com/dev/apikey";
        public override HubField[] Fields => new[]
        {
            new HubField("key", "Web API Key", "", "secret"), new HubField("id", "Мой SteamID64", "7656119…"),
            new HubField("notify", "Сообщать, когда друг начал играть", "", "bool", Required: false, Default: "1"),
        };
        public override int PollSeconds => 180;

        public override async Task Poll()
        {
            using var fl = await Integrations.GetJson($"https://api.steampowered.com/ISteamUser/GetFriendList/v1/?key={Esc(V("key"))}&steamid={Esc(V("id"))}&relationship=friend");
            var ids = fl.RootElement.GetProperty("friendslist").GetProperty("friends").EnumerateArray().Select(f => Integrations.S(f, "steamid")).Take(100).ToList();
            if (ids.Count == 0) { Rows = new(); Status = "Друзей не видно"; return; }
            using var sum = await Integrations.GetJson($"https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v2/?key={Esc(V("key"))}&steamids={string.Join(",", ids)}");
            var players = sum.RootElement.GetProperty("response").GetProperty("players").EnumerateArray()
                .Select(p => (Id: Integrations.S(p, "steamid"), Name: Integrations.S(p, "personaname"), State: Integrations.S(p, "personastate"), Game: Integrations.S(p, "gameextrainfo"), Url: Integrations.S(p, "profileurl")))
                .Where(p => p.State != "0").OrderByDescending(p => p.Game != "").ThenBy(p => p.Name).ToList();
            var was = (St("games") ?? "").Split('\n').ToHashSet();
            var now = players.Where(p => p.Game != "").Select(p => p.Id + "|" + p.Game).ToList();
            if (St("games") != null && Flag("notify"))
                foreach (var p in players.Where(p => p.Game != "" && !was.Contains(p.Id + "|" + p.Game)).Take(3)) Notify(p.Name + " играет", p.Game, p.Url);
            SetSt("games", string.Join("\n", now));
            Rows = players.Take(10).Select(p => new HubRow { Title = p.Name, Sub = p.Game != "" ? "🎮 " + p.Game : "в сети", Url = p.Url }).ToList();
            Status = $"В сети: {players.Count} · играют: {now.Count}";
        }
    }

    public class MastodonHub : Integration
    {
        public override string Key => "mastodon";
        public override string Name => "Mastodon";
        public override string Glyph => "";
        public override string Color => "#6364FF";
        public override string Desc => "Упоминания, ответы, подписки и лайки в Mastodon — всплывашкой в острове";
        public override string Help => "Твой сервер → Настройки → Разработка → Новое приложение (право read:notifications) → скопируй токен";
        public override HubField[] Fields => new[] { new HubField("server", "Сервер", "https://mastodon.social", Default: "https://mastodon.social"), new HubField("token", "Токен", "", "secret") };
        public override int PollSeconds => 120;

        static string Plain(string html) => System.Net.WebUtility.HtmlDecode(Regex.Replace(html ?? "", "<[^>]+>", " ")).Trim();

        public override async Task Poll()
        {
            using var doc = await Integrations.GetJson(V("server").TrimEnd('/') + "/api/v1/notifications?limit=10", r => r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", V("token")));
            long.TryParse(St("last"), out var last);
            long max = last;
            var rows = new List<HubRow>();
            foreach (var n in doc.RootElement.EnumerateArray())
            {
                long.TryParse(Integrations.S(n, "id"), out var id);
                max = Math.Max(max, id);
                var acc = n.GetProperty("account");
                var who = Integrations.S(acc, "display_name") is { Length: > 0 } dn ? dn : Integrations.S(acc, "acct");
                var type = Integrations.S(n, "type") switch { "mention" => "упомянул(а) тебя", "reblog" => "продвинул(а)", "favourite" => "лайкнул(а)", "follow" => "подписался(ась)", "poll" => "опрос завершён", var t => t };
                var text = n.TryGetProperty("status", out var st) && st.ValueKind == JsonValueKind.Object ? Plain(Integrations.S(st, "content")) : "";
                var url = n.TryGetProperty("status", out var st2) && st2.ValueKind == JsonValueKind.Object ? Integrations.S(st2, "url") : Integrations.S(acc, "url");
                rows.Add(new HubRow { Title = who + " " + type, Sub = text.Length > 100 ? text[..100] + "…" : text, Url = url });
                if (St("last") != null && id > last) Notify(who + " " + type, text, url);
            }
            Rows = rows;
            SetSt("last", max.ToString());
            Status = "Обновлено " + DateTime.Now.ToString("HH:mm");
        }
    }

    public class BlueskyHub : Integration
    {
        public override string Key => "bluesky";
        public override string Name => "Bluesky";
        public override string Glyph => "";
        public override string Color => "#0085FF";
        public override string Desc => "Ответы, упоминания, лайки и новые подписчики в Bluesky — всплывашкой в острове";
        public override string Help => "bsky.app → Настройки → Конфиденциальность и безопасность → Пароли приложений → добавь. Обычный пароль не нужен";
        public override string HelpUrl => "https://bsky.app/settings/app-passwords";
        public override HubField[] Fields => new[] { new HubField("handle", "Логин", "name.bsky.social"), new HubField("password", "Пароль приложения", "xxxx-xxxx-xxxx-xxxx", "secret") };
        public override int PollSeconds => 120;

        string _jwt;

        async Task<string> Session(bool fresh = false)
        {
            if (_jwt != null && !fresh) return _jwt;
            using var doc = await Integrations.PostJson("https://bsky.social/xrpc/com.atproto.server.createSession", new { identifier = V("handle").TrimStart('@'), password = V("password") });
            return _jwt = Integrations.S(doc.RootElement, "accessJwt");
        }

        public override async Task Poll()
        {
            const string url = "https://bsky.social/xrpc/app.bsky.notification.listNotifications?limit=15";
            JsonDocument doc;
            var jwt = await Session();
            try { doc = await Integrations.GetJson(url, r => r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt)); }
            catch (HttpRequestException e) when ((int?)e.StatusCode is 400 or 401)
            {
                jwt = await Session(true);   // сессия истекла — входим заново
                doc = await Integrations.GetJson(url, r => r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt));
            }
            using (doc)
            {
                var seen = St("last") ?? "";
                string newest = null;
                var rows = new List<HubRow>();
                foreach (var n in doc.RootElement.GetProperty("notifications").EnumerateArray())
                {
                    var at = Integrations.S(n, "indexedAt");
                    newest ??= at;
                    var a = n.GetProperty("author");
                    var who = Integrations.S(a, "displayName") is { Length: > 0 } dn ? dn : "@" + Integrations.S(a, "handle");
                    var type = Integrations.S(n, "reason") switch { "like" => "лайкнул(а)", "repost" => "сделал(а) репост", "follow" => "подписался(ась)", "mention" => "упомянул(а) тебя", "reply" => "ответил(а)", "quote" => "процитировал(а)", var r => r };
                    var text = n.TryGetProperty("record", out var rec) ? Integrations.S(rec, "text") : "";
                    var profile = "https://bsky.app/profile/" + Integrations.S(a, "handle");
                    rows.Add(new HubRow { Title = who + " " + type, Sub = text.Length > 100 ? text[..100] + "…" : text, Url = profile });
                    if (seen != "" && string.CompareOrdinal(at, seen) > 0 && Integrations.S(n, "isRead") != "True") Notify(who + " " + type, text, profile);
                }
                if (newest != null) SetSt("last", newest);
                Rows = rows.Take(10).ToList();
                Status = "Обновлено " + DateTime.Now.ToString("HH:mm");
            }
        }
    }

    public class HackerNewsHub : Integration
    {
        public override string Key => "hn";
        public override string Name => "Hacker News";
        public override string Glyph => "";
        public override string Color => "#FF6600";
        public override string Desc => "Главные новости Hacker News; всплывашка о статьях, набравших много очков. Без регистрации";
        public override HubField[] Fields => new[] { new HubField("score", "Сообщать о статьях с очками от", "300", Required: false, Default: "300") };
        public override int PollSeconds => 1800;

        public override async Task Poll()
        {
            using var top = await Integrations.GetJson("https://hacker-news.firebaseio.com/v0/topstories.json");
            var ids = top.RootElement.EnumerateArray().Take(12).Select(e => e.GetInt64()).ToList();
            var items = await Task.WhenAll(ids.Select(async id =>
            {
                try { using var d = await Integrations.GetJson($"https://hacker-news.firebaseio.com/v0/item/{id}.json"); return (id, Integrations.S(d.RootElement, "title"), Integrations.S(d.RootElement, "score"), Integrations.S(d.RootElement, "url"), Integrations.S(d.RootElement, "descendants")); }
                catch { return (id, "", "0", "", "0"); }
            }));
            int.TryParse(V("score"), out var min);
            var seen = (St("seen") ?? "").Split(',').ToHashSet();
            if (St("seen") != null && min > 0)
                foreach (var it in items.Where(i => int.TryParse(i.Item3, out var s) && s >= min && !seen.Contains(i.id.ToString())).Take(2))
                    Notify("Hacker News · " + it.Item3 + " очков", it.Item2, it.Item4 != "" ? it.Item4 : $"https://news.ycombinator.com/item?id={it.id}");
            SetSt("seen", string.Join(",", items.Where(i => int.TryParse(i.Item3, out var s) && s >= min).Select(i => i.id.ToString()).Concat(seen.Where(s => s != "")).Distinct().Take(200)));
            Rows = items.Where(i => i.Item2 != "").Select(i => new HubRow
            {
                Title = i.Item2, Sub = $"{i.Item3} очков · {i.Item5} комментариев", Url = i.Item4 != "" ? i.Item4 : $"https://news.ycombinator.com/item?id={i.id}",
            }).ToList();
            Status = "Обновлено " + DateTime.Now.ToString("HH:mm");
        }
    }

    // =====================================================================
    //  Дом и деньги: Philips Hue, акции
    // =====================================================================

    public class HueHub : Integration
    {
        public override string Key => "hue";
        public override string Name => "Philips Hue";
        public override string Glyph => "";
        public override string Color => "#FFB800";
        public override string Desc => "Свет Philips Hue прямо в острове: включить и выключить лампы и комнаты одним нажатием. Работает в домашней сети, без облака";
        public override string Help => "Нажми большую кнопку на мосту Hue и в течение 30 секунд нажми «Проверить» — ключ создастся сам. Адрес моста найдётся автоматически";
        public override HubField[] Fields => new[] { new HubField("ip", "Адрес моста", "найдётся сам", Required: false), new HubField("key", "Ключ", "создаётся сам", "secret", Required: false) };
        public override int PollSeconds => 30;

        async Task<string> Bridge()
        {
            if (V("ip") != "") return V("ip");
            using var doc = await Integrations.GetJson("https://discovery.meethue.com/");
            var ip = doc.RootElement.EnumerateArray().Select(b => Integrations.S(b, "internalipaddress")).FirstOrDefault(x => x != "");
            if (ip == null) throw new InvalidOperationException("мост Hue не найден в сети — впиши его адрес вручную");
            Integrations.SetValue(this, "ip", ip);
            return ip;
        }

        public override async Task<string> Test()
        {
            var ip = await Bridge();
            if (V("key") == "")
            {
                using var doc = await Integrations.PostJson($"http://{ip}/api", new { devicetype = "nisland#" + Environment.MachineName });
                var first = doc.RootElement[0];
                if (first.TryGetProperty("error", out var err)) return "Нажми кнопку на мосту Hue и сразу снова «Проверить» (" + Integrations.S(err, "description") + ")";
                Integrations.SetValue(this, "key", Integrations.S(first.GetProperty("success"), "username"));
            }
            await Poll();
            return $"Работает ✓ Ламп и комнат: {Rows.Count}";
        }

        public override async Task Poll()
        {
            if (V("key") == "") { Status = "Нажми «Проверить», чтобы подключить мост"; return; }
            var ip = await Bridge();
            var baseUrl = $"http://{ip}/api/{V("key")}";
            using var groups = await Integrations.GetJson(baseUrl + "/groups");
            using var lights = await Integrations.GetJson(baseUrl + "/lights");
            var rows = new List<HubRow>();
            foreach (var g in groups.RootElement.EnumerateObject())
            {
                if (Integrations.S(g.Value, "type") is not ("Room" or "Zone")) continue;
                var id = g.Name;
                bool on = g.Value.TryGetProperty("state", out var st) && st.TryGetProperty("any_on", out var ao) && ao.GetBoolean();
                rows.Add(new HubRow { Title = "🏠 " + Integrations.S(g.Value, "name"), Sub = on ? "свет горит" : "выключено", On = on,
                    Toggle = async v => { await Put($"{baseUrl}/groups/{id}/action", new { on = v }); await Poll(); return null; } });
            }
            foreach (var l in lights.RootElement.EnumerateObject())
            {
                var id = l.Name;
                var st = l.Value.GetProperty("state");
                bool on = st.GetProperty("on").GetBoolean(), reach = !st.TryGetProperty("reachable", out var r) || r.GetBoolean();
                int bri = st.TryGetProperty("bri", out var b) ? (int)Math.Round(b.GetInt32() / 2.54) : 0;
                rows.Add(new HubRow { Title = Integrations.S(l.Value, "name"), Sub = !reach ? "недоступна" : on ? $"горит · {bri}%" : "выключена", On = reach ? on : null,
                    Toggle = reach ? async v => { await Put($"{baseUrl}/lights/{id}/state", new { on = v }); await Poll(); return null; } : null });
            }
            Rows = rows;
            Status = $"Горит: {rows.Count(x => x.On == true)} из {rows.Count}";
        }

        static async Task Put(string url, object body)
        {
            using var resp = await Integrations.Http.PutAsync(url, new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"));
            resp.EnsureSuccessStatusCode();
        }
    }

    public class StocksHub : Integration
    {
        public override string Key => "stocks";
        public override string Name => "Акции и индексы";
        public override string Glyph => "";
        public override string Color => "#34C759";
        public override string Desc => "Цены акций, ETF и индексов (Apple, Tesla, S&P 500, Nasdaq) с изменением за день; всплывашка при резком движении. Без ключей";
        public override string Help => "Тикеры через запятую, как на Yahoo Finance: AAPL, TSLA, NVDA, ^GSPC (S&P 500), ^IXIC (Nasdaq), EURUSD=X, GC=F (золото)";
        public override HubField[] Fields => new[]
        {
            new HubField("symbols", "Тикеры", "AAPL, NVDA, ^GSPC", Default: "AAPL, NVDA, ^GSPC"),
            new HubField("move", "Сообщать при изменении за день, %", "0 — не сообщать", Required: false, Default: "4"),
        };
        public override int PollSeconds => 900;

        public override async Task Poll()
        {
            double.TryParse(V("move"), NumberStyles.Float, CultureInfo.InvariantCulture, out var move);
            var rows = new List<HubRow>();
            foreach (var sym in Lines("symbols").Select(s => s.ToUpperInvariant()).Take(15))
            {
                try
                {
                    using var doc = await Integrations.GetJson($"https://query1.finance.yahoo.com/v8/finance/chart/{Esc(sym)}?range=1d&interval=1d",
                        r => r.Headers.UserAgent.ParseAdd("Mozilla/5.0"));
                    var meta = doc.RootElement.GetProperty("chart").GetProperty("result")[0].GetProperty("meta");
                    double price = meta.GetProperty("regularMarketPrice").GetDouble();
                    double prev = meta.TryGetProperty("chartPreviousClose", out var pc) ? pc.GetDouble() : meta.TryGetProperty("previousClose", out var p2) ? p2.GetDouble() : price;
                    double ch = prev > 0 ? (price - prev) / prev * 100 : 0;
                    var cur = Integrations.S(meta, "currency");
                    var name = Integrations.S(meta, "shortName") is { Length: > 0 } sn ? sn : sym;
                    rows.Add(new HubRow
                    {
                        Title = $"{sym} · {price.ToString(price >= 100 ? "N2" : "N4", CultureInfo.GetCultureInfo("en-US"))} {cur}",
                        Sub = $"{(ch >= 0 ? "▲" : "▼")} {Math.Abs(ch):0.00}% за день · {name}", Url = "https://finance.yahoo.com/quote/" + Uri.EscapeDataString(sym),
                    });
                    var flag = "moved:" + sym;
                    if (move > 0 && Math.Abs(ch) >= move && St(flag) != DateTime.Today.ToString("yyyyMMdd"))
                    {
                        SetSt(flag, DateTime.Today.ToString("yyyyMMdd"));
                        Notify($"{sym} {(ch >= 0 ? "вырос" : "упал")} на {Math.Abs(ch):0.0}%", $"{price:N2} {cur} · {name}", "https://finance.yahoo.com/quote/" + Uri.EscapeDataString(sym));
                    }
                }
                catch (Exception e) { rows.Add(new HubRow { Title = sym, Sub = "нет данных: " + Integrations.Reason(e) }); }
            }
            Rows = rows;
            Status = "Обновлено " + DateTime.Now.ToString("HH:mm");
        }
    }
}
