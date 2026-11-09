using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Threading;
using NIsland.Core;

namespace NIsland.Services
{
    /// <summary>
    /// Самопроверка Telegram-бота без Telegram: NIsland.exe --selftest tgbot &lt;выход&gt;.
    /// Запросы к Bot API перехватываются. Выполняются все команды и все переходы по меню; кнопки, которые что-то меняют
    /// на ПК (громкость, клавиши, питание), не нажимаются — для них проверяется только, что бот их знает.
    /// Проверки: callback_data ≤ 64 байт, ≤ 8 кнопок в ряду, текст ≤ 4096, парные HTML-теги, нет исключений.
    /// </summary>
    static class TgBotTest
    {
        static readonly List<(string Method, string Json)> Sent = new();
        static readonly List<string> Problems = new();

        // заголовки кнопок, которые бот обрабатывает (Do) — неизвестная кнопка = ошибка
        static readonly HashSet<string> Known = new()
        {
            "nav", "status!", "ask", "do", "mu", "au", "out", "mx", "eq", "br", "sh", "w", "wa", "kill", "kb", "ms", "mc", "mstep", "d", "dp", "f",
            "fg", "fo", "fd", "fdok", "fc", "z", "pw", "mode", "pp", "vpn", "td", "tm", "ev", "fl", "ainew", "aicp", "airetry", "aip", "aim", "aimodels", "noop",
            "do2", "sc", "jb", "ch", "av2", "rd", "lv", "ap", "x4", "wg",
        };
        // эти кнопки безопасно нажимать: только показывают
        static readonly string[] SafePrefixes = { "nav:", "d:", "dp:", "f:", "w:", "aimodels:", "status!", "do:sysinfo", "do:disk", "do:lyrics", "do:notes", "do:recent", "ask:ai", "ask:search", "ask:type", "ask:copy", "ask:say", "ask:todo",
                                                  "do2:startup", "do2:now", "do2:live", "lv:" };
        // просмотр папок — не больше стольких нажатий, иначе обход тратит весь лимит на файлы и не доходит до других меню
        const int MaxFolderPresses = 25;

        public static int Run(string outputPath)
        {
            var sb = new StringBuilder();
            output = outputPath;
            var task = Task.Run(() => Body(sb));
            // команды обращаются к UI-потоку — крутим его, пока тест идёт
            var frame = new DispatcherFrame();
            task.ContinueWith(_ => frame.Continue = false);
            Dispatcher.PushFrame(frame);
            if (task.Exception != null) sb.AppendLine("FATAL " + task.Exception);
            sb.AppendLine();
            sb.AppendLine($"requests={Sent.Count} problems={Problems.Count}");
            foreach (var p in Problems.Distinct()) sb.AppendLine("PROBLEM " + p);
            File.WriteAllText(output, sb.ToString());
            return Problems.Count == 0 ? 0 : 3;
        }

        static string output = "";
        static async Task Body(StringBuilder log)
        {
            var tg = (TelegramHub)Integrations.Get("telegram");
            void Set(string k, string v) => tg.Cfg.Values[k] = v;   // в память, без шифрования и записи на диск
            tg.Cfg.Values["token"] = Secret.Protect("1:TEST");
            Set("chat", "42");
            foreach (var f in new[] { "control", "apps", "files", "input", "screen" }) Set(f, "1");
            foreach (var f in new[] { "power", "shell", "camera" }) Set(f, "0");   // эти должны вежливо отказать
            tg.Cfg.Enabled = true;

            TelegramHub.TestSink = (method, json) =>
            {
                lock (Sent) Sent.Add((method, json));
                Check(method, json);
                return method switch
                {
                    "getUpdates" => "{\"ok\":true,\"result\":[]}",
                    "sendMessageDraft" => "{\"ok\":false,\"description\":\"Bad Request: method not available in test\"}",
                    "sendRichMessageDraft" => "{\"ok\":true,\"result\":true}",
                    _ => "{\"ok\":true,\"result\":{\"message_id\":7}}",
                };
            };

            // 1. команды без аргументов и с безопасными аргументами
            var cmds = new List<(string, string)>
            {
                ("start", ""), ("help", ""), ("menu", ""), ("status", ""), ("top", ""), ("disk", ""), ("sysinfo", ""), ("uptime", ""), ("battery", ""), ("wifi", ""),
                ("ip", ""), ("music", ""), ("lyrics", ""), ("sound", ""), ("eq", ""), ("screen", ""), ("brightness", ""), ("windows", ""), ("apps", ""), ("keys", ""),
                ("mouse", ""), ("files", ""), ("ls", "~"), ("search", "zzzz-nisland-none"), ("power", ""), ("lock", ""), ("sleep", ""), ("restart", ""), ("shutdown", ""),
                ("cancel", ""), ("mode", ""), ("plan", ""), ("note", ""), ("todo", ""), ("remind", ""), ("remind", "абв"), ("timer", ""), ("clip", ""), ("say", ""),
                ("speak", ""), ("type", ""), ("open", ""), ("cam", ""), ("where", ""), ("weather", ""), ("alert", ""), ("ttn", ""), ("ai", ""), ("model", ""), ("settings", ""),
                ("cmd", "dir"), ("kill", ""), ("kill", "nonexistent-process-xyz"), ("kill", "csrss"), ("get", "C:\\nonexistent\\file.txt"), ("key", "foo+bar"), ("eq", "nope"),
                ("mode", "nope"), ("vol", "abc"), ("wallpaper", ""), ("unknowncmd", ""), ("ping", "127.0.0.1"), ("ai", "Ответь одним словом: привет"),
                // 2.0 (только то, что ничего не меняет на ПК)
                ("now", ""), ("live", "1"), ("watch", "10"), ("scene", ""), ("scene", "nope"), ("in", "10m /status"), ("in", "abc"), ("at", "23:59 /uptime"), ("at", "99:99 /x"),
                ("in", "1m /in 1m /x"), ("jobs", ""), ("cliphist", ""), ("history", ""), ("av", ""), ("tr", "en Привет, мир"), ("tr", ""), ("calc", "(12+8)*3"), ("calc", "abc"),
                ("qr", "https://example.com"), ("wake", ""), ("radio", ""), ("startup", ""), ("pin", ""), ("seek", ""), ("help", ""), ("health", ""), ("do", ""), ("do", "покажи погоду и посчитай 2*21"),
                // 3.0 (только чтение; то, что меняет ПК, здесь не запускается)
                ("cpu", ""), ("mem", ""), ("gpu", ""), ("idle", ""), ("active", ""), ("monitors", ""), ("ports", ""), ("conns", ""), ("usb", ""), ("eject", "E"),
                ("bin", ""), ("temp", ""), ("boot", ""), ("pending", ""), ("update", ""), ("service", "spooler"), ("service", "a;b"), ("bathealth", ""), ("diskhealth", ""),
                ("netusage", ""), ("wifiscan", ""), ("http", "example.com"), ("http", "-x"), ("ssl", "example.com"), ("whois", "example.com"), ("dig", "example.com"),
                ("dig", "a b"), ("ipinfo", "8.8.8.8"), ("ipinfo", "abc"), ("link", "https://example.com"), ("port", "example.com 443"), ("port", "-x 1"),
                ("unshort", "https://example.com"), ("conv", "100 usd uah"), ("conv", "abc"), ("wiki", "Киев"), ("clock", ""), ("clock", "Tokyo"), ("days", "31.12"),
                ("days", "abc"), ("moon", ""), ("sun", ""), ("tomorrow", ""), ("password", ""), ("password", "500"), ("uuid", ""), ("hash", "abc"), ("b64", "привет"),
                ("b64", "-d 0L/RgNC40LLQtdGC"), ("b64", "-d !!"), ("roll", ""), ("roll", "100"), ("roll", "5 10"), ("coin", ""), ("notes", ""), ("todos", ""),
                ("done", "abc"), ("notif", ""), ("appvol", ""), ("mascot", ""), ("mascot", "nope"), ("pomo", "status"),
                // 4.0 (только чтение; закрыть/заморозить/обновить/Caps Lock/раскладку здесь не запускаем)
                ("installed", ""), ("installed", "microsoft"), ("installed", "zzzz-none"), ("appinfo", ""), ("appinfo", "zzzz-none"), ("appinfo", "microsoft"),
                ("hang", ""), ("appcpu", "explorer"), ("appcpu", ""), ("autostart", ""), ("upgrades", ""), ("wsearch", "firefox"), ("wsearch", "a;b"),
                ("winset", ""), ("project", ""), ("emptybin", ""), ("recent", ""), ("printers", ""), ("printq", ""), ("events", ""), ("crashes", ""), ("bsod", ""),
                ("hotfix", ""), ("firewall", ""), ("wdstatus", ""), ("users", ""), ("shares", ""), ("drivers", ""), ("usbdev", ""), ("audiodev", ""), ("outdev", ""),
                ("outdev", "zzzz-none"), ("batreport", ""), ("clipimg", ""), ("sysreport", ""), ("wifihealth", ""), ("wifiq", ""), ("channels", ""), ("router", ""),
                ("forget", ""), ("launch", ""), ("launch", "zzzz-none"), ("quit", ""), ("quit", "zzzz-none"), ("freeze", ""), ("freeze", "zzzz-none"), ("priority", ""),
                ("uninstall", ""), ("uninstall", "zzzz-none"), ("upgrade", ""), ("upgrade", "a b"), ("winstall", "Mozilla.Firefox"), ("focus", ""), ("appmute", "zzzz-none"),
                // виджеты Bot API 10.4
                ("widget", ""), ("pet", ""), ("widgets", ""), ("widget", "nope"), ("widget", "плеер"), ("widget", "sysmon"), ("widget", "clock"), ("widget", "calc"),   // /close, /minall, /volup… меняют ПК — их проверяет только пульт без разрешений ниже
            };
            var queue = new Queue<string>();
            foreach (var (c, a) in cmds)
            {
                Sent.Clear();
                try
                {
                    var r = await tg.TestCommand(c, a);
                    if (r?.Html != null) { Check("reply", JsonSerializer.Serialize(new { text = r.Value.Html, reply_markup = r.Value.Keys })); Collect(JsonSerializer.Serialize(r.Value.Keys), queue); }
                    log.AppendLine($"/{c} {a} → {Short(r?.Html ?? LastText())}");
                }
                catch (Exception e) { Problems.Add($"/{c} {a}: {e.GetType().Name}: {e.Message}"); }
                lock (Sent) foreach (var (_, j) in Sent) Collect(j, queue);
            }

            // 2. обход меню: все кнопки
            var seen = new HashSet<string>();
            int pressed = 0, folders = 0;
            while (queue.Count > 0 && pressed < 400)
            {
                var data = queue.Dequeue();
                if (!seen.Add(data)) continue;
                var head = data.Split('|')[0];
                head = head.Contains(':') ? head[..head.IndexOf(':')] : head;
                if (!Known.Contains(head)) { Problems.Add("unknown button: " + data); continue; }
                if (!SafePrefixes.Any(p => data.StartsWith(p))) continue;
                if ((data.StartsWith("d:") || data.StartsWith("dp:") || data.StartsWith("f:")) && ++folders > MaxFolderPresses) continue;
                Sent.Clear();
                try
                {
                    var ans = await tg.TestCallback(data);
                    pressed++;
                    log.AppendLine($"[{data}] → {Short(ans)} | {Short(LastText())}");
                }
                catch (Exception e) { Problems.Add($"[{data}]: {e.GetType().Name}: {e.Message}"); }
                lock (Sent) foreach (var (_, j) in Sent) Collect(j, queue);
            }
            log.AppendLine($"buttons seen={seen.Count} pressed={pressed}");
            lock (log2) foreach (var l in log2.Distinct()) log.AppendLine(l);

            // 3. пульт из других мессенджеров: тот же движок, ответы не уходят в Telegram
            await Task.Delay(6000);   // кнопки обхода (автозагрузка, здоровье ПК) досылают ответы в фоне — дождаться их
            lock (Sent) Sent.Clear();
            var none = new Func<string, bool>(_ => false);
            var all = new Func<string, bool>(k => k != "shell" && k != "power");
            foreach (var (text, flags, expectFile, expectRefuse) in new (string, Func<string, bool>, bool, bool)[]
            {
                ("/status", none, false, false), ("!vol", none, false, false), ("/cpu", none, false, false), ("/screenshot", none, false, true),
                ("/screenshot", all, true, false), ("/lock", all, false, true), ("/cmd ipconfig", all, false, true), ("/live", all, false, false),
                ("/help", none, false, false), ("/wiki Linux", none, false, false), ("/nope", none, false, false), ("/active", none, false, true),
            })
            {
                var ctx = await TelegramHub.RunRemote(text, "Discord", flags, (_, _) => Task.CompletedTask, false);
                var outText = string.Join(" | ", ctx.Out);
                log.AppendLine($"remote {text} → files={ctx.Files.Count} {Short(outText)}");
                if (ctx.Out.Count == 0 && ctx.Files.Count == 0) Problems.Add($"remote {text}: no answer");
                if (expectFile && ctx.Files.Count == 0) Problems.Add($"remote {text}: file expected");
                if (expectRefuse && !outText.Contains("🔐")) Problems.Add($"remote {text}: refusal expected, got {Short(outText)}");
                if (expectRefuse && !outText.Contains("Discord")) Problems.Add($"remote {text}: refusal should name Discord");
                foreach (var o in ctx.Out) Check("remote", JsonSerializer.Serialize(new { text = o, parse_mode = "HTML" }));
            }
            lock (Sent) if (Sent.Count > 0) Problems.Add("remote commands leaked to Telegram: " + string.Join(" | ", Sent.Select(x => x.Method + " " + Short(x.Json))));
            var md = TelegramHub.ToMarkdown("<b>Жирный</b> <i>к</i> <code>x&lt;y</code> <a href=\"https://e.com\">ссылка</a>", false);
            if (md != "**Жирный** _к_ `x<y` [ссылка](https://e.com)") Problems.Add("ToMarkdown discord: " + md);
            var sk = TelegramHub.ToMarkdown("<b>A</b> 1 &lt; 2 <a href=\"https://e.com\">l</a>", true);
            if (sk != "*A* 1 &lt; 2 <https://e.com|l>") Problems.Add("ToMarkdown slack: " + sk);
            var mx = TelegramHub.ToMatrixHtml("<b>a</b><script>x</script><a href=\"javascript:alert(1)\">l</a>");
            if (mx.Contains("<script") || mx.Contains("javascript:")) Problems.Add("ToMatrixHtml unsafe: " + mx);
            log.AppendLine($"markdown: {md} · slack: {sk} · matrix: {mx}");
            var rh = TelegramHub.ToRichHtml("🎛 <b>Пульт</b>\nстрока 1\nстрока 2\n\nабзац <i>2</i>\n<pre>a\nb</pre>");
            if (!rh.StartsWith("<h3>🎛 Пульт</h3><p>строка 1<br>строка 2</p><p>абзац <i>2</i></p><pre>a\nb</pre>")) Problems.Add("ToRichHtml: " + rh);
            var lg = TelegramHub.Legacy("x\n" + TelegramHub.TestTbl(new[] { "A", "B" }, new[] { new[] { "1", "22" } }));
            if (!lg.Contains("<pre>") || lg.Contains("\u0002")) Problems.Add("Legacy table: " + lg);
            var mdh = TelegramHub.TestMd("**жир** *курсив* [ссылка](https://e.com) ~~зач~~\n> цитата\n| a | b |\n|---|---|\n| 1 | 2 |\n```py\nprint(1)\n```");
            foreach (var need in new[] { "<b>жир</b>", "<i>курсив</i>", "<a href=\"https://e.com\">ссылка</a>", "<s>зач</s>", "<blockquote>цитата</blockquote>", "<pre>| a | b |", "language-py" })
                if (!mdh.Contains(need)) Problems.Add("Md missing " + need + ": " + Short(mdh));
            log.AppendLine("rich: " + Short(rh) + " · md: " + Short(mdh));
            // все виджеты: HTML ≤ 4 КБ после zlib, состояние ≤ 1024 байт и JSON; предпросмотр с настоящим состоянием — в папку widgets рядом с отчётом
            var wdir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output)) ?? ".", "widgets");
            Directory.CreateDirectory(wdir);
            var index = new StringBuilder("<!doctype html><meta charset=utf-8><style>body{background:#0e1621;color:#fff;font:13px system-ui;display:flex;flex-wrap:wrap;gap:16px;padding:16px}" +
                ".w{display:flex;flex-direction:column;gap:4px}iframe{border:0;border-radius:14px;background:#182533}</style>");
            foreach (var wk in TelegramHub.TestKinds)
            {
                var html = TelegramHub.TestHtml(wk.Key);
                var raw = Encoding.UTF8.GetBytes(html);
                using var ms = new MemoryStream();
                using (var z = new System.IO.Compression.ZLibStream(ms, System.IO.Compression.CompressionLevel.SmallestSize, true)) z.Write(raw);
                string st;
                try { st = tg.TestWState(wk.Key); } catch (Exception e) { Problems.Add($"widget {wk.Key}: state {e.GetType().Name}: {e.Message}"); continue; }
                int sb = Encoding.UTF8.GetByteCount(st);
                if (sb > 1024) Problems.Add($"widget {wk.Key}: state {sb} > 1024 bytes");
                if (ms.Length > 3900) Problems.Add($"widget {wk.Key}: html compressed {ms.Length} > 3900");
                try { JsonDocument.Parse(st).Dispose(); } catch { Problems.Add($"widget {wk.Key}: state not JSON"); }
                var ans = await tg.TestWidgetAction("c:" + wk.Key + ":ref");
                if (!(wk.Key is "remote" or "pet") && ans == null) Problems.Add($"widget {wk.Key}: ref gave no state");
                log.AppendLine($"widget {wk.Key,-10} html={raw.Length} zlib={ms.Length} state={sb}");
                var stub = "<script>window.TelegramWidget={chatData:" + JsonSerializer.Serialize(st) + ",onEvent:function(){},copyText:function(){},request:function(q){document.title=q;return Promise.resolve({result:null})}};</script>";
                File.WriteAllText(Path.Combine(wdir, wk.Key + ".html"), html.Replace("<head>", "<head>" + stub));
                index.Append($"<div class=w><span>{wk.Name}</span><iframe src=\"{wk.Key}.html\" width={wk.W} height={(wk.H == 0 ? 360 : wk.H)}></iframe></div>");
            }
            File.WriteAllText(Path.Combine(wdir, "index.html"), index.ToString());
            // персонаж собирается под скин, аксессуар и эмоцию — самый тяжёлый вариант тоже должен влезать в 4 КБ
            foreach (var pSk in new[] { "blob", "cat", "ghost", "robot", "bunny" })
                foreach (var ht in new[] { "auto", "none", "cap", "crown", "bow", "party", "santa", "headphones", "glasses", "flower" })
                    foreach (var pMd in new[] { "idle", "amazed", "sneeze", "headbang", "scared", "hot", "sick", "angry", "cool" })
                    {
                        var raw = Encoding.UTF8.GetBytes(TelegramHub.TestPetHtml(pSk, ht, pMd));
                        using var ms = new MemoryStream();
                        using (var z = new System.IO.Compression.ZLibStream(ms, System.IO.Compression.CompressionLevel.SmallestSize, true)) z.Write(raw);
                        if (ms.Length > 3900) Problems.Add($"pet widget {pSk}/{ht}/{pMd}: compressed {ms.Length} > 3900");
                    }
            foreach (var q in new[] { "w:ref", "w:zzz", "p:poke", "p:love" })
            {
                var res = await tg.TestWidgetAction(q);
                if (res == null) { Problems.Add($"widget {q}: no state"); continue; }
                if (Encoding.UTF8.GetByteCount(res) > 1024) Problems.Add($"widget {q}: state {Encoding.UTF8.GetByteCount(res)} > 1024 bytes");
                try { JsonDocument.Parse(res).Dispose(); } catch { Problems.Add($"widget {q}: not JSON: {Short(res)}"); }
                log.AppendLine($"widget {q} → {Short(res)}");
            }
            TelegramHub.TestSink = null;
        }

        static string LastText()
        {
            lock (Sent)
                for (int i = Sent.Count - 1; i >= 0; i--)
                {
                    using var d = JsonDocument.Parse(Sent[i].Json);
                    if (d.RootElement.TryGetProperty("text", out var t)) return t.GetString();
                    if (d.RootElement.TryGetProperty("rich_message", out var rm))
                        return rm.TryGetProperty("html", out var rh) ? rh.GetString() : rm.TryGetProperty("markdown", out var rmd) ? rmd.GetString() : "";
                }
            return "";
        }

        static string Short(string s) => s == null ? "" : Regex.Replace(s.Length > 110 ? s[..TelegramHub.SafeLen(s, 110)] + "…" : s, @"\s+", " ");

        static void Collect(string json, Queue<string> q)
        {
            foreach (Match m in Regex.Matches(json ?? "", "\"callback_data\":\"((?:[^\"\\\\]|\\\\.)*)\""))
                q.Enqueue(JsonDocument.Parse("\"" + m.Groups[1].Value + "\"").RootElement.GetString());
        }

        static readonly List<string> log2 = new();
        static readonly string[] Tags = { "b", "i", "code", "pre", "blockquote" };

        static void Check(string method, string json)
        {
            using var d = JsonDocument.Parse(json);
            var r = d.RootElement;
            if (r.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
            {
                var s = t.GetString();
                if (s.Length > 4096) Problems.Add($"{method}: text {s.Length} > 4096: {Short(s)}");
                if (string.IsNullOrWhiteSpace(s)) Problems.Add($"{method}: empty text");
                if (!r.TryGetProperty("parse_mode", out _) && method != "reply") { }
                else
                    foreach (var tag in Tags)
                    {
                        int open = Regex.Matches(s, $"<{tag}(\\s[^>]*)?>").Count, close = Regex.Matches(s, $"</{tag}>").Count;
                        if (open != close) Problems.Add($"{method}: unbalanced <{tag}> {open}/{close}: {Short(s)}");
                    }
                if (Regex.IsMatch(s, @"<(?!/?(b|i|u|s|code|pre|blockquote|a|tg-spoiler)[\s>])[a-zA-Z]")) Problems.Add($"{method}: unescaped '<': {Short(s)}");
            }
            if (method == "sendWidget")
            {
                var html = r.TryGetProperty("html", out var wh) ? wh.GetString() ?? "" : "";
                var raw = Encoding.UTF8.GetBytes(html);
                using var ms = new System.IO.MemoryStream();
                using (var z = new System.IO.Compression.ZLibStream(ms, System.IO.Compression.CompressionLevel.SmallestSize, true)) z.Write(raw);
                if (raw.Length > 32768) Problems.Add($"sendWidget: html {raw.Length} > 32768");
                if (ms.Length > 3900) Problems.Add($"sendWidget: compressed {ms.Length} > 3900 (limit 4096)");
                if (r.TryGetProperty("allow_queries", out var aq) && aq.ValueKind == JsonValueKind.True && !html.Contains("TelegramWidget")) Problems.Add("sendWidget: allow_queries but html does not use TelegramWidget");
                if (Regex.IsMatch(html, @"\b(src|href)\s*=\s*[""']https?://", RegexOptions.IgnoreCase)) Problems.Add("sendWidget: external resource in widget");
                if (r.TryGetProperty("chat_data", out var cd) && Encoding.UTF8.GetByteCount(cd.GetString() ?? "") > 1024) Problems.Add("sendWidget: chat_data > 1024 bytes");
                log2.Add($"sendWidget html={raw.Length} zlib={ms.Length}");
            }
            if (r.TryGetProperty("rich_message", out var rich) && rich.ValueKind == JsonValueKind.Object)
            {
                if (rich.TryGetProperty("html", out var rhtml))
                {
                    var hs = rhtml.GetString() ?? "";
                    if (hs.Length > 32768) Problems.Add($"{method}: rich html {hs.Length} > 32768");
                    if (hs.Contains('\n') && !Regex.IsMatch(hs, "<pre")) Problems.Add($"{method}: raw newline in rich html: {Short(hs)}");
                    foreach (var tag in new[] { "b", "i", "u", "s", "code", "pre", "blockquote", "p", "h3", "table", "tr", "td", "th", "a", "tg-spoiler" })
                    {
                        int open = Regex.Matches(hs, $"<{tag}(\\s[^>]*)?>").Count, close = Regex.Matches(hs, $"</{tag}>").Count;
                        if (open != close) Problems.Add($"{method}: rich unbalanced <{tag}> {open}/{close}: {Short(hs)}");
                    }
                    if (Regex.IsMatch(hs, "[\u0002\u0003]")) Problems.Add($"{method}: table marker leaked into rich html");
                    if (Regex.IsMatch(hs, @"<(?!/?(b|i|u|s|code|pre|blockquote|a|tg-spoiler|p|br|h[1-6]|table|tr|td|th|ul|ol|li|details|summary|mark|sub|sup|hr)[\s>/])[a-zA-Z]"))
                        Problems.Add($"{method}: unknown tag in rich html: {Short(hs)}");
                }
                if (!rich.TryGetProperty("html", out _) && !rich.TryGetProperty("markdown", out _) && !rich.TryGetProperty("blocks", out _))
                    Problems.Add($"{method}: rich_message without html/markdown/blocks");
            }
            if (method != "reply" && r.TryGetProperty("text", out var tx2) && tx2.ValueKind == JsonValueKind.String && Regex.IsMatch(tx2.GetString() ?? "", "[\u0002\u0003]"))
                Problems.Add($"{method}: table marker leaked into text");
            // Telegram ждёт в reply_markup объект одного из видов — массив или строка дают «object expected as reply markup»
            if (r.TryGetProperty("reply_markup", out var rm0) && rm0.ValueKind != JsonValueKind.Null)
            {
                if (rm0.ValueKind != JsonValueKind.Object) Problems.Add($"{method}: reply_markup is {rm0.ValueKind}, object expected");
                else if (!rm0.TryGetProperty("inline_keyboard", out _) && !rm0.TryGetProperty("keyboard", out _) && !rm0.TryGetProperty("remove_keyboard", out _) && !rm0.TryGetProperty("force_reply", out _))
                    Problems.Add($"{method}: reply_markup without keyboard field");
                else if (rm0.TryGetProperty("inline_keyboard", out var ik0) && ik0.EnumerateArray().Sum(x => x.GetArrayLength()) > 100)
                    Problems.Add($"{method}: more than 100 inline buttons");
            }
            if (r.TryGetProperty("reply_markup", out var km) && km.ValueKind == JsonValueKind.Object && km.TryGetProperty("inline_keyboard", out var rows))
            {
                foreach (var row in rows.EnumerateArray())
                {
                    if (row.GetArrayLength() > 8) Problems.Add($"{method}: row with {row.GetArrayLength()} buttons");
                    if (row.GetArrayLength() == 0) Problems.Add($"{method}: empty row");
                    foreach (var b in row.EnumerateArray())
                    {
                        if (b.TryGetProperty("callback_data", out var cd) && Encoding.UTF8.GetByteCount(cd.GetString()) > 64) Problems.Add($"callback_data > 64 bytes: {cd.GetString()}");
                        if (!b.TryGetProperty("text", out var bt) || string.IsNullOrWhiteSpace(bt.GetString())) Problems.Add($"{method}: button without text");
                        if (!b.TryGetProperty("callback_data", out _) && !b.TryGetProperty("url", out _) && !b.TryGetProperty("copy_text", out _)) Problems.Add($"{method}: button without action");
                        if (b.TryGetProperty("url", out var bu) && !Regex.IsMatch(bu.GetString() ?? "", @"^(https?|tg)://\S+$")) Problems.Add($"{method}: bad button url: {bu.GetString()}");
                        if (b.TryGetProperty("style", out var bs) && bs.GetString() is not ("danger" or "success" or "primary")) Problems.Add($"{method}: bad button style {bs}");
                        if (b.TryGetProperty("copy_text", out var ctx) && (ctx.ValueKind != JsonValueKind.Object || (ctx.GetProperty("text").GetString()?.Length ?? 0) is 0 or > 256)) Problems.Add($"{method}: bad copy_text");
                    }
                }
            }
        }
    }
}
