using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Threading;
using NIsland.Core;

namespace NIsland.Services
{
    /// <summary>Поле настройки интеграции. Kind: text | secret | multi | bool.</summary>
    public record HubField(string Key, string Label, string Hint = "", string Kind = "text", bool Required = true, string Default = "");

    /// <summary>Строка на странице «Сервисы»: запись, кнопка действия или переключатель.</summary>
    public class HubRow
    {
        public string Title, Sub, Url, ActionText;
        public Func<Task<string>> Action;
        public bool? On;
        public Func<bool, Task<string>> Toggle;
    }

    /// <summary>Событие острова, которое можно переслать наружу (Telegram, ntfy, Discord, вебхук).</summary>
    public record HubEvent(string Kind, string Title, string Text);

    public class IntegrationConfig
    {
        public bool Enabled { get; set; }
        public Dictionary<string, string> Values { get; set; } = new();
        /// <summary>Какие события пересылать (для Telegram, ntfy, Discord, вебхука). null — по умолчанию.</summary>
        public List<string> Events { get; set; }
        /// <summary>Служебное: offset Telegram, последние id, состояния.</summary>
        public Dictionary<string, string> State { get; set; } = new();
    }

    /// <summary>Подключаемый сервис. Опрос, пересылка событий и строки для страницы «Сервисы».</summary>
    public abstract class Integration
    {
        public abstract string Key { get; }
        public abstract string Name { get; }
        public abstract string Glyph { get; }
        public abstract string Color { get; }
        public abstract string Desc { get; }
        public virtual string Help => null;
        public virtual string HelpUrl => null;
        public virtual HubField[] Fields => Array.Empty<HubField>();
        /// <summary>Какие события умеет пересылать (null — не умеет).</summary>
        public virtual string[] CanSend => null;
        public virtual string[] DefaultSend => new[] { "timer", "reminder", "call", "alert" };
        /// <summary>Как часто опрашивать, с (0 — не опрашивать).</summary>
        public virtual int PollSeconds => 0;
        public virtual bool Scrobbler => false;

        public IntegrationConfig Cfg => Integrations.Cfg(Key);
        public bool Enabled => Cfg.Enabled;
        public string V(string key) => Integrations.Value(this, key);
        public bool Flag(string key) => V(key) == "1";
        public bool Ready => Fields.Where(f => f.Required).All(f => !string.IsNullOrWhiteSpace(V(f.Key)));
        public IEnumerable<string> Lines(string key) => (V(key) ?? "").Split(new[] { '\n', '\r', ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(s => s.Length > 0);
        public string St(string k) => Cfg.State.TryGetValue(k, out var v) ? v : null;
        public void SetSt(string k, string v) { Cfg.State[k] = v; Store.SaveSettings(); }

        // что показать в острове и в настройках
        public string Status = "";
        public bool Ok;
        public List<HubRow> Rows = new();
        internal DateTime NextPoll;
        internal int Fails;

        /// <summary>Проверка подключения: короткий понятный результат или исключение с причиной.</summary>
        public virtual async Task<string> Test()
        {
            await Poll();
            return Rows.Count > 0 ? $"Работает ✓ Записей: {Rows.Count}" : "Работает ✓";
        }

        public virtual Task Poll() => Task.CompletedTask;
        public virtual Task Send(HubEvent e) => Task.CompletedTask;
        public virtual Task NowPlaying(Track t) => Task.CompletedTask;
        public virtual Task Scrobble(Track t) => Task.CompletedTask;

        protected static HttpClient Http => Integrations.Http;
        protected static string Esc(string s) => Uri.EscapeDataString(s ?? "");
        protected void Notify(string title, string sub, string url = null, bool important = false) => Integrations.Notify(this, title, sub, url, important);
    }

    public record Track(string Title, string Artist, string Album, double Duration, DateTime StartedUtc);

    /// <summary>Все интеграции: настройки (секреты — DPAPI), опрос по расписанию, пересылка событий, скробблинг.</summary>
    public static class Integrations
    {
        public static readonly HttpClient Http = MakeHttp();

        static HttpClient MakeHttp()
        {
            var h = new HttpClient(new SocketsHttpHandler { AutomaticDecompression = System.Net.DecompressionMethods.All, PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
            { Timeout = TimeSpan.FromSeconds(15) };
            h.DefaultRequestHeaders.UserAgent.ParseAdd($"NIsland/{AppInfo.Version} (+https://github.com/Neizvestnyi22/N-Island)");
            return h;
        }

        public static readonly List<Integration> All = new()
        {
            new TelegramHub(), new NtfyHub(), new DiscordHub(), new WebhookHub(), new HomeAssistantHub(), new GitHubHub(), new TodoistHub(),
            new CalendarHub(), new RssHub(), new ListenBrainzHub(), new LastFmHub(), new RatesHub(), new AlertsHub(), new NovaPoshtaHub(), new UptimeHub(),
            new MailHub(), new MonobankHub(), new SlackHub(), new GotifyHub(), new TogglHub(), new WakaTimeHub(),
            new PushoverHub(), new PushbulletHub(), new MatrixHub(), new NotionHub(), new TrelloHub(), new LinearHub(), new JiraHub(), new GitLabHub(),
            new TwitchHub(), new SteamHub(), new MastodonHub(), new BlueskyHub(), new HackerNewsHub(), new HueHub(), new StocksHub(),
        };

        public static readonly (string Key, string Name)[] EventKinds =
        {
            ("timer", "Таймер и помодоро"), ("reminder", "Напоминания и календарь"), ("call", "Звонки"), ("notify", "Уведомления приложений"),
            ("track", "Сейчас играет"), ("vpn", "VPN"), ("battery", "Батарея и устройства"), ("alert", "Важное от сервисов"),
            ("net", "Интернет пропал или появился"), ("parcel", "Посылки"), ("mail", "Новые письма"), ("money", "Деньги (Монобанк)"),
        };

        public static event Action Changed;
        public static void RaiseChanged() => Changed?.Invoke();

        public static IntegrationConfig Cfg(string key)
        {
            var d = Store.Settings.Integrations ??= new();
            if (!d.TryGetValue(key, out var c) || c == null) d[key] = c = new IntegrationConfig();
            c.Values ??= new();
            c.State ??= new();
            return c;
        }

        public static Integration Get(string key) => All.FirstOrDefault(i => i.Key == key);

        public static string Value(Integration it, string key)
        {
            var f = it.Fields.FirstOrDefault(x => x.Key == key);
            if (!it.Cfg.Values.TryGetValue(key, out var v) || v == null) return f?.Default ?? "";
            return f?.Kind == "secret" ? Secret.Unprotect(v) ?? "" : v;
        }

        public static void SetValue(Integration it, string key, string value)
        {
            var f = it.Fields.FirstOrDefault(x => x.Key == key);
            value = value?.Trim() ?? "";
            it.Cfg.Values[key] = f?.Kind == "secret" && value != "" ? Secret.Protect(value) : value;
            it.NextPoll = DateTime.MinValue;
            it.Fails = 0;
            Store.SaveSettings();
        }

        public static void SetEnabled(Integration it, bool on)
        {
            it.Cfg.Enabled = on;
            it.NextPoll = DateTime.MinValue;
            it.Fails = 0;
            if (!on) { it.Rows.Clear(); it.Status = ""; }
            Store.SaveSettings();
            Changed?.Invoke();
        }

        public static IList<string> Events(Integration it) => it.Cfg.Events ?? it.DefaultSend.ToList();

        public static void SetEvent(Integration it, string kind, bool on)
        {
            var list = Events(it).ToList();
            list.Remove(kind);
            if (on) list.Add(kind);
            it.Cfg.Events = list;
            Store.SaveSettings();
        }

        public static IEnumerable<Integration> Active => All.Where(i => i.Enabled && i.Ready);

        // ------------------------------------------------------------------
        //  Опрос по расписанию
        // ------------------------------------------------------------------

        static DispatcherTimer _timer;

        public static void Start()
        {
            if (_timer != null) return;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _timer.Tick += (_, __) => Tick();
            _timer.Start();
            MediaService.I.TrackChanged += OnTrack;
            MediaService.I.Changed += OnTrackMaybeStarted;
            ClipboardService.I.Copied += c => NovaPoshtaHub.OnCopied(c?.Text);
            // интернет пропал / появился — можно переслать на телефон (сообщение уйдёт, когда связь вернётся)
            System.Net.NetworkInformation.NetworkChange.NetworkAvailabilityChanged += (_, e) =>
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() => OnNetwork(e.IsAvailable)));
        }

        static DateTime _netLostAt;

        static void OnNetwork(bool up)
        {
            if (!up) { _netLostAt = DateTime.Now; return; }
            if (_netLostAt == default) return;
            var off = DateTime.Now - _netLostAt;
            _netLostAt = default;
            if (off.TotalSeconds < 20) return;   // короткие переподключения Wi-Fi не считаем
            // пока связи не было, сообщение всё равно не дошло бы — отправляем одно, когда она вернулась
            Publish("net", "Интернет снова есть", $"Связи не было {(off.TotalMinutes < 1 ? (int)off.TotalSeconds + " с" : (int)off.TotalMinutes + " мин")} (с {DateTime.Now - off:HH:mm})");
        }

        static void Tick()
        {
            var now = DateTime.Now;
            foreach (var it in Active.Where(i => i.PollSeconds > 0 && now >= i.NextPoll).ToList())
            {
                it.NextPoll = now.AddSeconds(it.PollSeconds);
                _ = PollOne(it);
            }
            ScrobbleTick();
        }

        public static async Task PollOne(Integration it)
        {
            try
            {
                await it.Poll();
                it.Ok = true;
                it.Fails = 0;
                if (string.IsNullOrEmpty(it.Status) || it.Status.StartsWith("Ошибка")) it.Status = "Обновлено " + DateTime.Now.ToString("HH:mm");
            }
            catch (Exception e)
            {
                it.Ok = false;
                it.Fails++;
                it.Status = "Ошибка: " + Reason(e);
                // не долбим сервис, который не отвечает: 2, 4, 8… минут, но не реже раза в полчаса
                it.NextPoll = DateTime.Now.AddSeconds(Math.Min(1800, it.PollSeconds * Math.Pow(2, Math.Min(it.Fails, 5))));
                if (it.Fails == 1) Log.Info($"hub {it.Key}: {e.Message}");
            }
            Changed?.Invoke();
        }

        /// <summary>Понятная причина ошибки без технического мусора.</summary>
        public static string Reason(Exception e)
        {
            if (e is HttpRequestException h && h.StatusCode is { } code)
                return (int)code switch
                {
                    401 or 403 => "неверный ключ или токен (" + (int)code + ")",
                    404 => "адрес не найден (404)",
                    429 => "слишком много запросов, попробую позже",
                    >= 500 => "сервис временно недоступен (" + (int)code + ")",
                    _ => "ответ " + (int)code,
                };
            if (e is TaskCanceledException) return "нет ответа (тайм-аут)";
            if (e is HttpRequestException) return "нет связи с сервером";
            return e.Message.Length > 160 ? e.Message[..160] : e.Message;
        }

        // ------------------------------------------------------------------
        //  События острова → наружу
        // ------------------------------------------------------------------

        static string _lastSig;
        static DateTime _lastAt;

        /// <summary>Вид всплывашки острова → вид события для пересылки (null — не пересылаем).</summary>
        public static string KindOf(string activityKind) => activityKind switch
        {
            "timer" or "pomodoro" => "timer",
            "event" or "water" or "eye" or "stand" or "prayer" => "reminder",
            "incall" or "callstart" => "call",
            "notif" or "notifkey" => "notify",
            "vpn" => "vpn",
            "lowbat" or "battery" or "usb" => "battery",
            "alert" => "alert",
            _ => null,
        };

        public static void Publish(string kind, string title, string text, Integration from = null)
        {
            if (kind == null) return;
            var sig = kind + "|" + title + "|" + text;
            if (sig == _lastSig && (DateTime.Now - _lastAt).TotalSeconds < 10) return;
            _lastSig = sig;
            _lastAt = DateTime.Now;
            var e = new HubEvent(kind, title ?? "", text ?? "");
            foreach (var it in Active.Where(i => i.CanSend != null && i != from && Events(i).Contains(kind)).ToList())
            {
                var target = it;
                _ = Task.Run(async () =>
                {
                    try { await target.Send(e); }
                    catch (Exception ex) { target.Status = "Не отправлено: " + Reason(ex); Log.Info($"hub send {target.Key}: {ex.Message}"); }
                });
            }
        }

        /// <summary>Сообщение от сервиса — во всплывашку острова (и, если важное, в пересылку).</summary>
        public static void Notify(Integration it, string title, string sub, string url, bool important)
        {
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (important) Sound.Play(SoundEvent.Alert);
                App.Island?.ShowActivity(new Activity
                {
                    Kind = important ? "alert" : "hub", Glyph = it.Glyph, Tint = ColorUtil.Parse(it.Color, System.Windows.Media.Colors.White),
                    Title = title, Sub = sub, Seconds = important ? 12 : 6, Wide = true,
                    OnClick = url != null ? () => SystemService.Run(url) : () => App.Island.Expand("hub"),
                });
                NotificationLog.Add(new Toast { App = it.Name, Title = title, Body = sub });
                if (important) Publish("alert", title, sub, it);
            }));
        }

        // ------------------------------------------------------------------
        //  Скробблинг (Last.fm, ListenBrainz): «сейчас играет» сразу, прослушивание — после половины трека или 4 минут
        // ------------------------------------------------------------------

        static Track _cur;
        static double _played;
        static bool _scrobbled;
        static DateTime _lastTick = DateTime.Now;

        static void OnTrackMaybeStarted()
        {
            // первый трек после запуска приходит без TrackChanged
            if (_cur == null && MediaService.I.HasSession && MediaService.I.Title != "") OnTrack();
        }

        static void OnTrack()
        {
            var m = MediaService.I;
            if (string.IsNullOrEmpty(m.Title)) return;
            var (title, artist) = LyricsService.CleanForScrobble(m.Title, m.Artist, m.FromBrowser);
            var t = new Track(title, artist, m.Album, m.Duration.TotalSeconds, DateTime.UtcNow);
            if (_cur != null && _cur.Title == t.Title && _cur.Artist == t.Artist) return;
            _cur = t;
            _played = 0;
            _scrobbled = false;
            if (artist == "") return;
            Publish("track", artist + " — " + title, MediaService.I.AppName);
            foreach (var it in Active.Where(i => i.Scrobbler && (m.FromBrowser ? i.Flag("browsers") : true)).ToList())
            {
                var target = it;
                _ = Task.Run(async () => { try { await target.NowPlaying(t); } catch (Exception e) { target.Status = "Ошибка: " + Reason(e); } });
            }
        }

        static void ScrobbleTick()
        {
            var now = DateTime.Now;
            double dt = (now - _lastTick).TotalSeconds;
            _lastTick = now;
            var m = MediaService.I;
            if (_cur == null || _scrobbled || !m.IsPlaying || dt > 30) return;
            _played += dt;
            double dur = m.Duration.TotalSeconds > 1 ? m.Duration.TotalSeconds : _cur.Duration;
            if (dur > 0 && dur < 30) return;   // меньше 30 с — по правилам Last.fm не скробблим
            double need = dur > 0 ? Math.Min(dur / 2, 240) : 240;
            if (_played < need || _cur.Artist == "") return;
            _scrobbled = true;
            var t = _cur with { Duration = dur };
            foreach (var it in Active.Where(i => i.Scrobbler && (m.FromBrowser ? i.Flag("browsers") : true)).ToList())
            {
                var target = it;
                _ = Task.Run(async () =>
                {
                    try { await target.Scrobble(t); target.Status = "Отправлено: " + t.Artist + " — " + t.Title; target.Ok = true; }
                    catch (Exception e) { target.Status = "Ошибка: " + Reason(e); target.Ok = false; }
                    System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() => Changed?.Invoke()));
                });
            }
        }

        // ------------------------------------------------------------------
        //  Помощники для сервисов
        // ------------------------------------------------------------------

        internal static async Task<JsonDocument> Json(HttpRequestMessage req)
        {
            using var resp = await Http.SendAsync(req);
            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode) throw new HttpRequestException(Snip(body), null, resp.StatusCode);
            return JsonDocument.Parse(body.Length == 0 ? "{}" : body);
        }

        internal static Task<JsonDocument> GetJson(string url, Action<HttpRequestMessage> setup = null)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            setup?.Invoke(req);
            return Json(req);
        }

        internal static Task<JsonDocument> PostJson(string url, object body, Action<HttpRequestMessage> setup = null)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
            setup?.Invoke(req);
            return Json(req);
        }

        internal static string Snip(string s)
        {
            s = System.Text.RegularExpressions.Regex.Replace(s ?? "", @"\s+", " ").Trim();
            return s.Length > 160 ? s[..160] + "…" : s;
        }

        internal static string S(JsonElement e, string k) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(k, out var v)
            ? v.ValueKind == JsonValueKind.String ? v.GetString() : v.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False ? v.ToString() : "" : "";

        internal static string Md5(string s) => Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();
    }

    // =====================================================================
    //  Пересылка: Telegram, ntfy, Discord, вебхук
    // =====================================================================

    public class NtfyHub : Integration
    {
        public override string Key => "ntfy";
        public override string Name => "ntfy (пуши с телефона)";
        public override string Glyph => "";
        public override string Color => "#338574";
        public override string Desc => "Уведомления с телефона, скриптов и серверов приходят в остров; события острова уходят тебе на телефон. Без регистрации";
        public override string Help => "Поставь приложение ntfy на телефон и подпишись на ту же тему. Тему придумай длинную и случайную — её может прочитать любой, кто её знает";
        public override string HelpUrl => "https://ntfy.sh/";
        public override HubField[] Fields => new[]
        {
            new HubField("topic", "Тема", "например nisland-7f3k9q2x"),
            new HubField("server", "Сервер", "https://ntfy.sh", Required: false, Default: "https://ntfy.sh"),
            new HubField("token", "Токен доступа", "для закрытого сервера", "secret", Required: false),
        };
        public override string[] CanSend => Integrations.EventKinds.Select(e => e.Key).ToArray();
        public override int PollSeconds => 30;

        string Url => (string.IsNullOrWhiteSpace(V("server")) ? "https://ntfy.sh" : V("server").TrimEnd('/')) + "/" + Esc(V("topic"));
        void Auth(HttpRequestMessage r) { if (V("token") != "") r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", V("token")); }

        public override async Task<string> Test()
        {
            await Send(new HubEvent("test", "N-Island подключён ✓", "Проверка связи с островом"));
            await Poll();
            return "Работает ✓ Тестовое уведомление отправлено в тему «" + V("topic") + "»";
        }

        public override async Task Send(HubEvent e)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, Url) { Content = new StringContent(e.Text == "" ? e.Title : e.Text, Encoding.UTF8) };
            req.Headers.TryAddWithoutValidation("Title", Uri.EscapeDataString(e.Title).Length == e.Title.Length ? e.Title : "=?UTF-8?B?" + Convert.ToBase64String(Encoding.UTF8.GetBytes(e.Title)) + "?=");
            req.Headers.TryAddWithoutValidation("Tags", e.Kind switch { "timer" => "alarm_clock", "call" => "telephone_receiver", "alert" => "warning", "vpn" => "lock", "track" => "musical_note", _ => "bell" });
            req.Headers.TryAddWithoutValidation("X-NIsland", "1");   // свои же сообщения при опросе не показываем
            Auth(req);
            using var resp = await Http.SendAsync(req);
            resp.EnsureSuccessStatusCode();
            Status = "Отправлено " + DateTime.Now.ToString("HH:mm");
        }

        public override async Task Poll()
        {
            var since = St("since") ?? "10m";
            var req = new HttpRequestMessage(HttpMethod.Get, Url + "/json?poll=1&since=" + Esc(since));
            Auth(req);
            using var resp = await Http.SendAsync(req);
            resp.EnsureSuccessStatusCode();
            var body = await resp.Content.ReadAsStringAsync();
            string last = since;
            foreach (var line in body.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                using var doc = JsonDocument.Parse(line);
                var m = doc.RootElement;
                if (Integrations.S(m, "event") != "message") continue;
                last = Integrations.S(m, "id");
                var title = Integrations.S(m, "title");
                var msg = Integrations.S(m, "message");
                if (msg.StartsWith("Сюда будут приходить") || title == "N-Island подключён ✓") continue;
                if (since == "10m") continue;   // первый опрос — только запоминаем, старое не показываем
                Rows.Insert(0, new HubRow { Title = title != "" ? title : msg, Sub = (title != "" ? msg + " · " : "") + DateTime.Now.ToString("HH:mm"), Url = Integrations.S(m, "click") is { Length: > 0 } c ? c : null });
                if (Rows.Count > 8) Rows.RemoveAt(Rows.Count - 1);
                Notify(title != "" ? title : "ntfy", msg, Integrations.S(m, "click") is { Length: > 0 } c2 ? c2 : null, Integrations.S(m, "priority") is "5" or "4");
            }
            if (last != since) SetSt("since", last);
            else if (since == "10m") SetSt("since", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());
        }
    }

    public class DiscordHub : Integration
    {
        public override string Key => "discord";
        public override string Name => "Discord";
        public override string Glyph => "";
        public override string Color => "#5865F2";
        public override string Desc => "Отправляет события острова в канал Discord через вебхук";
        public override string Help => "Настройки канала → Интеграции → Вебхуки → Новый вебхук → Копировать URL";
        public override HubField[] Fields => new[] { new HubField("url", "URL вебхука", "https://discord.com/api/webhooks/…", "secret") };
        public override string[] CanSend => Integrations.EventKinds.Select(e => e.Key).ToArray();

        public override async Task<string> Test()
        {
            await Send(new HubEvent("test", "N-Island подключён ✓", "Сюда будут приходить события острова"));
            return "Работает ✓ Сообщение отправлено в канал";
        }

        public override async Task Send(HubEvent e)
        {
            if (!V("url").StartsWith("https://")) throw new InvalidOperationException("Нужен адрес вебхука, начинающийся с https://");
            using var _ = await Integrations.PostJson(V("url"), new { username = "N-Island", content = $"**{e.Title}**" + (e.Text != "" ? "\n" + e.Text : "") });
            Status = "Отправлено " + DateTime.Now.ToString("HH:mm");
        }
    }

    public class WebhookHub : Integration
    {
        public override string Key => "webhook";
        public override string Name => "Вебхук (IFTTT, Zapier, Make, n8n)";
        public override string Glyph => "";
        public override string Color => "#FF9F0A";
        public override string Desc => "POST-запрос с JSON {event, title, text, time} на любой адрес — для автоматизаций и умного дома";
        public override HubField[] Fields => new[]
        {
            new HubField("url", "Адрес", "https://…", "secret"),
            new HubField("header", "Заголовок авторизации", "Bearer …  (необязательно)", "secret", Required: false),
        };
        public override string[] CanSend => Integrations.EventKinds.Select(e => e.Key).ToArray();

        public override async Task<string> Test()
        {
            await Send(new HubEvent("test", "N-Island подключён", "Проверка вебхука"));
            return "Работает ✓ Сервер принял запрос";
        }

        public override async Task Send(HubEvent e)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, V("url"))
            {
                Content = new StringContent(JsonSerializer.Serialize(new { @event = e.Kind, title = e.Title, text = e.Text, time = DateTimeOffset.Now.ToString("o"), app = "N-Island" }), Encoding.UTF8, "application/json"),
            };
            if (V("header") != "") req.Headers.TryAddWithoutValidation("Authorization", V("header"));
            using var resp = await Http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) throw new HttpRequestException(Integrations.Snip(await resp.Content.ReadAsStringAsync()), null, resp.StatusCode);
            Status = "Отправлено " + DateTime.Now.ToString("HH:mm");
        }
    }

    // =====================================================================
    //  Данные в острове: Home Assistant, GitHub, Todoist, календарь, RSS
    // =====================================================================

    public class HomeAssistantHub : Integration
    {
        public override string Key => "ha";
        public override string Name => "Home Assistant";
        public override string Glyph => "";
        public override string Color => "#18BCF2";
        public override string Desc => "Умный дом в острове: состояние датчиков, свет, розетки и сцены — переключаются одним нажатием";
        public override string Help => "Профиль → Безопасность → Долгосрочные токены доступа → Создать. Сущности — через запятую: light.kitchen, switch.fan, sensor.temperature, scene.movie";
        public override HubField[] Fields => new[]
        {
            new HubField("url", "Адрес", "http://homeassistant.local:8123"),
            new HubField("token", "Токен", "долгосрочный токен", "secret"),
            new HubField("entities", "Сущности", "light.kitchen, switch.fan, sensor.temp", "multi"),
        };
        public override int PollSeconds => 30;

        string Base => V("url").TrimEnd('/');
        void Auth(HttpRequestMessage r) => r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", V("token"));

        public override async Task<string> Test()
        {
            using var doc = await Integrations.GetJson(Base + "/api/", Auth);
            await Poll();
            return "Работает ✓ " + Integrations.S(doc.RootElement, "message") + $" · сущностей: {Rows.Count}";
        }

        public override async Task Poll()
        {
            var rows = new List<HubRow>();
            foreach (var id in Lines("entities").Take(12))
            {
                var entity = id;
                using var doc = await Integrations.GetJson(Base + "/api/states/" + Esc(entity), Auth);
                var r = doc.RootElement;
                var state = Integrations.S(r, "state");
                var attrs = r.TryGetProperty("attributes", out var a) ? a : default;
                var name = Integrations.S(attrs, "friendly_name") is { Length: > 0 } fn ? fn : entity;
                var unit = Integrations.S(attrs, "unit_of_measurement");
                string domain = entity.Split('.')[0];
                var row = new HubRow { Title = name, Sub = state + (unit != "" ? " " + unit : "") };
                if (domain is "light" or "switch" or "fan" or "input_boolean" or "automation" or "media_player" or "climate" or "cover")
                {
                    row.On = state is "on" or "open" or "playing" or "heat" or "cool";
                    row.Toggle = async on => { await Service("homeassistant", on ? "turn_on" : "turn_off", entity); await Poll(); return null; };
                }
                else if (domain is "scene" or "script" or "button")
                {
                    row.ActionText = "Запустить";
                    row.Action = async () => { await Service(domain == "button" ? "button" : domain, domain == "button" ? "press" : "turn_on", entity); return "Запущено"; };
                }
                rows.Add(row);
            }
            Rows = rows;
            Status = "Обновлено " + DateTime.Now.ToString("HH:mm");
        }

        async Task Service(string domain, string service, string entity)
        {
            using var _ = await Integrations.PostJson($"{Base}/api/services/{domain}/{service}", new { entity_id = entity }, Auth);
        }
    }

    public class GitHubHub : Integration
    {
        public override string Key => "github";
        public override string Name => "GitHub";
        public override string Glyph => "";
        public override string Color => "#8B949E";
        public override string Desc => "Непрочитанные уведомления GitHub: ревью, issues, упоминания — с всплывашкой о новых";
        public override string Help => "Settings → Developer settings → Personal access tokens → Tokens (classic), галочка «notifications»";
        public override string HelpUrl => "https://github.com/settings/tokens/new?scopes=notifications&description=N-Island";
        public override HubField[] Fields => new[] { new HubField("token", "Токен", "ghp_…", "secret") };
        public override int PollSeconds => 120;

        void Auth(HttpRequestMessage r) { r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", V("token")); r.Headers.Accept.ParseAdd("application/vnd.github+json"); }

        public override async Task<string> Test()
        {
            using var me = await Integrations.GetJson("https://api.github.com/user", Auth);
            await Poll();
            return $"Работает ✓ {Integrations.S(me.RootElement, "login")} · непрочитанных: {Rows.Count}";
        }

        public override async Task Poll()
        {
            using var doc = await Integrations.GetJson("https://api.github.com/notifications?per_page=20", Auth);
            var rows = new List<HubRow>();
            var seen = (St("seen") ?? "").Split(',').ToHashSet();
            var fresh = new List<string>();
            foreach (var n in doc.RootElement.EnumerateArray())
            {
                var id = Integrations.S(n, "id");
                var subj = n.GetProperty("subject");
                var repo = Integrations.S(n.GetProperty("repository"), "full_name");
                var title = Integrations.S(subj, "title");
                var api = Integrations.S(subj, "url");
                var html = api.Replace("https://api.github.com/repos/", "https://github.com/").Replace("/pulls/", "/pull/");
                rows.Add(new HubRow { Title = title, Sub = repo + " · " + Integrations.S(n, "reason"), Url = html != "" ? html : "https://github.com/notifications" });
                fresh.Add(id);
                if (St("seen") != null && !seen.Contains(id)) Notify("GitHub · " + repo, title, html != "" ? html : "https://github.com/notifications");
            }
            Rows = rows;
            SetSt("seen", string.Join(",", fresh));
            Status = rows.Count == 0 ? "Всё прочитано ✓" : "Непрочитанных: " + rows.Count;
        }
    }

    public class TodoistHub : Integration
    {
        public override string Key => "todoist";
        public override string Name => "Todoist";
        public override string Glyph => "";
        public override string Color => "#E44332";
        public override string Desc => "Задачи на сегодня и просроченные — отмечай выполненными прямо в острове";
        public override string Help => "Настройки Todoist → Интеграции → Для разработчиков → API-токен";
        public override string HelpUrl => "https://app.todoist.com/app/settings/integrations/developer";
        public override HubField[] Fields => new[] { new HubField("token", "API-токен", "", "secret") };
        public override int PollSeconds => 300;

        void Auth(HttpRequestMessage r) => r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", V("token"));

        public override async Task Poll()
        {
            using var doc = await Integrations.GetJson("https://api.todoist.com/api/v1/tasks/filter?query=" + Esc("today | overdue") + "&limit=30", Auth);
            var arr = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement : doc.RootElement.GetProperty("results");
            var rows = new List<HubRow>();
            foreach (var t in arr.EnumerateArray())
            {
                var id = Integrations.S(t, "id");
                var due = t.TryGetProperty("due", out var d) && d.ValueKind == JsonValueKind.Object ? Integrations.S(d, "string") : "";
                rows.Add(new HubRow
                {
                    Title = Integrations.S(t, "content"), Sub = due, Url = "https://app.todoist.com/app/task/" + id,
                    ActionText = "Готово", Action = async () =>
                    {
                        var req = new HttpRequestMessage(HttpMethod.Post, "https://api.todoist.com/api/v1/tasks/" + id + "/close");
                        Auth(req);
                        using var resp = await Http.SendAsync(req);
                        resp.EnsureSuccessStatusCode();
                        await Poll();
                        return "Задача выполнена ✓";
                    },
                });
            }
            Rows = rows;
            Status = rows.Count == 0 ? "На сегодня всё ✓" : "Задач на сегодня: " + rows.Count;
        }
    }

    public class CalendarHub : Integration
    {
        public override string Key => "ics";
        public override string Name => "Календарь (Google, Outlook, iCloud)";
        public override string Glyph => "";
        public override string Color => "#FF453A";
        public override string Desc => "События из твоего календаря по ссылке iCal: ближайшие встречи в острове и напоминание перед началом";
        public override string Help => "Google: Настройки календаря → «Секретный адрес в формате iCal». Outlook: Параметры → Общий доступ → опубликовать ICS. Можно несколько ссылок";
        public override HubField[] Fields => new[] { new HubField("urls", "Ссылки iCal (.ics)", "https://calendar.google.com/…/basic.ics", "secret") };
        public override int PollSeconds => 60;

        List<(DateTime Start, DateTime End, string Title, string Where, bool AllDay)> _events = new();
        DateTime _loadedAt;
        readonly HashSet<string> _reminded = new();

        public override async Task<string> Test()
        {
            _loadedAt = DateTime.MinValue;
            await Poll();
            return $"Работает ✓ Событий на 2 недели: {_events.Count}";
        }

        public override async Task Poll()
        {
            // календарь скачиваем раз в 15 минут, а напоминания проверяем каждую минуту
            if ((DateTime.Now - _loadedAt).TotalMinutes >= 15)
            {
                var all = new List<(DateTime, DateTime, string, string, bool)>();
                foreach (var url in Lines("urls"))
                {
                    var u = url.StartsWith("webcal://") ? "https://" + url[9..] : url;
                    all.AddRange(Ics.Parse(await Http.GetStringAsync(u), DateTime.Now.AddDays(-1), DateTime.Now.AddDays(14)));
                }
                _events = all.OrderBy(e => e.Item1).ToList();
                _loadedAt = DateTime.Now;
            }
            var now = DateTime.Now;
            int before = Math.Clamp(Store.Settings.CalRemindBefore, 0, 120);
            foreach (var e in _events.Where(e => !e.AllDay && e.Start > now && e.Start <= now.AddMinutes(before + 0.5)))
            {
                var sig = e.Start.ToString("o") + e.Title;
                if (!_reminded.Add(sig)) continue;
                Notify(e.Title, (before == 0 ? "Сейчас" : $"Через {Math.Max(1, (int)Math.Round((e.Start - now).TotalMinutes))} мин") + (e.Where != "" ? " · " + e.Where : ""), null, true);
            }
            Rows = _events.Where(e => e.End >= now).Take(6).Select(e => new HubRow
            {
                Title = e.Title,
                Sub = (e.AllDay ? Fmt.Date(e.Start) + " · весь день" : (e.Start.Date == now.Date ? "сегодня" : e.Start.Date == now.Date.AddDays(1) ? "завтра" : Fmt.Date(e.Start)) + " " + e.Start.ToString("HH:mm"))
                      + (e.Where != "" ? " · " + e.Where : ""),
            }).ToList();
            Status = Rows.Count == 0 ? "Ближайших событий нет" : "Ближайшее: " + Rows[0].Sub;
        }
    }

    public class RssHub : Integration
    {
        public override string Key => "rss";
        public override string Name => "Новости (RSS)";
        public override string Glyph => "";
        public override string Color => "#FF9500";
        public override string Desc => "Свежие заголовки из любых сайтов и каналов с RSS или Atom — в острове, о новых сообщает всплывашка";
        public override string Help => "Адреса лент по одному в строке. YouTube-канал: https://www.youtube.com/feeds/videos.xml?channel_id=…";
        public override HubField[] Fields => new[]
        {
            new HubField("feeds", "Ленты", "https://www.pravda.com.ua/rss/", "multi"),
            new HubField("notify", "Сообщать о новых", "", "bool", Required: false, Default: "1"),
        };
        public override int PollSeconds => 900;

        public override async Task Poll()
        {
            var items = new List<(DateTime At, string Title, string Link, string Feed)>();
            foreach (var url in Lines("feeds").Take(10))
            {
                try { items.AddRange(Rss.Parse(await Http.GetStringAsync(url))); }
                catch (Exception e) { Status = "Лента не открылась: " + Integrations.Reason(e); }
            }
            items = items.OrderByDescending(i => i.At).ToList();
            var seen = (St("seen") ?? "").Split('\n').ToHashSet();
            var fresh = items.Where(i => !seen.Contains(i.Link)).ToList();
            if (St("seen") != null && fresh.Count > 0 && Flag("notify"))
                Notify(fresh.Count == 1 ? fresh[0].Feed : $"Новых записей: {fresh.Count}", fresh[0].Title, fresh[0].Link);
            SetSt("seen", string.Join("\n", items.Take(80).Select(i => i.Link)));
            Rows = items.Take(10).Select(i => new HubRow { Title = i.Title, Sub = i.Feed + " · " + Ago(i.At), Url = i.Link }).ToList();
            if (!Status.StartsWith("Лента")) Status = "Обновлено " + DateTime.Now.ToString("HH:mm");
        }

        static string Ago(DateTime t)
        {
            var d = DateTime.Now - t;
            return d.TotalMinutes < 60 ? $"{Math.Max(1, (int)d.TotalMinutes)} мин назад" : d.TotalHours < 24 ? $"{(int)d.TotalHours} ч назад" : t.ToString("d MMM", Fmt.Ru);
        }
    }

    // =====================================================================
    //  Музыка: ListenBrainz и Last.fm
    // =====================================================================

    public class ListenBrainzHub : Integration
    {
        public override string Key => "listenbrainz";
        public override string Name => "ListenBrainz";
        public override string Glyph => "";
        public override string Color => "#EB743B";
        public override string Desc => "Скробблинг: всё, что ты слушаешь в любом приложении, сохраняется в твою историю ListenBrainz";
        public override string HelpUrl => "https://listenbrainz.org/settings/";
        public override string Help => "listenbrainz.org → Settings → User token";
        public override HubField[] Fields => new[]
        {
            new HubField("token", "Токен", "", "secret"),
            new HubField("browsers", "Музыка из браузера", "YouTube, VK, Яндекс в браузере", "bool", Required: false, Default: "1"),
        };
        public override bool Scrobbler => true;

        void Auth(HttpRequestMessage r) => r.Headers.Authorization = new AuthenticationHeaderValue("Token", V("token"));

        public override async Task<string> Test()
        {
            using var doc = await Integrations.GetJson("https://api.listenbrainz.org/1/validate-token", Auth);
            if (doc.RootElement.TryGetProperty("valid", out var v) && v.ValueKind == JsonValueKind.False) throw new InvalidOperationException("токен неверный");
            return "Работает ✓ Пользователь " + Integrations.S(doc.RootElement, "user_name");
        }

        object Meta(Track t) => new { artist_name = t.Artist, track_name = t.Title, release_name = string.IsNullOrEmpty(t.Album) ? null : t.Album,
            additional_info = new { media_player = "N-Island", duration_ms = t.Duration > 0 ? (int?)(t.Duration * 1000) : null } };

        public override async Task NowPlaying(Track t)
        {
            using var _ = await Integrations.PostJson("https://api.listenbrainz.org/1/submit-listens", new { listen_type = "playing_now", payload = new[] { new { track_metadata = Meta(t) } } }, Auth);
            Status = "Сейчас играет: " + t.Artist + " — " + t.Title;
        }

        public override async Task Scrobble(Track t)
        {
            using var _ = await Integrations.PostJson("https://api.listenbrainz.org/1/submit-listens",
                new { listen_type = "single", payload = new[] { new { listened_at = new DateTimeOffset(t.StartedUtc).ToUnixTimeSeconds(), track_metadata = Meta(t) } } }, Auth);
        }
    }

    public class LastFmHub : Integration
    {
        public override string Key => "lastfm";
        public override string Name => "Last.fm";
        public override string Glyph => "";
        public override string Color => "#D51007";
        public override string Desc => "Скробблинг в Last.fm из любого плеера и сайта: «Сейчас играет» и история прослушиваний";
        public override string Help => "Создай API-аккаунт на last.fm/api/account/create (бесплатно), вставь API key и Shared secret, логин и пароль. Пароль нужен один раз — хранится только ключ сессии";
        public override string HelpUrl => "https://www.last.fm/api/account/create";
        public override HubField[] Fields => new[]
        {
            new HubField("key", "API key"),
            new HubField("secret", "Shared secret", "", "secret"),
            new HubField("user", "Логин Last.fm"),
            new HubField("password", "Пароль", "нужен только для входа", "secret", Required: false),
            new HubField("browsers", "Музыка из браузера", "YouTube, VK, Яндекс в браузере", "bool", Required: false, Default: "1"),
        };
        public override bool Scrobbler => true;

        async Task<JsonDocument> Call(Dictionary<string, string> p)
        {
            p["api_key"] = V("key");
            var sig = Integrations.Md5(string.Concat(p.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => x.Key + x.Value)) + V("secret"));
            p["api_sig"] = sig;
            p["format"] = "json";
            var req = new HttpRequestMessage(HttpMethod.Post, "https://ws.audioscrobbler.com/2.0/") { Content = new FormUrlEncodedContent(p) };
            var doc = await Integrations.Json(req);
            if (doc.RootElement.TryGetProperty("error", out var err)) throw new InvalidOperationException(Integrations.S(doc.RootElement, "message") + " (" + err + ")");
            return doc;
        }

        async Task<string> Session()
        {
            if (St("sk") is { Length: > 0 } sk && St("skuser") == V("user")) return sk;
            if (V("password") == "") throw new InvalidOperationException("введи пароль Last.fm один раз и нажми «Проверить»");
            using var doc = await Call(new() { ["method"] = "auth.getMobileSession", ["username"] = V("user"), ["password"] = V("password") });
            var key = Integrations.S(doc.RootElement.GetProperty("session"), "key");
            SetSt("sk", key);
            SetSt("skuser", V("user"));
            Integrations.SetValue(this, "password", "");   // пароль больше не нужен — не храним
            return key;
        }

        public override async Task<string> Test()
        {
            await Session();
            return "Работает ✓ Вход выполнен, пароль удалён — хранится только ключ сессии";
        }

        public override async Task NowPlaying(Track t)
        {
            var p = new Dictionary<string, string> { ["method"] = "track.updateNowPlaying", ["artist"] = t.Artist, ["track"] = t.Title, ["sk"] = await Session() };
            if (!string.IsNullOrEmpty(t.Album)) p["album"] = t.Album;
            if (t.Duration > 0) p["duration"] = ((int)t.Duration).ToString(CultureInfo.InvariantCulture);
            using var _ = await Call(p);
            Status = "Сейчас играет: " + t.Artist + " — " + t.Title;
        }

        public override async Task Scrobble(Track t)
        {
            var p = new Dictionary<string, string>
            {
                ["method"] = "track.scrobble", ["artist"] = t.Artist, ["track"] = t.Title, ["sk"] = await Session(),
                ["timestamp"] = new DateTimeOffset(t.StartedUtc).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
            };
            if (!string.IsNullOrEmpty(t.Album)) p["album"] = t.Album;
            using var _ = await Call(p);
        }
    }

    // =====================================================================
    //  Полезное: курсы, воздушная тревога, Нова пошта, мониторинг сайтов
    // =====================================================================

    public class RatesHub : Integration
    {
        public override string Key => "rates";
        public override string Name => "Курсы валют и крипта";
        public override string Glyph => "";
        public override string Color => "#30D158";
        public override string Desc => "Курсы НБУ (для гривны) или open.er-api и цены CoinGecko в острове; всплывашка при резком движении крипты";
        public override HubField[] Fields => new[]
        {
            new HubField("base", "Валюта, в которой считать", "UAH", Required: false, Default: Currency.LocalCode),
            new HubField("cur", "Валюты", "USD, EUR, PLN", Required: false, Default: "USD, EUR"),
            new HubField("crypto", "Криптовалюты (id CoinGecko)", "bitcoin, ethereum", Required: false, Default: "bitcoin, ethereum"),
            new HubField("move", "Сообщать при изменении крипты за сутки, %", "0 — не сообщать", Required: false, Default: "7"),
        };
        public override int PollSeconds => 1800;

        public override async Task Poll()
        {
            var rows = new List<HubRow>();
            var bas = (V("base") == "" ? Currency.LocalCode : V("base")).ToUpperInvariant();
            var curs = Lines("cur").Select(c => c.ToUpperInvariant()).Where(c => c.Length == 3 && c != bas).ToList();
            if (curs.Count > 0)
            {
                if (bas == "UAH")
                {
                    using var doc = await Integrations.GetJson("https://bank.gov.ua/NBUStatService/v1/statdirectory/exchange?json");
                    foreach (var r in doc.RootElement.EnumerateArray())
                    {
                        var cc = Integrations.S(r, "cc");
                        if (!curs.Contains(cc)) continue;
                        rows.Add(new HubRow { Title = $"{cc} · {double.Parse(Integrations.S(r, "rate"), CultureInfo.InvariantCulture):0.00##} ₴", Sub = "НБУ на " + Integrations.S(r, "exchangedate") });
                    }
                }
                else
                {
                    using var doc = await Integrations.GetJson("https://open.er-api.com/v6/latest/" + bas);
                    var rates = doc.RootElement.GetProperty("rates");
                    foreach (var c in curs)
                        if (rates.TryGetProperty(c, out var v) && v.GetDouble() > 0) rows.Add(new HubRow { Title = $"{c} · {1 / v.GetDouble():0.00##} {bas}", Sub = "open.er-api.com" });
                }
            }
            var coins = Lines("crypto").Select(c => c.ToLowerInvariant()).ToList();
            if (coins.Count > 0)
            {
                using var doc = await Integrations.GetJson("https://api.coingecko.com/api/v3/simple/price?vs_currencies=usd&include_24hr_change=true&ids=" + Esc(string.Join(",", coins)));
                double.TryParse(V("move"), NumberStyles.Float, CultureInfo.InvariantCulture, out var move);
                foreach (var c in coins)
                {
                    if (!doc.RootElement.TryGetProperty(c, out var p)) continue;
                    double usd = p.GetProperty("usd").GetDouble();
                    double ch = p.TryGetProperty("usd_24h_change", out var chv) && chv.ValueKind == JsonValueKind.Number ? chv.GetDouble() : 0;
                    rows.Add(new HubRow { Title = $"{Fmt.Cap(c)} · ${usd.ToString(usd >= 100 ? "N0" : "N4", CultureInfo.GetCultureInfo("en-US"))}", Sub = $"{(ch >= 0 ? "▲" : "▼")} {Math.Abs(ch):0.0}% за сутки", Url = "https://www.coingecko.com/en/coins/" + c });
                    var flagKey = "moved:" + c;
                    if (move > 0 && Math.Abs(ch) >= move && St(flagKey) != DateTime.Today.ToString("yyyyMMdd"))
                    {
                        SetSt(flagKey, DateTime.Today.ToString("yyyyMMdd"));
                        Notify($"{Fmt.Cap(c)} {(ch >= 0 ? "вырос" : "упал")} на {Math.Abs(ch):0.0}%", $"${usd:N0} · за сутки", "https://www.coingecko.com/en/coins/" + c);
                    }
                }
            }
            Rows = rows;
            Status = "Обновлено " + DateTime.Now.ToString("HH:mm");
        }
    }

    public class UptimeHub : Integration
    {
        public override string Key => "uptime";
        public override string Name => "Мониторинг сайтов";
        public override string Glyph => "";
        public override string Color => "#64D2FF";
        public override string Desc => "Проверяет твои сайты и серверы каждые 5 минут: остров сообщает, если сайт упал или снова поднялся";
        public override HubField[] Fields => new[] { new HubField("urls", "Адреса", "https://example.com", "multi") };
        public override int PollSeconds => 300;

        public override async Task Poll()
        {
            var rows = new List<HubRow>();
            foreach (var url in Lines("urls").Take(15))
            {
                var u = url.StartsWith("http") ? url : "https://" + url;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                string state;
                bool up;
                try
                {
                    using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(10));
                    using var resp = await Http.GetAsync(u, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                    up = (int)resp.StatusCode < 500;
                    state = $"{(int)resp.StatusCode} · {sw.ElapsedMilliseconds} мс";
                }
                catch (Exception e) { up = false; state = Integrations.Reason(e); }
                rows.Add(new HubRow { Title = (up ? "● " : "○ ") + u.Replace("https://", "").Replace("http://", ""), Sub = state, Url = u });
                var key = "up:" + u;
                var prev = St(key);
                if (prev != null && prev != (up ? "1" : "0")) Notify(up ? "Сайт снова работает" : "Сайт недоступен", u + " · " + state, u, !up);
                if (prev != (up ? "1" : "0")) SetSt(key, up ? "1" : "0");
            }
            Rows = rows;
            Status = rows.All(r => r.Title.StartsWith("●")) ? "Все сайты работают ✓" : "Есть недоступные";
        }
    }
}
