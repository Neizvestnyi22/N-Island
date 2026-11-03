using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Win32;
using NIsland.Core;

namespace NIsland.Services
{
    /// <summary>Настройка одной программы: видеокарта, приоритет процессора, совместимость.</summary>
    public record AppTune(string Exe, string Gpu, int Priority, string Compat);

    /// <summary>
    /// Тонкая оптимизация Windows и программ. Те же правила, что у защиты от слежки: только документированные параметры,
    /// исходные значения сохраняются, всё возвращается кнопкой; права администратора — одним запросом на действие.
    /// </summary>
    public static class Optimize
    {
        const string Adv = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
        const string Desk = @"Control Panel\Desktop";

        static RegVal U(string key, string name, object v, RegistryValueKind k = RegistryValueKind.DWord) => new(false, key, name, v, k);
        static RegVal M(string key, string name, object v, RegistryValueKind k = RegistryValueKind.DWord) => new(true, key, name, v, k);

        [DllImport("user32.dll", SetLastError = true)] static extern bool SystemParametersInfo(uint action, uint param, IntPtr pv, uint flags);
        [DllImport("user32.dll", SetLastError = true)] static extern bool SystemParametersInfo(uint action, uint param, int[] pv, uint flags);

        static bool HiberFile()
        {
            try { return File.Exists(Path.Combine(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\", "hiberfil.sys")); } catch { return false; }
        }

        public static readonly PrivacyItem[] Items =
        {
            // ---------- интерфейс ----------
            new() { Key = "lightui", Group = "Интерфейс", Title = "Лёгкий интерфейс", Safety = Safety.Safe,
                    Explain = "Выключает анимации панели задач, тени и полупрозрачное выделение в проводнике, перетаскивание окна «с содержимым». " +
                              "На слабых ноутбуках окна и папки открываются заметно быстрее.",
                    Effect = "Интерфейс станет чуть проще. Часть изменений — после перезапуска проводника (раздел «Вид Windows»).",
                    Values = new[] { U(Adv, "TaskbarAnimations", 0), U(Adv, "ListviewShadow", 0), U(Adv, "ListviewAlphaSelect", 0),
                                     U(Desk, "DragFullWindows", "0", RegistryValueKind.String) },
                    After = on => SystemParametersInfo(0x0025, on ? 0u : 1u, IntPtr.Zero, 3) },   // SPI_SETDRAGFULLWINDOWS
            new() { Key = "quickaccess", Group = "Интерфейс", Title = "Проводник без «недавних» и «частых»", Safety = Safety.Safe,
                    Explain = "Проводник не собирает список недавних файлов и частых папок для «Главной». Открывается быстрее и приватнее.",
                    Effect = "На «Главной» не будет списка недавних файлов.",
                    Values = new[] { U(@"Software\Microsoft\Windows\CurrentVersion\Explorer", "ShowRecent", 0), U(@"Software\Microsoft\Windows\CurrentVersion\Explorer", "ShowFrequent", 0) } },
            new() { Key = "iconsonly", Group = "Интерфейс", Title = "Значки вместо миниатюр", Safety = Safety.Caution,
                    Explain = "Проводник не рисует превью фото и видео. Помогает в папках с тысячами файлов на слабом ноутбуке или HDD.",
                    Effect = "Вместо превью фотографий будут обычные значки.",
                    Values = new[] { U(Adv, "IconsOnly", 1) } },

            // ---------- игры и мышь ----------
            new() { Key = "gamedvr", Group = "Игры и мышь", Title = "Фоновая запись игр (Xbox Game Bar)", Safety = Safety.Safe,
                    Explain = "Windows в фоне записывает последние секунды игры «на всякий случай». Это отнимает FPS и место на диске.",
                    Effect = "Кнопка записи в Game Bar останется, но фоновая запись выключится. +FPS в играх.",
                    Values = new[] { U(@"System\GameConfigStore", "GameDVR_Enabled", 0), U(@"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 0) } },
            new() { Key = "mouseaccel", Group = "Игры и мышь", Title = "Без ускорения мыши", Safety = Safety.Safe,
                    Explain = "Выключает «Повышенную точность установки указателя»: курсор проходит одно и то же расстояние при любой скорости руки. " +
                              "Так любят играть геймеры и работать дизайнеры.",
                    Effect = "Сначала мышь покажется непривычной — через пару минут рука привыкает.",
                    Values = new[] { U(@"Control Panel\Mouse", "MouseSpeed", "0", RegistryValueKind.String), U(@"Control Panel\Mouse", "MouseThreshold1", "0", RegistryValueKind.String),
                                     U(@"Control Panel\Mouse", "MouseThreshold2", "0", RegistryValueKind.String) },
                    After = on => SystemParametersInfo(0x0004, 0, on ? new[] { 0, 0, 0 } : new[] { 6, 10, 1 }, 3) },   // SPI_SETMOUSE
            new() { Key = "netgame", Group = "Игры и мышь", Title = "Сеть и звук без ограничений для игр", Safety = Safety.Caution,
                    Explain = "Windows притормаживает сеть, пока играет музыка или видео, и держит 20% процессора для фоновых задач. " +
                              "Убираем ограничение сети и оставляем фону 10%.",
                    Effect = "Меньше задержек в онлайн-играх. Фоновые задачи во время игры будут работать чуть медленнее.",
                    Values = new[] { M(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "NetworkThrottlingIndex", unchecked((int)0xFFFFFFFF)),
                                     M(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "SystemResponsiveness", 10) } },
            new() { Key = "hags", Group = "Игры и мышь", Title = "Аппаратное планирование видеокарты", Safety = Safety.Caution,
                    Explain = "Видеокарта сама управляет своей памятью — меньше задержек в играх. Работает на современных видеокартах и драйверах.",
                    Effect = "Нужна перезагрузка. Если игры начнут подтормаживать — выключи обратно.",
                    Values = new[] { M(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", 2) } },

            // ---------- фон и память ----------
            new() { Key = "bgapps", Group = "Фон и память", Title = "Фоновые приложения из Microsoft Store", Safety = Safety.Caution,
                    Explain = "Приложения из магазина (Почта, Новости, Фотографии и др.) работают в фоне, даже когда закрыты.",
                    Effect = "Меньше нагрузки и дольше от батареи. Такие приложения перестанут присылать уведомления, пока закрыты.",
                    Values = new[] { U(@"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled", 1),
                                     U(@"Software\Microsoft\Windows\CurrentVersion\Search", "BackgroundAppGlobalToggle", 0) } },
            new() { Key = "widgets", Group = "Фон и память", Title = "Виджеты (новости и погода)", Safety = Safety.Safe,
                    Explain = "Панель виджетов держит в памяти браузерный движок — обычно 100–250 МБ, даже если ты её не открываешь.",
                    Effect = "Значок виджетов исчезнет с панели задач. Погода есть в N-Island.",
                    Values = new[] { M(@"SOFTWARE\Policies\Microsoft\Dsh", "AllowNewsAndInterests", 0) } },
            new() { Key = "edgebg", Group = "Фон и память", Title = "Edge не висит в фоне", Safety = Safety.Safe,
                    Explain = "Edge стартует вместе с Windows («ускорение запуска») и остаётся в памяти после закрытия.",
                    Effect = "Освободится 100–300 МБ памяти. Первый запуск Edge будет на секунду дольше.",
                    Values = new[] { M(@"SOFTWARE\Policies\Microsoft\Edge", "StartupBoostEnabled", 0), M(@"SOFTWARE\Policies\Microsoft\Edge", "BackgroundModeEnabled", 0) } },
            new() { Key = "chromebg", Group = "Фон и память", Title = "Chrome не работает после закрытия", Safety = Safety.Safe,
                    Explain = "Расширения Chrome могут работать в фоне, даже когда все окна закрыты.",
                    Effect = "Chrome полностью закрывается вместе с последним окном.",
                    Values = new[] { M(@"SOFTWARE\Policies\Google\Chrome", "BackgroundModeEnabled", 0) } },
            new() { Key = "sysmain", Group = "Фон и память", Title = "Предзагрузка программ (SysMain)", Safety = Safety.Caution,
                    Explain = "Служба заранее загружает в память часто используемые программы. На SSD выигрыш почти незаметен, " +
                              "а на ноутбуке с 4 ГБ памяти она может её забивать. На HDD — лучше оставить включённой.",
                    Effect = "Чуть больше свободной памяти и меньше активности диска; программы на HDD могут открываться дольше.",
                    Services = new[] { "SysMain" } },
            new() { Key = "search", Group = "Фон и память", Title = "Индексирование поиска", Safety = Safety.Caution,
                    Explain = "Служба поиска постоянно читает файлы, чтобы поиск в Пуске и проводнике был мгновенным. На слабом ноутбуке это заметно грузит диск.",
                    Effect = "Поиск файлов станет медленным, поиск в Outlook может перестать работать.",
                    Services = new[] { "WSearch" } },
            new() { Key = "throttle", Group = "Фон и память", Title = "Без ограничения фоновых процессов", Safety = Safety.Caution,
                    Explain = "Windows притормаживает программы в фоне, чтобы экономить батарею. Выключи, если фоновые задачи (рендер, архивация) работают слишком медленно.",
                    Effect = "Фоновые задачи быстрее, но батарея садится быстрее.",
                    Values = new[] { M(@"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling", "PowerThrottlingOff", 1) } },

            // ---------- диск и интернет ----------
            new() { Key = "storagesense", Group = "Диск и интернет", Title = "Контроль памяти — раз в неделю", Safety = Safety.Safe,
                    Explain = "Встроенная функция Windows сама удаляет временные файлы и старое содержимое корзины.",
                    Effect = "Windows раз в неделю будет тихо убирать мусор.",
                    Values = new[] { U(@"Software\Microsoft\Windows\CurrentVersion\StorageSense\Parameters\StoragePolicy", "01", 1),
                                     U(@"Software\Microsoft\Windows\CurrentVersion\StorageSense\Parameters\StoragePolicy", "2048", 7) } },
            new() { Key = "hibernate", Group = "Диск и интернет", Title = "Выключить гибернацию (освободить место)", Safety = Safety.Caution,
                    Explain = "Файл hiberfil.sys занимает на диске 40–75% от объёма оперативной памяти — на 16 ГБ это 6–12 ГБ.",
                    Effect = "Освободится место, но пропадут гибернация и «быстрый запуск» — включение станет на пару секунд дольше.",
                    OnCmd = new[] { "powercfg /hibernate off" }, OffCmd = new[] { "powercfg /hibernate on" }, State = () => !HiberFile() },
            new() { Key = "faststart", Group = "Диск и интернет", Title = "Выключить «быстрый запуск»", Safety = Safety.Caution,
                    Explain = "«Быстрый запуск» на самом деле не выключает Windows полностью. Из-за этого бывают глюки драйверов, Wi-Fi и проблемы с двумя системами на одном диске.",
                    Effect = "Каждое включение — как чистая перезагрузка, но на 2–5 секунд дольше.",
                    Values = new[] { M(@"SYSTEM\CurrentControlSet\Control\Session Manager\Power", "HiberbootEnabled", 0) } },
            new() { Key = "delivery", Group = "Диск и интернет", Title = "Не раздавать обновления другим", Safety = Safety.Safe,
                    Explain = "Windows раздаёт скачанные обновления чужим компьютерам через твой интернет.",
                    Effect = "Обновления качаются только с серверов Microsoft. Экономит трафик и скорость.",
                    Values = new[] { M(@"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization", "DODownloadMode", 0) } },
            new() { Key = "autoplay", Group = "Диск и интернет", Title = "Без автозапуска флешек", Safety = Safety.Safe,
                    Explain = "Windows не будет ничего открывать и запускать сама при подключении флешки или диска — защита от вирусов на флешках.",
                    Effect = "При подключении флешки открой её сам в проводнике. Уведомление N-Island о подключении останется.",
                    Values = new[] { U(@"Software\Microsoft\Windows\CurrentVersion\Explorer\AutoplayHandlers", "DisableAutoplay", 1) } },
        };

        // ------------------------------------------------------------------
        //  Тонкая настройка программ
        // ------------------------------------------------------------------

        const string GpuKey = @"Software\Microsoft\DirectX\UserGpuPreferences";
        const string Layers = @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";
        const string Ifeo = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options";

        public static readonly (string Key, string Name)[] GpuModes = { ("0", "Авто"), ("1", "Экономная"), ("2", "Мощная") };
        public static readonly (int Key, string Name)[] Priorities = { (0, "Обычный"), (6, "Выше обычного"), (3, "Высокий"), (5, "Ниже обычного") };
        /// <summary>Флаги совместимости, как во вкладке «Совместимость» свойств программы.</summary>
        public static readonly (string Flag, string Name, string Explain)[] CompatFlags =
        {
            ("DISABLEDXMAXIMIZEDWINDOWEDMODE", "Без оптимизации во весь экран", "Помогает от рывков и чёрного экрана в старых играх"),
            ("HIGHDPIAWARE", "Чёткий шрифт при масштабе", "Программа рисует себя сама — без размытия на экранах 125–200%"),
            ("RUNASADMIN", "Всегда от администратора", "Windows будет спрашивать права при каждом запуске"),
        };

        /// <summary>Видеокарта для программы: 0 — решает Windows, 1 — экономная (встроенная), 2 — мощная (дискретная).</summary>
        public static string GetGpu(string exe)
        {
            try { using var k = Registry.CurrentUser.OpenSubKey(GpuKey); var v = k?.GetValue(exe) as string; return v == null ? "0" : v.Contains("GpuPreference=2") ? "2" : v.Contains("GpuPreference=1") ? "1" : "0"; }
            catch { return "0"; }
        }

        public static void SetGpu(string exe, string mode)
        {
            try
            {
                using var k = Registry.CurrentUser.CreateSubKey(GpuKey);
                if (mode == "0") k.DeleteValue(exe, false);
                else k.SetValue(exe, $"GpuPreference={mode};", RegistryValueKind.String);
            }
            catch (Exception e) { Log.Info("gpu pref: " + e.Message); }
        }

        public static List<string> GetCompat(string exe)
        {
            try { using var k = Registry.CurrentUser.OpenSubKey(Layers); return ((k?.GetValue(exe) as string) ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(f => f != "~").ToList(); }
            catch { return new(); }
        }

        public static void SetCompat(string exe, string flag, bool on)
        {
            try
            {
                var flags = GetCompat(exe);
                flags.Remove(flag);
                if (on) flags.Add(flag);
                using var k = Registry.CurrentUser.CreateSubKey(Layers);
                if (flags.Count == 0) k.DeleteValue(exe, false);
                else k.SetValue(exe, "~ " + string.Join(" ", flags), RegistryValueKind.String);
            }
            catch (Exception e) { Log.Info("compat: " + e.Message); }
        }

        /// <summary>Приоритет процессора «навсегда» (Windows ставит его при каждом запуске программы). 0 — обычный (убрать настройку).</summary>
        public static int GetPriority(string exe)
        {
            try { using var k = Registry.LocalMachine.OpenSubKey($@"{Ifeo}\{Path.GetFileName(exe)}\PerfOptions"); return k?.GetValue("CpuPriorityClass") is int i ? i : 0; }
            catch { return 0; }
        }

        public static Task<int> SetPriority(string exe, int cls)
        {
            var key = $@"HKLM\{Ifeo}\{Path.GetFileName(exe)}\PerfOptions";
            return Elevated.Script("priority", new[]
            {
                cls == 0 ? $"reg delete \"{key}\" /f >nul 2>&1" : $"reg add \"{key}\" /v CpuPriorityClass /t REG_DWORD /d {cls} /f >nul",
            });
        }

        /// <summary>Все программы, которым N-Island или пользователь уже что-то настроил.</summary>
        public static List<AppTune> Tuned()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try { using var k = Registry.CurrentUser.OpenSubKey(GpuKey); foreach (var n in k?.GetValueNames() ?? Array.Empty<string>()) if (n.Contains('\\')) set.Add(n); } catch { }
            try { using var k = Registry.CurrentUser.OpenSubKey(Layers); foreach (var n in k?.GetValueNames() ?? Array.Empty<string>()) if (n.Contains('\\')) set.Add(n); } catch { }
            foreach (var e in Store.Data.TunedApps ?? new()) set.Add(e);
            return set.Where(File.Exists).Select(e => new AppTune(e, GetGpu(e), GetPriority(e), string.Join(" ", GetCompat(e)))).OrderBy(a => Path.GetFileName(a.Exe)).ToList();
        }
    }
}
