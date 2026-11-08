using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NIsland.Core;

namespace NIsland.Services
{
    /// <summary>Слово со своим временем — пословная синхронизация (enhanced LRC, NetEase yrc).</summary>
    public record LyricWord(TimeSpan Start, TimeSpan End, string Text);

    public record LyricLine(TimeSpan Time, string Text, List<LyricWord> Words = null);

    public class LyricsResult
    {
        public List<LyricLine> Lines;
        public bool Synced;
        public string Source;
        /// <summary>Длительность версии трека, под которую сделан текст, с (0 — неизвестна).</summary>
        public double Duration;
        public bool Instrumental;
        /// <summary>Выбран вручную — не заменяем автоматически.</summary>
        public bool Pinned;
        public bool HasWords => Lines?.Any(l => l.Words != null) == true;
    }

    /// <summary>Вариант текста для ручного выбора («не тот текст»). Текст NetEase и Kugou скачивается только по нажатию.</summary>
    public class LyricsCandidate
    {
        public string Track, Artist, Album, Source;
        public double Duration;
        public bool Synced;
        public double Score;
        public Func<Task<LyricsResult>> Load;
    }

    /// <summary>
    /// Тексты песен: LRCLIB (точный запрос по длительности, затем поиск), запасные NetEase и Kugou, простой текст — lyrics.ovh.
    /// Названия с YouTube, Spotify и VK чистятся («Official Video», «Remastered», «feat.», «Исполнитель - Название»),
    /// кандидаты сравниваются по названию, исполнителю (с транслитерацией) и длительности, поэтому чужая версия трека не подхватывается.
    /// </summary>
    public static class LyricsService
    {
        static readonly HttpClient Http = new(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectTimeout = TimeSpan.FromSeconds(6),
        }) { Timeout = TimeSpan.FromSeconds(7) };

        static readonly ConcurrentDictionary<string, LyricsResult> Cache = new();
        static readonly ConcurrentDictionary<string, Task<LyricsResult>> Inflight = new();
        static readonly string CacheDir = Path.Combine(Store.Dir, "lyrics");

        static LyricsService()
        {
            Http.DefaultRequestHeaders.UserAgent.ParseAdd($"NIsland/{AppInfo.Version} (+https://github.com/Neizvestnyi22/N-Island)");
        }

        // ------------------------------------------------------------------
        //  Главный вход
        // ------------------------------------------------------------------

        /// <param name="browser">Источник — браузер: название чаще «Исполнитель - Название», а «исполнитель» — это канал.</param>
        public static async Task<LyricsResult> Get(string title, string artist, string album, TimeSpan duration, bool browser = false)
        {
            var q = Query.From(title, artist, album, browser);
            if (q.Variants.Count == 0) return null;
            double dur = duration.TotalSeconds > 1 ? duration.TotalSeconds : 0;
            if (Cache.TryGetValue(q.Key, out var hit) && Fits(hit, dur)) return hit;
            var disk = ReadDisk(q.Key);
            if (disk != null && Fits(disk, dur)) { Cache[q.Key] = disk; return disk; }

            // одну и ту же песню часто спрашивают дважды подряд (сначала приходит название, потом исполнитель) — один запрос на всех
            string flight = q.Key + "|" + Math.Round(dur / 15);
            var task = Inflight.GetOrAdd(flight, _ => Load(q, dur));
            try { return await task; }
            finally { Inflight.TryRemove(flight, out _); }
        }

        /// <summary>Подходит ли сохранённый текст к этой версии трека: другая длительность — значит, другой мастер или клип.</summary>
        static bool Fits(LyricsResult r, double dur) =>
            r == null || r.Pinned || !r.Synced || r.Duration <= 0 || dur <= 0 || Math.Abs(r.Duration - dur) <= 8;

        static async Task<LyricsResult> Load(Query q, double dur)
        {
            var found = new List<(LyricsResult R, double Score)>();
            bool reached = false;   // хоть один источник ответил — значит, «не найдено» правда, а не обрыв сети
            bool failed = false;    // какой-то источник не ответил (LRCLIB часто отдаёт 503)

            void Take(IEnumerable<(LyricsResult R, double Score)> items, ref bool ok)
            {
                ok = true;
                foreach (var it in items) if (it.R != null && it.Score > 0) found.Add(it);
            }
            void Note<T>(Res<T> r) { if (!r.Ok) failed = true; }
            bool Done() => found.Any(f => f.R.Synced && f.Score >= 85);

            // запасные источники спрашиваем сразу, параллельно с LRCLIB: если LRCLIB не знает песню или тормозит,
            // ответ NetEase/Kugou уже готов — текст появляется на 1–3 секунды раньше
            Task<Res<List<(LyricsResult, double)>>> ne = null, kg = null;
            if (Store.Settings.LyricsFallback)
            {
                ne = Try("netease", () => NetEase(q, dur));
                kg = Try("kugou", () => Kugou(q, dur));
            }

            // 1) LRCLIB: точный запрос (название + исполнитель + альбом + длительность) и поиск — параллельно
            var main = q.Variants[0];
            var exact = Try("lrclib", () => LrcLibGet(main.Title, main.Artist, q.Album, dur));
            var search = Try("lrclib", () => LrcLibSearch(main.Title, main.Artist, q, dur));
            var loose = Try("lrclib", () => LrcLibQuery((main.Title + " " + main.Artist).Trim(), q, dur));
            var lrclib = Task.WhenAll(exact, search, loose);
            // LRCLIB тормозит, а запасной источник уже нашёл точный синхронный текст — не ждём LRCLIB дольше 3,5 с
            if (ne != null && await Task.WhenAny(lrclib, Task.Delay(3500)) != lrclib)
            {
                var quick = new List<(LyricsResult R, double Score)>();
                foreach (var t in new[] { ne, kg })
                    if (t.IsCompletedSuccessfully && t.Result.Ok) quick.AddRange(t.Result.Value.Where(x => x.Item1 != null && x.Item1.Synced && x.Item2 >= 85));
                if (quick.Count > 0)
                {
                    var fast = quick.OrderByDescending(x => (x.R.HasWords ? 5 : 0) + x.Score).First().R;
                    Cache[q.Key] = fast;
                    WriteDisk(q.Key, fast);
                    return fast;
                }
            }
            await lrclib;
            Note(exact.Result); Note(search.Result); Note(loose.Result);
            if (exact.Result.Ok) { reached = true; if (exact.Result.Value is { } ex) found.Add((ex, 95)); }
            if (search.Result.Ok) Take(search.Result.Value, ref reached);
            if (loose.Result.Ok) Take(loose.Result.Value, ref reached);

            // 2) другие варианты названия (YouTube: «Исполнитель - Название», без скобок, только первый исполнитель)
            for (int i = 1; i < q.Variants.Count && !Done() && i < 4; i++)
            {
                var v = q.Variants[i];
                var r = await Try("lrclib", () => LrcLibSearch(v.Title, v.Artist, q, dur));
                Note(r);
                if (r.Ok) Take(r.Value, ref reached);
            }

            // 3) запасные синхронные источники
            if (!Done() && ne != null)
            {
                await Task.WhenAll(ne, kg);
                Note(ne.Result); Note(kg.Result);
                if (ne.Result.Ok) Take(ne.Result.Value, ref reached);
                if (kg.Result.Ok) Take(kg.Result.Value, ref reached);
            }

            // 4) хоть какой-то текст без синхронизации
            if (!found.Any(f => f.R.Synced))
            {
                var ovh = await Try("ovh", () => LyricsOvh(main.Title, main.Artist));
                if (ovh.Ok) { reached = true; if (ovh.Value != null) found.Add((ovh.Value, 20)); }
            }

            var best = found.OrderByDescending(f => (f.R.Synced ? 1000 : 0) + (f.R.HasWords ? 5 : 0) + f.Score).Select(f => f.R).FirstOrDefault();
            if (best == null && !reached) return null;   // сеть подвела — «текста нет» не запоминаем
            // основной источник не ответил, а нашёлся только текст без синхронизации — показываем, но не запоминаем:
            // в следующий раз LRCLIB, скорее всего, ответит и даст синхронный
            if (failed && (best == null || !best.Synced)) return best;
            Cache[q.Key] = best;
            if (best != null) WriteDisk(q.Key, best);
            return best;
        }

        readonly struct Res<T> { public readonly bool Ok; public readonly T Value; public Res(bool ok, T v) { Ok = ok; Value = v; } }

        // источник, который несколько раз подряд не ответил, отдыхает 10 минут — не ждём его тайм-аутов на каждой песне
        static readonly ConcurrentDictionary<string, (int Fails, DateTime Until)> Health = new();

        /// <summary>Состояние источников (для настроек): имя → «работает» / «отдыхает до 14:20».</summary>
        public static IEnumerable<(string Source, string State)> SourceHealth() =>
            new[] { "lrclib", "netease", "kugou", "ovh" }.Select(s => (s, Health.TryGetValue(s, out var h) && h.Until > DateTime.Now ? $"не отвечает, пробую снова в {h.Until:HH:mm}" : h.Fails > 0 ? "были сбои" : "работает"));

        static async Task<Res<T>> Try<T>(string source, Func<Task<T>> f)
        {
            if (Health.TryGetValue(source, out var hs) && hs.Until > DateTime.Now) return new Res<T>(false, default);
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    var v = await f();
                    Health[source] = (0, DateTime.MinValue);
                    return new Res<T>(true, v);
                }
                catch (HttpRequestException e) when (attempt == 0 && (e.StatusCode is null or HttpStatusCode.TooManyRequests or >= HttpStatusCode.InternalServerError))
                {
                    // LRCLIB иногда отвечает 503, а при частых запросах — 429: одна повторная попытка после паузы
                    await Task.Delay(e.StatusCode == HttpStatusCode.TooManyRequests ? 2500 : 1200);
                }
                catch (Exception e)
                {
                    Log.Info($"lyrics {source}: " + e.Message);
                    var h = Health.TryGetValue(source, out var old) ? old : (0, DateTime.MinValue);
                    int fails = h.Item1 + 1;
                    Health[source] = (fails, fails >= 3 ? DateTime.Now.AddMinutes(10) : DateTime.MinValue);
                    return new Res<T>(false, default);
                }
            }
        }

        // ------------------------------------------------------------------
        //  Запрос: чистка названий из YouTube, Spotify, VK, Яндекс Музыки
        // ------------------------------------------------------------------

        sealed class Query
        {
            public List<(string Title, string Artist)> Variants = new();
            public string Album = "", Key = "";

            static readonly Regex Brackets = new(@"\s*[\(\[【（][^\)\]】）]*[\)\]】）]", RegexOptions.Compiled);
            static readonly Regex JunkBrackets = new(
                @"\s*[\(\[【（][^\)\]】）]*(official|video|lyric|audio|clip|visuali[sz]er|hd|hq|4k|8k|mv|m/v|remaster|explicit|clean|премьер|клип|текст|видео|official\s+music|music\s+video|live\s+session|full\s+album|prod\.?|with\s+lyrics)[^\)\]】）]*[\)\]】）]",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);
            static readonly Regex Feat = new(@"\s*[\(\[]?\s*\b(feat\.?|ft\.?|featuring|при уч\.?|при участии)\s+[^\)\]]*[\)\]]?", RegexOptions.IgnoreCase | RegexOptions.Compiled);
            static readonly Regex Suffix = new(@"\s+[-–—]\s+(\d{4}\s+)?(remaster(ed)?|re-?master(ed)?|live|radio edit|single version|album version|mono|stereo|bonus track|demo|acoustic version|original mix|extended mix|from .+)(\s+\d{4}|\s+version)?.*$",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);
            static readonly Regex Dash = new(@"^(.+?)\s+[-–—~]\s+(.+)$", RegexOptions.Compiled);
            static readonly Regex Quoted = new(@"^(.+?)\s+[«""“„](.+?)[»""”“]\s*$", RegexOptions.Compiled);
            static readonly Regex ChannelJunk = new(@"\s*(-\s*topic|vevo|official|official channel|официальный канал|music|records|tv)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

            public static Query From(string title, string artist, string album, bool browser)
            {
                var q = new Query { Album = (album ?? "").Trim() };
                string t = Tidy(title), a = (artist ?? "").Trim();
                bool channel = a != "" && ChannelJunk.IsMatch(a);
                a = ChannelJunk.Replace(a, "").Trim();
                t = Regex.Replace(t, @"\s*[|｜•].*$", "");                 // «Название | Премьера клипа 2024»
                t = JunkBrackets.Replace(t, "");
                t = Suffix.Replace(t, "");                                   // Spotify: «Song - Remastered 2011»
                string tNoFeat = Feat.Replace(t, "").Trim();

                // первым идёт самый вероятный вариант — по нему же спрашиваем запасные источники
                var list = new List<(string, string)>();
                var later = new List<(string, string)>();
                var quoted = Quoted.Match(tNoFeat);
                var dash = Dash.Match(tNoFeat);
                bool unsure = a == "" || browser || channel;   // «исполнитель» — это канал или его нет
                if (quoted.Success)
                    (unsure ? list : later).Add((quoted.Groups[2].Value, quoted.Groups[1].Value));   // Исполнитель «Название»
                if (dash.Success)
                {
                    string left = dash.Groups[1].Value.Trim(), right = dash.Groups[2].Value.Trim();
                    if (a != "" && Similar(right, a)) list.Add((left, right));                     // «Название - Исполнитель»
                    else if (unsure || Similar(left, a)) list.Add((right, left));                  // «Исполнитель - Название»
                    else later.Add((right, left));
                }
                list.Add((tNoFeat, a));
                list.AddRange(later);
                // без скобок вообще («Song (Remix)» → «Song») и только первый исполнитель («A, B & C» → «A»)
                string bare = Brackets.Replace(tNoFeat, "").Trim();
                string first = PrimaryArtist(a);
                foreach (var (vt, va) in list.ToList())
                {
                    var vb = Brackets.Replace(vt, "").Trim();
                    if (vb != vt) list.Add((vb, va));
                    var pa = PrimaryArtist(va);
                    if (pa != va) list.Add((vb, pa));
                }
                if (bare != "" && first != a) list.Add((bare, first));

                foreach (var (vt, va) in list)
                {
                    var tt = Feat.Replace(vt, "").Trim().Trim('"', '\'', '«', '»');
                    var aa = Feat.Replace(va, "").Trim();
                    if (tt.Length == 0) continue;
                    if (!q.Variants.Any(v => string.Equals(v.Title, tt, StringComparison.OrdinalIgnoreCase) && string.Equals(v.Artist, aa, StringComparison.OrdinalIgnoreCase)))
                        q.Variants.Add((tt, aa));
                }
                if (q.Variants.Count > 0) q.Key = (q.Variants[0].Title + "|" + q.Variants[0].Artist).ToLowerInvariant();
                return q;
            }

            static string Tidy(string s) => Regex.Replace((s ?? "").Replace(' ', ' '), @"\s+", " ").Trim();

            public static string PrimaryArtist(string a)
            {
                if (string.IsNullOrEmpty(a)) return "";
                var parts = Regex.Split(a, @"\s*(?:,|&|\+|/|;|\bx\b|\bvs\.?|\band\b|\bи\b|\bfeat\.?|\bft\.?)\s*", RegexOptions.IgnoreCase);
                return parts.FirstOrDefault(p => p.Trim().Length > 0)?.Trim() ?? a;
            }
        }

        // ------------------------------------------------------------------
        //  Сравнение названий и исполнителей
        // ------------------------------------------------------------------

        static string Norm(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var d = s.ToLowerInvariant().Replace('ё', 'е').Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(d.Length);
            foreach (var c in d)
                if (char.IsLetterOrDigit(c) && CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
            return sb.ToString();
        }

        static readonly string[] Lat =
        {
            "a","b","v","g","d","e","zh","z","i","y","k","l","m","n","o","p","r","s","t","u","f","h","ts","ch","sh","sch","","y","","e","yu","ya",
        };

        /// <summary>Кириллица → латиница, чтобы «Земфира» и «Zemfira» считались одним исполнителем.</summary>
        static string Translit(string s)
        {
            var sb = new StringBuilder(s.Length + 8);
            foreach (var c in s)
            {
                if (c >= 'а' && c <= 'я') sb.Append(Lat[c - 'а']);
                else if (c is 'і' or 'ї') sb.Append('i');
                else if (c == 'є') sb.Append("ye");
                else if (c == 'ґ') sb.Append('g');
                else sb.Append(c);
            }
            return sb.ToString().Replace("kh", "h").Replace("j", "y").Replace("w", "v").Replace("x", "ks");
        }

        static double Sim(string a, string b)
        {
            a = Norm(a); b = Norm(b);
            if (a.Length == 0 || b.Length == 0) return 0;
            if (a == b) return 1;
            string ta = Translit(a), tb = Translit(b);
            if (ta == tb) return 0.97;
            if ((a.Length >= 4 && b.Contains(a)) || (b.Length >= 4 && a.Contains(b))) return 0.88;
            return Math.Max(Ratio(a, b), Ratio(ta, tb));
        }

        static bool Similar(string a, string b) => Sim(a, b) >= 0.8;

        /// <summary>Похожесть по расстоянию Левенштейна, 0…1.</summary>
        static double Ratio(string a, string b)
        {
            if (a.Length > 64) a = a[..64];
            if (b.Length > 64) b = b[..64];
            var prev = new int[b.Length + 1];
            var cur = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++) prev[j] = j;
            for (int i = 1; i <= a.Length; i++)
            {
                cur[0] = i;
                for (int j = 1; j <= b.Length; j++)
                    cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
                (prev, cur) = (cur, prev);
            }
            return 1 - (double)prev[b.Length] / Math.Max(a.Length, b.Length);
        }

        static double ArtistSim(string want, string got)
        {
            if (string.IsNullOrWhiteSpace(want)) return 0.75;
            if (string.IsNullOrWhiteSpace(got)) return 0.4;
            var w = Regex.Split(want, @"\s*(?:,|&|/|;|\bx\b|\bfeat\.?|\bft\.?|\band\b|\bи\b)\s*", RegexOptions.IgnoreCase).Where(s => s.Trim() != "");
            var g = Regex.Split(got, @"\s*(?:,|&|/|;|\bx\b|\bfeat\.?|\bft\.?|\band\b|\bи\b)\s*", RegexOptions.IgnoreCase).Where(s => s.Trim() != "").ToList();
            double best = Sim(want, got);
            foreach (var x in w) foreach (var y in g) best = Math.Max(best, Sim(x, y));
            return best;
        }

        /// <summary>Оценка кандидата 0…100; ≤ 0 — не подходит. Чужая длительность делает текст несинхронным, а не подходящим.</summary>
        static double Score(string wantTitle, string wantArtist, double dur, string title, string artist, double cdur, ref bool synced)
        {
            string clean = Regex.Replace(title ?? "", @"\s*[\(\[][^\)\]]*[\)\]]", "");
            double ts = Math.Max(Sim(wantTitle, title), Sim(wantTitle, clean));
            if (ts < 0.6) return 0;
            double asim = ArtistSim(wantArtist, artist);
            if (!string.IsNullOrWhiteSpace(wantArtist) && asim < 0.45) return 0;
            double s = ts * 55 + asim * 30;
            if (dur > 1 && cdur > 1)
            {
                double d = Math.Abs(cdur - dur);
                if (d > 10) synced = false;          // другая версия (клип с интро, ремикс) — строчки разъедутся, показываем без синхронизации
                else s += 15 - d * 1.5;
            }
            else s += 6;
            return s;
        }

        // ------------------------------------------------------------------
        //  LRCLIB
        // ------------------------------------------------------------------

        static async Task<LyricsResult> LrcLibGet(string title, string artist, string album, double dur)
        {
            if (dur <= 1 || artist == "") return null;
            var url = "https://lrclib.net/api/get?track_name=" + Uri.EscapeDataString(title) + "&artist_name=" + Uri.EscapeDataString(artist)
                      + (album != "" ? "&album_name=" + Uri.EscapeDataString(album) : "") + "&duration=" + Math.Round(dur).ToString(CultureInfo.InvariantCulture);
            using var resp = await Http.GetAsync(url);
            if (resp.StatusCode == HttpStatusCode.NotFound) return null;
            resp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            return FromLrcLib(doc.RootElement);
        }

        static async Task<List<JsonElement>> LrcLibRaw(string url)
        {
            using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
            return doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray().Select(e => e.Clone()).Take(30).ToList() : new();
        }

        static async Task<List<(LyricsResult, double)>> LrcLibSearch(string title, string artist, Query q, double dur)
        {
            var url = "https://lrclib.net/api/search?track_name=" + Uri.EscapeDataString(title) + (artist != "" ? "&artist_name=" + Uri.EscapeDataString(artist) : "");
            return Rank(await LrcLibRaw(url), title, artist, dur);
        }

        static async Task<List<(LyricsResult, double)>> LrcLibQuery(string text, Query q, double dur)
        {
            if (text.Length < 2) return new();
            var v = q.Variants[0];
            return Rank(await LrcLibRaw("https://lrclib.net/api/search?q=" + Uri.EscapeDataString(text)), v.Title, v.Artist, dur);
        }

        static List<(LyricsResult, double)> Rank(List<JsonElement> list, string title, string artist, double dur)
        {
            var res = new List<(LyricsResult, double)>();
            foreach (var el in list)
            {
                var r = FromLrcLib(el);
                if (r == null) continue;
                bool synced = r.Synced;
                double score = Score(title, artist, dur, Str(el, "trackName"), Str(el, "artistName"), r.Duration, ref synced);
                if (score <= 0) continue;
                if (!synced && r.Synced) r = Unsync(r);
                res.Add((r, score));
            }
            return res;
        }

        static string Str(JsonElement el, string k) => el.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : "";
        static double Num(JsonElement el, string k) => el.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;

        static LyricsResult FromLrcLib(JsonElement b)
        {
            double d = Num(b, "duration");
            if (b.TryGetProperty("instrumental", out var ins) && ins.ValueKind == JsonValueKind.True)
                return new LyricsResult { Lines = new(), Instrumental = true, Source = "LRCLIB", Duration = d };
            var synced = Str(b, "syncedLyrics");
            if (synced.Length > 10)
            {
                var lines = Tidy(ParseLrc(synced), Str(b, "trackName"), Str(b, "artistName"));
                if (lines.Count(l => l.Text != "♪") >= 2) return new LyricsResult { Lines = lines, Synced = true, Source = "LRCLIB", Duration = d };
            }
            var plain = Str(b, "plainLyrics");
            if (plain.Length > 10) return new LyricsResult { Lines = PlainLines(plain), Synced = false, Source = "LRCLIB", Duration = d };
            return null;
        }

        static List<LyricLine> PlainLines(string text) =>
            text.Replace("\r", "").Split('\n').Select(l => new LyricLine(TimeSpan.Zero, l.Trim())).ToList();

        static LyricsResult Unsync(LyricsResult r) => new()
        {
            Lines = r.Lines.Where(l => l.Text != "♪").Select(l => new LyricLine(TimeSpan.Zero, l.Text)).ToList(),
            Synced = false, Source = r.Source, Duration = r.Duration,
        };

        // ------------------------------------------------------------------
        //  NetEase Cloud Music (синхронные и пословные тексты, без ключей)
        // ------------------------------------------------------------------

        static async Task<JsonDocument> NetEaseJson(string url)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Referrer = new Uri("https://music.163.com/");
            using var resp = await Http.SendAsync(req);
            resp.EnsureSuccessStatusCode();
            return JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        }

        static async Task<List<LyricsCandidate>> NetEaseSearch(string text, string wantTitle, string wantArtist, double dur)
        {
            using var doc = await NetEaseJson("https://music.163.com/api/search/get?type=1&limit=10&s=" + Uri.EscapeDataString(text));
            var list = new List<LyricsCandidate>();
            if (!doc.RootElement.TryGetProperty("result", out var r) || !r.TryGetProperty("songs", out var songs) || songs.ValueKind != JsonValueKind.Array) return list;
            foreach (var s in songs.EnumerateArray())
            {
                string name = Str(s, "name");
                string artists = s.TryGetProperty("artists", out var a) && a.ValueKind == JsonValueKind.Array
                    ? string.Join(", ", a.EnumerateArray().Select(x => Str(x, "name"))) : "";
                double d = Num(s, "duration") / 1000;
                bool synced = true;
                double score = Score(wantTitle, wantArtist, dur, name, artists, d, ref synced);
                if (score <= 0 || !s.TryGetProperty("id", out var idEl)) continue;
                long id = idEl.GetInt64();
                bool keepSync = synced;
                list.Add(new LyricsCandidate
                {
                    Track = name, Artist = artists, Album = s.TryGetProperty("album", out var al) ? Str(al, "name") : "", Duration = d, Synced = true,
                    Source = "NetEase", Score = score, Load = async () => { var res = await NetEaseLyric(id, name, artists, d); return keepSync || res == null ? res : Unsync(res); },
                });
            }
            return list.OrderByDescending(c => c.Score).ToList();
        }

        static async Task<LyricsResult> NetEaseLyric(long id, string title, string artist, double dur)
        {
            using var ly = await NetEaseJson($"https://music.163.com/api/song/lyric?id={id}&lv=1&kv=1&tv=-1&yv=1");
            var root = ly.RootElement;
            if (root.TryGetProperty("pureMusic", out var pm) && pm.ValueKind == JsonValueKind.True)
                return new LyricsResult { Lines = new(), Instrumental = true, Source = "NetEase", Duration = dur };
            string yrc = root.TryGetProperty("yrc", out var y) ? Str(y, "lyric") : "";
            string lrc = root.TryGetProperty("lrc", out var l) ? Str(l, "lyric") : "";
            var lines = yrc.Length > 20 ? ParseYrc(yrc) : null;
            if (lines == null || lines.Count < 4) lines = ParseLrc(lrc);
            lines = Tidy(lines, title, artist);
            return lines.Count(x => x.Text != "♪") >= 4 ? new LyricsResult { Lines = lines, Synced = true, Source = "NetEase", Duration = dur } : null;
        }

        static async Task<List<(LyricsResult, double)>> NetEase(Query q, double dur)
        {
            var v = q.Variants[0];
            var cands = await NetEaseSearch((v.Title + " " + v.Artist).Trim(), v.Title, v.Artist, dur);
            var res = new List<(LyricsResult, double)>();
            foreach (var c in cands.Take(2))
            {
                var r = await c.Load();
                if (r != null) { res.Add((r, c.Score - 3)); if (r.Synced) break; }
            }
            return res;
        }

        // ------------------------------------------------------------------
        //  Kugou (огромная база синхронных текстов, в том числе русских и украинских)
        // ------------------------------------------------------------------

        static async Task<List<LyricsCandidate>> KugouSearch(string title, string artist, double dur)
        {
            string kw = artist != "" ? artist + " - " + title : title;
            var url = "http://lyrics.kugou.com/search?ver=1&man=yes&client=pc&hash=&keyword=" + Uri.EscapeDataString(kw)
                      + (dur > 1 ? "&duration=" + Math.Round(dur * 1000).ToString(CultureInfo.InvariantCulture) : "");
            using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
            var list = new List<LyricsCandidate>();
            if (!doc.RootElement.TryGetProperty("candidates", out var cs) || cs.ValueKind != JsonValueKind.Array) return list;
            foreach (var c in cs.EnumerateArray())
            {
                string song = Str(c, "song"), singer = Str(c, "singer"), id = c.TryGetProperty("id", out var i) ? i.ToString() : "", key = Str(c, "accesskey");
                double d = Num(c, "duration") / 1000;
                bool synced = true;
                double score = Score(title, artist, dur, song, singer, d, ref synced);
                if (score <= 0 || id == "" || key == "") continue;
                bool keepSync = synced;
                list.Add(new LyricsCandidate
                {
                    Track = song, Artist = singer, Duration = d, Synced = true, Source = "Kugou", Score = score,
                    Load = async () => { var r = await KugouLyric(id, key, song, singer, d); return keepSync || r == null ? r : Unsync(r); },
                });
            }
            return list.OrderByDescending(c => c.Score).ToList();
        }

        static async Task<LyricsResult> KugouLyric(string id, string key, string title, string artist, double dur)
        {
            using var doc = JsonDocument.Parse(await Http.GetStringAsync($"http://lyrics.kugou.com/download?ver=1&client=pc&fmt=lrc&charset=utf8&id={id}&accesskey={key}"));
            var b64 = Str(doc.RootElement, "content");
            if (b64 == "") return null;
            var lrc = Encoding.UTF8.GetString(Convert.FromBase64String(b64));
            var lines = Tidy(ParseLrc(lrc), title, artist);
            return lines.Count(x => x.Text != "♪") >= 4 ? new LyricsResult { Lines = lines, Synced = true, Source = "Kugou", Duration = dur } : null;
        }

        static async Task<List<(LyricsResult, double)>> Kugou(Query q, double dur)
        {
            var v = q.Variants[0];
            var cands = await KugouSearch(v.Title, v.Artist, dur);
            var res = new List<(LyricsResult, double)>();
            foreach (var c in cands.Take(2))
            {
                var r = await c.Load();
                if (r != null) { res.Add((r, c.Score - 4)); if (r.Synced) break; }
            }
            return res;
        }

        // ------------------------------------------------------------------
        //  lyrics.ovh — простой текст, когда синхронного нет нигде
        // ------------------------------------------------------------------

        static async Task<LyricsResult> LyricsOvh(string title, string artist)
        {
            if (artist == "") return null;
            using var resp = await Http.GetAsync("https://api.lyrics.ovh/v1/" + Uri.EscapeDataString(artist) + "/" + Uri.EscapeDataString(title));
            if (resp.StatusCode == HttpStatusCode.NotFound) return null;
            resp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var text = Str(doc.RootElement, "lyrics");
            text = Regex.Replace(text, @"^Paroles de la chanson.*\r?\n", "", RegexOptions.IgnoreCase);
            if (text.Trim().Length < 20) return null;
            var lines = PlainLines(text);
            // lyrics.ovh часто ставит пустую строку между каждыми двумя — убираем лишние
            var tidy = new List<LyricLine>();
            foreach (var l in lines) if (l.Text != "" || (tidy.Count > 0 && tidy[^1].Text != "")) tidy.Add(l);
            return new LyricsResult { Lines = tidy, Synced = false, Source = "lyrics.ovh" };
        }

        // ------------------------------------------------------------------
        //  Длительность трека для приложений, которые её не сообщают
        // ------------------------------------------------------------------

        /// <summary>Длительность песни: из найденного текста или из каталога iTunes. 0 — не знаем.</summary>
        public static async Task<double> LookupDuration(string title, string artist, bool browser = false)
        {
            var q = Query.From(title, artist, "", browser);
            if (q.Variants.Count == 0) return 0;
            if (Cache.TryGetValue(q.Key, out var r) && r?.Duration > 1) return r.Duration;
            var v = q.Variants[0];
            try
            {
                using var doc = JsonDocument.Parse(await Http.GetStringAsync("https://itunes.apple.com/search?entity=song&limit=8&term=" + Uri.EscapeDataString((v.Artist + " " + v.Title).Trim())));
                double best = 0, bestScore = 0;
                foreach (var el in doc.RootElement.GetProperty("results").EnumerateArray())
                {
                    bool s = true;
                    double sc = Score(v.Title, v.Artist, 0, Str(el, "trackName"), Str(el, "artistName"), 0, ref s);
                    if (sc > bestScore && Num(el, "trackTimeMillis") > 0) { bestScore = sc; best = Num(el, "trackTimeMillis") / 1000; }
                }
                return bestScore >= 70 ? best : 0;
            }
            catch (Exception e) { Log.Info("duration lookup: " + e.Message); return 0; }
        }

        // ------------------------------------------------------------------
        //  Ручной выбор
        // ------------------------------------------------------------------

        /// <summary>Сохранить выбранный вручную вариант — в следующий раз он и будет.</summary>
        public static void Remember(string title, string artist, LyricsResult r, bool browser = false)
        {
            var q = Query.From(title, artist, "", browser);
            if (q.Variants.Count == 0 || r == null) return;
            r.Pinned = true;
            Cache[q.Key] = r;
            WriteDisk(q.Key, r);
        }

        /// <summary>Все варианты текста — для окна «Выбрать другой текст»: LRCLIB, NetEase и Kugou.</summary>
        public static async Task<List<LyricsCandidate>> Candidates(string title, string artist, TimeSpan duration, string query = null, bool browser = false)
        {
            double dur = duration.TotalSeconds > 1 ? duration.TotalSeconds : 0;
            string wt, wa, text;
            if (query != null) { wt = query; wa = ""; text = query; }
            else
            {
                var q = Query.From(title, artist, "", browser);
                if (q.Variants.Count == 0) return new();
                (wt, wa) = q.Variants[0];
                text = (wt + " " + wa).Trim();
            }

            var lrc = Task.Run(async () =>
            {
                var raw = query != null ? await LrcLibRaw("https://lrclib.net/api/search?q=" + Uri.EscapeDataString(text))
                                        : await LrcLibRaw("https://lrclib.net/api/search?track_name=" + Uri.EscapeDataString(wt) + (wa != "" ? "&artist_name=" + Uri.EscapeDataString(wa) : ""));
                if (raw.Count == 0 && query == null) raw = await LrcLibRaw("https://lrclib.net/api/search?q=" + Uri.EscapeDataString(text));
                var list = new List<LyricsCandidate>();
                foreach (var el in raw)
                {
                    var r = FromLrcLib(el);
                    if (r == null || r.Instrumental) continue;
                    bool s = r.Synced;
                    double score = query != null ? 50 : Score(wt, wa, dur, Str(el, "trackName"), Str(el, "artistName"), r.Duration, ref s);
                    var rr = r;
                    list.Add(new LyricsCandidate
                    {
                        Track = Str(el, "trackName"), Artist = Str(el, "artistName"), Album = Str(el, "albumName"), Duration = r.Duration,
                        Synced = r.Synced, Source = "LRCLIB", Score = score + (r.Synced ? 10 : 0), Load = () => Task.FromResult(rr),
                    });
                }
                return list;
            });
            var ne = Task.Run(() => NetEaseSearch(text, query != null ? query : wt, wa, dur));
            var kg = Task.Run(() => KugouSearch(query != null ? query : wt, wa, dur));
            var all = new List<LyricsCandidate>();
            foreach (var t in new[] { lrc, ne, kg })
            {
                try { all.AddRange((await t).Take(t == lrc ? 15 : 6)); }
                catch (Exception e) { Log.Info("lyrics candidates: " + e.Message); }
            }
            if (query != null)   // свободный запрос: оценки бессмысленны, показываем как пришло — сначала синхронные
                return all.OrderByDescending(c => c.Synced).ToList();
            return all.OrderByDescending(c => c.Score).ToList();
        }

        // ------------------------------------------------------------------
        //  Разбор форматов
        // ------------------------------------------------------------------

        static readonly Regex Tag = new(@"\[(\d{1,3}):(\d{1,2}(?:[.:]\d{1,3})?)\]", RegexOptions.Compiled);
        static readonly Regex WordTag = new(@"<(\d{1,3}):(\d{1,2}(?:[.:]\d{1,3})?)>", RegexOptions.Compiled);
        static readonly Regex OffsetTag = new(@"\[offset:\s*([+-]?\d+)\s*\]", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static double Sec(string min, string sec) =>
            int.Parse(min, CultureInfo.InvariantCulture) * 60 + double.Parse(sec.Replace(':', '.'), CultureInfo.InvariantCulture);

        static List<LyricLine> ParseLrc(string lrc)
        {
            var list = new List<LyricLine>();
            if (string.IsNullOrEmpty(lrc)) return list;
            double offset = 0;
            var om = OffsetTag.Match(lrc);
            if (om.Success) offset = int.Parse(om.Groups[1].Value, CultureInfo.InvariantCulture) / 1000.0;   // [offset:+500] — текст раньше на 0,5 с
            foreach (var raw0 in lrc.Replace("\r", "").Split('\n'))
            {
                var ms = Tag.Matches(raw0);
                if (ms.Count == 0) continue;
                var body = Tag.Replace(raw0, "");
                // «улучшенный» LRC: <00:12.34>слово <00:12.80>слово
                List<LyricWord> words = null;
                var wm = WordTag.Matches(body);
                if (wm.Count >= 2)
                {
                    words = new List<LyricWord>();
                    for (int i = 0; i < wm.Count; i++)
                    {
                        int from = wm[i].Index + wm[i].Length;
                        int to = i + 1 < wm.Count ? wm[i + 1].Index : body.Length;
                        var w = body[from..to];
                        if (w.Trim() == "") continue;
                        double st = Sec(wm[i].Groups[1].Value, wm[i].Groups[2].Value) - offset;
                        double en = i + 1 < wm.Count ? Sec(wm[i + 1].Groups[1].Value, wm[i + 1].Groups[2].Value) - offset : st + 0.6;
                        words.Add(new LyricWord(TimeSpan.FromSeconds(Math.Max(0, st)), TimeSpan.FromSeconds(Math.Max(0, en)), w));
                    }
                    if (words.Count == 0) words = null;
                }
                var text = Regex.Replace(WordTag.Replace(body, ""), @"\s+", " ").Trim();
                foreach (Match m in ms)
                {
                    double t = Sec(m.Groups[1].Value, m.Groups[2].Value) - offset;
                    list.Add(new LyricLine(TimeSpan.FromSeconds(Math.Max(0, t)), text == "" ? "♪" : text, ms.Count == 1 ? words : null));
                }
            }
            list.Sort((a, b) => a.Time.CompareTo(b.Time));
            return list;
        }

        static readonly Regex YrcLine = new(@"^\[(\d+),(\d+)\](.*)$", RegexOptions.Compiled);
        static readonly Regex YrcWord = new(@"\((\d+),(\d+),\d+\)([^\(]*)", RegexOptions.Compiled);

        /// <summary>NetEase yrc: [начало,длит](начало,длит,0)слово(…)слово — время в миллисекундах.</summary>
        static List<LyricLine> ParseYrc(string yrc)
        {
            var list = new List<LyricLine>();
            foreach (var raw in yrc.Replace("\r", "").Split('\n'))
            {
                if (raw.StartsWith("{")) continue;   // служебные строки с авторами
                var m = YrcLine.Match(raw);
                if (!m.Success) continue;
                var words = new List<LyricWord>();
                foreach (Match w in YrcWord.Matches(m.Groups[3].Value))
                {
                    if (w.Groups[3].Value == "") continue;
                    double st = double.Parse(w.Groups[1].Value, CultureInfo.InvariantCulture), d = double.Parse(w.Groups[2].Value, CultureInfo.InvariantCulture);
                    words.Add(new LyricWord(TimeSpan.FromMilliseconds(st), TimeSpan.FromMilliseconds(st + d), w.Groups[3].Value));
                }
                var text = Regex.Replace(string.Concat(words.Select(w => w.Text)), @"\s+", " ").Trim();
                if (text == "") continue;
                list.Add(new LyricLine(TimeSpan.FromMilliseconds(double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)), text, words));
            }
            list.Sort((a, b) => a.Time.CompareTo(b.Time));
            return list;
        }

        static readonly Regex Credits = new(
            @"^\s*(作词|作曲|编曲|制作人|混音|母带|录音|和声|吉他|贝斯|鼓|弦乐|监制|出品|发行|词|曲|编|OP|SP|Lyrics?\s*(by)?|Lyricist|Music\s*(by)?|Composed\s*(by)?|Composer|Written\s*(by)?|Writer|Produced\s*(by)?|Producer|Arranged\s*(by)?|Arranger|Mixed\s*(by)?|Mastered\s*(by)?|Recorded\s*(by)?|Vocals?\s*(by)?|Слова|Музыка|Автор(ы)?\s*(слов|музыки)?|Аранжировка|Сведение|Мастеринг|Продюсер)\s*[:：]",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Убираем титры («作词: …», «Lyrics by: …»), строку «Название - Исполнитель» в начале,
        /// дубли с одинаковым временем, слишком короткие паузы; длинное вступление и проигрыши помечаем ♪.</summary>
        static List<LyricLine> Tidy(List<LyricLine> lines, string title, string artist)
        {
            var res = new List<LyricLine>();
            string nt = Norm(title), na = Norm(artist);
            foreach (var l in lines)
            {
                if (Credits.IsMatch(l.Text)) continue;
                if (l.Time.TotalSeconds < 20 && l.Text != "♪")
                {
                    var n = Norm(l.Text);
                    if (nt.Length > 0 && n.Contains(nt) && (na.Length == 0 || n.Contains(na) || n.Length <= nt.Length + 2)) continue;
                    // «Obijmy - Okean Elzy» — заголовок латиницей: части похожи на название и исполнителя
                    var hm = Regex.Match(l.Text, @"^(.+?)\s+[-–—]\s+(.+)$");
                    if (hm.Success && na.Length > 0 &&
                        ((Sim(hm.Groups[2].Value, artist) >= 0.7 && Sim(hm.Groups[1].Value, title) >= 0.5) ||
                         (Sim(hm.Groups[1].Value, artist) >= 0.7 && Sim(hm.Groups[2].Value, title) >= 0.5))) continue;
                }
                if (res.Count > 0 && Math.Abs((res[^1].Time - l.Time).TotalMilliseconds) < 15 && l.Text != "♪")
                {
                    if (res[^1].Text == "♪") res[^1] = l;   // пустая строка и текст в одно время — оставляем текст
                    continue;                              // вторая строка с тем же временем — обычно перевод
                }
                if (l.Text == "♪" && res.Count > 0 && res[^1].Text == "♪") continue;
                res.Add(l);
            }
            // ♪ имеет смысл только для заметной паузы
            for (int i = res.Count - 1; i >= 0; i--)
            {
                if (res[i].Text != "♪") continue;
                double gap = i + 1 < res.Count ? (res[i + 1].Time - res[i].Time).TotalSeconds : 99;
                if (gap < 4 || i == res.Count - 1) res.RemoveAt(i);
            }
            if (res.Count > 0 && res[0].Text != "♪" && res[0].Time.TotalSeconds >= 6) res.Insert(0, new LyricLine(TimeSpan.Zero, "♪"));
            return res;
        }

        // ------------------------------------------------------------------
        //  Кеш на диске: повторные прослушивания без интернета
        // ------------------------------------------------------------------

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
                var res = new LyricsResult
                {
                    Synced = r.GetProperty("s").GetBoolean(),
                    Source = (r.TryGetProperty("src", out var src) ? src.GetString() : null) is { Length: > 0 } s ? s + " · кеш" : "кеш",
                    Duration = r.TryGetProperty("d", out var d) ? d.GetDouble() : 0,
                    Pinned = r.TryGetProperty("p", out var p) && p.GetBoolean(),
                    Instrumental = r.TryGetProperty("i", out var i) && i.GetBoolean(),
                    Lines = new(),
                };
                foreach (var x in r.GetProperty("l").EnumerateArray())
                {
                    List<LyricWord> words = null;
                    if (x.GetArrayLength() > 2 && x[2].ValueKind == JsonValueKind.Array)
                        words = x[2].EnumerateArray().Select(w => new LyricWord(TimeSpan.FromMilliseconds(w[0].GetDouble()), TimeSpan.FromMilliseconds(w[1].GetDouble()), w[2].GetString())).ToList();
                    res.Lines.Add(new LyricLine(TimeSpan.FromMilliseconds(x[0].GetDouble()), x[1].GetString(), words));
                }
                return res;
            }
            catch { return null; }
        }

        static void WriteDisk(string key, LyricsResult r)
        {
            if (!Store.Settings.LyricsCache || r?.Lines == null) return;
            try
            {
                Directory.CreateDirectory(CacheDir);
                var src = r.Source?.Replace(" · кеш", "").Replace("кеш", "");
                var json = JsonSerializer.Serialize(new
                {
                    s = r.Synced, d = Math.Round(r.Duration, 1), src, p = r.Pinned, i = r.Instrumental,
                    l = r.Lines.Select(x => x.Words == null
                        ? new object[] { Math.Round(x.Time.TotalMilliseconds), x.Text }
                        : new object[] { Math.Round(x.Time.TotalMilliseconds), x.Text, x.Words.Select(w => new object[] { Math.Round(w.Start.TotalMilliseconds), Math.Round(w.End.TotalMilliseconds), w.Text }) }),
                });
                File.WriteAllText(FileFor(key), json);
                var files = new DirectoryInfo(CacheDir).GetFiles("*.json");
                if (files.Length > 500)
                    foreach (var old in files.OrderBy(f => f.LastWriteTime).Take(files.Length - 500)) old.Delete();
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

        /// <summary>Для самопроверки (--selftest lyrics): результат поиска без кеша.</summary>
        internal static Task<LyricsResult> Fresh(string title, string artist, string album, double dur, bool browser)
        {
            var q = Query.From(title, artist, album, browser);
            return q.Variants.Count == 0 ? Task.FromResult<LyricsResult>(null) : Load(q, dur);
        }

        /// <summary>Чистое «Название — Исполнитель» для скробблинга (без «Official Video», «feat.», «- Topic»).</summary>
        public static (string Title, string Artist) CleanForScrobble(string title, string artist, bool browser)
        {
            var q = Query.From(title, artist, "", browser);
            return q.Variants.Count == 0 ? (title ?? "", artist ?? "") : q.Variants[0];
        }

        internal static string DescribeQuery(string title, string artist, bool browser) =>
            string.Join(" ‖ ", Query.From(title, artist, "", browser).Variants.Select(v => v.Artist + " — " + v.Title));
    }
}
