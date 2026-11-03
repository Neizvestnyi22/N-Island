using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using NIsland.Core;

namespace NIsland.Services
{
    /// <summary>
    /// OSINT и проверки безопасности по открытым источникам: ссылки (фишинг, сокращатели), домены (RDAP/WHOIS, DNS, сертификаты,
    /// поддомены по Certificate Transparency, архив Wayback), IP-адреса, заголовки безопасности сайтов, утечка пароля (k-анонимность —
    /// пароль никуда не уходит), метаданные и GPS в фото, хэш файла для VirusTotal. Только публичные данные, без ключей.
    /// </summary>
    public static class Osint
    {
        static readonly HttpClient Http = Make(true);
        static readonly HttpClient NoRedirect = Make(false);

        static HttpClient Make(bool redirects)
        {
            var h = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = redirects, AutomaticDecompression = DecompressionMethods.All, ConnectTimeout = TimeSpan.FromSeconds(8) })
            { Timeout = TimeSpan.FromSeconds(18) };
            h.DefaultRequestHeaders.UserAgent.ParseAdd($"Mozilla/5.0 (Windows NT 10.0; Win64; x64) NIsland/{AppInfo.Version}");
            return h;
        }

        /// <summary>Что передать из быстрого поиска в раздел OSINT.</summary>
        public static string Pending;

        public static string HostOf(string input)
        {
            input = (input ?? "").Trim();
            if (input == "") return "";
            if (!input.Contains("://")) input = "http://" + input;
            return Uri.TryCreate(input, UriKind.Absolute, out var u) ? u.IdnHost.ToLowerInvariant() : input;
        }

        static string Url(string input)
        {
            input = (input ?? "").Trim();
            return input.Contains("://") ? input : "https://" + input;
        }

        /// <summary>Регистрируемый домен: «login.paypal.com.evil.xyz» → «evil.xyz», «shop.com.ua» → «shop.com.ua».</summary>
        public static string Registrable(string host)
        {
            var p = host.Split('.');
            if (p.Length <= 2) return host;
            bool second = p[^2] is "com" or "net" or "org" or "gov" or "edu" or "co" or "ac" or "in" or "kiev" or "kharkov" or "od" or "lviv" && p[^1].Length == 2;
            return string.Join(".", p.Skip(p.Length - (second ? 3 : 2)));
        }

        // ------------------------------------------------------------------
        //  Ссылка: куда ведёт и похожа ли на фишинг
        // ------------------------------------------------------------------

        /// <summary>Пройти по цепочке редиректов, не открывая сайт в браузере (сокращатели bit.ly, t.co, clck.ru…).</summary>
        public static async Task<List<string>> Unshorten(string url)
        {
            var chain = new List<string> { Url(url) };
            for (int i = 0; i < 10; i++)
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, chain[^1]);
                using var resp = await NoRedirect.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
                if ((int)resp.StatusCode is >= 300 and < 400 && resp.Headers.Location is { } loc)
                {
                    var next = loc.IsAbsoluteUri ? loc.ToString() : new Uri(new Uri(chain[^1]), loc).ToString();
                    if (chain.Contains(next)) break;
                    chain.Add(next);
                }
                else break;
            }
            return chain;
        }

        static readonly string[] Brands =
        {
            "google", "gmail", "youtube", "apple", "icloud", "microsoft", "outlook", "office", "paypal", "facebook", "instagram", "whatsapp", "telegram",
            "tiktok", "twitter", "netflix", "amazon", "steam", "steamcommunity", "binance", "privat24", "privatbank", "monobank", "novaposhta", "olx",
            "rozetka", "diia", "sberbank", "tinkoff", "vk", "yandex", "gosuslugi", "kaspi", "discord", "roblox", "epicgames", "spotify", "metamask",
        };

        static readonly string[] RiskyTlds = { "zip", "mov", "top", "xyz", "click", "gq", "tk", "ml", "cf", "ga", "work", "rest", "cam", "buzz", "monster", "sbs", "cfd", "lol", "icu", "support" };

        public record LinkReport(string Final, List<string> Chain, int Risk, string Verdict, List<(bool Bad, string Text)> Signals);

        public static async Task<LinkReport> CheckLink(string input)
        {
            var signals = new List<(bool, string)>();
            int risk = 0;
            List<string> chain;
            bool down = false;
            try { chain = await Unshorten(input); }
            catch (Exception e)
            {
                // сайт не отвечает — адрес всё равно разбираем: фишинговые домены часто живут считанные дни
                chain = new() { Url(input) };
                down = true;
                risk += 1;
                signals.Add((true, "Сайт не отвечает или домена уже нет (" + Integrations.Reason(e) + ")"));
            }
            var final = chain[^1];
            if (chain.Count > 1) signals.Add((chain.Count > 3, $"Ссылка ведёт через {chain.Count - 1} переадресаций"));
            var uri = new Uri(final);
            var host = uri.IdnHost.ToLowerInvariant();
            var reg = Registrable(host);

            if (uri.Scheme != "https") { risk += 2; signals.Add((true, "Без шифрования (http) — пароли и карты туда вводить нельзя")); }
            else signals.Add((false, "Соединение зашифровано (https)"));
            if (IPAddress.TryParse(host, out _)) { risk += 3; signals.Add((true, "Вместо названия сайта — голый IP-адрес")); }
            if (host.Split('.').Any(l => l.StartsWith("xn--"))) { risk += 3; signals.Add((true, "Домен с подменёнными буквами (punycode): " + uri.Host)); }
            var brand = Brands.FirstOrDefault(b => host.Contains(b) && !reg.StartsWith(b + ".") && reg != b);
            if (brand != null) { risk += 4; signals.Add((true, $"В адресе есть «{brand}», но сайт на самом деле — {reg}")); }
            if (host.Count(c => c == '.') >= 4) { risk += 1; signals.Add((true, "Очень длинный адрес из многих частей")); }
            if (host.Count(c => c == '-') >= 3) { risk += 1; signals.Add((true, "Много дефисов в домене — частый приём фишинга")); }
            if (RiskyTlds.Contains(reg.Split('.').Last())) { risk += 1; signals.Add((true, $"Зона .{reg.Split('.').Last()} часто используется для мошенничества")); }

            var age = await DomainAge(reg);
            if (age is double days)
            {
                if (days < 30) { risk += 4; signals.Add((true, $"Домен зарегистрирован {Math.Max(0, (int)days)} дн. назад — очень свежий")); }
                else if (days < 180) { risk += 2; signals.Add((true, $"Домену всего {(int)(days / 30)} мес.")); }
                else signals.Add((false, $"Домену {days / 365:0.#} лет"));
            }
            else if (down) { risk += 1; signals.Add((true, "О домене нет сведений о регистрации")); }
            try
            {
                var wb = await Wayback(final);
                if (wb.Count == 0) { risk += 1; signals.Add((true, "Сайта нет в веб-архиве — у него нет истории")); }
                else signals.Add((false, $"В веб-архиве с {wb.First} ({wb.Count}+ снимков)"));
            }
            catch { }

            string verdict = risk >= 6 ? "Опасно: очень похоже на фишинг — не вводи там данные" : risk >= 3 ? "Подозрительно — будь осторожен" : "Явных признаков обмана не найдено";
            return new LinkReport(final, chain, risk, verdict, signals);
        }

        // ------------------------------------------------------------------
        //  Домен: RDAP (современный WHOIS), DNS, сертификат, поддомены, архив
        // ------------------------------------------------------------------

        static async Task<JsonDocument> Json(string url, Action<HttpRequestMessage> setup = null)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            setup?.Invoke(req);
            using var resp = await Http.SendAsync(req);
            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode) throw new HttpRequestException(Integrations.Snip(body), null, resp.StatusCode);
            return JsonDocument.Parse(body);
        }

        static string S(JsonElement e, string k) => Integrations.S(e, k);

        public static async Task<double?> DomainAge(string domain)
        {
            try
            {
                using var doc = await Json("https://rdap.org/domain/" + domain);
                foreach (var ev in doc.RootElement.GetProperty("events").EnumerateArray())
                    if (S(ev, "eventAction") == "registration" && DateTime.TryParse(S(ev, "eventDate"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var d))
                        return (DateTime.UtcNow - d).TotalDays;
            }
            catch { }
            return null;
        }

        public static async Task<List<(string K, string V)>> Whois(string input)
        {
            var domain = Registrable(HostOf(input));
            var res = new List<(string, string)> { ("Домен", domain) };
            try
            {
                using var doc = await Json("https://rdap.org/domain/" + domain);
                var r = doc.RootElement;
                foreach (var ev in r.TryGetProperty("events", out var evs) ? evs.EnumerateArray() : Enumerable.Empty<JsonElement>())
                {
                    var when = DateTime.TryParse(S(ev, "eventDate"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d.ToString("d MMMM yyyy", Fmt.Ru) : S(ev, "eventDate");
                    var name = S(ev, "eventAction") switch { "registration" => "Зарегистрирован", "expiration" => "Оплачен до", "last changed" => "Изменён", _ => null };
                    if (name != null) res.Add((name, when));
                }
                if (r.TryGetProperty("entities", out var ents))
                    foreach (var en in ents.EnumerateArray())
                        if (en.TryGetProperty("roles", out var roles) && roles.EnumerateArray().Any(x => x.GetString() == "registrar"))
                            res.Add(("Регистратор", VcardName(en)));
                if (r.TryGetProperty("nameservers", out var ns))
                    res.Add(("Серверы имён", string.Join(", ", ns.EnumerateArray().Select(x => S(x, "ldhName").ToLowerInvariant()))));
                if (r.TryGetProperty("status", out var st))
                    res.Add(("Статус", string.Join(", ", st.EnumerateArray().Select(x => x.GetString()))));
            }
            catch (Exception e) { res.Add(("WHOIS", "нет данных RDAP для этой зоны (" + Integrations.Reason(e) + ")")); }
            return res;
        }

        static string VcardName(JsonElement en)
        {
            try
            {
                foreach (var item in en.GetProperty("vcardArray")[1].EnumerateArray())
                    if (item[0].GetString() == "fn") return item[3].GetString();
            }
            catch { }
            return S(en, "handle");
        }

        /// <summary>DNS через DNS-over-HTTPS Cloudflare: A, AAAA, MX, NS, TXT, CAA и политика почты DMARC.</summary>
        public static async Task<List<(string K, string V)>> DnsRecords(string input)
        {
            var host = HostOf(input);
            var res = new List<(string, string)>();
            foreach (var type in new[] { "A", "AAAA", "CNAME", "MX", "NS", "TXT", "CAA" })
            {
                try
                {
                    using var doc = await Json($"https://cloudflare-dns.com/dns-query?name={Uri.EscapeDataString(host)}&type={type}", r => r.Headers.Accept.ParseAdd("application/dns-json"));
                    if (!doc.RootElement.TryGetProperty("Answer", out var ans)) continue;
                    var vals = ans.EnumerateArray().Select(a => S(a, "data").Trim('"')).Where(v => v != "").Distinct().ToList();
                    if (vals.Count > 0) res.Add((type, string.Join("\n", vals.Take(12))));
                }
                catch { }
            }
            try
            {
                using var doc = await Json($"https://cloudflare-dns.com/dns-query?name=_dmarc.{Registrable(host)}&type=TXT", r => r.Headers.Accept.ParseAdd("application/dns-json"));
                var dmarc = doc.RootElement.TryGetProperty("Answer", out var ans) ? ans.EnumerateArray().Select(a => S(a, "data").Trim('"')).FirstOrDefault(v => v.StartsWith("v=DMARC")) : null;
                res.Add(("DMARC", dmarc ?? "нет — письма от имени этого домена легче подделать"));
            }
            catch { }
            if (res.Count == 0) res.Add(("DNS", "записей не найдено — домена может не существовать"));
            return res;
        }

        /// <summary>Сертификат сайта прямо сейчас: кому выдан, кем, до какого числа, на какие имена.</summary>
        public static async Task<List<(string K, string V)>> Certificate(string input)
        {
            var host = HostOf(input);
            using var tcp = new TcpClient();
            var connect = tcp.ConnectAsync(host, 443);
            if (await Task.WhenAny(connect, Task.Delay(8000)) != connect) throw new TimeoutException("сайт не отвечает на порту 443");
            await connect;
            X509Certificate2 cert = null;
            SslPolicyErrors errors = SslPolicyErrors.None;
            using var ssl = new SslStream(tcp.GetStream(), false, (_, c, __, e) => { cert = c == null ? null : new X509Certificate2(c); errors = e; return true; });
            await ssl.AuthenticateAsClientAsync(host);
            if (cert == null) throw new InvalidOperationException("сертификат не получен");
            var sans = cert.Extensions.Cast<X509Extension>().FirstOrDefault(x => x.Oid?.Value == "2.5.29.17")?.Format(false) ?? "";
            int left = (int)(cert.NotAfter - DateTime.Now).TotalDays;
            var res = new List<(string, string)>
            {
                ("Выдан для", cert.GetNameInfo(X509NameType.SimpleName, false)),
                ("Кем выдан", cert.GetNameInfo(X509NameType.SimpleName, true)),
                ("Действует", $"{cert.NotBefore.ToString("d MMM yyyy", Fmt.Ru)} — {cert.NotAfter.ToString("d MMM yyyy", Fmt.Ru)} (осталось {left} дн.)"),
                ("Проверка Windows", errors == SslPolicyErrors.None ? "в порядке ✓" : "ОШИБКА: " + errors),
                ("Протокол", ssl.SslProtocol.ToString()),
            };
            if (sans != "") res.Add(("Имена", Regex.Replace(sans, @"DNS Name=|DNS-имя=|Ім.я DNS=", "").Replace(", ", "\n")));
            return res;
        }

        /// <summary>Поддомены из журналов Certificate Transparency (crt.sh) — что «торчит» наружу у домена.</summary>
        public static async Task<List<string>> Subdomains(string input)
        {
            var domain = Registrable(HostOf(input));
            IEnumerable<string> names;
            try
            {
                using var doc = await Json("https://crt.sh/?output=json&q=" + Uri.EscapeDataString("%." + domain));
                names = doc.RootElement.EnumerateArray().SelectMany(e => S(e, "name_value").Split('\n')).ToList();
            }
            catch
            {
                // crt.sh часто перегружен — запасной журнал сертификатов Cert Spotter
                using var doc = await Json($"https://api.certspotter.com/v1/issuances?domain={Uri.EscapeDataString(domain)}&include_subdomains=true&expand=dns_names");
                names = doc.RootElement.EnumerateArray().SelectMany(e => e.TryGetProperty("dns_names", out var d) ? d.EnumerateArray().Select(x => x.GetString()) : Enumerable.Empty<string>()).ToList();
            }
            return names
                .Select(n => (n ?? "").Trim().TrimStart('*', '.').ToLowerInvariant())
                .Where(n => n.EndsWith(domain) && !n.Contains('@'))
                .Distinct().OrderBy(n => n.Length).ThenBy(n => n).Take(300).ToList();
        }

        public record ArchiveInfo(int Count, string First, string Last, string Url);

        /// <summary>Веб-архив: когда сайт появился, сколько снимков, ссылка на ближайший.</summary>
        public static async Task<ArchiveInfo> Wayback(string input)
        {
            var host = HostOf(input);
            using var first = await Json($"https://web.archive.org/cdx/search/cdx?url={Uri.EscapeDataString(host)}&output=json&limit=1&fl=timestamp");
            using var last = await Json($"https://web.archive.org/cdx/search/cdx?url={Uri.EscapeDataString(host)}&output=json&limit=-1&fl=timestamp");
            using var count = await Json($"https://web.archive.org/cdx/search/cdx?url={Uri.EscapeDataString(host)}&output=json&limit=500&fl=timestamp&collapse=timestamp:6");
            string Ts(JsonDocument d) => d.RootElement.GetArrayLength() > 1 ? Pretty(d.RootElement[1][0].GetString()) : "";
            int n = Math.Max(0, count.RootElement.GetArrayLength() - 1);
            return new ArchiveInfo(n, Ts(first), Ts(last), "https://web.archive.org/web/*/" + host);
        }

        static string Pretty(string ts) => ts.Length >= 8 && DateTime.TryParseExact(ts[..8], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d.ToString("MMMM yyyy", Fmt.Ru) : ts;

        // ------------------------------------------------------------------
        //  IP и сайт
        // ------------------------------------------------------------------

        /// <summary>Сведения об IP: страна, город, провайдер, AS, хостинг или прокси, обратное имя. Пусто — свой внешний IP.</summary>
        public static async Task<List<(string K, string V)>> Ip(string input)
        {
            input = (input ?? "").Trim();
            if (input != "" && !IPAddress.TryParse(input, out _))
            {
                var addrs = await Dns.GetHostAddressesAsync(HostOf(input));
                input = addrs.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)?.ToString() ?? addrs.First().ToString();
            }
            var res = new List<(string, string)>();
            using (var doc = await Json("https://ipwho.is/" + input))
            {
                var r = doc.RootElement;
                if (r.TryGetProperty("success", out var ok) && ok.ValueKind == JsonValueKind.False) throw new InvalidOperationException(S(r, "message"));
                var conn = r.TryGetProperty("connection", out var c) ? c : default;
                res.Add(("IP", S(r, "ip")));
                res.Add(("Где", string.Join(", ", new[] { S(r, "country"), S(r, "region"), S(r, "city") }.Where(x => x != ""))));
                res.Add(("Провайдер", S(conn, "isp")));
                res.Add(("Сеть (AS)", "AS" + S(conn, "asn") + " " + S(conn, "org")));
                if (r.TryGetProperty("timezone", out var tz)) res.Add(("Часовой пояс", S(tz, "id")));
                input = S(r, "ip");
            }
            try
            {
                using var doc = await Json($"http://ip-api.com/json/{input}?fields=status,proxy,hosting,mobile,reverse");
                var r = doc.RootElement;
                var kinds = new List<string>();
                if (r.TryGetProperty("hosting", out var h) && h.ValueKind == JsonValueKind.True) kinds.Add("сервер / хостинг");
                if (r.TryGetProperty("proxy", out var p) && p.ValueKind == JsonValueKind.True) kinds.Add("VPN, прокси или Tor");
                if (r.TryGetProperty("mobile", out var m) && m.ValueKind == JsonValueKind.True) kinds.Add("мобильная сеть");
                res.Add(("Тип", kinds.Count == 0 ? "обычный домашний или офисный" : string.Join(", ", kinds)));
                if (S(r, "reverse") != "") res.Add(("Обратное имя", S(r, "reverse")));
            }
            catch { }
            return res;
        }

        /// <summary>Заголовки безопасности сайта и чем он работает.</summary>
        public static async Task<List<(string K, string V)>> Headers(string input)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            using var resp = await Http.GetAsync(Url(input), HttpCompletionOption.ResponseHeadersRead);
            var res = new List<(string, string)>
            {
                ("Ответ", $"{(int)resp.StatusCode} {resp.ReasonPhrase} · {sw.ElapsedMilliseconds} мс"),
                ("Итоговый адрес", resp.RequestMessage?.RequestUri?.ToString() ?? ""),
            };
            string H(string name) => resp.Headers.TryGetValues(name, out var v) ? string.Join(", ", v) : resp.Content.Headers.TryGetValues(name, out var cv) ? string.Join(", ", cv) : null;
            if (H("Server") is { } server) res.Add(("Сервер", server));
            if (H("X-Powered-By") is { } pb) res.Add(("Сделан на", pb));
            var checks = new (string Header, string Why)[]
            {
                ("Strict-Transport-Security", "всегда HTTPS"), ("Content-Security-Policy", "защита от внедрения скриптов"), ("X-Frame-Options", "защита от подмены в рамке"),
                ("X-Content-Type-Options", "запрет подмены типа файлов"), ("Referrer-Policy", "не раскрывает, откуда пришёл"), ("Permissions-Policy", "ограничивает камеру и микрофон"),
            };
            int ok = 0;
            foreach (var (h, why) in checks)
            {
                bool has = H(h) != null;
                if (has) ok++;
                res.Add((h, (has ? "есть ✓ " : "нет ✗ ") + "— " + why));
            }
            res.Insert(2, ("Защита", $"{ok} из {checks.Length} заголовков безопасности"));
            return res;
        }

        // ------------------------------------------------------------------
        //  Пароль, фото, файлы
        // ------------------------------------------------------------------

        /// <summary>Сколько раз пароль встречался в утечках (Have I Been Pwned, k-анонимность: наружу уходят только 5 символов хэша).</summary>
        public static async Task<long> PasswordLeaks(string password)
        {
            var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(password)));
            using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.pwnedpasswords.com/range/" + hash[..5]);
            req.Headers.TryAddWithoutValidation("Add-Padding", "true");
            using var resp = await Http.SendAsync(req);
            resp.EnsureSuccessStatusCode();
            var body = await resp.Content.ReadAsStringAsync();
            foreach (var line in body.Split('\n'))
            {
                var p = line.Trim().Split(':');
                if (p.Length == 2 && p[0] == hash[5..] && long.TryParse(p[1], out var n)) return n;
            }
            return 0;
        }

        public record PhotoMeta(List<(string K, string V)> Items, double? Lat, double? Lon, int Orientation);

        /// <summary>Что спрятано в фото: камера, дата, программа и координаты съёмки.</summary>
        public static PhotoMeta Photo(string path)
        {
            var items = new List<(string, string)>();
            double? lat = null, lon = null;
            int orient = 1;
            using var fs = File.OpenRead(path);
            var dec = BitmapDecoder.Create(fs, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.None);
            var f = dec.Frames[0];
            items.Add(("Размер", $"{f.PixelWidth} × {f.PixelHeight}, {new FileInfo(path).Length / 1024} КБ"));
            if (f.Metadata is BitmapMetadata m)
            {
                string Q(string q) { try { return m.GetQuery(q)?.ToString(); } catch { return null; } }
                void Add(string name, string v) { if (!string.IsNullOrWhiteSpace(v)) items.Add((name, v.Trim('\0', ' '))); }
                Add("Камера", string.Join(" ", new[] { SafeGet(() => m.CameraManufacturer), SafeGet(() => m.CameraModel) }.Where(x => !string.IsNullOrEmpty(x))));
                Add("Снято", SafeGet(() => m.DateTaken));
                Add("Программа", SafeGet(() => m.ApplicationName));
                Add("Автор", SafeGet(() => m.Author == null ? null : string.Join(", ", m.Author)));
                Add("Объектив", Q("/app1/ifd/exif/{ushort=42036}"));
                if (int.TryParse(Q("/app1/ifd/{ushort=274}"), out var o)) orient = o;
                lat = Gps(m, "/app1/ifd/gps/{ushort=2}", Q("/app1/ifd/gps/{ushort=1}"));
                lon = Gps(m, "/app1/ifd/gps/{ushort=4}", Q("/app1/ifd/gps/{ushort=3}"));
                if (lat != null && lon != null) items.Add(("Координаты", $"{lat:0.00000}, {lon:0.00000}"));
            }
            if (items.Count == 1) items.Add(("Метаданные", "нет — фото чистое"));
            return new PhotoMeta(items, lat, lon, orient);
        }

        static string SafeGet(Func<string> f) { try { return f(); } catch { return null; } }

        static double? Gps(BitmapMetadata m, string query, string reference)
        {
            try
            {
                if (m.GetQuery(query) is not ulong[] r || r.Length < 3) return null;
                double R(ulong v) { double num = v & 0xFFFFFFFF, den = v >> 32; return den == 0 ? 0 : num / den; }
                double deg = R(r[0]) + R(r[1]) / 60 + R(r[2]) / 3600;
                return reference is "S" or "W" ? -deg : deg;
            }
            catch { return null; }
        }

        /// <summary>Копия фото без метаданных (координат, камеры, дат). Поворот сохраняется. Оригинал не трогаем.</summary>
        public static string StripPhoto(string path, int orientation)
        {
            BitmapSource src;
            using (var fs = File.OpenRead(path))
            {
                var dec = BitmapDecoder.Create(fs, BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
                src = dec.Frames[0];
            }
            // поворот из EXIF применяем к пикселям, иначе без метаданных фото «ляжет на бок»
            double angle = orientation switch { 3 => 180, 6 => 90, 8 => 270, _ => 0 };
            if (angle != 0) src = new TransformedBitmap(src, new System.Windows.Media.RotateTransform(angle));
            var enc = new JpegBitmapEncoder { QualityLevel = 93 };
            enc.Frames.Add(BitmapFrame.Create(src));
            var dir = Path.GetDirectoryName(path);
            var outPath = Path.Combine(dir, Path.GetFileNameWithoutExtension(path) + "_clean.jpg");
            using (var o = File.Create(outPath)) enc.Save(o);
            return outPath;
        }

        public static string Sha256(string path)
        {
            using var fs = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
        }
    }
}
