using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using NIsland.Core;

namespace NIsland.Services
{
    /// <summary>Инструменты на открытых API (без ключей) и локальные утилиты.</summary>
    public static class ApiTools
    {
        static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        static ApiTools() => Http.DefaultRequestHeaders.UserAgent.ParseAdd("NIsland/1.3 (Windows)");

        /// <summary>Скорость интернета: задержка + загрузка ~20 МБ с Cloudflare.</summary>
        public static async Task<string> SpeedTest()
        {
            var sw = Stopwatch.StartNew();
            using (var r = await Http.GetAsync("https://speed.cloudflare.com/__down?bytes=0")) { }
            double ping = sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            long total = 0;
            using (var res = await Http.GetAsync("https://speed.cloudflare.com/__down?bytes=20000000", HttpCompletionOption.ResponseHeadersRead))
            using (var s = await res.Content.ReadAsStreamAsync())
            {
                var buf = new byte[81920];
                int n;
                while ((n = await s.ReadAsync(buf)) > 0) { total += n; if (sw.Elapsed.TotalSeconds > 12) break; }
            }
            double mbps = total * 8 / sw.Elapsed.TotalSeconds / 1e6;
            var speedRes = $"Загрузка ≈ {mbps:0.#} Мбит/с · задержка {ping:0} мс · скачано {total / 1e6:0.#} МБ (speed.cloudflare.com)";
            PcWatch.AddSpeed(speedRes);   // история скорости (/speedlog)
            return speedRes;
        }

        public static async Task<string> PublicIp()
        {
            using var doc = JsonDocument.Parse(await Http.GetStringAsync("https://ipwho.is/?lang=ru"));
            var r = doc.RootElement;
            string S(string k) => r.TryGetProperty(k, out var v) ? v.ToString() : "";
            string isp = r.TryGetProperty("connection", out var c) && c.TryGetProperty("isp", out var i) ? i.GetString() : "";
            return $"IP {S("ip")} · {S("city")}, {S("country")} · провайдер {isp}";
        }

        public static async Task<string> PingHost(string host)
        {
            host = string.IsNullOrWhiteSpace(host) ? "1.1.1.1" : host.Trim().Replace("https://", "").Replace("http://", "").Split('/')[0];
            using var p = new Ping();
            var times = new List<long>();
            int lost = 0;
            for (int i = 0; i < 4; i++)
            {
                try
                {
                    var r = await p.SendPingAsync(host, 2000);
                    if (r.Status == IPStatus.Success) times.Add(r.RoundtripTime); else lost++;
                }
                catch { lost++; }
            }
            return times.Count == 0 ? $"{host}: не отвечает" : $"{host}: среднее {times.Average():0} мс (мин {times.Min()}, макс {times.Max()}), потерь {lost} из 4";
        }

        public static async Task<string> Crypto()
        {
            var ids = string.Join(",", (Store.Settings.Crypto ?? "bitcoin").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            using var doc = JsonDocument.Parse(await Http.GetStringAsync($"https://api.coingecko.com/api/v3/simple/price?ids={Uri.EscapeDataString(ids)}&vs_currencies=usd&include_24hr_change=true"));
            var parts = new List<string>();
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                double usd = p.Value.TryGetProperty("usd", out var u) ? u.GetDouble() : 0;
                double ch = p.Value.TryGetProperty("usd_24h_change", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetDouble() : 0;
                var name = p.Name switch { "bitcoin" => "BTC", "ethereum" => "ETH", "the-open-network" => "TON", "solana" => "SOL", "tether" => "USDT", var x => x.ToUpperInvariant() };
                parts.Add($"{name} ${usd.ToString(usd >= 100 ? "N0" : "N2", Fmt.Ru)} ({(ch >= 0 ? "+" : "")}{ch:0.#}%)");
            }
            return parts.Count == 0 ? "Нет данных — проверь список монет в настройках" : string.Join("   ·   ", parts);
        }

        public static async Task<string> Wiki(string q)
        {
            if (string.IsNullOrWhiteSpace(q)) return "Введи запрос в поле сверху";
            var lang = q.Any(ch => ch >= 'А' && ch <= 'я') ? "ru" : "en";
            using var s = JsonDocument.Parse(await Http.GetStringAsync(
                $"https://{lang}.wikipedia.org/w/api.php?action=opensearch&limit=1&format=json&search={Uri.EscapeDataString(q.Trim())}"));
            if (s.RootElement[1].GetArrayLength() == 0) return "В Википедии ничего не нашлось";
            var title = s.RootElement[1][0].GetString();
            using var d = JsonDocument.Parse(await Http.GetStringAsync(
                $"https://{lang}.wikipedia.org/api/rest_v1/page/summary/{Uri.EscapeDataString(title.Replace(' ', '_'))}"));
            return $"{title}: {d.RootElement.GetProperty("extract").GetString()}";
        }

        public static async Task<BitmapImage> Qr(string text)
        {
            var bytes = await Http.GetByteArrayAsync("https://api.qrserver.com/v1/create-qr-code/?size=240x240&margin=8&data=" + Uri.EscapeDataString(text));
            var img = new BitmapImage();
            img.BeginInit();
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.StreamSource = new MemoryStream(bytes);
            img.EndInit();
            img.Freeze();
            return img;
        }

        public static async Task<string> Shorten(string url)
        {
            if (!url.StartsWith("http")) url = "https://" + url.Trim();
            var res = await Http.GetStringAsync("https://is.gd/create.php?format=simple&url=" + Uri.EscapeDataString(url.Trim()));
            return res.Trim();
        }

        public static async Task<string> SiteCheck(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return "Введи адрес сайта в поле сверху";
            if (!url.StartsWith("http")) url = "https://" + url.Trim();
            var sw = Stopwatch.StartNew();
            try
            {
                using var res = await Http.GetAsync(url.Trim(), HttpCompletionOption.ResponseHeadersRead);
                return $"{url}: {(int)res.StatusCode} {res.ReasonPhrase} · ответ за {sw.ElapsedMilliseconds} мс · {(res.IsSuccessStatusCode ? "работает ✓" : "есть проблема ⚠")}";
            }
            catch (Exception e) { return $"{url}: недоступен — {e.Message}"; }
        }

        public static async Task<string> Holidays()
        {
            var cc = string.IsNullOrWhiteSpace(Store.Settings.HolidayCountry) ? RegionInfo.CurrentRegion.TwoLetterISORegionName : Store.Settings.HolidayCountry.Trim().ToUpperInvariant();
            using var doc = JsonDocument.Parse(await Http.GetStringAsync($"https://date.nager.at/api/v3/NextPublicHolidays/{cc}"));
            var list = doc.RootElement.EnumerateArray().Take(3).Select(h =>
            {
                var d = DateTime.Parse(h.GetProperty("date").GetString(), Inv);
                int days = (int)(d.Date - DateTime.Today).TotalDays;
                return $"{d.ToString("d MMM", Fmt.Ru)} — {h.GetProperty("localName").GetString()} (через {days} дн.)";
            }).ToList();
            return list.Count == 0 ? $"Нет данных о праздниках для «{cc}»" : $"{cc}: " + string.Join("   ·   ", list);
        }

        public static async Task<string> AirQuality()
        {
            var w = await WeatherService.Get();
            if (w == null) return "Сначала нужна погода — проверь город в настройках";
            var url = string.Format(Inv, "https://air-quality-api.open-meteo.com/v1/air-quality?latitude={0}&longitude={1}&current=european_aqi,pm2_5,pm10", w.Lat, w.Lon);
            using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
            var c = doc.RootElement.GetProperty("current");
            double aqi = c.GetProperty("european_aqi").GetDouble();
            var level = aqi <= 20 ? "отличный" : aqi <= 40 ? "хороший" : aqi <= 60 ? "средний" : aqi <= 80 ? "плохой" : "очень плохой";
            return $"{w.City}: воздух {level} (AQI {aqi:0}) · PM2.5 {c.GetProperty("pm2_5").GetDouble():0.#} · PM10 {c.GetProperty("pm10").GetDouble():0.#} мкг/м³";
        }

        // ---------- локальные ----------
        public static string Password(int len = 18)
        {
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789!@#$%^&*-_=+?";
            var sb = new StringBuilder();
            for (int i = 0; i < len; i++) sb.Append(chars[RandomNumberGenerator.GetInt32(chars.Length)]);
            return sb.ToString();
        }

        public static string Hash(string text)
        {
            var b = Encoding.UTF8.GetBytes(text ?? "");
            return $"SHA-256: {Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant()}\nMD5: {Convert.ToHexString(MD5.HashData(b)).ToLowerInvariant()}";
        }

        public static string Base64(string text)
        {
            text ??= "";
            try
            {
                var raw = Convert.FromBase64String(text.Trim());
                var decoded = Encoding.UTF8.GetString(raw);
                if (decoded.All(ch => !char.IsControl(ch) || ch is '\n' or '\r' or '\t')) return decoded;
            }
            catch { }
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
        }

        public static string Uuid() => Guid.NewGuid().ToString();
    }
}
