using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Win32;
using NIsland.Core;

namespace NIsland.Services
{
    /// <summary>Значение реестра: Machine — HKLM (нужны права администратора), иначе HKCU.</summary>
    public record RegVal(bool Machine, string Key, string Name, object Value, RegistryValueKind Kind = RegistryValueKind.DWord);

    /// <summary>Пункт защиты от слежки: что выключаем, зачем и что изменится для тебя.</summary>
    public class PrivacyItem
    {
        public string Key, Group, Title, Explain, Effect;
        public Safety Safety;
        public RegVal[] Values = Array.Empty<RegVal>();
        public string[] Services;   // службы, которые выключаем (тип запуска «Отключена»)
        public string[] Tasks;      // задания планировщика, которые выключаем
        public string[] OnCmd, OffCmd;   // команды с правами администратора: включить пункт / вернуть как было
        public Func<bool> State;         // своя проверка «пункт включён» (для команд)
        public Action<bool> After;       // применить сразу, без перезахода в Windows (SystemParametersInfo и т. п.)
        public bool Admin => Values.Any(v => v.Machine || v.Key.StartsWith(@"Software\Policies", StringComparison.OrdinalIgnoreCase))
                             || Services != null || Tasks != null || OnCmd != null;
    }

    public record AccessUse(string App, string Kind, DateTime Last, bool InUse);

    /// <summary>
    /// Защита от слежки: только документированные политики и параметры Windows, без взлома системных файлов.
    /// Перед любым изменением исходные значения сохраняются в privacy-backup.json — всё отменяется одной кнопкой.
    /// </summary>
    public static class Privacy
    {
        const string Cdm = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";
        const string Adv = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
        const string Pol = @"SOFTWARE\Policies\Microsoft\Windows";

        static RegVal U(string key, string name, object v, RegistryValueKind k = RegistryValueKind.DWord) => new(false, key, name, v, k);
        static RegVal M(string key, string name, object v, RegistryValueKind k = RegistryValueKind.DWord) => new(true, key, name, v, k);

        public static readonly PrivacyItem[] Items =
        {
            // ---------- без прав администратора ----------
            new() { Key = "adid", Group = "Реклама", Title = "Рекламный идентификатор", Safety = Safety.Safe,
                    Explain = "Уникальный номер, по которому приложения узнают тебя и показывают «персональную» рекламу.",
                    Effect = "Реклама в приложениях останется, но перестанет подстраиваться под тебя.",
                    Values = new[] { U(@"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", 0) } },
            new() { Key = "ads", Group = "Реклама", Title = "Реклама и «советы» Windows", Safety = Safety.Safe,
                    Explain = "Предложения в Пуске, параметрах и на экране блокировки, «Добро пожаловать в Windows», подсказки после обновлений.",
                    Effect = "Меньше рекламы и всплывающих подсказок. На работу системы не влияет.",
                    Values = new[] { U(Cdm, "SubscribedContent-338388Enabled", 0), U(Cdm, "SubscribedContent-338389Enabled", 0), U(Cdm, "SubscribedContent-338393Enabled", 0),
                                     U(Cdm, "SubscribedContent-353694Enabled", 0), U(Cdm, "SubscribedContent-353696Enabled", 0), U(Cdm, "SubscribedContent-310093Enabled", 0),
                                     U(Cdm, "SubscribedContent-338387Enabled", 0), U(Cdm, "SystemPaneSuggestionsEnabled", 0), U(Cdm, "SoftLandingEnabled", 0),
                                     U(Cdm, "RotatingLockScreenOverlayEnabled", 0),
                                     U(@"Software\Microsoft\Windows\CurrentVersion\UserProfileEngagement", "ScoobeSystemSettingEnabled", 0) } },
            new() { Key = "silent", Group = "Реклама", Title = "Тихая установка рекламных приложений", Safety = Safety.Safe,
                    Explain = "Windows сама ставит «рекомендуемые» игры и программы (Candy Crush, TikTok и т. п.) в меню Пуск.",
                    Effect = "Новые рекламные приложения перестанут появляться сами. Уже установленные можно удалить в разделе «Программы».",
                    Values = new[] { U(Cdm, "SilentInstalledAppsEnabled", 0), U(Cdm, "PreInstalledAppsEnabled", 0), U(Cdm, "OemPreInstalledAppsEnabled", 0), U(Cdm, "PreInstalledAppsEverEnabled", 0) } },
            new() { Key = "startads", Group = "Реклама", Title = "Рекламные рекомендации в Пуске и проводнике", Safety = Safety.Safe,
                    Explain = "Блок «Рекомендуем», уведомления учётной записи в Пуске и реклама OneDrive/Microsoft 365 в проводнике.",
                    Effect = "Пуск и проводник станут чище. Применится после перезапуска проводника.",
                    Values = new[] { U(Adv, "Start_IrisRecommendations", 0), U(Adv, "Start_AccountNotifications", 0), U(Adv, "ShowSyncProviderNotifications", 0) } },
            new() { Key = "tailored", Group = "Сбор данных", Title = "Персональные советы по твоим данным", Safety = Safety.Safe,
                    Explain = "Microsoft использует данные диагностики, чтобы подбирать тебе советы и рекламу.",
                    Effect = "Ничего заметного — просто меньше данных уходит на сторону.",
                    Values = new[] { U(@"Software\Microsoft\Windows\CurrentVersion\Privacy", "TailoredExperiencesWithDiagnosticDataEnabled", 0) } },
            new() { Key = "track", Group = "Сбор данных", Title = "Отслеживание запуска приложений", Safety = Safety.Safe,
                    Explain = "Windows запоминает, какие программы ты открываешь, чтобы показывать «часто используемые».",
                    Effect = "Список «Часто используемые» в Пуске перестанет обновляться.",
                    Values = new[] { U(Adv, "Start_TrackProgs", 0) } },
            new() { Key = "feedback", Group = "Сбор данных", Title = "Запросы отзывов", Safety = Safety.Safe,
                    Explain = "Окна «Оцените Windows» и опросы от Microsoft.",
                    Effect = "Опросы больше не будут появляться.",
                    Values = new[] { U(@"Software\Microsoft\Siuf\Rules", "NumberOfSIUFInPeriod", 0), U(@"Software\Microsoft\Siuf\Rules", "PeriodInNanoSeconds", 0, RegistryValueKind.QWord) } },
            new() { Key = "typing", Group = "Сбор данных", Title = "Сбор набранного и рукописного текста", Safety = Safety.Safe,
                    Explain = "Windows изучает, как ты печатаешь и пишешь пером, и может отправлять образцы в Microsoft.",
                    Effect = "Подсказки при наборе станут чуть менее «личными».",
                    Values = new[] { U(@"Software\Microsoft\InputPersonalization", "RestrictImplicitTextCollection", 1), U(@"Software\Microsoft\InputPersonalization", "RestrictImplicitInkCollection", 1),
                                     U(@"Software\Microsoft\InputPersonalization\TrainedDataStore", "HarvestContacts", 0), U(@"Software\Microsoft\Personalization\Settings", "AcceptedPrivacyPolicy", 0),
                                     U(@"Software\Microsoft\Input\TIPC", "Enabled", 0) } },
            new() { Key = "lang", Group = "Сбор данных", Title = "Список языков для сайтов", Safety = Safety.Safe,
                    Explain = "Сайты могут узнавать, какие языки установлены у тебя в Windows, — это помогает отличать тебя от других.",
                    Effect = "Сайты будут подбирать язык по настройкам браузера.",
                    Values = new[] { U(@"Control Panel\International\User Profile", "HttpAcceptLanguageOptOut", 1) } },
            new() { Key = "bing", Group = "Сбор данных", Title = "Поиск Bing в меню Пуск", Safety = Safety.Safe,
                    Explain = "Всё, что ты вводишь в поиск Пуска, отправляется в Bing — даже если ты ищешь файл на компьютере.",
                    Effect = "Поиск Пуска будет искать только на компьютере — быстрее и приватнее.",
                    Values = new[] { U(@"Software\Microsoft\Windows\CurrentVersion\Search", "BingSearchEnabled", 0), U(@"Software\Microsoft\Windows\CurrentVersion\Search", "CortanaConsent", 0) } },
            new() { Key = "tips", Group = "Сбор данных", Title = "Уведомления «Советы и предложения»", Safety = Safety.Safe,
                    Explain = "Всплывающие уведомления с подсказками и предложениями сервисов Microsoft.",
                    Effect = "Меньше лишних уведомлений.",
                    Values = new[] { U(@"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\Windows.SystemToast.Suggested", "Enabled", 0) } },
            new() { Key = "speech", Group = "Голос и место", Title = "Онлайн-распознавание речи", Safety = Safety.Caution,
                    Explain = "Голос отправляется на серверы Microsoft для распознавания.",
                    Effect = "Голосовой ввод Win+H и голосовой доступ перестанут работать онлайн.",
                    Values = new[] { U(@"Software\Microsoft\Speech_OneCore\Settings\OnlineSpeechPrivacy", "HasAccepted", 0) } },
            new() { Key = "cloudclip", Group = "Голос и место", Title = "Синхронизация буфера обмена в облако", Safety = Safety.Caution,
                    Explain = "Скопированный текст (в том числе пароли!) может уходить в облако Microsoft для других твоих устройств.",
                    Effect = "Буфер обмена перестанет синхронизироваться между устройствами.",
                    Values = new[] { U(@"Software\Microsoft\Clipboard", "CloudClipboardAutomaticUpload", 0) } },
            new() { Key = "location", Group = "Голос и место", Title = "Доступ приложений к местоположению", Safety = Safety.Caution,
                    Explain = "Приложения из Microsoft Store могут узнавать, где ты находишься.",
                    Effect = "Карты, погода и «Найти устройство» перестанут определять место автоматически. Чёлка берёт погоду по городу — её это не затронет.",
                    Values = new[] { U(@"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\location", "Value", "Deny", RegistryValueKind.String) } },

            // ---------- политики Windows (нужны права администратора) ----------
            new() { Key = "telemetry", Group = "Телеметрия (админ)", Title = "Телеметрия Windows — минимум", Safety = Safety.Safe,
                    Explain = "Windows постоянно отправляет в Microsoft сведения о работе системы и программ. Ставим «только обязательные данные» — " +
                              "это официальный минимум для Home и Pro.",
                    Effect = "Ничего не ломается. В параметрах может появиться надпись «Некоторыми параметрами управляет организация» — это нормально.",
                    Values = new[] { M(Pol + @"\DataCollection", "AllowTelemetry", 1), M(Pol + @"\DataCollection", "MaxTelemetryAllowed", 1),
                                     M(Pol + @"\DataCollection", "DoNotShowFeedbackNotifications", 1), M(Pol + @"\DataCollection", "AllowDeviceNameInTelemetry", 0) } },
            new() { Key = "activity", Group = "Телеметрия (админ)", Title = "Журнал действий", Safety = Safety.Safe,
                    Explain = "Windows записывает, какие файлы, сайты и программы ты открывал, и может отправлять это в облако.",
                    Effect = "История действий перестанет собираться.",
                    Values = new[] { M(Pol + @"\System", "EnableActivityFeed", 0), M(Pol + @"\System", "PublishUserActivities", 0), M(Pol + @"\System", "UploadUserActivities", 0) } },
            new() { Key = "ceip", Group = "Телеметрия (админ)", Title = "Программа улучшения качества", Safety = Safety.Safe,
                    Explain = "Старая программа сбора статистики использования (CEIP) и обмен рукописными данными.",
                    Effect = "Ничего заметного.",
                    Values = new[] { M(@"SOFTWARE\Policies\Microsoft\SQMClient\Windows", "CEIPEnable", 0), M(Pol + @"\TabletPC", "PreventHandwritingDataSharing", 1),
                                     M(Pol + @"\HandwritingErrorReports", "PreventHandwritingErrorReports", 1), M(Pol + @"\AppCompat", "AITEnable", 0) } },
            new() { Key = "adpolicy", Group = "Телеметрия (админ)", Title = "Реклама и «потребительские функции» для всех", Safety = Safety.Safe,
                    Explain = "Политика, которая запрещает рекламный идентификатор и автоматическую установку рекламных приложений для всех пользователей.",
                    Effect = "Пункт «Рекламный идентификатор» в параметрах станет недоступен.",
                    Values = new[] { M(Pol + @"\AdvertisingInfo", "DisabledByGroupPolicy", 1), M(Pol + @"\CloudContent", "DisableWindowsConsumerFeatures", 1),
                                     M(Pol + @"\CloudContent", "DisableConsumerAccountStateContent", 1), M(Pol + @"\CloudContent", "DisableSoftLanding", 1) } },
            new() { Key = "websearch", Group = "Телеметрия (админ)", Title = "Интернет-поиск в Пуске — политикой", Safety = Safety.Safe,
                    Explain = "Надёжный способ выключить Bing в поиске Windows 11 (обычная настройка иногда возвращается после обновлений).",
                    Effect = "Поиск Пуска перестанет показывать результаты из интернета.",
                    Values = new[] { M(Pol + @"\Windows Search", "DisableWebSearch", 1), M(Pol + @"\Windows Search", "ConnectedSearchUseWeb", 0), M(Pol + @"\Windows Search", "AllowCortana", 0),
                                     U(@"Software\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions", 1) } },
            new() { Key = "copilot", Group = "ИИ Windows (админ)", Title = "Copilot в Windows", Safety = Safety.Safe,
                    Explain = "Встроенный ИИ-помощник Microsoft. В N-Island есть свой ИИ — Copilot можно выключить, если не пользуешься.",
                    Effect = "Кнопка и боковая панель Copilot исчезнут.",
                    Values = new[] { M(Pol + @"\WindowsCopilot", "TurnOffWindowsCopilot", 1), U(@"Software\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", 1),
                                     U(Adv, "ShowCopilotButton", 0) } },
            new() { Key = "recall", Group = "ИИ Windows (админ)", Title = "Recall — снимки экрана для ИИ", Safety = Safety.Safe,
                    Explain = "Функция Copilot+ ПК, которая каждые несколько секунд делает снимок экрана и запоминает всё, что ты видел.",
                    Effect = "Снимки не будут сохраняться. На обычных ноутбуках функции нет — пункт просто ничего не изменит.",
                    Values = new[] { M(Pol + @"\WindowsAI", "DisableAIDataAnalysis", 1), M(Pol + @"\WindowsAI", "AllowRecallEnablement", 0), U(@"Software\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis", 1) } },
            new() { Key = "diagtrack", Group = "Службы и задания (админ)", Title = "Служба телеметрии (DiagTrack)", Safety = Safety.Caution,
                    Explain = "Служба «Функциональные возможности для подключённых пользователей и телеметрия» — она и отправляет данные в Microsoft.",
                    Effect = "Безопасно для большинства. Перестанут работать «Средство просмотра диагностических данных» и отправка отзывов. Отменяется кнопкой «Вернуть».",
                    Services = new[] { "DiagTrack", "dmwappushservice" } },
            new() { Key = "tasks", Group = "Службы и задания (админ)", Title = "Задания сбора данных в планировщике", Safety = Safety.Caution,
                    Explain = "Фоновые задачи, которые по расписанию собирают сведения о программах и оборудовании.",
                    Effect = "Перед большими обновлениями Windows может хуже предупреждать о несовместимых программах.",
                    Tasks = new[] { @"\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser", @"\Microsoft\Windows\Application Experience\ProgramDataUpdater",
                                    @"\Microsoft\Windows\Customer Experience Improvement Program\Consolidator", @"\Microsoft\Windows\Customer Experience Improvement Program\UsbCeip",
                                    @"\Microsoft\Windows\DiskDiagnostic\Microsoft-Windows-DiskDiagnosticDataCollector", @"\Microsoft\Windows\Feedback\Siuf\DmClient",
                                    @"\Microsoft\Windows\Feedback\Siuf\DmClientOnScenarioDownload" } },
            new() { Key = "wer", Group = "Службы и задания (админ)", Title = "Отправка отчётов об ошибках", Safety = Safety.Caution,
                    Explain = "Когда программа вылетает, Windows отправляет отчёт (иногда с кусками памяти) в Microsoft.",
                    Effect = "Ты не будешь получать «решения проблем» от Windows, а разработчики — отчёты о сбоях.",
                    Values = new[] { M(Pol + @"\Windows Error Reporting", "Disabled", 1), M(@"SOFTWARE\Microsoft\Windows\Windows Error Reporting", "Disabled", 1) } },
            new() { Key = "edge", Group = "Службы и задания (админ)", Title = "Сбор данных в Microsoft Edge", Safety = Safety.Caution,
                    Explain = "Edge отправляет диагностику, историю для «персонализации» и отзывы.",
                    Effect = "В настройках Edge появится «Браузер управляется вашей организацией» — это просто значит, что действует политика.",
                    Values = new[] { M(@"SOFTWARE\Policies\Microsoft\Edge", "DiagnosticData", 0), M(@"SOFTWARE\Policies\Microsoft\Edge", "PersonalizationReportingEnabled", 0),
                                     M(@"SOFTWARE\Policies\Microsoft\Edge", "UserFeedbackAllowed", 0), M(@"SOFTWARE\Policies\Microsoft\Edge", "ShowRecommendationsEnabled", 0) } },
        };

        // ------------------------------------------------------------------
        //  Состояние
        // ------------------------------------------------------------------

        static RegistryKey Root(bool machine) => machine ? Registry.LocalMachine : Registry.CurrentUser;

        static object Read(RegVal v, out bool exists)
        {
            exists = false;
            try
            {
                using var k = Root(v.Machine).OpenSubKey(v.Key);
                var o = k?.GetValue(v.Name);
                exists = o != null;
                return o;
            }
            catch { return null; }
        }

        static bool Matches(RegVal v)
        {
            var cur = Read(v, out bool ex);
            if (!ex) return false;
            return v.Kind == RegistryValueKind.String ? string.Equals(cur as string, v.Value as string, StringComparison.OrdinalIgnoreCase)
                                                      : Convert.ToInt64(cur) == Convert.ToInt64(v.Value);
        }

        static int ServiceStart(string name)
        {
            try { using var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + name); return k?.GetValue("Start") is int i ? i : -1; }
            catch { return -1; }
        }

        public static bool IsProtected(PrivacyItem i)
        {
            if (i.State != null) { try { return i.State(); } catch { return false; } }
            if (i.OnCmd != null && i.Values.Length == 0 && !LoadBackup().ContainsKey("cmd:" + i.Key)) return false;
            if (i.Values.Length > 0 && !i.Values.All(Matches)) return false;
            if (i.Services != null && !i.Services.All(s => ServiceStart(s) is 4 or -1)) return false;
            if (i.Tasks != null && !LoadBackup().ContainsKey("task:" + i.Key)) return false;
            return true;
        }

        /// <summary>Сколько пунктов защиты включено: (включено, всего).</summary>
        public static (int On, int Total) Score()
        {
            int on = Items.Count(IsProtected);
            return (on, Items.Length);
        }

        // ------------------------------------------------------------------
        //  Резервная копия
        // ------------------------------------------------------------------

        class Saved { public bool Existed { get; set; } public string Kind { get; set; } public string Value { get; set; } }

        static string BackupFile { get { var d = Path.Combine(Store.Dir, "backup"); Directory.CreateDirectory(d); return Path.Combine(d, "privacy-backup.json"); } }

        static Dictionary<string, Saved> LoadBackup()
        {
            try { return JsonSerializer.Deserialize<Dictionary<string, Saved>>(File.ReadAllText(BackupFile)) ?? new(); }
            catch { return new(); }
        }

        static void SaveBackup(Dictionary<string, Saved> b) => File.WriteAllText(BackupFile, JsonSerializer.Serialize(b, new JsonSerializerOptions { WriteIndented = true }));

        static string Id(RegVal v) => (v.Machine ? "HKLM\\" : "HKCU\\") + v.Key + "\\" + v.Name;

        public static bool HasBackup(PrivacyItem i)
        {
            var b = LoadBackup();
            return i.Values.Any(v => b.ContainsKey(Id(v))) || i.Services?.Any(s => b.ContainsKey("svc:" + s)) == true || b.ContainsKey("task:" + i.Key)
                   || b.ContainsKey("cmd:" + i.Key);
        }

        // ------------------------------------------------------------------
        //  Применение и откат
        // ------------------------------------------------------------------

        /// <summary>Включает защиту. Пункты с HKLM, службами и заданиями применяются одним запросом прав администратора.</summary>
        public static async Task<string> Protect(IList<PrivacyItem> items)
        {
            var backup = LoadBackup();
            var reg = new StringBuilder();
            var cmd = new List<string>();
            int local = 0;
            foreach (var i in items)
            {
                foreach (var v in i.Values)
                {
                    var id = Id(v);
                    if (!backup.ContainsKey(id))
                    {
                        var cur = Read(v, out bool ex);
                        backup[id] = new Saved { Existed = ex, Kind = v.Kind.ToString(), Value = cur == null ? null : Convert.ToString(cur, System.Globalization.CultureInfo.InvariantCulture) };
                    }
                    if (v.Machine || v.Key.StartsWith(@"Software\Policies", StringComparison.OrdinalIgnoreCase)) AppendReg(reg, v, v.Value);
                    else { WriteUser(v, v.Value); local++; }
                }
                foreach (var s in i.Services ?? Array.Empty<string>())
                {
                    if (ServiceStart(s) < 0) continue;
                    if (!backup.ContainsKey("svc:" + s)) backup["svc:" + s] = new Saved { Existed = true, Value = ServiceStart(s).ToString() };
                    cmd.Add($"sc config {s} start= disabled >nul");
                    cmd.Add($"sc stop {s} >nul 2>&1");
                }
                if (i.Tasks != null)
                {
                    foreach (var t in i.Tasks) cmd.Add($"schtasks /Change /TN \"{t}\" /Disable >nul 2>&1");
                }
                if (i.OnCmd != null) cmd.AddRange(i.OnCmd);
            }
            SaveBackup(backup);
            var res = await RunAdmin(reg, cmd, "tune-on");
            if (res == false) return local > 0 ? "Личные настройки применены. Системные — нет: не дано разрешение администратора" : "Не дано разрешение администратора — ничего не изменено";
            if (res == true)
            {
                var b = LoadBackup();
                foreach (var i in items.Where(i => i.Tasks != null)) b["task:" + i.Key] = new Saved { Existed = true, Value = "disabled" };
                foreach (var i in items.Where(i => i.OnCmd != null)) b["cmd:" + i.Key] = new Saved { Existed = true, Value = "on" };
                SaveBackup(b);
            }
            foreach (var i in items) try { i.After?.Invoke(true); } catch { }
            Tweaks.Notify();
            return "Готово ✓ Исходные значения сохранены — всё можно вернуть";
        }

        /// <summary>Возвращает значения, которые были до N-Island.</summary>
        public static async Task<string> Restore(IList<PrivacyItem> items)
        {
            var backup = LoadBackup();
            var reg = new StringBuilder();
            var cmd = new List<string>();
            var done = new List<string>();
            foreach (var i in items)
            {
                foreach (var v in i.Values)
                {
                    var id = Id(v);
                    if (!backup.TryGetValue(id, out var s)) continue;
                    object old = !s.Existed ? null : v.Kind switch
                    {
                        RegistryValueKind.String => s.Value,
                        RegistryValueKind.QWord => long.TryParse(s.Value, out var q) ? q : 0L,
                        _ => int.TryParse(s.Value, out var d) ? d : 0,
                    };
                    if (v.Machine || v.Key.StartsWith(@"Software\Policies", StringComparison.OrdinalIgnoreCase)) AppendReg(reg, v, old);
                    else WriteUser(v, old);
                    done.Add(id);
                }
                foreach (var s in i.Services ?? Array.Empty<string>())
                {
                    if (!backup.TryGetValue("svc:" + s, out var b)) continue;
                    var start = b.Value switch { "2" => "auto", "3" => "demand", "4" => "disabled", _ => "demand" };
                    cmd.Add($"sc config {s} start= {start} >nul");
                    if (start == "auto") cmd.Add($"sc start {s} >nul 2>&1");
                    done.Add("svc:" + s);
                }
                if (i.Tasks != null && backup.ContainsKey("task:" + i.Key))
                {
                    foreach (var t in i.Tasks) cmd.Add($"schtasks /Change /TN \"{t}\" /Enable >nul 2>&1");
                    done.Add("task:" + i.Key);
                }
                // команды откатываем, если включали их мы — или если пункт включён, а пользователь просит вернуть (например, гибернация)
                if (i.OffCmd != null && (backup.ContainsKey("cmd:" + i.Key) || i.State?.Invoke() == true))
                {
                    cmd.AddRange(i.OffCmd);
                    done.Add("cmd:" + i.Key);
                }
            }
            var res = await RunAdmin(reg, cmd, "tune-off");
            if (res == false) return "Не дано разрешение администратора — системные пункты не возвращены";
            var after = LoadBackup();
            foreach (var d in done.Where(d => res == true || d.StartsWith("HKCU\\") && !d.Contains(@"\Software\Policies\")))
                after.Remove(d);
            SaveBackup(after);
            foreach (var i in items) try { i.After?.Invoke(false); } catch { }
            Tweaks.Notify();
            return "Возвращено как было ✓";
        }

        /// <summary>null — администратор не понадобился, true — успешно, false — отказ.</summary>
        static async Task<bool?> RunAdmin(StringBuilder reg, List<string> cmd, string name)
        {
            if (reg.Length == 0 && cmd.Count == 0) return null;
            var lines = new List<string>();
            if (reg.Length > 0)
            {
                var dir = Path.Combine(Store.Dir, "admin");
                Directory.CreateDirectory(dir);
                var file = Path.Combine(dir, $"{name}-{DateTime.Now:yyyyMMdd-HHmmss}.reg");
                File.WriteAllText(file, "Windows Registry Editor Version 5.00\r\n\r\n" + reg, Encoding.Unicode);
                lines.Add($"reg import \"{file}\" >nul 2>&1");
            }
            lines.AddRange(cmd);
            int code = await Elevated.Script(name, lines);
            return code >= 0;
        }

        static void AppendReg(StringBuilder sb, RegVal v, object value)
        {
            sb.Append('[').Append(v.Machine ? "HKEY_LOCAL_MACHINE\\" : "HKEY_CURRENT_USER\\").Append(v.Key).Append("]\r\n");
            sb.Append('"').Append(v.Name.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append("\"=");
            if (value == null) sb.Append('-');
            else if (v.Kind == RegistryValueKind.String) sb.Append('"').Append(value.ToString().Replace("\\", "\\\\").Replace("\"", "\\\"")).Append('"');
            else if (v.Kind == RegistryValueKind.QWord)
            {
                var bytes = BitConverter.GetBytes(Convert.ToInt64(value));
                sb.Append("hex(b):").Append(string.Join(",", bytes.Select(b => b.ToString("x2"))));
            }
            else sb.Append("dword:").Append(unchecked((uint)Convert.ToInt32(value)).ToString("x8"));
            sb.Append("\r\n\r\n");
        }

        static void WriteUser(RegVal v, object value)
        {
            try
            {
                if (value == null)
                {
                    using var k = Registry.CurrentUser.OpenSubKey(v.Key, true);
                    k?.DeleteValue(v.Name, false);
                }
                else
                {
                    using var k = Registry.CurrentUser.CreateSubKey(v.Key);
                    k.SetValue(v.Name, v.Kind == RegistryValueKind.QWord ? Convert.ToInt64(value) : v.Kind == RegistryValueKind.String ? value.ToString() : Convert.ToInt32(value), v.Kind);
                }
            }
            catch (Exception e) { Log.Info($"privacy {v.Name}: {e.Message}"); }
        }

        // ------------------------------------------------------------------
        //  Кто пользовался камерой, микрофоном и местоположением
        // ------------------------------------------------------------------

        public const string Consent = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore";

        public static List<AccessUse> AccessLog(bool microphone = true)
        {
            var res = new List<AccessUse>();
            foreach (var (cap, kind) in new[] { ("webcam", "камера"), ("microphone", "микрофон"), ("location", "место") })
            {
                if (!microphone && cap == "microphone") continue;   // сторожу камеры микрофон не нужен — звонки и так видны в чёлке
                try
                {
                    using var k = Registry.CurrentUser.OpenSubKey(Consent + "\\" + cap);
                    if (k == null) continue;
                    void Add(RegistryKey app, string name)
                    {
                        long start = app.GetValue("LastUsedTimeStart") is long s ? s : 0;
                        long stop = app.GetValue("LastUsedTimeStop") is long e ? e : 0;
                        if (start <= 0) return;
                        res.Add(new AccessUse(name, kind, DateTime.FromFileTime(Math.Max(start, stop)), stop == 0 || stop < start));
                    }
                    foreach (var sub in k.GetSubKeyNames())
                    {
                        if (sub == "NonPackaged")
                        {
                            using var np = k.OpenSubKey(sub);
                            foreach (var exe in np?.GetSubKeyNames() ?? Array.Empty<string>())
                            {
                                using var a = np.OpenSubKey(exe);
                                if (a != null) Add(a, Path.GetFileNameWithoutExtension(exe.Replace('#', '\\')));
                            }
                        }
                        else
                        {
                            using var a = k.OpenSubKey(sub);
                            var name = sub.Split('_')[0];
                            name = name.Contains('.') ? name[(name.LastIndexOf('.') + 1)..] : name;
                            if (a != null) Add(a, name);
                        }
                    }
                }
                catch { }
            }
            return res.OrderByDescending(r => r.InUse).ThenByDescending(r => r.Last).ToList();
        }

        // ------------------------------------------------------------------
        //  Файл hosts — блокировка рекламных и следящих доменов
        // ------------------------------------------------------------------

        const string Begin = "# >>> N-Island: блокировка трекеров", End = "# <<< N-Island";

        public static string HostsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "etc", "hosts");

        /// <summary>Сторонние рекламные и следящие домены. Домены Microsoft сюда намеренно не входят —
        /// Защитник Windows считает это вмешательством, а телеметрию надёжнее выключать политиками.</summary>
        public static readonly string[] TrackerDomains =
        {
            "doubleclick.net", "ad.doubleclick.net", "stats.g.doubleclick.net", "googleads.g.doubleclick.net", "pagead2.googlesyndication.com",
            "googlesyndication.com", "adservice.google.com", "google-analytics.com", "www.google-analytics.com", "ssl.google-analytics.com",
            "app-measurement.com", "pixel.facebook.com", "an.facebook.com", "ads.yahoo.com", "analytics.yahoo.com", "ib.adnxs.com", "adnxs.com",
            "static.criteo.net", "cdn.taboola.com", "trc.taboola.com", "widgets.outbrain.com", "sb.scorecardresearch.com", "scorecardresearch.com",
            "pixel.quantserve.com", "static.hotjar.com", "script.hotjar.com", "mc.yandex.ru", "an.yandex.ru", "top-fwz1.mail.ru", "ads.tiktok.com",
            "analytics.tiktok.com", "ads.linkedin.com", "px.ads.linkedin.com", "bat.bing.com", "ads.twitter.com", "static.ads-twitter.com",
        };

        public static int HostsBlocked()
        {
            try
            {
                var lines = File.ReadAllLines(HostsPath);
                int b = Array.IndexOf(lines, Begin), e = Array.IndexOf(lines, End);
                return b >= 0 && e > b ? lines.Skip(b + 1).Take(e - b - 1).Count(l => l.StartsWith("0.0.0.0")) : 0;
            }
            catch { return 0; }
        }

        public static int HostsCustomLines()
        {
            try { return File.ReadAllLines(HostsPath).Count(l => l.Trim().Length > 0 && !l.TrimStart().StartsWith("#")) - HostsBlocked(); }
            catch { return 0; }
        }

        /// <summary>Добавить или убрать наш блок в hosts. Перед изменением — копия в папку backup.</summary>
        public static async Task<bool> HostsBlock(bool on)
        {
            string text;
            try { text = File.ReadAllText(HostsPath); } catch { return false; }
            var dir = Path.Combine(Store.Dir, "backup");
            Directory.CreateDirectory(dir);
            var copy = Path.Combine(dir, $"hosts-{DateTime.Now:yyyyMMdd-HHmmss}.bak");
            File.WriteAllText(copy, text);
            // убираем старый блок
            var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
            int b = lines.IndexOf(Begin), e = lines.IndexOf(End);
            if (b >= 0 && e > b) lines.RemoveRange(b, e - b + 1);
            while (lines.Count > 0 && lines[^1].Trim() == "") lines.RemoveAt(lines.Count - 1);
            if (on)
            {
                lines.Add("");
                lines.Add(Begin);
                lines.AddRange(TrackerDomains.Select(d => "0.0.0.0 " + d));
                lines.Add(End);
            }
            var tmp = Path.Combine(Store.Dir, "admin", "hosts.new");
            Directory.CreateDirectory(Path.GetDirectoryName(tmp));
            File.WriteAllText(tmp, string.Join("\r\n", lines) + "\r\n", new UTF8Encoding(false));
            int code = await Elevated.Script("hosts", new[] { $"copy /y \"{tmp}\" \"{HostsPath}\" >nul", "ipconfig /flushdns >nul" });
            return code >= 0 && HostsBlocked() == (on ? TrackerDomains.Length : 0);
        }

        public static string LastHostsBackup()
        {
            try { return new DirectoryInfo(Path.Combine(Store.Dir, "backup")).GetFiles("hosts-*.bak").OrderByDescending(f => f.Name).FirstOrDefault()?.FullName; }
            catch { return null; }
        }

        public static Task<int> RestoreHosts(string backup) =>
            Elevated.Script("hosts-restore", new[] { $"copy /y \"{backup}\" \"{HostsPath}\" >nul", "ipconfig /flushdns >nul" });
    }
}
