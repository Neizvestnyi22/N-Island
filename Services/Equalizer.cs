using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Win32;
using NIsland.Core;

namespace NIsland.Services
{
    /// <summary>
    /// Системный эквалайзер: меняет звук ВСЕХ приложений через Equalizer APO (бесплатный открытый движок звука Windows).
    /// N-Island пишет свой файл N-Island.txt в папку настроек APO и один раз подключает его строкой «Include» в config.txt —
    /// APO подхватывает изменения на лету. Свои настройки пользователя в APO не трогаем. Профиль запоминается для каждого
    /// устройства вывода: наушники и колонки звучат каждый по-своему.
    /// </summary>
    public static class Equalizer
    {
        public static readonly int[] Freqs = { 31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000 };
        public const double MaxGain = 12;
        const string FileName = "N-Island.txt";
        const string Include = "Include: " + FileName;

        public record Preset(string Key, string Name, double[] Gains);

        public static readonly Preset[] Presets =
        {
            new("flat", "Ровно", new double[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }),
            new("bass", "Больше баса", new double[] { 6, 5, 4, 2, 0, 0, 0, 0, 0, 0 }),
            new("bassplus", "Мощный бас", new double[] { 9, 8, 6, 3, 0, -1, 0, 0, 0, 0 }),
            new("treble", "Больше верхов", new double[] { 0, 0, 0, 0, 0, 1, 2, 4, 5, 6 }),
            new("vocal", "Голос", new double[] { -2, -2, -1, 1, 3, 4, 4, 3, 1, 0 }),
            new("podcast", "Речь и подкасты", new double[] { -6, -5, -3, 0, 2, 4, 4, 3, 0, -2 }),
            new("rock", "Рок", new double[] { 5, 4, 2, -1, -2, -1, 2, 3, 4, 4 }),
            new("pop", "Поп", new double[] { -1, 1, 3, 4, 3, 0, -1, -1, 0, 1 }),
            new("electro", "Электроника", new double[] { 6, 5, 2, 0, -2, 1, 0, 2, 4, 5 }),
            new("hiphop", "Хип-хоп", new double[] { 6, 5, 2, 3, -1, -1, 1, 0, 2, 3 }),
            new("jazz", "Джаз", new double[] { 3, 2, 1, 2, -1, -1, 0, 1, 2, 3 }),
            new("classic", "Классика", new double[] { 4, 3, 2, 1, -1, -1, 0, 2, 3, 4 }),
            new("loud", "Громче (V-образный)", new double[] { 6, 4, 0, -2, -3, -2, 0, 3, 5, 6 }),
            new("laptop", "Динамики ноутбука", new double[] { -6, -3, 0, 2, 3, 3, 2, 2, 3, 2 }),
            new("headphones", "Наушники", new double[] { 3, 2, 1, 0, -1, 0, 1, 2, 2, 1 }),
            new("night", "Ночь (бас тише)", new double[] { -6, -5, -3, -1, 0, 1, 1, 1, 0, -1 }),
            new("games", "Игры (шаги и звуки)", new double[] { -3, -2, -1, 0, 1, 3, 5, 5, 3, 1 }),
        };

        public static string PresetName(string key) => Presets.FirstOrDefault(p => p.Key == key)?.Name ?? "Своя настройка";

        static Settings S => Store.Settings;
        public static event Action Changed;

        // ------------------------------------------------------------------
        //  Где установлен Equalizer APO
        // ------------------------------------------------------------------

        static string _configDir, _installDir;
        static DateTime _probedAt;

        static void Probe()
        {
            if ((DateTime.Now - _probedAt).TotalSeconds < 5) return;
            _probedAt = DateTime.Now;
            _configDir = _installDir = null;
            try
            {
                using var k = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(@"SOFTWARE\EqualizerAPO");
                _installDir = k?.GetValue("InstallPath") as string;
                _configDir = k?.GetValue("ConfigPath") as string;
            }
            catch { }
            var def = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "EqualizerAPO");
            if (string.IsNullOrEmpty(_installDir) && Directory.Exists(def)) _installDir = def;
            if (string.IsNullOrEmpty(_configDir) && _installDir != null) _configDir = Path.Combine(_installDir, "config");
            if (_configDir != null && !File.Exists(Path.Combine(_configDir, "config.txt")) && !Directory.Exists(_configDir)) _configDir = null;
            // после удаления APO остаётся папка с настройками и ключ реестра — без самого модуля это не «установлен»
            if (_installDir == null || !File.Exists(Path.Combine(_installDir, "EqualizerAPO.dll"))) _configDir = null;
        }

        /// <summary>Установленная версия Equalizer APO (по EqualizerAPO.dll, иначе по имени папки в меню «Пуск»).</summary>
        public static string InstalledVersion
        {
            get
            {
                if (!Installed) return null;
                try
                {
                    var v = FileVersionInfo.GetVersionInfo(Path.Combine(_installDir, "EqualizerAPO.dll"));
                    if (v.FileMajorPart > 0 || v.FileMinorPart > 0) return $"{v.FileMajorPart}.{v.FileMinorPart}.{v.FileBuildPart}";
                }
                catch { }
                try
                {
                    using var k = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(@"SOFTWARE\EqualizerAPO");
                    var m = System.Text.RegularExpressions.Regex.Match(k?.GetValue("Start Menu Folder") as string ?? "", @"(\d+\.\d+(\.\d+)?)");
                    if (m.Success) return m.Groups[1].Value;
                }
                catch { }
                return "?";
            }
        }

        // ------------------------------------------------------------------
        //  Обновления движка с официальной страницы (SourceForge)
        // ------------------------------------------------------------------

        public record ApoRelease(string Version, string Url, string Sha256, long Bytes);

        /// <summary>Последний выпуск Equalizer APO: best_release.json проекта (имя файла, SHA-256, размер).</summary>
        public static async Task<ApoRelease> Latest()
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Wget/1.21 (NIsland)");
            using var doc = System.Text.Json.JsonDocument.Parse(await http.GetStringAsync("https://sourceforge.net/projects/equalizerapo/best_release.json"));
            var r = doc.RootElement.GetProperty("release");
            var name = r.GetProperty("filename").GetString() ?? "";          // "/1.4.2/EqualizerAPO-x64-1.4.2.exe"
            var m = System.Text.RegularExpressions.Regex.Match(name, @"^/([\d.]+)/(EqualizerAPO-x64-[\d.]+\.exe)$");
            if (!m.Success) return null;
            var ver = m.Groups[1].Value;
            string file = m.Groups[2].Value, sha = r.TryGetProperty("sha256sum", out var s) ? s.GetString() : null;
            if (!Environment.Is64BitOperatingSystem) { file = file.Replace("-x64-", "-x86-"); sha = null; }
            long bytes = r.TryGetProperty("bytes", out var b) && b.TryGetInt64(out var bl) ? bl : 0;
            return new ApoRelease(ver, $"https://sourceforge.net/projects/equalizerapo/files/{ver}/{file}/download", sha?.ToLowerInvariant(), bytes);
        }

        /// <summary>a новее b (1.4.10 > 1.4.2).</summary>
        public static bool IsNewer(string a, string b) =>
            Version.TryParse(a, out var va) && (!Version.TryParse(b, out var vb) || va > vb);

        /// <summary>Раз в неделю: если на сайте вышла новая версия — всплывашка «Обновить эквалайзер».</summary>
        public static async Task CheckForUpdateLater()
        {
            try
            {
                if (!Installed || S.EqApoChecked == DateTime.Today.ToString("yyyy-MM-dd")) return;
                if (DateTime.TryParse(S.EqApoChecked, out var last) && (DateTime.Today - last).TotalDays < 7) return;
                await Task.Delay(TimeSpan.FromMinutes(2));
                var rel = await Latest();
                S.EqApoChecked = DateTime.Today.ToString("yyyy-MM-dd");
                if (rel != null && IsNewer(rel.Version, InstalledVersion))
                    System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() => App.Island?.ShowActivity(new Activity
                    {
                        Kind = "alert", Glyph = "\uE896", Title = "Эквалайзер: вышла версия " + rel.Version, Sub = "Установлена " + InstalledVersion,
                        Seconds = 10, Wide = true, OnClick = () => App.ShowSettings("eq"),
                    })));
            }
            catch (Exception e) { Log.Info("eq update check: " + e.Message); }
        }

        public static bool Installed { get { Probe(); return _configDir != null; } }
        public static string ConfigDir { get { Probe(); return _configDir; } }
        public static string InstallDir { get { Probe(); return _installDir; } }

        /// <summary>Что пошло не так при последней записи (null — всё хорошо).</summary>
        public static string LastError { get; private set; }
        /// <summary>В папку APO нельзя писать без прав администратора — можно выдать доступ один раз.</summary>
        public static bool NeedsPermission { get; private set; }

        /// <summary>
        /// Подключён ли APO к текущему устройству вывода (Configurator отмечает устройства при установке).
        /// null — не удалось определить.
        /// </summary>
        public static bool? OnCurrentDevice()
        {
            try
            {
                var id = AudioService.I.OutputId;
                if (string.IsNullOrEmpty(id)) return null;
                var guid = id[(id.LastIndexOf('.') + 1)..];
                using var fx = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                    .OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render\{guid}\FxProperties");
                if (fx == null) return false;
                foreach (var n in fx.GetValueNames())
                    if (fx.GetValue(n) is string s && s.Contains("EACD2258-FCAC-4FF4-B36D-419E924A6D79", StringComparison.OrdinalIgnoreCase)) return true;
                return false;
            }
            catch { return null; }
        }

        // ------------------------------------------------------------------
        //  Состояние
        // ------------------------------------------------------------------

        public static double[] Gains => Parse(S.EqGains);

        public static double[] Parse(string csv)
        {
            var g = new double[Freqs.Length];
            var parts = (csv ?? "").Split(',', StringSplitOptions.TrimEntries);
            for (int i = 0; i < g.Length && i < parts.Length; i++)
                if (double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) g[i] = Math.Clamp(v, -MaxGain, MaxGain);
            return g;
        }

        public static string Format(double[] g) => string.Join(",", g.Select(v => Math.Round(v, 1).ToString(CultureInfo.InvariantCulture)));

        /// <summary>Кривая поменялась (перетаскивание, пресет): запоминаем и сразу применяем.</summary>
        public static void SetGains(double[] gains, string preset = "custom")
        {
            S.EqGains = Format(gains);
            S.EqPreset = preset;
            if (S.EqPerDevice) SaveProfile();
            Apply();
        }

        public static void SetPreset(string key)
        {
            var p = Presets.FirstOrDefault(x => x.Key == key);
            if (p == null) return;
            if (!S.EqOn && key != "flat") S.EqOn = true;
            SetGains(p.Gains.ToArray(), key);
        }

        public static void SetOn(bool on)
        {
            S.EqOn = on;
            Apply();
        }

        static string Device => AudioService.I.OutputName ?? "";

        static void SaveProfile()
        {
            if (Device == "") return;
            Store.Data.EqProfiles ??= new();
            Store.Data.EqProfiles[Device] = S.EqPreset + "|" + S.EqGains + "|" + (S.EqOn ? "1" : "0");
            Store.SaveData();
        }

        /// <summary>Сменилось устройство вывода — берём его профиль (если запоминаем по устройствам).</summary>
        public static void OnOutputChanged(string name)
        {
            if (!S.EqPerDevice || string.IsNullOrEmpty(name)) return;
            if (Store.Data.EqProfiles != null && Store.Data.EqProfiles.TryGetValue(name, out var prof))
            {
                var p = prof.Split('|');
                S.EqPreset = p[0];
                S.EqGains = p.Length > 1 ? p[1] : "";
                S.EqOn = p.Length < 3 || p[2] == "1";
                Apply();
                if (S.EqOn && Installed) App.Island?.Toast("\uE9E9", "Эквалайзер: " + PresetName(S.EqPreset), name);
            }
        }

        public static void Start()
        {
            AudioService.I.OutputChanged += n => System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() => OnOutputChanged(n)));
            // включённый эквалайзер восстанавливаем при запуске (файл мог стереть кто-то ещё)
            if (S.EqOn && Installed) Apply();
        }

        /// <summary>Перед выходом ничего не выключаем: эквалайзер — это звук Windows, он должен работать и без острова.</summary>
        public static string Apply()
        {
            LastError = null;
            NeedsPermission = false;
            var dir = ConfigDir;
            if (dir == null) { LastError = "Equalizer APO не установлен"; Changed?.Invoke(); return LastError; }
            try
            {
                var g = Gains;
                var inv = CultureInfo.InvariantCulture;
                var lines = new List<string>
                {
                    "# Этот файл пишет N-Island (эквалайзер в чёлке). Изменения здесь перезапишутся.",
                    "# This file is written by N-Island. Edit the equalizer in N-Island.",
                };
                bool curve = g.Any(v => Math.Abs(v) > 0.05);
                if (S.EqOn && (curve || S.EqBoost != 0))
                {
                    lines.Add("Preamp: " + Preamp(g).ToString("0.0", inv) + " dB");
                    if (curve) lines.Add("GraphicEQ: " + string.Join("; ", Freqs.Select((f, i) => f.ToString(inv) + " " + g[i].ToString("0.0", inv))));
                }
                else lines.Add("# выключено");
                File.WriteAllLines(Path.Combine(dir, FileName), lines);
                EnsureInclude(dir);
            }
            catch (UnauthorizedAccessException)
            {
                NeedsPermission = true;
                LastError = "Нет доступа к папке Equalizer APO — нажми «Разрешить запись» (один раз, с правами администратора)";
            }
            catch (Exception e) { LastError = "Не удалось сохранить: " + e.Message; Log.Info("eq: " + e.Message); }
            Changed?.Invoke();
            return LastError;
        }

        /// <summary>
        /// Предусилитель. Раньше всегда «минус самая большая прибавка − 0,5 дБ»: с басами +6 дБ весь звук становился на 6,5 дБ тише,
        /// и на ноутбуке казалось, что с эквалайзером тихо. Теперь (по умолчанию) запас — половина прибавки: громкость почти та же,
        /// а хрип возможен только на самых громких местах с сильно поднятыми полосами. Плюс своё усиление (−6…+12 дБ) для тихих динамиков.
        /// </summary>
        public static double Preamp(double[] g)
        {
            double up = Math.Max(0, g.Length > 0 ? g.Max() : 0);
            double pre = S.EqAutoGain ? -up * 0.5 : -up - 0.5;
            return Math.Round(pre + Math.Clamp(S.EqBoost, -6, 12), 1);
        }

        static void EnsureInclude(string dir)
        {
            var cfg = Path.Combine(dir, "config.txt");
            var text = File.Exists(cfg) ? File.ReadAllText(cfg) : "";
            if (text.Split('\n').Any(l => l.Trim().Equals(Include, StringComparison.OrdinalIgnoreCase))) return;
            var backup = cfg + ".before-nisland";
            if (File.Exists(cfg) && !File.Exists(backup)) File.Copy(cfg, backup);
            File.AppendAllText(cfg, (text.Length > 0 && !text.EndsWith("\n") ? "\r\n" : "") + Include + "\r\n");
        }

        /// <summary>Убрать N-Island из APO: пустой файл и строка Include удаляются, config.txt снова как был.</summary>
        public static string Detach()
        {
            var dir = ConfigDir;
            if (dir == null) return null;
            try
            {
                var cfg = Path.Combine(dir, "config.txt");
                if (File.Exists(cfg))
                {
                    var keep = File.ReadAllLines(cfg).Where(l => !l.Trim().Equals(Include, StringComparison.OrdinalIgnoreCase));
                    File.WriteAllLines(cfg, keep);
                }
                var f = Path.Combine(dir, FileName);
                if (File.Exists(f)) File.Delete(f);
                S.EqOn = false;
                Changed?.Invoke();
                return null;
            }
            catch (Exception e) { return e.Message; }
        }

        /// <summary>Выдать обычным пользователям право записи в папку настроек APO (сам APO при установке обычно это делает).</summary>
        public static bool FixPermissions()
        {
            var dir = ConfigDir;
            if (dir == null) return false;
            try
            {
                using var p = Process.Start(new ProcessStartInfo("icacls.exe", $"\"{dir}\" /grant *S-1-5-32-545:(OI)(CI)M") { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden });
                p?.WaitForExit(15000);
                Apply();
                return !NeedsPermission;
            }
            catch { return false; }   // отказались в окне UAC
        }

        /// <summary>Открыть Configurator APO — в нём отмечают устройства, к которым подключается эквалайзер.</summary>
        public static void OpenConfigurator()
        {
            var exe = InstallDir == null ? null : new[] { "Configurator.exe", "DeviceSelector.exe" }.Select(n => Path.Combine(InstallDir, n)).FirstOrDefault(File.Exists);
            if (exe != null)
            {
                try { Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, Verb = "runas" })?.Dispose(); } catch { }
            }
            else SystemService.Run("https://sourceforge.net/p/equalizerapo/wiki/Documentation/");
        }

        /// <summary>
        /// Скачать официальный установщик Equalizer APO (SourceForge) и запустить его — Windows спросит разрешение.
        /// В установщике нужно отметить свои наушники и колонки, после него — перезагрузка.
        /// </summary>
        /// <summary>SHA-256 официального установщика Equalizer APO 1.4.2 x64 (опубликован на SourceForge).</summary>
        const string BundledSha256 = "7403be7427bbe1936a40dded082829b6e217fc4f5990fee5cba501f0ae055afa";
        const string BundledUrl = "https://sourceforge.net/projects/equalizerapo/files/1.4.2/EqualizerAPO-x64-1.4.2.exe/download";

        /// <summary>Установщик вшит в N-Island (сборки x64) — ставится без интернета.</summary>
        public static bool Bundled => Environment.Is64BitOperatingSystem && typeof(Equalizer).Assembly.GetManifestResourceInfo("eqapo.x64.exe") != null;

        internal static bool TestBundledHashOk()
        {
            using var rs = typeof(Equalizer).Assembly.GetManifestResourceStream("eqapo.x64.exe");
            if (rs == null) return false;
            using var ms = new MemoryStream();
            rs.CopyTo(ms);
            return Sha(ms.ToArray()) == BundledSha256;
        }

        static string Sha(byte[] b) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(b)).ToLowerInvariant();

        const string BundledVersion = "1.4.2";

        public static async Task<string> Install(Action<string> progress, ApoRelease rel = null)
        {
            try
            {
                var dir = Path.Combine(Path.GetTempPath(), "NIsland");
                Directory.CreateDirectory(dir);
                var file = Path.Combine(dir, Environment.Is64BitOperatingSystem ? "EqualizerAPO-x64-setup.exe" : "EqualizerAPO-x86-setup.exe");
                byte[] bytes = null;
                if (Bundled && (rel == null || rel.Version == BundledVersion))
                {
                    // встроенная копия — из самого exe N-Island, без скачивания
                    progress?.Invoke("Готовлю встроенный эквалайзер…");
                    using var rs = typeof(Equalizer).Assembly.GetManifestResourceStream("eqapo.x64.exe");
                    using var ms = new MemoryStream();
                    await rs.CopyToAsync(ms);
                    bytes = ms.ToArray();
                    if (Sha(bytes) != BundledSha256) bytes = null;   // повреждён — тогда скачаем
                }
                if (bytes == null)
                {
                    progress?.Invoke("Скачиваю Equalizer APO с официальной страницы…");
                    using var http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 10 }) { Timeout = TimeSpan.FromMinutes(3) };
                    http.DefaultRequestHeaders.UserAgent.ParseAdd("Wget/1.21 (NIsland)");
                    bool x64 = Environment.Is64BitOperatingSystem;
                    var url = rel?.Url ?? (x64 ? BundledUrl : "https://sourceforge.net/projects/equalizerapo/files/latest/download");
                    var want = rel != null ? rel.Sha256 : x64 ? BundledSha256 : null;
                    bytes = await http.GetByteArrayAsync(url);
                    // защита: ровно тот файл, что опубликован (SHA-256 со страницы проекта), иначе хотя бы программа Windows (MZ), а не страница сайта
                    if (want != null ? Sha(bytes) != want : bytes.Length < 300_000 || bytes[0] != (byte)'M' || bytes[1] != (byte)'Z')
                        return "SourceForge вернул не тот файл — попробуй позже или скачай вручную со страницы проекта";
                }
                await File.WriteAllBytesAsync(file, bytes);
                progress?.Invoke("Устанавливаю без лишних окон — подтверди в окне Windows…");
                // тихая установка (/S): без мастера; потом сразу выбор устройств и наша настройка
                using (var p = Process.Start(new ProcessStartInfo(file, "/S") { UseShellExecute = true, Verb = "runas" }))
                    if (p != null) await p.WaitForExitAsync();
                _probedAt = DateTime.MinValue;
                if (!Installed) return "Установщик закрылся, но Equalizer APO не появился — попробуй ещё раз";
                S.EqOn = true;
                Apply();
                if (LastError != null && NeedsPermission) FixPermissions();
                progress?.Invoke("Отметь свои динамики и наушники в окне Equalizer APO, нажми OK и перезагрузи ПК");
                OpenConfigurator();
                return null;
            }
            catch (System.ComponentModel.Win32Exception) { return "Установка отменена"; }
            catch (Exception e) { Log.Info("eq install: " + e.Message); return "Не получилось скачать: " + Integrations.Reason(e); }
        }
    }
}
