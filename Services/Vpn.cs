using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Win32;
using NIsland.Core;

namespace NIsland.Services
{
    public enum VpnState { Unknown, Off, Connecting, On }

    /// <summary>Одно подключение: программа VPN, её конфиг (OpenVPN, WireGuard) или VPN-подключение Windows.</summary>
    public class VpnItem
    {
        public string Key, Name, Kind, Color = "#0A84FF";
        /// <summary>Программа или консольная утилита, через которую управляем.</summary>
        public string Exe, App;
        /// <summary>Можно включать и выключать прямо из острова (есть официальная командная строка).</summary>
        public bool CanControl;
        public bool NeedsAdmin;
        public string Note;
        public VpnState State = VpnState.Unknown;
        public string Detail = "";
        internal Func<VpnItem, Task<(VpnState, string)>> Status;
        internal Func<VpnItem, bool, Task<string>> Switch;
        internal Regex Adapter;
    }

    /// <summary>
    /// VPN прямо в острове: находит установленные программы (Cloudflare WARP, NordVPN, Mullvad, Windscribe, Tailscale, ProtonVPN,
    /// Surfshark, ExpressVPN, AdGuard VPN, Amnezia, OpenVPN, WireGuard…) и VPN-подключения Windows. Включает и выключает их
    /// только через официальные команды самих программ (warp-cli, mullvad, nordvpn -c, rasdial…); если команды нет — показывает
    /// состояние по сетевому адаптеру и открывает приложение. Следит за подключением и сообщает IP и страну.
    /// </summary>
    public static class Vpn
    {
        static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(6) };
        static readonly string PF = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        static readonly string PF86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        static readonly string LocalApps = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        public static List<VpnItem> Items { get; private set; } = new();
        public static (string Ip, string Country, string City, string Flag, string Isp) PublicIp { get; private set; }
        public static DateTime DetectedAt { get; private set; }
        /// <summary>Сейчас включён какой-либо VPN (по сетевым адаптерам).</summary>
        public static bool Active { get; private set; }
        public static string ActiveName { get; private set; } = "";
        /// <summary>Состояние сменилось: (включён, название).</summary>
        public static event Action<bool, string> ActiveChanged;
        public static event Action Changed;

        static bool _watching;

        static readonly Regex VpnAdapter = new(
            @"WireGuard|Wintun|TAP-Windows|TAP-|OpenVPN|NordLynx|NordVPN|Mullvad|Windscribe|Cloudflare|WARP|Tailscale|Proton|Surfshark|ExpressVPN|Lightway|" +
            @"Amnezia|Hotspot Shield|AdGuard|Outline|Psiphon|PANGP|AnyConnect|Fortinet|SonicWall|Kaspersky|Radmin|ZeroTier|Hamachi|Hide\.me|IVPN|PrivateVPN|PIA|Private Internet|Planet",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // ------------------------------------------------------------------
        //  Поиск установленного
        // ------------------------------------------------------------------

        /// <summary>Описание поддерживаемой программы: где лежит, как называется в «Программах», как управлять.</summary>
        record Known(string Key, string Name, string Color, string[] Paths, string Uninstall, string AdapterRx, string Cli = null);

        static readonly Known[] Apps =
        {
            new("warp", "Cloudflare WARP", "#F48120", new[] { @"%PF%\Cloudflare\Cloudflare WARP\warp-cli.exe" }, "Cloudflare WARP", @"Cloudflare|WARP", "warp"),
            new("nord", "NordVPN", "#4687FF", new[] { @"%PF%\NordVPN\nordvpn.exe", @"%PF86%\NordVPN\nordvpn.exe" }, "NordVPN", @"NordLynx|NordVPN", "nord"),
            new("mullvad", "Mullvad VPN", "#294D73", new[] { @"%PF%\Mullvad VPN\resources\mullvad.exe" }, "Mullvad VPN", @"Mullvad|Wintun", "mullvad"),
            new("windscribe", "Windscribe", "#00D27A", new[] { @"%PF%\Windscribe\windscribe-cli.exe", @"%PF86%\Windscribe\windscribe-cli.exe" }, "Windscribe", @"Windscribe", "windscribe"),
            new("tailscale", "Tailscale", "#5A5A5A", new[] { @"%PF%\Tailscale\tailscale.exe" }, "Tailscale", @"Tailscale", "tailscale"),
            new("proton", "Proton VPN", "#6D4AFF", new[] { @"%PF%\Proton\VPN\ProtonVPN.Launcher.exe", @"%PF%\Proton Technologies\ProtonVPN\ProtonVPN.exe", @"%PF86%\Proton Technologies\ProtonVPN\ProtonVPN.exe" }, "Proton VPN", @"Proton"),
            new("surfshark", "Surfshark", "#1EBFBF", new[] { @"%PF%\Surfshark\Surfshark.exe", @"%PF86%\Surfshark\Surfshark.exe" }, "Surfshark", @"Surfshark"),
            new("express", "ExpressVPN", "#DA3940", new[] { @"%PF86%\ExpressVPN\expressvpn-ui\ExpressVPN.exe", @"%PF%\ExpressVPN\expressvpn-ui\ExpressVPN.exe" }, "ExpressVPN", @"ExpressVPN|Lightway"),
            new("adguard", "AdGuard VPN", "#68BC71", new[] { @"%PF%\AdGuardVpn\AdGuardVpn.exe", @"%PF86%\AdGuardVpn\AdGuardVpn.exe" }, "AdGuard VPN", @"AdGuard"),
            new("amnezia", "AmneziaVPN", "#FBB26A", new[] { @"%PF%\AmneziaVPN\AmneziaVPN.exe" }, "AmneziaVPN", @"Amnezia"),
            new("hotspot", "Hotspot Shield", "#3E9BEA", new[] { @"%PF86%\Hotspot Shield\bin\hsscp.exe", @"%PF%\Hotspot Shield\bin\hsscp.exe" }, "Hotspot Shield", @"Hotspot"),
            new("pia", "Private Internet Access", "#4BB749", new[] { @"%PF%\Private Internet Access\piactl.exe" }, "Private Internet Access", @"Private Internet|PIA", "pia"),
            new("kaspersky", "Kaspersky VPN", "#00A88E", new[] { @"%PF86%\Kaspersky Lab\Kaspersky VPN Secure Connection 5.20\ksde.exe" }, "Kaspersky VPN", @"Kaspersky"),
            new("outline", "Outline", "#1D2B33", new[] { @"%PF86%\Outline\Outline.exe", @"%LA%\Programs\outline-client\Outline.exe" }, "Outline", @"Outline"),
            new("psiphon", "Psiphon", "#0072BC", new[] { @"%LA%\Psiphon3\psiphon3.exe" }, "Psiphon", @"Psiphon"),
            new("zerotier", "ZeroTier", "#FFB441", new[] { @"%PF86%\ZeroTier\One\zerotier_desktop_ui.exe", @"%PF%\ZeroTier\One\zerotier_desktop_ui.exe" }, "ZeroTier", @"ZeroTier"),
            new("radmin", "Radmin VPN", "#2C7BE5", new[] { @"%PF86%\Radmin VPN\RvRvpnGui.exe" }, "Radmin VPN", @"Radmin"),
        };

        static string Expand(string p) => p.Replace("%PF%", PF).Replace("%PF86%", PF86).Replace("%LA%", LocalApps);

        /// <summary>Пути из «Программ и компонентов» — если программа стоит не в стандартной папке.</summary>
        static Dictionary<string, string> Installed()
        {
            var res = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (hive, view) in new[] { (RegistryHive.LocalMachine, RegistryView.Registry64), (RegistryHive.LocalMachine, RegistryView.Registry32), (RegistryHive.CurrentUser, RegistryView.Default) })
            {
                try
                {
                    using var root = RegistryKey.OpenBaseKey(hive, view).OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall");
                    if (root == null) continue;
                    foreach (var name in root.GetSubKeyNames())
                    {
                        using var k = root.OpenSubKey(name);
                        if (k?.GetValue("DisplayName") is not string dn) continue;
                        var loc = k.GetValue("InstallLocation") as string ?? "";
                        if (loc == "" && k.GetValue("DisplayIcon") is string icon) loc = Path.GetDirectoryName(icon.Split(',')[0].Trim('"')) ?? "";
                        res[dn] = loc;
                    }
                }
                catch { }
            }
            return res;
        }

        /// <summary>Найти всё, чем можно управлять. Небыстро (реестр, файлы, консольные утилиты) — вызывать в фоне.</summary>
        public static async Task<List<VpnItem>> Detect()
        {
            var list = await Task.Run(() =>
            {
                var items = new List<VpnItem>();
                var installed = Installed();
                foreach (var k in Apps)
                {
                    string exe = k.Paths.Select(Expand).FirstOrDefault(File.Exists);
                    if (exe == null)
                    {
                        // нестандартная папка: ищем запись в «Программах»
                        var hit = installed.FirstOrDefault(x => x.Key.StartsWith(k.Uninstall, StringComparison.OrdinalIgnoreCase));
                        if (hit.Key != null && hit.Value != "" && Directory.Exists(hit.Value))
                        {
                            var names = k.Paths.Select(p => Path.GetFileName(p)).Distinct();
                            exe = names.Select(n => Directory.EnumerateFiles(hit.Value, n, SearchOption.AllDirectories).FirstOrDefault()).FirstOrDefault(f => f != null);
                        }
                        if (exe == null) continue;
                    }
                    var item = new VpnItem { Key = k.Key, Name = k.Name, Color = k.Color, Kind = "app", Exe = exe, App = AppExe(k.Key, exe), Adapter = new Regex(k.AdapterRx, RegexOptions.IgnoreCase) };
                    ConfigureCli(item, k.Cli);
                    items.Add(item);
                }
                items.AddRange(OpenVpnConfigs());
                items.AddRange(WireGuardTunnels());
                items.AddRange(WindowsVpn());
                return items;
            });
            Items = list;
            DetectedAt = DateTime.Now;
            await RefreshAll();
            return list;
        }

        /// <summary>Графическое приложение рядом с консольной утилитой — для кнопки «Открыть».</summary>
        static string AppExe(string key, string cli)
        {
            var dir = Path.GetDirectoryName(cli) ?? "";
            string[] guess = key switch
            {
                "warp" => new[] { "Cloudflare WARP.exe" },
                "nord" => new[] { "NordVPN.exe" },
                "mullvad" => new[] { @"..\Mullvad VPN.exe" },
                "windscribe" => new[] { "Windscribe.exe" },
                "tailscale" => new[] { "tailscale-ipn.exe" },
                "pia" => new[] { "pia-client.exe" },
                _ => Array.Empty<string>(),
            };
            foreach (var g in guess)
            {
                var p = Path.GetFullPath(Path.Combine(dir, g));
                if (File.Exists(p)) return p;
            }
            return cli;
        }

        // ------------------------------------------------------------------
        //  Управление через официальные командные строки
        // ------------------------------------------------------------------

        static void ConfigureCli(VpnItem it, string cli)
        {
            switch (cli)
            {
                case "warp":
                    it.CanControl = true;
                    it.Status = async i =>
                    {
                        var (code, outp) = await Run(i.Exe, "--accept-tos status");
                        if (code != 0 && outp == "") return (VpnState.Unknown, "служба WARP не отвечает");
                        if (Regex.IsMatch(outp, @"Status update:\s*Connected|^Connected", RegexOptions.IgnoreCase | RegexOptions.Multiline)) return (VpnState.On, "подключён");
                        if (Regex.IsMatch(outp, @"Connecting", RegexOptions.IgnoreCase)) return (VpnState.Connecting, "подключается…");
                        if (Regex.IsMatch(outp, @"Registration Missing", RegexOptions.IgnoreCase)) return (VpnState.Off, "сначала войди в приложении WARP");
                        return (VpnState.Off, "выключен");
                    };
                    it.Switch = async (i, on) => Err(await Run(i.Exe, "--accept-tos " + (on ? "connect" : "disconnect")));
                    break;
                case "nord":
                    // у NordVPN под Windows есть только «подключить» и «отключить», состояние берём по адаптеру NordLynx/TAP
                    it.CanControl = true;
                    it.Status = i => Task.FromResult(ByAdapter(i));
                    it.Switch = async (i, on) => { Start(i.Exe, on ? "-c" : "-d"); await Task.Delay(on ? 6000 : 2500); return null; };
                    break;
                case "mullvad":
                    it.CanControl = true;
                    it.Status = async i =>
                    {
                        var (code, outp) = await Run(i.Exe, "status");
                        if (code != 0 && outp == "") return (VpnState.Unknown, "служба Mullvad не отвечает");
                        var m = Regex.Match(outp, @"Connected(?: to)?\s*(.*)", RegexOptions.IgnoreCase);
                        if (m.Success && !outp.StartsWith("Disconnected", StringComparison.OrdinalIgnoreCase)) return (VpnState.On, Short(m.Groups[1].Value) is { Length: > 0 } s ? s : "подключён");
                        if (outp.Contains("Connecting", StringComparison.OrdinalIgnoreCase)) return (VpnState.Connecting, "подключается…");
                        return (VpnState.Off, "выключен");
                    };
                    it.Switch = async (i, on) => Err(await Run(i.Exe, on ? "connect" : "disconnect"));
                    break;
                case "windscribe":
                    it.CanControl = true;
                    it.Status = async i =>
                    {
                        var (code, outp) = await Run(i.Exe, "status");
                        if (code != 0 && outp == "") return ByAdapter(i);
                        if (Regex.IsMatch(outp, @"Connect state:\s*Connected", RegexOptions.IgnoreCase)) return (VpnState.On, "подключён");
                        if (Regex.IsMatch(outp, @"Connecting", RegexOptions.IgnoreCase)) return (VpnState.Connecting, "подключается…");
                        if (Regex.IsMatch(outp, @"not logged|login", RegexOptions.IgnoreCase)) return (VpnState.Off, "сначала войди в приложении");
                        return (VpnState.Off, "выключен");
                    };
                    it.Switch = async (i, on) => Err(await Run(i.Exe, on ? "connect" : "disconnect"));
                    break;
                case "tailscale":
                    it.CanControl = true;
                    it.Status = async i =>
                    {
                        var (code, outp) = await Run(i.Exe, "status --json");
                        var m = Regex.Match(outp, "\"BackendState\"\\s*:\\s*\"(\\w+)\"");
                        if (!m.Success) return ByAdapter(i);
                        return m.Groups[1].Value switch
                        {
                            "Running" => (VpnState.On, "в сети"),
                            "Starting" => (VpnState.Connecting, "подключается…"),
                            "NeedsLogin" => (VpnState.Off, "нужно войти в аккаунт"),
                            _ => (VpnState.Off, "выключен"),
                        };
                    };
                    it.Switch = async (i, on) => Err(await Run(i.Exe, on ? "up" : "down"));
                    break;
                case "pia":
                    it.CanControl = true;
                    it.Status = async i =>
                    {
                        var (_, outp) = await Run(i.Exe, "get connectionstate");
                        return outp.Trim() switch
                        {
                            "Connected" => (VpnState.On, "подключён"),
                            var s when s.StartsWith("Connecting") || s.StartsWith("Reconnecting") => (VpnState.Connecting, "подключается…"),
                            "" => ByAdapter(i),
                            _ => (VpnState.Off, "выключен"),
                        };
                    };
                    it.Switch = async (i, on) => Err(await Run(i.Exe, on ? "connect" : "disconnect"));
                    break;
                default:
                    it.CanControl = false;
                    it.Note = "Управление — в самом приложении, остров показывает состояние";
                    it.Status = i => Task.FromResult(ByAdapter(i));
                    break;
            }
        }

        /// <summary>OpenVPN GUI: каждый конфиг — отдельная строка, команды «--command connect/disconnect».</summary>
        static IEnumerable<VpnItem> OpenVpnConfigs()
        {
            var gui = new[] { Path.Combine(PF, @"OpenVPN\bin\openvpn-gui.exe"), Path.Combine(PF86, @"OpenVPN\bin\openvpn-gui.exe") }.FirstOrDefault(File.Exists);
            if (gui == null) yield break;
            var dirs = new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @"OpenVPN\config"), Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(gui)), "config") };
            var configs = dirs.Where(Directory.Exists).SelectMany(d => SafeFiles(d, "*.ovpn")).Select(Path.GetFileNameWithoutExtension).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var c in configs)
            {
                var name = c;
                yield return new VpnItem
                {
                    Key = "ovpn:" + name, Name = "OpenVPN · " + name, Color = "#EA7E20", Kind = "openvpn", Exe = gui, App = gui, CanControl = true,
                    Adapter = new Regex(@"TAP-Windows|OpenVPN|Wintun", RegexOptions.IgnoreCase),
                    Status = i => Task.FromResult(OpenVpnState(name, i)),
                    Switch = async (i, on) => { Start(gui, $"--command {(on ? "connect" : "disconnect")} \"{name}\""); await Task.Delay(on ? 5000 : 2000); return null; },
                };
            }
            if (configs.Count == 0)
                yield return new VpnItem { Key = "ovpn", Name = "OpenVPN", Color = "#EA7E20", Kind = "app", Exe = gui, App = gui, Note = "Нет конфигов (.ovpn) в папке OpenVPN\\config", Status = i => Task.FromResult(ByAdapter(i)), Adapter = new Regex(@"TAP-Windows|OpenVPN", RegexOptions.IgnoreCase) };
        }

        static IEnumerable<string> SafeFiles(string dir, string mask)
        {
            try { return Directory.EnumerateFiles(dir, mask, SearchOption.AllDirectories).ToList(); } catch { return Array.Empty<string>(); }
        }

        static (VpnState, string) OpenVpnState(string config, VpnItem i)
        {
            var (st, d) = ByAdapter(i);
            return (st, st == VpnState.On ? "туннель OpenVPN поднят" : d);
        }

        /// <summary>WireGuard: установленные туннели — это службы «WireGuardTunnel$имя». Запуск службы требует прав администратора.</summary>
        static IEnumerable<VpnItem> WireGuardTunnels()
        {
            var wg = Path.Combine(PF, @"WireGuard\wireguard.exe");
            if (!File.Exists(wg)) yield break;
            var names = new List<string>();
            try
            {
                using var svc = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
                names = svc?.GetSubKeyNames().Where(n => n.StartsWith("WireGuardTunnel$", StringComparison.OrdinalIgnoreCase)).ToList() ?? new();
            }
            catch { }
            foreach (var n in names)
            {
                var service = n;
                var tunnel = n["WireGuardTunnel$".Length..];
                yield return new VpnItem
                {
                    Key = "wg:" + tunnel, Name = "WireGuard · " + tunnel, Color = "#88171A", Kind = "wireguard", Exe = wg, App = wg, CanControl = true, NeedsAdmin = true,
                    Note = "Включение и выключение туннеля спросит права администратора",
                    Status = async i =>
                    {
                        var (_, outp) = await Run("sc.exe", $"query \"{service}\"");
                        if (outp.Contains("RUNNING")) return (VpnState.On, "туннель включён");
                        if (outp.Contains("START_PENDING")) return (VpnState.Connecting, "включается…");
                        return (VpnState.Off, "выключен");
                    },
                    Switch = async (i, on) =>
                    {
                        try
                        {
                            using var p = Process.Start(new ProcessStartInfo("sc.exe", $"{(on ? "start" : "stop")} \"{service}\"") { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden });
                            await Task.Run(() => p?.WaitForExit(15000));
                            return null;
                        }
                        catch (System.ComponentModel.Win32Exception) { return "Нужны права администратора — запрос отклонён"; }
                    },
                };
            }
            if (names.Count == 0)
                yield return new VpnItem { Key = "wg", Name = "WireGuard", Color = "#88171A", Kind = "app", Exe = wg, App = wg, Note = "Нет активированных туннелей — один раз включи туннель в приложении WireGuard", Status = i => Task.FromResult(ByAdapter(i)), Adapter = new Regex("WireGuard", RegexOptions.IgnoreCase) };
        }

        /// <summary>Встроенные VPN-подключения Windows (Параметры → Сеть → VPN): rasdial «имя» / rasdial «имя» /disconnect.</summary>
        static IEnumerable<VpnItem> WindowsVpn()
        {
            var books = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Network\Connections\Pbk\rasphone.pbk"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Microsoft\Network\Connections\Pbk\rasphone.pbk"),
            };
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var book in books.Where(File.Exists))
            {
                string[] lines;
                try { lines = File.ReadAllLines(book); } catch { continue; }
                string section = null;
                foreach (var l in lines)
                {
                    var m = Regex.Match(l, @"^\[(.+)\]\s*$");
                    if (m.Success) { section = m.Groups[1].Value; continue; }
                    // Type=2 — VPN (1 — модем, 4/5 — прямое и широкополосное подключение)
                    if (section != null && Regex.IsMatch(l, @"^Type=2\s*$") && seen.Add(section))
                    {
                        var name = section;
                        yield return new VpnItem
                        {
                            Key = "ras:" + name, Name = name, Color = "#0078D4", Kind = "windows", Exe = "rasdial.exe", App = "ms-settings:network-vpn", CanControl = true,
                            Note = "VPN Windows · логин и пароль должны быть сохранены в подключении",
                            Status = async i =>
                            {
                                var (_, outp) = await Run("rasdial.exe", "");
                                return outp.Split('\n').Any(x => string.Equals(x.Trim(), name, StringComparison.OrdinalIgnoreCase)) ? (VpnState.On, "подключено") : (VpnState.Off, "отключено");
                            },
                            Switch = async (i, on) =>
                            {
                                var (code, outp) = await Run("rasdial.exe", on ? $"\"{name}\"" : $"\"{name}\" /disconnect", 40000);
                                if (code == 0) return null;
                                if (code is 691 or 703 or 718) return "Windows не знает логин и пароль — подключись один раз в «Параметры → VPN» с галочкой «Запомнить»";
                                return Short(outp) is { Length: > 0 } e ? e : "Ошибка " + code;
                            },
                        };
                    }
                }
            }
        }

        // ------------------------------------------------------------------
        //  Состояние
        // ------------------------------------------------------------------

        static List<(string Name, string Desc, NetworkInterfaceType Type)> UpAdapters()
        {
            try
            {
                return NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .Select(n => (n.Name, n.Description, n.NetworkInterfaceType)).ToList();
            }
            catch { return new(); }
        }

        static (VpnState, string) ByAdapter(VpnItem i)
        {
            if (i.Adapter == null) return (VpnState.Unknown, "");
            var up = UpAdapters().FirstOrDefault(a => i.Adapter.IsMatch(a.Desc) || i.Adapter.IsMatch(a.Name));
            return up.Name != null ? (VpnState.On, "подключён · " + up.Name) : (VpnState.Off, "выключен");
        }

        /// <summary>Какой-то VPN поднят — по сетевым адаптерам (работает для любых программ, даже неизвестных).</summary>
        public static (bool On, string Name) AnyActive()
        {
            var a = UpAdapters().FirstOrDefault(x => x.Type == NetworkInterfaceType.Ppp || VpnAdapter.IsMatch(x.Desc) || VpnAdapter.IsMatch(x.Name));
            if (a.Name == null) return (false, "");
            var known = Items.FirstOrDefault(i => i.Adapter != null && (i.Adapter.IsMatch(a.Desc) || i.Adapter.IsMatch(a.Name)));
            return (true, known?.Name ?? (a.Type == NetworkInterfaceType.Ppp ? "VPN Windows" : a.Desc));
        }

        public static async Task Refresh(VpnItem i)
        {
            if (i.Status == null) return;
            try { (i.State, i.Detail) = await i.Status(i); }
            catch (Exception e) { i.State = VpnState.Unknown; i.Detail = e.Message; }
        }

        public static async Task RefreshAll()
        {
            await Task.WhenAll(Items.Select(Refresh));
            UpdateActive();
            Changed?.Invoke();
        }

        /// <summary>Включить или выключить. Возвращает текст ошибки или null.</summary>
        public static async Task<string> Set(VpnItem i, bool on)
        {
            if (i.Switch == null) return "Эта программа не даёт управлять собой со стороны — открой её";
            i.State = VpnState.Connecting;
            i.Detail = on ? "подключается…" : "отключается…";
            Changed?.Invoke();
            string err;
            try { err = await i.Switch(i, on); }
            catch (Exception e) { err = e.Message; }
            // состояние обновляется не сразу — несколько раз переспрашиваем
            for (int k = 0; k < 6; k++)
            {
                await Refresh(i);
                if (i.State == (on ? VpnState.On : VpnState.Off)) break;
                await Task.Delay(1500);
            }
            UpdateActive();
            Changed?.Invoke();
            return err;
        }

        public static void Open(VpnItem i)
        {
            try { SystemService.Run(i.App ?? i.Exe); } catch (Exception e) { Log.Info("vpn open: " + e.Message); }
        }

        static void UpdateActive()
        {
            var (on, name) = AnyActive();
            if (on == Active && name == ActiveName) return;
            bool changed = on != Active;
            Active = on;
            ActiveName = name;
            if (changed) { ActiveChanged?.Invoke(on, name); _ = LoadIp(); }
        }

        /// <summary>Следить за сетью: VPN включили или выключили (в любой программе) — сообщаем и обновляем IP.</summary>
        public static void Watch()
        {
            if (_watching) return;
            _watching = true;
            (Active, ActiveName) = AnyActive();
            NetworkChange.NetworkAddressChanged += (_, __) =>
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(async () =>
                {
                    await Task.Delay(1500);   // адаптер поднимается не мгновенно
                    UpdateActive();
                    Changed?.Invoke();
                }));
        }

        /// <summary>Внешний IP и страна — видно, сработал ли VPN.</summary>
        public static async Task LoadIp()
        {
            try
            {
                using var doc = JsonDocument.Parse(await Http.GetStringAsync("https://ipwho.is/"));
                var r = doc.RootElement;
                string S(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : "";
                PublicIp = (S(r, "ip"), S(r, "country"), S(r, "city"), r.TryGetProperty("flag", out var f) ? S(f, "emoji") : "",
                            r.TryGetProperty("connection", out var c) ? S(c, "isp") : "");
            }
            catch (Exception e) { Log.Info("vpn ip: " + e.Message); PublicIp = default; }
            Changed?.Invoke();
        }

        // ------------------------------------------------------------------
        //  Запуск команд
        // ------------------------------------------------------------------

        static async Task<(int Code, string Output)> Run(string exe, string args, int timeoutMs = 15000)
        {
            return await Task.Run(() =>
            {
                try
                {
                    using var p = new Process
                    {
                        StartInfo = new ProcessStartInfo(exe, args)
                        {
                            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
                        },
                    };
                    var sb = new StringBuilder();
                    p.OutputDataReceived += (_, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
                    p.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
                    p.Start();
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    if (!p.WaitForExit(timeoutMs)) { try { p.Kill(true); } catch { } return (-1, "нет ответа"); }
                    p.WaitForExit();
                    lock (sb) return (p.ExitCode, sb.ToString().Trim());
                }
                catch (Exception e) { return (-1, e.Message); }
            });
        }

        static void Start(string exe, string args)
        {
            try { using var _ = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true }); }
            catch (Exception e) { Log.Info("vpn start: " + e.Message); }
        }

        static string Err((int Code, string Output) r) => r.Code == 0 ? null : Short(r.Output) is { Length: > 0 } s ? s : "Ошибка " + r.Code;

        static string Short(string s)
        {
            s = Regex.Replace(s ?? "", @"\s+", " ").Trim();
            return s.Length > 120 ? s[..120] + "…" : s;
        }
    }
}
