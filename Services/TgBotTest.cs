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
        };
        // эти кнопки безопасно нажимать: только показывают
        static readonly string[] SafePrefixes = { "nav:", "d:", "dp:", "f:", "w:", "aimodels:", "status!", "do:sysinfo", "do:disk", "do:lyrics", "do:notes", "do:recent", "ask:ai", "ask:search", "ask:type", "ask:copy", "ask:say", "ask:todo" };

        public static int Run(string output)
        {
            var sb = new StringBuilder();
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
            int pressed = 0;
            while (queue.Count > 0 && pressed < 400)
            {
                var data = queue.Dequeue();
                if (!seen.Add(data)) continue;
                var head = data.Split('|')[0];
                head = head.Contains(':') ? head[..head.IndexOf(':')] : head;
                if (!Known.Contains(head)) { Problems.Add("unknown button: " + data); continue; }
                if (!SafePrefixes.Any(p => data.StartsWith(p))) continue;
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
            TelegramHub.TestSink = null;
        }

        static string LastText()
        {
            lock (Sent)
                for (int i = Sent.Count - 1; i >= 0; i--)
                {
                    using var d = JsonDocument.Parse(Sent[i].Json);
                    if (d.RootElement.TryGetProperty("text", out var t)) return t.GetString();
                }
            return "";
        }

        static string Short(string s) => s == null ? "" : Regex.Replace(s.Length > 110 ? s[..110] + "…" : s, @"\s+", " ");

        static void Collect(string json, Queue<string> q)
        {
            foreach (Match m in Regex.Matches(json ?? "", "\"callback_data\":\"((?:[^\"\\\\]|\\\\.)*)\""))
                q.Enqueue(JsonDocument.Parse("\"" + m.Groups[1].Value + "\"").RootElement.GetString());
        }

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
                    }
                }
            }
        }
    }
}
