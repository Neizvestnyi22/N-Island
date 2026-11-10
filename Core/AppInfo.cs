namespace NIsland.Core
{
    /// <summary>Версия, авторы и ссылки — для «О программе» и обновлений (Services/Updater.cs).</summary>
    public static class AppInfo
    {
        /// <summary>Версия из сборки (&lt;Version&gt; в NIsland.csproj) — одна на всё приложение и релизы.</summary>
        public static readonly string Version = typeof(AppInfo).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "0.0.0";
        public static string DisplayVersion => System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(typeof(AppInfo).Assembly)?.InformationalVersion.Split('+')[0]??Version;
        public const string Credits = "made in TG @Ne1zvestny1i & Claude";
        public const string Telegram = "https://t.me/Ne1zvestny1i";
        /// <summary>owner/repo на GitHub — отсюда берутся релизы для скачивания и обновлений.</summary>
        public const string Repo = "Neizvestnyi22/N-Island";
        public static string RepoUrl => "https://github.com/" + Repo;
        public static string ReleasesUrl => RepoUrl + "/releases/latest";
    }
}
