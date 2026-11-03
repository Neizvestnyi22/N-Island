using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NIsland.Core;

namespace NIsland.Services
{
    // =====================================================================
    //  Переводчик (публичный endpoint Google Translate, без ключа)
    // =====================================================================
    public static class TranslateService
    {
        static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

        public static readonly (string Code, string Name)[] Langs =
        {
            ("ru", "Русский"), ("en", "English"), ("uk", "Українська"), ("de", "Deutsch"),
            ("es", "Español"), ("fr", "Français"), ("tr", "Türkçe"), ("zh-CN", "中文"),
        };

        public static string Name(string code) => Langs.FirstOrDefault(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase)).Name ?? code;

        /// <summary>(перевод, язык оригинала). Если оригинал уже на целевом языке — переводим на английский.</summary>
        public static async Task<(string Text, string From, string To)> Translate(string text, string to)
        {
            text = text?.Trim() ?? "";
            if (text == "") return ("", "", to);
            var (res, from) = await Raw(text, to);
            if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase) || (to == "ru" && from == "ru"))
            {
                var alt = to == "en" ? "ru" : "en";
                (res, from) = await Raw(text, alt);
                to = alt;
            }
            return (res, from, to);
        }

        public static async Task<(string Text, string From)> Raw(string text, string to)
        {
            var url = "https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&dt=t&tl=" + Uri.EscapeDataString(to)
                      + "&q=" + Uri.EscapeDataString(text.Length > 4000 ? text[..4000] : text);
            using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
            var sb = new StringBuilder();
            foreach (var seg in doc.RootElement[0].EnumerateArray())
                if (seg.ValueKind == JsonValueKind.Array && seg[0].ValueKind == JsonValueKind.String) sb.Append(seg[0].GetString());
            string from = doc.RootElement.GetArrayLength() > 2 && doc.RootElement[2].ValueKind == JsonValueKind.String ? doc.RootElement[2].GetString() : "";
            return (sb.ToString(), from);
        }
    }

    // =====================================================================
    //  Курсы валют (open.er-api.com, без ключа): «100 usd», «50 евро в рубли»
    // =====================================================================
    public static class Currency
    {
        static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };
        static readonly Dictionary<string, (DateTime At, JsonElement Rates)> Cache = new();

        static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase)
        {
            ["$"] = "USD", ["usd"] = "USD", ["долл"] = "USD", ["доллар"] = "USD", ["доллара"] = "USD", ["долларов"] = "USD", ["доллары"] = "USD", ["бакс"] = "USD", ["баксов"] = "USD",
            ["€"] = "EUR", ["eur"] = "EUR", ["евро"] = "EUR",
            ["₽"] = "RUB", ["rub"] = "RUB", ["руб"] = "RUB", ["рубль"] = "RUB", ["рубля"] = "RUB", ["рублей"] = "RUB", ["рубли"] = "RUB",
            ["₴"] = "UAH", ["uah"] = "UAH", ["грн"] = "UAH", ["гривна"] = "UAH", ["гривен"] = "UAH", ["гривны"] = "UAH",
            ["kzt"] = "KZT", ["тенге"] = "KZT", ["₸"] = "KZT",
            ["byn"] = "BYN", ["бел"] = "BYN",
            ["£"] = "GBP", ["gbp"] = "GBP", ["фунт"] = "GBP", ["фунтов"] = "GBP", ["фунты"] = "GBP",
            ["cny"] = "CNY", ["юань"] = "CNY", ["юаней"] = "CNY", ["юани"] = "CNY",
            ["try"] = "TRY", ["лира"] = "TRY", ["лир"] = "TRY", ["лиры"] = "TRY",
            ["jpy"] = "JPY", ["иена"] = "JPY", ["иен"] = "JPY", ["йен"] = "JPY",
            ["pln"] = "PLN", ["злотый"] = "PLN", ["злотых"] = "PLN",
            ["chf"] = "CHF", ["франк"] = "CHF", ["франков"] = "CHF",
            ["gel"] = "GEL", ["лари"] = "GEL", ["amd"] = "AMD", ["драм"] = "AMD", ["uzs"] = "UZS", ["сум"] = "UZS",
            ["aed"] = "AED", ["дирхам"] = "AED", ["cad"] = "CAD", ["aud"] = "AUD", ["czk"] = "CZK", ["krw"] = "KRW", ["inr"] = "INR",
        };

        static readonly Regex Rx = new(@"^\s*(\d+(?:[.,]\d+)?)\s*([^\d\s]{1,9})\s*(?:(?:в|to|in|->|=)\s*([^\d\s]{1,9}))?\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static string LocalCode
        {
            get
            {
                try { return new RegionInfo(CultureInfo.CurrentCulture.Name).ISOCurrencySymbol; }
                catch { return "USD"; }
            }
        }

        static string Code(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            if (Names.TryGetValue(s, out var c)) return c;
            if (s.Length == 3 && s.All(char.IsLetter) && s.All(ch => ch < 128)) return s.ToUpperInvariant();
            return null;
        }

        public static (double Amount, string From, string To)? Parse(string q)
        {
            var m = Rx.Match(q ?? "");
            if (!m.Success) return null;
            var from = Code(m.Groups[2].Value);
            if (from == null) return null;
            var to = m.Groups[3].Success ? Code(m.Groups[3].Value) : null;
            to ??= from == LocalCode ? (from == "USD" ? "EUR" : "USD") : LocalCode;
            if (!double.TryParse(m.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var amount)) return null;
            return (amount, from, to);
        }

        public static async Task<double?> Convert(double amount, string from, string to)
        {
            try
            {
                if (!Cache.TryGetValue(from, out var c) || (DateTime.Now - c.At).TotalHours > 3)
                {
                    using var doc = JsonDocument.Parse(await Http.GetStringAsync("https://open.er-api.com/v6/latest/" + from));
                    c = (DateTime.Now, doc.RootElement.GetProperty("rates").Clone());
                    Cache[from] = c;
                }
                return c.Rates.TryGetProperty(to, out var r) ? amount * r.GetDouble() : null;
            }
            catch { return null; }
        }

        public static string Fmt(double v, string code) =>
            v.ToString(v >= 100 ? "N0" : "N2", CultureInfo.GetCultureInfo("ru-RU")) + " " + code;
    }

    // =====================================================================
    //  Флешки и внешние диски
    // =====================================================================
    public static class Drives
    {
        static HashSet<string> _known;

        /// <summary>(новые, отключённые)</summary>
        public static (List<DriveInfo> Added, List<string> Removed) Poll()
        {
            var now = new Dictionary<string, DriveInfo>();
            try
            {
                foreach (var d in DriveInfo.GetDrives())
                    if (d.DriveType == DriveType.Removable && d.IsReady) now[d.Name] = d;
            }
            catch { }
            var added = new List<DriveInfo>();
            var removed = new List<string>();
            if (_known != null)
            {
                added.AddRange(now.Values.Where(d => !_known.Contains(d.Name)));
                removed.AddRange(_known.Where(n => !now.ContainsKey(n)));
            }
            _known = now.Keys.ToHashSet();
            return (added, removed);
        }

        public static string Label(DriveInfo d)
        {
            string label;
            try { label = string.IsNullOrWhiteSpace(d.VolumeLabel) ? "Флешка" : d.VolumeLabel; } catch { label = "Флешка"; }
            return $"{label} ({d.Name.TrimEnd('\\')})";
        }

        public static string Free(DriveInfo d)
        {
            try { return $"свободно {d.AvailableFreeSpace / 1e9:0.#} из {d.TotalSize / 1e9:0.#} ГБ"; } catch { return ""; }
        }
    }
}
