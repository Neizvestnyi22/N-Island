using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NIsland.Core;

namespace NIsland.Services
{
    public record LyricLine(TimeSpan Time, string Text);

    public class LyricsResult
    {
        public List<LyricLine> Lines;
        public bool Synced;
        public string Source;
    }

    /// <summary>Вариант текста для ручного выбора («не тот текст»).</summary>
    public record LyricsCandidate(string Track, string Artist, string Album, double Duration, bool Synced, LyricsResult Result);

    /// <summary>Синхронные тексты песен из открытой базы LRCLIB (без ключей) + кеш на диске.</summary>
    public static class LyricsService
    {
        static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
        static readonly Dictionary<string, LyricsResult> Cache = new();
        static readonly Regex Tag = new(@"\[(\d{1,3}):(\d{1,2}(?:[.:]\d{1,3})?)\]", RegexOptions.Compiled);
        static readonly Regex WordTag = new(@"<\d{1,3}:\d{1,2}(?:[.:]\d{1,3})?>", RegexOptions.Compiled);
        static readonly Regex Junk = new(@"\s*[\(\[][^\)\]]*(official|video|lyric|audio|clip|music video|mv|hd|4k|remaster|visualizer|live|премьера|клип)[^\)\]]*[\)\]]",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly string CacheDir = Path.Combine(Store.Dir, "lyrics");

        static LyricsService()
        {
            Http.DefaultRequestHeaders.UserAgent.ParseAdd("NIsland/1.4 (Windows notch app)");
        }

        public static async Task<LyricsResult> Get(string title, string artist, string album, TimeSpan duration)
        {
            (title, artist) = Clean(title, artist);
            if (title == "") return null;
            string key = (title + "|" + artist).ToLowerInvariant();
            if (Cache.TryGetValue(key, out var hit)) return hit;
            var disk = ReadDisk(key);
            if (disk != null) { Cache[key] = disk; return disk; }

            // одна и та же песня часто запрашивается дважды подряд (сначала приходит название, потом исполнитель) — один запрос на всех
            if (Inflight.TryGetValue(key, out var running)) return await running;
            var task = Load(key, title, artist, duration);
            Inflight[key] = task;
            try { return await task; }
            finally { Inflight.TryRemove(key, out _); }
        }

        static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Task<LyricsResult>> Inflight = new();

        static async Task<LyricsResult> Load(string key, string title, string artist, TimeSpan duration)
        {
            LyricsResult res = null;
            bool failed = false;
            for (int attempt = 0; ; attempt++)
            {
                try { res = await Fetch(title, artist, duration); break; }
                catch (Exception e)
                {
                    // LRCLIB иногда отвечает 503 — пробуем ещё раз с паузой, потом идём в запасной источник
                    if (attempt >= 2) { Log.Info("lyrics: " + e.Message); failed = true; break; }
                    await Task.Delay(attempt == 0 ? 1200 : 3500);
                }
            }
            if ((res == null || !res.Synced) && Store.Settings.LyricsFallback)
            {
                try { res = await FetchNetEase(title, artist, duration) ?? res; }
                catch (Exception e) { Log.Info("lyrics netease: " + e.Message); }
            }
            if (res == null && failed) return null;   // сеть подвела — не запоминаем «текста нет», попробуем в следующий раз
            Cache[key] = res;
            if (res != null) WriteDisk(key, res);
            return res;
        }

        static readonly Regex Credits = new(@"^\s*(作词|作曲|编曲|制作人|混音|母带|录音|和声|吉他|贝斯|鼓|弦乐|监制|出品|Produced|Producer|Composed|Composer|Lyrics|Lyricist|Written|Arranged)\b?\s*[:：]",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static string Norm(string s) => new string((s ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

        static async Task<JsonDocument> NetEase(string url)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Referrer = new Uri("https://music.163.com/");
            using var resp = await Http.SendAsync(req);
            resp.EnsureSuccessStatusCode();
            return JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        }

        /// <summary>Запасной источник синхронных текстов — открытый поиск NetEase Cloud Music (без ключей).
        /// Берём только трек с тем же названием и похожей длительностью, служебные строки «作词 / 作曲» убираем.</summary>
        static async Task<LyricsResult> FetchNetEase(string title, string artist, TimeSpan duration)
        {
            using var doc = await NetEase("https://music.163.com/api/search/get?type=1&limit=10&s=" + Uri.EscapeDataString((title + " " + artist).Trim()));
            if (!doc.RootElement.TryGetProperty("result", out var r) || !r.TryGetProperty("songs", out var songs) || songs.ValueKind != JsonValueKind.Array) return null;
            string nt = Norm(title), na = Norm(artist);
            long bestId = 0;
            double best = double.MaxValue;
            foreach (var s in songs.EnumerateArray())
            {
                var name = Norm(s.TryGetProperty("name", out var n) ? n.GetString() : "");
                if (name == "" || nt == "" || !(name == nt || name.StartsWith(nt) || nt.StartsWith(name))) continue;
                var artists = s.TryGetProperty("artists", out var a) && a.ValueKind == JsonValueKind.Array
                    ? string.Join("", a.EnumerateArray().Select(x => Norm(x.TryGetProperty("name", out var an) ? an.GetString() : ""))) : "";
                double score = na != "" && !artists.Contains(na) && !na.Contains(artists) ? 40 : 0;
                if (name != nt) score += 10;
                if (duration.TotalSeconds > 1 && s.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number)
                    score += Math.Abs(d.GetDouble() / 1000 - duration.TotalSeconds);
                if (score < best && s.TryGetProperty("id", out var id)) { best = score; bestId = id.GetInt64(); }
            }
            if (bestId == 0 || best > 30) return null;
            using var ly = await NetEase($"https://music.163.com/api/song/lyric?id={bestId}&lv=1&kv=1&tv=-1");
            var lrc = ly.RootElement.TryGetProperty("lrc", out var l) && l.TryGetProperty("lyric", out var t) ? t.GetString() : null;
            if (string.IsNullOrWhiteSpace(lrc)) return null;
            var lines = ParseLrc(lrc).Where(x => !Credits.IsMatch(x.Text)).ToList();
            return lines.Count(x => x.Text != "♪") >= 4 ? new LyricsResult { Lines = lines, Synced = true, Source = "NetEase" } : null;
        }

        /// <summary>Сохранить выбранный вручную вариант — в следующий раз он и будет.</summary>
        public static void Remember(string title, string artist, LyricsResult r)
        {
            (title, artist) = Clean(title, artist);
            string key = (title + "|" + artist).ToLowerInvariant();
            Cache[key] = r;
            WriteDisk(key, r);
        }

        static async Task<LyricsResult> Fetch(string title, string artist, TimeSpan duration)
        {
            var list = await Search(title, artist);
            var res = Pick(list, duration);
            if (res == null && artist != "")
                res = Pick(await Search(title + " " + artist, null), duration);
            return res;
        }

        static async Task<List<JsonElement>> Search(string title, string artist)
        {
            var url = artist == null
                ? "https://lrclib.net/api/search?q=" + Uri.EscapeDataString(title.Trim())
                : "https://lrclib.net/api/search?track_name=" + Uri.EscapeDataString(title)
                  + (artist != "" ? "&artist_name=" + Uri.EscapeDataString(artist) : "");
            using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
            return doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray().Select(e => e.Clone()).ToList() : new();
        }

        /// <summary>Все найденные варианты текста — для окна «Выбрать другой текст».</summary>
        public static async Task<List<LyricsCandidate>> Candidates(string title, string artist, string query = null)
        {
            (title, artist) = Clean(title, artist);
            var raw = query != null ? await Search(query, null) : await Search(title, artist);
            if (raw.Count == 0 && query == null) raw = await Search((title + " " + artist).Trim(), null);
            return raw.Select(ToCandidate).Where(c => c != null).Take(15).ToList();
        }

        static LyricsCandidate ToCandidate(JsonElement el)
        {
            var r = FromElement(el);
            if (r == null) return null;
            string S(string k) => el.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : "";
            double d = el.TryGetProperty("duration", out var dv) && dv.ValueKind == JsonValueKind.Number ? dv.GetDouble() : 0;
            return new LyricsCandidate(S("trackName"), S("artistName"), S("albumName"), d, r.Synced, r);
        }

        static (string, string) Clean(string title, string artist)
        {
            title = (title ?? "").Trim();
            artist = (artist ?? "").Trim();
            if (artist.EndsWith(" - Topic")) artist = artist[..^8];
            title = Junk.Replace(title, "").Trim();
            // YouTube: «Исполнитель - Название»
            int dash = title.IndexOf(" - ", StringComparison.Ordinal);
            if (dash > 0 && (artist == "" || title.StartsWith(artist, StringComparison.OrdinalIgnoreCase) || artist.Contains("VEVO")))
            {
                artist = title[..dash].Trim();
                title = title[(dash + 3)..].Trim();
            }
            int ft = title.IndexOf(" (feat", StringComparison.OrdinalIgnoreCase);
            if (ft > 0) title = title[..ft];
            return (title.Trim(), artist.Trim());
        }

        static LyricsResult Pick(List<JsonElement> list, TimeSpan duration)
        {
            JsonElement? best = null;
            double bestScore = double.MaxValue;
            foreach (var el in list)
            {
                bool synced = el.TryGetProperty("syncedLyrics", out var sl) && sl.ValueKind == JsonValueKind.String && sl.GetString().Length > 10;
                bool plain = el.TryGetProperty("plainLyrics", out var pl) && pl.ValueKind == JsonValueKind.String && pl.GetString().Length > 10;
                if (!synced && !plain) continue;
                double score = synced ? 0 : 1000;
                if (duration.TotalSeconds > 1 && el.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number)
                    score += Math.Abs(d.GetDouble() - duration.TotalSeconds);
                if (score < bestScore) { bestScore = score; best = el; }
            }
            if (best == null) return null;
            if (duration.TotalSeconds > 1 && bestScore > 25 && bestScore < 1000) return null; // явно другая версия трека
            return FromElement(best.Value);
        }

        static LyricsResult FromElement(JsonElement b)
        {
            if (b.TryGetProperty("syncedLyrics", out var s) && s.ValueKind == JsonValueKind.String && s.GetString().Length > 10)
            {
                var lines = ParseLrc(s.GetString());
                if (lines.Count > 0) return new LyricsResult { Lines = lines, Synced = true, Source = "LRCLIB" };
            }
            if (b.TryGetProperty("plainLyrics", out var p) && p.ValueKind == JsonValueKind.String && p.GetString().Length > 10)
                return new LyricsResult
                {
                    Lines = p.GetString().Split('\n').Select(l => new LyricLine(TimeSpan.Zero, l.Trim())).ToList(),
                    Synced = false, Source = "LRCLIB",
                };
            return null;
        }

        static List<LyricLine> ParseLrc(string lrc)
        {
            var list = new List<LyricLine>();
            foreach (var raw in lrc.Split('\n'))
            {
                var ms = Tag.Matches(raw);
                if (ms.Count == 0) continue;
                var text = WordTag.Replace(Tag.Replace(raw, ""), "").Trim(); // «улучшенный» LRC: убираем пословные метки
                foreach (Match m in ms)
                {
                    int min = int.Parse(m.Groups[1].Value);
                    double sec = double.Parse(m.Groups[2].Value.Replace(':', '.'), CultureInfo.InvariantCulture);
                    list.Add(new LyricLine(TimeSpan.FromSeconds(min * 60 + sec), text == "" ? "♪" : text));
                }
            }
            list.Sort((a, b) => a.Time.CompareTo(b.Time));
            return list;
        }

        // ---------- кеш на диске: повторные прослушивания без интернета ----------
        static string FileFor(string key) =>
            Path.Combine(CacheDir, Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(key)))[..20] + ".json");

        static LyricsResult ReadDisk(string key)
        {
            if (!Store.Settings.LyricsCache) return null;
            try
            {
                var f = FileFor(key);
                if (!File.Exists(f)) return null;
                using var doc = JsonDocument.Parse(File.ReadAllText(f));
                var r = doc.RootElement;
                return new LyricsResult
                {
                    Synced = r.GetProperty("s").GetBoolean(), Source = "кеш",
                    Lines = r.GetProperty("l").EnumerateArray().Select(x => new LyricLine(TimeSpan.FromMilliseconds(x[0].GetDouble()), x[1].GetString())).ToList(),
                };
            }
            catch { return null; }
        }

        static void WriteDisk(string key, LyricsResult r)
        {
            if (!Store.Settings.LyricsCache || r?.Lines == null) return;
            try
            {
                Directory.CreateDirectory(CacheDir);
                var json = JsonSerializer.Serialize(new { s = r.Synced, l = r.Lines.Select(x => new object[] { x.Time.TotalMilliseconds, x.Text }) });
                File.WriteAllText(FileFor(key), json);
                // держим кеш небольшим
                var files = new DirectoryInfo(CacheDir).GetFiles("*.json");
                if (files.Length > 400)
                    foreach (var old in files.OrderBy(f => f.LastWriteTime).Take(files.Length - 400)) old.Delete();
            }
            catch { }
        }

        public static (int Files, long Bytes) CacheInfo()
        {
            try
            {
                var files = new DirectoryInfo(CacheDir).GetFiles("*.json");
                return (files.Length, files.Sum(f => f.Length));
            }
            catch { return (0, 0); }
        }

        public static void ClearCache()
        {
            Cache.Clear();
            try { if (Directory.Exists(CacheDir)) Directory.Delete(CacheDir, true); } catch { }
        }
    }
}
