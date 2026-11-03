using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using NIsland.Core;

namespace NIsland.Services
{
    /// <summary>Вторая партия инструментов: сеть, инфо, текст, конвертеры (всё без ключей).</summary>
    public static class ApiTools2
    {
        static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        static ApiTools2() => Http.DefaultRequestHeaders.UserAgent.ParseAdd("NIsland/1.4 (Windows)");

        // ---------- сеть ----------
        public static async Task<string> IpInfo(string ip)
        {
            if (string.IsNullOrWhiteSpace(ip)) return "Введи IP-адрес или домен в поле сверху";
            using var doc = JsonDocument.Parse(await Http.GetStringAsync("https://ipwho.is/" + Uri.EscapeDataString(ip.Trim()) + "?lang=ru"));
            var r = doc.RootElement;
            if (r.TryGetProperty("success", out var ok) && !ok.GetBoolean()) return "Не удалось: " + (r.TryGetProperty("message", out var m) ? m.GetString() : "неизвестный адрес");
            string S(string k) => r.TryGetProperty(k, out var v) ? v.ToString() : "";
            string isp = r.TryGetProperty("connection", out var c) && c.TryGetProperty("isp", out var i) ? i.GetString() : "";
            return $"{S("ip")} · {S("city")}, {S("region")}, {S("country")} · провайдер {isp} · тип {S("type")}";
        }

        /// <summary>DNS-записи через Cloudflare DNS-over-HTTPS.</summary>
        public static async Task<string> Dns(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Введи домен, например example.com";
            name = name.Trim().Replace("https://", "").Replace("http://", "").Split('/')[0];
            var parts = new List<string>();
            foreach (var type in new[] { "A", "AAAA", "MX", "NS" })
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, $"https://cloudflare-dns.com/dns-query?name={Uri.EscapeDataString(name)}&type={type}");
                req.Headers.Accept.ParseAdd("application/dns-json");
                using var res = await Http.SendAsync(req);
                using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
                if (!doc.RootElement.TryGetProperty("Answer", out var ans)) continue;
                var vals = ans.EnumerateArray().Select(a => a.GetProperty("data").GetString()).Take(4).ToList();
                if (vals.Count > 0) parts.Add($"{type}: {string.Join(", ", vals)}");
            }
            return parts.Count == 0 ? $"{name}: записей не найдено" : $"{name}\n" + string.Join("\n", parts);
        }

        // ---------- инфо ----------
        public static async Task<string> Rates()
        {
            var local = Currency.LocalCode;
            using var doc = JsonDocument.Parse(await Http.GetStringAsync("https://open.er-api.com/v6/latest/USD"));
            var r = doc.RootElement.GetProperty("rates");
            double loc = r.TryGetProperty(local, out var l) ? l.GetDouble() : 1;
            var codes = new[] { "USD", "EUR", "GBP", "CNY", "TRY", "PLN", "KZT", "RUB", "UAH" }.Where(c => c != local);
            var parts = codes.Where(c => r.TryGetProperty(c, out _))
                .Select(c => $"{c} {(loc / r.GetProperty(c).GetDouble()).ToString(loc / r.GetProperty(c).GetDouble() >= 10 ? "N2" : "N3", Fmt.Ru)}");
            return $"Курс к {local} (open.er-api.com):\n" + string.Join("  ·  ", parts);
        }

        public static async Task<string> News()
        {
            var url = string.IsNullOrWhiteSpace(Store.Settings.NewsFeed) ? "https://lenta.ru/rss/news" : Store.Settings.NewsFeed.Trim();
            var x = XDocument.Parse(await Http.GetStringAsync(url));
            var items = x.Descendants().Where(e => e.Name.LocalName is "item" or "entry").Take(6)
                .Select(e => "• " + e.Elements().FirstOrDefault(t => t.Name.LocalName == "title")?.Value.Trim()).ToList();
            return items.Count == 0 ? "Лента пуста — проверь адрес RSS в настройках" : string.Join("\n", items);
        }

        public static async Task<string> Fact()
        {
            using var doc = JsonDocument.Parse(await Http.GetStringAsync("https://uselessfacts.jsph.pl/api/v2/facts/random?language=en"));
            var en = doc.RootElement.GetProperty("text").GetString();
            try { var (ru, _) = await TranslateService.Raw(en, "ru"); return "🤓 " + ru; }
            catch { return "🤓 " + en; }
        }

        public static async Task<string> Dictionary(string word)
        {
            if (string.IsNullOrWhiteSpace(word)) return "Введи английское слово";
            using var doc = JsonDocument.Parse(await Http.GetStringAsync("https://api.dictionaryapi.dev/api/v2/entries/en/" + Uri.EscapeDataString(word.Trim().ToLowerInvariant())));
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return "Слово не найдено";
            var e = doc.RootElement[0];
            var phon = e.TryGetProperty("phonetic", out var p) ? p.GetString() : "";
            var sb = new StringBuilder($"{e.GetProperty("word").GetString()} {phon}\n");
            foreach (var m in e.GetProperty("meanings").EnumerateArray().Take(3))
                sb.Append($"{m.GetProperty("partOfSpeech").GetString()}: {m.GetProperty("definitions")[0].GetProperty("definition").GetString()}\n");
            return sb.ToString().Trim();
        }

        public static async Task<string> Sun()
        {
            var w = await WeatherService.Get();
            if (w == null) return "Нужен город — проверь его в настройках погоды";
            var url = string.Format(Inv, "https://api.open-meteo.com/v1/forecast?latitude={0}&longitude={1}&daily=sunrise,sunset,daylight_duration&timezone=auto&forecast_days=1", w.Lat, w.Lon);
            using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
            var d = doc.RootElement.GetProperty("daily");
            var rise = DateTime.Parse(d.GetProperty("sunrise")[0].GetString(), Inv);
            var set = DateTime.Parse(d.GetProperty("sunset")[0].GetString(), Inv);
            var len = TimeSpan.FromSeconds(d.GetProperty("daylight_duration")[0].GetDouble());
            return $"{w.City}: восход {rise:HH:mm} · закат {set:HH:mm} · световой день {(int)len.TotalHours} ч {len.Minutes} мин";
        }

        /// <summary>Фаза луны — считается локально.</summary>
        public static string Moon()
        {
            const double synodic = 29.530588853;
            var newMoon = new DateTime(2000, 1, 6, 18, 14, 0, DateTimeKind.Utc);
            double age = ((DateTime.UtcNow - newMoon).TotalDays % synodic + synodic) % synodic;
            double illum = (1 - Math.Cos(2 * Math.PI * age / synodic)) / 2;
            var (icon, name) = age switch
            {
                < 1.85 => ("🌑", "Новолуние"),
                < 5.54 => ("🌒", "Растущий серп"),
                < 9.23 => ("🌓", "Первая четверть"),
                < 12.92 => ("🌔", "Растущая луна"),
                < 16.61 => ("🌕", "Полнолуние"),
                < 20.30 => ("🌖", "Убывающая луна"),
                < 23.99 => ("🌗", "Последняя четверть"),
                < 27.68 => ("🌘", "Убывающий серп"),
                _ => ("🌑", "Новолуние"),
            };
            double toFull = (14.77 - age + synodic) % synodic;
            return $"{icon} {name} · освещена на {illum * 100:0}% · возраст {age:0.#} дн. · до полнолуния {toFull:0} дн.";
        }

        // ---------- текст ----------
        public static string Case(string t)
        {
            if (string.IsNullOrEmpty(t)) return "Введи текст или скопируй его";
            var title = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(t.ToLower());
            var sentence = t.Length > 0 ? char.ToUpper(t[0]) + t[1..].ToLower() : t;
            return $"ВЕРХНИЙ: {t.ToUpper()}\nнижний: {t.ToLower()}\nКаждое Слово: {title}\nКак в предложении: {sentence}";
        }

        static readonly string[] Ru = { "а", "б", "в", "г", "д", "е", "ё", "ж", "з", "и", "й", "к", "л", "м", "н", "о", "п", "р", "с", "т", "у", "ф", "х", "ц", "ч", "ш", "щ", "ъ", "ы", "ь", "э", "ю", "я" };
        static readonly string[] Lat = { "a", "b", "v", "g", "d", "e", "yo", "zh", "z", "i", "y", "k", "l", "m", "n", "o", "p", "r", "s", "t", "u", "f", "kh", "ts", "ch", "sh", "shch", "", "y", "", "e", "yu", "ya" };

        public static string Translit(string t)
        {
            if (string.IsNullOrEmpty(t)) return "Введи текст";
            bool cyr = t.Any(c => c >= 'А' && c <= 'я' || c == 'ё' || c == 'Ё');
            var sb = new StringBuilder();
            if (cyr)
            {
                foreach (var ch in t)
                {
                    int i = Array.IndexOf(Ru, char.ToLower(ch).ToString());
                    if (i < 0) { sb.Append(ch); continue; }
                    var l = Lat[i];
                    sb.Append(char.IsUpper(ch) && l.Length > 0 ? char.ToUpper(l[0]) + l[1..] : l);
                }
                return sb.ToString();
            }
            // латиница → кириллица: сначала длинные сочетания
            var pairs = Lat.Select((l, i) => (l, r: Ru[i])).Where(p => p.l != "").OrderByDescending(p => p.l.Length).ToList();
            int pos = 0;
            while (pos < t.Length)
            {
                bool done = false;
                foreach (var (l, r) in pairs)
                {
                    if (pos + l.Length <= t.Length && string.Compare(t, pos, l, 0, l.Length, StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        sb.Append(char.IsUpper(t[pos]) ? r.ToUpper() : r);
                        pos += l.Length;
                        done = true;
                        break;
                    }
                }
                if (!done) sb.Append(t[pos++]);
            }
            return sb.ToString();
        }

        public static string Stats(string t)
        {
            if (string.IsNullOrEmpty(t)) return "Введи или скопируй текст";
            int words = Regex.Matches(t, @"[\p{L}\p{N}]+").Count;
            int sentences = Math.Max(1, Regex.Matches(t, @"[.!?…]+(\s|$)").Count);
            int lines = t.Split('\n').Length;
            int noSpaces = t.Count(c => !char.IsWhiteSpace(c));
            double minutes = words / 180.0;
            return $"Символов: {t.Length} (без пробелов {noSpaces}) · слов: {words} · предложений: {sentences} · строк: {lines} · чтение ≈ {Math.Max(1, Math.Round(minutes))} мин";
        }

        public static string Json(string t)
        {
            if (string.IsNullOrWhiteSpace(t)) return "Вставь JSON в поле или скопируй его";
            try
            {
                using var doc = JsonDocument.Parse(t);
                return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            }
            catch (JsonException e) { return "Ошибка в JSON: " + e.Message; }
        }

        // ---------- конвертеры ----------
        static readonly Dictionary<string, (string Kind, double Factor)> Units = new(StringComparer.OrdinalIgnoreCase)
        {
            ["mm"] = ("len", 0.001), ["мм"] = ("len", 0.001), ["cm"] = ("len", 0.01), ["см"] = ("len", 0.01), ["m"] = ("len", 1), ["м"] = ("len", 1),
            ["km"] = ("len", 1000), ["км"] = ("len", 1000), ["in"] = ("len", 0.0254), ["дюйм"] = ("len", 0.0254), ["дюймы"] = ("len", 0.0254), ["дюймов"] = ("len", 0.0254),
            ["ft"] = ("len", 0.3048), ["фут"] = ("len", 0.3048), ["футы"] = ("len", 0.3048), ["футов"] = ("len", 0.3048), ["yd"] = ("len", 0.9144), ["ярд"] = ("len", 0.9144),
            ["mi"] = ("len", 1609.344), ["миля"] = ("len", 1609.344), ["мили"] = ("len", 1609.344), ["миль"] = ("len", 1609.344),
            ["g"] = ("mass", 0.001), ["г"] = ("mass", 0.001), ["kg"] = ("mass", 1), ["кг"] = ("mass", 1), ["t"] = ("mass", 1000), ["т"] = ("mass", 1000),
            ["lb"] = ("mass", 0.45359237), ["фунт"] = ("mass", 0.45359237), ["фунты"] = ("mass", 0.45359237), ["фунтов"] = ("mass", 0.45359237),
            ["oz"] = ("mass", 0.0283495), ["унция"] = ("mass", 0.0283495), ["унций"] = ("mass", 0.0283495),
            ["ml"] = ("vol", 0.001), ["мл"] = ("vol", 0.001), ["l"] = ("vol", 1), ["л"] = ("vol", 1), ["gal"] = ("vol", 3.78541), ["галлон"] = ("vol", 3.78541),
            ["kmh"] = ("speed", 1 / 3.6), ["км/ч"] = ("speed", 1 / 3.6), ["mph"] = ("speed", 0.44704), ["ms"] = ("speed", 1), ["м/с"] = ("speed", 1), ["узел"] = ("speed", 0.514444),
            ["b"] = ("data", 1), ["б"] = ("data", 1), ["kb"] = ("data", 1024), ["кб"] = ("data", 1024), ["mb"] = ("data", 1048576), ["мб"] = ("data", 1048576),
            ["gb"] = ("data", 1073741824), ["гб"] = ("data", 1073741824), ["tb"] = ("data", 1099511627776), ["тб"] = ("data", 1099511627776),
            ["c"] = ("temp", 0), ["°c"] = ("temp", 0), ["с"] = ("temp", 0), ["цельсий"] = ("temp", 0), ["f"] = ("temp", 1), ["°f"] = ("temp", 1), ["ф"] = ("temp", 1), ["k"] = ("temp", 2), ["к"] = ("temp", 2),
        };

        public static string ConvertUnits(string q)
        {
            var m = Regex.Match(q ?? "", @"^\s*(-?[\d.,]+)\s*([^\d\s]+)\s+(?:в|in|to|->|=)\s+([^\d\s]+)\s*$", RegexOptions.IgnoreCase);
            if (!m.Success) return "Формат: «10 km в mi», «5 кг в фунты», «100 c в f», «2 гб в мб»";
            double v = double.Parse(m.Groups[1].Value.Replace(',', '.'), Inv);
            if (!Units.TryGetValue(m.Groups[2].Value, out var from) || !Units.TryGetValue(m.Groups[3].Value, out var to)) return "Не знаю таких единиц";
            if (from.Kind != to.Kind) return "Эти единицы нельзя перевести друг в друга";
            double res;
            if (from.Kind == "temp")
            {
                double c = from.Factor switch { 0 => v, 1 => (v - 32) * 5 / 9, _ => v - 273.15 };
                res = to.Factor switch { 0 => c, 1 => c * 9 / 5 + 32, _ => c + 273.15 };
            }
            else res = v * from.Factor / to.Factor;
            return $"{v.ToString("G6", Fmt.Ru)} {m.Groups[2].Value} = {Math.Round(res, 6).ToString("G8", Fmt.Ru)} {m.Groups[3].Value}";
        }

        public static string Unix(string q)
        {
            q = q?.Trim() ?? "";
            if (q == "") return $"Сейчас: {DateTimeOffset.UtcNow.ToUnixTimeSeconds()} (Unix, секунды)";
            if (long.TryParse(q, out var n))
            {
                var d = n > 100000000000 ? DateTimeOffset.FromUnixTimeMilliseconds(n) : DateTimeOffset.FromUnixTimeSeconds(n);
                return $"{n} → {d.ToLocalTime():dd.MM.yyyy HH:mm:ss} (у тебя) · {d.UtcDateTime:yyyy-MM-dd HH:mm:ss} UTC";
            }
            if (DateTime.TryParse(q, Fmt.Ru, DateTimeStyles.AssumeLocal, out var dt) || DateTime.TryParse(q, Inv, DateTimeStyles.AssumeLocal, out dt))
                return $"{dt:dd.MM.yyyy HH:mm:ss} → {new DateTimeOffset(dt).ToUnixTimeSeconds()}";
            return "Введи число Unix-времени или дату";
        }

        public static string Color(string q)
        {
            q = q?.Trim() ?? "";
            var hex = Regex.Match(q, @"^#?([0-9a-fA-F]{6}|[0-9a-fA-F]{3})$");
            int r, g, b;
            if (hex.Success)
            {
                var h = hex.Groups[1].Value;
                if (h.Length == 3) h = string.Concat(h.Select(c => $"{c}{c}"));
                r = Convert.ToInt32(h[..2], 16); g = Convert.ToInt32(h[2..4], 16); b = Convert.ToInt32(h[4..], 16);
            }
            else
            {
                var nums = Regex.Matches(q, @"\d{1,3}").Select(x => int.Parse(x.Value)).ToList();
                if (nums.Count < 3) return "Введи цвет: #FF8800, FF8800 или 255, 136, 0";
                (r, g, b) = (Math.Min(255, nums[0]), Math.Min(255, nums[1]), Math.Min(255, nums[2]));
            }
            double rf = r / 255.0, gf = g / 255.0, bf = b / 255.0, max = Math.Max(rf, Math.Max(gf, bf)), min = Math.Min(rf, Math.Min(gf, bf));
            double l = (max + min) / 2, s = max == min ? 0 : l > 0.5 ? (max - min) / (2 - max - min) : (max - min) / (max + min), hh = 0;
            if (max != min)
                hh = max == rf ? (gf - bf) / (max - min) + (gf < bf ? 6 : 0) : max == gf ? (bf - rf) / (max - min) + 2 : (rf - gf) / (max - min) + 4;
            return $"HEX #{r:X2}{g:X2}{b:X2} · RGB {r}, {g}, {b} · HSL {hh * 60:0}°, {s * 100:0}%, {l * 100:0}%";
        }
    }
}
