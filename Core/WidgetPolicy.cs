using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;

namespace NIsland.Core
{
    /// <summary>Profile-1 budgets shared by sending, state updates and the portable audit.</summary>
    public static class WidgetPolicy
    {
        public const int HtmlLimit = 32768, CompressedLimit = 4096, StateLimit = 1024, QueryLimit = 64;
        public static bool ValidQuery(string s) => s != null && !s.Contains('\0') && Encoding.UTF8.GetByteCount(s) is >= 1 and <= QueryLimit;
        public static bool ValidData(string s) => s != null && !s.Contains('\0') && Encoding.UTF8.GetByteCount(s) <= StateLimit;
        public static int CompressedBytes(string html)
        {
            using var ms = new MemoryStream();
            using (var z = new ZLibStream(ms, CompressionLevel.SmallestSize, true)) z.Write(Encoding.UTF8.GetBytes(html));
            return (int)ms.Length;
        }
        public static void ValidateHtml(string html)
        {
            if (html == null || html.Contains('\0') || Encoding.UTF8.GetByteCount(html) > HtmlLimit || CompressedBytes(html) > CompressedLimit)
                throw new ArgumentException("Widget HTML exceeds the profile-1 budget or contains NUL");
        }
        // Row trimming alone is insufficient: a menu can exceed 1 KB with buttons but no rows.
        public static string FitState(string text, int budget = 1000)
        {
            if (budget < 128 || budget > StateLimit) throw new ArgumentOutOfRangeException(nameof(budget));
            var n = JsonNode.Parse(text) as JsonObject ?? throw new ArgumentException("Object state expected");
            string Serialize() => n.ToJsonString(new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            var s = Serialize();
            while (Encoding.UTF8.GetByteCount(s) > budget)
            {
                if (n["r"] is JsonArray rows && rows.Count > 1) rows.RemoveAt(rows.Count - 1);
                else if (Shorten(n)) { }
                else if (n["r"] is JsonArray last && last.Count > 0) last.RemoveAt(0);
                else if (n["b"] is JsonArray buttons && buttons.Count > 1) buttons.RemoveAt(buttons.Count - 1);
                else throw new ArgumentException("Widget state cannot be safely fitted");
                s = Serialize();
            }
            if (!ValidData(s)) throw new ArgumentException("Invalid widget state");
            return s;
        }
        static bool Shorten(JsonNode node)
        {
            // Never truncate protocol keys/operations or widget ids. Only human-readable strings.
            if (node is JsonObject o)
            {
                foreach (var k in new[] { "e", "s", "h" })
                    if (o[k] is JsonValue v && v.TryGetValue<string>(out var s) && s.Length > 32)
                    { o[k] = Prefix(s, Math.Max(16, s.Length / 2)) + "…"; return true; }
                if (o["r"] is JsonArray rows)
                    foreach (var row in rows.OfType<JsonArray>())
                        for (int i = 1; i < row.Count; i++)
                            if (row[i] is JsonValue rv && rv.TryGetValue<string>(out var rs) && rs.Length > 32 && (i == 1 || (row[0]?.ToString() is "t" or "T" or "k" or "y")))
                            { row[i] = Prefix(rs, Math.Max(24, rs.Length / 2)) + "…"; return true; }
            }
            return false;
        }
        static string Prefix(string s, int length) => s[..(length > 0 && char.IsHighSurrogate(s[length - 1]) ? length - 1 : length)];

        public static string RefreshButton(string html, string position)
        {
            string refresh = html.Contains("function ev(s)") ? "e='';$('s').textContent='';$('v').textContent='0'"
                : html.Contains("function nw()") ? "nw()"
                : html.Contains("function set(t,name)") ? "tick()"
                : html.Contains("function step()") ? "on=0;clearInterval(tm);$('o').textContent='▶';$('o').style.transform='scale(.55)'"
                : html.Contains("function r(n)") ? "document.getElementById('v').textContent='🎲';document.getElementById('h').textContent=''"
                : html.Contains("Z={}") ? "load(T?(T.chatData||T.publicData):'{}')"
                : html.Contains("requestAnimationFrame(f)") ? "load(T?(T.chatData||T.publicData):'{}')"
                : "draw()";
            return html.Replace("</body>", "<button onclick=\"" + refresh + "\" style=\"position:fixed;" + position + ";border:0;border-radius:8px;background:#8882;color:inherit;padding:2px 8px;font-size:13px;flex:none\">⟳</button></body>");
        }

        public static string ProfileHtml(string html)
        {
            // Applied to every template: timestamp inset, wrapping and motion preference.
            const string css = "<style>body{padding-bottom:max(12px,env(safe-area-inset-bottom))}button.w{opacity:1;color:var(--tg-theme-hint-color,#888)}.h,.s,.t,.k{overflow-wrap:anywhere}.o{opacity:1;color:var(--tg-theme-hint-color,#888)}@media(prefers-reduced-motion:reduce){*{animation:none!important;transition:none!important}}</style>";
            return html.Replace("</head>", css + "</head>");
        }
    }
}
