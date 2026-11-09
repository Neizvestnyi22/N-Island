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
            "game", "do2", "sc", "jb", "ch", "av2", "rd", "lv", "ap", "x4", "wg", "bw",
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
            if (task.Exception != null) { sb.AppendLine("FATAL " + task.Exception); Problems.Add("FATAL " + task.Exception.GetBaseException().Message); }
            sb.AppendLine();
            sb.AppendLine($"requests={Sent.Count} problems={Problems.Count}");
            foreach (var p in Problems.Distinct()) sb.AppendLine("PROBLEM " + p);
            File.WriteAllText(output, sb.ToString());
            return Problems.Count == 0 ? 0 : 3;
        }

        static string output = "";
        static int WidgetSends, widgetButtons;
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
                    "getMe" => "{\"ok\":true,\"result\":{\"id\":1,\"is_bot\":true,\"username\":\"test_bot\",\"supports_inline_queries\":true}}",
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
                ("widget", ""), ("pet", ""), ("widgets", ""), ("widget", "nope"), ("widget", "плеер"), ("widget", "sysmon"), ("widget", "clock"), ("widget", "calc"),
                // 2.4: отслеживание и контроль — только просмотр (лимиты, фокус, будильники, обои, починки здесь не меняются)
                ("screentime", ""), ("screentime", "вчера"), ("week", ""), ("sessions", ""), ("batlog", ""), ("traffic", ""), ("wifilog", ""), ("usblog", ""),
                ("iplog", ""), ("downloads", ""), ("downloads", "999"), ("notifstats", ""), ("speedlog", ""), ("temps", ""), ("ramtop", ""), ("timeline", ""),
                ("report", ""), ("report", "вчера"), ("miclevel", ""), ("limits", ""), ("limit", ""), ("limit", "abc"), ("focusmode", ""), ("watchproc", ""),
                ("alarms", ""), ("alarm", ""), ("alarm", "99:99 x"), ("fixes", ""), ("fixes", "nope"), ("awakefor", ""), ("loadalert", ""), ("loadalert", "cpu 5"),
                ("prayer", ""), ("agenda", ""), ("shelf", ""), ("shelf", "999"), ("news", ""), ("privacy", ""), ("widget", "экранное"), ("widget", "обои"),
                // браузер: только просмотр и неверные аргументы (ничего не открывается и не нажимается)
                ("browser", ""), ("tabs", ""), ("links", ""), ("fav", ""), ("fav", "add"), ("fav", "del 99"), ("fav", "zzz"), ("tab", "zzz"), ("scroll", "zzz"),
                ("zoom", "zzz"), ("web", ""), ("incognito", ""), ("widget", "браузер"),
                ("pagefind", ""), ("typein", ""), ("pagelink", ""), ("video", "zzz"), ("closebrowser", ""),   // снимок, пересказ и «из буфера» — не здесь: трогают браузер   // /close, /minall, /volup… меняют ПК — их проверяет только пульт без разрешений ниже
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
            // живые виджеты из шагов выше сами обновляются в фоне — останавливаем, иначе их правки выглядят как «утечка» в Telegram
            await tg.TestCallbackAt("wg:!stop", 1);
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
                if (ms.Length > WidgetPolicy.CompressedLimit) Problems.Add($"widget {wk.Key}: html compressed {ms.Length} > 3900");
                try
                {
                    using var sd = JsonDocument.Parse(st);
                    // каждая кнопка и каждая строка с кнопкой должны вести к действию, которое этот вид умеет
                    if (sd.RootElement.TryGetProperty("k", out _) && !(wk.Key is "remote" or "pet"))
                    {
                        if (!TelegramHub.TestHasOps(wk.Key)) Problems.Add($"widget {wk.Key}: no action list (WOps)");
                        var qs = new List<string>();
                        if (sd.RootElement.TryGetProperty("b", out var bs))
                            foreach (var row in bs.EnumerateArray()) foreach (var b in row.EnumerateArray()) qs.Add(b[1].GetString());
                        if (sd.RootElement.TryGetProperty("r", out var rs))
                            foreach (var row in rs.EnumerateArray())
                                if (row[0].GetString() == "l" && row.GetArrayLength() > 2 && row[2].ValueKind == JsonValueKind.String) qs.Add(row[2].GetString());
                        foreach (var q in qs)
                        {
                            var head = q.Contains('|') ? q[..q.IndexOf('|')] : q;
                            if (!TelegramHub.TestKnownOp(wk.Key, head)) Problems.Add($"widget {wk.Key}: button '{q}' has no action");
                            if (Encoding.UTF8.GetByteCount("c:" + wk.Key + ":" + q) > 64) Problems.Add($"widget {wk.Key}: query '{q}' longer than 64 bytes");
                        }
                        widgetButtons += qs.Count;
                    }
                }
                catch (Exception e) { Problems.Add($"widget {wk.Key}: state not JSON ({e.GetType().Name})"); }
                var ans = await tg.TestWidgetAction("c:" + wk.Key + ":ref");
                if (!(wk.Key is "remote" or "pet") && WidgetTools.Find(wk.Key) == null && ans == null) Problems.Add($"widget {wk.Key}: ref gave no state");
                log.AppendLine($"widget {wk.Key,-10} html={raw.Length} zlib={ms.Length} state={sb}");
                var stub = "<script>window.__q=[];window.TelegramWidget={chatData:" + JsonSerializer.Serialize(st) + ",onEvent:function(){},copyText:function(){},request:function(q){__q.push(q);document.title=q;return Promise.resolve({result:null})}};</script>";
                File.WriteAllText(Path.Combine(wdir, wk.Key + ".html"), html.Replace("<head>", "<head>" + stub));
                index.Append($"<div class=w><span>{wk.Name}</span><iframe src=\"{wk.Key}.html\" width={wk.W} height={(wk.H == 0 ? 360 : wk.H)}></iframe></div>");
            }
            File.WriteAllText(Path.Combine(wdir, "index.html"), index.ToString());
            log.AppendLine($"widget buttons checked: {widgetButtons}");
            // список действий каждого вида — для проверки нажатий в браузере (widgets/ops.json)
            File.WriteAllText(Path.Combine(wdir, "ops.json"), JsonSerializer.Serialize(TelegramHub.TestKinds.ToDictionary(k => k.Key,
                k => new[] { "ref" }.Concat(TelegramHub.WOps.TryGetValue(k.Key, out var o) ? o : Array.Empty<string>()).ToArray())));
            // «Отвечать виджетами»: команды без аргументов уходят виджетом (Command возвращает null и вызывает sendWidget)
            tg.Cfg.Values["widgets_default"] = "1";
            // все команды из списка и все экраны меню должны уходить виджетом
            int cmdsOk = 0, menusOk = 0;
            // виджет «Питание» только показывает кнопки; без разрешения бот правильно отвечает отказом — для проверки разрешаем
            tg.Cfg.Values.TryGetValue("power", out var powerWas);
            tg.Cfg.Values["power"] = "1";
            foreach (var c in TelegramHub.TestDefaultCmds.ToList())
            {
                int before = WidgetSends;
                var r = await tg.TestCommand(c, "");
                if (r?.Html != null || WidgetSends == before) Problems.Add($"widgets_default: /{c} did not answer with a widget");
                else cmdsOk++;
            }
            foreach (var mn in TelegramHub.TestMenus.ToList())
            {
                int before = WidgetSends;
                var r = await tg.TestCallback("nav:" + mn);
                if (WidgetSends == before) Problems.Add($"widgets_default: menu nav:{mn} did not answer with a widget ({Short(r ?? "")})");
                else menusOk++;
            }
            log.AppendLine($"widgets_default: commands {cmdsOk}/{TelegramHub.TestDefaultCmds.Count()} · menus {menusOk}/{TelegramHub.TestMenus.Count()}");
            tg.Cfg.Values["power"] = powerWas ?? "0";
            if ((await tg.TestCommand("weather", "Kyiv"))?.Html == null && WidgetSends > 0) log.AppendLine("widgets_default: /weather with arg → message (ok if not null)");
            tg.Cfg.Values["widgets_default"] = "0";
            int b0 = WidgetSends;
            await tg.TestCommand("music", "");
            await tg.TestCallback("nav:music");
            if (WidgetSends != b0) Problems.Add("widgets_default off: /music or the music menu still sent a widget");
            log.AppendLine($"widgets_default: ok, sends={WidgetSends}");

            // Production 10.4: no widget forwarding/picker, a catalog button remains.
            lock (Sent) Sent.Clear();
            await tg.TestCommand("widget", "плеер");
            string sw;
            lock (Sent) sw = Sent.LastOrDefault(x => x.Method == "sendWidget").Json ?? "";
            if (sw.Contains("wg:fw") || !sw.Contains("wg:menu")) Problems.Add("production: widget must have catalog, not forward button");
            lock (Sent) Sent.Clear();
            var blockedForward = await tg.TestCallbackAt("wg:fw", 555);
            await tg.TestUpdate("{\"update_id\":1,\"message\":{\"message_id\":900,\"chat\":{\"id\":42,\"type\":\"private\"},\"chat_shared\":{\"request_id\":1,\"chat_id\":-1001234,\"title\":\"Test group\"}}}");
            lock (Sent) if (Sent.Any(x => x.Method == "forwardMessage" || x.Json.Contains("request_chat"))) Problems.Add("production: forwarding attempted");
            if (string.IsNullOrEmpty(blockedForward)) Problems.Add("production: legacy forward callback gave no explanation");
            log.AppendLine("production: forwarding denied");

            // кнопки плеера через бота и виджеты — только с поддельным плеером (tools/SmtcFake), иначе тест музыкой не управляет
            if (Environment.GetEnvironmentVariable("NISLAND_PLAYER_TEST") == "1")
            {
                for (int i = 0; i < 40 && !MediaService.I.HasSession; i++) await Task.Delay(250);
                log.AppendLine("player test: session " + MediaService.I.HasSession + " · " + MediaService.I.TrackLine);
                foreach (var (kind, q) in new[] { ("bot", "mu:play"), ("bot", "mu:play"), ("bot", "mu:next"), ("bot", "mu:prev"),
                                                  ("widget", "c:player:play"), ("widget", "c:player:play"), ("widget", "c:player:next"), ("widget", "c:player:prev"),
                                                  ("remote", "w:play"), ("remote", "w:play"), ("remote", "w:next"), ("remote", "w:prev") })
                {
                    var r = kind == "bot" ? await tg.TestCallback(q) : await tg.TestWidgetAction(q);
                    log.AppendLine($"player {DateTime.Now:HH:mm:ss.fff} {kind} {q} → {Short(r ?? "")}");
                    await Task.Delay(1300);
                }
            }

            // обработчик нажатий: ответ с состоянием + новое состояние в само сообщение; двойной тап — одно действие; чужой чат — ничего
            string WQ(string id, long chat, long msg, string data) =>
                "{\"update_id\":10,\"widget_query\":{\"id\":\"" + id + "\",\"from\":{\"id\":42},\"chat\":{\"id\":" + chat + ",\"type\":\"private\"},\"message_id\":" + msg + ",\"chat_instance\":\"1\",\"data\":\"" + data + "\"}}";
            lock (Sent) Sent.Clear();
            await tg.TestUpdate(WQ("wq1", 42, 700, "c:timer:ref"));
            await Task.Delay(400);
            List<(string Method, string Json)> got;
            lock (Sent) got = Sent.ToList();
            var tapAns = got.FirstOrDefault(x => x.Method == "answerWidgetQuery").Json ?? "";
            if (!tapAns.Contains("\"data\"")) Problems.Add("widget tap: answer without state");
            if (!got.Any(x => x.Method == "editMessageWidgetData" && x.Json.Contains("700"))) Problems.Add("widget tap: new state was not saved into the message");
            lock (Sent) Sent.Clear();
            await Task.WhenAll(tg.TestUpdate(WQ("wq2", 42, 701, "c:notes:in")), tg.TestUpdate(WQ("wq3", 42, 701, "c:notes:in")));
            await Task.Delay(400);
            int prompts;
            lock (Sent) prompts = Sent.Count(x => x.Method is "sendMessage" or "sendRichMessage" && Txt(x.Json).Contains("заметки"));
            if (prompts != 1) Problems.Add($"widget double tap: the action ran {prompts} times");
            lock (Sent) Sent.Clear();
            await tg.TestUpdate(WQ("wq4", 99, 702, "c:timer:toggle"));
            await Task.Delay(300);
            string ans4;
            lock (Sent) ans4 = Sent.FirstOrDefault(x => x.Method == "answerWidgetQuery").Json ?? "";
            if (ans4.Contains("\"data\"")) Problems.Add("widget tap from another chat was served");
            log.AppendLine($"widget taps: state saved, double tap → {prompts} action, stranger refused");
            lock (Sent) Sent.Clear();
            await tg.TestInline("{\"id\":\"iq1\",\"from\":{\"id\":42},\"query\":\"w player\"}");
            await tg.TestInline("{\"id\":\"iq2\",\"from\":{\"id\":42},\"query\":\"\"}");
            await tg.TestInline("{\"id\":\"iq3\",\"from\":{\"id\":99},\"query\":\"w player\"}");
            List<string> inl;
            lock (Sent) inl = Sent.Where(x => x.Method == "answerInlineQuery").Select(x => x.Json).ToList();
            if (inl.Count != 3) Problems.Add($"inline: expected 3 answers, got {inl.Count}");
            else
            {
                foreach (var answer in inl)
                {
                    using var doc = JsonDocument.Parse(answer);
                    if (doc.RootElement.GetProperty("results").GetArrayLength() != 0) Problems.Add("production: inline returned widgets");
                }
                log.AppendLine("production: inline widgets denied for owner and stranger");
            }


            // защитник через бота: меню с управлением, карантин, неизвестная кнопка не запускает проверку, тревога — с кнопками
            lock (Sent) Sent.Clear();
            var avm = await tg.TestCommand("defender", "");
            if (avm?.Html == null || !JsonSerializer.Serialize(avm.Value.Keys).Contains("av2:rt") || !JsonSerializer.Serialize(avm.Value.Keys).Contains("av2:ql"))
                Problems.Add("defender menu: no realtime / quarantine buttons");
            await tg.TestCallback("av2:ql");
            var stale = await tg.TestCallback("av2:zzz");
            bool scanStarted;
            lock (Sent) scanStarted = Sent.Any(x => Txt(x.Json).Contains("Быстрая проверка") || Txt(x.Json).Contains("1–3 мин"));
            if (scanStarted) Problems.Add("defender: an unknown av2 button started a scan");
            lock (Sent) Sent.Clear();
            var fake = new Threat(Path.Combine(Path.GetTempPath(), "ni-test-not-a-file.exe"), 85, "Опасно", new List<string> { "test reason" }, "00ff", false);
            if (!tg.ThreatAlert(fake)) Problems.Add("threat alert: not sent to Telegram");
            await Task.Delay(1500);
            string alertJson;
            lock (Sent) alertJson = Sent.Where(x => x.Method is "sendMessage" or "sendRichMessage").Select(x => x.Json).LastOrDefault() ?? "";
            if (!alertJson.Contains("av2:q:") || !alertJson.Contains("av2:ok:")) Problems.Add("threat alert: no quarantine / trust buttons");
            var qr = await tg.TestCallback(System.Text.RegularExpressions.Regex.Match(alertJson, @"av2:q:[A-Za-z0-9_-]+").Value);
            if (qr == null || !qr.Contains("уже нет")) Problems.Add("threat alert: quarantine of a missing file: " + Short(qr ?? "null"));
            {
                bool? onUi = null;
                System.ComponentModel.PropertyChangedEventHandler hh = (_, e) => { if (e.PropertyName == nameof(Settings.MascotHat)) onUi = System.Windows.Application.Current.Dispatcher.CheckAccess(); };
                Store.Settings.PropertyChanged += hh;
                var was = Store.Settings.MascotHat;
                await Task.Run(() => Store.Settings.MascotHat = was == "crown" ? "cap" : "crown");
                for (int i = 0; i < 20 && onUi == null; i++) await Task.Delay(50);
                await Task.Run(() => Store.Settings.MascotHat = was);
                await Task.Delay(300);
                Store.Settings.PropertyChanged -= hh;
                if (onUi != true) Problems.Add($"settings changed off the UI thread: handler ran on the UI thread = {onUi?.ToString() ?? "never"}");
                // команды, которые меняют настройки из потока бота (как /mascot), не должны падать
                foreach (var (c, a) in new[] { ("mascot", "crown"), ("mascot", was) })
                {
                    try { var r = await tg.TestCommand(c, a); if ((r?.Html ?? "").Contains("thread")) Problems.Add($"/{c} {a}: {Short(r?.Html)}"); }
                    catch (Exception e) { Problems.Add($"/{c} {a}: {e.GetType().Name}: {e.Message}"); }
                }
                await Task.Delay(300);
                log.AppendLine($"settings from a background thread: handler on UI = {onUi}");
            }
            log.AppendLine($"defender: menu ok, stale='{Short(stale ?? "")}', alert ok, quarantine of a missing file → '{Short(qr ?? "")}'");
            // персонаж собирается под скин, аксессуар и эмоцию — самый тяжёлый вариант тоже должен влезать в 4 КБ
            foreach (var pSk in new[] { "blob", "cat", "ghost", "robot", "bunny" })
                foreach (var ht in new[] { "auto", "none", "cap", "crown", "bow", "party", "santa", "headphones", "glasses", "flower" })
                    foreach (var pMd in new[] { "idle", "amazed", "sneeze", "headbang", "scared", "hot", "sick", "angry", "cool" })
                    {
                        var raw = Encoding.UTF8.GetBytes(TelegramHub.TestPetHtml(pSk, ht, pMd));
                        using var ms = new MemoryStream();
                        using (var z = new System.IO.Compression.ZLibStream(ms, System.IO.Compression.CompressionLevel.SmallestSize, true)) z.Write(raw);
                        if (ms.Length > WidgetPolicy.CompressedLimit) Problems.Add($"pet widget {pSk}/{ht}/{pMd}: compressed {ms.Length} > 3900");
                    }
            // «⟳» в каждом виджете: в карточках — в шапке, у виджетов без бота — своя кнопка перезагрузки
            foreach (var wk in TelegramHub.TestKinds)
            {
                var h = TelegramHub.TestHtml(wk.Key);
                if (!h.Contains("⟳") && !h.Contains("↺")) Problems.Add($"widget {wk.Key}: no refresh button");
            }
            if (!TelegramHub.TestHtml("player").Contains("D.pm")) Problems.Add("player widget: no mini mascot");
            // в общей карточке имена функций не должны совпадать с переменными (так «E» затирала рисование персонажа — его не было видно)
            {
                var card = TelegramHub.TestHtml("player");
                foreach (System.Text.RegularExpressions.Match fm in System.Text.RegularExpressions.Regex.Matches(card, @"function (\w+)\("))
                    if (System.Text.RegularExpressions.Regex.IsMatch(card, @"(var |,)" + fm.Groups[1].Value + "="))
                        Problems.Add($"card widget js: function {fm.Groups[1].Value} is overwritten by a variable");
                if (card.Contains("requestAnimationFrame")) Problems.Add("card widget js: requestAnimationFrame does not run in the widget viewer — use a timer");
            }
            // синхронизация с ПК: событие → правка виджета и пульта без нажатий; второй раз то же состояние — без лишних запросов
            {
                tg.TestRememberWidget(9001, "player");
                tg.TestTrackMenu("music", 9002);
                Sent.Clear();
                await tg.TestFlush(new[] { "player" }, new[] { "music" });
                string first; lock (Sent) first = Sent.LastOrDefault(s => s.Method == "editMessageWidgetData" && s.Json.Contains("\"message_id\":9001")).Json;
                int wEdits, mEdits;
                lock (Sent) { wEdits = Sent.Count(s => s.Method == "editMessageWidgetData" && s.Json.Contains("9001")); mEdits = Sent.Count(s => s.Method == "editMessageText" && s.Json.Contains("9002")); }
                if (wEdits != 1) Problems.Add($"pc sync: player widget edits {wEdits} (want 1)");
                if (mEdits != 1) Problems.Add($"pc sync: music menu edits {mEdits} (want 1)");
                Sent.Clear();
                await tg.TestFlush(new[] { "player" }, new[] { "music" });
                // считаем только правки своих сообщений: живые виджеты, отправленные раньше в самотесте, в это время обновляются сами
                lock (Sent)
                {
                    wEdits = Sent.Count(s => s.Method == "editMessageWidgetData" && s.Json.Contains("\"message_id\":9001"));
                    mEdits = Sent.Count(s => s.Method == "editMessageText" && s.Json.Contains("\"message_id\":9002"));
                }
                string second; lock (Sent) second = Sent.LastOrDefault(s => s.Method == "editMessageWidgetData" && s.Json.Contains("\"message_id\":9001")).Json;
                // повторная правка — ошибка, только если состояние то же самое (за 2,5 с на ПК могло что-то правда поменяться: заряд, громкость)
                if (mEdits != 0 || (wEdits != 0 && second == first)) Problems.Add($"pc sync: unchanged state still edited ({wEdits}+{mEdits})");
                else if (wEdits != 0) log.AppendLine("pc sync: state changed between checks — re-edit is right");
                log.AppendLine("pc sync: ok");
            }
            // ссылки из чата: что считается ссылкой и когда сообщение — «только ссылки»
            {
                var l = TelegramHub.TestFindLinks("смотри youtube.com/watch?v=1 и https://x.org/a, ещё www.site.ru и file.txt, mail@example.com");
                var want = new[] { "https://youtube.com/watch?v=1", "https://x.org/a", "https://www.site.ru/" };
                if (!l.SequenceEqual(want)) Problems.Add("links: found [" + string.Join(" ", l) + "]");
                if (!TelegramHub.TestOnlyLinks("https://a.com/x")) Problems.Add("links: bare link not 'only links'");
                if (!TelegramHub.TestOnlyLinks("youtube.com  github.com/x")) Problems.Add("links: two links not 'only links'");
                if (TelegramHub.TestOnlyLinks("привет https://a.com")) Problems.Add("links: text with link counted as 'only links'");
                if (TelegramHub.TestFindLinks("просто текст. и точка").Count != 0) Problems.Add("links: plain text has links");
                log.AppendLine("links: ok");
            }
            // каталог и переходы на месте: категория → виджет → «⬅ Назад», всё в том же сообщении (7), без новых сообщений
            {
                Sent.Clear();
                string Kind(string st) { try { using var d = JsonDocument.Parse(st); return d.RootElement.GetProperty("k").GetString(); } catch { return null; } }
                bool HasNav(string st) => st != null && st.Contains("\"nav\"");
                var s1 = await tg.TestWidgetAction("c:menu:cat|0");
                var s2 = await tg.TestWidgetAction("c:menu:go|timer");
                var s3 = await tg.TestWidgetAction("c:timer:open|stopwatch");
                var s4 = await tg.TestWidgetAction("c:stopwatch:nav");
                var s5 = await tg.TestWidgetAction("c:timer:nav");
                var s6 = await tg.TestWidgetAction("c:menu:nav");
                int sends; lock (Sent) sends = Sent.Count(s => s.Method == "sendWidget");
                if (Kind(s1) != "menu" || !s1.Contains("go|")) Problems.Add("catalog: category view: " + Short(s1));
                if (Kind(s2) != "timer" || !HasNav(s2)) Problems.Add("catalog: go → timer in place: " + Short(s2));
                if (Kind(s3) != "stopwatch" || !HasNav(s3)) Problems.Add("open → stopwatch in place: " + Short(s3));
                if (Kind(s4) != "timer") Problems.Add("nav back → timer: " + Short(s4));
                if (Kind(s5) != "menu") Problems.Add("nav back → catalog: " + Short(s5));
                if (Kind(s6) != "menu" || s6.Contains("go|")) Problems.Add("nav → categories: " + Short(s6));
                if (sends != 0) Problems.Add($"catalog: {sends} new widget message(s) instead of updating in place");
                foreach (var st in new[] { s1, s2, s3, s4, s5, s6 }) if (st != null && Encoding.UTF8.GetByteCount(st) > 1024) Problems.Add("catalog: state > 1024 bytes");
                // меню /widgets: категория листается в том же сообщении, второй выбор обновляет виджет выше
                tg.TestForgetMenuWidget();
                var c0 = await tg.TestCallbackAt("wg:@c:0", 55);
                lock (Sent) Sent.Clear();
                await tg.TestCallbackAt("wg:timer", 55);
                await tg.TestCallbackAt("wg:stopwatch", 55);
                int sends2, morph2;
                lock (Sent) { sends2 = Sent.Count(s => s.Method == "sendWidget"); morph2 = Sent.Count(s => s.Method == "editMessageWidgetData" && s.Json.Contains("stopwatch")); }
                if (sends2 != 1 || morph2 < 1) Problems.Add($"widgets menu: second pick should update the widget above (sent {sends2}, updated {morph2})");
                log.AppendLine($"catalog: ok · menu sends {sends2}, updates {morph2}");
            }
            // уведомления виджетом: важное — новым виджетом, тихое — правкой того же; прежний такой же виджет удаляется
            {
                lock (Sent) Sent.Clear();
                tg.TestRememberSent(8001, "events");
                bool ok1 = await tg.TestPushEvent("limit", "Лимит теста", false);
                await Task.Delay(300);
                int sendsE, delOld;
                lock (Sent) { sendsE = Sent.Count(s => s.Method == "sendWidget" && s.Json.Contains("events")); delOld = Sent.Count(s => s.Method == "deleteMessage" && s.Json.Contains("8001")); }
                if (!ok1 || sendsE != 1) Problems.Add($"events: loud event should send one widget (ok={ok1}, sent {sendsE})");
                if (delOld != 1) Problems.Add($"events: the previous events widget was not deleted ({delOld})");
                lock (Sent) Sent.Clear();
                bool ok2 = await tg.TestPushEvent("track", "Трек теста", true);
                int sendsS, editsS;
                lock (Sent) { sendsS = Sent.Count(s => s.Method == "sendWidget"); editsS = Sent.Count(s => s.Method == "editMessageWidgetData" && s.Json.Contains("events")); }
                if (!ok2 || sendsS != 0 || editsS != 1) Problems.Add($"events: quiet event should edit the same widget (sent {sendsS}, edited {editsS})");
                lock (Sent) Sent.Clear();
                await tg.TestPushPcText("⏰ <b>Подъём теста</b> · 07:30");
                string st; lock (Sent) st = Sent.LastOrDefault(s => s.Method == "sendWidget").Json ?? "";
                if (!st.Contains("snooze")) Problems.Add("events: alarm event has no snooze button");
                var ev = tg.TestWState("events");
                if (Encoding.UTF8.GetByteCount(ev) > 1024) Problems.Add("events: state > 1024 bytes");
                log.AppendLine($"events: loud {sendsE}, deleted old {delOld}, quiet edits {editsS}");
            }
            foreach (var q in new[] { "w:ref", "w:zzz", "p:poke", "p:love", "p:ref" })
            {
                var res = await tg.TestWidgetAction(q);
                if (res == null) { Problems.Add($"widget {q}: no state"); continue; }
                if (Encoding.UTF8.GetByteCount(res) > 1024) Problems.Add($"widget {q}: state {Encoding.UTF8.GetByteCount(res)} > 1024 bytes");
                try { JsonDocument.Parse(res).Dispose(); } catch { Problems.Add($"widget {q}: not JSON: {Short(res)}"); }
                log.AppendLine($"widget {q} → {Short(res)}");
            }
            TelegramHub.TestSink = null;
        }

        /// <summary>Текст сообщения из тела запроса (в JSON кириллица экранирована — искать по нему нельзя).</summary>
        static string Txt(string json)
        {
            try
            {
                using var d = JsonDocument.Parse(json);
                if (d.RootElement.TryGetProperty("text", out var t)) return t.GetString() ?? "";
                if (d.RootElement.TryGetProperty("rich_message", out var rm))
                    return rm.TryGetProperty("html", out var rh) ? rh.GetString() ?? "" : rm.TryGetProperty("markdown", out var rmd) ? rmd.GetString() ?? "" : "";
            }
            catch { }
            return "";
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
            // ошибка «объект принадлежит другому потоку» — значит, код бота трогает интерфейс не из его потока
            if (json.Contains("different thread owns it") || json.Contains("другой поток")) Problems.Add($"{method}: cross-thread: {Short(json)}");
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
            if (method == "answerInlineQuery")
            {
                foreach (var it in r.GetProperty("results").EnumerateArray())
                {
                    var c = it.GetProperty("input_message_content");
                    var html = c.GetProperty("html").GetString() ?? "";
                    var raw = Encoding.UTF8.GetBytes(html);
                    using var ms = new System.IO.MemoryStream();
                    using (var z = new System.IO.Compression.ZLibStream(ms, System.IO.Compression.CompressionLevel.SmallestSize, true)) z.Write(raw);
                    if (ms.Length > WidgetPolicy.CompressedLimit) Problems.Add($"inline {it.GetProperty("id")}: html compressed {ms.Length} > 3900");
                    var cd = c.TryGetProperty("chat_data", out var cdv) ? cdv.GetString() ?? "" : "";
                    if (Encoding.UTF8.GetByteCount(cd) > 1024) Problems.Add($"inline {it.GetProperty("id")}: chat_data > 1024 bytes");
                    if (c.TryGetProperty("allow_queries", out var aq) && aq.ValueKind == JsonValueKind.True) Problems.Add($"inline {it.GetProperty("id")}: a forwarded widget must not send queries");
                    if (cd.StartsWith("{\"ro\":1,") is false && cd.StartsWith("{\"k\":")) Problems.Add($"inline {it.GetProperty("id")}: card is not read-only");
                }
            }
            if (method == "sendWidget")
            {
                System.Threading.Interlocked.Increment(ref WidgetSends);
                var html = r.TryGetProperty("html", out var wh) ? wh.GetString() ?? "" : "";
                var raw = Encoding.UTF8.GetBytes(html);
                using var ms = new System.IO.MemoryStream();
                using (var z = new System.IO.Compression.ZLibStream(ms, System.IO.Compression.CompressionLevel.SmallestSize, true)) z.Write(raw);
                if (raw.Length > 32768) Problems.Add($"sendWidget: html {raw.Length} > 32768");
                if (ms.Length > WidgetPolicy.CompressedLimit) Problems.Add($"sendWidget: compressed {ms.Length} > 3900 (limit 4096)");
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
                        if (!b.TryGetProperty("callback_data", out _) && !b.TryGetProperty("url", out _) && !b.TryGetProperty("copy_text", out _) && !b.TryGetProperty("switch_inline_query_chosen_chat", out _)) Problems.Add($"{method}: button without action");
                        if (b.TryGetProperty("switch_inline_query_chosen_chat", out var sq) && !(sq.TryGetProperty("query", out var qq) && qq.GetString() is { Length: > 0 } && sq.TryGetProperty("allow_user_chats", out _))) Problems.Add($"{method}: bad chat-picker button");
                        if (b.TryGetProperty("url", out var bu) && !Regex.IsMatch(bu.GetString() ?? "", @"^(https?|tg)://\S+$")) Problems.Add($"{method}: bad button url: {bu.GetString()}");
                        if (b.TryGetProperty("style", out var bs) && bs.GetString() is not ("danger" or "success" or "primary")) Problems.Add($"{method}: bad button style {bs}");
                        if (b.TryGetProperty("copy_text", out var ctx) && (ctx.ValueKind != JsonValueKind.Object || (ctx.GetProperty("text").GetString()?.Length ?? 0) is 0 or > 256)) Problems.Add($"{method}: bad copy_text");
                    }
                }
            }
        }
    }
}
