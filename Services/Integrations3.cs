using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NIsland.Core;

namespace NIsland.Services
{
    // =====================================================================
    //  Почта по IMAP: Gmail, Outlook, ukr.net, i.ua, Яндекс…
    // =====================================================================

    public class MailHub : Integration
    {
        public override string Key => "mail";
        public override string Name => "Почта (Gmail, Outlook, ukr.net)";
        public override string Glyph => "";
        public override string Color => "#0A84FF";
        public override string Desc => "Непрочитанные письма в острове и всплывашка о новых — от кого и тема. Письма только читаются заголовками и не помечаются прочитанными";
        public override string Help => "Gmail: включи двухэтапную проверку и создай «Пароль приложения» (myaccount.google.com/apppasswords) — обычный пароль Google не подойдёт. " +
                                       "Outlook: outlook.office365.com. ukr.net: imap.ukr.net (включи IMAP в настройках ящика). Пароль хранится зашифрованным";
        public override string HelpUrl => "https://myaccount.google.com/apppasswords";
        public override HubField[] Fields => new[]
        {
            new HubField("host", "Сервер IMAP", "imap.gmail.com", Default: "imap.gmail.com"),
            new HubField("user", "Логин (почта)", "name@gmail.com"),
            new HubField("password", "Пароль приложения", "", "secret"),
            new HubField("notify", "Сообщать о новых письмах", "", "bool", Required: false, Default: "1"),
            new HubField("folder", "Папка", "INBOX", Required: false, Default: "INBOX"),
        };
        public override string[] DefaultSend => new[] { "mail" };
        public override int PollSeconds => 120;

        string WebUrl => V("host").ToLowerInvariant() switch
        {
            var h when h.Contains("gmail") => "https://mail.google.com/",
            var h when h.Contains("outlook") || h.Contains("office365") || h.Contains("hotmail") => "https://outlook.live.com/mail/",
            var h when h.Contains("ukr.net") => "https://mail.ukr.net/",
            var h when h.Contains("i.ua") => "https://mail.i.ua/",
            var h when h.Contains("yandex") => "https://mail.yandex.ru/",
            var h when h.Contains("proton") => "https://mail.proton.me/",
            _ => null,
        };

        public override async Task<string> Test()
        {
            await Poll();
            return "Работает ✓ " + Status;
        }

        public override async Task Poll()
        {
            var host = V("host").Trim();
            int port = 993;
            if (host.Contains(':') && int.TryParse(host[(host.LastIndexOf(':') + 1)..], out var p)) { port = p; host = host[..host.LastIndexOf(':')]; }
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            using var imap = await Imap.Connect(host, port, cts.Token);
            await imap.Cmd($"LOGIN {Imap.Q(V("user"))} {Imap.Q(V("password"))}", cts.Token, "неверный логин или пароль приложения");
            await imap.Cmd("EXAMINE " + Imap.Q(V("folder") == "" ? "INBOX" : V("folder")), cts.Token);   // только чтение: письма не становятся прочитанными
            var search = await imap.Cmd("UID SEARCH UNSEEN", cts.Token);
            var uids = search.SelectMany(l => l.StartsWith("* SEARCH") ? l[8..].Split(' ', StringSplitOptions.RemoveEmptyEntries) : Array.Empty<string>())
                             .Select(x => uint.TryParse(x, out var u) ? u : 0).Where(u => u > 0).OrderByDescending(u => u).ToList();
            var mails = new List<(uint Uid, string From, string Subject, DateTime? Date)>();
            if (uids.Count > 0)
            {
                var lines = await imap.Cmd($"UID FETCH {string.Join(",", uids.Take(8))} (UID BODY.PEEK[HEADER.FIELDS (FROM SUBJECT DATE)])", cts.Token);
                uint cur = 0;
                foreach (var l in lines)
                {
                    var m = Regex.Match(l, @"UID (\d+)");
                    if (l.StartsWith("* ") && m.Success) { cur = uint.Parse(m.Groups[1].Value); continue; }
                    if (cur == 0 || !l.Contains(':')) continue;
                    var headers = Regex.Replace(l, @"\r?\n[ \t]+", " ").Split('\n');
                    string H(string k) => headers.FirstOrDefault(h => h.StartsWith(k + ":", StringComparison.OrdinalIgnoreCase))?[(k.Length + 1)..].Trim();
                    var from = Osint.Decode(H("From") ?? "");
                    var name = Regex.Match(from, @"^""?([^""<]+?)""?\s*<").Groups[1].Value;
                    DateTime? date = DateTime.TryParse(Regex.Replace(H("Date") ?? "", @"\s*\(.*\)$", ""), CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
                    mails.Add((cur, name != "" ? name : from.Trim('<', '>'), Osint.Decode(H("Subject") ?? "(без темы)"), date));
                    cur = 0;
                }
            }
            try { await imap.Cmd("LOGOUT", cts.Token); } catch { }

            var seen = (St("seen") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
            if (St("seen") != null && Flag("notify"))
            {
                var fresh = mails.Where(x => !seen.Contains(x.Uid.ToString())).ToList();
                if (fresh.Count == 1) { Notify("✉ " + fresh[0].From, fresh[0].Subject, WebUrl); Integrations.Publish("mail", "Письмо от " + fresh[0].From, fresh[0].Subject); }
                else if (fresh.Count > 1) { Notify($"Новых писем: {fresh.Count}", string.Join(" · ", fresh.Take(3).Select(x => x.From)), WebUrl); Integrations.Publish("mail", $"Новых писем: {fresh.Count}", string.Join("\n", fresh.Take(5).Select(x => x.From + ": " + x.Subject))); }
            }
            SetSt("seen", string.Join(",", uids.Take(50)));
            Rows = mails.Select(x => new HubRow { Title = x.Subject, Sub = x.From + (x.Date is DateTime dt ? " · " + (dt.Date == DateTime.Today ? dt.ToString("HH:mm") : dt.ToString("d MMM", Fmt.Ru)) : ""), Url = WebUrl }).ToList();
            Status = uids.Count == 0 ? "Всё прочитано ✓" : "Непрочитанных: " + uids.Count;
        }

        /// <summary>Минимальный IMAP-клиент: TLS, команды с метками, литералы {n}.</summary>
        sealed class Imap : IDisposable
        {
            readonly TcpClient _tcp;
            readonly SslStream _ssl;
            readonly byte[] _buf = new byte[8192];
            int _len, _pos, _tag;

            Imap(TcpClient tcp, SslStream ssl) { _tcp = tcp; _ssl = ssl; }

            public static async Task<Imap> Connect(string host, int port, CancellationToken ct)
            {
                var tcp = new TcpClient();
                await tcp.ConnectAsync(host, port, ct);
                var ssl = new SslStream(tcp.GetStream());
                await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = host }, ct);
                var im = new Imap(tcp, ssl);
                var greet = await im.Line(ct);
                if (!greet.StartsWith("* OK")) throw new IOException("сервер не похож на IMAP: " + greet);
                return im;
            }

            public static string Q(string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

            async Task<int> Fill(CancellationToken ct)
            {
                _len = await _ssl.ReadAsync(_buf, ct);
                _pos = 0;
                if (_len == 0) throw new IOException("сервер закрыл соединение");
                return _len;
            }

            async Task<string> Line(CancellationToken ct)
            {
                var ms = new MemoryStream();
                while (true)
                {
                    if (_pos >= _len) await Fill(ct);
                    byte b = _buf[_pos++];
                    if (b == '\n') break;
                    if (b != '\r') ms.WriteByte(b);
                }
                return Encoding.UTF8.GetString(ms.ToArray());
            }

            async Task<string> Exact(int n, CancellationToken ct)
            {
                var data = new byte[n];
                int got = 0;
                while (got < n)
                {
                    if (_pos >= _len) await Fill(ct);
                    int take = Math.Min(n - got, _len - _pos);
                    Array.Copy(_buf, _pos, data, got, take);
                    _pos += take; got += take;
                }
                return Encoding.UTF8.GetString(data);
            }

            /// <summary>Команда → все строки ответа; литерал {n} возвращается отдельной строкой целиком.</summary>
            public async Task<List<string>> Cmd(string cmd, CancellationToken ct, string onNo = null)
            {
                var tag = "a" + (++_tag);
                var bytes = Encoding.UTF8.GetBytes(tag + " " + cmd + "\r\n");
                await _ssl.WriteAsync(bytes, ct);
                var res = new List<string>();
                while (true)
                {
                    var line = await Line(ct);
                    var lit = Regex.Match(line, @"\{(\d+)\}$");
                    if (lit.Success)
                    {
                        res.Add(line);
                        res.Add(await Exact(int.Parse(lit.Groups[1].Value), ct));
                        continue;
                    }
                    if (line.StartsWith(tag + " "))
                    {
                        if (!line.StartsWith(tag + " OK")) throw new InvalidOperationException(onNo ?? line[(tag.Length + 1)..]);
                        return res;
                    }
                    res.Add(line);
                }
            }

            public void Dispose() { try { _ssl.Dispose(); _tcp.Dispose(); } catch { } }
        }
    }

    // =====================================================================
    //  Slack и Gotify: события острова наружу (Gotify — и обратно)
    // =====================================================================

    public class GotifyHub : Integration
    {
        public override string Key => "gotify";
        public override string Name => "Gotify (свой сервер пушей)";
        public override string Glyph => "";
        public override string Color => "#64D2FF";
        public override string Desc => "Свой сервер уведомлений: события острова уходят на телефон, а сообщения от скриптов и умного дома приходят в остров";
        public override string Help => "В Gotify создай приложение (его токен — для отправки) и клиента (его токен — чтобы читать сообщения)";
        public override string HelpUrl => "https://gotify.net/";
        public override HubField[] Fields => new[]
        {
            new HubField("server", "Адрес сервера", "https://push.example.com"),
            new HubField("app", "Токен приложения", "для отправки", "secret"),
            new HubField("client", "Токен клиента", "чтобы получать сообщения (необязательно)", "secret", Required: false),
        };
        public override string[] CanSend => Integrations.EventKinds.Select(e => e.Key).ToArray();
        public override int PollSeconds => 30;

        string Base => V("server").TrimEnd('/');

        public override async Task<string> Test()
        {
            await Send(new HubEvent("test", "N-Island подключён ✓", "Проверка связи с островом"));
            if (V("client") != "") await Poll();
            return "Работает ✓ Тестовое сообщение отправлено";
        }

        public override async Task Send(HubEvent e)
        {
            using var doc = await Integrations.PostJson(Base + "/message?token=" + Esc(V("app")),
                new { title = e.Title, message = e.Text == "" ? e.Title : e.Text, priority = e.Kind is "alert" or "call" ? 8 : 5 });
            if (doc.RootElement.TryGetProperty("id", out var id)) SetSt("mine", id.ToString());
            Status = "Отправлено " + DateTime.Now.ToString("HH:mm");
        }

        public override async Task Poll()
        {
            if (V("client") == "") return;
            using var doc = await Integrations.GetJson(Base + "/message?limit=10", r => r.Headers.TryAddWithoutValidation("X-Gotify-Key", V("client")));
            long.TryParse(St("last"), out var last);
            long.TryParse(St("mine"), out var mine);
            long max = last;
            var rows = new List<HubRow>();
            foreach (var m in doc.RootElement.GetProperty("messages").EnumerateArray())
            {
                long id = m.GetProperty("id").GetInt64();
                max = Math.Max(max, id);
                var title = Integrations.S(m, "title"); var msg = Integrations.S(m, "message");
                rows.Add(new HubRow { Title = title != "" ? title : msg, Sub = title != "" ? msg : "" });
                if (St("last") != null && id > last && id != mine) Notify(title != "" ? title : "Gotify", msg, null, m.TryGetProperty("priority", out var p) && p.GetInt32() >= 8);
            }
            Rows = rows.Take(8).ToList();
            if (St("last") != max.ToString()) SetSt("last", max.ToString());
            Status = "Обновлено " + DateTime.Now.ToString("HH:mm");
        }
    }

    // =====================================================================
    //  Работа: Toggl Track (таймер в острове) и WakaTime (время в коде)
    // =====================================================================

    public class TogglHub : Integration
    {
        public override string Key => "toggl";
        public override string Name => "Toggl Track";
        public override string Glyph => "";
        public override string Color => "#E57CD8";
        public override string Desc => "Учёт рабочего времени: идущий таймер в острове, старт и стоп одной кнопкой, сколько отработано сегодня. Может запускаться вместе с помодоро";
        public override string Help => "Toggl Track → Profile settings → API Token";
        public override string HelpUrl => "https://track.toggl.com/profile";
        public override HubField[] Fields => new[]
        {
            new HubField("token", "API-токен", "", "secret"),
            new HubField("desc", "Название для новых записей", "Работа", Required: false, Default: "Работа"),
            new HubField("pomodoro", "Вместе с помодоро", "фокус запускает таймер, перерыв — останавливает", "bool", Required: false, Default: "0"),
        };
        public override int PollSeconds => 60;

        const string Api = "https://api.track.toggl.com/api/v9";
        void Auth(HttpRequestMessage r) => r.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes(V("token") + ":api_token")));
        static bool _hooked;

        public override async Task<string> Test()
        {
            using var me = await Integrations.GetJson(Api + "/me", Auth);
            SetSt("ws", Integrations.S(me.RootElement, "default_workspace_id"));
            await Poll();
            return "Работает ✓ " + Integrations.S(me.RootElement, "fullname") + " · " + Status;
        }

        async Task<string> Workspace()
        {
            if (St("ws") is { Length: > 0 } ws) return ws;
            using var me = await Integrations.GetJson(Api + "/me", Auth);
            ws = Integrations.S(me.RootElement, "default_workspace_id");
            SetSt("ws", ws);
            return ws;
        }

        public async Task<string> Start(string desc = null)
        {
            var ws = await Workspace();
            using var _ = await Integrations.PostJson($"{Api}/workspaces/{ws}/time_entries",
                new { description = desc ?? V("desc"), created_with = "N-Island", start = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"), duration = -1, workspace_id = long.Parse(ws) }, Auth);
            await Poll();
            return "Таймер запущен";
        }

        public async Task<string> Stop(string id = null)
        {
            var ws = await Workspace();
            id ??= St("running");
            if (string.IsNullOrEmpty(id)) return "Таймер не идёт";
            var req = new HttpRequestMessage(HttpMethod.Patch, $"{Api}/workspaces/{ws}/time_entries/{id}/stop");
            Auth(req);
            using var resp = await Http.SendAsync(req);
            resp.EnsureSuccessStatusCode();
            await Poll();
            return "Таймер остановлен";
        }

        public override async Task Poll()
        {
            HookPomodoro();
            using var cur = await Integrations.GetJson(Api + "/me/time_entries/current", Auth);
            var rows = new List<HubRow>();
            if (cur.RootElement.ValueKind == JsonValueKind.Object)
            {
                var id = Integrations.S(cur.RootElement, "id");
                SetSt("running", id);
                DateTime.TryParse(Integrations.S(cur.RootElement, "start"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var st);
                var dur = DateTime.UtcNow - st;
                rows.Add(new HubRow { Title = "▶ " + (Integrations.S(cur.RootElement, "description") is { Length: > 0 } d ? d : "без названия"), Sub = $"идёт {(int)dur.TotalHours}:{dur.Minutes:00}", ActionText = "Стоп", Action = () => Stop(id) });
            }
            else
            {
                SetSt("running", "");
                rows.Add(new HubRow { Title = "Таймер не запущен", Sub = V("desc"), ActionText = "Старт", Action = () => Start() });
            }
            try
            {
                using var today = await Integrations.GetJson(Api + "/me/time_entries?start_date=" + Esc(DateTime.Today.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")) + "&end_date=" + Esc(DateTime.UtcNow.AddMinutes(1).ToString("yyyy-MM-ddTHH:mm:ssZ")), Auth);
                double secs = 0;
                foreach (var e in today.RootElement.EnumerateArray())
                {
                    long d = e.GetProperty("duration").GetInt64();
                    secs += d >= 0 ? d : (DateTimeOffset.UtcNow.ToUnixTimeSeconds() + d);
                }
                Status = $"Сегодня {(int)(secs / 3600)} ч {(int)(secs % 3600 / 60)} мин";
            }
            catch { Status = "Обновлено " + DateTime.Now.ToString("HH:mm"); }
            Rows = rows;
        }

        void HookPomodoro()
        {
            if (_hooked) return;
            _hooked = true;
            bool lastFocus = false;
            PomodoroService.I.Changed += () =>
            {
                var p = PomodoroService.I;
                bool focus = p.Running && p.Phase == PomoPhase.Focus;
                if (focus == lastFocus) return;   // Changed приходит каждую секунду — реагируем только на смену фазы
                lastFocus = focus;
                if (!Enabled || !Ready || !Flag("pomodoro")) return;
                _ = Task.Run(async () =>
                {
                    try { if (p.Running && p.Phase == PomoPhase.Focus) { if (string.IsNullOrEmpty(St("running"))) await Start(); } else if (!string.IsNullOrEmpty(St("running"))) await Stop(); }
                    catch (Exception e) { Status = "Ошибка: " + Integrations.Reason(e); }
                });
            };
        }
    }

    public class WakaTimeHub : Integration
    {
        public override string Key => "wakatime";
        public override string Name => "WakaTime";
        public override string Glyph => "";
        public override string Color => "#2DD4BF";
        public override string Desc => "Сколько ты сегодня программировал: всего, по языкам и проектам — из плагина WakaTime в редакторе";
        public override string Help => "wakatime.com/settings/api-key → скопируй Secret API Key";
        public override string HelpUrl => "https://wakatime.com/settings/api-key";
        public override HubField[] Fields => new[]
        {
            new HubField("key", "Secret API Key", "waka_…", "secret"),
            new HubField("goal", "Цель на день, ч", "0 — без цели", Required: false, Default: "0"),
        };
        public override int PollSeconds => 600;

        void Auth(HttpRequestMessage r) => r.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes(V("key"))));

        public override async Task Poll()
        {
            using var doc = await Integrations.GetJson("https://wakatime.com/api/v1/users/current/summaries?range=Today", Auth);
            var day = doc.RootElement.GetProperty("data")[0];
            var total = day.GetProperty("grand_total");
            double secs = total.GetProperty("total_seconds").GetDouble();
            string Top(string k) => day.TryGetProperty(k, out var arr) ? string.Join(", ", arr.EnumerateArray().Take(3).Select(x => Integrations.S(x, "name") + " " + Integrations.S(x, "text"))) : "";
            var rows = new List<HubRow> { new() { Title = $"Сегодня: {(int)(secs / 3600)} ч {(int)(secs % 3600 / 60)} мин", Sub = "в редакторе", Url = "https://wakatime.com/dashboard" } };
            if (Top("languages") is { Length: > 0 } langs) rows.Add(new HubRow { Title = "Языки", Sub = langs });
            if (Top("projects") is { Length: > 0 } projs) rows.Add(new HubRow { Title = "Проекты", Sub = projs });
            if (double.TryParse(V("goal"), NumberStyles.Float, CultureInfo.InvariantCulture, out var goal) && goal > 0)
            {
                if (secs >= goal * 3600 && St("goal") != DateTime.Today.ToString("yyyyMMdd"))
                {
                    SetSt("goal", DateTime.Today.ToString("yyyyMMdd"));
                    Notify("Цель на день выполнена 🎉", $"{goal:0.#} ч кода");
                }
                rows[0].Sub = $"цель {goal:0.#} ч · {Math.Min(100, secs / 36 / goal):0}%";
            }
            Rows = rows;
            Status = rows[0].Title;
        }
    }
}
