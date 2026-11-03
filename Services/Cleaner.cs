using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Win32;
using NIsland.Core;

namespace NIsland.Services
{
    /// <summary>Насколько безопасно действие — показывается цветной меткой рядом с каждым пунктом.</summary>
    public enum Safety { Safe, Caution, NotRecommended }

    /// <summary>Что можно почистить. Удаляется только содержимое папок из списка, сами папки остаются.</summary>
    public class CleanTarget
    {
        public string Key, Group, Title, Explain;
        public Safety Safety;
        public bool Admin;                 // папка системная — нужны права администратора (Windows спросит)
        public Func<IEnumerable<string>> Roots = () => Array.Empty<string>();
        public string Pattern = "*";
        public bool TopOnly;               // без вложенных папок
        public int MinAgeHours;            // свежие файлы не трогаем — их может использовать работающая программа
        public string[] Processes;         // если программа запущена — пропускаем целиком
        public Func<(long Bytes, int Items)> CustomScan;   // для следов в реестре и корзины
        public Func<int> CustomClean;
        public bool InfoOnly;              // «не рекомендуется» — только объяснение, удалять не даём

        public long Bytes; public int Files; public bool Scanned;
        public string Blocked => Processes?.FirstOrDefault(p => Process.GetProcessesByName(p).Length > 0);
    }

    public record CleanResult(long Freed, int Deleted, int Skipped, string Note);

    /// <summary>
    /// Глубокая очистка Windows. Правила безопасности:
    /// • удаляем только внутри заранее известных папок кэша/временных файлов (белый список корней);
    /// • не заходим в ссылки и точки соединения (junction) — они могут вести в твои файлы;
    /// • пропускаем системные файлы, занятые файлы и всё моложе MinAgeHours;
    /// • кэш программы чистим, только когда она закрыта;
    /// • пароли, история, закладки, куки и личные файлы не трогаем никогда.
    /// </summary>
    public static class Cleaner
    {
        static string Local => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        static string Roaming => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        static string Win => Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        static string ProgData => Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        static string LocalLow => Path.Combine(Path.GetDirectoryName(Local) ?? Local, "LocalLow");

        static IEnumerable<string> Existing(params string[] dirs) => dirs.Where(Directory.Exists);

        /// <summary>Кэши браузера на движке Chromium: все профили, только папки кэша.</summary>
        static IEnumerable<string> Chromium(string userData)
        {
            if (!Directory.Exists(userData)) yield break;
            foreach (var shared in new[] { "ShaderCache", "GrShaderCache", "GraphiteDawnCache" })
                if (Directory.Exists(Path.Combine(userData, shared))) yield return Path.Combine(userData, shared);
            string[] profiles;
            try { profiles = Directory.GetDirectories(userData).Where(d => Path.GetFileName(d) is "Default" or "Guest Profile" || Path.GetFileName(d).StartsWith("Profile ")).ToArray(); }
            catch { yield break; }
            foreach (var p in profiles)
                foreach (var c in new[] { "Cache", "Code Cache", "GPUCache", "DawnCache", "DawnGraphiteCache", "DawnWebGPUCache" })
                    if (Directory.Exists(Path.Combine(p, c))) yield return Path.Combine(p, c);
        }

        static IEnumerable<string> FirefoxCaches()
        {
            var root = Path.Combine(Local, "Mozilla", "Firefox", "Profiles");
            if (!Directory.Exists(root)) yield break;
            foreach (var p in Directory.GetDirectories(root))
            {
                if (Directory.Exists(Path.Combine(p, "cache2"))) yield return Path.Combine(p, "cache2");
                if (Directory.Exists(Path.Combine(p, "startupCache"))) yield return Path.Combine(p, "startupCache");
            }
        }

        static IEnumerable<string> Electron(string root) =>
            Existing(new[] { "Cache", "Code Cache", "GPUCache", "DawnCache", "DawnGraphiteCache" }.Select(c => Path.Combine(root, c)).ToArray());

        public static readonly List<CleanTarget> Targets = BuildTargets();

        static List<CleanTarget> BuildTargets()
        {
            const string ExplainBrowser = "Кэш — копии картинок и скриптов сайтов. Удаляется ТОЛЬКО кэш: пароли, вкладки, история, закладки и входы на сайты " +
                                          "остаются на месте. Сайты при первом открытии загрузятся чуть медленнее. Браузер нужно закрыть.";
            var t = new List<CleanTarget>
            {
                // --- система ---
                new() { Key = "temp", Group = "Система", Title = "Временные файлы программ", Safety = Safety.Safe, MinAgeHours = 24,
                        Explain = "Программы кладут сюда файлы на время работы и часто забывают удалить. Берём только файлы старше суток — " +
                                  "свежие может использовать установщик, который работает прямо сейчас.",
                        Roots = () => Existing(Path.GetTempPath()) },
                new() { Key = "wintemp", Group = "Система", Title = "Временные файлы Windows", Safety = Safety.Safe, MinAgeHours = 48, Admin = true,
                        Explain = "То же самое, но для системных служб. Нужны права администратора — Windows спросит разрешение.",
                        Roots = () => Existing(Path.Combine(Win, "Temp")) },
                new() { Key = "wer", Group = "Система", Title = "Отчёты об ошибках", Safety = Safety.Safe,
                        Explain = "Отчёты о вылетах программ, которые Windows собирается отправить в Microsoft. Тебе они не нужны.",
                        Roots = () => Existing(Path.Combine(Local, "Microsoft", "Windows", "WER")) },
                new() { Key = "wersys", Group = "Система", Title = "Системные отчёты об ошибках", Safety = Safety.Safe, Admin = true,
                        Explain = "Архив и очередь отчётов об ошибках всех пользователей.",
                        Roots = () => Existing(Path.Combine(ProgData, "Microsoft", "Windows", "WER", "ReportArchive"), Path.Combine(ProgData, "Microsoft", "Windows", "WER", "ReportQueue")) },
                new() { Key = "dumps", Group = "Система", Title = "Дампы вылетов программ", Safety = Safety.Safe,
                        Explain = "Снимки памяти упавших программ. Нужны только разработчикам, могут весить сотни мегабайт.",
                        Roots = () => Existing(Path.Combine(Local, "CrashDumps")) },
                new() { Key = "shaders", Group = "Система", Title = "Кэш шейдеров видеокарты", Safety = Safety.Safe,
                        Explain = "Заготовки графики для игр и браузеров. Пересоздаются сами; первый запуск игры может быть на пару секунд дольше.",
                        Roots = () => Existing(Path.Combine(Local, "D3DSCache"), Path.Combine(Local, "NVIDIA", "DXCache"), Path.Combine(Local, "NVIDIA", "GLCache"),
                                               Path.Combine(Local, "AMD", "DxCache"), Path.Combine(Local, "AMD", "DxcCache"), Path.Combine(Local, "AMD", "GLCache"),
                                               Path.Combine(Local, "AMD", "VkCache"), Path.Combine(LocalLow, "Intel", "ShaderCache")) },
                new() { Key = "thumbs", Group = "Система", Title = "Кэш миниатюр", Safety = Safety.Safe, TopOnly = true, Pattern = "thumbcache_*.db",
                        Explain = "Уменьшенные копии фото и видео для проводника. Пересоздадутся при открытии папок. Занятые проводником файлы пропускаются.",
                        Roots = () => Existing(Path.Combine(Local, "Microsoft", "Windows", "Explorer")) },
                new() { Key = "wu", Group = "Система", Title = "Скачанные обновления Windows", Safety = Safety.Caution, Admin = true, MinAgeHours = 72,
                        Explain = "Файлы уже установленных обновлений. Windows скачает заново, если понадобится. Не запускай, пока идёт установка обновлений.",
                        Roots = () => Existing(Path.Combine(Win, "SoftwareDistribution", "Download")) },
                new() { Key = "delivery", Group = "Система", Title = "Кэш оптимизации доставки", Safety = Safety.Safe, Admin = true,
                        Explain = "Куски обновлений, которыми Windows делится с другими компьютерами. Безопасно удалить.",
                        Roots = () => Existing(Path.Combine(Win, "ServiceProfiles", "NetworkService", "AppData", "Local", "Microsoft", "Windows", "DeliveryOptimization", "Cache")) },
                new() { Key = "logs", Group = "Система", Title = "Старые журналы Windows", Safety = Safety.Caution, Admin = true, MinAgeHours = 24 * 14, Pattern = "*.log",
                        Explain = "Журналы установки и служб старше двух недель. Полезны мастеру при поиске сложной проблемы — если всё работает, не нужны.",
                        Roots = () => Existing(Path.Combine(Win, "Logs")) },
                new() { Key = "recycle", Group = "Система", Title = "Корзина", Safety = Safety.Caution,
                        Explain = "Здесь лежат файлы, которые ты удалил. После очистки их уже не вернуть — сначала загляни в корзину.",
                        CustomScan = () => { var r = Maint.RecycleBin(); return (r.Bytes, (int)r.Items); },
                        CustomClean = () => Maint.EmptyRecycleBin() ? 1 : 0 },

                // --- не рекомендуется: только объясняем ---
                new() { Key = "prefetch", Group = "Не трогать", Title = "Prefetch (ускорение запуска)", Safety = Safety.NotRecommended, InfoOnly = true,
                        Explain = "Миф, что его чистка ускоряет компьютер. Windows по этим файлам быстрее запускает программы — после удаления " +
                                  "они станут открываться МЕДЛЕННЕЕ, пока Windows не соберёт данные заново." },
                new() { Key = "installer", Group = "Не трогать", Title = "Папка Windows\\Installer", Safety = Safety.NotRecommended, InfoOnly = true,
                        Explain = "Копии установщиков программ. Без них перестанут обновляться и удаляться Office, драйверы и многое другое." },
                new() { Key = "winsxs", Group = "Не трогать", Title = "Папка WinSxS вручную", Safety = Safety.NotRecommended, InfoOnly = true,
                        Explain = "Хранилище компонентов Windows. Удаление файлов вручную ломает систему. Сжать её безопасно можно только " +
                                  "официально — кнопкой «Сжать хранилище компонентов» в разделе «Починить»." },
                new() { Key = "drivers", Group = "Не трогать", Title = "DriverStore и System32", Safety = Safety.NotRecommended, InfoOnly = true,
                        Explain = "Драйверы и ядро системы. Чистильщики, которые туда лезут, — частая причина синих экранов." },
                new() { Key = "downloads", Group = "Не трогать", Title = "Папка «Загрузки»", Safety = Safety.NotRecommended, InfoOnly = true,
                        Explain = "Это твои файлы, автоматически их не удаляем. Открой «Файлы → Старые загрузки» и выбери сам — они уйдут в корзину." },
            };

            // --- браузеры ---
            void B(string key, string title, string[] procs, Func<IEnumerable<string>> roots) =>
                t.Add(new CleanTarget { Key = key, Group = "Браузеры", Title = title, Safety = Safety.Safe, Processes = procs, Explain = ExplainBrowser, Roots = roots });
            B("chrome", "Google Chrome", new[] { "chrome" }, () => Chromium(Path.Combine(Local, "Google", "Chrome", "User Data")));
            B("edge", "Microsoft Edge", new[] { "msedge" }, () => Chromium(Path.Combine(Local, "Microsoft", "Edge", "User Data")));
            B("yandex", "Яндекс Браузер", new[] { "browser" }, () => Chromium(Path.Combine(Local, "Yandex", "YandexBrowser", "User Data")));
            B("opera", "Opera и Opera GX", new[] { "opera" }, () => Existing(Path.Combine(Local, "Opera Software", "Opera Stable", "Cache"), Path.Combine(Local, "Opera Software", "Opera GX Stable", "Cache"))
                                                                   .Concat(Electron(Path.Combine(Roaming, "Opera Software", "Opera Stable"))).Concat(Electron(Path.Combine(Roaming, "Opera Software", "Opera GX Stable"))));
            B("brave", "Brave", new[] { "brave" }, () => Chromium(Path.Combine(Local, "BraveSoftware", "Brave-Browser", "User Data")));
            B("vivaldi", "Vivaldi", new[] { "vivaldi" }, () => Chromium(Path.Combine(Local, "Vivaldi", "User Data")));
            B("firefox", "Firefox", new[] { "firefox" }, FirefoxCaches);

            // --- приложения ---
            void A(string key, string title, string[] procs, string explain, Func<IEnumerable<string>> roots, Safety s = Safety.Safe) =>
                t.Add(new CleanTarget { Key = key, Group = "Приложения", Title = title, Safety = s, Processes = procs, Explain = explain, Roots = roots });
            A("discord", "Discord", new[] { "Discord" }, "Кэш картинок и интерфейса. Сообщения и настройки не трогаются.", () => Electron(Path.Combine(Roaming, "discord")));
            A("telegram", "Telegram Desktop", new[] { "Telegram" }, "Кэш просмотренных фото и видео. Переписка хранится в облаке, медиа догрузятся при просмотре.",
              () => Existing(Path.Combine(Roaming, "Telegram Desktop", "tdata", "user_data", "cache"), Path.Combine(Roaming, "Telegram Desktop", "tdata", "user_data", "media_cache")));
            A("spotify", "Spotify", new[] { "Spotify" }, "Кэш прослушанных песен. Если ты скачивал песни для офлайна — их придётся скачать снова.",
              () => Existing(Path.Combine(Local, "Spotify", "Data"), Path.Combine(Local, "Spotify", "Browser", "Cache")), Safety.Caution);
            A("teams", "Microsoft Teams", new[] { "ms-teams", "Teams" }, "Кэш интерфейса и картинок. Чаты хранятся в облаке.",
              () => Electron(Path.Combine(Roaming, "Microsoft", "Teams")).Concat(Existing(Path.Combine(Local, "Packages", "MSTeams_8wekyb3d8bbwe", "LocalCache", "Microsoft", "MSTeams", "EBWebView", "Default", "Cache"))));
            A("vscode", "Visual Studio Code", new[] { "Code" }, "Кэш редактора. Проекты, настройки и расширения остаются.",
              () => Electron(Path.Combine(Roaming, "Code")).Concat(Existing(Path.Combine(Roaming, "Code", "CachedData"), Path.Combine(Roaming, "Code", "CachedExtensionVSIXs"))));
            A("steam", "Steam (веб-кэш)", new[] { "steam", "steamwebhelper" }, "Кэш магазина и сообщества. Игры и сохранения не трогаются.",
              () => Existing(Path.Combine(Local, "Steam", "htmlcache")));
            A("devcache", "Кэш npm, pip и NuGet", null, "Скачанные пакеты для программистов. Скачаются заново при следующей сборке.",
              () => Existing(Path.Combine(Local, "npm-cache"), Path.Combine(Local, "pip", "Cache"), Path.Combine(Local, "NuGet", "v3-cache")));

            // --- следы активности ---
            t.Add(new CleanTarget
            {
                Key = "recent", Group = "Следы", Title = "Список недавних файлов", Safety = Safety.Safe,
                Explain = "Ярлыки «Недавние» в проводнике и в списках переходов на панели задач. Сами файлы не удаляются.",
                Roots = () => Existing(Path.Combine(Roaming, "Microsoft", "Windows", "Recent")),
            });
            t.Add(new CleanTarget
            {
                Key = "mru", Group = "Следы", Title = "История «Выполнить», адресов и поиска в проводнике", Safety = Safety.Safe,
                Explain = "Что ты вводил в Win+R, в адресной строке и поиске проводника. Удаляются только записи истории.",
                CustomScan = () => (0, MruKeys.Sum(k => RegCount(k))),
                CustomClean = () => MruKeys.Sum(k => RegClear(k)),
            });
            t.Add(new CleanTarget
            {
                Key = "clipboard", Group = "Следы", Title = "Журнал буфера обмена Windows", Safety = Safety.Safe,
                Explain = "То, что видно по Win+V. Закреплённые элементы Windows оставит.",
                CustomScan = () => (0, 1),
                CustomClean = () => { try { return global::Windows.ApplicationModel.DataTransfer.Clipboard.ClearHistory() ? 1 : 0; } catch { return 0; } },
            });
            t.Add(new CleanTarget
            {
                Key = "dns", Group = "Следы", Title = "Кэш DNS", Safety = Safety.Safe,
                Explain = "Список недавно открытых сайтов, который Windows хранит для скорости. Сброс помогает, если сайт «не открывается».",
                CustomScan = () => (0, 1),
                CustomClean = () => Maint.FlushDns() ? 1 : 0,
            });
            return t;
        }

        static readonly string[] MruKeys =
        {
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\RunMRU",
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\TypedPaths",
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\WordWheelQuery",
        };

        static int RegCount(string key)
        {
            try { using var k = Registry.CurrentUser.OpenSubKey(key); return k?.GetValueNames().Count(n => n != "") ?? 0; }
            catch { return 0; }
        }

        static int RegClear(string key)
        {
            int n = 0;
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(key, true);
                if (k == null) return 0;
                foreach (var v in k.GetValueNames()) { if (v == "") continue; k.DeleteValue(v, false); n++; }
            }
            catch { }
            return n;
        }

        // ------------------------------------------------------------------
        //  Проверки безопасности
        // ------------------------------------------------------------------

        /// <summary>Корни, внутри которых вообще разрешено что-то удалять.</summary>
        static string[] AllowedBases => new[]
        {
            Path.GetTempPath(), Local, Roaming, LocalLow,
            Path.Combine(Win, "Temp"), Path.Combine(Win, "SoftwareDistribution", "Download"), Path.Combine(Win, "Logs"),
            Path.Combine(Win, "ServiceProfiles", "NetworkService", "AppData", "Local", "Microsoft", "Windows", "DeliveryOptimization", "Cache"),
            Path.Combine(ProgData, "Microsoft", "Windows", "WER"),
        }.Select(Norm).ToArray();

        /// <summary>Сюда нельзя никогда — даже если путь каким-то образом окажется внутри разрешённого.</summary>
        static string[] Forbidden
        {
            get
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                return new[]
                {
                    Path.Combine(Win, "System32"), Path.Combine(Win, "SysWOW64"), Path.Combine(Win, "WinSxS"), Path.Combine(Win, "Installer"),
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    Path.Combine(home, "Documents"), Path.Combine(home, "Desktop"), Path.Combine(home, "Downloads"), Path.Combine(home, "Pictures"),
                    Path.Combine(home, "Music"), Path.Combine(home, "Videos"), Path.Combine(home, "OneDrive"), Store.Dir,
                }.Where(p => !string.IsNullOrEmpty(p)).Select(Norm).ToArray();
            }
        }

        static string Norm(string p) => Path.GetFullPath(p).TrimEnd('\\') + "\\";

        /// <summary>Папка-корень цели проходит строгую проверку: внутри белого списка, не системная, не ссылка.</summary>
        public static bool IsSafeRoot(string dir)
        {
            try
            {
                var full = Norm(dir);
                if (full.Length <= 4) return false;                                  // корень диска
                if (!AllowedBases.Any(b => full.StartsWith(b, StringComparison.OrdinalIgnoreCase))) return false;
                // целиком AppData, профиль или папку Windows не чистим никогда — только конкретные папки кэша внутри
                var broad = new[] { Local, Roaming, LocalLow, ProgData, Win, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                                    Path.Combine(Local, "Microsoft"), Path.Combine(Local, "Microsoft", "Windows"), Path.Combine(Roaming, "Microsoft"),
                                    Path.Combine(Roaming, "Microsoft", "Windows"), Path.Combine(Local, "Packages"), Path.Combine(Local, "Programs") };
                if (broad.Any(b => string.Equals(full, Norm(b), StringComparison.OrdinalIgnoreCase))) return false;
                if (Forbidden.Any(f => full.StartsWith(f, StringComparison.OrdinalIgnoreCase))) return false;
                var info = new DirectoryInfo(full);
                if (!info.Exists || info.Attributes.HasFlag(FileAttributes.ReparsePoint)) return false;
                return true;
            }
            catch { return false; }
        }

        static IEnumerable<FileInfo> Files(CleanTarget t)
        {
            var cutoff = DateTime.Now.AddHours(-t.MinAgeHours);
            var opts = new EnumerationOptions
            {
                RecurseSubdirectories = !t.TopOnly, IgnoreInaccessible = true, MaxRecursionDepth = 16,
                AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System | FileAttributes.Device,
            };
            foreach (var root in t.Roots().Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!IsSafeRoot(root)) { Log.Info("cleaner: skip unsafe root " + root); continue; }
                IEnumerable<string> list;
                try { list = Directory.EnumerateFiles(root, t.Pattern, opts); } catch { continue; }
                foreach (var f in list)
                {
                    FileInfo fi;
                    try { fi = new FileInfo(f); if (t.MinAgeHours > 0 && fi.LastWriteTime > cutoff) continue; }
                    catch { continue; }
                    yield return fi;
                }
            }
        }

        public static Task Scan(IEnumerable<CleanTarget> targets) => Task.Run(() =>
        {
            foreach (var t in targets)
            {
                long b = 0; int n = 0;
                try
                {
                    if (t.CustomScan != null) (b, n) = t.CustomScan();
                    else if (!t.InfoOnly) foreach (var f in Files(t)) { try { b += f.Length; n++; } catch { } }
                }
                catch { }
                t.Bytes = b; t.Files = n; t.Scanned = true;
            }
        });

        /// <summary>Чистит выбранное. Цели с правами администратора уходят одним пакетом в отдельный процесс (Windows спросит разрешение).</summary>
        public static async Task<CleanResult> Clean(IList<CleanTarget> targets)
        {
            long freed = 0; int del = 0, skip = 0;
            var notes = new List<string>();
            foreach (var t in targets.Where(x => !x.InfoOnly && !x.Admin))
            {
                var busy = t.Blocked;
                if (busy != null) { notes.Add($"{t.Title}: закрой программу"); continue; }
                var r = await Task.Run(() => CleanOne(t));
                freed += r.Freed; del += r.Deleted; skip += r.Skipped;
            }
            var admin = targets.Where(x => x.Admin && !x.InfoOnly).Select(x => x.Key).ToList();
            if (admin.Count > 0)
            {
                var r = await Elevated.Clean(admin);
                if (r == null) notes.Add("системные папки: нет разрешения администратора");
                else { freed += r.Freed; del += r.Deleted; skip += r.Skipped; }
            }
            foreach (var t in targets) t.Scanned = false;
            Store.Settings.LastCleanup = DateTime.Now;
            Store.Settings.CleanedTotal += freed;
            return new CleanResult(freed, del, skip, string.Join("; ", notes));
        }

        public static CleanResult CleanOne(CleanTarget t)
        {
            if (t.InfoOnly) return new CleanResult(0, 0, 0, null);
            if (t.CustomClean != null)
            {
                long before = t.Bytes;
                int n = 0;
                try { n = t.CustomClean(); } catch { }
                return new CleanResult(n > 0 ? before : 0, n, 0, null);
            }
            long freed = 0; int del = 0, skip = 0;
            foreach (var f in Files(t).ToList())
            {
                try
                {
                    if (f.Attributes.HasFlag(FileAttributes.ReadOnly)) { skip++; continue; } // только чтение — кто-то защитил его намеренно
                    long len = f.Length;
                    f.Delete();
                    freed += len; del++;
                }
                catch { skip++; }
            }
            // пустые подпапки внутри (сами корни остаются)
            if (!t.TopOnly)
                foreach (var root in t.Roots().Where(IsSafeRoot))
                {
                    try
                    {
                        foreach (var d in Directory.EnumerateDirectories(root, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System })
                                     .OrderByDescending(x => x.Length))
                            try { if (!Directory.EnumerateFileSystemEntries(d).Any()) Directory.Delete(d); } catch { }
                    }
                    catch { }
                }
            return new CleanResult(freed, del, skip, null);
        }

        // ------------------------------------------------------------------
        //  Поиск лишнего среди личных файлов — только показываем, удаляем в корзину по выбору
        // ------------------------------------------------------------------

        static IEnumerable<string> UserDirs(params string[] names)
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return names.Select(n => Path.Combine(home, n)).Where(Directory.Exists);
        }

        static IEnumerable<FileInfo> UserFiles(IEnumerable<string> dirs)
        {
            var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System | FileAttributes.Hidden };
            foreach (var d in dirs)
            {
                IEnumerable<string> list;
                try { list = Directory.EnumerateFiles(d, "*", opts); } catch { continue; }
                foreach (var f in list) { FileInfo fi = null; try { fi = new FileInfo(f); } catch { } if (fi != null) yield return fi; }
            }
        }

        /// <summary>Файлы в «Загрузках», которые не открывались и не менялись больше N дней.</summary>
        public static Task<List<FileInfo>> OldDownloads(int days = 90) => Task.Run(() =>
        {
            var cutoff = DateTime.Now.AddDays(-days);
            return UserFiles(UserDirs("Downloads")).Where(f => f.LastWriteTime < cutoff && f.LastAccessTime < cutoff)
                                                    .OrderByDescending(f => f.Length).Take(60).ToList();
        });

        /// <summary>Дубликаты (одинаковое содержимое) в личных папках: сначала по размеру, потом по хешу.</summary>
        public static Task<List<List<FileInfo>>> Duplicates(long minBytes = 512 * 1024) => Task.Run(() =>
        {
            var bySize = UserFiles(UserDirs("Downloads", "Desktop", "Documents", "Pictures", "Videos", "Music"))
                .Where(f => f.Length >= minBytes).GroupBy(f => f.Length).Where(g => g.Count() > 1);
            var res = new List<List<FileInfo>>();
            foreach (var g in bySize)
            {
                foreach (var h in g.GroupBy(f => Hash(f)).Where(h => h.Key != null && h.Count() > 1))
                    res.Add(h.OrderBy(f => f.CreationTime).ToList());
                if (res.Count >= 40) break;
            }
            return res.OrderByDescending(l => l[0].Length * (l.Count - 1)).ToList();
        });

        static string Hash(FileInfo f)
        {
            try
            {
                using var s = f.OpenRead();
                // первые и последние 1 МБ + размер — быстро и надёжно для фото/видео/архивов
                var buf = new byte[Math.Min(f.Length, 1 << 20)];
                int n = s.Read(buf, 0, buf.Length);
                using var sha = SHA1.Create();
                sha.TransformBlock(buf, 0, n, null, 0);
                if (f.Length > buf.Length * 2L)
                {
                    s.Seek(-buf.Length, SeekOrigin.End);
                    n = s.Read(buf, 0, buf.Length);
                }
                sha.TransformFinalBlock(buf, 0, n);
                return Convert.ToHexString(sha.Hash) + f.Length;
            }
            catch { return null; }
        }

        /// <summary>Пустые папки в личных каталогах (без системных и скрытых).</summary>
        public static Task<List<string>> EmptyFolders() => Task.Run(() =>
        {
            var res = new List<string>();
            var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System | FileAttributes.Hidden };
            foreach (var d in UserDirs("Downloads", "Desktop", "Documents"))
            {
                try
                {
                    foreach (var sub in Directory.EnumerateDirectories(d, "*", opts))
                        try { if (!Directory.EnumerateFileSystemEntries(sub).Any()) res.Add(sub); } catch { }
                }
                catch { }
                if (res.Count > 200) break;
            }
            return res;
        });
    }

    /// <summary>
    /// Действия с правами администратора. Всё, что требует прав, выполняется отдельным процессом:
    /// Windows показывает стандартный запрос UAC, и пользователь видит, что именно запускается.
    /// </summary>
    public static class Elevated
    {
        static string Dir { get { var d = Path.Combine(Store.Dir, "admin"); Directory.CreateDirectory(d); return d; } }

        static Task<int> RunAs(string exe, string args) => Task.Run(() =>
        {
            try
            {
                using var p = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden });
                if (p == null) return -1;
                p.WaitForExit(10 * 60_000);
                return p.HasExited ? p.ExitCode : -2;
            }
            catch (System.ComponentModel.Win32Exception) { return -1; } // нажали «Нет» в запросе Windows
            catch (Exception e) { Log.Info("elevated: " + e.Message); return -1; }
        });

        /// <summary>Выполнить команды cmd от имени администратора одним запросом UAC. Скрипт остаётся в папке admin для проверки.</summary>
        public static Task<int> Script(string name, IEnumerable<string> lines)
        {
            var file = Path.Combine(Dir, $"{name}-{DateTime.Now:yyyyMMdd-HHmmss}.cmd");
            File.WriteAllText(file, "@echo off\r\nchcp 65001 >nul\r\n" + string.Join("\r\n", lines) + "\r\n", new System.Text.UTF8Encoding(false));
            foreach (var old in new DirectoryInfo(Dir).GetFiles("*.cmd").OrderByDescending(f => f.CreationTime).Skip(30)) try { old.Delete(); } catch { }
            return RunAs("cmd.exe", $"/c \"\"{file}\"\"");
        }

        /// <summary>Долгая проверка в видимом окне (SFC, DISM, chkdsk): человек видит ход и результат, окно ждёт нажатия клавиши.</summary>
        public static void Console(string name, string title, IEnumerable<string> lines)
        {
            var file = Path.Combine(Dir, $"{name}-{DateTime.Now:yyyyMMdd-HHmmss}.cmd");
            File.WriteAllText(file, $"@echo off\r\nchcp 65001 >nul\r\ntitle N-Island: {title}\r\necho N-Island: {title}\r\necho Не закрывай это окно до конца проверки.\r\necho.\r\n" +
                string.Join("\r\n", lines) + "\r\necho.\r\necho Готово. Нажми любую клавишу, чтобы закрыть окно.\r\npause >nul\r\n", new System.Text.UTF8Encoding(false));
            try { Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"\"{file}\"\"") { UseShellExecute = true, Verb = "runas" }); }
            catch { }
        }

        /// <summary>Чистка системных папок: этот же exe с ключом --elevated-clean и теми же проверками безопасности.</summary>
        public static async Task<CleanResult> Clean(List<string> keys)
        {
            var result = Path.Combine(Dir, "clean-result.json");
            try { File.Delete(result); } catch { }
            int code = await RunAs(Environment.ProcessPath, $"--elevated-clean {string.Join(",", keys)} \"{result}\"");
            if (code < 0 || !File.Exists(result)) return null;
            try { return JsonSerializer.Deserialize<CleanResult>(File.ReadAllText(result)); } catch { return null; }
        }

        /// <summary>Точка входа второго процесса (уже с правами администратора).</summary>
        public static int RunCleanJob(string[] args)
        {
            try
            {
                var keys = args[1].Split(',', StringSplitOptions.RemoveEmptyEntries);
                long freed = 0; int del = 0, skip = 0;
                foreach (var t in Cleaner.Targets.Where(t => keys.Contains(t.Key) && t.Admin && !t.InfoOnly))
                {
                    var r = Cleaner.CleanOne(t);
                    freed += r.Freed; del += r.Deleted; skip += r.Skipped;
                }
                File.WriteAllText(args[2], JsonSerializer.Serialize(new CleanResult(freed, del, skip, null)));
                return 0;
            }
            catch { return 1; }
        }

        public static bool IsAdmin
        {
            get
            {
                try { using var id = System.Security.Principal.WindowsIdentity.GetCurrent(); return new System.Security.Principal.WindowsPrincipal(id).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator); }
                catch { return false; }
            }
        }

        /// <summary>Точка восстановления Windows перед серьёзными изменениями.</summary>
        public static Task<int> RestorePoint() => Script("restore-point", new[]
        {
            "powershell -NoProfile -ExecutionPolicy Bypass -Command \"Enable-ComputerRestore -Drive $env:SystemDrive -ErrorAction SilentlyContinue; " +
            "New-ItemProperty -Path 'HKLM:\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\SystemRestore' -Name SystemRestorePointCreationFrequency -Value 0 -PropertyType DWord -Force | Out-Null; " +
            "Checkpoint-Computer -Description 'N-Island' -RestorePointType MODIFY_SETTINGS\"",
        });
    }
}
