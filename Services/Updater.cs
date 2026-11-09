using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NIsland.Core;

namespace NIsland.Services
{
    public record UpdateInfo(string Version, string PageUrl, string Notes, string AssetName, string AssetUrl, long Size, string SumsUrl,
                             string AssetApiUrl = null, string SumsApiUrl = null);

    public enum UpdateState { UpToDate, Available, NoReleases, Error }

    /// <summary>
    /// Обновление через GitHub Releases: проверка последнего релиза, выбор файла под эту сборку (x64 / x86 / lite),
    /// скачивание, проверка SHA256, замена exe с откатом и перезапуск. Ничего не ставится в систему — меняется только сам exe.
    /// </summary>
    public static class Updater
    {
        static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

        static Updater()
        {
            Http.DefaultRequestHeaders.UserAgent.ParseAdd("N-Island/" + AppInfo.Version);
            Http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        }

        /// <summary>Лёгкая сборка — та, что работает на установленном .NET (а не носит его с собой).</summary>
        public static bool IsLite => RuntimeEnvironment.GetRuntimeDirectory().Contains(@"\dotnet\shared\", StringComparison.OrdinalIgnoreCase);

        public static string Arch => RuntimeInformation.ProcessArchitecture == Architecture.X86 ? "x86" : "x64";

        /// <summary>Какой файл релиза подходит этой копии: NIsland-1.5.0-win-x64.zip, -win-x86.zip или -win-x64-lite.exe.</summary>
        public static string Suffix => IsLite ? $"-win-{Arch}-lite.exe" : $"-win-{Arch}.zip";

        public static UpdateInfo Last { get; private set; }

        public static async Task<(UpdateState State, UpdateInfo Info, string Error)> Check()
        {
            try
            {
                HttpResponseMessage resp = null;
                for (int attempt = 0; ; attempt++)
                {
                    try
                    {
                        resp = await Http.GetAsync($"https://api.github.com/repos/{AppInfo.Repo}/releases/latest");
                        if (!Retryable((int)resp.StatusCode) || attempt >= 2) break;
                        resp.Dispose();
                    }
                    catch (Exception) when (attempt < 2) { }
                    await Task.Delay(Backoff[attempt]);
                }
                using var _r = resp;
                if ((int)resp.StatusCode == 404) return (UpdateState.NoReleases, null, null);
                if (!resp.IsSuccessStatusCode) return (UpdateState.Error, null, StatusText((int)resp.StatusCode));
                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                var r = doc.RootElement;
                var tag = r.GetProperty("tag_name").GetString()?.TrimStart('v', 'V') ?? "";
                string assetName = null, assetUrl = null, sums = null, assetApi = null, sumsApi = null;
                long size = 0;
                foreach (var a in r.GetProperty("assets").EnumerateArray())
                {
                    var name = a.GetProperty("name").GetString() ?? "";
                    var url = a.GetProperty("browser_download_url").GetString();
                    var api = a.TryGetProperty("url", out var au) ? au.GetString() : null;   // api.github.com/…/releases/assets/<id> — запасной путь
                    if (name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase)) { sums = url; sumsApi = api; }
                    else if (name.EndsWith(Suffix, StringComparison.OrdinalIgnoreCase)) { assetName = name; assetUrl = url; assetApi = api; size = a.GetProperty("size").GetInt64(); }
                }
                var info = new UpdateInfo(tag, r.GetProperty("html_url").GetString(), r.TryGetProperty("body", out var b) ? b.GetString() : "",
                                          assetName, assetUrl, size, sums, assetApi, sumsApi);
                Last = info;
                bool newer = Version.TryParse(tag, out var remote) && Version.TryParse(AppInfo.Version, out var local) && remote > local;
                return (newer ? UpdateState.Available : UpdateState.UpToDate, info, null);
            }
            catch (Exception e) { return (UpdateState.Error, null, Friendly(e)); }
        }

        // ------------------------------------------------------------------
        //  Надёжная загрузка: повторы, докачка, запасной адрес, понятные ошибки
        // ------------------------------------------------------------------

        static readonly int[] Backoff = { 2000, 5000, 10000 };
        internal static Action<string> Trace;   // для самопроверки: что происходило с каждой попыткой

        /// <summary>Сервер временно не справился — стоит повторить (5xx, 408 «долго», 429 «слишком часто»).</summary>
        static bool Retryable(int code) => code >= 500 || code == 408 || code == 429;

        static string StatusText(int code) => code switch
        {
            504 or 502 or 503 => I18n.T("Сервер загрузки GitHub не ответил вовремя") + $" ({code}) — " + I18n.T("попробуй ещё раз через минуту"),
            404 => I18n.T("Файл обновления не найден на GitHub") + " (404) — " + I18n.T("скачай вручную со страницы релиза"),
            403 or 429 => I18n.T("GitHub временно ограничил скачивание") + $" ({code}) — " + I18n.T("попробуй через несколько минут"),
            >= 500 => I18n.T("Сервер GitHub ответил ошибкой") + $" ({code}) — " + I18n.T("попробуй позже"),
            _ => I18n.T("GitHub ответил") + $" {code} — " + I18n.T("скачай вручную со страницы релиза"),
        };

        /// <summary>В сборке вырезаны тексты сообщений .NET (UseSystemResourceKeys) — поэтому свои, понятные.</summary>
        public static string Friendly(Exception e) => e switch
        {
            DownloadException d => d.Message,
            HttpRequestException h when h.StatusCode is { } sc => StatusText((int)sc),
            HttpRequestException => I18n.T("Нет связи с GitHub — проверь интернет (или VPN) и попробуй ещё раз"),
            TaskCanceledException or OperationCanceledException => I18n.T("Загрузка зависла и была прервана — попробуй ещё раз"),
            IOException io when io.InnerException is System.Net.Sockets.SocketException => I18n.T("Связь оборвалась во время загрузки — попробуй ещё раз"),
            UnauthorizedAccessException => I18n.T("Нет прав на запись — перенеси N-Island в Документы или обнови вручную"),
            _ => e.Message,
        };

        sealed class DownloadException : Exception { public DownloadException(string m) : base(m) { } }

        /// <summary>
        /// Скачать файл: до 4 попыток с паузами, докачка с места обрыва (Range), обрыв, если 60 с не пришло ни байта.
        /// Если прямая ссылка так и не ответила — тот же файл через API GitHub (тоже GitHub, контрольная сумма сверяется после).
        /// </summary>
        internal static async Task Download(string url, string apiUrl, string file, long size, Action<double> progress)
        {
            Exception last = null;
            long done = 0;
            if (File.Exists(file)) File.Delete(file);
            foreach (var (u, api) in new[] { (url, false), (apiUrl, true) })
            {
                if (u == null) continue;
                // прямая ссылка — 2 попытки (если её сервер лежит, не ждём), запасной адрес через API — 4
                int tries = api || apiUrl == null ? 4 : 2;
                for (int attempt = 0; attempt < tries; attempt++)
                {
                    if (attempt > 0) await Task.Delay(Backoff[Math.Min(attempt - 1, Backoff.Length - 1)]);
                    try
                    {
                        using var req = new HttpRequestMessage(HttpMethod.Get, u);
                        if (api) { req.Headers.Accept.Clear(); req.Headers.Accept.ParseAdd("application/octet-stream"); }
                        if (done > 0) req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(done, null);
                        using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
                        int code = (int)resp.StatusCode;
                        Trace?.Invoke($"{(api ? "api" : "direct")} #{attempt + 1}: {code}{(done > 0 ? $" (с {done:N0} байт)" : "")}");
                        // 416 — докачка с места, которого у файла нет: всё уже скачано или сервер не знает такой точки
                        if (code == 416 && done > 0)
                        {
                            if (size > 0 && done >= size) return;
                            done = 0;
                            if (File.Exists(file)) File.Delete(file);
                            last = new DownloadException(StatusText(code));
                            continue;
                        }
                        if (!resp.IsSuccessStatusCode)
                        {
                            last = new DownloadException(StatusText(code));
                            if (Retryable(code)) continue;
                            break;   // 404 и т. п. — повторять бессмысленно, пробуем запасной адрес
                        }
                        bool append = done > 0 && code == 206;
                        if (!append) done = 0;   // сервер не поддержал докачку — сначала
                        long total = (resp.Content.Headers.ContentLength ?? 0) + done;
                        if (total <= done) total = size;
                        using var src = await resp.Content.ReadAsStreamAsync();
                        using var dst = new FileStream(file, append ? FileMode.Append : FileMode.Create, FileAccess.Write);
                        var buf = new byte[81920];
                        while (true)
                        {
                            using var idle = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                            int n = await src.ReadAsync(buf, idle.Token);
                            if (n <= 0) break;
                            await dst.WriteAsync(buf.AsMemory(0, n));
                            done += n;
                            if (total > 0) progress?.Invoke(Math.Min(1, (double)done / total));
                        }
                        if (size > 0 && done < size) { last = new DownloadException(I18n.T("Связь оборвалась во время загрузки — попробуй ещё раз")); continue; }
                        return;
                    }
                    catch (Exception e) when (e is HttpRequestException or IOException or TaskCanceledException or OperationCanceledException)
                    {
                        last = e;
                        Log.Info($"update download: {(api ? "api" : "direct")} attempt {attempt + 1}: {e.GetType().Name} {e.Message}");
                        Trace?.Invoke($"{(api ? "api" : "direct")} #{attempt + 1}: {e.GetType().Name} после {done:N0} байт");
                    }
                }
                done = 0;
                if (File.Exists(file)) File.Delete(file);
            }
            throw last is DownloadException ? last : new DownloadException(Friendly(last ?? new Exception("?")));
        }

        static async Task<string> DownloadText(string url, string apiUrl)
        {
            var tmp = Path.Combine(Path.GetTempPath(), "NIsland-update-" + Guid.NewGuid().ToString("N")[..8] + ".txt");
            try { await Download(url, apiUrl, tmp, 0, null); return await File.ReadAllTextAsync(tmp); }
            finally { try { File.Delete(tmp); } catch { } }
        }

        /// <summary>
        /// Скачать и поставить обновление. progress — 0..1. Возвращает null при успехе (программа перезапустится) или текст ошибки.
        /// </summary>
        static bool IsGitHub(string url) =>
            Uri.TryCreate(url, UriKind.Absolute, out var x) && x.Scheme == Uri.UriSchemeHttps &&
            (x.Host == "github.com" || x.Host.EndsWith(".github.com") || x.Host.EndsWith(".githubusercontent.com"));

        public static async Task<string> Install(UpdateInfo u, Action<double> progress)
        {
            if (u?.AssetUrl == null) return "В релизе нет файла для этой версии (" + Suffix.TrimStart('-') + ") — скачай вручную со страницы релиза";
            // скачиваем только с GitHub и только со сверкой контрольной суммы
            if (!IsGitHub(u.AssetUrl) || u.SumsUrl == null || !IsGitHub(u.SumsUrl)) return "В релизе нет проверочного файла SHA256SUMS.txt с GitHub — обновление отменено, скачай вручную со страницы релиза";
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return "Не удалось определить, где лежит программа";
            var dir = Path.Combine(Path.GetTempPath(), "NIsland-update");
            try
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                Directory.CreateDirectory(dir);
                var file = Path.Combine(dir, Path.GetFileName(u.AssetName));

                // 1. скачиваем с прогрессом — с повторами, докачкой и запасным адресом
                await Download(u.AssetUrl, IsGitHub(u.AssetApiUrl) ? u.AssetApiUrl : null, file, u.Size, progress);

                // 2. контрольная сумма из SHA256SUMS.txt — защита от битой или подменённой загрузки
                if (u.SumsUrl != null)
                {
                    var sums = await DownloadText(u.SumsUrl, IsGitHub(u.SumsApiUrl) ? u.SumsApiUrl : null);
                    var line = sums.Split('\n').FirstOrDefault(l => l.TrimEnd().EndsWith(u.AssetName, StringComparison.OrdinalIgnoreCase));
                    if (line == null) return "В SHA256SUMS.txt нет этого файла — обновление отменено";
                    var expected = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
                    string actual;
                    using (var fs = File.OpenRead(file)) actual = Convert.ToHexString(SHA256.HashData(fs));
                    if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase)) return "Файл скачался с ошибкой (не совпала контрольная сумма) — обновление отменено";
                }

                // 3. достаём exe из архива
                var newExe = file;
                if (file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    using var zip = ZipFile.OpenRead(file);
                    var entry = zip.Entries.FirstOrDefault(e => e.Name.Equals("NIsland.exe", StringComparison.OrdinalIgnoreCase));
                    if (entry == null) return "В архиве нет NIsland.exe";
                    newExe = Path.Combine(dir, "NIsland.new.exe");
                    entry.ExtractToFile(newExe, true);
                }

                // 4. меняем exe: работающий файл Windows разрешает переименовать, но не перезаписать
                var old = exe + ".old";
                try { if (File.Exists(old)) File.Delete(old); } catch { }
                try { File.Move(exe, old); }
                catch (UnauthorizedAccessException) { return "Нет прав на запись в папку программы — перенеси N-Island в Документы или обнови вручную"; }
                try { File.Copy(newExe, exe, true); }
                catch (Exception e)
                {
                    try { File.Move(old, exe); } catch { }   // откат: возвращаем старую версию
                    return "Не удалось записать новую версию: " + Friendly(e);
                }

                // 5. перезапуск: новая копия дождётся, пока эта закроется
                Log.Info($"update: {AppInfo.Version} → {u.Version} ({u.AssetName})");
                Store.Flush();
                Process.Start(new ProcessStartInfo(exe, "--restart --autostart --settings about") { UseShellExecute = false });
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(App.Quit));
                return null;
            }
            catch (Exception e) { Log.Info("update: " + e); return "Не получилось обновить: " + Friendly(e); }
        }

        /// <summary>Самопроверка: проверить релиз, скачать файл этой сборки и сверить SHA-256 — без замены exe.</summary>
        internal static async Task<string> TestDownload(Action<string> log)
        {
            Trace = l => log("  " + l);
            var (state, u, err) = await Check();
            log($"check: {state} {u?.Version} {u?.AssetName} api={u?.AssetApiUrl != null} {err}");
            if (u?.AssetUrl == null) return "no asset";
            var dir = Path.Combine(Path.GetTempPath(), "NIsland-update-test");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, u.AssetName);
            var sw = Stopwatch.StartNew();
            await Download(u.AssetUrl, u.AssetApiUrl, file, u.Size, null);
            var sums = await DownloadText(u.SumsUrl, u.SumsApiUrl);
            var expected = sums.Split('\n').First(l => l.TrimEnd().EndsWith(u.AssetName, StringComparison.OrdinalIgnoreCase)).Trim().Split(' ')[0];
            string actual;
            using (var fs = File.OpenRead(file)) actual = Convert.ToHexString(SHA256.HashData(fs));
            log($"download: {new FileInfo(file).Length:N0} bytes in {sw.ElapsedMilliseconds} ms, sha ok={actual.Equals(expected, StringComparison.OrdinalIgnoreCase)}");
            // апидресный путь отдельно
            var f2 = Path.Combine(dir, "api-" + u.AssetName);
            sw.Restart();
            await Download(null, u.AssetApiUrl, f2, u.Size, null);
            string a2;
            using (var fs = File.OpenRead(f2)) a2 = Convert.ToHexString(SHA256.HashData(fs));
            log($"api download: {sw.ElapsedMilliseconds} ms, sha ok={a2.Equals(expected, StringComparison.OrdinalIgnoreCase)}");
            // сервер с ошибкой 504 — повторы и понятный текст
            sw.Restart();
            try { await Download("https://httpbin.org/status/504", null, Path.Combine(dir, "x.bin"), 0, null); log("504: no error?!"); }
            catch (Exception e) { log($"504: {Friendly(e)} after {sw.ElapsedMilliseconds} ms"); }
            try { Directory.Delete(dir, true); } catch { }
            return null;
        }

        /// <summary>После обновления удалить старый exe и временные файлы.</summary>
        public static void Cleanup()
        {
            try { var old = Environment.ProcessPath + ".old"; if (File.Exists(old)) File.Delete(old); } catch { }
            try { var dir = Path.Combine(Path.GetTempPath(), "NIsland-update"); if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        }
    }
}
