using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace NIsland.Core
{
    /// <summary>Версия, авторы и ссылки — для «О программе» и проверки обновлений.</summary>
    public static class AppInfo
    {
        public const string Version = "1.5.0";
        public const string Credits = "made in TG @Ne1zvestny1i & Claude";
        public const string Telegram = "https://t.me/Ne1zvestny1i";
        /// <summary>owner/repo на GitHub — отсюда берутся релизы для скачивания и проверки обновлений.</summary>
        public const string Repo = "Neizvestnyi22/N-Island";
        public static string RepoUrl => "https://github.com/" + Repo;
        public static string ReleasesUrl => RepoUrl + "/releases/latest";

        static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

        static AppInfo() => Http.DefaultRequestHeaders.UserAgent.ParseAdd("N-Island/" + Version);

        /// <summary>Последний релиз на GitHub: (версия, ссылка) или null, если сеть недоступна.</summary>
        public static async Task<(string Version, string Url, bool Newer)?> CheckUpdate()
        {
            try
            {
                using var doc = JsonDocument.Parse(await Http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest"));
                var tag = doc.RootElement.GetProperty("tag_name").GetString()?.TrimStart('v', 'V') ?? "";
                var url = doc.RootElement.GetProperty("html_url").GetString();
                bool newer = System.Version.TryParse(tag, out var remote) && System.Version.TryParse(Version, out var local) && remote > local;
                return (tag, url, newer);
            }
            catch { return null; }
        }
    }
}
