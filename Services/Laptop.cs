using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using NIsland.Core;

namespace NIsland.Services
{
    public record HealthTip(string Text, string Section, Safety Level);
    public record Health(int Score, List<HealthTip> Tips);
    public record NetUser(string Process, int Pid, int Connections, string Sample);
    public record Fix(string Key, string Title, string Explain, Safety Safety, bool Admin, bool Console, string[] Commands, Action Local = null);
    public record DnsPreset(string Name, string Primary, string Secondary, string Explain);
    public record PowerModeInfo(string Key, string Name, Guid Id, string Explain);
    public record StartupAdvice(string Verdict, string Why, Safety Level);

    /// <summary>Собственные функции «Центра управления ноутбуком» — то, чего нет в стандартных настройках Windows.</summary>
    public static class Laptop
    {
        // ------------------------------------------------------------------
        //  Индекс здоровья ноутбука
        // ------------------------------------------------------------------

        /// <summary>Оценка 0–100 и понятные советы, что улучшить.</summary>
        public static async Task<Health> CheckHealth()
        {
            var tips = new List<HealthTip>();
            int score = 100;
            var root = Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\');
            var sys = Maint.Disks().FirstOrDefault(d => string.Equals(d.Name, root, StringComparison.OrdinalIgnoreCase));
            if (sys != null && sys.Total > 0)
            {
                double free = 100.0 * sys.Free / sys.Total;
                if (free < 10) { score -= 25; tips.Add(new($"На диске {sys.Name} свободно всего {free:0}% — Windows начинает тормозить. Сделай очистку.", "clean", Safety.NotRecommended)); }
                else if (free < 20) { score -= 10; tips.Add(new($"На диске {sys.Name} свободно {free:0}%. Лучше держать больше 20%.", "files", Safety.Caution)); }
            }
            int ram = SystemService.I.RamPercent();
            if (ram > 85) { score -= 10; tips.Add(new($"Память занята на {ram}%. Закрой лишние программы или нажми «Освободить память».", "perf", Safety.Caution)); }
            var bat = await Maint.Battery();
            if (bat != null && bat.Design > 0)
            {
                if (bat.Percent < 60) { score -= 15; tips.Add(new($"Батарея держит {bat.Percent}% от новой — пора подумать о замене.", "power", Safety.NotRecommended)); }
                else if (bat.Percent < 80) { score -= 5; tips.Add(new($"Батарея держит {bat.Percent}% от новой. Включи сигнал при 80% заряда — это замедлит износ.", "power", Safety.Caution)); }
            }
            int startup = Maint.Startup().Count(s => s.Enabled);
            if (startup > 12) { score -= 10; tips.Add(new($"При включении запускается {startup} программ. Отключи лишние — ноутбук будет включаться быстрее.", "apps", Safety.Caution)); }
            else if (startup > 7) { score -= 4; tips.Add(new($"В автозагрузке {startup} программ — посмотри советы, что можно отключить.", "apps", Safety.Safe)); }
            if (Maint.Uptime.TotalDays >= Store.Settings.RebootDays) { score -= 5; tips.Add(new($"Ноутбук не перезагружался {Maint.Uptime.TotalDays:0} дн. Перезагрузка очищает память и ставит обновления.", "overview", Safety.Caution)); }
            var (on, total) = Privacy.Score();
            if (on < total / 2) { score -= 10; tips.Add(new($"Защита от слежки: включено {on} из {total}. Включи хотя бы безопасные пункты.", "privacy", Safety.Caution)); }
            var temp = await Maint.ScanTemp();
            if (temp.Bytes > 1L << 30) { score -= 10; tips.Add(new($"Мусора во временных файлах: {Maint.Size(temp.Bytes)}.", "clean", Safety.Caution)); }
            else if (temp.Bytes > 300L << 20) { score -= 4; tips.Add(new($"Временных файлов накопилось {Maint.Size(temp.Bytes)}.", "clean", Safety.Safe)); }
            if (tips.Count == 0) tips.Add(new("Всё отлично — ноутбук в хорошей форме ✓", null, Safety.Safe));
            return new Health(Math.Clamp(score, 0, 100), tips);
        }

        // ------------------------------------------------------------------
        //  Паспорт ноутбука
        // ------------------------------------------------------------------

        public static Task<List<(string Name, string Value)>> Passport() => Task.Run(() =>
        {
            var res = new List<(string, string)>();
            string Wmi(string cls, string props, Func<ManagementObject, string> f)
            {
                try
                {
                    using var s = new ManagementObjectSearcher($"SELECT {props} FROM {cls}");
                    return string.Join(" · ", s.Get().Cast<ManagementObject>().Select(f).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct());
                }
                catch { return ""; }
            }
            res.Add(("Модель", Wmi("Win32_ComputerSystem", "Manufacturer, Model", o => $"{o["Manufacturer"]} {o["Model"]}".Trim())));
            res.Add(("Серийный номер", Wmi("Win32_BIOS", "SerialNumber", o => o["SerialNumber"]?.ToString()?.Trim())));
            res.Add(("Процессор", Wmi("Win32_Processor", "Name, NumberOfCores, NumberOfLogicalProcessors",
                o => $"{o["Name"]?.ToString()?.Trim()} ({o["NumberOfCores"]} ядер / {o["NumberOfLogicalProcessors"]} потоков)")));
            res.Add(("Видеокарта", Wmi("Win32_VideoController", "Name", o => o["Name"]?.ToString())));
            var speed = Wmi("Win32_PhysicalMemory", "Speed", o => o["Speed"] is null ? null : $"{o["Speed"]} МГц");
            res.Add(("Память", speed.Length > 0 ? $"{Maint.Size(Maint.TotalRam())} · {speed}" : Maint.Size(Maint.TotalRam())));
            res.Add(("Диски", string.Join(" · ", Maint.Disks().Select(d => $"{d.Name} {Maint.Size(d.Total)}"))));
            res.Add(("Windows", Maint.WindowsVersion));
            try
            {
                using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
                if (k?.GetValue("InstallDate") is int unix)
                    res.Add(("Windows установлена", DateTimeOffset.FromUnixTimeSeconds((uint)unix).LocalDateTime.ToString("d MMMM yyyy", Fmt.Ru)));
            }
            catch { }
            res.Add(("BIOS", Wmi("Win32_BIOS", "SMBIOSBIOSVersion", o => o["SMBIOSBIOSVersion"]?.ToString())));
            var scr = System.Windows.Forms.Screen.PrimaryScreen;
            if (scr != null) res.Add(("Экран", $"{scr.Bounds.Width}×{scr.Bounds.Height}"));
            res.Add(("Работает без перезагрузки", $"{(int)Maint.Uptime.TotalDays} дн. {Maint.Uptime.Hours} ч"));
            return res.Where(r => !string.IsNullOrWhiteSpace(r.Item2)).ToList();
        });

        // ------------------------------------------------------------------
        //  Режим питания Windows 11 (ползунок «Эффективность — Производительность»)
        // ------------------------------------------------------------------

        [DllImport("powrprof.dll")] static extern uint PowerGetEffectiveOverlayScheme(out Guid g);
        [DllImport("powrprof.dll")] static extern uint PowerSetActiveOverlayScheme(Guid g);
        [DllImport("powrprof.dll")] static extern bool SetSuspendState(bool hibernate, bool force, bool wakeupDisabled);
        [DllImport("user32.dll")] static extern bool LockWorkStation();

        public static readonly PowerModeInfo[] PowerModes =
        {
            new("saver", "Экономия", new Guid("961cc777-2547-4f9d-8174-7d86181b8a7a"), "Дольше работает от батареи, чуть медленнее и тише"),
            new("balanced", "Баланс", Guid.Empty, "Как Windows делает по умолчанию"),
            new("perf", "Производительность", new Guid("ded574b5-45a0-4f42-8737-46345c09c238"), "Быстрее всего, но греется и быстрее разряжается"),
        };

        public static string PowerMode
        {
            get
            {
                try { return PowerGetEffectiveOverlayScheme(out var g) == 0 ? PowerModes.FirstOrDefault(m => m.Id == g)?.Key ?? "balanced" : null; }
                catch { return null; }
            }
            set
            {
                var m = PowerModes.FirstOrDefault(x => x.Key == value);
                if (m == null) return;
                try { PowerSetActiveOverlayScheme(m.Id); } catch { }
            }
        }

        /// <summary>Экономия заряда Windows: порог включения (100 — включена всегда на батарее, 20 — как по умолчанию).</summary>
        public static void SetSaverThreshold(int pct)
        {
            Maint.RunCmd("powercfg", $"/setdcvalueindex SCHEME_CURRENT SUB_ENERGYSAVER ESBATTTHRESHOLD {pct}");
            Maint.RunCmd("powercfg", "/setactive SCHEME_CURRENT");
        }

        // ------------------------------------------------------------------
        //  Сценарии: несколько настроек одним нажатием
        // ------------------------------------------------------------------

        public static readonly (string Key, string Title, string Glyph, string Explain)[] Scenarios =
        {
            ("work", "Работа", "", "Баланс питания, режим «Не беспокоить» в чёлке, экран гаснет как обычно"),
            ("movie", "Кино", "", "Экран не гаснет, яркость 100%, уведомления не мешают"),
            ("game", "Игры", "", "Максимальная производительность, игровой режим Windows, чёлка спрятана"),
            ("saver", "Экономия", "", "Экономия заряда, яркость 40%, ничего лишнего — максимум времени от батареи"),
            ("present", "Презентация", "", "Значки рабочего стола спрятаны, экран не гаснет, ничего не всплывает"),
            ("normal", "Обычный", "", "Вернуть всё как было по умолчанию"),
        };

        public static void ApplyScenario(string key)
        {
            var sys = SystemService.I;
            var island = App.Island;
            bool icons = true, awake = false;
            string mode = "normal", power = "balanced";
            int saver = 20;
            int? bright = null;
            switch (key)
            {
                case "work": mode = "dnd"; break;
                case "movie": mode = "dnd"; awake = true; bright = 100; break;
                case "game": mode = "game"; awake = true; power = "perf"; Tweaks.Performance[0].Set(true); break;
                case "saver": power = "saver"; saver = 100; bright = 40; break;
                case "present": mode = "present"; awake = true; icons = false; break;
            }
            PowerMode = power;
            SetSaverThreshold(saver);
            sys.KeepAwake = awake;
            if (bright is int b) sys.SetBrightness(b);
            if (DesktopIconsVisible != icons) ToggleDesktopIcons();
            island?.SetMode(mode);
            Store.Settings.Scenario = key == "normal" ? "" : key;
        }

        // ------------------------------------------------------------------
        //  Значки рабочего стола
        // ------------------------------------------------------------------

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);

        public static bool DesktopIconsVisible
        {
            get
            {
                try { using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced"); return !(k?.GetValue("HideIcons") is int i && i == 1); }
                catch { return true; }
            }
        }

        /// <summary>То же, что «Вид → Отображать значки рабочего стола» в контекстном меню рабочего стола.</summary>
        public static void ToggleDesktopIcons()
        {
            var def = FindWindowEx(FindWindow("Progman", null), IntPtr.Zero, "SHELLDLL_DefView", null);
            for (IntPtr w = IntPtr.Zero; def == IntPtr.Zero && (w = FindWindowEx(IntPtr.Zero, w, "WorkerW", null)) != IntPtr.Zero;)
                def = FindWindowEx(w, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (def != IntPtr.Zero) SendMessage(def, 0x0111, new IntPtr(0x7402), IntPtr.Zero);
        }

        // ------------------------------------------------------------------
        //  Советник автозагрузки
        // ------------------------------------------------------------------

        static readonly (string[] Match, bool Keep, string Why)[] StartupKnowledge =
        {
            (new[] { "securityhealth", "windowsdefender", "kaspersky", "avp", "eset", "egui", "drweb", "avast", "avg", "bitdefender", "norton", "mcafee", "360" }, true, "Антивирус — оставь включённым."),
            (new[] { "nisland", "n-island" }, true, "Сам N-Island."),
            (new[] { "rtkaud", "realtek", "rthdvcpl", "waves", "maxxaudio", "dolby", "nahimic" }, true, "Звуковой драйвер — без него может пропасть звук или эффекты."),
            (new[] { "synaptics", "syntp", "elan", "etdctrl", "touchpad", "precision" }, true, "Тачпад — без него могут не работать жесты."),
            (new[] { "igfx", "intel", "amd", "radeon", "nvidia", "nvbackend", "nvcontainer" }, true, "Драйвер видеокарты — лучше оставить."),
            (new[] { "lenovo", "vantage", "asus", "armoury", "dell", "msi", "acer", "huawei", "hotkey", "fnkey", "smartappearance" }, true, "Утилита производителя ноутбука: горячие клавиши Fn, режимы батареи, подсветка."),
            (new[] { "ctfmon", "bluetooth", "btsendto", "onscreen", "punto", "keyboard" }, true, "Нужна для клавиатуры, языков или Bluetooth."),
            (new[] { "logitech", "lghub", "razer", "synapse", "steelseries", "corsair", "icue", "bloody" }, true, "Программа мыши/клавиатуры: без неё не работают макросы и подсветка."),
            (new[] { "microsoft.lists" }, false, "Microsoft Lists — списки из Microsoft 365. Можно отключить, откроется при необходимости."),
            (new[] { "onedrive" }, false, "Синхронизация OneDrive. Если не хранишь там файлы — смело отключай, запустишь вручную."),
            (new[] { "teams", "skype", "zoom" }, false, "Программа для созвонов. Запустишь, когда понадобится."),
            (new[] { "discord", "telegram", "whatsapp", "viber", "slack" }, false, "Мессенджер. Можно отключить и открывать вручную — включение станет быстрее."),
            (new[] { "spotify", "yandexmusic", "itunes", "aimp" }, false, "Музыка — запускается за пару секунд вручную."),
            (new[] { "steam", "epicgames", "eadesktop", "origin", "battle.net", "ubisoft", "gog" }, false, "Игровой магазин. Отключи — запустишь перед игрой."),
            (new[] { "msedge", "edge", "opera", "yandex", "browser", "chrome" }, false, "Браузер подгружается заранее и держит память. Можно отключить."),
            (new[] { "adobe", "ccxprocess", "acrotray", "creative cloud" }, false, "Обновления и фоновые службы Adobe. Можно отключить."),
            (new[] { "utorrent", "qbittorrent", "bittorrent", "mediaget" }, false, "Торрент-клиент: в фоне раздаёт файлы и занимает интернет."),
            (new[] { "anydesk", "teamviewer", "rustdesk" }, false, "Удалённый доступ. Включай, только когда нужно — так и безопаснее."),
            (new[] { "cortana", "copilot" }, false, "Помощник Microsoft. Можно отключить."),
            (new[] { "canva", "figma", "notion", "obsidian" }, false, "Рабочая программа запускается заранее. Можно отключить и открывать вручную."),
            (new[] { "googleupdate", "update", "updater" }, false, "Проверка обновлений. Программа обновится при обычном запуске."),
            (new[] { "autolaunch", "autostart", "launchatlogin" }, false, "Программа сама добавила себя в автозапуск, чтобы открываться быстрее. Можно отключить."),
        };

        public static StartupAdvice Advise(StartupItem s)
        {
            var text = (s.Name + " " + s.Command).ToLowerInvariant();
            foreach (var (match, keep, why) in StartupKnowledge)
                if (match.Any(m => text.Contains(m)))
                    return keep ? new("Лучше оставить", why, Safety.NotRecommended) : new("Можно отключить", why, Safety.Safe);
            return new("Неизвестно", "Если не знаешь, что это за программа, — не отключай.", Safety.Caution);
        }

        // ------------------------------------------------------------------
        //  DNS
        // ------------------------------------------------------------------

        public static readonly DnsPreset[] DnsPresets =
        {
            new("Автоматически", null, null, "DNS от провайдера или роутера — как было изначально"),
            new("Cloudflare", "1.1.1.1", "1.0.0.1", "Самый быстрый публичный DNS, не продаёт данные о сайтах"),
            new("AdGuard", "94.140.14.14", "94.140.15.15", "Вырезает рекламу и трекеры во всех браузерах и приложениях"),
            new("Quad9", "9.9.9.9", "149.112.112.112", "Блокирует сайты с вирусами и фишингом"),
            new("Google", "8.8.8.8", "8.8.4.4", "Надёжный и быстрый"),
            new("Для семьи", "1.1.1.3", "1.0.0.3", "Cloudflare: блокирует вредоносные и взрослые сайты"),
            new("Яндекс", "77.88.8.8", "77.88.8.1", "Быстрый в России и СНГ"),
        };

        static List<NetworkInterface> ActiveAdapters()
        {
            try
            {
                return NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
                    .Where(n => n.GetIPProperties().GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork))
                    .ToList();
            }
            catch { return new(); }
        }

        public static string CurrentDns()
        {
            var a = ActiveAdapters().FirstOrDefault();
            if (a == null) return "нет подключения";
            var ips = a.GetIPProperties().DnsAddresses.Where(x => x.AddressFamily == AddressFamily.InterNetwork).Select(x => x.ToString()).ToList();
            var p = DnsPresets.FirstOrDefault(d => d.Primary != null && ips.Contains(d.Primary));
            return (p?.Name ?? "провайдер") + " · " + string.Join(", ", ips);
        }

        public static Task<int> SetDns(DnsPreset p)
        {
            var lines = new List<string>();
            foreach (var a in ActiveAdapters())
            {
                if (p.Primary == null) lines.Add($"netsh interface ipv4 set dnsservers name=\"{a.Name}\" source=dhcp");
                else
                {
                    lines.Add($"netsh interface ipv4 set dnsservers name=\"{a.Name}\" source=static address={p.Primary} register=primary validate=no");
                    lines.Add($"netsh interface ipv4 add dnsservers name=\"{a.Name}\" address={p.Secondary} index=2 validate=no");
                }
            }
            if (lines.Count == 0) return Task.FromResult(-1);
            lines.Add("ipconfig /flushdns >nul");
            return Elevated.Script("dns", lines);
        }

        // ------------------------------------------------------------------
        //  Кто сейчас в интернете
        // ------------------------------------------------------------------

        public static Task<List<NetUser>> Connections() => Task.Run(() =>
        {
            var map = new Dictionary<int, (int Count, HashSet<string> Hosts)>();
            foreach (Match m in Regex.Matches(Maint.RunCmd("netstat", "-ano"), @"^\s*TCP\s+\S+\s+(\S+)\s+\S+\s+(\d+)\s*$", RegexOptions.Multiline))
            {
                var remote = m.Groups[1].Value;
                int pid = int.Parse(m.Groups[2].Value);
                if (pid == 0 || remote.StartsWith("0.0.0.0") || remote.StartsWith("[::]") || remote.StartsWith("127.") || remote.StartsWith("[::1]") || remote.StartsWith("*")) continue;
                var host = remote[..remote.LastIndexOf(':')].Trim('[', ']');
                if (!map.TryGetValue(pid, out var v)) v = (0, new HashSet<string>());
                v.Hosts.Add(host);
                map[pid] = (v.Count + 1, v.Hosts);
            }
            var res = new List<NetUser>();
            foreach (var (pid, v) in map)
            {
                string name;
                try { using var p = Process.GetProcessById(pid); name = p.ProcessName; } catch { name = "pid " + pid; }
                res.Add(new NetUser(name, pid, v.Count, string.Join(", ", v.Hosts.Take(3)) + (v.Hosts.Count > 3 ? $" и ещё {v.Hosts.Count - 3}" : "")));
            }
            // одинаковые программы (несколько процессов браузера) — одной строкой
            return res.GroupBy(r => r.Process, StringComparer.OrdinalIgnoreCase)
                      .Select(g => new NetUser(g.Key, g.First().Pid, g.Sum(x => x.Connections), g.First().Sample))
                      .OrderByDescending(r => r.Connections).ToList();
        });

        // ------------------------------------------------------------------
        //  Мастер исправлений
        // ------------------------------------------------------------------

        public static readonly Fix[] Fixes =
        {
            new("dns", "Не открывается сайт", "Сбрасывает кэш DNS — записную книжку адресов сайтов. Помогает, когда один сайт не открывается, а другие работают.",
                Safety.Safe, false, false, null, () => Maint.FlushDns()),
            new("explorer", "Зависла панель задач или проводник", "Перезапускает проводник: панель задач и окна папок исчезнут на пару секунд и вернутся. Открытые программы не закроются.",
                Safety.Safe, false, false, null, Tweaks.RestartExplorer),
            new("icons", "Значки стали белыми или неправильными", "Пересобирает кэш значков. Экран моргнёт, проводник перезапустится.",
                Safety.Safe, false, false, null, RebuildIconCache),
            new("store", "Не работает Microsoft Store", "Официальный сброс кэша магазина (wsreset). Покупки и приложения не пострадают.",
                Safety.Safe, false, false, null, () => SystemService.Run("wsreset.exe")),
            new("audio", "Пропал звук", "Перезапускает службы звука Windows. Музыка прервётся на секунду.",
                Safety.Safe, true, false, new[] { "net stop AudioEndpointBuilder /y >nul 2>&1", "net start AudioEndpointBuilder >nul 2>&1", "net start Audiosrv >nul 2>&1" }),
            new("time", "Часы показывают неправильное время", "Синхронизирует часы с сервером времени Microsoft.",
                Safety.Safe, true, false, new[] { "sc config w32time start= demand >nul", "net start w32time >nul 2>&1", "w32tm /resync /force >nul 2>&1" }),
            new("print", "Принтер не печатает", "Перезапускает диспетчер печати и убирает зависшие задания из очереди.",
                Safety.Safe, true, false, new[] { "net stop spooler >nul 2>&1", "del /q /f \"%SystemRoot%\\System32\\spool\\PRINTERS\\*.*\" >nul 2>&1", "net start spooler >nul 2>&1" }),
            new("chkdsk", "Проверить диск на ошибки", "Быстрая проверка файловой системы без перезагрузки (chkdsk /scan). Только читает и сообщает, ничего не меняет.",
                Safety.Safe, true, true, new[] { "chkdsk %SystemDrive% /scan" }),
            new("sfc", "Проверить системные файлы", "Официальная проверка Windows (SFC): находит повреждённые системные файлы и заменяет их правильными копиями из Windows. " +
                "Ничего лишнего не трогает. 5–20 минут — в окне видно ход проверки.",
                Safety.Safe, true, true, new[] { "sfc /scannow" }),
            new("dism", "Восстановить образ Windows", "DISM скачивает правильные копии системных файлов с серверов Microsoft, затем SFC их ставит. " +
                "Запускай, если SFC нашёл ошибки, но не смог исправить. 10–30 минут, нужен интернет.",
                Safety.Safe, true, true, new[] { "DISM /Online /Cleanup-Image /RestoreHealth", "sfc /scannow" }),
            new("defender", "Быстрая проверка на вирусы", "Быстрое сканирование Защитником Windows — самые опасные места системы.",
                Safety.Safe, true, true, new[] { "\"%ProgramFiles%\\Windows Defender\\MpCmdRun.exe\" -SignatureUpdate", "\"%ProgramFiles%\\Windows Defender\\MpCmdRun.exe\" -Scan -ScanType 1" }),
            new("winsxs", "Сжать хранилище компонентов", "Официально удаляет старые версии системных компонентов (DISM). Освобождает 1–5 ГБ. " +
                "Минус: уже установленные обновления нельзя будет удалить.",
                Safety.Caution, true, true, new[] { "DISM /Online /Cleanup-Image /StartComponentCleanup" }),
            new("net", "Интернет пропадает или очень медленный", "Полный сброс сетевых настроек Windows (Winsock и TCP/IP). После него нужна перезагрузка, " +
                "а некоторые VPN-программы придётся переустановить. Пробуй, если остальное не помогло.",
                Safety.Caution, true, false, new[] { "netsh winsock reset >nul", "netsh int ip reset >nul", "ipconfig /flushdns >nul" }),
            new("wu", "Обновления Windows зависли", "Способ Microsoft: остановить службы обновления и начать загрузку заново. " +
                "Старая папка не удаляется, а переименовывается — её можно вернуть.",
                Safety.Caution, true, false, new[] { "net stop wuauserv /y >nul 2>&1", "net stop bits /y >nul 2>&1",
                    "ren \"%SystemRoot%\\SoftwareDistribution\" SoftwareDistribution.old-%RANDOM% >nul 2>&1", "net start bits >nul 2>&1", "net start wuauserv >nul 2>&1" }),
        };

        public static async Task<string> RunFix(Fix f)
        {
            if (f.Local != null) { await Task.Run(f.Local); return "Готово ✓"; }
            if (f.Console) { Elevated.Console(f.Key, f.Title, f.Commands); return "Открыто окно проверки — следи за ходом там"; }
            int code = await Elevated.Script("fix-" + f.Key, f.Commands);
            return code < 0 ? "Не дано разрешение администратора" : f.Key == "net" ? "Готово ✓ Перезагрузи ноутбук" : "Готово ✓";
        }

        static void RebuildIconCache()
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            foreach (var p in Process.GetProcessesByName("explorer")) using (p) { try { p.Kill(); } catch { } }
            System.Threading.Thread.Sleep(800);
            try { File.Delete(Path.Combine(local, "IconCache.db")); } catch { }
            try { foreach (var f in Directory.GetFiles(Path.Combine(local, "Microsoft", "Windows", "Explorer"), "iconcache_*.db")) try { File.Delete(f); } catch { } } catch { }
            if (Process.GetProcessesByName("explorer").Length == 0) SystemService.Run("explorer.exe");
        }

        // ------------------------------------------------------------------
        //  Таймер выключения
        // ------------------------------------------------------------------

        static DispatcherTimer _offT;
        public static DateTime? OffAt { get; private set; }
        public static string OffAction { get; private set; }

        public static readonly (string Key, string Name)[] OffActions = { ("sleep", "Сон"), ("hibernate", "Гибернация"), ("shutdown", "Выключить"), ("restart", "Перезагрузить"), ("lock", "Заблокировать") };

        public static void ScheduleOff(string action, int minutes)
        {
            OffAction = action;
            OffAt = DateTime.Now.AddMinutes(minutes);
            _offT ??= new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(10) };
            _offT.Tick -= OffTick;
            _offT.Tick += OffTick;
            _offT.Start();
        }

        public static void CancelOff() { OffAt = null; _offT?.Stop(); }

        static void OffTick(object s, EventArgs e)
        {
            if (OffAt is not DateTime at) { _offT.Stop(); return; }
            var left = at - DateTime.Now;
            if (left.TotalSeconds > 70 && left.TotalSeconds <= 80)
                App.Island?.Toast("", "Через минуту: " + OffActions.First(a => a.Key == OffAction).Name.ToLower(), "Отменить — в Центре управления → Питание");
            if (left > TimeSpan.Zero) return;
            CancelOff();
            Store.Flush();
            switch (OffAction)
            {
                case "sleep": SetSuspendState(false, false, false); break;
                case "hibernate": SetSuspendState(true, false, false); break;
                case "shutdown": Maint.RunCmd("shutdown", "/s /t 30 /c \"N-Island: выключение по таймеру\""); break;
                case "restart": Maint.RunCmd("shutdown", "/r /t 30 /c \"N-Island: перезагрузка по таймеру\""); break;
                case "lock": LockWorkStation(); break;
            }
        }
    }

    /// <summary>Фоновые сторожа ноутбука: камера, батарея, «прожорливые» программы, диск, перезагрузка, автоочистка.
    /// Один лёгкий таймер раз в 5 секунд, тяжёлые проверки — реже.</summary>
    public class LaptopService
    {
        public static readonly LaptopService I = new();
        static Settings S => Store.Settings;
        DispatcherTimer _t;
        int _tick;
        HashSet<string> _camOn = new();
        bool? _lastAc;
        bool _highShown;
        DateTime _rebootShown, _diskShown;
        readonly Dictionary<int, (TimeSpan Cpu, int Strikes)> _cpu = new();
        readonly Dictionary<string, DateTime> _hungryShown = new();
        DateTime _cpuAt;

        public void Start()
        {
            _t = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(5) };
            _t.Tick += (_, __) => { try { Tick(); } catch (Exception e) { Log.Info("laptop: " + e.Message); } };
            _t.Start();
            StartCameraWatch();
        }

        [DllImport("advapi32.dll")]
        static extern int RegNotifyChangeKeyValue(Microsoft.Win32.SafeHandles.SafeRegistryHandle key, bool subtree, uint filter,
                                                  Microsoft.Win32.SafeHandles.SafeWaitHandle ev, bool async);

        /// <summary>Сторож камеры без опроса: Windows сама будит поток, когда меняется журнал доступа к камере/месту.</summary>
        void StartCameraWatch()
        {
            var dispatcher = System.Windows.Application.Current.Dispatcher;
            dispatcher.BeginInvoke(new Action(() => { try { WatchCamera(false); } catch { } }), DispatcherPriority.ApplicationIdle);
            new System.Threading.Thread(() =>
            {
                try
                {
                    using var key = Registry.CurrentUser.OpenSubKey(Privacy.Consent);
                    if (key == null) return;
                    using var ev = new System.Threading.AutoResetEvent(false);
                    while (RegNotifyChangeKeyValue(key.Handle, true, 0x1 | 0x4, ev.SafeWaitHandle, true) == 0)   // имя ключа | значение
                    {
                        ev.WaitOne();
                        System.Threading.Thread.Sleep(400);   // пачка изменений — одна проверка
                        dispatcher.BeginInvoke(new Action(() => { try { if (S.CamMicAlert || S.ActivityJournal) WatchCamera(S.CamMicAlert); } catch { } }));
                    }
                }
                catch (Exception e) { Log.Info("camera watch: " + e.Message); }
            }) { IsBackground = true, Name = "camera-watch" }.Start();
        }

        void Show(string kind, string glyph, string title, string sub, string section, double seconds = 6) =>
            App.Island?.ShowActivity(new Activity
            {
                Kind = kind, Glyph = glyph, Title = title, Sub = sub, Seconds = seconds, Wide = true,
                ActionText = section != null ? "Открыть" : null, Action = section != null ? () => App.ShowCenter(section) : null,
            });

        void Tick()
        {
            _tick++;
            WatchPower();
            if (_tick % 6 == 0 && S.HungryWatch)                                   // раз в 30 с
            {
                // перебирать процессы дорого — делаем это, только когда процессор в целом загружен
                if (TotalCpu() >= S.HungryCpu || _cpu.Values.Any(v => v.Strikes > 0)) WatchCpu();
                else if (_cpu.Count > 0) { _cpu.Clear(); _cpuAt = default; }
            }
            if (_tick % 120 == 24) Daily();                                       // раз в 10 мин, первый раз через 2 мин
        }

        void WatchCamera(bool alert)
        {
            var all = Privacy.AccessLog(S.ActivityJournal);
            if (S.ActivityJournal) Tracking.Track(all);
            var now = all.Where(a => a.InUse && a.Kind is "камера" or "место").Select(a => a.Kind + ":" + a.App).ToHashSet();
            foreach (var n in now.Where(n => !_camOn.Contains(n)))
            {
                var parts = n.Split(':', 2);
                if (alert)
                    Show("cam", parts[0] == "камера" ? "" : "", parts[0] == "камера" ? "Камера включена" : "Запрошено местоположение", parts[1], "privacy", 5);
            }
            _camOn = now;
        }

        void WatchPower()
        {
            var sys = SystemService.I;
            if (!sys.HasBattery) return;
            bool ac = sys.Charging;
            if (_lastAc != null && _lastAc != ac && S.AutoPlan)
                Laptop.PowerMode = ac ? (string.IsNullOrEmpty(S.PlanOnAc) ? "perf" : S.PlanOnAc) : (string.IsNullOrEmpty(S.PlanOnDc) ? "saver" : S.PlanOnDc);
            _lastAc = ac;
            int pct = sys.BatteryPercent;
            if (!ac || pct < S.BatteryHigh - 5) _highShown = false;
            if (ac && S.BatteryAlertHigh && pct >= S.BatteryHigh && !_highShown)
            {
                _highShown = true;
                Show("battery", "", $"Заряд {pct}% — можно отключить зарядку", "Так батарея изнашивается медленнее", "power", 8);
            }
        }

        [DllImport("kernel32.dll")] static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
        long _sIdle, _sKernel, _sUser;

        /// <summary>Загрузка всего процессора с прошлого вызова, % (свой счётчик — не мешает индикатору в чёлке).</summary>
        int TotalCpu()
        {
            if (!GetSystemTimes(out var i, out var k, out var u)) return 0;
            long di = i - _sIdle, tot = k - _sKernel + u - _sUser;
            bool first = _sKernel == 0;
            _sIdle = i; _sKernel = k; _sUser = u;
            return first || tot <= 0 ? 0 : (int)(100.0 * (tot - di) / tot);
        }

        void WatchCpu()
        {
            var now = DateTime.Now;
            double wall = (now - _cpuAt).TotalSeconds;
            _cpuAt = now;
            int cores = Environment.ProcessorCount;
            var seen = new HashSet<int>();
            foreach (var p in Process.GetProcesses())
            {
                using (p)
                {
                    try
                    {
                        if (p.Id <= 4) continue;
                        var cpu = p.TotalProcessorTime;
                        seen.Add(p.Id);
                        if (_cpu.TryGetValue(p.Id, out var prev) && wall is > 5 and < 120)
                        {
                            double load = (cpu - prev.Cpu).TotalSeconds / wall / cores * 100;
                            int strikes = load >= S.HungryCpu ? prev.Strikes + 1 : 0;
                            _cpu[p.Id] = (cpu, strikes);
                            if (strikes == 3 && (!_hungryShown.TryGetValue(p.ProcessName, out var t) || (now - t).TotalHours > 1))
                            {
                                _hungryShown[p.ProcessName] = now;
                                Show("pc", "", $"{p.ProcessName} грузит процессор на {load:0}%", "Уже полторы минуты. Если программа зависла — закрой её", "perf", 8);
                            }
                        }
                        else _cpu[p.Id] = (cpu, 0);
                    }
                    catch { }
                }
            }
            foreach (var dead in _cpu.Keys.Where(k => !seen.Contains(k)).ToList()) _cpu.Remove(dead);
        }

        void Daily()
        {
            var now = DateTime.Now;
            if (S.RebootRemind && Maint.Uptime.TotalDays >= S.RebootDays && (now - _rebootShown).TotalHours > 20 && now.Hour is >= 10 and < 22)
            {
                _rebootShown = now;
                Show("pc", "", $"Ноутбук работает без перезагрузки {(int)Maint.Uptime.TotalDays} дн.", "Перезагрузи, когда будет удобно — станет шустрее", null, 7);
            }
            if (S.DiskLowAlert && (now - _diskShown).TotalHours > 20)
            {
                var root = Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\');
                var d = Maint.Disks().FirstOrDefault(x => string.Equals(x.Name, root, StringComparison.OrdinalIgnoreCase));
                if (d != null && d.Total > 0 && d.Free * 100.0 / d.Total < 10)
                {
                    _diskShown = now;
                    Show("pc", "", $"Мало места на диске {d.Name}: {Maint.Size(d.Free)}", "Сделай очистку в Центре управления", "clean", 8);
                }
            }
            if (S.AutoClean && (now - S.LastCleanup).TotalDays >= Math.Max(1, S.AutoCleanDays)) _ = AutoClean();
        }

        /// <summary>Тихая автоочистка: только «безопасные» пункты без прав администратора; программы, которые сейчас открыты, пропускаются.</summary>
        async Task AutoClean()
        {
            var list = Cleaner.Targets.Where(t => t.Safety == Safety.Safe && !t.Admin && !t.InfoOnly && t.CustomClean == null && t.Group != "Следы" && t.Blocked == null).ToList();
            var r = await Cleaner.Clean(list);
            if (r.Freed > 1 << 20) Show("pc", "", "Автоочистка: освобождено " + Maint.Size(r.Freed), "Только временные файлы и кэш", null, 5);
        }
    }
}
