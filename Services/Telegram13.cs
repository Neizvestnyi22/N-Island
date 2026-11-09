using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NIsland.Core;

namespace NIsland.Services
{
    // =====================================================================
    //  Команды 2.4: отслеживание и контроль (PcWatch) и виджеты для того, что уже было.
    //  Каждая команда отвечает текстом с кнопкой «🧩 Виджет»; при «Отвечать виджетами» — сразу виджетом.
    // =====================================================================
    public partial class TelegramHub
    {
        static readonly (string Cmd, string Desc)[] Commands5 =
        {
            ("screentime", "Экранное время и программы"), ("week", "Неделя за ПК"), ("sessions", "Вход, блокировки, сон"), ("batlog", "История заряда"),
            ("traffic", "Трафик за день и неделю"), ("wifilog", "История сигнала Wi-Fi"), ("usblog", "Журнал флешек"), ("iplog", "Внешний IP: /iplog on · off"),
            ("downloads", "Новые загрузки: /downloads 1 — прислать"), ("notifstats", "Кто шлёт уведомления"), ("speedlog", "История скорости интернета"),
            ("temps", "Температура"), ("ramtop", "Кто занял память"), ("timeline", "События дня"), ("report", "Отчёт за день: /report 21:00 · off"),
            ("miclevel", "Громкость микрофона: /miclevel 70"), ("wallnext", "Следующие обои: /wallnext · prev · random"), ("limits", "Лимиты времени"),
            ("limit", "Лимит: /limit chrome 120 · /limit chrome 0"), ("focusmode", "Фокус: /focusmode 25 · stop"), ("watchproc", "Следить: /watchproc steam · -steam"),
            ("alarms", "Будильники"), ("alarm", "Будильник: /alarm 07:30 Подъём · /alarm del 1"), ("fixes", "Починки: /fixes dns · wifi · ram · explorer"),
            ("awakefor", "Не засыпать: /awakefor 2 (часа) · off"), ("loadalert", "Сторож: /loadalert cpu 90 · ram 85 · off"), ("prayer", "Время молитв"),
            ("agenda", "Ближайшие события"), ("shelf", "Полка: /shelf 1 — прислать"), ("news", "Новости"), ("privacy", "Кто слушает и смотрит"),
            ("cleanscan", "Найти мусор"),
        };

        internal static IEnumerable<string> TestCommands5 => Commands5.Select(c => c.Cmd);

        /// <summary>Сообщение на телефон от сторожей ПК (лимиты, IP, загрузки, процессы, нагрузка, будильник, отчёт).</summary>
        public bool PushText(string html)
        {
            if (Remote != null || !Enabled || !Ready || string.IsNullOrEmpty(V("chat")) || string.IsNullOrEmpty(html)) return false;
            _ = Task.Run(async () =>
            {
                try { if (!await PushPcText(html)) await Message(html); }
                catch (Exception e) { Log.Info("telegram push: " + e.Message); }
            });
            return true;
        }

        static object WBtn(string kind) => K(Row(B("🧩 Виджет", "wg:" + kind)));

        static string Bar(int pct, int width = 10)
        {
            int n = Math.Clamp((int)Math.Round(pct / 100.0 * width), 0, width);
            return new string('▰', n) + new string('▱', width - n);
        }

        async Task<(string Html, object Keys)?> Command5(string cmd, string arg)
        {
            string no;
            var now = DateTime.Now;
            arg = arg.Trim();
            switch (cmd)
            {
                case "screentime":
                {
                    bool y = arg is "вчера" or "yesterday" or "1";
                    var t = PcWatch.Snap(DateTime.Today.AddDays(y ? -1 : 0));
                    var sb = new StringBuilder($"⏱ <b>{(y ? "Вчера" : "Сегодня")} за ПК: {Du(t.ActiveSec)}</b>\n");
                    var top = t.Apps.OrderByDescending(a => a.Value).Take(10).ToList();
                    int mx = top.Count > 0 ? top[0].Value : 1;
                    foreach (var a in top) sb.Append($"\n<code>{Bar(a.Value * 100 / mx, 8)}</code> {H(a.Key)} — {Du(a.Value)}");
                    if (top.Count == 0) sb.Append("\nПока пусто — время считается, пока ты за ПК");
                    if (!Store.Settings.TrackOn) sb.Append("\n\n⚠️ Отслеживание выключено: N-Island → Центр управления → Отслеживание");
                    return (sb + "\n\nВчера: /screentime вчера · неделя: /week · лимиты: /limits", WBtn("screentime"));
                }
                case "week":
                {
                    var days = PcWatch.Days(7);
                    var sb = new StringBuilder($"📅 <b>Неделя за ПК</b> · в среднем {Du((int)days.Average(x => x.ActiveSec))} в день\n");
                    int mx = Math.Max(1, days.Max(x => x.ActiveSec));
                    foreach (var x in days)
                    {
                        var top = x.Apps.OrderByDescending(a => a.Value).FirstOrDefault();
                        sb.Append($"\n<code>{DateTime.Parse(x.Date):dd.MM} {Bar(x.ActiveSec * 100 / mx, 8)}</code> {Du(x.ActiveSec)}" + (top.Key != null ? $" · {H(top.Key)}" : ""));
                    }
                    return (sb.ToString(), WBtn("week"));
                }
                case "sessions":
                {
                    var t = PcWatch.Snap(DateTime.Today);
                    var sb = new StringBuilder($"🔐 <b>Сессии</b>\nВключён: {PcWatch.BootTime:dd.MM HH:mm} · работает {Du((int)(now - PcWatch.BootTime).TotalSeconds)}\nБез дела: {Du((int)PcWatch.IdleSeconds())}\n");
                    foreach (var e in t.Events.Where(e => e[1] is "boot" or "lock" or "unlock" or "sleep" or "wake").TakeLast(15)) sb.Append($"\n{e[0]} {H(Ev(e))}");
                    return (sb.ToString(), WBtn("sessions"));
                }
                case "batlog":
                {
                    var sys = SystemService.I;
                    if (!sys.HasBattery) return ("🔋 Батареи нет — ПК работает от сети", null);
                    var t = PcWatch.Snap(DateTime.Today);
                    var sb = new StringBuilder($"🔋 <b>{sys.BatteryPercent}%</b>" + (sys.Charging ? " ⚡ заряжается" : ""));
                    if (PcWatch.ChargeLeft() is TimeSpan cl) sb.Append($" · до полной {Du((int)cl.TotalSeconds)}");
                    if (PcWatch.DischargeLeft() is TimeSpan dl) sb.Append($" · осталось {Du((int)dl.TotalSeconds)}");
                    sb.Append('\n');
                    foreach (var b in t.Bat.Where((_, i) => i % 3 == 0 || i == t.Bat.Count - 1).TakeLast(16))
                        sb.Append($"\n<code>{b[0] / 60:00}:{b[0] % 60:00} {Bar(b[1], 10)}</code> {b[1]}%{(b[2] == 1 ? " ⚡" : "")}");
                    if (t.Bat.Count == 0) sb.Append("\nЗаряд записывается раз в 10 минут — история появится позже");
                    return (sb.ToString(), WBtn("batlog"));
                }
                case "traffic":
                {
                    var days = PcWatch.Days(7);
                    var t = days[0];
                    var sb = new StringBuilder($"🌐 <b>Трафик сегодня: {PcWatch.Bytes(t.Down + t.Up)}</b>\n↓ {PcWatch.Bytes(t.Down)} · ↑ {PcWatch.Bytes(t.Up)}\n");
                    long mx = Math.Max(1, days.Max(x => x.Down + x.Up));
                    foreach (var x in days) sb.Append($"\n<code>{DateTime.Parse(x.Date):dd.MM} {Bar((int)((x.Down + x.Up) * 100 / mx), 8)}</code> {PcWatch.Bytes(x.Down + x.Up)}");
                    sb.Append($"\n\nЗа неделю: {PcWatch.Bytes(days.Sum(x => x.Down + x.Up))}");
                    return (sb.ToString(), WBtn("traffic"));
                }
                case "wifilog":
                {
                    var w = PcWatch.Snap(DateTime.Today).Wifi;
                    if (w.Count == 0) return ("📶 Истории сигнала пока нет: он записывается раз в 5 минут, пока ПК подключён к Wi-Fi", WBtn("wifilog"));
                    var sb = new StringBuilder($"📶 <b>Сигнал Wi-Fi сегодня</b> · сейчас {w[^1][1]}% · среднее {w.Average(x => x[1]):0}%\n");
                    foreach (var x in w.Where((_, i) => i % 3 == 0 || i == w.Count - 1).TakeLast(16)) sb.Append($"\n<code>{x[0] / 60:00}:{x[0] % 60:00} {Bar(x[1], 10)}</code> {x[1]}%");
                    return (sb.ToString(), WBtn("wifilog"));
                }
                case "usblog":
                {
                    var sb = new StringBuilder("🔌 <b>Флешки и внешние диски</b>\n");
                    try { foreach (var dr in DriveInfo.GetDrives().Where(x => x.DriveType == DriveType.Removable && x.IsReady)) sb.Append($"\n💾 {H(dr.Name)} {H(dr.VolumeLabel)} — свободно {Maint.Size(dr.AvailableFreeSpace)} из {Maint.Size(dr.TotalSize)}"); } catch { }
                    var evs = PcWatch.Days(7).SelectMany(x => x.Events.Where(e => e[1] == "usb").Select(e => (x.Date, e))).Take(15).ToList();
                    sb.Append(evs.Count == 0 ? "\n\nЗа неделю ничего не подключали" : "\n\n<b>Журнал</b> (+ подключён, − отключён)");
                    foreach (var (date, e) in evs) sb.Append($"\n{DateTime.Parse(date):dd.MM} {e[0]} {H(e[2])}");
                    return (sb.ToString(), WBtn("usblog"));
                }
                case "iplog":
                {
                    if (arg is "on" or "off" or "вкл" or "выкл")
                    {
                        if ((no = Need("control")) != null) return (no, null);
                        bool on = arg is "on" or "вкл";
                        await Ui(() => Store.Settings.IpAlert = on);
                        return (on ? "🔔 Напишу, когда внешний IP сменится" : "🔕 Не сообщаю о смене IP", null);
                    }
                    if (PcWatch.LastIp == null) await PcWatch.CheckIpNow();
                    var sb = new StringBuilder($"🌍 <b>Внешний IP</b>: <code>{H(PcWatch.LastIp ?? "не удалось узнать")}</code>\nVPN: {(Vpn.Active ? H(Vpn.ActiveName) : "выкл")} · сообщать о смене: {(Store.Settings.IpAlert ? "да" : "нет")} (/iplog on · off)\n");
                    foreach (var x in PcWatch.Days(14)) foreach (var e in x.Events.Where(e => e[1] == "ip")) sb.Append($"\n{DateTime.Parse(x.Date):dd.MM} {e[0]} {H(e[2])}");
                    return (sb.ToString(), WBtn("iplog"));
                }
                case "downloads":
                {
                    var list = PcWatch.RecentDownloads(10);
                    if (int.TryParse(arg, out var n))
                    {
                        if ((no = Need("files")) != null) return (no, null);
                        if (n < 1 || n > list.Count) return ("Нет такого номера. Список: /downloads", null);
                        var r = await SendPath(list[n - 1].Path);
                        if (r != null) return (r, null);
                        return null;
                    }
                    if (arg is "on" or "off")
                    {
                        if ((no = Need("control")) != null) return (no, null);
                        await Ui(() => Store.Settings.DownloadsNotify = arg == "on");
                        return (arg == "on" ? "🔔 Сообщу о каждом новом файле в «Загрузках»" : "🔕 О новых загрузках не сообщаю", null);
                    }
                    var sb = new StringBuilder("📥 <b>Последние загрузки</b>\n");
                    for (int i = 0; i < list.Count; i++)
                    {
                        long size = 0; try { size = new FileInfo(list[i].Path).Length; } catch { }
                        sb.Append($"\n{i + 1}. {H(Cut(Path.GetFileName(list[i].Path), 50))} · {Maint.Size(size)} · {list[i].At:dd.MM HH:mm}");
                    }
                    if (list.Count == 0) sb.Append("\nПапка пуста");
                    return (sb + $"\n\nПрислать: /downloads 1 · сообщать о новых: /downloads {(Store.Settings.DownloadsNotify ? "off" : "on")}", WBtn("downloads"));
                }
                case "notifstats":
                {
                    var t = PcWatch.Snap(DateTime.Today);
                    var top = t.Notifs.OrderByDescending(x => x.Value).Take(10).ToList();
                    if (top.Count == 0) return ("🔔 Сегодня уведомлений не было", WBtn("notifstats"));
                    int mx = top[0].Value;
                    return ($"🔔 <b>Уведомлений сегодня: {t.Notifs.Values.Sum()}</b>\n" + string.Join("", top.Select(a => $"\n<code>{Bar(a.Value * 100 / mx, 8)}</code> {H(a.Key)} — {a.Value}")), WBtn("notifstats"));
                }
                case "speedlog":
                {
                    var all = PcWatch.Days(14).SelectMany(x => Enumerable.Reverse(x.Speed).Select(s => (x.Date, s))).Take(15).ToList();
                    if (all.Count == 0) return ("🚀 Замеров ещё не было. Замерить: /speed", WBtn("speedlog"));
                    return ("🚀 <b>История скорости</b>\n" + string.Join("", all.Select(x => $"\n{DateTime.Parse(x.Date):dd.MM} {x.s[0]} — {x.s[1]} Мбит/с" + (x.s[2] != "" ? $" · {x.s[2]} мс" : ""))) + "\n\nНовый замер: /speed", WBtn("speedlog"));
                }
                case "temps":
                {
                    var z = await Task.Run(PcWatch.Temperatures);
                    var sb = new StringBuilder($"🌡 <b>Температура</b> · нагрузка {SystemService.I.CpuPercent()}%\n");
                    foreach (var (zone, c) in z) sb.Append($"\n{(c > 85 ? "🔴" : c > 70 ? "🟡" : "🟢")} {H(zone)} — {c:0} °C");
                    if (z.Count == 0) sb.Append("\nДатчики не отдают температуру Windows — многие ноутбуки показывают её только в своих программах");
                    return (sb.ToString(), WBtn("temps"));
                }
                case "ramtop":
                {
                    var top = await Task.Run(() => PcWatch.TopRam(12));
                    long mx = top.Count > 0 ? Math.Max(1, top[0].Bytes) : 1;
                    return ($"🧠 <b>Память занята на {SystemService.I.RamPercent()}%</b>\n" + string.Join("", top.Select(x => $"\n<code>{Bar((int)(x.Bytes * 100 / mx), 8)}</code> {H(x.Name)} — {Maint.Size(x.Bytes)}")) + "\n\nОсвободить: /ram", WBtn("ramtop"));
                }
                case "timeline":
                {
                    var t = PcWatch.Snap(DateTime.Today);
                    if (t.Events.Count == 0) return ("🗓 Сегодня ещё ничего не случилось", WBtn("timeline"));
                    return ($"🗓 <b>События дня</b> · {t.Events.Count}\n" + string.Join("", t.Events.TakeLast(30).Select(e => $"\n{e[0]} {H(Ev(e))}")), WBtn("timeline"));
                }
                case "report":
                {
                    if (arg is "off" or "выкл")
                    {
                        if ((no = Need("control")) != null) return (no, null);
                        await Ui(() => Store.Settings.DailyReportTime = "");
                        return ("📊 Автоотчёт выключен", null);
                    }
                    var m = System.Text.RegularExpressions.Regex.Match(arg, @"^(\d{1,2})[:.](\d{2})$");
                    if (m.Success && int.Parse(m.Groups[1].Value) < 24 && int.Parse(m.Groups[2].Value) < 60)
                    {
                        if ((no = Need("control")) != null) return (no, null);
                        var tm = $"{int.Parse(m.Groups[1].Value):00}:{m.Groups[2].Value}";
                        await Ui(() => Store.Settings.DailyReportTime = tm);
                        return ($"📊 Буду присылать отчёт каждый день в {tm}", null);
                    }
                    bool y = arg is "вчера" or "yesterday";
                    return (PcWatch.Report(DateTime.Today.AddDays(y ? -1 : 0)) + "\n\n" +
                            (string.IsNullOrEmpty(Store.Settings.DailyReportTime) ? "Каждый день сам: /report 21:00" : $"Автоотчёт в {Store.Settings.DailyReportTime} · выключить: /report off"), WBtn("report"));
                }
                case "miclevel":
                {
                    var a = AudioService.I;
                    if (int.TryParse(arg.TrimEnd('%'), out var v))
                    {
                        if ((no = Need("control")) != null) return (no, null);
                        v = Math.Clamp(v, 0, 100);
                        await Ui(() => { a.MicLevel = v / 100.0; if (v > 0) a.SetMicMute(false); });
                    }
                    double lv = a.MicLevel;
                    if (lv < 0) return ("🎙 Микрофон не найден", null);
                    return ($"🎙 Микрофон: {(a.MicMuted ? "выключен" : "включён")} · громкость {Math.Round(lv * 100)}%\n<code>{Bar((int)(lv * 100))}</code>\n\nИзменить: /miclevel 70 · вкл/выкл: /mic", WBtn("mic"));
                }
                case "wallnext":
                {
                    if ((no = Need("control")) != null) return (no, null);
                    int dir = arg is "prev" or "назад" ? -1 : arg is "random" or "rnd" or "случайные" ? 0 : 1;
                    var n = await Ui(() => PcWatch.NextWallpaper(dir));
                    return (n == null ? $"🖼 В папке «{H(PcWatch.WallFolder)}» нет картинок. Положи их в «Изображения\\Wallpapers» или выбери папку в настройках N-Island" : $"🖼 Обои: {H(n)}", WBtn("wall"));
                }
                case "limits":
                {
                    var t = PcWatch.Snap(DateTime.Today);
                    var lim = PcWatch.Limits();
                    var sb = new StringBuilder($"⏳ <b>Лимиты времени</b> · {(Store.Settings.LimitAction == "minimize" ? "по лимиту окно сворачивается" : "по лимиту — предупреждение")}\n");
                    foreach (var (app, min) in lim)
                    {
                        int used = AppSec(t, app);
                        sb.Append($"\n{(used >= min * 60 ? "⛔" : "🟢")} {H(app)} — {used / 60} из {min} мин");
                    }
                    if (lim.Count == 0) sb.Append("\nЛимитов нет");
                    return (sb + "\n\nДобавить: /limit chrome 120 · снять: /limit chrome 0", WBtn("limits"));
                }
                case "limit":
                {
                    var m = System.Text.RegularExpressions.Regex.Match(arg, @"^(.+?)\s+(\d{1,4})$");
                    if (!m.Success) return ("Напиши так: /limit chrome 120 (минут в день, 0 — снять)", null);
                    if ((no = Need("control")) != null) return (no, null);
                    var app = m.Groups[1].Value.Trim().Replace(".exe", "", StringComparison.OrdinalIgnoreCase);
                    int min = Math.Min(1440, int.Parse(m.Groups[2].Value));
                    await Ui(() => PcWatch.SetLimit(app, min));
                    return (min > 0 ? $"⏳ {H(app)}: не больше {min} мин в день" : $"⏳ Лимит для {H(app)} снят", WBtn("limits"));
                }
                case "focusmode":
                {
                    if (arg is "stop" or "стоп" or "off")
                    {
                        if ((no = Need("control")) != null) return (no, null);
                        await Ui(() => PcWatch.StartFocus(0));
                        return ("⏹ Фокус-сессия остановлена", null);
                    }
                    if (int.TryParse(arg, out var fm))
                    {
                        if ((no = Need("control")) != null) return (no, null);
                        fm = Math.Clamp(fm, 1, 240);
                        await Ui(() => PcWatch.StartFocus(fm));
                        return ($"🎯 Фокус на {fm} мин: {H(string.Join(", ", PcWatch.FocusApps()))} будут сворачиваться", WBtn("focus"));
                    }
                    var fu = PcWatch.FocusUntil;
                    return ((fu is DateTime u ? $"🎯 Идёт фокус-сессия, осталось {Du((int)(u - now).TotalSeconds)}" : "🎯 Фокус выключен") +
                            $"\nСворачиваю: {H(string.Join(", ", PcWatch.FocusApps()))}\n\nНачать: /focusmode 25 · остановить: /focusmode stop", WBtn("focus"));
                }
                case "watchproc":
                {
                    var names = (Store.Settings.WatchProcs ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
                    if (arg != "")
                    {
                        if ((no = Need("control")) != null) return (no, null);
                        bool del = arg.StartsWith("-");
                        var n = arg.TrimStart('-').Trim().Replace(".exe", "", StringComparison.OrdinalIgnoreCase);
                        if (n.Length < 2 || n.Contains(',')) return ("Напиши так: /watchproc steam · убрать: /watchproc -steam", null);
                        if (del) names.RemoveAll(x => x.Equals(n, StringComparison.OrdinalIgnoreCase));
                        else if (!names.Contains(n, StringComparer.OrdinalIgnoreCase)) names.Add(n);
                        await Ui(() => Store.Settings.WatchProcs = string.Join(", ", names));
                        return (del ? $"👁 Больше не слежу за {H(n)}" : $"👁 Слежу за {H(n)}: напишу, когда запустится и закроется", WBtn("procwatch"));
                    }
                    return ("👁 <b>Слежу за программами</b>\n" + (names.Count == 0 ? "Пока ни за кем" : string.Join("\n", names.Select(x => "• " + H(x)))) +
                            "\n\nДобавить: /watchproc steam · убрать: /watchproc -steam", WBtn("procwatch"));
                }
                case "alarms":
                case "alarm":
                {
                    var list = PcWatch.AlarmList();
                    if (arg.StartsWith("del", StringComparison.OrdinalIgnoreCase) || arg.StartsWith("удал", StringComparison.OrdinalIgnoreCase))
                    {
                        if ((no = Need("control")) != null) return (no, null);
                        if (!int.TryParse(arg.Split(' ', 2).ElementAtOrDefault(1), out var di) || di < 1 || di > list.Count) return ("Напиши так: /alarm del 1", null);
                        list.RemoveAt(di - 1);
                        await Ui(() => PcWatch.SetAlarms(list));
                        return ("⏰ Будильник удалён", WBtn("alarms"));
                    }
                    if (arg != "")
                    {
                        if ((no = Need("control")) != null) return (no, null);
                        return await WidgetInput4("alarms", arg);
                    }
                    return ("⏰ <b>Будильники</b>\n" + (list.Count == 0 ? "Нет" : string.Join("\n", list.Select((a, i) => $"{i + 1}. {a.Time} — {H(a.Label)}"))) +
                            "\n\nДобавить: /alarm 07:30 Подъём · удалить: /alarm del 1\nНа ПК звенит и показывается в чёлке («Отложить 10 мин»), сюда приходит сообщение", WBtn("alarms"));
                }
                case "fixes":
                {
                    if (arg == "") return ("🛠 <b>Быстрые починки</b>\n/fixes dns — сбросить кэш DNS (сайты не открываются)\n/fixes wifi — переподключить Wi-Fi\n/fixes ram — освободить память\n/fixes explorer — перезапустить Проводник (пропала панель задач, всё подвисло)", WBtn("fixes"));
                    if ((no = Need("control")) != null) return (no, null);
                    switch (arg)
                    {
                        case "dns": return (Maint.FlushDns() ? "✓ Кэш DNS сброшен" : "Не удалось сбросить кэш DNS", null);
                        case "wifi": return (await WifiTools.Reconnect() ?? "📶 Переподключено", null);
                        case "ram": return ($"🧠 Освобождено {Maint.Size(await Maint.OptimizeRam())}", null);
                        case "explorer": { var err = await Task.Run(PcWatch.RestartExplorer); return (err == null ? "🔄 Проводник перезапущен" : "Не получилось: " + H(err), null); }
                        default: return ("Есть: dns · wifi · ram · explorer", null);
                    }
                }
                case "awakefor":
                {
                    if (arg is "off" or "выкл")
                    {
                        if ((no = Need("control")) != null) return (no, null);
                        await Ui(() => PcWatch.SetAwake(false, 0));
                        return ("💤 ПК снова засыпает как обычно", null);
                    }
                    if (int.TryParse(arg, out var hrs))
                    {
                        if ((no = Need("control")) != null) return (no, null);
                        hrs = Math.Clamp(hrs, 1, 12);
                        await Ui(() => PcWatch.SetAwake(true, hrs));
                        return ($"☕ ПК не заснёт {hrs} ч — до {now.AddHours(hrs):HH:mm}", WBtn("awake"));
                    }
                    var on = SystemService.I.KeepAwake;
                    return ((on ? "☕ ПК не засыпает" + (PcWatch.AwakeUntil is DateTime au ? $" до {au:HH:mm}" : "") : "💤 ПК засыпает как обычно") + "\n\nНа время: /awakefor 2 · выключить: /awakefor off", WBtn("awake"));
                }
                case "loadalert":
                {
                    var s = Store.Settings;
                    var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 0)
                    {
                        if ((no = Need("control")) != null) return (no, null);
                        if (parts[0] is "off" or "выкл") await Ui(() => { s.LoadAlertCpu = 0; s.LoadAlertRam = 0; });
                        else if (parts.Length == 2 && parts[0] is "cpu" or "ram" && int.TryParse(parts[1].TrimEnd('%'), out var lv) && lv is 0 or (>= 50 and <= 99))
                            await Ui(() => { if (parts[0] == "cpu") s.LoadAlertCpu = lv; else s.LoadAlertRam = lv; });
                        else return ("Напиши так: /loadalert cpu 90 · /loadalert ram 85 · /loadalert off", null);
                    }
                    return ($"🚨 <b>Сторож нагрузки</b>\nПроцессор: {(s.LoadAlertCpu == 0 ? "не слежу" : "≥ " + s.LoadAlertCpu + "% дольше 5 минут")}\nПамять: {(s.LoadAlertRam == 0 ? "не слежу" : "≥ " + s.LoadAlertRam + "% дольше 5 минут")}\n\nСейчас: процессор {SystemService.I.CpuPercent()}% · память {SystemService.I.RamPercent()}%", WBtn("guard"));
                }
                case "prayer":
                {
                    var p = await PrayerService.Get();
                    if (p == null || p.Count == 0) return ("🕌 Не удалось загрузить время молитв — проверь город в настройках N-Island", null);
                    var nowT = now.TimeOfDay;
                    var next = p.FirstOrDefault(x => x.At > nowT);
                    return ("🕌 <b>Время молитв</b> · " + now.ToString("dd.MM") + "\n" + string.Join("\n", p.Select(x => (x.Name == next.Name ? "▶ <b>" : "   ") + H(x.Name) + " — " + x.At.ToString(@"hh\:mm") + (x.Name == next.Name ? "</b>" : ""))), WBtn("prayer"));
                }
                case "agenda":
                {
                    List<CalEvent> evs;
                    try { evs = await Ui(() => Store.Data.Events.ToList()); } catch { evs = new(); }
                    var up = evs.Where(e => e.Date.Date >= DateTime.Today && e.Date.Date <= DateTime.Today.AddDays(30)).OrderBy(e => e.At ?? e.Date).Take(20).ToList();
                    if (up.Count == 0) return ("📅 Событий на месяц вперёд нет — они добавляются в календаре острова", WBtn("agenda"));
                    return ("📅 <b>Ближайшие события</b>\n" + string.Join("\n", up.Select(e => $"{e.Date:dd.MM} {(string.IsNullOrEmpty(e.Time) ? "весь день" : e.Time)} — {H(e.Title)}")), WBtn("agenda"));
                }
                case "shelf":
                {
                    List<string> files;
                    try { files = await Ui(() => Store.Data.Shelf.ToList()); } catch { files = new(); }
                    if (int.TryParse(arg, out var n))
                    {
                        if ((no = Need("files")) != null) return (no, null);
                        if (n < 1 || n > files.Count) return ("Нет такого номера. Список: /shelf", null);
                        var r = await SendPath(files[n - 1]);
                        if (r != null) return (r, null);
                        return null;
                    }
                    if (files.Count == 0) return ("📦 Полка пуста — перетащи файлы на остров", WBtn("shelf"));
                    return ("📦 <b>Полка</b>\n" + string.Join("\n", files.Select((f, i) => $"{i + 1}. {H(Path.GetFileName(f.TrimEnd('\\')))}")) + "\n\nПрислать: /shelf 1", WBtn("shelf"));
                }
                case "news":
                {
                    try { var n = await ApiTools2.News(); WCSet("news", n); return ("📰 <b>Новости</b>\n" + H(n), WBtn("news")); }
                    catch (Exception e) { return ("📰 Не удалось загрузить новости: " + H(Integrations.Reason(e)), null); }
                }
                case "privacy":
                {
                    var l = await Task.Run(() => Privacy.AccessLog(true));
                    var sb = new StringBuilder("🕵 <b>Кто пользовался камерой, микрофоном и местоположением</b>\n");
                    foreach (var x in l.OrderByDescending(x => x.InUse).ThenByDescending(x => x.Last).Take(15))
                        sb.Append($"\n{(x.InUse ? "🔴 сейчас" : x.Last.ToString("dd.MM HH:mm"))} · {H(x.Kind)} · {H(x.App)}");
                    if (l.Count == 0) sb.Append("\nНикто");
                    return (sb.ToString(), WBtn("privacy"));
                }
                case "cleanscan":
                {
                    await CleanScan();
                    var res = WC<List<string[]>>("clean") ?? new();
                    long total = res.Sum(x => long.Parse(x[1]));
                    return ($"🧹 <b>Можно освободить {Maint.Size(total)}</b>\n" + string.Join("\n", res.Take(12).Select(x => $"• {H(x[0])} — {Maint.Size(long.Parse(x[1]))}")) +
                            "\n\nОчистить — кнопкой в виджете (только кэш и временные файлы) или на ПК: Центр управления → Глубокая очистка", WBtn("cleanup"));
                }
            }
            return null;
        }
    }
}
