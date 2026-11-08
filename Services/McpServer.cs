using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using NIsland.Core;

namespace NIsland.Services
{
    /// <summary>
    /// MCP-сервер N-Island: ИИ-агенты (Claude Code, Codex, Cursor, Claude Desktop…) подключаются к острову и управляют им —
    /// уведомления и прогресс задач в чёлке, музыка, заметки, напоминания, сообщения на телефон, настройка вида острова.
    /// Транспорт — Streamable HTTP на 127.0.0.1 (только этот ПК) с токеном; для агентов только со stdio — мост «NIsland.exe --mcp».
    /// </summary>
    public static class McpServer
    {
        public record Tool(string Name, string Title, string Desc, int Level, JsonObject Schema, Func<JsonElement, Task<object>> Run);
        public record Image(byte[] Png);
        public record CallLog(DateTime At, string Tool, bool Ok, string Note);

        public const string ProtocolVersion = "2025-06-18";
        static Settings S => Store.Settings;
        static TcpListener _listener;
        static CancellationTokenSource _cts;
        public static readonly ConcurrentQueue<CallLog> Log = new();
        public static event Action Changed;
        public static bool Running => _listener != null;
        public static string Error { get; private set; }

        public static string Token
        {
            get
            {
                if (string.IsNullOrEmpty(S.McpToken)) S.McpToken = NewToken();
                return S.McpToken;
            }
        }

        public static string NewToken() => "nis_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(18)).ToLowerInvariant();
        public static string Url => $"http://127.0.0.1:{S.McpPort}/mcp";
        static string InfoFile => Path.Combine(Store.Dir, "mcp.json");

        // ------------------------------------------------------------------
        //  Запуск и остановка
        // ------------------------------------------------------------------

        public static void Apply()
        {
            Stop();
            if (!S.McpOn) return;
            try
            {
                _listener = new TcpListener(IPAddress.Loopback, Math.Clamp(S.McpPort, 1024, 65535));
                _listener.Start();
                _cts = new CancellationTokenSource();
                var ct = _cts.Token;
                // мосту stdio нужно знать порт и токен
                if (!Store.ReadOnly) File.WriteAllText(InfoFile, JsonSerializer.Serialize(new { port = S.McpPort, token = Token }));
                _ = Task.Run(() => AcceptLoop(ct));
                Error = null;
            }
            catch (Exception e)
            {
                _listener = null;
                Error = e is SocketException se && se.SocketErrorCode == SocketError.AddressAlreadyInUse ? $"Порт {S.McpPort} занят другой программой — выбери другой" : e.Message;
                NIsland.Core.Log.Info("mcp: " + e.Message);
            }
            Changed?.Invoke();
        }

        public static void Stop()
        {
            try { _cts?.Cancel(); _listener?.Stop(); } catch { }
            _listener = null;
            Changed?.Invoke();
        }

        static async Task AcceptLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient c;
                try { c = await _listener.AcceptTcpClientAsync(ct); }
                catch { break; }
                _ = Task.Run(() => Serve(c, ct));
            }
        }

        // ------------------------------------------------------------------
        //  Минимальный HTTP/1.1 (без HttpListener — тому нужны права администратора на порт)
        // ------------------------------------------------------------------

        static async Task Serve(TcpClient client, CancellationToken ct)
        {
            using (client)
            {
                client.ReceiveTimeout = 30000;
                var stream = client.GetStream();
                try
                {
                    while (!ct.IsCancellationRequested)
                    {
                        var head = await ReadHead(stream, ct);
                        if (head == null) return;
                        var lines = head.Split("\r\n");
                        var req = lines[0].Split(' ');
                        if (req.Length < 2) return;
                        var headers = lines.Skip(1).Where(l => l.Contains(':')).GroupBy(l => l[..l.IndexOf(':')].Trim().ToLowerInvariant())
                                           .ToDictionary(g => g.Key, g => g.First()[(g.First().IndexOf(':') + 1)..].Trim());
                        int len = headers.TryGetValue("content-length", out var cl) && int.TryParse(cl, out var n) ? n : 0;
                        var body = len > 0 ? await ReadBody(stream, Math.Min(len, 4 * 1024 * 1024), ct) : "";
                        var (status, type, payload, extra) = await Route(req[0], req[1], headers, body);
                        var bytes = Encoding.UTF8.GetBytes(payload ?? "");
                        var sb = new StringBuilder($"HTTP/1.1 {status} {(status == 200 ? "OK" : status == 202 ? "Accepted" : status == 401 ? "Unauthorized" : status == 403 ? "Forbidden" : status == 405 ? "Method Not Allowed" : "Error")}\r\n");
                        sb.Append($"Content-Type: {type}\r\nContent-Length: {bytes.Length}\r\nCache-Control: no-store\r\n");
                        if (extra != null) sb.Append(extra);
                        bool keep = !(headers.TryGetValue("connection", out var conn) && conn.Equals("close", StringComparison.OrdinalIgnoreCase));
                        sb.Append(keep ? "Connection: keep-alive\r\n\r\n" : "Connection: close\r\n\r\n");
                        await stream.WriteAsync(Encoding.ASCII.GetBytes(sb.ToString()), ct);
                        if (bytes.Length > 0) await stream.WriteAsync(bytes, ct);
                        if (!keep) return;
                    }
                }
                catch { }
            }
        }

        static async Task<string> ReadHead(NetworkStream s, CancellationToken ct)
        {
            var buf = new List<byte>(1024);
            var one = new byte[1];
            while (buf.Count < 32768)
            {
                int r = await s.ReadAsync(one, ct);
                if (r == 0) return null;
                buf.Add(one[0]);
                int c = buf.Count;
                if (c >= 4 && buf[c - 4] == '\r' && buf[c - 3] == '\n' && buf[c - 2] == '\r' && buf[c - 1] == '\n') return Encoding.ASCII.GetString(buf.ToArray(), 0, c - 4);
            }
            return null;
        }

        static async Task<string> ReadBody(NetworkStream s, int len, CancellationToken ct)
        {
            var buf = new byte[len];
            int got = 0;
            while (got < len) { int r = await s.ReadAsync(buf.AsMemory(got, len - got), ct); if (r == 0) break; got += r; }
            return Encoding.UTF8.GetString(buf, 0, got);
        }

        static async Task<(int Status, string Type, string Body, string Extra)> Route(string method, string path, Dictionary<string, string> h, string body)
        {
            var p = path.Split('?')[0];
            if (p == "/health") return (200, "text/plain", "ok", null);
            if (p != "/mcp") return (404, "text/plain", "not found", null);
            // защита от DNS-rebinding: страница в браузере не должна достучаться до острова
            // (раньше «http://localhost.evil.com» проходил проверку StartsWith — теперь сравниваем сам хост)
            if (h.TryGetValue("origin", out var origin) && origin != "null" && !IsLocalOrigin(origin))
                return (403, "text/plain", "forbidden origin", null);
            if (h.TryGetValue("host", out var host) && !IsLocalHost(host))
                return (403, "text/plain", "forbidden host", null);
            var auth = h.TryGetValue("authorization", out var a) ? a : "";
            var qtok = path.Contains("token=") ? Uri.UnescapeDataString(path[(path.IndexOf("token=") + 6)..].Split('&')[0]) : "";
            if (!SameSecret(auth, "Bearer " + Token) && !SameSecret(qtok, Token))
                return (401, "application/json", "{\"error\":\"unauthorized\"}", "WWW-Authenticate: Bearer\r\n");
            if (method == "GET") return (405, "text/plain", "SSE stream is not supported; use POST", "Allow: POST\r\n");
            if (method == "DELETE") return (200, "text/plain", "", null);
            if (method != "POST") return (405, "text/plain", "", "Allow: POST\r\n");
            var resp = await Handle(body);
            return resp == null ? (202, "text/plain", "", null) : (200, "application/json", resp, null);
        }

        // ------------------------------------------------------------------
        //  JSON-RPC / MCP
        // ------------------------------------------------------------------

        /// <summary>Разобрать запрос (одиночный или пачку) и вернуть ответ; null — только уведомления, ответа нет.</summary>
        public static async Task<string> Handle(string body)
        {
            JsonNode node;
            try { node = JsonNode.Parse(body); }
            catch { return Err(null, -32700, "Parse error"); }
            if (node is JsonArray arr)
            {
                var outs = new JsonArray();
                foreach (var item in arr)
                {
                    var r = await One(item as JsonObject);
                    if (r != null) outs.Add(JsonNode.Parse(r));
                }
                return outs.Count == 0 ? null : outs.ToJsonString(Out);
            }
            return await One(node as JsonObject);
        }

        static readonly JsonSerializerOptions Out = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        static string Ok(JsonNode id, JsonNode result) => new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id?.DeepClone(), ["result"] = result }.ToJsonString(Out);
        static string Err(JsonNode id, int code, string msg) => new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id?.DeepClone(), ["error"] = new JsonObject { ["code"] = code, ["message"] = msg } }.ToJsonString(Out);

        static async Task<string> One(JsonObject req)
        {
            if (req == null) return Err(null, -32600, "Invalid Request");
            var id = req["id"];
            var method = req["method"]?.GetValue<string>() ?? "";
            var prm = req["params"] as JsonObject;
            if (id == null) return null;   // уведомление (notifications/initialized и т. п.) — без ответа
            switch (method)
            {
                case "initialize":
                {
                    var ver = prm?["protocolVersion"]?.GetValue<string>() ?? ProtocolVersion;
                    return Ok(id, new JsonObject
                    {
                        ["protocolVersion"] = ver is "2024-11-05" or "2025-03-26" or "2025-06-18" ? ver : ProtocolVersion,
                        ["capabilities"] = new JsonObject { ["tools"] = new JsonObject { ["listChanged"] = false } },
                        ["serverInfo"] = new JsonObject { ["name"] = "n-island", ["title"] = "N-Island", ["version"] = AppInfo.Version },
                        ["instructions"] = "N-Island — Dynamic Island для Windows на ПК пользователя. Используй island_notify / island_progress, чтобы показывать ход " +
                                           "долгих задач и их итог прямо в чёлке, telegram_send — чтобы написать пользователю на телефон, когда закончил. " +
                                           "Перед изменением вида острова посмотри settings_list. Отвечай пользователю на его языке.",
                    });
                }
                case "ping": return Ok(id, new JsonObject());
                case "tools/list":
                {
                    var list = new JsonArray();
                    foreach (var t in Tools.Where(t => t.Level <= S.McpLevel))
                        list.Add(new JsonObject
                        {
                            ["name"] = t.Name, ["title"] = t.Title, ["description"] = t.Desc, ["inputSchema"] = t.Schema.DeepClone(),
                            ["annotations"] = new JsonObject { ["readOnlyHint"] = t.Level == 0, ["openWorldHint"] = false },
                        });
                    return Ok(id, new JsonObject { ["tools"] = list });
                }
                case "tools/call":
                {
                    var name = prm?["name"]?.GetValue<string>();
                    var tool = Tools.FirstOrDefault(t => t.Name == name);
                    if (tool == null) return Err(id, -32602, "Unknown tool: " + name);
                    if (tool.Level > S.McpLevel) return Ok(id, Result("Инструмент выключен: в N-Island → Настройки → MCP разреши уровень «" + LevelName(tool.Level) + "»", true));
                    var args = JsonDocument.Parse((prm?["arguments"] ?? new JsonObject()).ToJsonString()).RootElement;
                    try
                    {
                        var r = await tool.Run(args);
                        Note(name, true, r is string s0 ? Cut(s0, 80) : "");
                        return Ok(id, r is Image img
                            ? new JsonObject { ["content"] = new JsonArray(new JsonObject { ["type"] = "image", ["data"] = Convert.ToBase64String(img.Png), ["mimeType"] = "image/png" }) }
                            : Result(r is string s ? s : JsonSerializer.Serialize(r, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }), false));
                    }
                    catch (Exception e)
                    {
                        Note(name, false, e.Message);
                        return Ok(id, Result(e is InvalidOperationException ? e.Message : "Ошибка: " + e.Message, true));
                    }
                }
                case "resources/list": return Ok(id, new JsonObject { ["resources"] = new JsonArray() });
                case "prompts/list": return Ok(id, new JsonObject { ["prompts"] = new JsonArray() });
                default: return Err(id, -32601, "Method not found: " + method);
            }
        }

        static JsonObject Result(string text, bool error) =>
            new() { ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }), ["isError"] = error };

        static void Note(string tool, bool ok, string note)
        {
            Log.Enqueue(new CallLog(DateTime.Now, tool, ok, note));
            while (Log.Count > 60) Log.TryDequeue(out _);
            Changed?.Invoke();
        }

        static bool IsLocalName(string host) => host is "127.0.0.1" or "localhost" or "[::1]" or "::1";

        /// <summary>Origin вида http://127.0.0.1:порт или http://localhost:порт — ровно этот хост, без поддоменов.</summary>
        static bool IsLocalOrigin(string origin) =>
            Uri.TryCreate(origin, UriKind.Absolute, out var u) && (u.Scheme == "http" || u.Scheme == "https") && IsLocalName(u.Host);

        /// <summary>Заголовок Host: защита от DNS-rebinding (чужой домен, указывающий на 127.0.0.1).</summary>
        static bool IsLocalHost(string host)
        {
            host = host.Trim();
            if (host.StartsWith('[')) { int e = host.IndexOf(']'); return e > 0 && IsLocalName(host[..(e + 1)]); }
            int c = host.LastIndexOf(':');
            return IsLocalName(c > 0 ? host[..c] : host);
        }

        /// <summary>Сравнение токена за постоянное время.</summary>
        static bool SameSecret(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            var x = Encoding.UTF8.GetBytes(a); var y = Encoding.UTF8.GetBytes(b);
            return x.Length == y.Length && CryptographicOperations.FixedTimeEquals(x, y);
        }

        public static string LevelName(int l) => l switch { 0 => "Только чтение", 1 => "Управление", _ => "Настройка острова" };
        static string Cut(string s, int n) => s.Length > n ? s[..n] + "…" : s;

        // ------------------------------------------------------------------
        //  Инструменты
        // ------------------------------------------------------------------

        static JsonObject Schema(params (string Name, string Type, string Desc, bool Req)[] props)
        {
            var p = new JsonObject();
            foreach (var (n, t, d, _) in props) p[n] = new JsonObject { ["type"] = t, ["description"] = d };
            var req = new JsonArray(props.Where(x => x.Req).Select(x => (JsonNode)x.Name).ToArray());
            return new JsonObject { ["type"] = "object", ["properties"] = p, ["required"] = req };
        }

        static string Str(JsonElement a, string k, string def = null) => a.ValueKind == JsonValueKind.Object && a.TryGetProperty(k, out var v) && v.ValueKind != JsonValueKind.Null ? (v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString()) : def;
        static double Num(JsonElement a, string k, double def) => double.TryParse(Str(a, k), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : def;
        static Task<T> Ui<T>(Func<T> f) => System.Windows.Application.Current.Dispatcher.InvokeAsync(f).Task;
        static Task Ui(Action f) => System.Windows.Application.Current.Dispatcher.InvokeAsync(f).Task;

        static readonly Dictionary<string, (string Glyph, Color Tint)> Kinds = new()
        {
            ["info"] = ("", Color.FromRgb(0x0A, 0x84, 0xFF)), ["success"] = ("", Color.FromRgb(0x30, 0xD1, 0x58)),
            ["warning"] = ("", Color.FromRgb(0xFF, 0x9F, 0x0A)), ["error"] = ("", Color.FromRgb(0xFF, 0x45, 0x3A)),
            ["agent"] = ("", Color.FromRgb(0xD9, 0x77, 0x57)),
        };

        /// <summary>Настройки, которые агент может читать и менять (вид и поведение острова, не ключи и не приватное).</summary>
        static readonly string[] SettingPrefixes =
        {
            "Viz", "Lyrics", "Mascot", "Liquid", "Glass", "Blur", "Notch", "Accent", "Preset", "Style", "Anim", "Pg", "Island", "Desk", "Clock", "Mode", "MaxRadius",
            "BgOpacity", "IdleOpacity", "IslandOpacity", "ExpandedWidth", "SideBubble", "LiveBars", "LiveLyrics", "Beat", "ArtInNotch", "Visualizer", "PlayerGlow",
            "ShowRemaining", "OpenMode", "PageIcons", "Weather", "Language",
        };

        static IEnumerable<PropertyInfo> EditableSettings() => typeof(Settings).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite && p.CanRead && (p.PropertyType == typeof(bool) || p.PropertyType == typeof(int) || p.PropertyType == typeof(string) || p.PropertyType == typeof(double))
                        && SettingPrefixes.Any(x => p.Name.StartsWith(x, StringComparison.Ordinal)) && !p.Name.Contains("Key") && !p.Name.Contains("Token") && !p.Name.Contains("Bounds"));

        public static readonly List<Tool> Tools = new()
        {
            // ---------- чтение ----------
            new("island_status", "Состояние ПК и острова", "Время, режим острова, батарея, процессор и память, громкость, что играет, погода, активное окно, непрочитанные уведомления.", 0, Schema(), async _ =>
            {
                var w = WeatherService.Cached;
                var m = MediaService.I;
                var sys = SystemService.I;
                return await Ui(() => new
                {
                    time = DateTime.Now.ToString("yyyy-MM-dd HH:mm"), mode = S.Mode, battery = sys.HasBattery ? (object)new { percent = sys.BatteryPercent, charging = sys.Charging } : null,
                    cpu = sys.CpuPercent(), ram = sys.RamPercent(), volume = Math.Round(AudioService.I.Volume * 100), muted = AudioService.I.Muted, output = AudioService.I.OutputName,
                    playing = m.HasSession ? new { title = m.Title, artist = m.Artist, app = m.AppName, isPlaying = m.IsPlaying } : null,
                    weather = w != null ? new { city = w.City, temp = Math.Round(w.Temp), text = WeatherService.Describe(w.Code).Text } : null,
                    activeWindow = PcControl.ActiveTitle(), unreadNotifications = NotificationLog.Unread,
                });
            }),
            new("media_now_playing", "Что играет", "Трек, исполнитель, приложение, позиция и текущая строка песни.", 0, Schema(), async _ => await Ui(() =>
            {
                var m = MediaService.I;
                if (!m.HasSession) return (object)"Сейчас ничего не играет";
                return new { title = m.Title, artist = m.Artist, album = m.Album, app = m.AppName, isPlaying = m.IsPlaying, position = m.HasTime ? Fmt.Time(m.Position) : null, duration = m.HasTime ? Fmt.Time(m.Duration) : null, lyricLine = m.CurrentLyric };
            })),
            new("media_lyrics", "Текст песни", "Полный текст текущей песни (с временем строк, если он синхронный).", 0, Schema(("timestamps", "boolean", "добавить время строк", false)), async a => await Ui(() =>
            {
                var m = MediaService.I;
                if (m.Lyrics == null) return "Текст не найден";
                bool ts = Str(a, "timestamps") == "True" || Str(a, "timestamps") == "true";
                return m.TrackLine + "\n\n" + string.Join("\n", m.Lyrics.Select(l => (ts && m.LyricsSynced ? $"[{(int)l.Time.TotalMinutes:00}:{l.Time.Seconds:00}] " : "") + l.Text));
            })),
            new("weather", "Погода", "Погода в городе пользователя: сейчас, советы и прогноз на несколько дней.", 0, Schema(), async _ =>
            {
                var w = await WeatherService.Get();
                if (w == null) return "Погода недоступна: " + WeatherService.LastError;
                return new { city = w.City, temp = w.Temp, feels = w.Feels, text = WeatherService.Describe(w.Code).Text, wind = w.Wind, humidity = w.Humidity, advice = WeatherService.Advice(w),
                             days = w.Days.Take(5).Select(d => new { day = d.Day.ToString("yyyy-MM-dd"), max = d.Max, min = d.Min, text = WeatherService.Describe(d.Code).Text }) };
            }),
            new("system_info", "О компьютере", "Windows, процессор, видеокарта, память, диски, время работы.", 0, Schema(), _ => Task.FromResult<object>(PcControl.SysInfo())),
            new("windows_list", "Открытые окна", "Заголовки и программы открытых окон; активное помечено.", 0, Schema(), _ => Task.FromResult<object>(PcControl.ListWindows().Select(w => new { title = w.Title, process = w.Process, active = w.Active, minimized = w.Minimized }))),
            new("notes_get", "Заметки", "Текст заметок пользователя из острова.", 0, Schema(), async _ => await Ui(() => (object)(Store.Data.Notes ?? ""))),
            new("todos_list", "Задачи", "Список задач пользователя (id, текст, выполнена ли).", 0, Schema(), async _ => await Ui(() => (object)Store.Data.Todos.Select(t => new { id = t.Id, text = t.Text, done = t.Done }).ToList())),
            new("reminders_list", "Напоминания", "Ближайшие напоминания и события календаря острова.", 0, Schema(), async _ => await Ui(() => (object)Store.Data.Events.Where(e => e.At is DateTime d && d > DateTime.Now.AddHours(-1)).OrderBy(e => e.At).Take(30).Select(e => new { at = e.At?.ToString("yyyy-MM-dd HH:mm"), title = e.Title }).ToList())),
            new("notifications_recent", "Последние уведомления", "Уведомления Windows, которые видел остров (приложение, заголовок, текст).", 0, Schema(("limit", "integer", "сколько (по умолчанию 15)", false)), async a => await Ui(() => (object)NotificationLog.Items.Take((int)Num(a, "limit", 15)).Select(n => new { app = n.App, title = n.Title, body = n.Body }).ToList())),
            new("settings_list", "Настройки острова", "Настройки вида и поведения острова, которые можно менять через settings_set: имя, тип, текущее значение.", 0, Schema(("filter", "string", "часть имени, например Viz или Mascot", false)), async a => await Ui(() =>
            {
                var f = Str(a, "filter") ?? "";
                return (object)EditableSettings().Where(p => f == "" || p.Name.Contains(f, StringComparison.OrdinalIgnoreCase)).Select(p => new { name = p.Name, type = p.PropertyType.Name, value = p.GetValue(S) }).ToList();
            })),
            new("visualizer_styles", "Стили визуализатора и палитры", "Ключи стилей визуализатора, палитр, персонажей и стилей текста песен — для settings_set.", 0, Schema(), _ => Task.FromResult<object>(new
            {
                styles = NIsland.Controls.Visualizer.Styles.Select(s => new { key = s.Key, name = s.Name, group = s.Group }),
                palettes = NIsland.Controls.Visualizer.Palettes.Select(p => p.Key), mascots = NIsland.Controls.Mascot.Skins.Select(m => m.Key).Prepend("none"),
                lyricsStyles = new[] { "apple", "classic", "neon", "minimal", "bounce" }, modes = Modes.All.Select(m => m.Key),
            })),

            new("integrations_status", "Состояние интеграций", "Какие интеграции включены (Telegram, почта, GitHub, курсы…), работают ли они и что пишут о последней проверке. Ключи и токены не показываются.", 0, Schema(),
                async _ => await Ui(() => Integrations.All.Where(i => i.Enabled).Select(i => new { key = i.Key, name = i.Name, ready = i.Ready, ok = i.Ok, status = i.Status }).ToList())),
            new("security_audit", "Проверка безопасности Windows", "Защитник и его базы, брандмауэр, UAC, SmartScreen, подмена сайтов в hosts, прокси, RDP, неподписанные программы в автозапуске.", 0, Schema(),
                async _ => (await Antivirus.Audit()).Select(x => new { level = x.Level == 2 ? "danger" : x.Level == 1 ? "warning" : "ok", title = x.Title, advice = x.Advice }).ToList()),
            new("av_scan_file", "Проверить файл на вирусы", "Проверка файла Защитником N-Island: база угроз, движок Windows (AMSI), подпись и эвристики. Возвращает оценку 0–100 и причины.", 1,
                Schema(("path", "string", "полный путь к файлу", true)),
                async a =>
                {
                    var p = Str(a, "path", "");
                    if (!System.IO.File.Exists(p)) throw new InvalidOperationException("Файл не найден: " + p);
                    var t = await Task.Run(() => Antivirus.ScanFile(p));
                    return new { path = t.Path, score = t.Score, verdict = t.Verdict, reasons = t.Reasons, sha256 = t.Sha256 };
                }),

            // ---------- управление ----------
            new("island_notify", "Уведомление в острове", "Показать всплывашку в чёлке острова: заголовок, текст, тип (info, success, warning, error, agent). Удобно сообщить о завершении задачи.", 1,
                Schema(("title", "string", "заголовок", true), ("text", "string", "текст", false), ("kind", "string", "info | success | warning | error | agent", false), ("seconds", "number", "сколько показывать (по умолчанию 6)", false), ("sound", "boolean", "со звуком", false)),
                async a =>
                {
                    var kind = Str(a, "kind", "agent");
                    var (glyph, tint) = Kinds.TryGetValue(kind, out var k) ? k : Kinds["agent"];
                    await Ui(() =>
                    {
                        if (Str(a, "sound") is "true" or "True") Sound.Play(kind is "error" or "warning" ? SoundEvent.Alert : SoundEvent.Notification);
                        App.Island?.ShowActivity(new Activity { Kind = "agent", Glyph = glyph, Tint = tint, Title = Str(a, "title", "Агент"), Sub = Str(a, "text", ""), Seconds = Math.Clamp(Num(a, "seconds", 6), 2, 60), Wide = true });
                    });
                    return "Показано в острове";
                }),
            new("island_progress", "Прогресс задачи", "Показать в острове полосу прогресса долгой задачи (0–100). Вызывай по мере работы — полоса обновляется.", 1,
                Schema(("title", "string", "что делается", true), ("percent", "number", "0–100", true), ("text", "string", "подпись", false)),
                async a =>
                {
                    double pct = Math.Clamp(Num(a, "percent", 0), 0, 100);
                    await Ui(() => App.Island?.ShowActivity(new Activity { Kind = "agent", Glyph = pct >= 100 ? "" : "", Tint = Color.FromRgb(0xD9, 0x77, 0x57), Title = Str(a, "title", "Задача"), Sub = Str(a, "text", $"{pct:0}%"), Progress = pct / 100, Seconds = pct >= 100 ? 5 : 20, Wide = true }));
                    return $"Прогресс {pct:0}%";
                }),
            new("telegram_send", "Написать на телефон", "Отправить сообщение пользователю в Telegram через бота N-Island (если он подключён). Например: «Сборка готова, тесты прошли».", 1,
                Schema(("text", "string", "текст сообщения", true)),
                async a =>
                {
                    var tg = Integrations.Get("telegram");
                    if (tg == null || !tg.Enabled || !tg.Ready) throw new InvalidOperationException("Telegram-бот не подключён в N-Island");
                    await tg.Send(new HubEvent("agent", "ИИ-агент", Str(a, "text", "")));
                    return "Отправлено";
                }),
            new("media_control", "Управление музыкой", "Действие с плеером: play, pause, toggle, next, prev, forward (+10 с), back (−10 с).", 1,
                Schema(("action", "string", "play | pause | toggle | next | prev | forward | back", true)),
                async a =>
                {
                    var act = Str(a, "action", "toggle");
                    await Ui(() =>
                    {
                        var m = MediaService.I;
                        switch (act) { case "play": m.Play(); break; case "pause": m.Pause(); break; case "next": m.Next(); break; case "prev": m.Prev(); break; case "forward": m.SeekBy(10); break; case "back": m.SeekBy(-10); break; default: m.TogglePlay(); break; }
                    });
                    return "Готово: " + act;
                }),
            new("set_volume", "Громкость", "Установить громкость системы в процентах или выключить звук.", 1,
                Schema(("percent", "number", "0–100", false), ("mute", "boolean", "true — без звука, false — со звуком", false)),
                async a =>
                {
                    await Ui(() =>
                    {
                        if (Str(a, "percent") != null) AudioService.I.SetVolume(Math.Clamp(Num(a, "percent", 50), 0, 100) / 100);
                        if (Str(a, "mute") is { } mu && (mu is "true" or "True") != AudioService.I.Muted) AudioService.I.ToggleMute();
                    });
                    return "Готово";
                }),
            new("set_mode", "Режим острова", "Режим: normal, dnd (не беспокоить), focus, minimal, night, game, present.", 1,
                Schema(("mode", "string", "ключ режима", true)),
                async a =>
                {
                    var mode = Str(a, "mode", "normal");
                    if (!Modes.All.Any(m => m.Key == mode)) throw new InvalidOperationException("Нет такого режима. Есть: " + string.Join(", ", Modes.All.Select(m => m.Key)));
                    await Ui(() => App.Island?.SetMode(mode));
                    return "Режим: " + mode;
                }),
            new("note_add", "Добавить заметку", "Дописать строку в заметки острова.", 1, Schema(("text", "string", "текст", true)),
                async a => { await Ui(() => { Store.Data.Notes = (Store.Data.Notes ?? "").TrimEnd() + (string.IsNullOrWhiteSpace(Store.Data.Notes) ? "" : "\n") + Str(a, "text", ""); Store.SaveData(); }); return "Добавлено"; }),
            new("todo_add", "Добавить задачу", "Новая задача в списке острова.", 1, Schema(("text", "string", "текст задачи", true)),
                async a => { var id = await Ui(() => { var t = new TodoItem { Text = Str(a, "text", "") }; Store.Data.Todos.Insert(0, t); Store.SaveData(); return t.Id; }); return "Задача добавлена, id " + id; }),
            new("todo_complete", "Отметить задачу", "Отметить задачу выполненной по id (из todos_list).", 1, Schema(("id", "string", "id задачи", true)),
                async a => await Ui(() => { var t = Store.Data.Todos.FirstOrDefault(x => x.Id == Str(a, "id")); if (t == null) throw new InvalidOperationException("Нет такой задачи"); t.Done = true; Store.SaveData(); return "Готово: " + t.Text; })),
            new("reminder_add", "Напоминание", "Напомнить пользователю: через N минут или в указанное время (HH:mm или yyyy-MM-dd HH:mm).", 1,
                Schema(("text", "string", "о чём напомнить", true), ("minutes", "number", "через сколько минут", false), ("at", "string", "когда: HH:mm или yyyy-MM-dd HH:mm", false)),
                async a =>
                {
                    DateTime when;
                    var at = Str(a, "at");
                    if (at != null && DateTime.TryParse(at, out var d)) { when = at.Length <= 5 ? DateTime.Today + d.TimeOfDay : d; if (when < DateTime.Now) when = when.AddDays(1); }
                    else when = DateTime.Now.AddMinutes(Math.Max(1, Num(a, "minutes", 10)));
                    await Ui(() => { Store.Data.Events.Add(new CalEvent { Date = when.Date, Time = when.ToString("HH:mm"), Title = Str(a, "text", "") }); Store.SaveData(); });
                    return "Напомню " + when.ToString("yyyy-MM-dd HH:mm");
                }),
            new("timer_start", "Таймер", "Запустить таймер острова на N минут (0 — остановить).", 1, Schema(("minutes", "number", "минуты", true)),
                async a =>
                {
                    double min = Num(a, "minutes", 5);
                    await Ui(() => { var t = TimerService.I; t.Reset(); if (min > 0) { t.SetMinutes(min); t.Start(); } });
                    return min > 0 ? $"Таймер на {min:0.#} мин" : "Таймер остановлен";
                }),
            new("clipboard_get", "Буфер обмена", "Текст из буфера обмена ПК.", 1, Schema(), async _ => await Ui(() => { try { return (object)(System.Windows.Clipboard.ContainsText() ? System.Windows.Clipboard.GetText() : "(в буфере нет текста)"); } catch { return "(буфер недоступен)"; } })),
            new("clipboard_set", "Скопировать в буфер", "Положить текст в буфер обмена ПК.", 1, Schema(("text", "string", "текст", true)),
                async a => { await Ui(() => ClipboardService.I.CopyBack(Str(a, "text", ""))); return "Скопировано"; }),
            new("speak", "Сказать вслух", "Произнести текст голосом Windows на ПК.", 1, Schema(("text", "string", "текст", true)),
                async a => { await PcControl.Speak(Cut(Str(a, "text", ""), 1500)); return "Сказано"; }),
            new("open", "Открыть", "Открыть на ПК ссылку, файл, папку или программу.", 1, Schema(("target", "string", "https://…, путь или имя программы", true)),
                async a =>
                {
                    var t = Str(a, "target", "").Trim();
                    // ссылки — уровень «Управление»; файлы и программы запускают код — только при уровне «Настройка острова»
                    bool web = Regex.IsMatch(t, @"^https?://", RegexOptions.IgnoreCase);
                    if (!web && S.McpLevel < 2) return "Открывать файлы и программы можно только с уровнем доступа «" + LevelName(2) + "». Ссылки https:// — можно";
                    await Ui(() => SystemService.Run(t));
                    return "Открыто: " + t;
                }),
            new("focus_window", "Окно на передний план", "Вывести на передний план окно, в заголовке которого есть этот текст.", 1, Schema(("title", "string", "часть заголовка", true)),
                _a =>
                {
                    var q = Str(_a, "title", "");
                    var w = PcControl.ListWindows().FirstOrDefault(x => x.Title.Contains(q, StringComparison.OrdinalIgnoreCase));
                    if (w == null) throw new InvalidOperationException("Окно не найдено");
                    PcControl.Focus(w.Handle);
                    return Task.FromResult<object>("Показано: " + w.Title);
                }),
            new("screenshot", "Снимок экрана", "Снимок всех экранов (или активного окна) — картинкой.", 1, Schema(("window", "boolean", "только активное окно", false)),
                async a => new Image(await Task.Run(() => PcControl.Screenshot(Str(a, "window") is "true" or "True" ? -1 : 0, true)))),
            new("island_expand", "Открыть страницу острова", "Развернуть остров на странице: media, ai, weather, todo, calendar, notes, timer, eq, hub и др.", 1, Schema(("page", "string", "ключ страницы", false)),
                async a => { await Ui(() => App.Island?.Expand(Str(a, "page"), true)); return "Открыто"; }),

            // ---------- настройка острова ----------
            new("settings_set", "Изменить настройку острова", "Поменять вид или поведение острова (имена и типы — из settings_list). Например: VizStyle=galaxy, Mascot=cat, Accent=#FF375F, LiquidGlass=true.", 2,
                Schema(("name", "string", "имя настройки", true), ("value", "string", "новое значение", true)),
                async a =>
                {
                    var name = Str(a, "name", "");
                    var prop = EditableSettings().FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                    if (prop == null) throw new InvalidOperationException("Эту настройку менять нельзя или её нет. Список — settings_list");
                    var raw = Str(a, "value", "");
                    object val = prop.PropertyType == typeof(bool) ? raw.Trim().ToLowerInvariant() is "true" or "1" or "yes" or "on"
                               : prop.PropertyType == typeof(int) ? int.Parse(raw, CultureInfo.InvariantCulture)
                               : prop.PropertyType == typeof(double) ? double.Parse(raw, CultureInfo.InvariantCulture) : raw;
                    var old = await Ui(() => { var o = prop.GetValue(S); prop.SetValue(S, val); return o; });
                    if (prop.Name == "Accent") await Ui(() => App.ApplyAccent(null));
                    return $"{prop.Name}: {old} → {val}";
                }),
        };

        // ------------------------------------------------------------------
        //  Мост stdio: NIsland.exe --mcp (для агентов, которые умеют только stdio)
        // ------------------------------------------------------------------

        /// <summary>Строки JSON-RPC из stdin → HTTP основной копии → stdout. Если остров не запущен — запускаем.</summary>
        public static int Bridge()
        {
            var stdin = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
            var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            (int Port, string Token)? info = null;
            (int, string)? Info()
            {
                try { using var d = JsonDocument.Parse(File.ReadAllText(InfoFile)); return (d.RootElement.GetProperty("port").GetInt32(), d.RootElement.GetProperty("token").GetString()); }
                catch { return null; }
            }
            bool started = false;
            string line;
            while ((line = stdin.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                string resp = null;
                for (int attempt = 0; attempt < 2 && resp == null; attempt++)
                {
                    info ??= Info();
                    try
                    {
                        if (info == null) throw new HttpRequestException("no info");
                        using var req = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{info.Value.Port}/mcp") { Content = new StringContent(line, Encoding.UTF8, "application/json") };
                        req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + info.Value.Token);
                        using var r = http.Send(req);
                        if ((int)r.StatusCode == 202) { resp = ""; break; }
                        if ((int)r.StatusCode == 401) { info = null; continue; }
                        resp = r.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    }
                    catch
                    {
                        info = null;
                        if (!started)
                        {
                            // остров не запущен или MCP выключен — запускаем копию с включённым MCP и ждём
                            started = true;
                            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath, "--mcp-on") { UseShellExecute = true })?.Dispose(); } catch { }
                            for (int i = 0; i < 20 && Info() == null; i++) Thread.Sleep(500);
                            Thread.Sleep(800);
                        }
                    }
                }
                if (resp == null)
                {
                    try
                    {
                        var id = JsonNode.Parse(line)?["id"];
                        if (id != null) resp = Err(id, -32000, "N-Island не отвечает: запусти N-Island и включи MCP в Настройки → MCP");
                    }
                    catch { resp = Err(null, -32700, "Parse error"); }
                }
                if (!string.IsNullOrEmpty(resp)) stdout.WriteLine(resp.Replace("\r", "").Replace("\n", " "));
            }
            return 0;
        }
    }
}
