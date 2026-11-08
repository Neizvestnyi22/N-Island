using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NIsland.Core;

namespace NIsland.Services
{
    /// <summary>
    /// Перевод строк песни. Бесплатный Google часто отвечает 429 — тогда пробуем второй адрес Google, MyMemory и, в конце, ИИ острова.
    /// Результат запоминается на трек и язык, чтобы переключение туда-обратно не тратило лимиты.
    /// </summary>
    public static class LyricsTranslate
    {
        static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };
        static readonly Dictionary<string, List<string>> Cache = new();

        /// <summary>Языки перевода в плеере (код Google → название на своём языке).</summary>
        public static readonly (string Code, string Name)[] Langs =
        {
            ("ru", "Русский"), ("uk", "Українська"), ("en", "English"), ("de", "Deutsch"), ("es", "Español"), ("fr", "Français"),
            ("it", "Italiano"), ("pl", "Polski"), ("pt", "Português"), ("tr", "Türkçe"), ("ja", "日本語"), ("ko", "한국어"), ("zh-CN", "中文"),
        };

        public static string Name(string code) => Langs.FirstOrDefault(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase)).Name ?? code;

        /// <summary>Строки уже на этом языке — перевод не нужен.</summary>
        public sealed class SameLanguageException : Exception { public SameLanguageException() : base("same language") { } }

        /// <summary>Перевод построчно: столько же строк, сколько на входе. Бросает исключение с понятным текстом, если не сработал ни один сервис.</summary>
        public static async Task<(List<string> Lines, string Via)> Run(string trackKey, IReadOnlyList<string> lines, string to, CancellationToken ct = default)
        {
            string ck = trackKey + "|" + to;
            lock (Cache) if (Cache.TryGetValue(ck, out var hit)) return (hit, "cache");
            var src = Guess(lines);
            if (src != null && Same(src, to)) throw new SameLanguageException();

            var errors = new List<string>();
            var tries = new (string Name, Func<Task<(List<string>, string)>> Run)[]
            {
                ("Google", () => GoogleGtx(lines, to, ct)),
                ("Google 2", () => GoogleDict(lines, to, ct)),
                ("MyMemory", () => MyMemory(lines, src ?? "en", to, ct)),
                ("ИИ", () => ViaAi(lines, to, ct)),
            };
            foreach (var (name, run) in tries)
            {
                if (ct.IsCancellationRequested) break;
                try
                {
                    var (res, from) = await run();
                    if (!string.IsNullOrEmpty(from) && Same(from, to)) throw new SameLanguageException();
                    if (res != null && res.Count == lines.Count && res.Any(x => !string.IsNullOrWhiteSpace(x)))
                    {
                        lock (Cache) { if (Cache.Count > 40) Cache.Clear(); Cache[ck] = res; }
                        return (res, name);
                    }
                    errors.Add(name + ": " + (res == null ? "пусто" : $"строк {res.Count} из {lines.Count}"));
                }
                catch (SameLanguageException) { throw; }
                catch (Exception e) { errors.Add(name + ": " + Short(e)); }
            }
            Log.Info("lyrics translate failed: " + string.Join(" | ", errors));
            throw new InvalidOperationException(errors.Any(e => e.Contains("429")) ? "Переводчики сейчас перегружены (429) — попробуй через минуту" : "Не удалось перевести: " + string.Join("; ", errors.Take(2)));
        }

        static bool Same(string a, string b) => string.Equals(a.Split('-')[0], b.Split('-')[0], StringComparison.OrdinalIgnoreCase);

        static string Short(Exception e) => e is HttpRequestException h && h.StatusCode != null ? ((int)h.StatusCode).ToString() : e is TaskCanceledException ? "нет ответа" : e.Message.Length > 60 ? e.Message[..60] : e.Message;

        /// <summary>Язык строк по алфавиту — для MyMemory (ему нужен исходный язык) и чтобы не переводить русское на русский.</summary>
        public static string Guess(IReadOnlyList<string> lines)
        {
            var t = string.Join(" ", lines);
            int cyr = 0, lat = 0, hangul = 0, kana = 0, han = 0, uk = 0;
            foreach (var ch in t)
            {
                if (ch is >= 'А' and <= 'я' or 'ё' or 'Ё') cyr++;
                else if (ch is 'і' or 'ї' or 'є' or 'ґ' or 'І' or 'Ї' or 'Є' or 'Ґ') { cyr++; uk++; }
                else if (ch is >= 'a' and <= 'z' or >= 'A' and <= 'Z') lat++;
                else if (ch >= '가' && ch <= '힯') hangul++;
                else if (ch >= '぀' && ch <= 'ヿ') kana++;
                else if (ch >= '一' && ch <= '鿿') han++;
            }
            int max = new[] { cyr, lat, hangul, kana, han }.Max();
            if (max < 3) return null;
            if (max == cyr) return uk > 2 ? "uk" : "ru";
            if (max == hangul) return "ko";
            if (max == kana || (max == han && kana > 0)) return "ja";
            if (max == han) return "zh-CN";
            return "en";
        }

        static string Join(IReadOnlyList<string> lines) => string.Join("\n", lines.Select(l => string.IsNullOrWhiteSpace(l) || l == "♪" ? "." : l.Trim()));

        static List<string> Split(string text, IReadOnlyList<string> src)
        {
            var parts = text.Replace("\r", "").Split('\n');
            if (parts.Length != src.Count) return parts.ToList();   // вызывающий отбросит: строки съехали бы
            return parts.Select((p, i) => src[i] == "♪" || string.IsNullOrWhiteSpace(src[i]) ? "" : p.Trim()).ToList();
        }

        // ---------- Google (основной адрес) ----------
        static async Task<(List<string>, string)> GoogleGtx(IReadOnlyList<string> lines, string to, CancellationToken ct)
        {
            var text = Join(lines);
            if (text.Length > 4500) text = text[..4500];
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&dt=t&tl=" + Uri.EscapeDataString(to))
            { Content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("q", text) }) };
            using var resp = await Http.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            var sb = new StringBuilder();
            foreach (var seg in doc.RootElement[0].EnumerateArray())
                if (seg.ValueKind == JsonValueKind.Array && seg[0].ValueKind == JsonValueKind.String) sb.Append(seg[0].GetString());
            string from = doc.RootElement.GetArrayLength() > 2 && doc.RootElement[2].ValueKind == JsonValueKind.String ? doc.RootElement[2].GetString() : "";
            return (Split(sb.ToString(), lines), from);
        }

        // ---------- Google (адрес расширения Chrome, отдельный лимит) ----------
        static async Task<(List<string>, string)> GoogleDict(IReadOnlyList<string> lines, string to, CancellationToken ct)
        {
            var text = Join(lines);
            if (text.Length > 4500) text = text[..4500];
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://clients5.google.com/translate_a/t?client=dict-chrome-ex&sl=auto&tl=" + Uri.EscapeDataString(to))
            { Content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("q", text) }) };
            using var resp = await Http.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            // ответ бывает ["перевод"], [["перевод","en"]] или {"sentences":[{"trans":…}],"src":"en"}
            string res = null, from = "";
            var r = doc.RootElement;
            if (r.ValueKind == JsonValueKind.Array && r.GetArrayLength() > 0)
            {
                var first = r[0];
                if (first.ValueKind == JsonValueKind.String) res = first.GetString();
                else if (first.ValueKind == JsonValueKind.Array && first.GetArrayLength() > 0)
                {
                    res = first[0].GetString();
                    if (first.GetArrayLength() > 1 && first[1].ValueKind == JsonValueKind.String) from = first[1].GetString();
                }
            }
            else if (r.ValueKind == JsonValueKind.Object && r.TryGetProperty("sentences", out var sents))
            {
                res = string.Concat(sents.EnumerateArray().Select(s => s.TryGetProperty("trans", out var t) ? t.GetString() : ""));
                if (r.TryGetProperty("src", out var src)) from = src.GetString();
            }
            return (res == null ? null : Split(res, lines), from);
        }

        // ---------- MyMemory (бесплатно, ~5000 символов в день без ключа) ----------
        static async Task<(List<string>, string)> MyMemory(IReadOnlyList<string> lines, string from, string to, CancellationToken ct)
        {
            var result = new List<string>();
            // кусками до ~450 символов, строки внутри куска через перевод строки
            var chunk = new List<string>();
            int len = 0;
            async Task Flush()
            {
                if (chunk.Count == 0) return;
                var url = "https://api.mymemory.translated.net/get?langpair=" + Uri.EscapeDataString(from.Split('-')[0] + "|" + to.Split('-')[0]) + "&q=" + Uri.EscapeDataString(string.Join("\n", chunk));
                using var doc = JsonDocument.Parse(await Http.GetStringAsync(url, ct));
                var status = doc.RootElement.TryGetProperty("responseStatus", out var st) ? st.ToString() : "200";
                if (status != "200") throw new HttpRequestException(status == "429" ? "429" : "MyMemory " + status);
                var tr = doc.RootElement.GetProperty("responseData").GetProperty("translatedText").GetString() ?? "";
                var parts = tr.Replace("\r", "").Split('\n');
                if (parts.Length != chunk.Count) throw new InvalidOperationException("строки съехали");
                result.AddRange(parts.Select(p => p.Trim()));
                chunk.Clear(); len = 0;
            }
            foreach (var l in lines)
            {
                var t = string.IsNullOrWhiteSpace(l) || l == "♪" ? "." : l.Trim();
                if (len + t.Length > 450) await Flush();
                chunk.Add(t); len += t.Length + 1;
            }
            await Flush();
            return (result.Select((p, i) => lines[i] == "♪" || string.IsNullOrWhiteSpace(lines[i]) ? "" : p).ToList(), from);
        }

        // ---------- ИИ острова (последний вариант) ----------
        static async Task<(List<string>, string)> ViaAi(IReadOnlyList<string> lines, string to, CancellationToken ct)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Translate the song lyrics below into {Name(to)} ({to}). Keep the meaning and the mood, do not rhyme on purpose.");
            sb.AppendLine("Answer ONLY with the same numbered lines in the format «N|translation», one per line, nothing else.");
            for (int i = 0; i < lines.Count; i++) sb.AppendLine($"{i + 1}|{(lines[i] == "♪" ? "." : lines[i])}");
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(60));
            var ans = await Ai.Ask(new[] { new AiMessage("user", sb.ToString()) }, null, cts.Token, channel: "lyrics");
            var map = new Dictionary<int, string>();
            foreach (Match m in Regex.Matches(ans ?? "", @"^\s*(\d+)\s*[|:.)]\s*(.*)$", RegexOptions.Multiline))
                if (int.TryParse(m.Groups[1].Value, out int n)) map[n] = m.Groups[2].Value.Trim();
            if (map.Count < lines.Count * 0.7) return (null, "");
            return (lines.Select((l, i) => l == "♪" ? "" : map.TryGetValue(i + 1, out var v) ? v : "").ToList(), "");
        }
    }
}
