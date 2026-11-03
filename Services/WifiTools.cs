using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NIsland.Core;

namespace NIsland.Services
{
    /// <summary>Текущее подключение Wi-Fi.</summary>
    public record WifiLink(string Ssid, string Bssid, string Profile, int Signal, int Rssi, int Channel, string Band, string Standard,
                           double RxMbps, double TxMbps, string Security, string Adapter);

    /// <summary>Точка доступа рядом (одна строка на каждый BSSID).</summary>
    public record WifiAp(string Ssid, string Bssid, int Rssi, int Signal, int Channel, string Band, string Standard, string Security, bool Current, bool Wps = false);

    /// <summary>Загрузка канала: сколько сетей и насколько сильно они мешают.</summary>
    public record ChannelLoad(int Channel, string Band, int Networks, double Load, bool Current, bool Best);

    /// <summary>Пункт проверки сети: 0 — хорошо, 1 — стоит обратить внимание, 2 — проблема.</summary>
    public record HealthItem(int Level, string Title, string Advice);

    /// <summary>Устройство в локальной сети.</summary>
    public record LanDevice(string Ip, string Mac, string Name, string Kind, double Ms);

    /// <summary>Итог проверки качества связи: до роутера и до интернета.</summary>
    public record LinkQuality(double GwMs, double GwJitter, double GwLoss, double NetMs, double NetJitter, double NetLoss, double DnsMs, string Verdict, string Advice);

    /// <summary>
    /// Wi-Fi через официальный Native Wifi API Windows (wlanapi.dll): текущее подключение, сканирование сетей рядом,
    /// подбор свободного канала, проверка качества (роутер против интернета), устройства в сети, переподключение.
    /// Данные не зависят от языка Windows (в отличие от вывода netsh). Права администратора не нужны.
    /// </summary>
    public static class WifiTools
    {
        [DllImport("wlanapi.dll")] static extern int WlanOpenHandle(int ver, IntPtr reserved, out int negotiated, out IntPtr handle);
        [DllImport("wlanapi.dll")] static extern int WlanCloseHandle(IntPtr handle, IntPtr reserved);
        [DllImport("wlanapi.dll")] static extern int WlanEnumInterfaces(IntPtr handle, IntPtr reserved, out IntPtr list);
        [DllImport("wlanapi.dll")] static extern int WlanQueryInterface(IntPtr handle, ref Guid iface, int opcode, IntPtr reserved, out int size, out IntPtr data, IntPtr type);
        [DllImport("wlanapi.dll")] static extern int WlanScan(IntPtr handle, ref Guid iface, IntPtr ssid, IntPtr ie, IntPtr reserved);
        [DllImport("wlanapi.dll")] static extern int WlanGetNetworkBssList(IntPtr handle, ref Guid iface, IntPtr ssid, int bssType, bool secure, IntPtr reserved, out IntPtr list);
        [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)] static extern int WlanDeleteProfile(IntPtr handle, ref Guid iface, string name, IntPtr reserved);
        [DllImport("wlanapi.dll")] static extern void WlanFreeMemory(IntPtr p);

        /// <summary>Открыть клиент WLAN, взять первый беспроводной адаптер и выполнить действие.</summary>
        static T With<T>(Func<IntPtr, Guid, string, T> f, T fallback)
        {
            if (WlanOpenHandle(2, IntPtr.Zero, out _, out var h) != 0) return fallback;
            try
            {
                if (WlanEnumInterfaces(h, IntPtr.Zero, out var list) != 0) return fallback;
                try
                {
                    int n = Marshal.ReadInt32(list);
                    if (n == 0) return fallback;
                    // WLAN_INTERFACE_INFO: GUID (16) + описание WCHAR[256] (512) + состояние (4) = 532 байта, список начинается с 8-го байта
                    var first = list + 8;
                    var guid = Marshal.PtrToStructure<Guid>(first);
                    var desc = Marshal.PtrToStringUni(first + 16) ?? "";
                    return f(h, guid, desc);
                }
                finally { WlanFreeMemory(list); }
            }
            catch (Exception e) { Log.Info("wifi: " + e.Message); return fallback; }
            finally { WlanCloseHandle(h, IntPtr.Zero); }
        }

        public static bool HasAdapter => With((h, g, d) => true, false);

        static string Ssid(IntPtr p)
        {
            int len = Math.Clamp(Marshal.ReadInt32(p), 0, 32);
            var bytes = new byte[len];
            Marshal.Copy(p + 4, bytes, 0, len);
            return Encoding.UTF8.GetString(bytes);
        }

        static string Mac(IntPtr p)
        {
            var b = new byte[6];
            Marshal.Copy(p, b, 0, 6);
            return string.Join(":", b.Select(x => x.ToString("x2")));
        }

        static string Phy(int t) => t switch
        {
            4 => "802.11a", 5 => "802.11b", 6 => "802.11g", 7 => "Wi-Fi 4 (n)", 8 => "Wi-Fi 5 (ac)", 10 => "Wi-Fi 6 (ax)", 11 => "Wi-Fi 7 (be)", _ => "Wi-Fi",
        };

        static string Auth(int a) => a switch
        {
            1 => "Открытая", 2 => "WEP", 3 => "WPA", 4 => "WPA-Personal", 6 => "WPA2-Enterprise", 7 => "WPA2-Personal",
            8 or 11 => "WPA3-Enterprise", 9 => "WPA3-Personal", 10 => "OWE", _ => "—",
        };

        static (int Channel, string Band) FromFreq(int khz)
        {
            int mhz = khz / 1000;
            if (mhz >= 2400 && mhz < 2500) return (mhz == 2484 ? 14 : (mhz - 2407) / 5, "2,4 ГГц");
            if (mhz >= 5000 && mhz < 5925) return ((mhz - 5000) / 5, "5 ГГц");
            if (mhz >= 5925 && mhz < 7200) return ((mhz - 5950) / 5, "6 ГГц");
            return (0, "");
        }

        static string BandOf(int ch) => ch is >= 1 and <= 14 ? "2,4 ГГц" : ch >= 32 ? "5 ГГц" : "";

        /// <summary>Уровень сигнала в процентах по RSSI (как в Windows: −100 dBm = 0 %, −50 dBm = 100 %).</summary>
        public static int Percent(int rssi) => Math.Clamp(2 * (rssi + 100), 0, 100);

        // ------------------------------------------------------------------
        //  Текущее подключение
        // ------------------------------------------------------------------

        public static Task<WifiLink> Current() => Task.Run(() => With<WifiLink>((h, g, desc) =>
        {
            // wlan_intf_opcode_current_connection = 7 → WLAN_CONNECTION_ATTRIBUTES
            if (WlanQueryInterface(h, ref g, 7, IntPtr.Zero, out _, out var p, IntPtr.Zero) != 0) return null;
            try
            {
                int state = Marshal.ReadInt32(p);
                if (state != 1) return null;   // wlan_interface_state_connected
                var profile = Marshal.PtrToStringUni(p + 8) ?? "";
                var a = p + 520;                                  // WLAN_ASSOCIATION_ATTRIBUTES
                var ssid = Ssid(a);
                var bssid = Mac(a + 40);
                int phy = Marshal.ReadInt32(a + 48);
                int quality = Marshal.ReadInt32(a + 56);
                double rx = (uint)Marshal.ReadInt32(a + 60) / 1000.0, tx = (uint)Marshal.ReadInt32(a + 64) / 1000.0;
                int auth = Marshal.ReadInt32(p + 596);
                int channel = Query(h, g, 8);                    // wlan_intf_opcode_channel_number
                int rssi = Query(h, g, 0x10000102);              // wlan_intf_opcode_rssi
                if (rssi == int.MinValue || rssi >= 0) rssi = quality / 2 - 100;
                return new WifiLink(ssid, bssid, profile, quality, rssi, channel == int.MinValue ? 0 : channel, BandOf(channel), Phy(phy), rx, tx, Auth(auth), desc);
            }
            finally { WlanFreeMemory(p); }
        }, null));

        static int Query(IntPtr h, Guid g, int opcode)
        {
            if (WlanQueryInterface(h, ref g, opcode, IntPtr.Zero, out _, out var p, IntPtr.Zero) != 0) return int.MinValue;
            try { return Marshal.ReadInt32(p); }
            finally { WlanFreeMemory(p); }
        }

        // ------------------------------------------------------------------
        //  Сети рядом
        // ------------------------------------------------------------------

        /// <summary>Просим адаптер пересканировать эфир (результат появляется через 2–4 с).</summary>
        public static Task<bool> Rescan() => Task.Run(() => With((h, g, d) => WlanScan(h, ref g, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero) == 0, false));

        public static Task<List<WifiAp>> Nearby(bool scan = true) => Task.Run(async () =>
        {
            if (scan && await Rescan()) await Task.Delay(3500);
            var cur = await Current();
            return With((h, g, d) =>
            {
                var res = new List<WifiAp>();
                // dot11_BSS_type_any = 3
                if (WlanGetNetworkBssList(h, ref g, IntPtr.Zero, 3, false, IntPtr.Zero, out var list) != 0) return res;
                try
                {
                    int n = Marshal.ReadInt32(list + 4);
                    for (int i = 0; i < n; i++)
                    {
                        var e = list + 8 + i * 360;                 // WLAN_BSS_ENTRY — 360 байт
                        var ssid = Ssid(e);
                        var bssid = Mac(e + 40);
                        int phy = Marshal.ReadInt32(e + 52);
                        int rssi = Marshal.ReadInt32(e + 56);
                        int caps = (ushort)Marshal.ReadInt16(e + 88);
                        int freq = Marshal.ReadInt32(e + 92);
                        int ieOff = Marshal.ReadInt32(e + 352), ieSize = Marshal.ReadInt32(e + 356);
                        var (ch, band) = FromFreq(freq);
                        var (sec, wps) = Security(e + ieOff, ieSize, (caps & 0x10) != 0);
                        res.Add(new WifiAp(ssid == "" ? "(скрытая сеть)" : ssid, bssid, rssi, Percent(rssi), ch, band, Phy(phy), sec,
                            cur != null && string.Equals(cur.Bssid, bssid, StringComparison.OrdinalIgnoreCase), wps));
                    }
                }
                finally { WlanFreeMemory(list); }
                return res.OrderByDescending(a => a.Current).ThenByDescending(a => a.Rssi).ToList();
            }, new List<WifiAp>());
        });

        /// <summary>Защита сети по информационным элементам маяка: RSN (WPA2/WPA3), WPA, иначе WEP или открытая; плюс включён ли WPS.</summary>
        static (string Security, bool Wps) Security(IntPtr ie, int size, bool privacy)
        {
            var b = new byte[Math.Clamp(size, 0, 4096)];
            try { Marshal.Copy(ie, b, 0, b.Length); } catch { return (privacy ? "защищена" : "Открытая", false); }
            bool wps = false;
            for (int i = 0; i + 5 < b.Length;)
            {
                int id = b[i], len = b[i + 1];
                if (i + 2 + len > b.Length) break;
                if (id == 221 && len >= 4 && b[i + 2] == 0x00 && b[i + 3] == 0x50 && b[i + 4] == 0xF2 && b[i + 5] == 4) wps = true;
                i += 2 + len;
            }
            return (SecurityName(b, privacy), wps);
        }

        static string SecurityName(byte[] b, bool privacy)
        {
            try
            {
                bool wpa = false;
                for (int i = 0; i + 1 < b.Length;)
                {
                    int id = b[i], len = b[i + 1];
                    if (i + 2 + len > b.Length) break;
                    if (id == 48 && len >= 8)
                    {
                        // RSN: версия(2), групповой шифр(4), N парных(2)+4N, N AKM(2)+4N — SAE (8) или SAE-EXT (24) → WPA3
                        int p = i + 2 + 2 + 4;
                        int pc = b[p] | (b[p + 1] << 8);
                        p += 2 + 4 * pc;
                        if (p + 2 <= i + 2 + len)
                        {
                            int ac = b[p] | (b[p + 1] << 8);
                            bool sae = false, psk = false, ent = false;
                            for (int k = 0; k < ac && p + 2 + 4 * k + 4 <= i + 2 + len; k++)
                            {
                                int type = b[p + 2 + 4 * k + 3];
                                if (type is 8 or 24) sae = true;
                                else if (type is 2 or 6) psk = true;
                                else if (type is 1 or 5 or 12) ent = true;
                            }
                            return sae && psk ? "WPA2/WPA3" : sae ? "WPA3" : ent ? "WPA2-Enterprise" : "WPA2";
                        }
                        return "WPA2";
                    }
                    if (id == 221 && len >= 4 && b[i + 2] == 0x00 && b[i + 3] == 0x50 && b[i + 4] == 0xF2 && b[i + 5] == 1) wpa = true;
                    i += 2 + len;
                }
                return wpa ? "WPA" : privacy ? "WEP" : "Открытая";
            }
            catch { return privacy ? "защищена" : "Открытая"; }
        }

        /// <summary>Совет по каналу: насколько занят текущий и какой свободнее (2,4 ГГц — только 1/6/11, 5 ГГц — без DFS).</summary>
        public static (string Now, string Best) ChannelAdvice(List<WifiAp> aps, WifiLink link)
        {
            if (link == null || link.Channel == 0) return ("", "");
            bool is24 = link.Channel <= 14;
            double Load(int ch) => aps.Where(a => Neighbor(a, link) && (a.Channel <= 14) == is24)
                .Sum(a => (is24 ? Math.Max(0, 1 - Math.Abs(a.Channel - ch) / 5.0) : a.Channel == ch ? 1 : 0) * (a.Signal / 100.0 + 0.2));
            int[] candidates = is24 ? new[] { 1, 6, 11 } : new[] { 36, 40, 44, 48, 149, 153, 157, 161 };
            int others = aps.Count(a => Neighbor(a, link) && a.Channel == link.Channel);
            int best = candidates.OrderBy(Load).First();
            string now = others == 0 ? $"На канале {link.Channel} кроме тебя никого — отлично" : $"На канале {link.Channel} ещё сетей: {others}";
            string adv = Load(best) + 0.3 < Load(link.Channel) && best != link.Channel
                ? $"Свободнее канал {best} — его можно выбрать в настройках роутера"
                : "Канал выбран удачно, менять не нужно";
            return (now, adv);
        }

        /// <summary>Та же коробка: у одного роутера сети 2,4/5 ГГц, гостевая и скрытая отличаются только первым и последним байтом BSSID.</summary>
        public static bool SameRouter(string a, string b)
        {
            if (a == null || b == null || a.Length != 17 || b.Length != 17) return false;
            return string.Equals(a[3..14], b[3..14], StringComparison.OrdinalIgnoreCase);
        }

        static bool Neighbor(WifiAp a, WifiLink link) => link == null || (a.Bssid != link.Bssid && !SameRouter(a.Bssid, link.Bssid));

        static readonly int[] Ch24 = Enumerable.Range(1, 13).ToArray();
        static readonly int[] Ch5 = { 36, 40, 44, 48, 52, 56, 60, 64, 100, 104, 108, 112, 116, 120, 124, 128, 132, 136, 140, 144, 149, 153, 157, 161, 165 };
        static bool Dfs(int ch) => ch is >= 52 and <= 144;

        /// <summary>Загрузка каждого канала: 2,4 ГГц учитывает перекрытие соседних каналов (±4), 5 ГГц — только свой канал.
        /// «Лучший» — свободнее всех среди тех, что стоит выбирать (1/6/11 и 5 ГГц без радарного DFS).</summary>
        public static List<ChannelLoad> ChannelTable(List<WifiAp> aps, WifiLink link)
        {
            var res = new List<ChannelLoad>();
            foreach (var (chs, band, is24) in new[] { (Ch24, "2,4 ГГц", true), (Ch5, "5 ГГц", false) })
            {
                var others = aps.Where(a => (a.Channel <= 14) == is24 && a.Channel > 0 && Neighbor(a, link)).ToList();
                double Load(int ch) => others.Sum(a => (is24 ? Math.Max(0, 1 - Math.Abs(a.Channel - ch) / 5.0) : a.Channel == ch ? 1 : 0) * (a.Signal / 100.0 + 0.2));
                var pick = is24 ? new[] { 1, 6, 11 } : chs.Where(c => !Dfs(c)).ToArray();
                int best = pick.OrderBy(Load).ThenBy(c => c).First();
                foreach (var ch in chs)
                    res.Add(new ChannelLoad(ch, band, others.Count(a => a.Channel == ch), Math.Round(Load(ch), 2), link?.Channel == ch, ch == best));
            }
            return res;
        }

        /// <summary>Свободные каналы одной строкой: «2,4 ГГц: 1, 11 · 5 ГГц: 40, 44, 149…».</summary>
        public static string FreeChannels(List<ChannelLoad> table)
        {
            string Part(string band, Func<ChannelLoad, bool> ok) =>
                band + ": " + string.Join(", ", table.Where(c => c.Band == band && ok(c) && c.Load < 0.15).Select(c => c.Channel).Take(8));
            return Part("2,4 ГГц", c => c.Channel is 1 or 6 or 11) + " · " + Part("5 ГГц", c => !Dfs(c.Channel));
        }

        /// <summary>Время ответа DNS-сервера (настоящий UDP-запрос), мс; -1 — не ответил.</summary>
        public static async Task<double> DnsSpeed(IPAddress server, string name = "www.wikipedia.org")
        {
            var q = new List<byte> { 0x4E, 0x49, 0x01, 0x00, 0, 1, 0, 0, 0, 0, 0, 0 };
            foreach (var label in name.Split('.')) { q.Add((byte)label.Length); q.AddRange(Encoding.ASCII.GetBytes(label)); }
            q.AddRange(new byte[] { 0, 0, 1, 0, 1 });
            var times = new List<double>();
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    using var udp = new UdpClient(server.AddressFamily);
                    var sw = Stopwatch.StartNew();
                    await udp.SendAsync(q.ToArray(), q.Count, new IPEndPoint(server, 53));
                    var recv = udp.ReceiveAsync();
                    if (await Task.WhenAny(recv, Task.Delay(2000)) == recv) times.Add(sw.Elapsed.TotalMilliseconds);
                }
                catch { }
            }
            return times.Count == 0 ? -1 : times.OrderBy(t => t).ElementAt(times.Count / 2);
        }

        /// <summary>DNS-серверы, которые сейчас использует Wi-Fi (IPv4).</summary>
        public static List<IPAddress> CurrentDns()
        {
            try
            {
                var n = NetworkInterface.GetAllNetworkInterfaces().Where(x => x.OperationalStatus == OperationalStatus.Up)
                    .OrderBy(x => x.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? 0 : 1)
                    .FirstOrDefault(x => x.GetIPProperties().GatewayAddresses.Any());
                return n?.GetIPProperties().DnsAddresses.Where(a => a.AddressFamily == AddressFamily.InterNetwork).ToList() ?? new();
            }
            catch { return new(); }
        }

        /// <summary>Драйвер Wi-Fi адаптера: версия и дата (через WMI).</summary>
        public static (string Version, DateTime? Date) Driver(string adapter)
        {
            try
            {
                using var s = new System.Management.ManagementObjectSearcher("SELECT DeviceName, DriverVersion, DriverDate FROM Win32_PnPSignedDriver WHERE DeviceClass='NET'");
                foreach (System.Management.ManagementObject o in s.Get())
                    using (o)
                    {
                        if (!string.Equals(o["DeviceName"] as string, adapter, StringComparison.OrdinalIgnoreCase)) continue;
                        DateTime? d = o["DriverDate"] is string ds && ds.Length >= 8 && DateTime.TryParseExact(ds[..8], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt) ? dt : null;
                        return (o["DriverVersion"] as string ?? "", d);
                    }
            }
            catch { }
            return ("", null);
        }

        /// <summary>Сеть помечена как лимитная (Windows экономит трафик: не качает обновления, приложения в фоне).</summary>
        public static bool? Metered()
        {
            try
            {
                var p = Windows.Networking.Connectivity.NetworkInformation.GetInternetConnectionProfile();
                if (p == null) return null;
                return p.GetConnectionCost().NetworkCostType is Windows.Networking.Connectivity.NetworkCostType.Fixed or Windows.Networking.Connectivity.NetworkCostType.Variable;
            }
            catch { return null; }
        }

        /// <summary>Какой стандарт умеет адаптер — по его названию (AX201 → Wi-Fi 6, BE200 → Wi-Fi 7…).</summary>
        static int AdapterGen(string desc)
        {
            var d = (desc ?? "").ToUpperInvariant();
            if (d.Contains("WI-FI 7") || Regex.IsMatch(d, @"\bBE\d{3}")) return 7;
            if (d.Contains("WI-FI 6") || Regex.IsMatch(d, @"\bAX\d{3}") || d.Contains("802.11AX")) return 6;
            if (d.Contains("WI-FI 5") || Regex.IsMatch(d, @"\bAC\s?\d{4}|\b\d{4}AC\b|8265|8260|7265|3165|3168") || d.Contains("802.11AC")) return 5;
            return 0;
        }

        static int LinkGen(string standard) => standard.Contains("7") ? 7 : standard.Contains("6") ? 6 : standard.Contains("5") ? 5 : standard.Contains("4") ? 4 : 0;

        /// <summary>
        /// Полная проверка сети: сигнал, диапазон, канал, защита и WPS, стандарт роутера против адаптера, задержки и потери,
        /// скорость DNS (свой против Cloudflare и Google), драйвер, лимитное подключение. По каждому пункту — вывод и совет.
        /// </summary>
        public static async Task<(List<HealthItem> Items, List<ChannelLoad> Channels, LinkQuality Quality)> Health(Action<string> progress = null)
        {
            var items = new List<HealthItem>();
            void Add(int lvl, string title, string advice) => items.Add(new HealthItem(lvl, title, advice));
            progress?.Invoke("Сканирую эфир…");
            var link = await Current();
            var aps = await Nearby();
            var table = ChannelTable(aps, link);
            if (link == null)
            {
                bool wired = NetworkInterface.GetIsNetworkAvailable();
                Add(wired ? 0 : 2, wired ? "Подключение по кабелю (или Wi-Fi выключен)" : "Wi-Fi не подключён",
                    wired ? "Проверяю интернет, DNS и задержки." : HasAdapter ? "Подключись к сети, чтобы проверить её." : "На этом компьютере нет Wi-Fi адаптера или он выключен в Диспетчере устройств.");
                if (wired) await NetChecks(items, progress);
                return (items, table, null);
            }

            // сигнал
            if (link.Rssi >= -60) Add(0, $"Сигнал отличный ({link.Rssi} dBm)", "Расстояние до роутера в самый раз.");
            else if (link.Rssi >= -70) Add(1, $"Сигнал средний ({link.Rssi} dBm)", "Видео пойдёт, но скорость не максимальная. Ближе к роутеру или без стен по пути будет лучше.");
            else Add(2, $"Сигнал слабый ({link.Rssi} dBm)", "Подойди ближе, подними роутер повыше и подальше от микроволновки и металла. Помогут репитер или mesh-система.");

            // диапазон: есть ли 5 ГГц у того же роутера
            if (link.Band.StartsWith("2"))
            {
                string Root(string s) => Regex.Replace(s.ToLowerInvariant(), @"[\s_\-]*(5g|5ghz|5ггц|_5|-5)$", "");
                var five = aps.FirstOrDefault(a => a.Channel > 14 && (a.Ssid == link.Ssid || Root(a.Ssid) == Root(link.Ssid)));
                Add(1, "Подключение на 2,4 ГГц", five != null
                    ? $"У твоего роутера есть сеть 5 ГГц — «{five.Ssid}» (сигнал {five.Signal}%). Подключись к ней: скорость будет в 2–5 раз выше, помех меньше."
                    : "2,4 ГГц дальнобойнее, но медленнее и шумнее. Если роутер умеет 5 ГГц — включи её в его настройках.");
            }
            else Add(0, $"Диапазон {link.Band}", "Быстрый диапазон с малым количеством помех.");

            // канал
            var mine = table.FirstOrDefault(c => c.Current);
            var best = table.FirstOrDefault(c => c.Best && c.Band == link.Band);
            int same = aps.Count(a => Neighbor(a, link) && a.Channel == link.Channel);
            if (mine != null && best != null && best.Channel != link.Channel && best.Load + 0.4 < mine.Load)
                Add(1, $"Канал {link.Channel} загружен (сетей рядом: {same})", $"Свободнее канал {best.Channel}. Поменяй его в настройках роутера (раздел Wi-Fi / Беспроводная сеть → Канал).");
            else Add(0, $"Канал {link.Channel}", same == 0 ? "На твоём канале больше никого — помех нет." : $"Соседей на канале: {same}, но это лучший вариант из доступных.");
            // вторая сеть того же роутера на 2,4 ГГц на «перекрывающемся» канале (2–5, 7–10, 12–13)
            var own24 = aps.FirstOrDefault(a => a.Channel is > 0 and <= 14 && (a.Bssid == link.Bssid || SameRouter(a.Bssid, link.Bssid)));
            if (own24 != null && own24.Channel is not (1 or 6 or 11))
            {
                var free24 = table.Where(c => c.Band == "2,4 ГГц" && c.Channel is 1 or 6 or 11).OrderBy(c => c.Load).First();
                Add(1, $"Сеть 2,4 ГГц роутера («{own24.Ssid}») на канале {own24.Channel}",
                    $"На 2,4 ГГц ставь только 1, 6 или 11 — остальные каналы перекрывают соседей и ловят их помехи. Свободнее всего сейчас канал {free24.Channel}.");
            }
            if (link.Band.StartsWith("5") && Dfs(link.Channel))
                Add(1, $"Канал {link.Channel} — радарный (DFS)", "На таких каналах роутер обязан уходить при сигнале радаров — бывают короткие обрывы. Каналы 36–48 и 149–161 стабильнее.");

            // защита
            var curAp = aps.FirstOrDefault(a => a.Current);
            if (link.Security is "Открытая" or "WEP")
                Add(2, $"Защита: {link.Security}", "Сеть не защищена — соседи могут подключиться и видеть незашифрованный трафик. Включи в роутере WPA2 или WPA3 с длинным паролем.");
            else if (link.Security.StartsWith("WPA3")) Add(0, "Защита WPA3", "Самая современная защита Wi-Fi.");
            else Add(0, $"Защита {link.Security}", "Надёжно при длинном пароле (от 12 символов). Если роутер и все устройства умеют WPA3 — можно включить её.");
            if (curAp?.Wps == true)
                Add(1, "Включён WPS", "Подключение по кнопке/PIN-коду WPS — известная дыра: PIN подбирается за часы. Выключи WPS в настройках роутера.");

            // стандарт
            int ag = AdapterGen(link.Adapter), lg = LinkGen(link.Standard);
            if (ag > 0 && lg > 0 && ag > lg)
                Add(1, $"Роутер работает в {link.Standard}, а адаптер умеет Wi-Fi {ag}", ag >= 6 && lg <= 5
                    ? "Роутер с Wi-Fi 6 дал бы больше скорости и стабильности при многих устройствах. Если роутер уже Wi-Fi 6 — включи режим 802.11ax в его настройках."
                    : "Проверь в настройках роутера, включён ли самый новый режим.");
            else Add(0, $"Стандарт {link.Standard}", ag > 0 ? $"Адаптер и роутер используют лучший общий стандарт." : "");

            // скорость соединения
            double expected = link.Band.StartsWith("2") ? 72 : 200;
            if (link.Rssi >= -65 && Math.Max(link.RxMbps, link.TxMbps) < expected)
                Add(1, $"Низкая скорость соединения ({Math.Max(link.RxMbps, link.TxMbps):0} Мбит/с) при хорошем сигнале", "Похоже на помехи или ограничение ширины канала в роутере (20 МГц). Смени канал или включи 40/80 МГц для 5 ГГц.");
            else Add(0, $"Скорость соединения {Math.Max(link.RxMbps, link.TxMbps):0} Мбит/с", "Это скорость между ноутбуком и роутером — реальный интернет зависит от тарифа.");

            // качество: роутер и интернет
            progress?.Invoke("Проверяю задержки и потери…");
            var q = await TestQuality(progress);
            int ql = q.Verdict.StartsWith("Отлично") || q.Verdict.StartsWith("Хорошо") ? 0 : q.Verdict.Contains("DNS") ? 1 : 2;
            Add(ql, $"Связь: {q.Verdict.ToLowerInvariant()} (роутер {q.GwMs:0} мс, интернет {q.NetMs:0} мс, потери {Math.Max(q.GwLoss, q.NetLoss):0}%)", q.Advice);

            // DNS
            progress?.Invoke("Сравниваю DNS-серверы…");
            var dns = CurrentDns();
            if (dns.Count > 0)
            {
                var mineMs = await DnsSpeed(dns[0]);
                var cf = await DnsSpeed(IPAddress.Parse("1.1.1.1"));
                var gg = await DnsSpeed(IPAddress.Parse("8.8.8.8"));
                double bestPublic = new[] { cf, gg }.Where(x => x > 0).DefaultIfEmpty(-1).Min();
                string fastest = cf > 0 && (gg < 0 || cf <= gg) ? "Cloudflare (1.1.1.1)" : "Google (8.8.8.8)";
                if (mineMs < 0) Add(2, $"DNS {dns[0]} не отвечает", "Сайты могут не открываться. Поставь DNS Cloudflare или Google в разделе «Интернет и Wi-Fi».");
                else if (bestPublic > 0 && mineMs > bestPublic * 2 && mineMs - bestPublic > 25)
                    Add(1, $"DNS медленный: {mineMs:0} мс (у {fastest} — {bestPublic:0} мс)", "Смени DNS в разделе «Интернет и Wi-Fi» — сайты начнут открываться быстрее.");
                else Add(0, $"DNS {dns[0]} отвечает за {mineMs:0} мс", bestPublic > 0 ? $"Публичные: Cloudflare {cf:0} мс, Google {gg:0} мс — менять не нужно." : "");
            }

            await NetChecks(items, progress);

            // драйвер
            var (ver, date) = Driver(link.Adapter);
            if (date is DateTime d && (DateTime.Now - d).TotalDays > 730)
                Add(1, $"Драйвер Wi-Fi от {d:dd.MM.yyyy}", "Драйверу больше двух лет. Новый с сайта производителя ноутбука или адаптера часто чинит обрывы и ускоряет связь.");
            else if (date != null) Add(0, $"Драйвер Wi-Fi {ver}", $"от {date:dd.MM.yyyy} — свежий.");

            // лимитное подключение
            if (Metered() == true)
                Add(1, "Сеть помечена как лимитная", "Windows экономит трафик: не скачивает обновления и ограничивает фоновые приложения. Для домашнего безлимита выключи: Параметры → Wi-Fi → свойства сети → «Лимитное подключение».");

            return (items, table, q);
        }

        // ------------------------------------------------------------------
        //  Интернет глубже: страница входа, подмена DNS, IPv6, лаги под нагрузкой, MTU, часы
        // ------------------------------------------------------------------

        static readonly System.Net.Http.HttpClient Probe = new(new System.Net.Http.SocketsHttpHandler { AllowAutoRedirect = false, ConnectTimeout = TimeSpan.FromSeconds(6) }) { Timeout = TimeSpan.FromSeconds(30) };

        static async Task NetChecks(List<HealthItem> items, Action<string> progress)
        {
            void Add(int lvl, string title, string advice) => items.Add(new HealthItem(lvl, title, advice));

            // 1. настоящий ли это интернет (кафе, отели, метро подсовывают страницу входа)
            progress?.Invoke("Проверяю, нет ли страницы входа…");
            try
            {
                using var r = await Probe.GetAsync("http://www.msftconnecttest.com/connecttest.txt");
                var body = await r.Content.ReadAsStringAsync();
                if ((int)r.StatusCode is >= 300 and < 400 || body.Trim() != "Microsoft Connect Test")
                    Add(2, "Сеть просит войти (страница входа)", "Это сеть кафе, отеля или провайдера с авторизацией: открой любой сайт в браузере и войди. До этого интернет не работает или работает частично.");
                else Add(0, "Интернет настоящий", "Сеть не подменяет страницы и не требует входа.");
            }
            catch { Add(2, "Нет доступа в интернет", "До роутера связь есть, а дальше — нет. Перезагрузи роутер; если не поможет — проблема у провайдера."); }

            // 2. подмена ответов DNS: несуществующий сайт должен давать «не найдено», а не рекламу провайдера
            try
            {
                var fake = "nisland-check-" + Guid.NewGuid().ToString("N")[..10] + ".com";
                var addrs = await Dns.GetHostAddressesAsync(fake);
                if (addrs.Length > 0)
                    Add(1, "DNS подменяет ответы", $"Несуществующий адрес открылся как {addrs[0]} — провайдер или роутер перенаправляет ошибки на свою страницу (часто с рекламой). Поставь DNS Cloudflare или Google в разделе «Интернет и Wi-Fi».");
                else Add(0, "DNS честный", "Несуществующие сайты не перенаправляются на рекламу.");
            }
            catch (SocketException) { Add(0, "DNS честный", "Несуществующие сайты не перенаправляются на рекламу."); }
            catch { }

            // 3. IPv6
            try
            {
                using var tcp = new TcpClient(AddressFamily.InterNetworkV6);
                var c = tcp.ConnectAsync(IPAddress.Parse("2606:4700:4700::1111"), 443);
                bool v6 = await Task.WhenAny(c, Task.Delay(3000)) == c && tcp.Connected;
                Add(0, v6 ? "IPv6 работает" : "IPv6 нет", v6 ? "Новый протокол интернета доступен — некоторые сайты и игры работают через него быстрее." : "Провайдер или роутер не даёт IPv6. Ничего страшного — почти всё работает по IPv4.");
            }
            catch { Add(0, "IPv6 нет", "Провайдер или роутер не даёт IPv6. Ничего страшного — почти всё работает по IPv4."); }

            // 4. MTU: самый большой пакет без дробления
            try
            {
                using var ping = new Ping();
                int lo = 1200, hi = 1472, best = 0;
                var opts = new PingOptions(64, true);
                while (lo <= hi)
                {
                    int mid = (lo + hi) / 2;
                    var r = await ping.SendPingAsync(IPAddress.Parse("1.1.1.1"), 1500, new byte[mid], opts);
                    if (r.Status == IPStatus.Success) { best = mid; lo = mid + 1; } else hi = mid - 1;
                }
                if (best > 0)
                {
                    int mtu = best + 28;
                    if (mtu >= 1500) Add(0, "MTU 1500", "Пакеты идут без дробления — оптимально.");
                    else if (mtu >= 1400) Add(0, $"MTU {mtu}", "Немного меньше 1500 — обычно из-за PPPoE у провайдера или VPN. Это нормально.");
                    else Add(1, $"MTU {mtu} — маленький", "Часть сайтов может открываться долго или «висеть». Проверь настройки MTU в роутере (для PPPoE обычно 1492).");
                }
            }
            catch { }

            // 5. часы: сильно сбитое время ломает HTTPS («сертификат недействителен»)
            try
            {
                using var r = await Probe.SendAsync(new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Head, "https://www.cloudflare.com/"));
                if (r.Headers.Date is DateTimeOffset srv)
                {
                    var diff = (DateTimeOffset.UtcNow - srv).TotalSeconds;
                    if (Math.Abs(diff) > 120) Add(1, $"Часы ПК сбиты на {Math.Abs(diff) / 60:0} мин", "Из-за этого сайты пишут «соединение не защищено». Параметры → Время и язык → «Синхронизировать сейчас».");
                    else Add(0, "Часы ПК точные", $"Расхождение с интернетом {Math.Abs(diff):0} с.");
                }
            }
            catch { }

            // 6. лаги под нагрузкой (bufferbloat) и скорость загрузки
            progress?.Invoke("Качаю тестовый файл и меряю задержку под нагрузкой…");
            try
            {
                var idle = await PingSeries(IPAddress.Parse("1.1.1.1"), 6);
                var dl = Task.Run(async () =>
                {
                    var sw = Stopwatch.StartNew();
                    long total = 0;
                    using var resp = await Probe.GetAsync("https://speed.cloudflare.com/__down?bytes=40000000", System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
                    using var s = await resp.Content.ReadAsStreamAsync();
                    var buf = new byte[81920];
                    int n;
                    while ((n = await s.ReadAsync(buf)) > 0 && sw.Elapsed.TotalSeconds < 12) total += n;
                    return total * 8 / Math.Max(0.1, sw.Elapsed.TotalSeconds) / 1e6;
                });
                await Task.Delay(800);
                var load = await PingSeries(IPAddress.Parse("1.1.1.1"), 10);
                double mbps = await dl;
                double extra = load.Avg - idle.Avg;
                string grade = extra < 20 ? "A" : extra < 60 ? "B" : extra < 150 ? "C" : "D";
                Add(0, $"Скорость загрузки ≈ {mbps:0} Мбит/с", "Быстрый тест через Cloudflare. Точнее — «Тест скорости» в этом разделе.");
                if (grade is "A" or "B") Add(0, $"Лаги под нагрузкой: {grade} (+{Math.Max(0, extra):0} мс)", "Когда кто-то качает файлы, задержка почти не растёт — игры и звонки не лагают.");
                else Add(grade == "C" ? 1 : 2, $"Лаги под нагрузкой: {grade} (+{extra:0} мс)",
                    "Пока что-то скачивается, задержка сильно растёт — в играх и звонках будут лаги (bufferbloat). Включи в роутере SQM / Smart QoS или ограничь скорость загрузки на 10% ниже тарифа.");
            }
            catch (Exception e) { Log.Info("bufferbloat: " + e.Message); }
        }

        // ------------------------------------------------------------------
        //  Качество связи: роутер против интернета
        // ------------------------------------------------------------------

        public static IPAddress Gateway()
        {
            try
            {
                var nic = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                    .Concat(NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback))
                    .FirstOrDefault(n => n.GetIPProperties().GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork));
                return nic?.GetIPProperties().GatewayAddresses.First(g => g.Address.AddressFamily == AddressFamily.InterNetwork).Address;
            }
            catch { return null; }
        }

        static async Task<(double Avg, double Jitter, double Loss)> PingSeries(IPAddress ip, int count, Action<int> progress = null)
        {
            var times = new List<double>();
            int lost = 0;
            using var ping = new Ping();
            for (int i = 0; i < count; i++)
            {
                try
                {
                    var r = await ping.SendPingAsync(ip, 1000);
                    if (r.Status == IPStatus.Success) times.Add(r.RoundtripTime); else lost++;
                }
                catch { lost++; }
                progress?.Invoke(i + 1);
                await Task.Delay(150);
            }
            double avg = times.Count > 0 ? times.Average() : 0;
            double jitter = times.Count > 1 ? times.Zip(times.Skip(1), (a, b) => Math.Abs(a - b)).Average() : 0;
            return (avg, jitter, 100.0 * lost / count);
        }

        /// <summary>20 пингов до роутера и до интернета параллельно + время DNS — и понятный вывод, где проблема.</summary>
        public static async Task<LinkQuality> TestQuality(Action<string> progress = null)
        {
            var gw = Gateway();
            int done = 0;
            void Tick(int _) { Interlocked.Increment(ref done); progress?.Invoke($"Проверяю… {Math.Min(100, done * 100 / 40)}%"); }
            var gwTask = gw != null ? PingSeries(gw, 20, Tick) : Task.FromResult((0.0, 0.0, 100.0));
            var netTask = PingSeries(IPAddress.Parse("1.1.1.1"), 20, Tick);
            double dns = -1;
            try
            {
                var sw = Stopwatch.StartNew();
                await Dns.GetHostAddressesAsync("www.microsoft.com");
                dns = sw.Elapsed.TotalMilliseconds;
            }
            catch { }
            var (ga, gj, gl) = await gwTask;
            var (na, nj, nl) = await netTask;

            string verdict, advice;
            if (gw == null) { verdict = "Нет подключения к роутеру"; advice = "Проверь, подключён ли Wi-Fi."; }
            else if (gl >= 10 || ga > 40 || gj > 25)
            {
                verdict = "Слабое место — Wi-Fi";
                advice = "Связь с роутером нестабильна: подойди ближе, убери преграды, попробуй 5 ГГц или другой канал. Провайдер тут ни при чём.";
            }
            else if (nl >= 5 || na > 120 || nj > 40)
            {
                verdict = "Слабое место — интернет (провайдер)";
                advice = "До роутера всё отлично, а дальше — задержки или потери. Перезагрузи роутер; если не поможет — это к провайдеру.";
            }
            else if (dns > 250) { verdict = "Связь хорошая, но DNS медленный"; advice = "Сайты открываются с задержкой — смени DNS на Cloudflare или Google в разделе «Интернет и Wi-Fi»."; }
            else if (ga < 8 && na < 60) { verdict = "Отлично"; advice = "И Wi-Fi, и интернет работают быстро и стабильно."; }
            else { verdict = "Хорошо"; advice = "Связь в норме: подходит для видео и звонков."; }
            return new LinkQuality(ga, gj, gl, na, nj, nl, dns, verdict, advice);
        }

        // ------------------------------------------------------------------
        //  Устройства в сети
        // ------------------------------------------------------------------

        /// <summary>Кто подключён к той же сети: опрос адресов подсети и таблица ARP. Только чтение, ничего не меняет.</summary>
        public static async Task<List<LanDevice>> Devices(Action<string> progress = null)
        {
            var (own, mask) = OwnIp();
            if (own == null) return new();
            var b = own.GetAddressBytes();
            var m = mask.GetAddressBytes();
            // не больше /24: в больших сетях сканируем 254 адреса вокруг своего
            var baseAddr = new byte[] { (byte)(b[0] & m[0]), (byte)(b[1] & m[1]), (byte)(b[2] & m[2]), 0 };
            if (m[2] != 255) baseAddr = new byte[] { b[0], b[1], b[2], 0 };
            var gw = Gateway();
            var times = new System.Collections.Concurrent.ConcurrentDictionary<string, double>();
            using var gate = new SemaphoreSlim(48);
            int done = 0;
            var tasks = Enumerable.Range(1, 254).Select(async i =>
            {
                var ip = new IPAddress(new byte[] { baseAddr[0], baseAddr[1], baseAddr[2], (byte)i });
                await gate.WaitAsync();
                try
                {
                    using var p = new Ping();
                    var r = await p.SendPingAsync(ip, 700);
                    if (r.Status == IPStatus.Success) times[ip.ToString()] = r.RoundtripTime;
                }
                catch { }
                finally { gate.Release(); progress?.Invoke($"Ищу устройства… {Interlocked.Increment(ref done) * 100 / 254}%"); }
            });
            await Task.WhenAll(tasks);

            // ARP: телефоны часто не отвечают на ping, но MAC уже в таблице
            var arp = await Task.Run(() => Maint.RunCmd("arp", "-a"));
            var macs = new Dictionary<string, string>();
            foreach (Match x in Regex.Matches(arp, @"(\d+\.\d+\.\d+\.\d+)\s+([0-9a-fA-F]{2}(?:-[0-9a-fA-F]{2}){5})"))
            {
                var ip = x.Groups[1].Value;
                if (!ip.StartsWith($"{baseAddr[0]}.{baseAddr[1]}.{baseAddr[2]}.") || ip.EndsWith(".255")) continue;
                var mac = x.Groups[2].Value.Replace('-', ':').ToLowerInvariant();
                if (mac.StartsWith("ff:ff") || mac.StartsWith("01:00:5e")) continue;
                macs[ip] = mac;
            }
            var ips = times.Keys.Union(macs.Keys).Distinct().ToList();
            var list = new List<LanDevice>();
            var names = await Task.WhenAll(ips.Select(async ip =>
            {
                try
                {
                    var t = Dns.GetHostEntryAsync(ip);
                    if (await Task.WhenAny(t, Task.Delay(1500)) == t && t.Result.HostName != ip) return (ip, t.Result.HostName.Split('.')[0]);
                }
                catch { }
                return (ip, "");
            }));
            foreach (var (ip, name) in names)
            {
                macs.TryGetValue(ip, out var mac);
                string kind = ip == own.ToString() ? "Этот компьютер"
                    : gw != null && ip == gw.ToString() ? "Роутер"
                    : mac != null && (Convert.ToByte(mac[..2], 16) & 0x02) != 0 ? "Телефон или ноутбук (случайный MAC)"
                    : "Устройство";
                if (ip == own.ToString()) mac ??= OwnMac();
                list.Add(new LanDevice(ip, mac ?? "", name, kind, times.TryGetValue(ip, out var ms) ? ms : -1));
            }
            return list.OrderBy(d => d.Kind == "Роутер" ? 0 : d.Kind == "Этот компьютер" ? 1 : 2).ThenBy(d => Version.TryParse(d.Ip, out var v) ? v : new Version()).ToList();
        }

        static (IPAddress Ip, IPAddress Mask) OwnIp()
        {
            try
            {
                foreach (var n in NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up)
                             .OrderBy(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? 0 : 1))
                {
                    var props = n.GetIPProperties();
                    if (!props.GatewayAddresses.Any()) continue;
                    var a = props.UnicastAddresses.FirstOrDefault(u => u.Address.AddressFamily == AddressFamily.InterNetwork);
                    if (a != null) return (a.Address, a.IPv4Mask);
                }
            }
            catch { }
            return (null, null);
        }

        static string OwnMac()
        {
            try
            {
                var n = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(x => x.OperationalStatus == OperationalStatus.Up && x.NetworkInterfaceType == NetworkInterfaceType.Wireless80211);
                return n == null ? null : string.Join(":", n.GetPhysicalAddress().GetAddressBytes().Select(x => x.ToString("x2")));
            }
            catch { return null; }
        }

        // ------------------------------------------------------------------
        //  Действия
        // ------------------------------------------------------------------

        /// <summary>Переподключиться к текущей сети — помогает, когда Wi-Fi «подвис».</summary>
        public static async Task<string> Reconnect()
        {
            var cur = await Current();
            if (cur == null) return "Wi-Fi не подключён";
            await Task.Run(() => Maint.RunCmd("netsh", "wlan disconnect"));
            await Task.Delay(1500);
            await Task.Run(() => Maint.RunCmd("netsh", $"wlan connect name=\"{cur.Profile.Replace("\"", "")}\""));
            for (int i = 0; i < 20; i++)
            {
                await Task.Delay(500);
                if (await Current() is { } now) return "Переподключено к «" + now.Ssid + "» ✓";
            }
            return "Не удалось переподключиться — выбери сеть вручную в трее Windows";
        }

        /// <summary>Забыть сохранённую сеть (Windows перестанет к ней подключаться и удалит пароль).</summary>
        public static Task<string> Forget(string profile) => Task.Run(() => With((h, g, d) =>
        {
            int r = WlanDeleteProfile(h, ref g, profile, IntPtr.Zero);
            return r == 0 ? "Сеть «" + profile + "» забыта" : r == 5 ? "Эту сеть может удалить только администратор" : "Не удалось (код " + r + ")";
        }, "Wi-Fi адаптер не найден"));

        /// <summary>Страница настроек роутера (адрес шлюза).</summary>
        public static void OpenRouter()
        {
            var gw = Gateway();
            if (gw != null) SystemService.Run("http://" + gw);
        }

        public static string Bars(int signal) => signal >= 75 ? "▂▄▆█" : signal >= 50 ? "▂▄▆" : signal >= 25 ? "▂▄" : "▂";

        public static string SignalWord(int rssi) => rssi >= -55 ? "отличный" : rssi >= -67 ? "хороший" : rssi >= -75 ? "средний" : "слабый";
    }
}
