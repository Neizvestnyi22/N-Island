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
                if (S.EqOn && g.Any(v => Math.Abs(v) > 0.05))
                {
                    double pre = -Math.Max(0, g.Max()) - 0.5;   // запас, чтобы громкие места не хрипели
                    lines.Add("Preamp: " + pre.ToString("0.0", inv) + " dB");
                    lines.Add("GraphicEQ: " + string.Join("; ", Freqs.Select((f, i) => f.ToString(inv) + " " + g[i].ToString("0.0", inv))));
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
            var exe = InstallDir != null ? Path.Combine(InstallDir, "Configurator.exe") : null;
            if (exe != null && File.Exists(exe))
            {
                try { Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, Verb = "runas" })?.Dispose(); } catch { }
            }
            else SystemService.Run("https://sourceforge.net/p/equalizerapo/wiki/Documentation/");
        }

        /// <summary>
        /// Скачать официальный установщик Equalizer APO (SourceForge) и запустить его — Windows спросит разрешение.
        /// В установщике нужно отметить свои наушники и колонки, после него — перезагрузка.
        /// </summary>
        public static async Task<string> Install(Action<string> progress)
        {
            try
            {
                var dir = Path.Combine(Path.GetTempPath(), "NIsland");
                Directory.CreateDirectory(dir);
                var file = Path.Combine(dir, Environment.Is64BitOperatingSystem ? "EqualizerAPO-x64-setup.exe" : "EqualizerAPO-x86-setup.exe");
                progress?.Invoke("Скачиваю Equalizer APO с официальной страницы…");
                using (var http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 10 }) { Timeout = TimeSpan.FromMinutes(3) })
                {
                    http.DefaultRequestHeaders.UserAgent.ParseAdd("Wget/1.21 (NIsland)");
                    var url = "https://sourceforge.net/projects/equalizerapo/files/latest/download";
                    var bytes = await http.GetByteArrayAsync(url);
                    // защита: это должна быть программа Windows (MZ), а не страница сайта
                    if (bytes.Length < 300_000 || bytes[0] != (byte)'M' || bytes[1] != (byte)'Z') return "SourceForge вернул не установщик — скачай вручную со страницы проекта";
                    await File.WriteAllBytesAsync(file, bytes);
                }
                progress?.Invoke("Запускаю установщик — подтверди в окне Windows…");
                Process.Start(new ProcessStartInfo(file) { UseShellExecute = true, Verb = "runas" })?.Dispose();
                return null;
            }
            catch (System.ComponentModel.Win32Exception) { return "Установка отменена"; }
            catch (Exception e) { Log.Info("eq install: " + e.Message); return "Не получилось скачать: " + Integrations.Reason(e); }
        }
    }
}
