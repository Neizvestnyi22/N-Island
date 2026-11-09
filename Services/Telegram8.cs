using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NIsland.Core;

namespace NIsland.Services
{
    // =====================================================================
    //  Виджеты Telegram, вторая партия (20): вода, уведомления, ИИ-агенты, ИИ над буфером, VPN, Wi-Fi вокруг,
    //  скорость интернета, курсы валют, солнце и луна, здоровье ПК, автозагрузка, окна, обновления программ,
    //  история треков, горячие клавиши, пульт презентации, отложенные команды, защита, календарь, дыхание.
    //  Долгая работа (замер скорости, winget, сканирование, ИИ) отвечает сразу «⏳», а результат бот
    //  досылает через editMessageWidgetData — у ответа на нажатие всего 15 секунд.
    // =====================================================================
    public partial class TelegramHub
    {
        // Календарь: целиком в виджете, листается без бота
        const string CalendarHtml = """
<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><style>
html,body{margin:0}body{font:13px system-ui,sans-serif;color:var(--tg-theme-text-color,#eee);padding:10px 12px}
.h{display:flex;justify-content:space-between;align-items:center;font-weight:600;font-size:15px;text-transform:capitalize}
.h button{border:0;border-radius:8px;background:#8882;color:inherit;padding:4px 12px;font-size:15px}
.g{display:grid;grid-template-columns:repeat(7,1fr);gap:3px;margin-top:8px;text-align:center}
.g span{padding:5px 0;border-radius:8px;font-variant-numeric:tabular-nums}.w{color:var(--tg-theme-hint-color,#999);font-size:11px}
.o{opacity:.3}.we{color:#ff6b6b}.t{background:var(--tg-theme-button-color,#3390ec);color:#fff;font-weight:700}
#f{color:var(--tg-theme-hint-color,#999);font-size:12px;margin-top:6px}
</style></head><body><div class="h"><button id="p">‹</button><span id="m"></span><button id="n">›</button></div><div class="g" id="g"></div><div id="f"></div><script>
var o=0;function $(i){return document.getElementById(i)}
function draw(){var n=new Date(),d=new Date(n.getFullYear(),n.getMonth()+o,1),y=d.getFullYear(),M=d.getMonth(),h='';
$('m').textContent=d.toLocaleDateString(undefined,{month:'long',year:'numeric'});
for(var w=0;w<7;w++)h+='<span class="w">'+new Date(2024,0,1+w).toLocaleDateString(undefined,{weekday:'short'})+'</span>';
var s=(d.getDay()+6)%7,days=new Date(y,M+1,0).getDate(),pv=new Date(y,M,0).getDate();
for(var i=0;i<42;i++){var v=i-s+1,c='';if(v<1){c='o';v=pv+v}else if(v>days){c='o';v-=days}else if(y==n.getFullYear()&&M==n.getMonth()&&v==n.getDate())c='t';else if(i%7>4)c='we';h+='<span class="'+c+'">'+v+'</span>'}
$('g').innerHTML=h;var st=new Date(n.getFullYear(),0,1),doy=Math.floor((n-st)/864e5)+1,wk=Math.ceil((doy+(st.getDay()+6)%7)/7);
$('f').textContent='📅 '+n.toLocaleDateString(undefined,{weekday:'long',day:'numeric',month:'long'})+' · #'+doy+' · W'+wk}
$('p').onclick=function(){o--;draw()};$('n').onclick=function(){o++;draw()};$('m').onclick=function(){o=0;draw()};draw();
</script></body></html>
""";

        // Дыхательная гимнастика: шар растёт на вдохе, держится на задержке, сдувается на выдохе
        const string BreathHtml = """
<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><style>
html,body{margin:0}body{font:13px system-ui,sans-serif;color:var(--tg-theme-text-color,#eee);padding:10px;text-align:center}
#o{width:150px;height:150px;margin:6px auto;border-radius:50%;display:flex;align-items:center;justify-content:center;font-size:16px;font-weight:600;cursor:pointer;
background:radial-gradient(circle at 35% 30%,#9be7ff 0,#3390ec 75%);color:#fff;transform:scale(.55);transition:transform linear;box-shadow:0 0 40px #3390ec80}
#c{color:var(--tg-theme-hint-color,#999);min-height:16px}.r{display:flex;gap:6px;margin-top:8px}button{flex:1;border:0;border-radius:10px;padding:7px 4px;background:#8882;color:inherit}
button.a{background:var(--tg-theme-button-color,#3390ec);color:#fff}
</style></head><body><div id="o">▶</div><div id="c"></div><div class="r" id="r"></div><script>
var L=navigator.language||'',W=/^uk/.test(L)?['Вдих','Затримка','Видих']:/^(ru|be|kk)/.test(L)?['Вдох','Задержка','Выдох']:['Inhale','Hold','Exhale'];
var P={'4·4·4·4':[4,4,4,4],'4·7·8':[4,7,8,0],'5·5':[5,0,5,0]},k='4·4·4·4',on=0,ph=0,left=0,n=0,tm;function $(i){return document.getElementById(i)}
function lbl(){$('o').textContent=W[ph==0?0:ph==2?2:1]+' '+left;$('c').textContent='× '+n}
function step(){var s=P[k],o=$('o');while(!s[ph])ph=(ph+1)%4;left=s[ph];o.style.transitionDuration=(ph%2?0:left)+'s';o.style.transform='scale('+(ph<2?1:.55)+')';lbl();
tm=setInterval(function(){left--;if(left>0)return lbl();clearInterval(tm);if(ph==3||(ph==2&&!s[3]))n++;ph=(ph+1)%4;if(on)step()},1000)}
$('o').onclick=function(){on=!on;clearInterval(tm);if(on){ph=0;step()}else{$('o').textContent='▶';$('o').style.transform='scale(.55)'}};
Object.keys(P).forEach(function(m){var b=document.createElement('button');b.textContent=m;if(m==k)b.className='a';b.onclick=function(){k=m;
document.querySelectorAll('button').forEach(function(x){x.className=x==b?'a':''});if(on){clearInterval(tm);ph=0;step()}};$('r').appendChild(b)});
</script></body></html>
""";

        // ------------------------------------------------------------------
        //  Фоновая работа и кэш результатов
        // ------------------------------------------------------------------

        static readonly HashSet<string> _wBusy = new();
        static readonly Dictionary<string, (DateTime At, object Data)> _wCache = new();
        static readonly Dictionary<string, DateTime> _wTried = new();

        static TData WC<TData>(string key) where TData : class { lock (_wCache) return _wCache.TryGetValue(key, out var v) ? v.Data as TData : null; }
        static DateTime WCAt(string key) { lock (_wCache) return _wCache.TryGetValue(key, out var v) ? v.At : DateTime.MinValue; }
        static void WCSet(string key, object data) { lock (_wCache) _wCache[key] = (DateTime.Now, data); }
        static bool Busy(string kind) { lock (_wBusy) return _wBusy.Contains(kind); }

        /// <summary>Сам запускать загрузку не чаще раза в 2 минуты — иначе при ошибке состояние без данных запускало бы её снова и снова.</summary>
        static bool AutoStart(string kind)
        {
            lock (_wTried)
            {
                if (_wTried.TryGetValue(kind, out var t) && (DateTime.Now - t).TotalMinutes < 2) return false;
                _wTried[kind] = DateTime.Now;
                return true;
            }
        }

        /// <summary>Долгая работа в фоне: один запуск на вид; по готовности — новое состояние во все недавние виджеты этого вида.</summary>
        void Bg(string kind, Func<Task<string>> work, bool auto = false)
        {
            lock (_wBusy) if (!_wBusy.Add(kind)) return;
            _ = Task.Run(async () =>
            {
                string note = null;
                try { note = await work(); }
                catch (Exception e) { note = "⚠️ " + Cut(e.Message, 120); Log.Info($"widget {kind}: {e.Message}"); }
                finally { lock (_wBusy) _wBusy.Remove(kind); }
                // сам запущенная загрузка могла закончиться раньше, чем Telegram вернул номер сообщения с виджетом
                if (auto) await Task.Delay(2000);
                await PushKind(kind, note);
            });
        }

        async Task PushKind(string kind, string note = null)
        {
            List<long> ids;
            lock (_widgets) ids = _widgets.Where(w => w.Value.Kind == kind).Select(w => w.Key).OrderByDescending(x => x).Take(5).ToList();
            foreach (var id in ids)
            {
                try { await Call("editMessageWidgetData", new { chat_id = V("chat"), message_id = id, chat_data = WState(kind, note) }); }
                catch (TgError e) when (e.Message.Contains("not modified")) { }
                catch (TgError e) { Log.Info("telegram widget push: " + e.Message); }
            }
        }

        static readonly (string Label, string Combo, string Style)[] WKeys =
        {
            ("Ctrl+C", "ctrl+c", null), ("Ctrl+V", "ctrl+v", null), ("Ctrl+Z", "ctrl+z", null), ("Ctrl+S", "ctrl+s", null),
            ("Alt+Tab", "alt+tab", null), ("Win+D", "win+d", null), ("Win+V", "win+v", null), ("PrtSc", "prtsc", null),
            ("Esc", "esc", null), ("Tab", "tab", null), ("␣", "space", null), ("Enter ⏎", "enter", "p"),
            ("←", "left", null), ("↑", "up", null), ("↓", "down", null), ("→", "right", null),
            ("Диспетчер задач", "ctrl+shift+esc", null), ("Alt+F4", "alt+f4", "d"),
        };

        static readonly Dictionary<string, string> SlideKeys = new() { ["left"] = "left", ["right"] = "right", ["f5"] = "f5", ["sf5"] = "shift+f5", ["b"] = "b", ["esc"] = "esc" };

        static List<string[]> ParseRates(string text)
        {
            var line = text.Split('\n').Skip(1).FirstOrDefault() ?? "";
            return line.Split('·', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(p => p.Split(' ', 2)).Where(p => p.Length == 2).Select(p => new[] { p[0], p[1] }).ToList();
        }

        List<string[]> DefenderRows()
        {
            var outp = Ps("$s = Get-MpComputerStatus; 'Защита в реальном времени|' + $s.RealTimeProtectionEnabled; 'Защитник Windows|' + $s.AntivirusEnabled; 'Базы Защитника|' + $s.AntivirusSignatureLastUpdated.ToString('dd.MM HH:mm')", 30000);
            return outp.Split('\n').Select(l => l.Trim().Split('|', 2)).Where(r => r.Length == 2)
                .Select(r => new[] { T(r[0]), r[1] switch { "True" => "🟢 " + T("да"), "False" => "🔴 " + T("нет"), var x => x } }).ToList();
        }

        async Task<string> HealthCheck()
        {
            var rows = new List<string[]>();
            try
            {
                var (last, reboot, days) = await PcHealth.WindowsUpdate();
                rows.Add(new[] { T("Обновления Windows"), last is DateTime l ? l.ToString("dd.MM.yyyy") : "?" });
                rows.Add(new[] { T("Ждёт перезагрузки"), reboot ? "🟡 " + T("да") : "🟢 " + T("нет") });
                rows.Add(new[] { T("Работает без перезагрузки"), days + " " + T("дн.") });
            }
            catch { }
            try { var c = await PcHealth.Crashes(7); rows.Add(new[] { T("Сбои программ за неделю"), (c.Count == 0 ? "🟢 " : "🟡 ") + c.Count }); } catch { }
            try { var dv = await PcHealth.ProblemDevices(); rows.Add(new[] { T("Устройства с ошибкой"), (dv.Count == 0 ? "🟢 " : "🔴 ") + dv.Count }); } catch { }
            try { var cpu = await PcHealth.Cpu(); rows.Add(new[] { T("Процессор"), cpu.Throttled ? "🟡 " + Cut(T(cpu.Why ?? "сбрасывает частоту"), 30) : $"🟢 {cpu.MhzNow} {T("МГц")}" }); } catch { }
            try
            {
                foreach (var dk in Maint.Disks().Where(x => x.Total > 0 && x.Free < x.Total / 10).Take(2))
                    rows.Add(new[] { T("Мало места") + " " + dk.Name.TrimEnd('\\'), "🔴 " + Maint.Size(dk.Free) });
            }
            catch { }
            WCSet("health", rows);
            return null;
        }

        // ------------------------------------------------------------------
        //  Состояния
        // ------------------------------------------------------------------

        Dictionary<string, object> CardState2(string kind)
        {
            Dictionary<string, object> d;
            string wait = T("⏳ Выполняется…");
            switch (kind)
            {
                case "water":
                {
                    int ml = Water.TodayMl, goal = Math.Max(1, Store.Settings.WaterGoal), pct = (int)Math.Round(100.0 * ml / goal);
                    d = Card(kind, "💧 Вода за сегодня", $"{ml} / {goal} {T("мл")}");
                    CRows(d).Add(new object[] { "b", "💧", pct, pct >= 100 ? "#30d158" : "#64d2ff", pct + "%" });
                    if (Water.LastDrink is DateTime ld) CRows(d).Add(new object[] { "k", T("Последний стакан"), ld.Date == DateTime.Today ? ld.ToString("HH:mm") : ld.ToString("dd.MM HH:mm") });
                    if (Water.NextReminder is DateTime nr && nr > DateTime.Now) CRows(d).Add(new object[] { "k", T("Напомню"), nr.ToString("HH:mm") });
                    Btns(d, Bt("+150", "150"), Bt("+250", "250", "p"), Bt("+500", "500"));
                    Btns(d, Bt("↶ Отменить", "undo"), Bt("⟳", "ref"));
                    return d;
                }
                case "notif":
                {
                    List<LogItem> items;
                    try { items = NotificationLog.Items.Take(6).ToList(); } catch { items = new(); }
                    d = Card(kind, "🔔 Уведомления на ПК", NotificationLog.Unread > 0 ? T("непрочитанных: ") + NotificationLog.Unread : null);
                    if (items.Count == 0) CRows(d).Add(new object[] { "t", T("Пока пусто") });
                    foreach (var it in items)
                        CRows(d).Add(new object[] { "k", $"{it.Time:HH:mm} · {Cut(it.App ?? "", 16)}", Cut(string.Join(" — ", new[] { it.Title, it.Body }.Where(s => !string.IsNullOrWhiteSpace(s))).Replace('\n', ' '), 60) });
                    Btns(d, Bt("✓ Прочитано", "read"), Bt("🧹 Очистить", "clear", "d"), Bt("⟳", "ref"));
                    return d;
                }
                case "agents":
                {
                    List<AgentEvent> last;
                    try { last = (Store.Data?.AgentEvents ?? new()).OrderByDescending(e => e.At).Take(6).ToList(); } catch { last = new(); }
                    d = Card(kind, "🤖 ИИ-агенты на ПК", last.Count == 0 ? T("событий пока нет") : T("последнее: ") + last[0].At.ToString("dd.MM HH:mm"));
                    foreach (var e in last) CRows(d).Add(new object[] { "k", $"{e.At:HH:mm} {Cut(e.Agent, 14)}", Cut($"{e.Kind}: {e.Message}".Replace('\n', ' '), 70) });
                    Btns(d, Bt("⟳", "ref"));
                    return d;
                }
                case "ai":
                {
                    string clip = "";
                    try { clip = ClipboardService.I.Items.FirstOrDefault()?.Text ?? ""; } catch { }
                    d = Card(kind, "✨ ИИ над буфером обмена", clip == "" ? T("буфер пуст — скопируй текст на ПК") : "📋 " + Cut(clip.Replace('\n', ' '), 40));
                    if (Busy(kind)) CRows(d).Add(new object[] { "t", T("⏳ ИИ думает…") });
                    else if (WC<string>("ai") is { } res) CRows(d).Add(new object[] { "y", "📋", Cut(res, 230) });
                    Btns(d, Bt("📝 Кратко", "sum"), Bt("🌐 Перевод", "tr"));
                    Btns(d, Bt("✏️ Исправить", "fix"), Bt("💡 Объяснить", "exp"));
                    if (WC<string>("ai") != null) Btns(d, Bt("📋 Ответ в буфер ПК", "put", "p"));
                    return d;
                }
                case "vpn":
                {
                    var ip = Vpn.PublicIp;
                    d = Card(kind, Vpn.Active ? "🔒 VPN включён" : "🔓 VPN выключен", Vpn.Active ? Cut(Vpn.ActiveName, 40) : null);
                    if (!string.IsNullOrEmpty(ip.Ip)) CRows(d).Add(new object[] { "k", T("Внешний IP"), $"{ip.Flag} {ip.Ip}" + (string.IsNullOrEmpty(ip.City) ? "" : " · " + Cut(ip.City, 16)) });
                    if (Busy(kind)) CRows(d).Add(new object[] { "t", wait });
                    List<VpnItem> items;
                    try { items = Vpn.Items.ToList(); } catch { items = new(); }
                    if (items.Count == 0) CRows(d).Add(new object[] { "t", T("VPN-программы не найдены") });
                    foreach (var i in items.Take(6))
                        CRows(d).Add(new object[] { "l", (i.State == VpnState.On ? "🟢 " : i.State == VpnState.Connecting ? "🟡 " : "⚪ ") + Cut(i.Name, 26),
                            i.Switch != null ? "v|" + Cut(i.Key, 50) : null, T(i.State == VpnState.On ? "Выкл" : "Вкл") });
                    Btns(d, Bt("🌐 Проверить IP", "ip"), Bt("⟳", "ref"));
                    return d;
                }
                case "wifiscan":
                {
                    var aps = WC<List<WifiAp>>("aps");
                    d = Card(kind, "📡 Wi-Fi вокруг", aps == null ? null : T("сетей: ") + aps.Count + " · " + WCAt("aps").ToString("HH:mm"));
                    if (Busy(kind)) CRows(d).Add(new object[] { "t", T("⏳ Слушаю эфир…") });
                    else if (aps == null) CRows(d).Add(new object[] { "t", T("Нажми «Сканировать»") });
                    foreach (var ap in (aps ?? new()).OrderByDescending(a => a.Current).ThenByDescending(a => a.Signal).Take(7))
                        CRows(d).Add(new object[] { "b", (ap.Current ? "✓ " : "") + Cut(string.IsNullOrEmpty(ap.Ssid) ? T("(скрытая)") : ap.Ssid, 16), ap.Signal,
                            ap.Signal < 40 ? "#ff453a" : ap.Signal < 65 ? "#ffd60a" : "#30d158", $"{ap.Band} · {ap.Channel}" });
                    Btns(d, Bt("📡 Сканировать", "scan", "p"));
                    return d;
                }
                case "speed":
                {
                    var res = WC<string>("speed");
                    d = Card(kind, "🚀 Скорость интернета", res == null ? null : WCAt("speed").ToString("dd.MM HH:mm"));
                    if (Busy(kind)) CRows(d).Add(new object[] { "t", T("⏳ Замеряю… до 30 секунд") });
                    else if (res == null) CRows(d).Add(new object[] { "t", T("Ещё не замеряли") });
                    else foreach (var p in res.Split('·', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(4)) CRows(d).Add(new object[] { "t", Cut(T(p), 80) });
                    Btns(d, Bt("▶ Замерить", "run", "p"));
                    return d;
                }
                case "rates":
                {
                    var r = WC<List<string[]>>("rates");
                    if ((r == null || (DateTime.Now - WCAt("rates")).TotalMinutes > 30) && AutoStart(kind))
                        Bg(kind, async () => { WCSet("rates", ParseRates(await ApiTools2.Rates())); return null; }, true);
                    d = Card(kind, "💱 Курсы валют", T("к ") + Currency.LocalCode + (r == null ? "" : " · " + WCAt("rates").ToString("HH:mm")));
                    if (r == null) CRows(d).Add(new object[] { "t", Busy(kind) ? T("⏳ Загружаю…") : T("Не загрузились — нажми ⟳") });
                    else foreach (var x in r.Take(8)) CRows(d).Add(new object[] { "k", x[0], x[1] });
                    Btns(d, Bt("⟳", "ref"));
                    return d;
                }
                case "sky":
                {
                    var w = WeatherService.Cached;
                    d = Card(kind, "🌗 Солнце и луна", w?.City);
                    if (w?.Sunrise is DateTime sr0 && w.Sunset is DateTime ss0)
                    {
                        DateTime sr = DateTime.Today + sr0.TimeOfDay, ss = DateTime.Today + ss0.TimeOfDay, now = DateTime.Now;
                        var len = ss - sr;
                        CRows(d).Add(new object[] { "k", "🌅 " + T("Восход") + " · 🌇 " + T("Закат"), $"{sr:HH:mm} – {ss:HH:mm}" });
                        CRows(d).Add(new object[] { "k", T("Световой день"), $"{(int)len.TotalHours} {T("ч")} {len.Minutes} {T("мин")}" });
                        if (now > sr && now < ss) CRows(d).Add(new object[] { "d", (int)(ss - now).TotalSeconds, 1, T("до заката") });
                        else if (now < sr) CRows(d).Add(new object[] { "d", (int)(sr - now).TotalSeconds, 1, T("до восхода") });
                    }
                    else CRows(d).Add(new object[] { "t", T("Восход и закат появятся, когда загрузится погода") });
                    var moon = ApiTools2.Moon().Split(" · ");
                    CRows(d).Add(new object[] { "T", T(moon[0]) });
                    foreach (var p in moon.Skip(1)) CRows(d).Add(new object[] { "t", T(p) });
                    Btns(d, Bt("⟳", "ref"));
                    return d;
                }
                case "health":
                {
                    var r = WC<List<string[]>>("health");
                    if (r == null && AutoStart(kind)) Bg(kind, HealthCheck, true);
                    d = Card(kind, "🩺 Здоровье ПК", r == null ? null : T("проверено ") + WCAt("health").ToString("dd.MM HH:mm"));
                    if (Busy(kind)) CRows(d).Add(new object[] { "t", T("⏳ Смотрю обновления, сбои, драйверы, процессор…") });
                    foreach (var x in r ?? new()) CRows(d).Add(new object[] { "k", x[0], x[1] });
                    Btns(d, Bt("🩺 Проверить снова", "run", "p"));
                    return d;
                }
                case "startup":
                {
                    var items = Maint.Startup();
                    d = Card(kind, "🚀 Автозагрузка", T("включено: ") + items.Count(i => i.Enabled) + " / " + items.Count);
                    foreach (var i in items.OrderByDescending(i => i.CanToggle).Take(9))
                        CRows(d).Add(new object[] { "l", (i.Enabled ? "🟢 " : "⚪ ") + Cut(i.Name, 26), i.CanToggle ? "s|" + Cut(i.Name, 60) : null, T(i.Enabled ? "Выкл" : "Вкл") });
                    Btns(d, Bt("⟳", "ref"));
                    return d;
                }
                case "windows":
                {
                    var wins = PcControl.ListWindows();
                    d = Card(kind, "🪟 Открытые окна", T("всего: ") + wins.Count);
                    foreach (var w in wins.Take(8))
                        CRows(d).Add(new object[] { "l", (w.Active ? "▶ " : w.Minimized ? "▫ " : "") + Cut(w.Title, 24) + " · " + Cut(w.Process, 12), "f|" + w.Handle.ToInt64(), "↗" });
                    Btns(d, Bt("🗕 Свернуть все", "min"), Bt("⟳", "ref"));
                    return d;
                }
                case "upgrades":
                {
                    var r = WC<List<WingetUpgrade>>("upg");
                    if (r == null && AutoStart(kind)) Bg(kind, CheckUpgrades, true);
                    d = Card(kind, "⬆️ Обновления программ", r == null ? null : T("можно обновить: ") + r.Count + " · " + WCAt("upg").ToString("HH:mm"));
                    if (Busy(kind)) CRows(d).Add(new object[] { "t", T("⏳ Спрашиваю winget… до минуты") });
                    else if (WC<string>("upgErr") is { } err) CRows(d).Add(new object[] { "t", "⚠️ " + Cut(T(err), 120) });
                    else if (r?.Count == 0) CRows(d).Add(new object[] { "t", T("✓ Всё обновлено") });
                    foreach (var u in (r ?? new()).Take(7))
                        CRows(d).Add(new object[] { "l", $"{Cut(u.Name, 20)} · {Cut(u.Current, 9)} → {Cut(u.Available, 9)}", "u|" + Cut(u.Id, 60), "⬆" });
                    Btns(d, Bt("🔍 Проверить", "run"));
                    return d;
                }
                case "history":
                {
                    List<PlayedItem> list;
                    try { list = (Store.Data?.Played ?? new()).Take(8).ToList(); } catch { list = new(); }
                    d = Card(kind, "🕘 Недавно играло");
                    if (list.Count == 0) CRows(d).Add(new object[] { "t", T("История пуста") });
                    foreach (var p in list) CRows(d).Add(new object[] { "y", "📋", $"{p.Time:HH:mm} · {Cut(p.Line, 64)}" });
                    Btns(d, Bt("⟳", "ref"));
                    return d;
                }
                case "keys":
                    d = Card(kind, "⌨️ Клавиши на ПК", T("нажимаются в активном окне"));
                    for (int i = 0; i < WKeys.Length; i += 4)
                        Btns(d, WKeys.Skip(i).Take(4).Select((k, j) => Bt(k.Label, "k|" + (i + j), k.Style)).ToArray());
                    return d;
                case "slides":
                {
                    PcControl.Win fg = null;
                    try { fg = PcControl.ListWindows().FirstOrDefault(w => w.Active); } catch { }
                    d = Card(kind, "📽 Пульт презентации", fg == null ? null : "▶ " + Cut(fg.Title, 40));
                    Btns(d, Bt("◀ Назад", "left"), Bt("Вперёд ▶", "right", "p"));
                    Btns(d, Bt("▶ С начала", "f5"), Bt("▶ С текущего", "sf5"));
                    Btns(d, Bt("⬛ Чёрный экран", "b"), Bt("✖ Выйти", "esc"));
                    return d;
                }
                case "jobs":
                {
                    List<Job> js;
                    lock (Jobs) js = Jobs.OrderBy(j => j.At).ToList();
                    d = Card(kind, "⏰ Отложенные команды", js.Count == 0 ? null : T("ждут: ") + js.Count);
                    if (js.Count == 0) CRows(d).Add(new object[] { "t", T("Ничего не запланировано. Пример: /in 10m /lock · /at 18:30 /shutdown") });
                    else CRows(d).Add(new object[] { "d", (int)Math.Max(0, (js[0].At - DateTime.Now).TotalSeconds), 1, T("до ближайшей") });
                    foreach (var j in js.Take(7))
                        CRows(d).Add(new object[] { "l", $"{j.At:HH:mm} · /{j.Cmd} {Cut(j.Arg ?? "", 20)}", "x|" + j.Id, "✖" });
                    Btns(d, Bt("⟳", "ref"));
                    return d;
                }
                case "defender":
                {
                    var wd = WC<List<string[]>>("wd");
                    if (wd == null && AutoStart(kind)) Bg(kind, async () => { WCSet("wd", await Task.Run(DefenderRows)); return null; }, true);
                    d = Card(kind, "🛡 Защита ПК");
                    CRows(d).Add(new object[] { "k", T("Сканер N-Island"), (Store.Settings.AvRealtime ? "🟢 " : "⚪ ") + T(Store.Settings.AvRealtime ? "следит" : "выключен") });
                    CRows(d).Add(new object[] { "k", T("Сигнатур в базе"), Antivirus.DbCount.ToString("N0") });
                    int q = 0;
                    try { q = Antivirus.Quarantined().Count; } catch { }
                    CRows(d).Add(new object[] { "k", T("В карантине"), q.ToString() });
                    if (WC<string>("avscan") is { } sc) CRows(d).Add(new object[] { "k", T("Быстрая проверка"), sc });
                    if (Busy(kind)) CRows(d).Add(new object[] { "t", wait });
                    foreach (var x in wd ?? new()) CRows(d).Add(new object[] { "k", x[0], x[1] });
                    Btns(d, Bt("🔍 Быстрая проверка", "scan", "p"), Bt("⟳", "ref"));
                    return d;
                }
            }
            return null;
        }

        async Task<string> CheckUpgrades()
        {
            var (list, err) = await PcHealth.WingetUpgrades();
            WCSet("upgErr", err);
            WCSet("upg", list ?? new List<WingetUpgrade>());
            return null;
        }

        // ------------------------------------------------------------------
        //  Действия (флаг разрешения уже проверен в CardAction)
        // ------------------------------------------------------------------

        async Task<string> CardAction2(string kind, string head, string arg)
        {
            switch (kind, head)
            {
                case ("water", "undo"): await Ui(Water.Undo); return "↶";
                case ("water", _) when int.TryParse(head, out var ml) && ml is > 0 and <= 1000:
                    await Ui(() => Water.Drink(ml)); return $"💧 +{ml} {T("мл")}";
                case ("notif", "read"): await Ui(NotificationLog.MarkRead); return null;
                case ("notif", "clear"): await Ui(NotificationLog.Clear); return T("🧹 Очищено");
                case ("ai", "sum" or "tr" or "fix" or "exp"):
                {
                    string clip = "";
                    try { clip = ClipboardService.I.Items.FirstOrDefault()?.Text ?? ""; } catch { }
                    if (clip.Trim() == "") return T("Буфер обмена пуст");
                    bool cyr = Regex.Matches(clip, "[а-яёіїєґ]", RegexOptions.IgnoreCase).Count > clip.Length / 4;
                    string lang = I18n.Current switch { "uk" => "украинский", "en" => "английский", _ => "русский" };
                    string task = head switch
                    {
                        "sum" => "Перескажи текст кратко, 2–4 предложения, на языке текста.",
                        "tr" => "Переведи текст на " + (cyr ? "английский" : lang) + " язык. Только перевод.",
                        "fix" => "Исправь ошибки и опечатки в тексте, сохрани язык и смысл. Только исправленный текст.",
                        _ => "Объясни простыми словами, о чём этот текст, на языке: " + lang + ". Коротко.",
                    };
                    Bg("ai", async () =>
                    {
                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
                        var ans = await Ai.Ask(new[] { new AiMessage("user", task + "\n\nТекст:\n" + Cut(clip, 6000)) }, null, cts.Token, channel: "telegram-widget");
                        WCSet("ai", Ai.StripThink(ans ?? "").Trim());
                        return "✨ " + (Ai.LastAnswered ?? "");
                    });
                    return null;
                }
                case ("ai", "put"):
                    if (WC<string>("ai") is { } res) { await Ui(() => System.Windows.Clipboard.SetText(res)); return T("📋 Ответ в буфере обмена ПК"); }
                    return null;
                case ("vpn", "v"):
                {
                    var item = Vpn.Items.FirstOrDefault(x => x.Key == arg || Cut(x.Key, 50) == arg);
                    if (item == null) return T("Нет такого подключения");
                    bool on = item.State != VpnState.On;
                    Bg("vpn", async () => T(await await Ui(() => Vpn.Set(item, on))));
                    return null;
                }
                case ("vpn", "ip"): Bg("vpn", async () => { await await Ui(Vpn.LoadIp); return null; }); return null;
                case ("vpn", "ref"): await Ui(() => _ = Vpn.RefreshAll()); return null;
                case ("wifiscan", "scan"): Bg("wifiscan", async () => { WCSet("aps", await WifiTools.Nearby(true)); return null; }); return null;
                case ("speed", "run"): Bg("speed", async () => { WCSet("speed", await ApiTools.SpeedTest()); return null; }); return null;
                case ("rates", "ref"): Bg("rates", async () => { WCSet("rates", ParseRates(await ApiTools2.Rates())); return null; }); return null;
                case ("sky", "ref"): if (WeatherService.Cached == null) await WeatherService.Get(); return null;
                case ("health", "run"): Bg("health", HealthCheck); return null;
                case ("startup", "s"):
                {
                    var it = Maint.Startup().FirstOrDefault(x => x.CanToggle && (x.Name == arg || Cut(x.Name, 60) == arg));
                    if (it == null) return T("Нет такой записи");
                    await Task.Run(() => Maint.SetStartup(it, !it.Enabled));
                    return (it.Enabled ? "⚪ " : "🟢 ") + it.Name;
                }
                case ("windows", "f"):
                {
                    var w = long.TryParse(arg, out var h) ? PcControl.ListWindows().FirstOrDefault(x => x.Handle.ToInt64() == h) : null;
                    if (w == null) return T("Окно уже закрыто");
                    await Task.Run(() => PcControl.Focus(w.Handle));
                    return "↗ " + Cut(w.Title, 40);
                }
                case ("windows", "min"): PcControl.Hotkey("win+d"); await Task.Delay(400); return null;
                case ("upgrades", "run"): Bg("upgrades", CheckUpgrades); return null;
                case ("upgrades", "u"):
                {
                    if (!Regex.IsMatch(arg, @"^[\w.+-]{1,100}$")) return null;
                    Bg("upgrades", async () =>
                    {
                        var o = await Task.Run(() => Winget($"upgrade --id {arg} -e --silent --accept-package-agreements --accept-source-agreements --disable-interactivity", 600000));
                        WCSet("upg", (WC<List<WingetUpgrade>>("upg") ?? new()).Where(x => x.Id != arg).ToList());
                        var last = o.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? "";
                        return "⬆ " + arg + ": " + Cut(last, 80);
                    });
                    return T("⬆ Обновляю ") + arg + "…";
                }
                case ("keys", "k"):
                    if (int.TryParse(arg, out var ki) && ki >= 0 && ki < WKeys.Length) return PcControl.Hotkey(WKeys[ki].Combo) ?? "⌨ " + WKeys[ki].Label;
                    return null;
                case ("slides", _) when SlideKeys.TryGetValue(head, out var combo): return PcControl.Hotkey(combo);
                case ("jobs", "x"):
                {
                    Job j;
                    lock (Jobs) { j = Jobs.FirstOrDefault(x => x.Id == arg); if (j != null) Jobs.Remove(j); }
                    j?.Cts.Cancel();
                    return T(j == null ? "Уже выполнено" : "✖ Отменено");
                }
                case ("defender", "ref"): Bg("defender", async () => { WCSet("wd", await Task.Run(DefenderRows)); return null; }); return null;
                case ("defender", "scan"):
                    Bg("defender", async () =>
                    {
                        var th = await Antivirus.QuickScan((_, _, _) => { }, CancellationToken.None);
                        WCSet("avscan", $"{DateTime.Now:HH:mm} · " + (th.Count == 0 ? "🟢 " + T("чисто") : "🔴 " + T("найдено: ") + th.Count));
                        return null;
                    });
                    return T("🔍 Проверка запущена");
            }
            return null;
        }
    }
}
