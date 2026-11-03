using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace NIsland.Services
{
    /// <summary>
    /// OSINT 1.7: проверка файла (настоящий тип, подпись, откуда скачан), разбор заголовков письма, проверка почтового адреса,
    /// технологии сайта, открытые порты, производитель устройства по MAC, невидимые символы и подмена букв в тексте.
    /// </summary>
    public static partial class Osint
    {
        /// <summary>Файл из меню Проводника «Проверить в N-Island» — раздел OSINT откроется сразу с ним.</summary>
        public static string PendingFile;

        // ------------------------------------------------------------------
        //  Файл: что это на самом деле
        // ------------------------------------------------------------------

        static readonly (byte[] Magic, int At, string Type, string[] Ext)[] Signatures =
        {
            (new byte[] { 0x4D, 0x5A }, 0, "программа Windows (EXE/DLL)", new[] { "exe", "dll", "sys", "scr", "com", "cpl", "ocx", "msi" }),
            (new byte[] { 0x25, 0x50, 0x44, 0x46 }, 0, "PDF-документ", new[] { "pdf" }),
            (new byte[] { 0x50, 0x4B, 0x03, 0x04 }, 0, "ZIP-архив (или документ Office, APK, JAR)", new[] { "zip", "docx", "xlsx", "pptx", "apk", "jar", "odt", "epub", "xpi", "vsix", "nupkg", "3mf" }),
            (new byte[] { 0x52, 0x61, 0x72, 0x21 }, 0, "RAR-архив", new[] { "rar" }),
            (new byte[] { 0x37, 0x7A, 0xBC, 0xAF }, 0, "7-Zip-архив", new[] { "7z" }),
            (new byte[] { 0xFF, 0xD8, 0xFF }, 0, "картинка JPEG", new[] { "jpg", "jpeg", "jfif" }),
            (new byte[] { 0x89, 0x50, 0x4E, 0x47 }, 0, "картинка PNG", new[] { "png" }),
            (new byte[] { 0x47, 0x49, 0x46, 0x38 }, 0, "картинка GIF", new[] { "gif" }),
            (new byte[] { 0x49, 0x44, 0x33 }, 0, "аудио MP3", new[] { "mp3" }),
            (new byte[] { 0x66, 0x74, 0x79, 0x70 }, 4, "видео/аудио MP4", new[] { "mp4", "m4a", "mov", "heic", "m4v", "3gp" }),
            (new byte[] { 0xD0, 0xCF, 0x11, 0xE0 }, 0, "старый документ Office (DOC/XLS) или MSI", new[] { "doc", "xls", "ppt", "msi", "msg" }),
            (new byte[] { 0x7F, 0x45, 0x4C, 0x46 }, 0, "программа Linux (ELF)", new[] { "" }),
            (new byte[] { 0x4C, 0x00, 0x00, 0x00 }, 0, "ярлык Windows (LNK)", new[] { "lnk" }),
            (new byte[] { 0x1F, 0x8B }, 0, "архив GZIP", new[] { "gz", "tgz" }),
            (new byte[] { 0x52, 0x49, 0x46, 0x46 }, 0, "RIFF (WAV, AVI, WEBP)", new[] { "wav", "avi", "webp" }),
            (new byte[] { 0x7B, 0x5C, 0x72, 0x74 }, 0, "документ RTF", new[] { "rtf", "doc" }),
        };

        static readonly string[] Dangerous = { "exe", "scr", "com", "bat", "cmd", "pif", "vbs", "vbe", "js", "jse", "wsf", "wsh", "ps1", "hta", "msi", "lnk", "cpl", "jar", "dll", "reg", "iso", "img", "vhd" };

        [DllImport("wintrust.dll", CharSet = CharSet.Unicode)]
        static extern int WinVerifyTrust(IntPtr hwnd, ref Guid action, ref WINTRUST_DATA data);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct WINTRUST_FILE_INFO { public int cbStruct; public string pcwszFilePath; public IntPtr hFile; public IntPtr pgKnownSubject; }

        [StructLayout(LayoutKind.Sequential)]
        struct WINTRUST_DATA
        {
            public int cbStruct; public IntPtr pPolicyCallbackData, pSIPClientData; public int dwUIChoice, fdwRevocationChecks, dwUnionChoice;
            public IntPtr pFile; public int dwStateAction; public IntPtr hWVTStateData; public IntPtr pwszURLReference; public int dwProvFlags, dwUIContext; public IntPtr pSignatureSettings;
        }

        /// <summary>Подпись Authenticode: 0 — подписан и подпись верна, иначе код ошибки (не подписан, подделан, отозван).</summary>
        static int VerifySignature(string path)
        {
            var fi = new WINTRUST_FILE_INFO { cbStruct = Marshal.SizeOf<WINTRUST_FILE_INFO>(), pcwszFilePath = path };
            var pFile = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
            try
            {
                Marshal.StructureToPtr(fi, pFile, false);
                var data = new WINTRUST_DATA
                {
                    cbStruct = Marshal.SizeOf<WINTRUST_DATA>(), dwUIChoice = 2 /*без окон*/, fdwRevocationChecks = 0, dwUnionChoice = 1 /*файл*/,
                    pFile = pFile, dwStateAction = 0, dwProvFlags = 0x80 /*кэш отзыва, без сети*/,
                };
                var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");   // WINTRUST_ACTION_GENERIC_VERIFY_V2
                return WinVerifyTrust(new IntPtr(-1), ref action, ref data);
            }
            finally { Marshal.DestroyStructure<WINTRUST_FILE_INFO>(pFile); Marshal.FreeHGlobal(pFile); }
        }

        [DllImport("wintrust.dll", SetLastError = true)] static extern bool CryptCATAdminAcquireContext2(out IntPtr admin, IntPtr subsystem, [MarshalAs(UnmanagedType.LPWStr)] string hashAlg, IntPtr policy, int flags);
        [DllImport("wintrust.dll", SetLastError = true)] static extern bool CryptCATAdminCalcHashFromFileHandle2(IntPtr admin, IntPtr file, ref int size, byte[] hash, int flags);
        [DllImport("wintrust.dll", SetLastError = true)] static extern IntPtr CryptCATAdminEnumCatalogFromHash(IntPtr admin, byte[] hash, int size, int flags, IntPtr prev);
        [DllImport("wintrust.dll")] static extern bool CryptCATAdminReleaseCatalogContext(IntPtr admin, IntPtr info, int flags);
        [DllImport("wintrust.dll")] static extern bool CryptCATAdminReleaseContext(IntPtr admin, int flags);

        /// <summary>
        /// Системные файлы Windows подписаны не внутри себя, а в каталоге (.cat) — ищем отпечаток файла в каталогах системы.
        /// </summary>
        static bool InSystemCatalog(string path)
        {
            foreach (var alg in new[] { "SHA256", null })
            {
                if (!CryptCATAdminAcquireContext2(out var admin, IntPtr.Zero, alg, IntPtr.Zero, 0)) continue;
                try
                {
                    using var fs = File.OpenRead(path);
                    var h = fs.SafeFileHandle.DangerousGetHandle();
                    int size = 0;
                    CryptCATAdminCalcHashFromFileHandle2(admin, h, ref size, null, 0);
                    if (size <= 0 || size > 128) continue;
                    var hash = new byte[size];
                    if (!CryptCATAdminCalcHashFromFileHandle2(admin, h, ref size, hash, 0)) continue;
                    var info = CryptCATAdminEnumCatalogFromHash(admin, hash, size, 0, IntPtr.Zero);
                    if (info != IntPtr.Zero) { CryptCATAdminReleaseCatalogContext(admin, info, 0); return true; }
                }
                catch { }
                finally { CryptCATAdminReleaseContext(admin, 0); }
            }
            return false;
        }

        public record FileReport(List<(string K, string V)> Items, List<(bool Bad, string Text)> Signals, string Sha256);

        /// <summary>
        /// Проверка файла без отправки куда-либо: настоящий тип по первым байтам против расширения, двойное расширение
        /// («отчёт.pdf.exe»), цифровая подпись и издатель, отметка «скачан из интернета» и адрес, откуда скачан.
        /// </summary>
        public static FileReport CheckFile(string path)
        {
            var items = new List<(string, string)>();
            var sig = new List<(bool, string)>();
            var info = new FileInfo(path);
            var name = info.Name;
            var ext = info.Extension.TrimStart('.').ToLowerInvariant();
            items.Add(("Файл", name));
            items.Add(("Размер", Maint.Size(info.Length)));
            items.Add(("Изменён", info.LastWriteTime.ToString("d MMMM yyyy, HH:mm", Core.Fmt.Ru)));

            var head = new byte[16];
            using (var fs = File.OpenRead(path)) fs.Read(head, 0, head.Length);
            var real = Signatures.FirstOrDefault(s => head.Length >= s.At + s.Magic.Length && s.Magic.Select((b, i) => head[s.At + i] == b).All(x => x));
            items.Add(("Что внутри на самом деле", real.Type ?? "не распознано (текст или редкий формат)"));
            if (real.Type != null && !real.Ext.Contains(ext) && !(real.Ext.Contains("") && ext == ""))
            {
                bool exeInside = real.Type.StartsWith("программа");
                sig.Add((true, exeInside ? $"Внутри — программа, а расширение «.{ext}». Так маскируют вирусы под документы и картинки. Не запускай!"
                                         : $"Расширение «.{ext}» не совпадает с содержимым ({real.Type}) — файл переименован."));
            }
            else if (real.Type != null) sig.Add((false, "Расширение совпадает с содержимым"));

            var parts = name.Split('.');
            if (parts.Length >= 3 && Dangerous.Contains(ext) && parts[^2].ToLowerInvariant() is "pdf" or "doc" or "docx" or "xls" or "xlsx" or "jpg" or "jpeg" or "png" or "txt" or "mp4" or "mp3" or "zip" or "rar")
                sig.Add((true, $"Двойное расширение «.{parts[^2]}.{ext}» — классическая уловка: Windows прячет последнее расширение, и программа выглядит как документ."));
            if (name.Any(c => c == '‮' || c == '‭'))
                sig.Add((true, "В имени спрятан символ разворота текста (RTL) — настоящее расширение показано задом наперёд."));
            if (Dangerous.Contains(ext)) sig.Add((false, $"Тип «.{ext}» может выполнять команды — запускай, только если доверяешь источнику."));

            // цифровая подпись
            if (ext is "exe" or "dll" or "msi" or "sys" or "ps1" or "cab" or "cat" or "appx" or "msix" || (real.Type?.StartsWith("программа") ?? false))
            {
                int hr = VerifySignature(path);
                if (hr == unchecked((int)0x800B0100) && InSystemCatalog(path))
                {
                    items.Add(("Цифровая подпись", "подписан в каталоге Windows ✓ (системный файл)"));
                    sig.Add((false, "Файл есть в каталоге подписей Windows — это оригинальный системный файл."));
                    hr = 1;   // подпись уже описали
                }
                string signer = null;
                try { signer = new X509Certificate2(X509Certificate.CreateFromSignedFile(path)).GetNameInfo(X509NameType.SimpleName, false); } catch { }
                string state = hr switch
                {
                    0 => "подписан, подпись верна ✓",
                    unchecked((int)0x800B0100) => "не подписан",
                    unchecked((int)0x80096010) => "ПОДПИСЬ НЕВЕРНА — файл изменён после подписи",
                    unchecked((int)0x800B010C) => "сертификат отозван",
                    unchecked((int)0x800B0101) => "сертификат истёк",
                    unchecked((int)0x800B0109) => "подписан неизвестным (самодельным) сертификатом",
                    _ => "не удалось проверить (0x" + hr.ToString("X8") + ")",
                };
                if (hr != 1) items.Add(("Цифровая подпись", state));
                if (signer != null) items.Add(("Издатель", signer));
                if (hr == 0) sig.Add((false, "Программа подписана" + (signer != null ? " издателем «" + signer + "»" : "") + " — её не меняли после выпуска."));
                else if (hr == 1) { }
                else if (hr == unchecked((int)0x800B0100)) sig.Add((true, "Программа без цифровой подписи. Это не всегда вирус, но от крупных разработчиков такого обычно не бывает."));
                else sig.Add((true, "С подписью проблема: " + state + "."));
            }

            // отметка «из интернета» (Zone.Identifier) — Windows пишет, откуда скачан файл
            try
            {
                var ads = path + ":Zone.Identifier";
                var zone = File.ReadAllLines(ads);
                string Get(string k) => zone.FirstOrDefault(l => l.StartsWith(k + "=", StringComparison.OrdinalIgnoreCase))?[(k.Length + 1)..];
                var z = Get("ZoneId");
                items.Add(("Откуда", z switch { "3" => "скачан из интернета", "4" => "скачан с опасного сайта", "1" => "из локальной сети", "2" => "с доверенного сайта", _ => "зона " + z }));
                if (Get("HostUrl") is { Length: > 0 } host) items.Add(("Скачан по адресу", host));
                if (Get("ReferrerUrl") is { Length: > 0 } refr) items.Add(("Со страницы", refr));
            }
            catch { items.Add(("Откуда", "нет отметки о скачивании (создан на этом ПК, распакован или скопирован)")); }

            string hash = null;
            try { hash = Sha256(path); items.Add(("SHA-256", hash)); } catch { }
            return new FileReport(items, sig, hash);
        }

        // ------------------------------------------------------------------
        //  Заголовки письма: откуда оно на самом деле
        // ------------------------------------------------------------------

        /// <summary>Разбор заголовков письма (Gmail: «Показать оригинал»; Outlook: «Просмотреть исходный код»).</summary>
        public static (List<(string K, string V)> Items, List<(bool Bad, string Text)> Signals) EmailHeaders(string raw)
        {
            var items = new List<(string, string)>();
            var sig = new List<(bool, string)>();
            // склеиваем перенесённые строки заголовков
            var unfolded = Regex.Replace((raw ?? "").Replace("\r", "").TrimStart('\n', ' ', '\t'), @"\n[ \t]+", " ");
            // заголовки заканчиваются первой пустой строкой — дальше уже текст письма
            var headers = unfolded.Split('\n').TakeWhile(l => l.Trim().Length > 0)
                .Select(l => { int i = l.IndexOf(':'); return i > 0 ? (l[..i].Trim(), l[(i + 1)..].Trim()) : (null, null); })
                .Where(h => h.Item1 != null).ToList();
            string H(string k) => headers.FirstOrDefault(h => h.Item1.Equals(k, StringComparison.OrdinalIgnoreCase)).Item2;
            IEnumerable<string> All(string k) => headers.Where(h => h.Item1.Equals(k, StringComparison.OrdinalIgnoreCase)).Select(h => h.Item2);
            if (headers.Count < 3) { sig.Add((true, "Это не похоже на заголовки письма. Скопируй «исходный текст» / «оригинал» письма целиком.")); return (items, sig); }

            string Addr(string s) => s == null ? null : Regex.Match(s, @"<([^>]+)>").Success ? Regex.Match(s, @"<([^>]+)>").Groups[1].Value : s.Trim();
            string Dom(string a) => a != null && a.Contains('@') ? a[(a.LastIndexOf('@') + 1)..].ToLowerInvariant().Trim('>', ' ') : null;
            var from = H("From"); var ret = H("Return-Path"); var reply = H("Reply-To");
            items.Add(("От кого", Decode(from) ?? "—"));
            if (H("Subject") is { } subj) items.Add(("Тема", Decode(subj)));
            if (H("Date") is { } date) items.Add(("Дата", date));
            if (ret != null) items.Add(("Ответы о доставке (Return-Path)", ret));
            if (reply != null) items.Add(("Ответ уйдёт на", Decode(reply)));
            string fd = Dom(Addr(from)), rd = Dom(Addr(ret)), rpd = Dom(Addr(reply));
            if (rpd != null && fd != null && Registrable(rpd) != Registrable(fd))
                sig.Add((true, $"Ответ уйдёт на другой домен ({rpd}), чем отправитель ({fd}) — так делают мошенники, чтобы переписка шла им."));
            if (rd != null && fd != null && Registrable(rd) != Registrable(fd))
                sig.Add((false, $"Отправлено через сервис {rd} (рассылки так делают нормально, но проверь, что ты ждёшь письмо от {fd})."));

            // проверки подлинности
            var auth = string.Join(" ", All("Authentication-Results").Concat(All("ARC-Authentication-Results")));
            foreach (var k in new[] { "spf", "dkim", "dmarc" })
            {
                var m = Regex.Match(auth, k + @"=(\w+)", RegexOptions.IgnoreCase);
                if (!m.Success) continue;
                var v = m.Groups[1].Value.ToLowerInvariant();
                items.Add((k.ToUpperInvariant(), v));
                if (v is "fail" or "softfail" or "permerror") sig.Add((true, $"{k.ToUpperInvariant()} не прошёл ({v}) — письмо могло быть отправлено не тем, за кого себя выдаёт."));
                else if (v == "pass") sig.Add((false, $"{k.ToUpperInvariant()} пройден"));
            }

            // путь письма: Received снизу вверх
            var received = All("Received").Reverse().ToList();
            var hops = new List<string>();
            DateTime? first = null, last = null;
            string origin = null;
            foreach (var r in received)
            {
                var fromHost = Regex.Match(r, @"from\s+(\S+)").Groups[1].Value;
                var ip = Regex.Match(r, @"\[(\d{1,3}(?:\.\d{1,3}){3}|[0-9a-f:]{6,})\]", RegexOptions.IgnoreCase).Groups[1].Value;
                var by = Regex.Match(r, @"by\s+(\S+)").Groups[1].Value.TrimEnd(';', ',');
                if (fromHost.Trim('[', ']') == ip) fromHost = "";
                int semi = r.LastIndexOf(';');
                DateTime? at = null;
                if (semi > 0 && DateTime.TryParse(Regex.Replace(r[(semi + 1)..], @"\s*\(.*\)\s*$", "").Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var dt)) at = dt;
                first ??= at; if (at != null) last = at;
                if (origin == null && ip != "" && !ip.StartsWith("10.") && !ip.StartsWith("192.168.") && !ip.StartsWith("127.") && !Regex.IsMatch(ip, @"^172\.(1[6-9]|2\d|3[01])\.")) origin = ip;
                hops.Add($"{(fromHost != "" ? fromHost + " " : "")}{(ip != "" ? "[" + ip + "]" : fromHost == "" ? "?" : "")} → {(by != "" ? by : "?")}{(at != null ? " · " + at.Value.ToLocalTime().ToString("HH:mm:ss") : "")}");
            }
            if (hops.Count > 0) items.Add(("Путь письма", string.Join("\n", hops)));
            if (origin != null) items.Add(("IP отправителя", origin + " — проверь его в разделе «IP-адрес»"));
            if (first != null && last != null) items.Add(("Шло", Math.Max(0, (last.Value - first.Value).TotalSeconds) < 120 ? $"{Math.Max(0, (last.Value - first.Value).TotalSeconds):0} с" : $"{(last.Value - first.Value).TotalMinutes:0} мин"));
            if (H("X-Mailer") is { } mailer) items.Add(("Почтовая программа", mailer));
            return (items, sig);
        }

        /// <summary>Заголовки в кодировке «=?UTF-8?B?…?=» → обычный текст.</summary>
        public static string Decode(string s)
        {
            if (s == null) return null;
            return Regex.Replace(s, @"=\?([^?]+)\?([BbQq])\?([^?]*)\?=", m =>
            {
                try
                {
                    var enc = Encoding.GetEncoding(m.Groups[1].Value);
                    var data = m.Groups[2].Value.ToUpperInvariant() == "B"
                        ? Convert.FromBase64String(m.Groups[3].Value)
                        : QDecode(m.Groups[3].Value);
                    return enc.GetString(data);
                }
                catch { return m.Value; }
            }).Replace("?= =?", "");
        }

        static byte[] QDecode(string s)
        {
            var bytes = new List<byte>();
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '_') bytes.Add(32);
                else if (s[i] == '=' && i + 2 < s.Length && byte.TryParse(s.AsSpan(i + 1, 2), NumberStyles.HexNumber, null, out var b)) { bytes.Add(b); i += 2; }
                else bytes.Add((byte)s[i]);
            }
            return bytes.ToArray();
        }

        // ------------------------------------------------------------------
        //  Почтовый адрес: существует ли домен, принимает ли почту, одноразовый ли
        // ------------------------------------------------------------------

        static readonly HashSet<string> Disposable = new(StringComparer.OrdinalIgnoreCase)
        {
            "mailinator.com", "10minutemail.com", "guerrillamail.com", "guerrillamail.net", "sharklasers.com", "temp-mail.org", "tempmail.com", "yopmail.com",
            "trashmail.com", "getnada.com", "dispostable.com", "maildrop.cc", "throwawaymail.com", "fakeinbox.com", "mohmal.com", "emailondeck.com",
            "mintemail.com", "tempr.email", "mail.tm", "moakt.com", "1secmail.com", "spamgourmet.com", "burnermail.io", "tempmailo.com", "emailfake.com",
            "mytemp.email", "inboxkitten.com", "harakirimail.com", "mailnesia.com", "tmpmail.org", "tmail.ws", "linshiyouxiang.net", "dropmail.me",
        };

        public static async Task<List<(string K, string V)>> EmailCheck(string email)
        {
            email = (email ?? "").Trim();
            var res = new List<(string, string)>();
            var m = Regex.Match(email, @"^[^@\s]+@([^@\s]+\.[^@\s]{2,})$");
            if (!m.Success) { res.Add(("Адрес", "написан с ошибкой — должно быть имя@домен.зона")); return res; }
            var dom = m.Groups[1].Value.ToLowerInvariant();
            res.Add(("Домен", dom));
            res.Add(("Одноразовая почта", Disposable.Contains(dom) || Disposable.Contains(Registrable(dom)) ? "ДА — такие ящики живут минуты, ими пользуются для спама и обмана" : "не похоже"));
            async Task<List<string>> Q(string name, string type)
            {
                using var doc = await Json($"https://cloudflare-dns.com/dns-query?name={Uri.EscapeDataString(name)}&type={type}", r => r.Headers.Accept.ParseAdd("application/dns-json"));
                return doc.RootElement.TryGetProperty("Answer", out var a) ? a.EnumerateArray().Select(x => S(x, "data").Trim('"')).ToList() : new();
            }
            var mx = await Q(dom, "MX");
            res.Add(("Принимает почту (MX)", mx.Count > 0 ? "да: " + string.Join(", ", mx.Take(3).Select(x => x.Split(' ').Last().TrimEnd('.'))) : "НЕТ — на этот адрес письма не дойдут"));
            var spf = (await Q(dom, "TXT")).FirstOrDefault(t => t.StartsWith("v=spf1"));
            res.Add(("SPF (кто может слать от имени домена)", spf ?? "нет — от имени домена легко подделать письмо"));
            var dmarc = (await Q("_dmarc." + dom, "TXT")).FirstOrDefault(t => t.StartsWith("v=DMARC"));
            res.Add(("DMARC (что делать с подделками)", dmarc == null ? "нет" : dmarc.Contains("p=reject") ? "отклонять ✓" : dmarc.Contains("p=quarantine") ? "в спам ✓" : "только наблюдать (p=none)"));
            if (await DomainAge(dom) is double days) res.Add(("Возраст домена", days < 60 ? $"{days:0} дн. — ОЧЕНЬ НОВЫЙ, будь осторожен" : days < 365 ? $"{days:0} дн." : $"{days / 365:0.#} лет"));
            return res;
        }

        // ------------------------------------------------------------------
        //  Сайт: на чём сделан, robots.txt, security.txt
        // ------------------------------------------------------------------

        static readonly (string Name, string Pattern, string Where)[] Tech =
        {
            ("WordPress", "wp-content|wp-includes", "html"), ("Joomla", "/media/jui/|joomla", "html"), ("Drupal", "drupal-settings-json|/sites/default/files", "html"),
            ("1С-Битрикс", "/bitrix/|bx-core", "html"), ("Tilda", "tildacdn|t-records", "html"), ("Wix", "wixstatic|_wixCssImports", "html"),
            ("Shopify", "cdn.shopify.com", "html"), ("Squarespace", "squarespace", "html"), ("Webflow", "webflow", "html"), ("Ghost", "ghost-sdk|content=\"Ghost", "html"),
            ("OpenCart", "catalog/view/theme", "html"), ("PrestaShop", "prestashop", "html"), ("Magento", "Mage.Cookies|/static/version", "html"), ("Horoshop", "horoshop", "html"),
            ("Next.js", "__NEXT_DATA__|/_next/", "html"), ("Nuxt", "__NUXT__|/_nuxt/", "html"), ("Angular", "ng-version", "html"), ("React", "data-reactroot|react-dom", "html"),
            ("Vue", "data-v-[0-9a-f]{6}|vue.runtime", "html"), ("Svelte", "svelte-", "html"), ("Astro", "astro-island|/_astro/", "html"), ("Gatsby", "___gatsby", "html"),
            ("jQuery", "jquery[.-]", "html"), ("Bootstrap", "bootstrap(\\.min)?\\.(css|js)", "html"), ("Tailwind", "tailwind", "html"),
            ("Google Analytics", "googletagmanager.com|google-analytics.com", "html"), ("Яндекс Метрика", "mc.yandex.ru|ym\\(", "html"),
            ("Facebook Pixel", "connect.facebook.net", "html"), ("Hotjar", "hotjar", "html"), ("TikTok Pixel", "analytics.tiktok.com", "html"),
            ("reCAPTCHA", "recaptcha", "html"), ("Cloudflare Turnstile", "challenges.cloudflare.com", "html"), ("Intercom", "intercom", "html"), ("Jivo", "jivosite|code.jivo", "html"),
        };

        public static async Task<List<(string K, string V)>> SiteTech(string input)
        {
            var url = Url(input);
            var res = new List<(string, string)>();
            using var resp = await Http.GetAsync(url);
            var html = await resp.Content.ReadAsStringAsync();
            if (html.Length > 600_000) html = html[..600_000];
            string Hd(string k) => resp.Headers.TryGetValues(k, out var v) ? string.Join(", ", v) : resp.Content.Headers.TryGetValues(k, out var c) ? string.Join(", ", c) : null;
            res.Add(("Адрес после переходов", resp.RequestMessage?.RequestUri?.ToString() ?? url));
            res.Add(("Ответ", (int)resp.StatusCode + " " + resp.ReasonPhrase));
            if (Hd("Server") is { } srv) res.Add(("Сервер", srv));
            if (Hd("X-Powered-By") is { } pb) res.Add(("Работает на", pb));
            var cdn = new List<string>();
            if (Hd("CF-RAY") != null) cdn.Add("Cloudflare");
            if (Hd("X-Amz-Cf-Id") != null) cdn.Add("Amazon CloudFront");
            if (Hd("X-Vercel-Id") != null) cdn.Add("Vercel");
            if (Hd("X-Served-By") is { } sb && sb.Contains("cache")) cdn.Add("Fastly");
            if (Hd("X-Akamai-Transformed") != null || (Hd("Server") ?? "").Contains("Akamai")) cdn.Add("Akamai");
            if (Hd("X-GitHub-Request-Id") != null) cdn.Add("GitHub Pages");
            if (Hd("X-Nf-Request-Id") != null) cdn.Add("Netlify");
            if (cdn.Count > 0) res.Add(("CDN / хостинг", string.Join(", ", cdn)));
            var gen = Regex.Match(html, @"<meta[^>]+name=[""']generator[""'][^>]+content=[""']([^""']+)", RegexOptions.IgnoreCase);
            if (gen.Success) res.Add(("Генератор", WebUtility.HtmlDecode(gen.Groups[1].Value)));
            var found = Tech.Where(t => Regex.IsMatch(html, t.Pattern, RegexOptions.IgnoreCase)).Select(t => t.Name).Distinct().ToList();
            res.Add(("Технологии", found.Count > 0 ? string.Join(", ", found) : "ничего известного не нашёл"));
            var title = Regex.Match(html, @"<title[^>]*>([^<]{1,200})", RegexOptions.IgnoreCase);
            if (title.Success) res.Add(("Заголовок", WebUtility.HtmlDecode(title.Groups[1].Value.Trim())));

            var baseUri = resp.RequestMessage?.RequestUri ?? new Uri(url);
            async Task<string> Small(string path)
            {
                try
                {
                    using var r = await Http.GetAsync(new Uri(baseUri, path));
                    if (!r.IsSuccessStatusCode) return null;
                    var t = await r.Content.ReadAsStringAsync();
                    return t.TrimStart().StartsWith("<") ? null : t;   // страница-заглушка вместо файла
                }
                catch { return null; }
            }
            var robots = await Small("/robots.txt");
            if (robots != null)
            {
                var dis = robots.Split('\n').Where(l => l.TrimStart().StartsWith("Disallow:", StringComparison.OrdinalIgnoreCase)).Select(l => l.Split(':', 2)[1].Trim()).Where(x => x != "").Distinct().Take(15).ToList();
                var maps = robots.Split('\n').Where(l => l.TrimStart().StartsWith("Sitemap:", StringComparison.OrdinalIgnoreCase)).Select(l => l.Split(':', 2)[1].Trim()).Take(3).ToList();
                res.Add(("robots.txt — скрыто от поисковиков", dis.Count > 0 ? string.Join("\n", dis) : "ничего"));
                if (maps.Count > 0) res.Add(("Карта сайта", string.Join("\n", maps)));
            }
            else res.Add(("robots.txt", "нет"));
            var sec = await Small("/.well-known/security.txt");
            res.Add(("security.txt (куда сообщать об уязвимостях)", sec != null ? Regex.Match(sec, @"Contact:\s*(\S+)", RegexOptions.IgnoreCase).Groups[1].Value is { Length: > 0 } c ? c : "есть" : "нет"));
            return res;
        }

        // ------------------------------------------------------------------
        //  Открытые порты одного адреса (свой роутер, свой сервер)
        // ------------------------------------------------------------------

        static readonly (int Port, string What, bool Risky)[] CommonPorts =
        {
            (21, "FTP", true), (22, "SSH", false), (23, "Telnet", true), (25, "почта SMTP", false), (53, "DNS", false), (80, "сайт HTTP", false), (110, "почта POP3", false),
            (139, "общие папки Windows (NetBIOS)", true), (143, "почта IMAP", false), (443, "сайт HTTPS", false), (445, "общие папки Windows (SMB)", true),
            (554, "камеры RTSP", true), (1433, "база MS SQL", true), (1900, "UPnP", true), (3306, "база MySQL", true), (3389, "удалённый рабочий стол (RDP)", true),
            (5432, "база PostgreSQL", true), (5900, "удалённый доступ VNC", true), (6379, "Redis", true), (7547, "управление роутером провайдером (TR-069)", true),
            (8080, "веб-панель / прокси", false), (8443, "веб-панель HTTPS", false), (8291, "MikroTik Winbox", true), (9100, "принтер", false), (27017, "MongoDB", true),
        };

        /// <summary>Какие из 25 популярных портов открыты. Подключение — обычное (как браузер), без сканирования сети целиком.</summary>
        public static async Task<List<(string K, string V)>> Ports(string input, Action<string> progress = null)
        {
            var host = HostOf(input);
            if (host == "") host = WifiTools.Gateway()?.ToString() ?? "";
            if (host == "") throw new InvalidOperationException("укажи адрес");
            var addrs = await Dns.GetHostAddressesAsync(host);
            var ip = addrs.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork) ?? addrs.First();
            int done = 0;
            var tasks = CommonPorts.Select(async p =>
            {
                using var tcp = new TcpClient(ip.AddressFamily);
                bool open;
                try
                {
                    var c = tcp.ConnectAsync(ip, p.Port);
                    open = await Task.WhenAny(c, Task.Delay(1800)) == c && tcp.Connected;
                }
                catch { open = false; }
                progress?.Invoke($"Проверяю порты… {System.Threading.Interlocked.Increment(ref done) * 100 / CommonPorts.Length}%");
                return (p, open);
            }).ToList();
            var results = await Task.WhenAll(tasks);
            var res = new List<(string, string)> { ("Адрес", host == ip.ToString() ? host : $"{host} ({ip})") };
            var open = results.Where(r => r.open).ToList();
            if (open.Count == 0) res.Add(("Открытые порты", "ни одного из популярных — снаружи всё закрыто ✓"));
            foreach (var (p, _) in open)
                res.Add(($"{p.Port}", p.What + (p.Risky ? " — ⚠ если это смотрит в интернет, лучше закрыть или спрятать за VPN" : "")));
            return res;
        }

        // ------------------------------------------------------------------
        //  MAC-адрес → производитель
        // ------------------------------------------------------------------

        public static async Task<string> MacVendor(string mac)
        {
            var clean = Regex.Replace(mac ?? "", "[^0-9A-Fa-f]", "");
            if (clean.Length < 6) throw new InvalidOperationException("MAC-адрес — 12 знаков, например 3C:22:FB:12:34:56");
            int first = Convert.ToInt32(clean[..2], 16);
            if ((first & 2) != 0) return "Случайный (частный) адрес — так телефоны и ноутбуки прячут настоящий MAC в чужих сетях";
            using var r = await Http.GetAsync("https://api.macvendors.com/" + clean[..6]);
            if (r.StatusCode == HttpStatusCode.NotFound) return "Производитель не найден в базе";
            r.EnsureSuccessStatusCode();
            return (await r.Content.ReadAsStringAsync()).Trim();
        }

        // ------------------------------------------------------------------
        //  Невидимые символы и подмена букв
        // ------------------------------------------------------------------

        public record TextReport(string Clean, List<string> Found);

        /// <summary>
        /// Находит невидимые символы (нулевой ширины, разворот текста), латиницу вперемешку с кириллицей в одном слове
        /// («раураl» с латинской a) — так обходят фильтры и подделывают ссылки и логины.
        /// </summary>
        public static TextReport HiddenChars(string text)
        {
            var found = new List<string>();
            var sb = new StringBuilder();
            int zw = 0, bidi = 0, nbsp = 0;
            foreach (var c in text ?? "")
            {
                if (c is '​' or '‌' or '‍' or '⁠' or '﻿' or '­') { zw++; continue; }
                if (c is >= '‪' and <= '‮' or >= '⁦' and <= '⁩') { bidi++; continue; }
                if (c is ' ' or ' ' or ' ') { nbsp++; sb.Append(' '); continue; }
                sb.Append(c);
            }
            if (zw > 0) found.Add($"Невидимых символов нулевой ширины: {zw} — ими помечают текст или обходят фильтры");
            if (bidi > 0) found.Add($"Символов разворота текста: {bidi} — ими маскируют расширения файлов и адреса");
            if (nbsp > 0) found.Add($"Неразрывных пробелов: {nbsp} (обычно безвредно)");
            foreach (Match w in Regex.Matches(sb.ToString(), @"[\p{L}]{3,}"))
            {
                bool lat = w.Value.Any(ch => ch is >= 'a' and <= 'z' or >= 'A' and <= 'Z');
                bool cyr = w.Value.Any(ch => ch is >= 'а' and <= 'я' or >= 'А' and <= 'Я' or 'ё' or 'Ё' or 'і' or 'ї' or 'є' or 'ґ' or 'І' or 'Ї' or 'Є' or 'Ґ');
                if (lat && cyr) found.Add($"«{w.Value}» — латиница и кириллица в одном слове (подмена букв)");
                if (found.Count > 25) break;
            }
            if (found.Count == 0) found.Add("Ничего подозрительного: невидимых символов и подмены букв нет ✓");
            return new TextReport(sb.ToString(), found);
        }
    }
}
