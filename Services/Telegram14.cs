using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NIsland.Core;

namespace NIsland.Services
{
    // =====================================================================
    //  Виджеты 2.4: отслеживание (экранное время, неделя, сессии, заряд, трафик, Wi-Fi, USB, внешний IP, загрузки,
    //  уведомления, скорость, температура, память, журнал дня, отчёт) и контроль (микрофон, обои, лимиты, фокус,
    //  слежка за программами, будильники, починки, «не засыпать», сторож нагрузки), плюс виджеты для того, что
    //  уже было без них: Wi-Fi и Bluetooth, кто слушает и смотрит, молитвы, события, полка, переводчик, очистка, новости.
    // =====================================================================
    public partial class TelegramHub
    {
        static Settings Ws => Store.Settings;
        static int _stDay;
        static string _trTo = "en";
        static readonly string[] TrLangs = { "en", "ru", "uk", "de", "pl", "es", "fr" };
        string _fixConfirm, _cleanConfirm;
        DateTime _fixConfirmAt, _cleanConfirmAt;

        /// <summary>Длительность с переводом единиц: «1 ч 5 мин».</summary>
        static string Du(int sec) => sec >= 3600 ? $"{sec / 3600} {T("ч")} {sec % 3600 / 60} {T("мин")}" : $"{Math.Max(0, sec) / 60} {T("мин")}";

        static int AppSec(TrackDay t, string app) => t.Apps.Where(a => a.Key.Equals(app, StringComparison.OrdinalIgnoreCase)).Sum(a => a.Value);

        static readonly Dictionary<string, string> EvIcons = new()
        {
            ["boot"] = "⚡", ["lock"] = "🔒", ["unlock"] = "🔓", ["sleep"] = "😴", ["wake"] = "☀️", ["usb"] = "🔌", ["ip"] = "🌍", ["proc"] = "▶",
            ["limit"] = "⏳", ["download"] = "📥", ["alarm"] = "⏰", ["load"] = "🔥", ["focus"] = "🎯",
        };

        static string Ev(string[] e) => (EvIcons.TryGetValue(e[1], out var i) ? i + " " : "") + T(e[2]);

        Dictionary<string, object> CardState4(string kind)
        {
            Dictionary<string, object> d;
            var now = DateTime.Now;
            switch (kind)
            {
                // ---------------- Отслеживание ----------------
                case "screentime":
                {
                    var t = PcWatch.Snap(DateTime.Today.AddDays(-_stDay));
                    d = Card(kind, "⏱ Экранное время", T(_stDay == 0 ? "сегодня" : "вчера") + " · " + T("за ПК") + " " + Du(t.ActiveSec));
                    var top = t.Apps.OrderByDescending(x => x.Value).Take(7).ToList();
                    int mx = top.Count > 0 ? top[0].Value : 1;
                    var lim = PcWatch.Limits();
                    foreach (var a in top)
                    {
                        bool over = lim.TryGetValue(a.Key, out var lm) && a.Value >= lm * 60;
                        CRows(d).Add(new object[] { "b", Cut(a.Key, 16), a.Value * 100 / Math.Max(1, mx), over ? "#ff453a" : "#64d2ff", Du(a.Value) + (over ? " ⛔" : "") });
                    }
                    if (top.Count == 0) CRows(d).Add(new object[] { "t", T("Пока пусто — время считается, пока ты за ПК") });
                    if (!Ws.TrackOn) CRows(d).Add(new object[] { "t", T("⚠️ Отслеживание выключено в настройках") });
                    Btns(d, Bt(_stDay == 0 ? "◀ Вчера" : "Сегодня ▶", "day"), Bt("📅 Неделя", "open|week"), Bt("⏳ Лимиты", "open|limits"));
                    return d;
                }
                case "week":
                {
                    var days = Enumerable.Range(0, 7).Select(i => PcWatch.Snap(DateTime.Today.AddDays(i - 6))).ToList();
                    int avg = (int)days.Average(x => x.ActiveSec);
                    d = Card(kind, "📅 Неделя за ПК", T("в среднем") + " " + Du(avg) + " " + T("в день"));
                    CRows(d).Add(new object[] { "g", T("Часы за ПК"), days.Select(x => Math.Round(x.ActiveSec / 3600.0, 1)).ToArray(), "#64d2ff", Du(days[^1].ActiveSec), 1 });
                    foreach (var x in Enumerable.Reverse(days))
                    {
                        var top = x.Apps.OrderByDescending(a => a.Value).FirstOrDefault();
                        CRows(d).Add(new object[] { "k", DateTime.Parse(x.Date).ToString("dd.MM"), Du(x.ActiveSec) + (top.Key != null ? " · " + Cut(top.Key, 14) : "") });
                    }
                    Btns(d, Bt("⏱ Сегодня", "open|screentime"), Bt("📊 Отчёт", "open|report"));
                    return d;
                }
                case "sessions":
                {
                    d = Card(kind, "🔐 Сессии", T("включён") + " " + PcWatch.BootTime.ToString("dd.MM HH:mm"));
                    CRows(d).Add(new object[] { "k", T("Работает"), Du((int)(now - PcWatch.BootTime).TotalSeconds) });
                    CRows(d).Add(new object[] { "k", T("Без дела"), Du((int)PcWatch.IdleSeconds()) });
                    var evs = PcWatch.Snap(DateTime.Today).Events.Where(e => e[1] is "boot" or "lock" or "unlock" or "sleep" or "wake").TakeLast(8).Reverse();
                    foreach (var e in evs) CRows(d).Add(new object[] { "k", e[0], Ev(e) });
                    Btns(d, Bt("🔒 Заблокировать", "lock", "d"), Bt("🗓 Журнал дня", "open|timeline"));
                    return d;
                }
                case "batlog":
                {
                    var sys = SystemService.I;
                    var t = PcWatch.Snap(DateTime.Today);
                    d = Card(kind, "🔋 История заряда", sys.HasBattery ? sys.BatteryPercent + "%" + (sys.Charging ? " ⚡" : "") : T("батареи нет"));
                    if (t.Bat.Count > 1)
                    {
                        CRows(d).Add(new object[] { "g", T("Сегодня"), t.Bat.Select(b => b[1]).ToArray(), "#30d158", $"{t.Bat.Min(b => b[1])}–{t.Bat.Max(b => b[1])}%", 100 });
                        // расход: последний отрезок без зарядки
                        var seg = new List<int[]>();
                        for (int i = t.Bat.Count - 1; i >= 0 && t.Bat[i][2] == 0; i--) seg.Insert(0, t.Bat[i]);
                        if (seg.Count >= 2 && seg[^1][0] > seg[0][0] && seg[0][1] > seg[^1][1])
                        {
                            double rate = (seg[0][1] - seg[^1][1]) / ((seg[^1][0] - seg[0][0]) / 60.0);
                            CRows(d).Add(new object[] { "k", T("Расход"), $"{rate:0.#} %/{T("ч")}" + (rate > 0.5 ? $" · ~{Du((int)(sys.BatteryPercent / rate * 3600))}" : "") });
                        }
                    }
                    else CRows(d).Add(new object[] { "t", T("Заряд записывается раз в 10 минут — график появится позже") });
                    var y = PcWatch.Snap(DateTime.Today.AddDays(-1));
                    if (y.Bat.Count > 0) CRows(d).Add(new object[] { "k", T("Вчера"), $"{y.Bat.Min(b => b[1])}–{y.Bat.Max(b => b[1])}%" });
                    Btns(d, Bt("🔋 Батарея", "open|battery"));
                    return d;
                }
                case "traffic":
                {
                    var days = Enumerable.Range(0, 7).Select(i => PcWatch.Snap(DateTime.Today.AddDays(i - 6))).ToList();
                    var t = days[^1];
                    d = Card(kind, "🌐 Трафик", T("сегодня"));
                    CRows(d).Add(new object[] { "T", PcWatch.Bytes(t.Down + t.Up) });
                    CRows(d).Add(new object[] { "k", "↓ " + T("Загружено"), PcWatch.Bytes(t.Down) });
                    CRows(d).Add(new object[] { "k", "↑ " + T("Отдано"), PcWatch.Bytes(t.Up) });
                    CRows(d).Add(new object[] { "g", T("7 дней, МБ"), days.Select(x => (x.Down + x.Up) >> 20).ToArray(), "#bf5af2", PcWatch.Bytes(days.Sum(x => x.Down + x.Up)), 1 });
                    long d14 = PcWatch.Days(14).Sum(x => x.Down + x.Up);
                    CRows(d).Add(new object[] { "k", T("За 14 дней"), PcWatch.Bytes(d14) });
                    Btns(d, Bt("📶 Сеть", "open|network"));
                    return d;
                }
                case "wifilog":
                {
                    var w = PcWatch.Snap(DateTime.Today).Wifi;
                    d = Card(kind, "📶 История Wi-Fi", w.Count > 0 ? T("сигнал") + " " + w[^1][1] + "%" : T("нет данных"));
                    if (w.Count > 1)
                    {
                        CRows(d).Add(new object[] { "g", T("Сигнал сегодня"), w.Select(x => x[1]).ToArray(), "#64d2ff", $"{w.Min(x => x[1])}–{w.Max(x => x[1])}%", 100 });
                        CRows(d).Add(new object[] { "k", T("Среднее"), $"{w.Average(x => x[1]):0}%" });
                        int weak = w.Count(x => x[1] < 40);
                        if (weak > 0) CRows(d).Add(new object[] { "k", T("Слабый сигнал"), $"~{weak * 5} {T("мин")}" });
                    }
                    else CRows(d).Add(new object[] { "t", T("Сигнал записывается раз в 5 минут, пока ПК подключён к Wi-Fi") });
                    Btns(d, Bt("🔄 Переподключить", "reconnect"), Bt("📶 Сеть", "open|network"));
                    return d;
                }
                case "usblog":
                {
                    d = Card(kind, "🔌 USB-журнал", T("флешки и внешние диски"));
                    try
                    {
                        foreach (var dr in DriveInfo.GetDrives().Where(x => x.DriveType == DriveType.Removable && x.IsReady).Take(4))
                            CRows(d).Add(new object[] { "k", "💾 " + dr.Name.TrimEnd('\\') + " " + Cut(dr.VolumeLabel, 14), Maint.Size(dr.AvailableFreeSpace) + " " + T("свободно из") + " " + Maint.Size(dr.TotalSize) });
                    }
                    catch { }
                    var evs = PcWatch.Days(3).SelectMany(x => x.Events.Where(e => e[1] == "usb").Select(e => (x.Date, e))).Take(8).ToList();
                    foreach (var (date, e) in evs) CRows(d).Add(new object[] { "k", (date == PcWatch.Today.Date ? "" : DateTime.Parse(date).ToString("dd.MM ")) + e[0], e[2] });
                    if (evs.Count == 0) CRows(d).Add(new object[] { "t", T("За 3 дня ничего не подключали") });
                    Btns(d, Bt("💽 Диски", "open|disks"));
                    return d;
                }
                case "iplog":
                {
                    d = Card(kind, "🌍 Внешний IP", PcWatch.LastIp ?? T("ещё не проверялся"));
                    CRows(d).Add(new object[] { "k", "VPN", Vpn.Active ? Cut(Vpn.ActiveName, 24) : T("выкл") });
                    foreach (var e in PcWatch.Days(7).SelectMany(x => x.Events.Where(e => e[1] == "ip").Reverse().Select(e => (x.Date, e))).Take(6))
                        CRows(d).Add(new object[] { "k", DateTime.Parse(e.Date).ToString("dd.MM ") + e.e[0], e.e[2] });
                    if (Busy(kind)) CRows(d).Add(new object[] { "t", T("⏳ Проверяю…") });
                    Btns(d, Bt("⟳ Проверить", "check"), Bt(Ws.IpAlert ? "🔔 Сообщать о смене" : "🔕 Не сообщать", "alert", Ws.IpAlert ? "p" : null));
                    return d;
                }
                case "downloads":
                {
                    var list = PcWatch.RecentDownloads(6);
                    d = Card(kind, "📥 Загрузки", T("последние файлы"));
                    foreach (var (at, path) in list)
                    {
                        long size = 0; try { size = new FileInfo(path).Length; } catch { }
                        CRows(d).Add(new object[] { "l", Cut(Path.GetFileName(path), 24) + " · " + Maint.Size(size), "send|" + WHash(path), "📤" });
                    }
                    if (list.Count == 0) CRows(d).Add(new object[] { "t", T("Папка «Загрузки» пуста") });
                    Btns(d, Bt(Ws.DownloadsNotify ? "🔔 Сообщать о новых" : "🔕 Не сообщать о новых", "notify", Ws.DownloadsNotify ? "p" : null));
                    return d;
                }
                case "notifstats":
                {
                    var t = PcWatch.Snap(DateTime.Today);
                    var top = t.Notifs.OrderByDescending(x => x.Value).Take(7).ToList();
                    d = Card(kind, "🔔 Кто шлёт уведомления", T("сегодня") + ": " + t.Notifs.Values.Sum());
                    int mx = top.Count > 0 ? top[0].Value : 1;
                    foreach (var a in top) CRows(d).Add(new object[] { "b", Cut(a.Key, 16), a.Value * 100 / mx, "#ff9f0a", a.Value.ToString() });
                    if (top.Count == 0) CRows(d).Add(new object[] { "t", T("Сегодня уведомлений не было") });
                    Btns(d, Bt("🔔 Последние", "open|notif"));
                    return d;
                }
                case "speedlog":
                {
                    var all = PcWatch.Days(7).SelectMany(x => Enumerable.Reverse(x.Speed).Select(s => (x.Date, s))).Take(10).ToList();
                    d = Card(kind, "🚀 История скорости", all.Count > 0 ? all[0].s[1] + " " + T("Мбит/с") : T("замеров ещё не было"));
                    if (all.Count > 1)
                        CRows(d).Add(new object[] { "g", T("Последние замеры"), Enumerable.Reverse(all).Select(x => double.TryParse(x.s[1].Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0).ToArray(), "#30d158", "", 1 });
                    foreach (var (date, s) in all.Take(5))
                        CRows(d).Add(new object[] { "k", DateTime.Parse(date).ToString("dd.MM ") + s[0], $"{s[1]} {T("Мбит/с")}" + (s[2] != "" ? $" · {s[2]} {T("мс")}" : "") });
                    if (Busy(kind)) CRows(d).Add(new object[] { "t", T("⏳ Замеряю… до 20 секунд") });
                    Btns(d, Bt("🚀 Замерить", "run", "p"));
                    return d;
                }
                case "temps":
                {
                    var z = WC<List<(string Zone, double C)>>("temps");
                    if (z == null || (now - WCAt("temps")).TotalSeconds > 10) WCSet("temps", z = PcWatch.Temperatures());
                    int cpu = SystemService.I.CpuPercent();
                    d = Card(kind, "🌡 Температура", T("нагрузка") + " " + cpu + "%");
                    foreach (var (zone, c) in z.Take(5))
                        CRows(d).Add(new object[] { "b", Cut(zone, 12), (int)Math.Clamp(c, 0, 100), c > 85 ? "#ff453a" : c > 70 ? "#ffd60a" : "#30d158", $"{c:0} °C" });
                    if (z.Count == 0) CRows(d).Add(new object[] { "t", T("Датчики не отдают температуру Windows — многие ноутбуки показывают её только в своих программах") });
                    CRows(d).Add(new object[] { "b", "CPU", cpu, Heat(cpu), cpu + "%" });
                    Btns(d, Bt("📈 Нагрузка", "open|sysmon"));
                    return d;
                }
                case "ramtop":
                {
                    var top = PcWatch.TopRam(8);
                    d = Card(kind, "🧠 Кто занял память", T("занято") + " " + SystemService.I.RamPercent() + "%");
                    long mx = top.Count > 0 ? Math.Max(1, top[0].Bytes) : 1;
                    foreach (var (name, b) in top) CRows(d).Add(new object[] { "b", Cut(name, 16), (int)(b * 100 / mx), "#bf5af2", Maint.Size(b) });
                    Btns(d, Bt("🧹 Освободить память", "free", "p"), Bt("📊 Процессы", "open|apps"));
                    return d;
                }
                case "timeline":
                {
                    var t = PcWatch.Snap(DateTime.Today);
                    d = Card(kind, "🗓 События дня", now.ToString("dd.MM") + " · " + t.Events.Count);
                    foreach (var e in Enumerable.Reverse(t.Events).Take(12)) CRows(d).Add(new object[] { "k", e[0], Cut(Ev(e), 40) });
                    if (t.Events.Count == 0) CRows(d).Add(new object[] { "t", T("Сегодня ещё ничего не случилось") });
                    return d;
                }
                case "report":
                {
                    var t = PcWatch.Snap(DateTime.Today);
                    d = Card(kind, "📊 Отчёт за день", string.IsNullOrEmpty(Ws.DailyReportTime) ? T("автоотчёт выключен") : T("автоотчёт в") + " " + Ws.DailyReportTime);
                    CRows(d).Add(new object[] { "k", "⏱ " + T("За ПК"), Du(t.ActiveSec) });
                    var top = t.Apps.OrderByDescending(x => x.Value).FirstOrDefault();
                    if (top.Key != null) CRows(d).Add(new object[] { "k", "🏆 " + T("Больше всего"), Cut(top.Key, 16) + " · " + Du(top.Value) });
                    CRows(d).Add(new object[] { "k", "🌐 " + T("Трафик"), PcWatch.Bytes(t.Down + t.Up) });
                    if (t.Bat.Count > 0) CRows(d).Add(new object[] { "k", "🔋 " + T("Заряд"), $"{t.Bat.Min(b => b[1])}–{t.Bat.Max(b => b[1])}%" });
                    CRows(d).Add(new object[] { "k", "🔒 " + T("Блокировок"), t.Events.Count(e => e[1] == "lock").ToString() });
                    CRows(d).Add(new object[] { "k", "🔔 " + T("Уведомлений"), t.Notifs.Values.Sum().ToString() });
                    if (t.Blocked > 0) CRows(d).Add(new object[] { "k", "🎯 " + T("Отвлечений свёрнуто"), t.Blocked.ToString() });
                    Btns(d, Bt("📤 Прислать текстом", "send"), Bt("⏰ " + (string.IsNullOrEmpty(Ws.DailyReportTime) ? T("Автоотчёт: выкл") : Ws.DailyReportTime), "time"));
                    return d;
                }

                // ---------------- Контроль ----------------
                case "mic":
                {
                    var a = AudioService.I;
                    double lv = a.MicLevel;
                    if (lv < 0) { d = Card(kind, "🎙 Микрофон", T("микрофон не найден")); return d; }
                    bool mu = a.MicMuted;
                    int pct = (int)Math.Round(lv * 100);
                    d = Card(kind, "🎙 Микрофон", mu ? T("выключен") : T("включён") + " · " + pct + "%");
                    CRows(d).Add(new object[] { "b", "🎙", pct, mu ? "#ff453a" : "#30d158", mu ? T("выкл") : pct + "%" });
                    Btns(d, Bt("0", "v|0"), Bt("25", "v|25"), Bt("50", "v|50"), Bt("75", "v|75"), Bt("100", "v|100"));
                    Btns(d, Bt(mu ? "🎙 Включить" : "🔇 Выключить", "mute", mu ? "s" : "d"));
                    return d;
                }
                case "wall":
                {
                    var c = WC<int[]>("wallcount");
                    if (c == null || (now - WCAt("wallcount")).TotalSeconds > 30) WCSet("wallcount", c = new[] { PcWatch.WallImages().Count });
                    d = Card(kind, "🖼 Обои", c[0] + " " + T("картинок") + " · " + Cut(Path.GetFileName(PcWatch.WallFolder), 24));
                    if (WC<string>("wallname") is { } n) CRows(d).Add(new object[] { "t", "▶ " + Cut(n, 60) });
                    if (c[0] == 0) CRows(d).Add(new object[] { "t", T("Положи картинки в папку «Изображения\\Wallpapers» или выбери папку в настройках N-Island") });
                    Btns(d, Bt("◀", "prev"), Bt("🎲 Случайные", "rnd", "p"), Bt("▶", "next"));
                    return d;
                }
                case "limits":
                {
                    var t = PcWatch.Snap(DateTime.Today);
                    d = Card(kind, "⏳ Лимиты времени", T(Ws.LimitAction == "minimize" ? "по лимиту окно сворачивается" : "по лимиту — только предупреждение"));
                    foreach (var (app, min) in PcWatch.Limits())
                    {
                        int used = AppSec(t, app);
                        CRows(d).Add(new object[] { "l", $"{Cut(app, 16)} · {used / 60}/{min} {T("мин")}" + (used >= min * 60 ? " ⛔" : ""), "del|" + WHash(app.ToLowerInvariant()), "✖" });
                    }
                    if (PcWatch.Limits().Count == 0) CRows(d).Add(new object[] { "t", T("Лимитов нет. Например: chrome 120 — не больше двух часов в день") });
                    Btns(d, Bt("✍️ Добавить", "in", "p"), Bt(Ws.LimitAction == "minimize" ? "🪟 Сворачивать" : "⚠️ Предупреждать", "act"));
                    return d;
                }
                case "focus":
                {
                    var fu = PcWatch.FocusUntil;
                    d = Card(kind, "🎯 Фокус", fu != null ? T("идёт фокус-сессия") : T("отвлекающие окна будут сворачиваться"));
                    if (fu is DateTime u) CRows(d).Add(new object[] { "d", (int)(u - now).TotalSeconds, 1, T("до конца") });
                    CRows(d).Add(new object[] { "t", T("Сворачивать:") + " " + Cut(string.Join(", ", PcWatch.FocusApps()), 120) });
                    CRows(d).Add(new object[] { "k", T("Свёрнуто сегодня"), PcWatch.Snap(DateTime.Today).Blocked.ToString() });
                    Btns(d, Bt("15", "go|15"), Bt("25", "go|25", fu == null ? "p" : null), Bt("50", "go|50"), Bt("90", "go|90"));
                    if (fu != null) Btns(d, Bt("⏹ Остановить", "stop", "d"));
                    return d;
                }
                case "procwatch":
                {
                    var names = (Ws.WatchProcs ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    d = Card(kind, "👁 Слежу за программами", T("о запуске и закрытии — сообщение сюда"));
                    foreach (var n in names.Take(8))
                    {
                        bool up = false; try { up = Process.GetProcessesByName(n).Length > 0; } catch { }
                        CRows(d).Add(new object[] { "l", (up ? "🟢 " : "⚪ ") + Cut(n, 24), "del|" + WHash(n.ToLowerInvariant()), "✖" });
                    }
                    if (names.Length == 0) CRows(d).Add(new object[] { "t", T("Пока ни за кем. Добавь, например: steam") });
                    Btns(d, Bt("✍️ Добавить", "in", "p"));
                    return d;
                }
                case "alarms":
                {
                    var list = PcWatch.AlarmList();
                    var hm = now.ToString("HH:mm");
                    var next = list.FirstOrDefault(a => string.CompareOrdinal(a.Time, hm) > 0);
                    if (next.Time == null && list.Count > 0) next = list[0];
                    d = Card(kind, "⏰ Будильники", next.Time != null ? T("ближайший") + " " + next.Time : T("нет будильников"));
                    foreach (var (time, label) in list.Take(8)) CRows(d).Add(new object[] { "l", $"{time} · {Cut(label, 24)}", "del|" + WHash(time + "|" + label), "✖" });
                    Btns(d, Bt("✍️ Добавить", "in", "p"));
                    return d;
                }
                case "fixes":
                    d = Card(kind, "🛠 Быстрые починки", T("если что-то «подвисло»"));
                    Btns(d, Bt("🔄 Перезапустить Проводник", "explorer"));
                    Btns(d, Bt("🌐 Сбросить DNS", "dns"), Bt("📶 Переподключить Wi-Fi", "wifi"));
                    Btns(d, Bt("🧠 Освободить память", "ram"));
                    return d;
                case "awake":
                {
                    bool on = SystemService.I.KeepAwake;
                    var until = PcWatch.AwakeUntil;
                    d = Card(kind, "☕ Не засыпать", on ? (until is DateTime u0 ? T("до") + " " + u0.ToString("HH:mm") : T("включено")) : T("ПК засыпает как обычно"));
                    if (on && until is DateTime u) CRows(d).Add(new object[] { "d", (int)(u - now).TotalSeconds, 1, T("осталось") });
                    Btns(d, Bt(on ? "💤 Выключить" : "☕ Включить", on ? "off" : "on", on ? "d" : "s"));
                    Btns(d, Bt("1 ч", "h|1"), Bt("2 ч", "h|2"), Bt("4 ч", "h|4"));
                    return d;
                }
                case "guard":
                {
                    int cpu = SystemService.I.CpuPercent(), ram = SystemService.I.RamPercent();
                    d = Card(kind, "🚨 Сторож нагрузки", T("сообщит сюда, если нагрузка держится 5 минут"));
                    CRows(d).Add(new object[] { "b", "CPU", cpu, Heat(cpu), cpu + "%" });
                    CRows(d).Add(new object[] { "b", "RAM", ram, Heat(ram), ram + "%" });
                    Btns(d, Bt("CPU: " + (Ws.LoadAlertCpu == 0 ? T("выкл") : "≥" + Ws.LoadAlertCpu + "%"), "cpu", Ws.LoadAlertCpu > 0 ? "p" : null),
                            Bt("RAM: " + (Ws.LoadAlertRam == 0 ? T("выкл") : "≥" + Ws.LoadAlertRam + "%"), "ram", Ws.LoadAlertRam > 0 ? "p" : null));
                    return d;
                }

                // ---------------- Виджеты для того, что уже было ----------------
                case "radios":
                {
                    var r = WC<List<(string Id, string Name, string Kind, bool On)>>("radios");
                    if ((r == null || (now - WCAt("radios")).TotalSeconds > 30) && AutoStart(kind)) Bg(kind, async () => { WCSet("radios", await RadioList()); return null; }, true);
                    d = Card(kind, "📻 Wi-Fi и Bluetooth", T("радиомодули ПК"));
                    if (r == null) CRows(d).Add(new object[] { "t", T("⏳ Загружаю…") });
                    else foreach (var x in r.Take(6)) CRows(d).Add(new object[] { "l", (x.On ? "🟢 " : "⚪ ") + Cut(x.Name, 24), "t|" + WHash(x.Id), x.On ? T("Выкл") : T("Вкл") });
                    if (r != null && r.Count == 0) CRows(d).Add(new object[] { "t", T("Радиомодули не найдены") });
                    return d;
                }
                case "privacy":
                {
                    var l = WC<List<AccessUse>>("privacy");
                    if (l == null || (now - WCAt("privacy")).TotalSeconds > 20) WCSet("privacy", l = Privacy.AccessLog(true));
                    var inUse = l.Where(x => x.InUse).ToList();
                    d = Card(kind, "🕵 Кто слушает и смотрит", inUse.Count > 0 ? "🔴 " + T("сейчас") + ": " + Cut(string.Join(", ", inUse.Select(x => x.App)), 40) : T("сейчас никто"));
                    foreach (var x in l.OrderByDescending(x => x.InUse).ThenByDescending(x => x.Last).Take(8))
                        CRows(d).Add(new object[] { "k", (x.InUse ? "🔴 " : "") + Cut(x.App, 20), T(x.Kind) + " · " + (x.InUse ? T("сейчас") : x.Last.ToString("dd.MM HH:mm")) });
                    if (l.Count == 0) CRows(d).Add(new object[] { "t", T("Камерой и микрофоном пока никто не пользовался") });
                    return d;
                }
                case "prayer":
                {
                    var p = PrayerService.Cached;
                    if (p == null && AutoStart(kind)) Bg(kind, async () => { await PrayerService.Get(); return null; }, true);
                    var nowT = now.TimeOfDay;
                    var next = p?.FirstOrDefault(x => x.At > nowT);
                    d = Card(kind, "🕌 Время молитв", next is { Name: not null } n ? T("следующая") + ": " + T(n.Name) + " " + n.At.ToString(@"hh\:mm") : now.ToString("dd.MM"));
                    if (p == null) CRows(d).Add(new object[] { "t", Busy(kind) ? T("⏳ Загружаю…") : T("Не загрузилось — нажми ⟳") });
                    else foreach (var x in p) CRows(d).Add(new object[] { "k", (next is { Name: not null } nn && nn.Name == x.Name ? "▶ " : "") + T(x.Name), x.At.ToString(@"hh\:mm") });
                    return d;
                }
                case "agenda":
                {
                    List<CalEvent> evs;
                    try { evs = Store.Data.Events.ToList(); } catch { evs = new(); }
                    var up = evs.Where(e => e.Date.Date >= DateTime.Today && e.Date.Date <= DateTime.Today.AddDays(14))
                        .OrderBy(e => e.At ?? e.Date).Take(8).ToList();
                    d = Card(kind, "📅 Ближайшие события", T("на 2 недели") + ": " + up.Count);
                    foreach (var e in up) CRows(d).Add(new object[] { "k", e.Date.ToString("dd.MM") + " " + (string.IsNullOrEmpty(e.Time) ? "" : e.Time), Cut(e.Title, 30) });
                    if (up.Count == 0) CRows(d).Add(new object[] { "t", T("Событий нет — они добавляются в календаре острова") });
                    Btns(d, Bt("📅 Календарь", "open|calendar"));
                    return d;
                }
                case "shelf":
                {
                    List<string> files;
                    try { files = Store.Data.Shelf.ToList(); } catch { files = new(); }
                    d = Card(kind, "📦 Полка", T("файлов") + ": " + files.Count);
                    foreach (var f in files.Take(7))
                    {
                        long size = 0; try { size = File.Exists(f) ? new FileInfo(f).Length : 0; } catch { }
                        CRows(d).Add(new object[] { "l", Cut(Path.GetFileName(f.TrimEnd('\\')), 24) + (size > 0 ? " · " + Maint.Size(size) : ""), "send|" + WHash(f), "📤" });
                    }
                    if (files.Count == 0) CRows(d).Add(new object[] { "t", T("Полка пуста — перетащи файлы на остров") });
                    return d;
                }
                case "translate":
                {
                    var tr = WC<string[]>("tr");
                    d = Card(kind, "🌐 Переводчик", T("на язык") + ": " + _trTo.ToUpperInvariant());
                    if (Busy(kind)) CRows(d).Add(new object[] { "t", T("⏳ Перевожу…") });
                    if (tr != null)
                    {
                        CRows(d).Add(new object[] { "t", Cut(tr[0], 120) });
                        CRows(d).Add(new object[] { "y", "📋", Cut(tr[1], 330) });
                        CRows(d).Add(new object[] { "k", T("Язык"), $"{tr[2]} → {tr[3]}" });
                    }
                    Btns(d, Bt("✍️ Перевести текст", "in", "p"), Bt("→ " + _trTo.ToUpperInvariant(), "to"));
                    return d;
                }
                case "cleanup":
                {
                    var res = WC<List<string[]>>("clean");
                    long total = res?.Sum(x => long.Parse(x[1])) ?? 0;
                    d = Card(kind, "🧹 Очистка", res == null ? T("нажми «Найти мусор»") : T("можно освободить") + " " + Maint.Size(total));
                    if (Busy(kind)) CRows(d).Add(new object[] { "t", T("⏳ Работаю… до минуты") });
                    if (res != null)
                    {
                        long mx = Math.Max(1, res.Count > 0 ? long.Parse(res[0][1]) : 1);
                        foreach (var x in res.Take(7)) CRows(d).Add(new object[] { "b", Cut(T(x[0]), 18), (int)(long.Parse(x[1]) * 100 / mx), "#ff9f0a", Maint.Size(long.Parse(x[1])) });
                    }
                    var row = new List<object[]> { Bt("🔍 Найти мусор", "scan") };
                    if (total > 0) row.Add(Bt("🧹 Очистить", "clean", "d"));
                    Btns(d, row.ToArray());
                    return d;
                }
                case "news":
                {
                    var n = WC<string>("news");
                    if ((n == null || (now - WCAt("news")).TotalMinutes > 15) && AutoStart(kind)) Bg(kind, async () => { WCSet("news", await ApiTools2.News()); return null; }, true);
                    d = Card(kind, "📰 Новости", n == null ? null : WCAt("news").ToString("HH:mm"));
                    if (n == null) CRows(d).Add(new object[] { "t", Busy(kind) ? T("⏳ Загружаю…") : T("Не загрузились — нажми ⟳") });
                    else foreach (var line in n.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(6)) CRows(d).Add(new object[] { "t", Cut(line, 110) });
                    return d;
                }
            }
            return null;
        }

        // ------------------------------------------------------------------
        //  Действия
        // ------------------------------------------------------------------

        async Task<string> CardAction4(string kind, string head, string arg)
        {
            switch (kind, head)
            {
                case ("screentime", "day"): _stDay = _stDay == 0 ? 1 : 0; return null;
                case ("sessions", "lock"):
                    if (!Flag("power")) return T("🔐 «Питание» выключено — включается на ПК");
                    Native.LockWorkStation(); return "🔒";
                case ("wifilog", "reconnect"): return T(await WifiTools.Reconnect() ?? "📶 Переподключено");
                case ("iplog", "check"): Bg("iplog", async () => { await PcWatch.CheckIpNow(); return null; }); return null;
                case ("iplog", "alert"): await Ui(() => Ws.IpAlert = !Ws.IpAlert); return null;
                case ("downloads", "send"):
                {
                    var f = PcWatch.RecentDownloads(30).FirstOrDefault(x => WHash(x.Path) == arg).Path;
                    if (f == null) return T("Файла уже нет");
                    var r = await SendPath(f);
                    return r == null ? T("📤 Файл в чате") : Cut(r, 120);
                }
                case ("downloads", "notify"): await Ui(() => Ws.DownloadsNotify = !Ws.DownloadsNotify); return null;
                case ("speedlog", "run"): Bg("speedlog", async () => { await ApiTools.SpeedTest(); return null; }); return null;
                case ("ramtop", "free"): return T("🧠 Освобождено") + " " + Maint.Size(await Maint.OptimizeRam());
                case ("report", "send"): await Message(PcWatch.Report(DateTime.Today)); return T("📤 Отчёт в чате");
                case ("report", "time"):
                {
                    var times = new[] { "", "21:00", "23:00", "09:00" };
                    int i = Array.IndexOf(times, Ws.DailyReportTime ?? "");
                    var nt = times[(i + 1) % times.Length];
                    await Ui(() => Ws.DailyReportTime = nt);
                    return null;
                }
                case ("mic", "v") when int.TryParse(arg, out var mv) && mv is >= 0 and <= 100:
                    await Ui(() => { AudioService.I.MicLevel = mv / 100.0; if (mv > 0) AudioService.I.SetMicMute(false); }); return null;
                case ("mic", "mute"): await Ui(() => AudioService.I.ToggleMicMute()); return null;
                case ("wall", "prev" or "next" or "rnd"):
                {
                    var n = await Ui(() => PcWatch.NextWallpaper(head == "prev" ? -1 : head == "next" ? 1 : 0));
                    WCSet("wallname", n);
                    return n == null ? T("В папке нет картинок") : "🖼 " + Cut(n, 40);
                }
                case ("limits", "del"):
                {
                    var app = PcWatch.Limits().Keys.FirstOrDefault(a => WHash(a.ToLowerInvariant()) == arg);
                    if (app == null) return T("Нет такого лимита");
                    await Ui(() => PcWatch.SetLimit(app, 0));
                    return "✖ " + app;
                }
                case ("limits", "act"): await Ui(() => Ws.LimitAction = Ws.LimitAction == "minimize" ? "warn" : "minimize"); return null;
                case ("focus", "go") when int.TryParse(arg, out var fm) && fm is > 0 and <= 240:
                    await Ui(() => PcWatch.StartFocus(fm)); return "🎯 " + fm + " " + T("мин");
                case ("focus", "stop"): await Ui(() => PcWatch.StartFocus(0)); return "⏹";
                case ("procwatch", "del"):
                {
                    var names = (Ws.WatchProcs ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
                    int i = names.FindIndex(n => WHash(n.ToLowerInvariant()) == arg);
                    if (i < 0) return T("Уже убрано");
                    names.RemoveAt(i);
                    await Ui(() => Ws.WatchProcs = string.Join(", ", names));
                    return null;
                }
                case ("alarms", "del"):
                {
                    var list = PcWatch.AlarmList();
                    int i = list.FindIndex(a => WHash(a.Time + "|" + a.Label) == arg);
                    if (i < 0) return T("Уже удалён");
                    list.RemoveAt(i);
                    await Ui(() => PcWatch.SetAlarms(list));
                    return null;
                }
                case ("fixes", "explorer"):
                    if (_fixConfirm == "explorer" && (DateTime.Now - _fixConfirmAt).TotalSeconds < 10)
                    {
                        _fixConfirm = null;
                        var err = await Task.Run(PcWatch.RestartExplorer);
                        return err == null ? T("🔄 Проводник перезапущен") : Cut(err, 100);
                    }
                    _fixConfirm = "explorer"; _fixConfirmAt = DateTime.Now;
                    return T("Нажми ещё раз в течение 10 секунд — окна Проводника закроются");
                case ("fixes", "dns"): return T(Maint.FlushDns() ? "✓ Кэш DNS сброшен" : "Не удалось сбросить кэш DNS");
                case ("fixes", "wifi"): return T(await WifiTools.Reconnect() ?? "📶 Переподключено");
                case ("fixes", "ram"): return T("🧠 Освобождено") + " " + Maint.Size(await Maint.OptimizeRam());
                case ("awake", "on" or "off"): await Ui(() => PcWatch.SetAwake(head == "on", 0)); return null;
                case ("awake", "h") when int.TryParse(arg, out var hh) && hh is > 0 and <= 12: await Ui(() => PcWatch.SetAwake(true, hh)); return "☕ " + hh + " " + T("ч");
                case ("guard", "cpu"): await Ui(() => Ws.LoadAlertCpu = Ws.LoadAlertCpu switch { 0 => 80, 80 => 90, 90 => 95, _ => 0 }); return null;
                case ("guard", "ram"): await Ui(() => Ws.LoadAlertRam = Ws.LoadAlertRam switch { 0 => 85, 85 => 90, 90 => 95, _ => 0 }); return null;
                case ("radios", "t"):
                {
                    var list = await RadioList();
                    var it = list.FirstOrDefault(x => WHash(x.Id) == arg);
                    if (it.Id == null) return T("Нет такого модуля");
                    var r = await RadioToggle(it.Id);
                    WCSet("radios", await RadioList());
                    return Cut(T(r ?? ""), 100);
                }
                case ("radios", "ref"): WCSet("radios", await RadioList()); return null;
                case ("privacy", "ref"): WCSet("privacy", null); return null;
                case ("prayer", "ref"): await PrayerService.Get(true); return null;
                case ("news", "ref"): Bg("news", async () => { WCSet("news", await ApiTools2.News()); return null; }); return null;
                case ("temps", "ref"): WCSet("temps", null); return null;
                case ("shelf", "send"):
                {
                    var f = Store.Data.Shelf.ToList().FirstOrDefault(x => WHash(x) == arg);
                    if (f == null) return T("Файла уже нет на полке");
                    var r = await SendPath(f);
                    return r == null ? T("📤 Файл в чате") : Cut(r, 120);
                }
                case ("translate", "to"): _trTo = TrLangs[(Array.IndexOf(TrLangs, _trTo) + 1) % TrLangs.Length]; return null;
                case ("cleanup", "scan"): Bg("cleanup", CleanScan); return null;
                case ("cleanup", "clean"):
                {
                    var res = WC<List<string[]>>("clean");
                    if (res == null || res.Count == 0) return T("Сначала найди мусор");
                    if (_cleanConfirm != "clean" || (DateTime.Now - _cleanConfirmAt).TotalSeconds > 10)
                    {
                        _cleanConfirm = "clean"; _cleanConfirmAt = DateTime.Now;
                        return T("Нажми ещё раз в течение 10 секунд — удалятся только кэш и временные файлы");
                    }
                    _cleanConfirm = null;
                    var keys = res.Select(x => x[2]).ToList();
                    Bg("cleanup", async () =>
                    {
                        var r = await Cleaner.Clean(Cleaner.Targets.Where(t => keys.Contains(t.Key) && t.Safety == Safety.Safe && !t.Admin).ToList());
                        WCSet("clean", null);
                        return T("🧹 Освобождено") + " " + Maint.Size(r.Freed);
                    });
                    return null;
                }
            }
            return null;
        }

        /// <summary>Поиск мусора для виджета: только безопасные цели без прав администратора.</summary>
        static async Task<string> CleanScan()
        {
            var ts = Cleaner.Targets.Where(t => t.Safety == Safety.Safe && !t.InfoOnly && !t.Admin).ToList();
            await Cleaner.Scan(ts);
            WCSet("clean", ts.Where(t => t.Bytes > 0).OrderByDescending(t => t.Bytes).Select(t => new[] { t.Title, t.Bytes.ToString(), t.Key }).ToList());
            return null;
        }

        /// <summary>Ответ на «✍️» в новых виджетах.</summary>
        async Task<(string Html, object Keys)?> WidgetInput4(string kind, string text)
        {
            switch (kind)
            {
                case "limits":
                {
                    var m = System.Text.RegularExpressions.Regex.Match(text, @"^\s*(.+?)\s+(\d{1,4})\s*(мин|m|min)?\s*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (!m.Success) return ("Напиши так: chrome 120 (минут в день, 0 — снять лимит)", null);
                    var app = m.Groups[1].Value.Trim().Replace(".exe", "", StringComparison.OrdinalIgnoreCase);
                    int min = Math.Min(1440, int.Parse(m.Groups[2].Value));
                    await Ui(() => PcWatch.SetLimit(app, min));
                    _ = PushKind(kind);
                    return (min > 0 ? $"⏳ {H(app)}: не больше {min} мин в день" : $"⏳ Лимит для {H(app)} снят", null);
                }
                case "procwatch":
                {
                    var n = text.Trim().Replace(".exe", "", StringComparison.OrdinalIgnoreCase);
                    if (n.Length is < 2 or > 60 || n.Contains(',')) return ("Напиши имя процесса, например: steam", null);
                    await Ui(() => Ws.WatchProcs = string.Join(", ", (Ws.WatchProcs ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Append(n).Distinct(StringComparer.OrdinalIgnoreCase)));
                    _ = PushKind(kind);
                    return ($"👁 Слежу за {H(n)}: напишу, когда запустится и закроется", null);
                }
                case "alarms":
                {
                    var m = System.Text.RegularExpressions.Regex.Match(text, @"^\s*(\d{1,2})[:.](\d{2})\s*(.*)$");
                    if (!m.Success || int.Parse(m.Groups[1].Value) > 23 || int.Parse(m.Groups[2].Value) > 59) return ("Напиши так: 07:30 Подъём", null);
                    var time = $"{int.Parse(m.Groups[1].Value):00}:{m.Groups[2].Value}";
                    var label = m.Groups[3].Value.Trim();
                    if (label == "") label = "Будильник";
                    await Ui(() => PcWatch.SetAlarms(PcWatch.AlarmList().Append((time, Cut(label, 60)))));
                    _ = PushKind(kind);
                    return ($"⏰ Будильник на {time}: {H(label)}", null);
                }
                case "translate":
                {
                    var src = text.Length > 1500 ? text[..1500] : text;
                    var to = _trTo;
                    Bg("translate", async () =>
                    {
                        var r = await TranslateService.Translate(src, to);
                        WCSet("tr", new[] { src, r.Text ?? "", r.From ?? "?", r.To ?? to });
                        return null;
                    });
                    return ("🌐 Перевожу — ответ появится в виджете", null);
                }
            }
            return null;
        }
    }
}
