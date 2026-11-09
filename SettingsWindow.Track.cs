using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using NIsland.Core;
using NIsland.Pages;
using NIsland.Services;

namespace NIsland
{
    /// <summary>Центр управления: «Отслеживание и контроль» (2.4) — экранное время, лимиты, фокус, будильники, сторожа, отчёт в Telegram.</summary>
    public partial class SettingsWindow
    {
        UIElement TrackSec()
        {
            var page = new StackPanel();
            var status = Status();

            // сводка за сегодня (обновляется сама)
            var today = new StackPanel();
            void LoadToday()
            {
                var t = PcWatch.Snap(DateTime.Today);
                today.Children.Clear();
                var top = t.Apps.OrderByDescending(a => a.Value).Take(5).Select(a => (a.Key, PcWatch.Dur(a.Value)));
                today.Children.Add(Table(new[]
                {
                    ("За ПК сегодня", PcWatch.Dur(t.ActiveSec)),
                    ("Трафик", "↓ " + PcWatch.Bytes(t.Down) + " · ↑ " + PcWatch.Bytes(t.Up)),
                    ("Блокировок / сон", t.Events.Count(e => e[1] == "lock") + " / " + t.Events.Count(e => e[1] == "sleep")),
                    ("Уведомлений", t.Notifs.Values.Sum().ToString()),
                    ("Внешний IP", PcWatch.LastIp ?? "—"),
                }.Concat(top)));
            }
            LoadToday();
            today.Live(TimeSpan.FromSeconds(30), () => { LoadToday(); return System.Threading.Tasks.Task.CompletedTask; });
            page.Children.Add(Group("Сегодня", today,
                Item("Отслеживать", "Экранное время по программам, вход и блокировки, заряд, трафик, Wi-Fi, флешки, загрузки, уведомления. Всё хранится только на этом ПК (14 дней) — в Telegram попадает, только когда ты спросишь.", Safety.Safe,
                     Toggle(nameof(Settings.TrackOn))),
                Item("Отчёт за день", "Прислать в Telegram (бот N-Island) сводку за сегодня.", Safety.Safe,
                     Act("Прислать", () => System.Threading.Tasks.Task.FromResult(PcWatch.ToPhone != null && Integrations.All.OfType<TelegramHub>().FirstOrDefault()?.PushText(PcWatch.Report(DateTime.Today)) == true
                         ? I18n.T("Отправлено в Telegram") : I18n.T("Telegram-бот не подключён")), status))));
            page.Children.Add(status);

            page.Children.Add(Group("Контроль времени",
                Wide("Лимиты времени в программах", "Программа=минут в день через «;», например: chrome=120;steam=60. Когда время выйдет — всплывашка и сообщение в Telegram.",
                     Input(nameof(Settings.AppLimits), 360)),
                Item("Когда лимит исчерпан", null, Safety.Safe, Seg(nameof(Settings.LimitAction), ("warn", I18n.T("Предупредить")), ("minimize", I18n.T("Сворачивать окно"))), "minimize — окно программы сворачивается, пока она на переднем плане"),
                Wide("Фокус: что сворачивать", "Имена процессов через запятую. Во время фокус-сессии их окна сворачиваются, как только ты на них переключишься.",
                     Input(nameof(Settings.FocusBlockApps), 360)),
                Item("Фокус-сессия", PcWatch.FocusUntil is DateTime fu ? I18n.T("Идёт до") + " " + fu.ToString("HH:mm") : "25 минут без отвлечений", Safety.Safe,
                     UI.Row(UI.Pill("25 мин", () => PcWatch.StartFocus(25), true), UI.Pill("50 мин", () => PcWatch.StartFocus(50)).M(6, 0, 0, 0), UI.Pill("Стоп", () => PcWatch.StartFocus(0)).M(6, 0, 0, 0)))));

            page.Children.Add(Group("Будильники и отчёт",
                Wide("Будильники", "Время|подпись через «;», например: 07:30|Подъём;13:00|Обед. Звенят на ПК (кнопка «Отложить 10 мин» в чёлке) и приходят в Telegram.",
                     Input(nameof(Settings.Alarms), 360)),
                Item("Ежедневный отчёт в Telegram", "Время в формате ЧЧ:мм, пусто — не присылать.", Safety.Safe, Input(nameof(Settings.DailyReportTime), 90))));

            page.Children.Add(Group("Сторожа — сообщения в Telegram",
                Item("Процессор загружен", "Если нагрузка держится 5 минут подряд.", Safety.Safe,
                     Stepper(nameof(Settings.LoadAlertCpu), 0, 95, 5, v => v == 0 ? I18n.T("выкл") : "≥ " + v + "%")),
                Item("Память занята", "Если держится 5 минут подряд.", Safety.Safe,
                     Stepper(nameof(Settings.LoadAlertRam), 0, 95, 5, v => v == 0 ? I18n.T("выкл") : "≥ " + v + "%")),
                Item("Сменился внешний IP", "Проверка раз в 15 минут (api.ipify.org).", Safety.Safe, Toggle(nameof(Settings.IpAlert))),
                Item("Новый файл в «Загрузках»", "Имя файла; прислать сам файл — /downloads 1.", Safety.Safe, Toggle(nameof(Settings.DownloadsNotify))),
                Wide("Следить за программами", "Имена процессов через запятую: сообщу, когда запустятся и закроются.", Input(nameof(Settings.WatchProcs), 360))));

            page.Children.Add(Group("Обои по кругу",
                Wide("Папка с обоями", "Пусто — «Изображения\\Wallpapers», а если её нет — «Изображения».", Input(nameof(Settings.WallpaperFolder), 360)),
                Item("Сменить обои", PcWatch.WallFolder, Safety.Safe,
                     UI.Row(UI.Pill("◀", () => PcWatch.NextWallpaper(-1)), UI.Pill("Случайные", () => PcWatch.NextWallpaper(0), true).M(6, 0, 6, 0), UI.Pill("▶", () => PcWatch.NextWallpaper(1))))));

            page.Children.Add(Info("В Telegram: /screentime · /week · /limits · /focusmode · /alarm · /report · /widgets → «Отслеживание» и «Контроль»."));
            return page;
        }
    }
}
