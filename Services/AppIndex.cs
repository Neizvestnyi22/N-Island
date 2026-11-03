using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Media;
using NIsland.Core;

namespace NIsland.Services
{
    /// <summary>
    /// Программы для быстрого поиска: ярлыки меню «Пуск» (с иконками) и приложения Store через Get-StartApps.
    /// Список кешируется на диске и тихо обновляется раз в сутки.
    /// </summary>
    public static class AppIndex
    {
        public record App(string Name, string Target, string IconPath);

        static List<App> _apps = new();
        static Task _loading;
        static DateTime _loadedAt;
        static readonly Dictionary<string, ImageSource> Icons = new(StringComparer.OrdinalIgnoreCase);
        static readonly string CacheFile = Path.Combine(Store.Dir, "apps.json");
        static readonly Regex Junk = new(@"uninst|деинстал|удал(ить|ение)|видал|readme|help|справк|документац|website|веб-сайт|release notes|license|лицензи|changelog|what's new|support|поддержк",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static IReadOnlyList<App> Apps
        {
            get
            {
                if (_apps.Count == 0 && _loading == null) Refresh();
                else if ((DateTime.Now - _loadedAt).TotalHours > 6 && (_loading == null || _loading.IsCompleted)) Refresh();
                return _apps;
            }
        }

        public static void Refresh()
        {
            if (_apps.Count == 0) TryCache();
            _loading = Task.Run(Scan).ContinueWith(t =>
            {
                if (t.Result.Count > 0) { _apps = t.Result; Save(); }
                _loadedAt = DateTime.Now;
            });
        }

        static void TryCache()
        {
            try
            {
                if (!File.Exists(CacheFile)) return;
                var list = JsonSerializer.Deserialize<List<App>>(File.ReadAllText(CacheFile));
                if (list?.Count > 0) _apps = list;
            }
            catch { }
        }

        static void Save()
        {
            try { File.WriteAllText(CacheFile, JsonSerializer.Serialize(_apps)); } catch { }
        }

        static List<App> Scan()
        {
            var res = new Dictionary<string, App>(StringComparer.OrdinalIgnoreCase);
            foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), Environment.GetFolderPath(Environment.SpecialFolder.StartMenu) })
            {
                try
                {
                    foreach (var f in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
                    {
                        var ext = Path.GetExtension(f).ToLowerInvariant();
                        if (ext is not (".lnk" or ".url" or ".appref-ms")) continue;
                        var name = Path.GetFileNameWithoutExtension(f);
                        if (Junk.IsMatch(name)) continue;
                        res.TryAdd(name, new App(name, f, f));
                    }
                }
                catch { }
            }
            // приложения из Microsoft Store (Калькулятор, Фото, WhatsApp, Telegram из Store…) — ярлыков у них нет
            try
            {
                using var p = Process.Start(new ProcessStartInfo("powershell.exe",
                    "-NoProfile -NonInteractive -Command \"[Console]::OutputEncoding=[Text.Encoding]::UTF8; Get-StartApps | ConvertTo-Json -Compress\"")
                {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, StandardOutputEncoding = Encoding.UTF8,
                });
                var json = p.StandardOutput.ReadToEnd();
                p.WaitForExit(15000);
                using var doc = JsonDocument.Parse(json);
                var arr = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray() : Enumerable.Repeat(doc.RootElement, 1);
                foreach (var a in arr)
                {
                    var name = a.GetProperty("Name").GetString();
                    var id = a.GetProperty("AppID").GetString();
                    if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(id) || Junk.IsMatch(name)) continue;
                    if (res.ContainsKey(name)) continue;   // уже есть ярлык с иконкой
                    if (!id.Contains('!') && !id.StartsWith("{")) continue;   // классические программы уже нашлись по ярлыкам
                    res[name] = new App(name, "shell:AppsFolder\\" + id, null);
                }
            }
            catch (Exception e) { Log.Info("apps: " + e.Message); }
            return res.Values.OrderBy(a => a.Name).ToList();
        }

        public static ImageSource Icon(App a)
        {
            if (a.IconPath == null) return null;
            if (Icons.TryGetValue(a.IconPath, out var i)) return i;
            var img = FileIcons.Get(a.IconPath);
            if (Icons.Count < 300) Icons[a.IconPath] = img;
            return img;
        }
    }
}
