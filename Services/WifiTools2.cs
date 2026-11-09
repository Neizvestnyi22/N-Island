using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using NIsland.Core;

namespace NIsland.Services
{
    /// <summary>1.8: ещё инструменты сети — точка доступа, стабильность, DNS, трассировка, ремонт, адаптер, Wake-on-LAN, карта сигнала, двойники, регионы, пароли.</summary>
    public static class WifiTools2
    {
        // ------------------------------------------------------------------
        //  1. Мобильная точка доступа (раздать интернет с ноутбука)
        // ------------------------------------------------------------------

        static global::Windows.Networking.NetworkOperators.NetworkOperatorTetheringManager Tethering()
        {
            var profile = global::Windows.Networking.Connectivity.NetworkInformation.GetInternetConnectionProfile();
            if (profile == null) throw new InvalidOperationException("Нет подключения к интернету — раздавать нечего");
            return global::Windows.Networking.NetworkOperators.NetworkOperatorTetheringManager.CreateFromConnectionProfile(profile);
        }

        // 2.2: раз в 30 секунд смотрим сигнал; ниже порога из настроек — одна всплывашка (повторно — когда сигнал вернётся и снова упадёт)
        static System.Windows.Threading.DispatcherTimer _watch;
        static bool _weakWarned;

        public static void WatchSignal()
        {
            if (_watch != null) return;
            _watch = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _watch.Tick += async (_, __) =>
            {
                int lim = Store.Settings.WifiSignalAlert;
                if (lim <= 0) { _weakWarned = false; return; }
                try
                {
                    var l = await WifiTools.Current();
                    if (l == null) { _weakWarned = false; return; }
                    if (l.Signal < lim && !_weakWarned)
                    {
                        _weakWarned = true;
                        App.Island?.Toast("\uE701", I18n.T("Слабый Wi-Fi") + $": {l.Signal}%", $"{l.Ssid} · {l.Band} · {I18n.T("канал")} {l.Channel}");
                    }
                    else if (l.Signal >= lim + 10) _weakWarned = false;
                }
                catch { }
            };
            _watch.Start();
        }

        public static (bool On, string Name, string Pass, int Clients, int Max)? HotspotState()
        {
            try
            {
                var m = Tethering();
                var cfg = m.GetCurrentAccessPointConfiguration();
                return (m.TetheringOperationalState == global::Windows.Networking.NetworkOperators.TetheringOperationalState.On, cfg.Ssid, cfg.Passphrase, (int)m.ClientCount, (int)m.MaxClientCount);
            }
            catch { return null; }
        }

        public static async Task<string> SetHotspot(bool on, string name = null, string pass = null)
        {
            try
            {
                var m = Tethering();
                if (!string.IsNullOrWhiteSpace(name) || !string.IsNullOrWhiteSpace(pass))
                {
                    var cfg = m.GetCurrentAccessPointConfiguration();
                    if (!string.IsNullOrWhiteSpace(name)) cfg.Ssid = name.Trim();
                    if (!string.IsNullOrWhiteSpace(pass))
                    {
                        if (pass.Trim().Length < 8) return "Пароль — минимум 8 символов";
                        cfg.Passphrase = pass.Trim();
                    }
                    await m.ConfigureAccessPointAsync(cfg);
                }
                var r = on ? await m.StartTetheringAsync() : await m.StopTetheringAsync();
                return r.Status == global::Windows.Networking.NetworkOperators.TetheringOperationStatus.Success ? null
                    : "Windows не включила точку доступа: " + r.Status + (string.IsNullOrEmpty(r.AdditionalErrorMessage) ? "" : " · " + r.AdditionalErrorMessage);
            }
            catch (Exception e) { return e.Message; }
        }

        // ------------------------------------------------------------------
        //  2. Стабильность связи за минуту: задержка, джиттер, потери, обрывы
        // ------------------------------------------------------------------

        public record StabilityResult(List<double> Ms, int Lost, double Avg, double Jitter, double Max, int Spikes, string Verdict);

        public static async Task<StabilityResult> Stability(int seconds, Action<int, double> progress)
        {
            var target = IPAddress.Parse("1.1.1.1");
            var ms = new List<double>();
            int lost = 0;
            using var ping = new Ping();
            for (int i = 0; i < seconds * 2; i++)
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    var r = await ping.SendPingAsync(target, 1500);
                    if (r.Status == IPStatus.Success) ms.Add(r.RoundtripTime); else { lost++; ms.Add(-1); }
                }
                catch { lost++; ms.Add(-1); }
                progress?.Invoke(i, ms[^1]);
                var wait = 500 - (int)sw.ElapsedMilliseconds;
                if (wait > 0) await Task.Delay(wait);
            }
            var ok = ms.Where(x => x >= 0).ToList();
            double avg = ok.Count > 0 ? ok.Average() : -1;
            double jitter = ok.Count > 1 ? ok.Zip(ok.Skip(1), (a, b) => Math.Abs(a - b)).Average() : 0;
            double max = ok.Count > 0 ? ok.Max() : -1;
            int spikes = ok.Count(x => avg > 0 && x > avg * 3 && x > avg + 60);
            double lossPct = ms.Count == 0 ? 100 : lost * 100.0 / ms.Count;
            string verdict = ok.Count == 0 ? "Интернет не отвечает"
                : lossPct > 3 ? $"Нестабильно: теряется {lossPct:0}% пакетов — будут обрывы в играх и звонках"
                : jitter > 30 || spikes > 3 ? $"Скачет задержка (джиттер {jitter:0} мс, всплесков {spikes}) — звонки будут «квакать», в играх лаги"
                : avg > 120 ? "Связь стабильная, но задержка большая"
                : "Стабильно ✓ Подходит для игр и звонков";
            return new StabilityResult(ms, lost, avg, jitter, max, spikes, verdict);
        }

        // ------------------------------------------------------------------
        //  3. Сравнение DNS-серверов
        // ------------------------------------------------------------------

        public static async Task<List<(string Name, string Ip, double Ms)>> DnsBenchmark()
        {
            var list = new List<(string Name, string Ip)>
            {
                ("Cloudflare", "1.1.1.1"), ("Google", "8.8.8.8"), ("Quad9", "9.9.9.9"), ("AdGuard", "94.140.14.14"),
                ("OpenDNS", "208.67.222.222"), ("Control D", "76.76.2.0"), ("Яндекс", "77.88.8.8"), ("Comodo", "8.26.56.26"),
            };
            foreach (var d in WifiTools.CurrentDns().Take(2)) if (!list.Any(x => x.Ip == d.ToString())) list.Insert(0, ("Сейчас (" + d + ")", d.ToString()));
            var res = await Task.WhenAll(list.Select(async d =>
            {
                // разные сайты, чтобы не мерить кэш одного ответа
                double a = await WifiTools.DnsSpeed(IPAddress.Parse(d.Ip), "www.wikipedia.org");
                double b = await WifiTools.DnsSpeed(IPAddress.Parse(d.Ip), "github.com");
                double v = a < 0 || b < 0 ? Math.Max(a, b) : (a + b) / 2;
                return (d.Name, d.Ip, v);
            }));
            return res.OrderBy(x => x.v < 0 ? 99999 : x.v).ToList();
        }

        // ------------------------------------------------------------------
        //  4. Трассировка: через какие узлы идёт связь и где тормозит
        // ------------------------------------------------------------------

        public static async Task<List<(int Hop, string Ip, string Host, double Ms)>> Trace(string target, Action<string> progress = null)
        {
            IPAddress ip;
            if (!IPAddress.TryParse(target, out ip))
                ip = (await Dns.GetHostAddressesAsync(target)).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                     ?? throw new InvalidOperationException("Не удалось найти адрес " + target);
            var hops = new List<(int, string, string, double)>();
            using var ping = new Ping();
            var buf = new byte[32];
            for (int ttl = 1; ttl <= 24; ttl++)
            {
                progress?.Invoke($"Узел {ttl}…");
                var sw = Stopwatch.StartNew();
                PingReply r;
                try { r = await ping.SendPingAsync(ip, 1500, buf, new PingOptions(ttl, true)); }
                catch { hops.Add((ttl, "*", "", -1)); continue; }
                double t = sw.Elapsed.TotalMilliseconds;
                if (r.Status is IPStatus.TtlExpired or IPStatus.Success)
                {
                    string host = "";
                    try { var he = await Task.WhenAny(Dns.GetHostEntryAsync(r.Address), Task.Delay(700)); if (he is Task<IPHostEntry> h && h.IsCompletedSuccessfully) host = h.Result.HostName; } catch { }
                    hops.Add((ttl, r.Address.ToString(), host, r.Status == IPStatus.Success ? r.RoundtripTime : t));
                    if (r.Status == IPStatus.Success) break;
                }
                else hops.Add((ttl, "*", "", -1));
            }
            return hops;
        }

        // ------------------------------------------------------------------
        //  5. Быстрый ремонт: DNS, новый адрес от роутера, переподключение
        // ------------------------------------------------------------------

        public static async Task<List<string>> QuickRepair(Action<string> progress)
        {
            var log = new List<string>();
            progress("Сбрасываю кэш DNS…");
            log.Add(Maint.FlushDns() ? "✓ Кэш DNS сброшен" : "✕ Кэш DNS не сбросился");
            progress("Прошу у роутера новый адрес…");
            var renew = await Task.Run(() => Maint.RunCmd("ipconfig", "/renew"));
            log.Add(renew.Contains("IPv4") || renew.Length > 0 ? "✓ Адрес от роутера обновлён" : "• Адрес не обновился (это нормально для проводной сети)");
            if (WifiTools.HasAdapter)
            {
                progress("Переподключаю Wi-Fi…");
                log.Add("✓ " + (await WifiTools.Reconnect() ?? "Wi-Fi переподключён"));
            }
            progress("Проверяю интернет…");
            await Task.Delay(3000);
            try
            {
                using var p = new Ping();
                var r = await p.SendPingAsync("1.1.1.1", 2000);
                log.Add(r.Status == IPStatus.Success ? $"✓ Интернет работает ({r.RoundtripTime} мс)" : "✕ Интернет всё ещё не отвечает — перезагрузи роутер (выключи на 30 секунд)");
            }
            catch { log.Add("✕ Интернет всё ещё не отвечает — перезагрузи роутер (выключи на 30 секунд)"); }
            return log;
        }

        // ------------------------------------------------------------------
        //  6. Всё об адаптере
        // ------------------------------------------------------------------

        public static List<(string K, string V)> AdapterInfo()
        {
            var rows = new List<(string, string)>();
            var nic = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .OrderByDescending(n => n.GetIPProperties().GatewayAddresses.Count)
                .FirstOrDefault();
            if (nic == null) { rows.Add(("Подключение", "нет")); return rows; }
            var ip = nic.GetIPProperties();
            rows.Add(("Адаптер", nic.Description));
            rows.Add(("Тип", nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? "Wi-Fi" : nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet ? "кабель (Ethernet)" : nic.NetworkInterfaceType.ToString()));
            rows.Add(("Скорость соединения", nic.Speed > 0 ? (nic.Speed / 1_000_000) + " Мбит/с" : "—"));
            rows.Add(("MAC-адрес", string.Join(":", nic.GetPhysicalAddress().GetAddressBytes().Select(b => b.ToString("X2")))));
            foreach (var a in ip.UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork))
                rows.Add(("IPv4", $"{a.Address} / {a.IPv4Mask}"));
            var v6 = ip.UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetworkV6 && !a.Address.IsIPv6LinkLocal).Select(a => a.Address.ToString()).ToList();
            rows.Add(("IPv6", v6.Count > 0 ? string.Join("\n", v6.Take(3)) : "нет"));
            rows.Add(("Шлюз (роутер)", string.Join(", ", ip.GatewayAddresses.Select(g => g.Address.ToString()))));
            rows.Add(("DNS", string.Join(", ", ip.DnsAddresses.Select(d => d.ToString()).Take(4))));
            try
            {
                var v4 = ip.GetIPv4Properties();
                rows.Add(("Адрес выдаёт", v4.IsDhcpEnabled ? "роутер (DHCP)" + (ip.DhcpServerAddresses.Count > 0 ? " · " + ip.DhcpServerAddresses[0] : "") : "задан вручную"));
                rows.Add(("MTU", v4.Mtu.ToString()));
            }
            catch { }
            try
            {
                var st = nic.GetIPStatistics();
                rows.Add(("Принято / отправлено", $"{Maint.Size(st.BytesReceived)} / {Maint.Size(st.BytesSent)}"));
                if (st.IncomingPacketsWithErrors + st.OutgoingPacketsWithErrors > 0) rows.Add(("Пакеты с ошибками", (st.IncomingPacketsWithErrors + st.OutgoingPacketsWithErrors).ToString()));
            }
            catch { }
            return rows;
        }

        // ------------------------------------------------------------------
        //  7. Wake-on-LAN: включить другой компьютер по сети
        // ------------------------------------------------------------------

        public static async Task<string> WakeOnLan(string mac)
        {
            var hex = new string((mac ?? "").Where(Uri.IsHexDigit).ToArray());
            if (hex.Length != 12) return "MAC-адрес — 12 шестнадцатеричных цифр, например 00:1A:2B:3C:4D:5E";
            var bytes = Enumerable.Range(0, 6).Select(i => Convert.ToByte(hex.Substring(i * 2, 2), 16)).ToArray();
            var packet = new byte[102];
            for (int i = 0; i < 6; i++) packet[i] = 0xFF;
            for (int i = 1; i <= 16; i++) Array.Copy(bytes, 0, packet, i * 6, 6);
            using var udp = new UdpClient { EnableBroadcast = true };
            foreach (var port in new[] { 9, 7 })
                await udp.SendAsync(packet, packet.Length, new IPEndPoint(IPAddress.Broadcast, port));
            return null;
        }

        // ------------------------------------------------------------------
        //  8. Карта сигнала по комнатам
        // ------------------------------------------------------------------

        public static async Task<string> MeasureRoom(string room)
        {
            room = (room ?? "").Trim();
            if (room == "") return "Напиши, где ты сейчас (например: кухня)";
            var samples = new List<int>();
            for (int i = 0; i < 6; i++)
            {
                var l = await WifiTools.Current();
                if (l == null) return "Wi-Fi не подключён";
                samples.Add(l.Rssi);
                await Task.Delay(500);
            }
            int rssi = (int)samples.Average();
            Store.Data.SignalMap ??= new();
            Store.Data.SignalMap[room] = rssi;
            Store.SaveData();
            return null;
        }

        // ------------------------------------------------------------------
        //  9. Двойники и безопасность сетей рядом
        // ------------------------------------------------------------------

        static readonly string[] DefaultNames = { "tp-link_", "tplink", "dlink", "d-link", "asus", "keenetic-", "netgear", "linksys", "mercusys", "tenda_", "huawei-", "zte_", "xiaomi_", "redmi_", "mikrotik", "default", "wireless" };

        public static List<(int Level, string Title, string Advice)> Security(List<WifiAp> aps)
        {
            var res = new List<(int, string, string)>();
            var cur = aps.FirstOrDefault(a => a.Current);
            if (cur != null)
            {
                var twins = aps.Where(a => a.Ssid == cur.Ssid && a.Bssid != cur.Bssid).ToList();
                var bad = twins.Where(t => t.Security != cur.Security).ToList();
                if (bad.Count > 0)
                    res.Add((2, $"Подозрительный двойник «{cur.Ssid}»", $"Рядом есть сеть с тем же именем, но другой защитой ({string.Join(", ", bad.Select(b => b.Security).Distinct())}). Так выглядит «злой двойник», который перехватывает пароли. Не подключайся к ней."));
                else if (twins.Count > 0)
                    res.Add((0, $"Сеть «{cur.Ssid}» раздают {twins.Count + 1} точки", "Это обычная mesh-система или усилитель — всё в порядке."));
                if (cur.Security.Contains("Open") || cur.Security.Contains("Открыт") || cur.Security == "")
                    res.Add((2, "Ты в открытой сети без пароля", "Всё, что передаётся без https, видят соседи. Включи VPN или поставь пароль на роутер."));
                else if (cur.Security.Contains("WEP"))
                    res.Add((2, "Защита WEP — взламывается за минуты", "Включи в роутере WPA2 или WPA3."));
                else if (cur.Security.Contains("WPA3"))
                    res.Add((0, "Защита WPA3 — лучшая из доступных", null));
                if (cur.Wps) res.Add((1, "На роутере включён WPS", "Кнопка/ПИН WPS — лазейка для подбора пароля. Выключи WPS в настройках роутера."));
                if (DefaultNames.Any(n => cur.Ssid.ToLowerInvariant().StartsWith(n)))
                    res.Add((1, "Имя сети — заводское", "По заводскому имени видно модель роутера, а у некоторых моделей и пароль по умолчанию. Переименуй сеть и смени пароль роутера."));
            }
            int open = aps.Count(a => !a.Current && (a.Security.Contains("Open") || a.Security.Contains("Открыт")));
            if (open > 0) res.Add((1, $"Открытых сетей рядом: {open}", "Не подключайся к ним без VPN."));
            var hiddenTwins = aps.GroupBy(a => a.Ssid).Where(g => g.Key != "" && g.Select(x => x.Security).Distinct().Count() > 1 && (cur == null || g.Key != cur.Ssid)).Select(g => g.Key).ToList();
            foreach (var t in hiddenTwins.Take(3)) res.Add((1, $"Двойник у сети «{t}»", "У сети с этим именем разная защита на разных точках — возможна подделка."));
            if (res.Count == 0) res.Add((0, "Подозрительного рядом не видно ✓", null));
            return res;
        }

        // ------------------------------------------------------------------
        //  10. Задержка до регионов (игры, облака)
        // ------------------------------------------------------------------

        public static readonly (string Name, string Host)[] Regions =
        {
            ("Европа · Франкфурт", "dynamodb.eu-central-1.amazonaws.com"), ("Европа · Стокгольм", "dynamodb.eu-north-1.amazonaws.com"),
            ("Европа · Лондон", "dynamodb.eu-west-2.amazonaws.com"), ("Европа · Милан", "dynamodb.eu-south-1.amazonaws.com"),
            ("США · Восток", "dynamodb.us-east-1.amazonaws.com"), ("США · Запад", "dynamodb.us-west-2.amazonaws.com"),
            ("Ближний Восток · Бахрейн", "dynamodb.me-south-1.amazonaws.com"), ("Азия · Сингапур", "dynamodb.ap-southeast-1.amazonaws.com"),
            ("Азия · Токио", "dynamodb.ap-northeast-1.amazonaws.com"), ("Южная Америка · Сан-Паулу", "dynamodb.sa-east-1.amazonaws.com"),
        };

        /// <summary>Время TCP-соединения (ICMP многие облака не пропускают) — лучшая из трёх попыток.</summary>
        public static async Task<double> TcpMs(string host, int port = 443)
        {
            double best = -1;
            IPAddress ip;
            try { ip = (await Dns.GetHostAddressesAsync(host)).First(a => a.AddressFamily == AddressFamily.InterNetwork); } catch { return -1; }
            for (int i = 0; i < 3; i++)
            {
                using var c = new TcpClient();
                var sw = Stopwatch.StartNew();
                var t = c.ConnectAsync(ip, port);
                if (await Task.WhenAny(t, Task.Delay(2500)) != t || t.IsFaulted) continue;
                double ms = sw.Elapsed.TotalMilliseconds;
                best = best < 0 ? ms : Math.Min(best, ms);
            }
            return best;
        }

        // ------------------------------------------------------------------
        //  11. Надёжный пароль для Wi-Fi
        // ------------------------------------------------------------------

        public static string StrongPassword(int length = 16, bool readable = true)
        {
            // без похожих символов (l/1/I, O/0) — его будут вводить на телефонах и телевизорах
            const string Letters = "abcdefghijkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ", Digits = "23456789", Signs = "-_+=!?#";
            string all = Letters + Digits + (readable ? "" : Signs);
            var sb = new StringBuilder();
            for (int i = 0; i < length; i++)
            {
                if (readable && i > 0 && i % 5 == 4) { sb.Append('-'); continue; }
                sb.Append(all[RandomNumberGenerator.GetInt32(all.Length)]);
            }
            return sb.ToString();
        }

        /// <summary>Примерная стойкость пароля Wi-Fi.</summary>
        public static string Strength(string p)
        {
            if (string.IsNullOrEmpty(p)) return "без пароля";
            int pool = (p.Any(char.IsLower) ? 26 : 0) + (p.Any(char.IsUpper) ? 26 : 0) + (p.Any(char.IsDigit) ? 10 : 0) + (p.Any(c => !char.IsLetterOrDigit(c)) ? 20 : 0);
            double bits = p.Length * Math.Log2(Math.Max(pool, 2));
            if (p.All(char.IsDigit) && p.Length <= 10) bits = Math.Min(bits, 30);   // телефон или дата рождения
            return bits < 40 ? "слабый — подберут быстро" : bits < 60 ? "средний" : bits < 80 ? "хороший" : "отличный";
        }
    }
}
