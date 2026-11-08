using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using NIsland.Core;

namespace NIsland.Services
{
    public record Threat(string Path, int Score, string Verdict, List<string> Reasons, string Sha256, bool Known);
    public record QuarantineItem(string Id, string Original, string Sha256, string Reason, DateTime At, long Size);

    // подпись файла — из проверки файлов OSINT
    public static partial class Osint
    {
        /// <summary>0 — подписан и подпись верна; 1 — системный файл из каталога Windows; иначе код ошибки WinVerifyTrust.</summary>
        public static (int Hr, string Signer) Signature(string path)
        {
            int hr = VerifySignature(path);
            if (hr == unchecked((int)0x800B0100) && InSystemCatalog(path)) return (1, "Microsoft Windows");
            string signer = null;
            if (hr == 0)
                try { signer = new System.Security.Cryptography.X509Certificates.X509Certificate2(System.Security.Cryptography.X509Certificates.X509Certificate.CreateFromSignedFile(path)).GetNameInfo(System.Security.Cryptography.X509Certificates.X509NameType.SimpleName, false); } catch { }
            return (hr, signer);
        }
    }

    /// <summary>
    /// «Защитник N-Island»: своя проверка поверх Защитника Windows. База известных вредоносных хешей (signature-base, обновляется),
    /// движок установленного антивируса через AMSI, свои эвристики для программ (упаковщики, внедрение в процессы, кейлоггеры,
    /// шифровальщики, отключение защиты) и скриптов (обфускация, загрузчики), быстрая проверка автозагрузки и процессов,
    /// защита в реальном времени для «Загрузок», рабочего стола и автозагрузки, карантин и аудит настроек безопасности.
    /// </summary>
    public static class Antivirus
    {
        static Settings S => Store.Settings;
        static string Dir => Path.Combine(Store.Dir, "av");
        static string DbFile => Path.Combine(Dir, "hashes.txt");
        static string QDir => Path.Combine(Dir, "quarantine");
        public static event Action Changed;

        // ------------------------------------------------------------------
        //  База хешей
        // ------------------------------------------------------------------

        static Dictionary<string, string> _db;
        public static int DbCount => _db?.Count ?? 0;
        public static DateTime? DbUpdated => File.Exists(DbFile) ? File.GetLastWriteTime(DbFile) : null;
        const string DbUrl = "https://raw.githubusercontent.com/Neo23x0/signature-base/master/iocs/hash-iocs.txt";

        public static void LoadDb()
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(DbFile))
                    foreach (var line in File.ReadLines(DbFile))
                    {
                        if (line.Length < 32 || line[0] == '#') continue;
                        int c = line.IndexOf(';');
                        var h = (c > 0 ? line[..c] : line).Trim();
                        if (h.Length is 32 or 40 or 64 && h.All(Uri.IsHexDigit)) d[h] = c > 0 ? line[(c + 1)..].Trim() : "известная угроза";
                    }
            }
            catch (Exception e) { Log.Info("av db: " + e.Message); }
            _db = d;
        }

        public static async Task<string> UpdateDb()
        {
            try
            {
                var text = await Integrations.Http.GetStringAsync(DbUrl);
                if (!text.Contains(';')) return "Сервер вернул не базу";
                Directory.CreateDirectory(Dir);
                File.WriteAllText(DbFile, text);
                LoadDb();
                Changed?.Invoke();
                return null;
            }
            catch (Exception e) { return "Не удалось обновить базу: " + Integrations.Reason(e); }
        }

        // ------------------------------------------------------------------
        //  AMSI: движок установленного антивируса (обычно Защитник Windows)
        // ------------------------------------------------------------------

        [DllImport("amsi.dll", CharSet = CharSet.Unicode)] static extern int AmsiInitialize(string appName, out IntPtr ctx);
        [DllImport("amsi.dll")] static extern void AmsiUninitialize(IntPtr ctx);
        [DllImport("amsi.dll")] static extern int AmsiOpenSession(IntPtr ctx, out IntPtr session);
        [DllImport("amsi.dll")] static extern void AmsiCloseSession(IntPtr ctx, IntPtr session);
        [DllImport("amsi.dll", CharSet = CharSet.Unicode)] static extern int AmsiScanBuffer(IntPtr ctx, byte[] buffer, uint length, string contentName, IntPtr session, out int result);
        static IntPtr _amsi;

        /// <summary>null — AMSI недоступен; иначе true — движок считает содержимое вредоносным.</summary>
        public static bool? AmsiMalware(byte[] data, string name)
        {
            try
            {
                if (_amsi == IntPtr.Zero && AmsiInitialize("N-Island", out _amsi) != 0) { _amsi = IntPtr.Zero; return null; }
                if (AmsiOpenSession(_amsi, out var session) != 0) return null;
                try { return AmsiScanBuffer(_amsi, data, (uint)data.Length, name, session, out var r) == 0 ? r >= 32768 : null; }
                finally { AmsiCloseSession(_amsi, session); }
            }
            catch { return null; }
        }

        /// <summary>Отдельная проверка файла Защитником Windows (MpCmdRun). true — угроза найдена.</summary>
        public static async Task<(bool? Threat, string Text)> DefenderScan(string path)
        {
            var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Windows Defender", "MpCmdRun.exe");
            if (!File.Exists(exe)) return (null, "Защитник Windows не найден");
            try
            {
                var psi = new ProcessStartInfo(exe, $"-Scan -ScanType 3 -File \"{path}\" -DisableRemediation") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
                using var p = Process.Start(psi);
                var o = await p.StandardOutput.ReadToEndAsync();
                await p.WaitForExitAsync();
                return p.ExitCode == 2 ? (true, "Защитник Windows нашёл угрозу") : p.ExitCode == 0 ? (false, "Защитник Windows: угроз нет ✓") : (null, "Защитник Windows не смог проверить (код " + p.ExitCode + ")");
            }
            catch (Exception e) { return (null, e.Message); }
        }

        /// <summary>Хеш в открытой базе CIRCL: известный чистый файл (NSRL) или отмеченный как вредоносный.</summary>
        public static async Task<string> OnlineLookup(string sha256)
        {
            try
            {
                using var r = await Integrations.Http.GetAsync("https://hashlookup.circl.lu/lookup/sha256/" + sha256);
                if (r.StatusCode == System.Net.HttpStatusCode.NotFound) return "В открытой базе файлов (CIRCL) этого файла нет — он редкий или новый";
                using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
                var root = doc.RootElement;
                if (root.TryGetProperty("KnownMalicious", out var km)) return "⚠ Отмечен как вредоносный: " + km;
                var name = root.TryGetProperty("FileName", out var fn) ? fn.GetString() : "";
                var prod = root.TryGetProperty("ProductCode", out var pc) && pc.TryGetProperty("ProductName", out var pn) ? pn.GetString() : "";
                return "✓ Известный чистый файл" + (prod != "" ? " из «" + prod + "»" : name != "" ? " (" + Path.GetFileName(name) + ")" : "");
            }
            catch (Exception e) { return "Не удалось проверить онлайн: " + Integrations.Reason(e); }
        }

        // ------------------------------------------------------------------
        //  Проверка файла
        // ------------------------------------------------------------------

        static readonly string[] ExecExt = { ".exe", ".dll", ".scr", ".com", ".pif", ".cpl", ".sys", ".ocx", ".msi" };
        static readonly string[] ScriptExt = { ".ps1", ".psm1", ".bat", ".cmd", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh", ".hta", ".reg" };
        public static readonly string[] Risky = ExecExt.Concat(ScriptExt).Concat(new[] { ".lnk", ".jar", ".zip", ".iso", ".img", ".vhd", ".url" }).ToArray();

        static readonly (string Name, Regex Rx, int W)[] ScriptRules =
        {
            ("закодированная команда PowerShell (-EncodedCommand)", new(@"-e(nc|ncodedcommand)?\s+[A-Za-z0-9+/=]{40,}", RegexOptions.IgnoreCase), 35),
            ("выполнение скачанного кода (IEX / Invoke-Expression)", new(@"\b(iex|invoke-expression)\b", RegexOptions.IgnoreCase), 20),
            ("скачивание из интернета (DownloadString / WebClient / Invoke-WebRequest)", new(@"downloadstring|downloadfile|net\.webclient|invoke-webrequest|iwr\s+http|start-bitstransfer", RegexOptions.IgnoreCase), 15),
            ("длинный текст в Base64 — так прячут код", new(@"frombase64string|[A-Za-z0-9+/]{300,}={0,2}", RegexOptions.IgnoreCase), 15),
            ("отключение Защитника Windows", new(@"set-mppreference\s+-disable|add-mppreference\s+-exclusion|disableantispyware|disablerealtimemonitoring", RegexOptions.IgnoreCase), 45),
            ("удаление теневых копий (так делают шифровальщики)", new(@"vssadmin(\.exe)?\s+delete\s+shadows|wmic\s+shadowcopy\s+delete|bcdedit.+recoveryenabled\s+no", RegexOptions.IgnoreCase), 60),
            ("загрузка через системные утилиты (certutil / bitsadmin / mshta / regsvr32)", new(@"certutil(\.exe)?\s+-urlcache|bitsadmin(\.exe)?\s+/transfer|mshta(\.exe)?\s+https?:|regsvr32(\.exe)?\s+/s\s+/n\s+/u\s+/i:http", RegexOptions.IgnoreCase), 35),
            ("скрытый запуск окна (-WindowStyle Hidden / -nop)", new(@"-w(indowstyle)?\s+hidden|-nop\b|-noprofile.+-noninteractive", RegexOptions.IgnoreCase), 10),
            ("добавление в автозагрузку", new(@"currentversion\\run|schtasks(\.exe)?\s+/create|startup\\", RegexOptions.IgnoreCase), 12),
            ("обход проверки скриптов (AMSI bypass)", new(@"amsiutils|amsiinitfailed|amsiscanbuffer", RegexOptions.IgnoreCase), 50),
            ("кража паролей браузера", new(@"login data|\\local state|cookies\.sqlite|logins\.json", RegexOptions.IgnoreCase), 25),
        };

        static readonly (string Name, string[] All, int W)[] ApiRules =
        {
            ("внедрение кода в другие программы", new[] { "VirtualAllocEx", "WriteProcessMemory", "CreateRemoteThread" }, 30),
            ("подмена процесса (process hollowing)", new[] { "UnmapViewOfSection", "SetThreadContext", "ResumeThread" }, 30),
            ("перехват клавиатуры (кейлоггер)", new[] { "SetWindowsHookEx", "GetAsyncKeyState" }, 20),
            ("скачивание и запуск файлов", new[] { "URLDownloadToFile", "ShellExecute" }, 15),
            ("шифрование файлов с перебором папок", new[] { "CryptEncrypt", "FindFirstFile", "FindNextFile", "MoveFileEx" }, 15),
            ("снимки экрана и буфер обмена", new[] { "BitBlt", "GetClipboardData", "GetDesktopWindow" }, 6),
        };

        static readonly string[] PackerSections = { "UPX0", "UPX1", ".aspack", ".adata", "MPRESS1", "MPRESS2", ".themida", ".vmp0", ".vmp1", ".enigma1", ".petite", "PEC2" };

        static double Entropy(byte[] data, int start, int len)
        {
            if (len <= 0) return 0;
            var f = new int[256];
            for (int i = start; i < start + len && i < data.Length; i++) f[data[i]]++;
            double e = 0;
            foreach (var c in f) if (c > 0) { double p = (double)c / len; e -= p * Math.Log2(p); }
            return e;
        }

        static bool IsPe(byte[] b) => b.Length > 0x40 && b[0] == 'M' && b[1] == 'Z';

        /// <summary>Разбор PE: секции (упаковщики, энтропия) и подозрительные функции Windows среди импортов и строк.</summary>
        static void PeHeuristics(byte[] b, List<string> reasons, ref int score)
        {
            try
            {
                int pe = BitConverter.ToInt32(b, 0x3C);
                if (pe <= 0 || pe > b.Length - 248 || b[pe] != 'P' || b[pe + 1] != 'E') { reasons.Add("Повреждённый заголовок программы"); score += 10; return; }
                int nSec = BitConverter.ToUInt16(b, pe + 6), optSize = BitConverter.ToUInt16(b, pe + 20);
                int sec = pe + 24 + optSize;
                bool packer = false, hot = false;
                for (int i = 0; i < nSec && sec + 40 * (i + 1) <= b.Length; i++)
                {
                    int o = sec + 40 * i;
                    var name = Encoding.ASCII.GetString(b, o, 8).TrimEnd('\0');
                    int rawSize = BitConverter.ToInt32(b, o + 16), rawPtr = BitConverter.ToInt32(b, o + 20);
                    uint ch = BitConverter.ToUInt32(b, o + 36);
                    if (PackerSections.Any(p => name.Equals(p, StringComparison.OrdinalIgnoreCase))) packer = true;
                    if ((ch & 0x20000000) != 0 && rawSize > 4096 && rawPtr > 0 && rawPtr + rawSize <= b.Length && Entropy(b, rawPtr, rawSize) > 7.3) hot = true;
                    if ((ch & 0x20000000) != 0 && (ch & 0x80000000) != 0) { reasons.Add($"Секция «{name}» одновременно исполняемая и записываемая — так распаковывают скрытый код"); score += 10; }
                }
                if (packer) { reasons.Add("Сжат упаковщиком (UPX, Themida, VMProtect…) — содержимое скрыто от проверки"); score += 15; }
                if (hot) { reasons.Add("Код программы зашифрован или сжат (высокая энтропия)"); score += 10; }
            }
            catch { }
            var text = Encoding.ASCII.GetString(b);
            // большие .NET-программы несут внутри всю стандартную библиотеку с тысячами имён функций — по строкам их не оценить
            bool dotnetBundle = b.Length > 8 * 1024 * 1024 && text.Contains("System.Private.CoreLib", StringComparison.Ordinal);
            if (!dotnetBundle)
            foreach (var (name, all, w) in ApiRules)
                if (all.All(api => text.Contains(api, StringComparison.Ordinal))) { reasons.Add("Умеет: " + name); score += w; }
            if (text.Contains("vssadmin delete shadows", StringComparison.OrdinalIgnoreCase) || text.Contains("shadowcopy delete", StringComparison.OrdinalIgnoreCase))
            { reasons.Add("Внутри команда удаления теневых копий — признак шифровальщика"); score += 50; }
            if (text.Contains("DisableAntiSpyware") || text.Contains("DisableRealtimeMonitoring") || text.Contains("Add-MpPreference"))
            { reasons.Add("Внутри команды отключения Защитника Windows"); score += 35; }
            if (Regex.IsMatch(text, @"stratum\+tcp://|xmrig|cryptonight", RegexOptions.IgnoreCase)) { reasons.Add("Признаки скрытого майнера криптовалюты"); score += 40; }
        }

        static bool InSystemDirs(string path)
        {
            var p = path.ToLowerInvariant();
            return p.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows).ToLowerInvariant() + "\\")
                || p.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles).ToLowerInvariant() + "\\")
                || p.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86).ToLowerInvariant() + "\\");
        }

        static bool InRiskyDirs(string path)
        {
            var p = path.ToLowerInvariant();
            return p.Contains("\\appdata\\local\\temp\\") || p.Contains("\\appdata\\roaming\\") && !p.Contains("\\microsoft\\") || p.Contains("\\startup\\") || p.Contains("\\programdata\\") && !p.Contains("\\microsoft\\");
        }

        /// <summary>Самопроверка: правила для скриптов на тексте в памяти (без записи на диск, чтобы не будить Защитник Windows).</summary>
        internal static List<string> ScriptRulesForTest(string text) => ScriptRules.Where(r => r.Rx.IsMatch(text)).Select(r => r.Name).ToList();

        public static bool Allowed(string sha) => (S.AvAllow ?? "").Contains(sha, StringComparison.OrdinalIgnoreCase);

        public static void Allow(string sha)
        {
            if (string.IsNullOrEmpty(sha) || Allowed(sha)) return;
            S.AvAllow = string.Join(",", (S.AvAllow ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Append(sha).TakeLast(500));
        }

        /// <summary>Проверить один файл. Ничего не меняет — только оценка 0–100 и причины.</summary>
        public static Threat ScanFile(string path, bool useAmsi = true)
        {
            var reasons = new List<string>();
            int score = 0;
            var fi = new FileInfo(path);
            if (!fi.Exists) return new Threat(path, 0, "Нет файла", reasons, null, false);
            var ext = fi.Extension.ToLowerInvariant();
            byte[] data = null;
            string sha = null;
            try
            {
                if (fi.Length <= 200L * 1024 * 1024)
                {
                    data = File.ReadAllBytes(path);
                    sha = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
                }
            }
            catch (IOException) { reasons.Add("Файл занят другой программой — содержимое не проверено"); }
            catch (UnauthorizedAccessException) { reasons.Add("Нет доступа к файлу — содержимое не проверено"); }

            if (sha != null && Allowed(sha)) return new Threat(path, 0, "В списке доверенных", new List<string> { "Ты отметил этот файл как безопасный" }, sha, false);

            // 1) база известных угроз
            bool known = false;
            if (data != null && _db != null && _db.Count > 0)
            {
                var md5 = Convert.ToHexString(MD5.HashData(data));
                var sha1 = Convert.ToHexString(SHA1.HashData(data));
                foreach (var h in new[] { sha, md5, sha1 })
                    if (_db.TryGetValue(h, out var note)) { reasons.Add("Есть в базе известных угроз: " + note); score += 100; known = true; break; }
            }

            bool pe = data != null && IsPe(data);
            bool script = ScriptExt.Contains(ext);
            // 2) движок установленного антивируса (AMSI)
            if (useAmsi && data != null && !known && (script || (pe && data.Length <= 25 * 1024 * 1024)))
            {
                if (AmsiMalware(data, path) == true) { reasons.Add("Движок Защитника Windows (AMSI) считает файл вредоносным"); score += 90; }
            }

            // 3) подпись и место
            (int Hr, string Signer) sig = (-1, null);
            if (pe || ext is ".msi" or ".ps1") sig = Osint.Signature(path);
            bool signed = sig.Hr is 0 or 1;
            if (pe && !signed)
            {
                reasons.Add("Без цифровой подписи");
                score += 8;
                if (InRiskyDirs(path)) { reasons.Add("Лежит в папке, откуда часто запускаются вирусы (Temp, AppData, автозагрузка)"); score += 15; }
            }

            // 4) эвристики
            if (pe) PeHeuristics(data, reasons, ref score);
            if (script && data != null)
            {
                var text = Encoding.UTF8.GetString(data);
                if (text.Contains('\0')) text = Encoding.Unicode.GetString(data);
                foreach (var (name, rx, w) in ScriptRules)
                    if (rx.IsMatch(text)) { reasons.Add(char.ToUpper(name[0]) + name[1..]); score += w; }
            }
            if (ext == ".lnk" && data != null)
            {
                var s = (Encoding.Unicode.GetString(data) + Encoding.ASCII.GetString(data)).ToLowerInvariant();
                if ((s.Contains("powershell") || s.Contains("cmd.exe") || s.Contains("mshta") || s.Contains("wscript")) && (s.Contains("http") || s.Contains("-enc") || s.Contains("hidden")))
                { reasons.Add("Ярлык запускает скрытую команду со скачиванием — классическая ловушка"); score += 55; }
            }
            if (ext == ".zip" && data != null)
            {
                try
                {
                    using var z = new System.IO.Compression.ZipArchive(new MemoryStream(data));
                    var bad = z.Entries.Where(e => Risky.Take(18).Contains(Path.GetExtension(e.Name).ToLowerInvariant())).Select(e => e.Name).ToList();
                    if (bad.Count > 0) { reasons.Add("В архиве программы или скрипты: " + string.Join(", ", bad.Take(4))); score += 8; }
                    if (z.Entries.Any(e => Regex.IsMatch(e.Name, @"\.(pdf|docx?|xlsx?|jpe?g|png|txt)\.(exe|scr|js|vbs|bat|cmd|lnk)$", RegexOptions.IgnoreCase)))
                    { reasons.Add("В архиве файл с двойным расширением — маскировка программы под документ"); score += 45; }
                }
                catch { }
            }
            if (Regex.IsMatch(fi.Name, @"\.(pdf|docx?|xlsx?|jpe?g|png|txt|mp4|mp3)\.(exe|scr|com|js|vbs|bat|cmd|lnk|hta)$", RegexOptions.IgnoreCase))
            { reasons.Add("Двойное расширение — программа выдаёт себя за документ или картинку"); score += 45; }
            if (fi.Name.Any(c => c is '\u202E' or '\u202D')) { reasons.Add("В имени спрятан символ разворота текста"); score += 45; }

            // сам N-Island (у сборок нет подписи) — свой
            if (pe && score < 90)
            {
                try
                {
                    var vi = FileVersionInfo.GetVersionInfo(path);
                    if (vi.ProductName == "N-Island" && vi.CompanyName == "@Ne1zvestny1i") { score = Math.Max(0, score - 40); reasons.Add("Программа N-Island"); }
                }
                catch { }
            }
            // доверие: подписанным известным издателем — заметно спокойнее (если нет совпадения по базе или AMSI)
            if (signed && score < 90)
            {
                score = Math.Max(0, score - 45);
                reasons.Add(sig.Hr == 1 ? "Системный файл Windows ✓" : "Подписан" + (sig.Signer != null ? " издателем «" + sig.Signer + "»" : "") + " ✓");
            }
            score = Math.Clamp(score, 0, 100);
            var verdict = score >= 70 ? "Опасно" : score >= 40 ? "Подозрительно" : "Чисто";
            return new Threat(path, score, verdict, reasons, sha, known);
        }

        // ------------------------------------------------------------------
        //  Проверки
        // ------------------------------------------------------------------

        static IEnumerable<string> RecentRisky(string dir, int days, int max)
        {
            if (!Directory.Exists(dir)) yield break;
            var since = DateTime.Now.AddDays(-days);
            IEnumerable<FileInfo> files;
            try { files = new DirectoryInfo(dir).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, MaxRecursionDepth = 3 }); }
            catch { yield break; }
            int n = 0;
            foreach (var f in files)
            {
                if (n >= max) yield break;
                bool risky;
                try { risky = Risky.Contains(f.Extension.ToLowerInvariant()) && f.LastWriteTime >= since; } catch { continue; }
                if (risky) { n++; yield return f.FullName; }
            }
        }

        /// <summary>Путь к программе из строки автозапуска: «"C:\a b\x.exe" -arg» → C:\a b\x.exe.</summary>
        public static string ExeFromCommand(string cmd)
        {
            if (string.IsNullOrWhiteSpace(cmd)) return null;
            cmd = Environment.ExpandEnvironmentVariables(cmd.Trim());
            if (cmd.StartsWith('"')) { int e = cmd.IndexOf('"', 1); return e > 1 ? cmd[1..e] : null; }
            var m = Regex.Match(cmd, @"^(.+?\.(exe|com|bat|cmd|vbs|js|ps1|scr|dll))\b", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value : cmd.Split(' ')[0];
        }

        static List<string> ScheduledTaskExes()
        {
            var res = new List<string>();
            try
            {
                var csv = Maint.RunCmd("schtasks", "/query /fo csv /v /nh");
                foreach (var line in csv.Split('\n'))
                {
                    var cols = Regex.Matches(line, "\"((?:[^\"]|\"\")*)\"").Select(m => m.Groups[1].Value).ToList();
                    if (cols.Count < 9) continue;
                    var exe = ExeFromCommand(cols[8]);
                    if (exe != null && exe.Contains('\\')) res.Add(exe);
                }
            }
            catch { }
            return res;
        }

        static List<string> ProcessExes()
        {
            var res = new List<string>();
            try
            {
                using var mos = new System.Management.ManagementObjectSearcher("SELECT ExecutablePath FROM Win32_Process");
                foreach (System.Management.ManagementObject o in mos.Get()) if (o["ExecutablePath"] is string p && p != "") res.Add(p);
            }
            catch { }
            return res;
        }

        /// <summary>
        /// Быстрая проверка: автозагрузка, задачи планировщика, запущенные программы и свежие файлы в «Загрузках», на рабочем столе, во временных папках.
        /// Подписанные программы из системных папок пропускаем — там вирусы почти не живут, а проверка долгая.
        /// </summary>
        public static Task<List<Threat>> QuickScan(Action<string, int, int> progress, CancellationToken ct) => Task.Run(() =>
        {
            progress?.Invoke("Собираю список…", 0, 0);
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var targets = new List<string>();
            targets.AddRange(Maint.Startup().Select(s => ExeFromCommand(s.Command)).Where(p => p != null));
            targets.AddRange(ScheduledTaskExes());
            targets.AddRange(ProcessExes());
            foreach (var d in new[] { Path.Combine(home, "Downloads"), Environment.GetFolderPath(Environment.SpecialFolder.Desktop), Path.GetTempPath(),
                                      Environment.GetFolderPath(Environment.SpecialFolder.Startup), Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup),
                                      Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) })
                targets.AddRange(RecentRisky(d, d.EndsWith("Roaming") ? 30 : 90, 400));
            var list = targets.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return ScanList(list, progress, ct, skipTrustedSystem: true);
        });

        public static Task<List<Threat>> ScanFolder(string dir, Action<string, int, int> progress, CancellationToken ct) => Task.Run(() =>
        {
            progress?.Invoke("Собираю список…", 0, 0);
            var list = new List<string>();
            try
            {
                foreach (var f in new DirectoryInfo(dir).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.Offline }))
                {
                    if (Risky.Contains(f.Extension.ToLowerInvariant())) list.Add(f.FullName);
                    if (list.Count >= 20000 || ct.IsCancellationRequested) break;
                }
            }
            catch { }
            return ScanList(list, progress, ct, skipTrustedSystem: false);
        });

        static List<Threat> ScanList(List<string> list, Action<string, int, int> progress, CancellationToken ct, bool skipTrustedSystem)
        {
            var found = new List<Threat>();
            int i = 0;
            foreach (var path in list)
            {
                if (ct.IsCancellationRequested) break;
                i++;
                if (i % 5 == 1) progress?.Invoke(path, i, list.Count);
                try
                {
                    if (skipTrustedSystem && InSystemDirs(path) && Osint.Signature(path).Hr is 0 or 1) continue;
                    var t = ScanFile(path);
                    if (t.Score >= 40) found.Add(t);
                }
                catch { }
            }
            S.AvLastScan = DateTime.Now.ToString("o");
            S.AvLastCount = list.Count;
            Note($"Проверено файлов: {i}, найдено: {found.Count}");
            return found.OrderByDescending(t => t.Score).ToList();
        }

        // ------------------------------------------------------------------
        //  Карантин
        // ------------------------------------------------------------------

        static readonly byte[] QKey = Encoding.ASCII.GetBytes("N-Island quarantine: not executable");
        static string QIndex => Path.Combine(QDir, "index.json");

        public static List<QuarantineItem> Quarantined()
        {
            try { return File.Exists(QIndex) ? JsonSerializer.Deserialize<List<QuarantineItem>>(File.ReadAllText(QIndex)) ?? new() : new(); }
            catch { return new(); }
        }

        static void SaveQ(List<QuarantineItem> l) { Directory.CreateDirectory(QDir); File.WriteAllText(QIndex, JsonSerializer.Serialize(l)); }

        static byte[] Xor(byte[] d) { var r = new byte[d.Length]; for (int i = 0; i < d.Length; i++) r[i] = (byte)(d[i] ^ QKey[i % QKey.Length]); return r; }

        /// <summary>Убрать файл в карантин: содержимое шифруется (запустить нельзя), оригинал удаляется. Вернуть можно в любой момент.</summary>
        public static string Quarantine(string path, string reason)
        {
            try
            {
                var data = File.ReadAllBytes(path);
                var id = Guid.NewGuid().ToString("N")[..12];
                Directory.CreateDirectory(QDir);
                File.WriteAllBytes(Path.Combine(QDir, id + ".q"), Xor(data));
                File.Delete(path);
                var l = Quarantined();
                l.Insert(0, new QuarantineItem(id, path, Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant(), reason, DateTime.Now, data.Length));
                SaveQ(l);
                Note("В карантин: " + path);
                Changed?.Invoke();
                return null;
            }
            catch (UnauthorizedAccessException) { return "Нет прав на этот файл — запусти N-Island от администратора или удали файл вручную"; }
            catch (IOException e) { return "Файл занят (программа запущена?) — закрой её и повтори. " + e.Message; }
        }

        public static string Restore(string id)
        {
            var l = Quarantined();
            var it = l.FirstOrDefault(x => x.Id == id);
            if (it == null) return "Нет такой записи";
            try
            {
                var target = it.Original;
                if (File.Exists(target)) target = Path.Combine(Path.GetDirectoryName(target), Path.GetFileNameWithoutExtension(target) + " (восстановлен)" + Path.GetExtension(target));
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.WriteAllBytes(target, Xor(File.ReadAllBytes(Path.Combine(QDir, id + ".q"))));
                File.Delete(Path.Combine(QDir, id + ".q"));
                l.Remove(it);
                SaveQ(l);
                Allow(it.Sha256);   // раз вернули — больше не трогаем
                Note("Восстановлен: " + target);
                Changed?.Invoke();
                return null;
            }
            catch (Exception e) { return e.Message; }
        }

        public static void DeleteForever(string id)
        {
            var l = Quarantined();
            l.RemoveAll(x => x.Id == id);
            try { File.Delete(Path.Combine(QDir, id + ".q")); } catch { }
            SaveQ(l);
            Changed?.Invoke();
        }

        // ------------------------------------------------------------------
        //  Защита в реальном времени
        // ------------------------------------------------------------------

        static readonly List<FileSystemWatcher> Watchers = new();
        static System.Windows.Threading.DispatcherTimer _runWatch;
        static HashSet<string> _runKnown;
        static readonly Dictionary<string, DateTime> Pending = new(StringComparer.OrdinalIgnoreCase);
        public static Action<Threat> OnThreat;
        public static Action<string, string> OnNewAutorun;

        public static void ApplyRealtime()
        {
            foreach (var w in Watchers) { w.EnableRaisingEvents = false; w.Dispose(); }
            Watchers.Clear();
            _runWatch?.Stop();
            if (!S.AvRealtime) return;
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            foreach (var d in new[] { Path.Combine(home, "Downloads"), Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                                      Environment.GetFolderPath(Environment.SpecialFolder.Startup), Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup) }.Where(Directory.Exists).Distinct())
            {
                try
                {
                    var w = new FileSystemWatcher(d) { IncludeSubdirectories = !d.Contains("Startup"), NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size };
                    w.Created += (_, e) => Queue(e.FullPath);
                    w.Renamed += (_, e) => Queue(e.FullPath);
                    w.Changed += (_, e) => Queue(e.FullPath);
                    w.EnableRaisingEvents = true;
                    Watchers.Add(w);
                }
                catch (Exception e) { Log.Info("av watch: " + e.Message); }
            }
            if (S.AvWatchStartup)
            {
                _runKnown = Maint.Startup().Select(s => s.Name + "|" + s.Command).ToHashSet();
                _runWatch = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(45) };
                _runWatch.Tick += (_, __) =>
                {
                    var now = Maint.Startup();
                    foreach (var s in now.Where(s => !_runKnown.Contains(s.Name + "|" + s.Command)))
                    {
                        OnNewAutorun?.Invoke(s.Name, s.Command);
                        var exe = ExeFromCommand(s.Command);
                        if (exe != null && File.Exists(exe)) Queue(exe, force: true);
                    }
                    _runKnown = now.Select(s => s.Name + "|" + s.Command).ToHashSet();
                };
                _runWatch.Start();
            }
        }

        static void Queue(string path, bool force = false)
        {
            if (!force && !Risky.Contains(Path.GetExtension(path).ToLowerInvariant())) return;
            lock (Pending)
            {
                bool first = !Pending.ContainsKey(path);
                Pending[path] = DateTime.Now;
                if (!first) return;
            }
            // ждём, пока файл докачается (размер перестанет меняться)
            _ = Task.Run(async () =>
            {
                while (true)
                {
                    await Task.Delay(2500);
                    lock (Pending) { if ((DateTime.Now - Pending[path]).TotalSeconds >= 2.4) { Pending.Remove(path); break; } }
                }
                try
                {
                    if (!File.Exists(path) || path.EndsWith(".crdownload") || path.EndsWith(".part") || path.EndsWith(".tmp")) return;
                    var t = ScanFile(path);
                    Note($"Проверен {Path.GetFileName(path)}: {t.Verdict} ({t.Score})");
                    if (t.Score >= 40) OnThreat?.Invoke(t);
                }
                catch { }
            });
        }

        // ------------------------------------------------------------------
        //  Аудит безопасности Windows
        // ------------------------------------------------------------------

        public static Task<List<(int Level, string Title, string Advice)>> Audit() => Task.Run(() =>
        {
            var r = new List<(int, string, string)>();
            try
            {
                using var mos = new System.Management.ManagementObjectSearcher(@"root\Microsoft\Windows\Defender", "SELECT AntivirusEnabled, RealTimeProtectionEnabled, AntivirusSignatureAge, IsTamperProtected FROM MSFT_MpComputerStatus");
                foreach (System.Management.ManagementObject o in mos.Get())
                {
                    if (o["AntivirusEnabled"] is false) r.Add((1, "Защитник Windows выключен", "Похоже, работает другой антивирус. Если его нет — включи Защитник в «Безопасности Windows»"));
                    else if (o["RealTimeProtectionEnabled"] is false) r.Add((2, "Защита в реальном времени Защитника выключена", "Включи её в «Безопасность Windows → Защита от вирусов и угроз»"));
                    else r.Add((0, "Защитник Windows работает ✓", null));
                    if (o["AntivirusSignatureAge"] is uint age && age > 7) r.Add((1, $"Базы Защитника не обновлялись {age} дн.", "Открой «Безопасность Windows» и нажми «Проверить наличие обновлений»"));
                    if (o["IsTamperProtected"] is false) r.Add((1, "Защита от подделки выключена", "Включи её — иначе вирус может отключить Защитник"));
                }
            }
            catch { r.Add((1, "Не удалось узнать состояние Защитника Windows", null)); }
            var fw = Maint.RunCmd("netsh", "advfirewall show allprofiles state");
            if (Regex.IsMatch(fw, @"\bOFF\b|ВЫКЛ|ВИМК", RegexOptions.IgnoreCase)) r.Add((2, "Брандмауэр выключен хотя бы для одной сети", "Включи брандмауэр Windows для всех профилей"));
            else if (fw.Length > 0) r.Add((0, "Брандмауэр включён ✓", null));
            try
            {
                using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System");
                if (k?.GetValue("EnableLUA") is int lua && lua == 0) r.Add((2, "Контроль учётных записей (UAC) выключен", "Любая программа получает права администратора без спроса. Включи UAC"));
                else if (k?.GetValue("ConsentPromptBehaviorAdmin") is int cp && cp == 0) r.Add((1, "UAC не спрашивает подтверждения", "Подними ползунок UAC хотя бы на уровень по умолчанию"));
            }
            catch { }
            try
            {
                using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer");
                if ((k?.GetValue("SmartScreenEnabled") as string)?.Equals("Off", StringComparison.OrdinalIgnoreCase) == true) r.Add((1, "SmartScreen выключен", "Он предупреждает о скачанных неизвестных программах — включи его"));
            }
            catch { }
            try
            {
                var hosts = File.ReadAllLines(Path.Combine(Environment.SystemDirectory, @"drivers\etc\hosts"));
                var guard = new[] { "microsoft.com", "windowsupdate", "update.microsoft", "defender", "virustotal", "kaspersky", "eset", "malwarebytes", "google.com", "bank", "privat24", "monobank", "paypal" };
                var bad = hosts.Select(l => l.Split('#')[0].Trim()).Where(l => l != "").Select(l => Regex.Split(l, @"\s+")).Where(p => p.Length >= 2)
                    .Where(p => guard.Any(g => p[1].Contains(g, StringComparison.OrdinalIgnoreCase))).Select(p => p[1] + " → " + p[0]).ToList();
                if (bad.Count > 0) r.Add((2, "В файле hosts перенаправлены важные сайты", "Так вирусы блокируют антивирусы или подменяют банки: " + string.Join(", ", bad.Take(4))));
            }
            catch { }
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings");
                if (k?.GetValue("AutoConfigURL") is string pac && pac != "") r.Add((1, "Включён прокси по скрипту (PAC)", "Весь трафик идёт через " + pac + ". Если ты это не ставил — выключи в Параметрах → Сеть → Прокси"));
                else if (k?.GetValue("ProxyEnable") is int pe && pe == 1) r.Add((1, "Включён прокси-сервер: " + k.GetValue("ProxyServer"), "Если это не твой VPN или работа — выключи его"));
            }
            catch { }
            try
            {
                using var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Terminal Server");
                if (k?.GetValue("fDenyTSConnections") is int deny && deny == 0) r.Add((1, "Включён удалённый рабочий стол (RDP)", "Если ты не подключаешься к этому ПК удалённо — выключи: это частая цель атак"));
            }
            catch { }
            int unsigned = Maint.Startup().Select(s => ExeFromCommand(s.Command)).Where(p => p != null && File.Exists(p) && !InSystemDirs(p)).Count(p => Osint.Signature(p).Hr is not (0 or 1));
            if (unsigned > 0) r.Add((1, $"В автозагрузке программ без подписи: {unsigned}", "Запусти «Быструю проверку» — она проверит их"));
            return r.OrderByDescending(x => x.Item1).ToList();
        });

        // ------------------------------------------------------------------
        //  Журнал
        // ------------------------------------------------------------------

        public static readonly List<(DateTime At, string Text)> History = new();

        static void Note(string text)
        {
            lock (History) { History.Insert(0, (DateTime.Now, text)); if (History.Count > 80) History.RemoveAt(History.Count - 1); }
            Changed?.Invoke();
        }

        public static void Start()
        {
            LoadDb();
            ApplyRealtime();
            // база старше недели — обновим фоном
            if (DbUpdated is not DateTime d || (DateTime.Now - d).TotalDays > 7) _ = UpdateDb();
        }
    }
}
