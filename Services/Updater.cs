using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using NIsland.Core;

namespace NIsland.Services
{
    public record UpdateInfo(string Version, string PageUrl, string Notes, string AssetName, string AssetUrl, long Size, string SumsUrl);

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
                using var resp = await Http.GetAsync($"https://api.github.com/repos/{AppInfo.Repo}/releases/latest");
                if ((int)resp.StatusCode == 404) return (UpdateState.NoReleases, null, null);
                if (!resp.IsSuccessStatusCode) return (UpdateState.Error, null, $"GitHub ответил {(int)resp.StatusCode}");
                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                var r = doc.RootElement;
                var tag = r.GetProperty("tag_name").GetString()?.TrimStart('v', 'V') ?? "";
                string assetName = null, assetUrl = null, sums = null;
                long size = 0;
                foreach (var a in r.GetProperty("assets").EnumerateArray())
                {
                    var name = a.GetProperty("name").GetString() ?? "";
                    var url = a.GetProperty("browser_download_url").GetString();
                    if (name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase)) sums = url;
                    else if (name.EndsWith(Suffix, StringComparison.OrdinalIgnoreCase)) { assetName = name; assetUrl = url; size = a.GetProperty("size").GetInt64(); }
                }
                var info = new UpdateInfo(tag, r.GetProperty("html_url").GetString(), r.TryGetProperty("body", out var b) ? b.GetString() : "",
                                          assetName, assetUrl, size, sums);
                Last = info;
                bool newer = Version.TryParse(tag, out var remote) && Version.TryParse(AppInfo.Version, out var local) && remote > local;
                return (newer ? UpdateState.Available : UpdateState.UpToDate, info, null);
            }
            catch (Exception e) { return (UpdateState.Error, null, e.Message); }
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

                // 1. скачиваем с прогрессом
                using (var resp = await Http.GetAsync(u.AssetUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    resp.EnsureSuccessStatusCode();
                    long total = resp.Content.Headers.ContentLength ?? u.Size;
                    using var src = await resp.Content.ReadAsStreamAsync();
                    using var dst = File.Create(file);
                    var buf = new byte[81920];
                    long done = 0;
                    int n;
                    while ((n = await src.ReadAsync(buf)) > 0)
                    {
                        await dst.WriteAsync(buf.AsMemory(0, n));
                        done += n;
                        if (total > 0) progress?.Invoke(Math.Min(1, (double)done / total));
                    }
                }

                // 2. контрольная сумма из SHA256SUMS.txt — защита от битой или подменённой загрузки
                if (u.SumsUrl != null)
                {
                    var sums = await Http.GetStringAsync(u.SumsUrl);
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
                    return "Не удалось записать новую версию: " + e.Message;
                }

                // 5. перезапуск: новая копия дождётся, пока эта закроется
                Log.Info($"update: {AppInfo.Version} → {u.Version} ({u.AssetName})");
                Store.Flush();
                Process.Start(new ProcessStartInfo(exe, "--restart --autostart --settings about") { UseShellExecute = false });
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(App.Quit));
                return null;
            }
            catch (Exception e) { return "Не получилось обновить: " + e.Message; }
        }

        /// <summary>После обновления удалить старый exe и временные файлы.</summary>
        public static void Cleanup()
        {
            try { var old = Environment.ProcessPath + ".old"; if (File.Exists(old)) File.Delete(old); } catch { }
            try { var dir = Path.Combine(Path.GetTempPath(), "NIsland-update"); if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        }
    }
}
