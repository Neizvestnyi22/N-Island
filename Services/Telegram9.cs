using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NIsland.Core;

namespace NIsland.Services
{
    // =====================================================================
    //  Виджеты Telegram, третья партия (13): тачпад, ввод текста на ПК, запуск программ, вывод звука, снимок экрана,
    //  точка доступа, пинг, устройства в сети, криптовалюты, вопрос ИИ, крестики-нолики, 2048, обратный отсчёт.
    //  Плюс общий список действий каждого вида (WOps): неизвестное нажатие не выполняется, а самотест сверяет,
    //  что каждая кнопка и каждое поле ввода во всех виджетах ведут к настоящему действию.
    //  И настройка «Отвечать виджетами»: команды без аргументов, у которых есть виджет, приходят виджетом.
    // =====================================================================
    public partial class TelegramHub
    {
        /// <summary>Какие действия умеет каждый вид ("#" — число, "ref" есть у всех). Пустой список — только просмотр.</summary>
        internal static readonly Dictionary<string, string[]> WOps = new()
        {
            ["player"] = new[] { "prev", "next", "play", "like", "back", "fwd", "shuffle", "repeat", "ab", "vu", "vd", "mute", "sleep" },
            ["lyrics"] = new[] { "prev", "next", "play", "back", "fwd", "like" },
            ["mixer"] = new[] { "vu", "vd", "mute", "m", "v" },
            ["eq"] = new[] { "p", "toggle" },
            ["timer"] = new[] { "toggle", "reset", "in", "add", "#" },
            ["stopwatch"] = new[] { "toggle", "lap", "reset" },
            ["pomodoro"] = new[] { "toggle", "skip", "reset" },
            ["weather"] = Array.Empty<string>(), ["sysmon"] = Array.Empty<string>(), ["clock"] = Array.Empty<string>(),
            ["battery"] = new[] { "pl" },
            ["network"] = new[] { "reconnect" },
            ["disks"] = Array.Empty<string>(),
            ["apps"] = new[] { "k" },
            ["todo"] = new[] { "t", "clean", "in" },
            ["notes"] = new[] { "in" },
            ["clipboard"] = new[] { "in" },
            ["modes"] = new[] { "m" },
            ["scenes"] = new[] { "s" },
            ["display"] = new[] { "bu", "bd", "theme", "off", "b", "pr" },
            ["power"] = new[] { "lock", "sleep", "in15", "in30", "in60", "cancel", "restart", "shutdown" },
            ["calc"] = Array.Empty<string>(), ["dice"] = Array.Empty<string>(), ["calendar"] = Array.Empty<string>(), ["breath"] = Array.Empty<string>(),
            ["water"] = new[] { "undo", "#" },
            ["notif"] = new[] { "read", "clear" },
            ["agents"] = Array.Empty<string>(),
            ["ai"] = new[] { "sum", "tr", "fix", "exp", "put" },
            ["vpn"] = new[] { "v", "ip" },
            ["wifiscan"] = new[] { "scan" },
            ["speed"] = new[] { "run" },
            ["rates"] = Array.Empty<string>(), ["sky"] = Array.Empty<string>(),
            ["health"] = new[] { "run" },
            ["startup"] = new[] { "s" },
            ["windows"] = new[] { "f", "min", "alttab" },
            ["upgrades"] = new[] { "run", "u" },
            ["history"] = Array.Empty<string>(),
            ["keys"] = new[] { "k" },
            ["slides"] = new[] { "left", "right", "f5", "sf5", "b", "w", "esc", "home", "end", "space" },
            ["jobs"] = new[] { "x", "q" },
            ["defender"] = new[] { "scan", "wd", "rt", "db", "ql" },
            // третья партия
            ["mouse"] = new[] { "mv", "c", "w", "step" },
            ["typer"] = new[] { "in", "k" },
            ["launcher"] = new[] { "o", "in" },
            ["audioout"] = new[] { "o", "v", "mute" },
            ["screenshot"] = new[] { "full", "win", "clip" },
            ["hotspot"] = new[] { "on", "off" },
            ["ping"] = Array.Empty<string>(),
            ["lan"] = new[] { "scan" },
            ["crypto"] = Array.Empty<string>(),
            ["askai"] = new[] { "in", "clear", "put" },
            ["ttt"] = Array.Empty<string>(), ["g2048"] = Array.Empty<string>(), ["countdown"] = Array.Empty<string>(), ["viz"] = Array.Empty<string>(),
            // 2.4
            ["screentime"] = new[] { "day" }, ["week"] = Array.Empty<string>(), ["sessions"] = new[] { "lock" }, ["batlog"] = Array.Empty<string>(),
            ["traffic"] = Array.Empty<string>(), ["wifilog"] = new[] { "reconnect" }, ["usblog"] = Array.Empty<string>(), ["iplog"] = new[] { "alert", "check" },
            ["downloads"] = new[] { "send", "notify" }, ["notifstats"] = Array.Empty<string>(), ["speedlog"] = new[] { "run" }, ["temps"] = Array.Empty<string>(),
            ["ramtop"] = new[] { "free" }, ["timeline"] = Array.Empty<string>(), ["report"] = new[] { "time", "send" },
            ["mic"] = new[] { "v", "mute" }, ["wall"] = new[] { "prev", "next", "rnd" }, ["limits"] = new[] { "del", "in", "act" }, ["focus"] = new[] { "go", "stop" },
            ["procwatch"] = new[] { "del", "in" }, ["alarms"] = new[] { "del", "in" }, ["fixes"] = new[] { "explorer", "dns", "wifi", "ram" },
            ["awake"] = new[] { "on", "off", "h" }, ["guard"] = new[] { "cpu", "ram" },
            ["radios"] = new[] { "t" }, ["privacy"] = Array.Empty<string>(), ["prayer"] = Array.Empty<string>(), ["agenda"] = Array.Empty<string>(),
            ["shelf"] = new[] { "send" }, ["translate"] = new[] { "in", "to" }, ["cleanup"] = new[] { "scan", "clean" }, ["news"] = Array.Empty<string>(),
            ["browser"] = new[] { "back", "fwd", "reload", "newtab", "close", "reopen", "next", "prev", "up", "down", "top", "bottom", "zin", "zout", "z0", "full",
                                  "play", "vmute", "in", "inc", "fav", "o", "w", "view", "tab", "shot", "link", "sum", "find", "type", "clip",
                                  "rw", "ff", "faster", "slower", "fc", "quit" },
        };

        internal static bool TestKnownOp(string kind, string head) => KnownOp(kind, head);
        internal static bool TestHasOps(string kind) => WOps.ContainsKey(kind);
        internal static IEnumerable<string> TestDefaultCmds => WDefaults.Keys;

        static bool KnownOp(string kind, string head) =>
            head is "ref" or "" or "open" || (WOps.TryGetValue(kind, out var ops) && (ops.Contains(head) || (ops.Contains("#") && int.TryParse(head, out _))));

        /// <summary>Команда без аргументов → вид виджета (настройка «Отвечать виджетами»).</summary>
        static readonly Dictionary<string, string> WDefaults = new()
        {
            ["music"] = "player", ["lyrics"] = "lyrics", ["appvol"] = "mixer", ["eq"] = "eq", ["timer"] = "timer", ["sw"] = "stopwatch", ["pomo"] = "pomodoro",
            ["clock"] = "clock", ["weather"] = "weather", ["top"] = "apps", ["cpu"] = "apps", ["todos"] = "todo", ["notes"] = "notes", ["clip"] = "clipboard",
            ["screen"] = "display", ["power"] = "power", ["roll"] = "dice", ["water"] = "water", ["notif"] = "notif", ["agents"] = "agents", ["vpn"] = "vpn",
            ["wifiscan"] = "wifiscan", ["moon"] = "sky", ["sun"] = "sky", ["autostart"] = "startup", ["windows"] = "windows", ["upgrades"] = "upgrades",
            ["keys"] = "keys", ["jobs"] = "jobs", ["mascot"] = "pet", ["lan"] = "lan", ["type"] = "typer", ["audiodev"] = "audioout", ["outdev"] = "audioout",
            ["installed"] = "launcher", ["sysinfo"] = "sysmon", ["status"] = "remote", ["conv"] = "rates", ["days"] = "countdown",
            // 2.3.1: все остальные команды, у которых есть виджет
            ["now"] = "player", ["vol"] = "mixer", ["note"] = "notes", ["todo"] = "todo", ["cliphist"] = "clipboard", ["scene"] = "scenes",
            ["history"] = "history", ["speed"] = "speed", ["health"] = "health", ["startup"] = "startup", ["launch"] = "launcher", ["calc"] = "calc",
            ["coin"] = "dice", ["mem"] = "sysmon", ["av"] = "defender", ["wdstatus"] = "defender", ["wifihealth"] = "network", ["wifiq"] = "network",
            ["channels"] = "wifiscan", ["router"] = "network", ["netusage"] = "network", ["in"] = "jobs", ["at"] = "jobs", ["live"] = "remote",
            ["bathealth"] = "battery",
            // 2.4
            ["screentime"] = "screentime", ["week"] = "week", ["sessions"] = "sessions", ["batlog"] = "batlog", ["traffic"] = "traffic", ["wifilog"] = "wifilog",
            ["usblog"] = "usblog", ["iplog"] = "iplog", ["downloads"] = "downloads", ["notifstats"] = "notifstats", ["speedlog"] = "speedlog", ["temps"] = "temps",
            ["ramtop"] = "ramtop", ["timeline"] = "timeline", ["report"] = "report", ["miclevel"] = "mic", ["limits"] = "limits", ["focusmode"] = "focus",
            ["watchproc"] = "procwatch", ["alarms"] = "alarms", ["alarm"] = "alarms", ["fixes"] = "fixes", ["awakefor"] = "awake", ["loadalert"] = "guard",
            ["prayer"] = "prayer", ["agenda"] = "agenda", ["shelf"] = "shelf", ["news"] = "news", ["privacy"] = "privacy", ["cleanscan"] = "cleanup",
            ["radio"] = "radios", ["browser"] = "browser", ["tabs"] = "browser", ["links"] = "browser", ["tr"] = "translate", ["translate"] = "translate", ["usb"] = "usblog", ["boot"] = "sessions", ["idle"] = "sessions",
        };

        /// <summary>Экран меню бота (кнопка nav:…) → вид виджета. Навигация (главное, «Ещё», настройки, ИИ, файлы) остаётся сообщениями.</summary>
        internal static readonly Dictionary<string, string> WMenus = new()
        {
            ["music"] = "player", ["sound"] = "mixer", ["outs"] = "audioout", ["eq"] = "eq", ["mixer"] = "mixer", ["screen"] = "display",
            ["power"] = "power", ["plans"] = "battery", ["net"] = "network", ["vpn"] = "vpn", ["timer"] = "timer", ["todo"] = "todo",
            ["modes"] = "modes", ["top"] = "apps", ["win"] = "windows", ["slides"] = "slides", ["mouse"] = "mouse", ["input"] = "keys",
            ["av"] = "defender", ["clips"] = "clipboard", ["history"] = "history", ["jobs"] = "jobs", ["scenes"] = "scenes", ["browser"] = "browser",
        };

        /// <summary>Кнопка меню при «Отвечать виджетами»: вместо экрана с кнопками — виджет новым сообщением.</summary>
        async Task<bool> MenuAsWidget(string menu)
        {
            if (Remote != null || _noWidgets || V("widgets_default") != "1" || !WMenus.TryGetValue(menu, out var kind)) return false;
            return await SendW(kind);
        }

        internal static IEnumerable<string> TestMenus => WMenus.Keys;

        /// <summary>true — ответ ушёл виджетом (Command возвращает null), false — отвечаем как обычно.</summary>
        async Task<bool> AnswerAsWidget(string cmd, string arg)
        {
            if (arg != "" || Remote != null || _noWidgets || V("widgets_default") != "1" || !WDefaults.TryGetValue(cmd, out var kind)) return false;
            return await SendW(kind);
        }

        /// <summary>Ответ на «✍️» из виджета: выполнить и обновить сам виджет.</summary>
        async Task<(string Html, object Keys)?> WidgetInput(string kind, string text)
        {
            text = text.Trim();
            if (text == "") return null;
            string need = kind switch { "typer" => "input", "launcher" => "apps", _ => "control" };
            if (Need(need) is { } no) return (no, null);
            string res;
            switch (kind)
            {
                case "typer":
                {
                    var t = text.Length > 1000 ? text[..1000] : text;
                    await Task.Run(() => PcControl.Type(t));
                    res = "⌨️ Напечатано на ПК: " + t.Length + " симв.";
                    break;
                }
                case "launcher":
                    return (await LaunchApp(text), null);
                case "askai":
                {
                    var q = text.Length > 1000 ? text[..1000] : text;
                    WCSet("askq", q); WCSet("aska", null);
                    Bg("askai", async () =>
                    {
                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
                        var ans = await Ai.Ask(new[] { new AiMessage("user", q + "\n\n(Ответь коротко, до 5 предложений, без таблиц.)") }, null, cts.Token, channel: "telegram-widget");
                        WCSet("aska", Ai.StripThink(ans ?? "").Trim());
                        return "🧠 " + (Ai.LastAnswered ?? "");
                    });
                    return ("🧠 Спросил ИИ — ответ появится в виджете", null);
                }
                case "todo":
                    await Ui(() => { Store.Data.Todos.Insert(0, new TodoItem { Text = Cut(text, 300) }); Store.SaveData(); });
                    res = "✅ Добавлено";
                    break;
                case "notes":
                    await Ui(() => { var n = Store.Data.Notes ?? ""; Store.Data.Notes = (n.TrimEnd() + (n.Trim() == "" ? "" : "\n") + text).Trim(); Store.SaveData(); });
                    res = "📝 Дописано в заметки";
                    break;
                case "clipboard":
                    await Ui(() => ClipboardService.I.CopyBack(text));
                    res = "📋 В буфере обмена ПК";
                    break;
                case "timer":
                {
                    if (!int.TryParse(text.Replace("мин", "").Trim(), out var min) || min < 1 || min > 600) return ("Напиши число минут от 1 до 600", null);
                    await Ui(() => { var tm = TimerService.I; tm.Reset(); tm.SetMinutes(min); tm.Start(); });
                    res = $"⏲ Таймер на {min} мин запущен";
                    break;
                }
                default: return await WidgetInput4(kind, text);
            }
            _ = PushKind(kind);
            return (res, null);
        }

        // ------------------------------------------------------------------
        //  Свои HTML: крестики-нолики, 2048, обратный отсчёт (всё в виджете, без бота)
        // ------------------------------------------------------------------

        const string TttHtml = """
<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><style>
html,body{margin:0}body{font:14px system-ui,sans-serif;color:var(--tg-theme-text-color,#eee);padding:10px;text-align:center}
#g{display:grid;grid-template-columns:repeat(3,72px);gap:6px;justify-content:center;margin:8px 0}#g button{height:72px;border:0;border-radius:14px;background:#8882;color:inherit;font-size:38px;font-weight:700}
#s{min-height:18px;color:var(--tg-theme-hint-color,#999)}.r{display:flex;gap:6px;justify-content:center}.r button{border:0;border-radius:10px;padding:7px 12px;background:#8882;color:inherit}
.w{background:var(--tg-theme-button-color,#3390ec)!important;color:#fff}
</style></head><body><div id="s"></div><div id="g"></div><div class="r"><button id="n">↺</button><button id="m"></button></div><script>
var B,o,two=0,sc=[0,0],L=[[0,1,2],[3,4,5],[6,7,8],[0,3,6],[1,4,7],[2,5,8],[0,4,8],[2,4,6]],ru=/^(ru|uk|be)/.test(navigator.language||'');
function $(i){return document.getElementById(i)}
function win(b){for(var i=0;i<8;i++){var l=L[i];if(b[l[0]]&&b[l[0]]==b[l[1]]&&b[l[1]]==b[l[2]])return l}return null}
function bot(){var e=B.map(function(v,i){return v?-1:i}).filter(function(i){return i>=0}),t;
for(var p of['O','X'])for(var i of e){B[i]=p;t=win(B);B[i]='';if(t)return i}if(!B[4])return 4;
var c=e.filter(function(i){return i%2==0&&i!=4});return(c.length?c:e)[Math.random()*(c.length?c.length:e.length)|0]}
function end(){var w=win(B);if(w){sc[B[w[0]]=='X'?0:1]++;w.forEach(function(i){$('g').children[i].className='w'});o=0;$('s').textContent=(B[w[0]]=='X'?'❌':'⭕')+(ru?' победил':' wins')+' · '+sc[0]+':'+sc[1];return 1}
if(B.every(function(v){return v})){o=0;$('s').textContent=(ru?'Ничья':'Draw')+' · '+sc[0]+':'+sc[1];return 1}return 0}
function put(i,p){B[i]=p;$('g').children[i].textContent=p=='X'?'✕':'○'}
function turn(){$('s').textContent=(o=='X'?'❌':'⭕')+(ru?' ходит':' to move')}
function nw(){B=['','','','','','','','',''];o='X';$('g').innerHTML='';for(var i=0;i<9;i++){var b=document.createElement('button');b.onclick=(function(k){return function(){
if(!o||B[k])return;put(k,o);if(end())return;if(two){o=o=='X'?'O':'X';turn();return}o='O';var j=bot();put(j,'O');if(!end()){o='X';turn()}}})(i);$('g').appendChild(b)}turn()}
$('n').onclick=nw;$('m').onclick=function(){two=!two;sc=[0,0];$('m').textContent=two?'👥':'🤖';nw()};$('m').textContent='🤖';nw();
</script></body></html>
""";

        const string G2048Html = """
<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><style>
html,body{margin:0}body{font:14px system-ui,sans-serif;color:var(--tg-theme-text-color,#eee);padding:8px;text-align:center;touch-action:none}
#h{display:flex;justify-content:space-between;font-weight:600;margin:0 4px 6px}#g{display:grid;grid-template-columns:repeat(4,58px);gap:5px;justify-content:center}
#g div{height:58px;border-radius:9px;background:#8882;display:flex;align-items:center;justify-content:center;font-weight:700;font-size:20px}
.r{display:grid;grid-template-columns:repeat(4,1fr);gap:5px;margin:7px 4px 0}.r button{border:0;border-radius:10px;padding:8px 0;background:#8882;color:inherit;font-size:16px}
</style></head><body><div id="h"><span id="s">0</span><span id="b"></span><span id="n" style="cursor:pointer">↺</span></div><div id="g"></div>
<div class="r"><button data-d="0">←</button><button data-d="1">↑</button><button data-d="2">↓</button><button data-d="3">→</button></div><script>
var G,S,best=0,C=['','#eee4da','#ede0c8','#f2b179','#f59563','#f67c5f','#f65e3b','#edcf72','#edcc61','#edc850','#edc53f','#edc22e'];
function $(i){return document.getElementById(i)}
function add(){var e=[];G.forEach(function(v,i){if(!v)e.push(i)});if(e.length)G[e[Math.random()*e.length|0]]=Math.random()<.9?2:4}
function draw(){$('g').innerHTML=G.map(function(v){var k=v?Math.min(11,Math.log2(v)):0;return'<div style="background:'+(k?C[k]:'')+';color:'+(k>2?'#fff':'#776e65')+';font-size:'+(v>999?15:20)+'px">'+(v||'')+'</div>'}).join('');
$('s').textContent='★ '+S;best=Math.max(best,S);$('b').textContent='🏆 '+best}
function line(a){var r=a.filter(function(v){return v});for(var i=0;i<r.length-1;i++)if(r[i]==r[i+1]){r[i]*=2;S+=r[i];r.splice(i+1,1)}while(r.length<4)r.push(0);return r}
function mv(d){var ch=0;for(var k=0;k<4;k++){var idx=[0,1,2,3].map(function(j){return d==0?k*4+j:d==3?k*4+3-j:d==1?j*4+k:(3-j)*4+k}),a=idx.map(function(i){return G[i]}),r=line(a);
idx.forEach(function(i,j){if(G[i]!=r[j])ch=1;G[i]=r[j]})}if(ch){add();draw()}if(!G.some(function(v,i){return!v||(i%4<3&&v==G[i+1])||(i<12&&v==G[i+4])}))$('s').textContent='★ '+S+' · 🏁'}
function nw(){G=Array(16).fill(0);S=0;add();add();draw()}
document.querySelectorAll('button').forEach(function(b){b.onclick=function(){mv(+b.dataset.d)}});$('n').onclick=nw;
var sx,sy;document.addEventListener('touchstart',function(e){sx=e.touches[0].clientX;sy=e.touches[0].clientY});
document.addEventListener('touchend',function(e){var dx=e.changedTouches[0].clientX-sx,dy=e.changedTouches[0].clientY-sy;if(Math.max(Math.abs(dx),Math.abs(dy))<25)return;
mv(Math.abs(dx)>Math.abs(dy)?(dx<0?0:3):(dy<0?1:2))});nw();
</script></body></html>
""";

        const string CountdownHtml = """
<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><style>
html,body{margin:0}body{font:13px system-ui,sans-serif;color:var(--tg-theme-text-color,#eee);padding:10px 12px;text-align:center}
#t{font-weight:600;font-size:15px}#d{font-size:44px;font-weight:700;font-variant-numeric:tabular-nums;line-height:1.1;margin:4px 0}#u{font-variant-numeric:tabular-nums;color:var(--tg-theme-hint-color,#999)}
.r{display:flex;gap:5px;margin-top:8px}button{flex:1;min-width:0;border:0;border-radius:10px;padding:7px 0;background:#8882;color:inherit}
</style></head><body><div id="t"></div><div id="d"></div><div id="u"></div>
<div class="r"><button data-a="-7">−7</button><button data-a="-1">−1</button><button data-a="1">+1</button><button data-a="7">+7</button><button data-a="30">+30</button></div>
<div class="r"><button data-k="ny">🎄</button><button data-k="su">☀️</button><button data-k="w">📅</button><button data-k="m">🌙</button></div><script>
var T,ru=/^(ru|uk|be)/.test(navigator.language||''),N;function $(i){return document.getElementById(i)}
function next(m,d){var n=new Date(),t=new Date(n.getFullYear(),m,d);if(t<=n)t.setFullYear(t.getFullYear()+1);return t}
function set(t,name){T=t;N=name;try{localStorage.setItem('cd',JSON.stringify([+t,name]))}catch(e){}tick()}
function tick(){var s=Math.max(0,(T-Date.now())/1e3|0),d=s/86400|0;$('t').textContent=N;$('d').textContent=d+(ru?' дн.':' d');
$('u').textContent=('0'+(s/3600%24|0)).slice(-2)+':'+('0'+(s/60%60|0)).slice(-2)+':'+('0'+s%60).slice(-2)+' · '+T.toLocaleDateString(undefined,{day:'numeric',month:'long',year:'numeric'})}
document.querySelectorAll('[data-a]').forEach(function(b){b.onclick=function(){var t=new Date(+T);t.setDate(t.getDate()+(+b.dataset.a));if(t>Date.now())set(t,'📅 '+t.toLocaleDateString(undefined,{day:'numeric',month:'long'}))}});
document.querySelectorAll('[data-k]').forEach(function(b){b.onclick=function(){var k=b.dataset.k;
if(k=='ny')set(next(0,1),ru?'🎄 До Нового года':'🎄 Until New Year');else if(k=='su')set(next(5,1),ru?'☀️ До лета':'☀️ Until summer');
else if(k=='w'){var n=new Date(),t=new Date(n.getFullYear(),n.getMonth(),n.getDate()+((6-n.getDay()+7)%7||7));set(t,ru?'📅 До выходных':'📅 Until the weekend')}
else{var n2=new Date(),t2=new Date(n2.getFullYear(),n2.getMonth(),n2.getDate()+1);set(t2,ru?'🌙 До полуночи':'🌙 Until midnight')}}});
try{var c=JSON.parse(localStorage.getItem('cd'));if(c&&c[0]>Date.now())set(new Date(c[0]),c[1])}catch(e){}if(!T)set(next(0,1),ru?'🎄 До Нового года':'🎄 Until New Year');setInterval(tick,1000);
</script></body></html>
""";

        // ------------------------------------------------------------------
        //  Состояния
        // ------------------------------------------------------------------

        static int _wMouseStep = 60;
        static readonly (string Label, string Combo)[] TyperKeys =
        {
            ("Enter ⏎", "enter"), ("⌫", "backspace"), ("Tab", "tab"), ("Esc", "esc"),
            ("Ctrl+A", "ctrl+a"), ("Ctrl+C", "ctrl+c"), ("Ctrl+V", "ctrl+v"), ("Ctrl+Z", "ctrl+z"),
        };

        static readonly Dictionary<string, (int X, int Y)> MouseDirs = new()
        {
            ["ul"] = (-1, -1), ["u"] = (0, -1), ["ur"] = (1, -1), ["l"] = (-1, 0), ["r"] = (1, 0), ["dl"] = (-1, 1), ["d"] = (0, 1), ["dr"] = (1, 1),
        };

        static readonly Dictionary<string, (List<double> Ms, int Lost)> _pingHist = new();

        static (string Name, string Host)[] PingHosts()
        {
            string gw = null;
            try { gw = WifiTools.Gateway()?.ToString(); } catch { }
            return new[] { (gw != null ? "Роутер" : null, gw), ("Cloudflare", "1.1.1.1"), ("Google", "8.8.8.8") }.Where(h => h.Item1 != null).ToArray();
        }

        Dictionary<string, object> CardState3(string kind)
        {
            Dictionary<string, object> d;
            switch (kind)
            {
                case "mouse":
                    d = Card(kind, "🖱 Тачпад", T("шаг ") + _wMouseStep + " px");
                    Btns(d, Bt("↖", "mv|ul"), Bt("↑", "mv|u"), Bt("↗", "mv|ur"));
                    Btns(d, Bt("←", "mv|l"), Bt("● " + T("клик"), "c|l", "p"), Bt("→", "mv|r"));
                    Btns(d, Bt("↙", "mv|dl"), Bt("↓", "mv|d"), Bt("↘", "mv|dr"));
                    Btns(d, Bt("ПКМ", "c|r"), Bt("2×", "c|d"), Bt("⇞", "w|3"), Bt("⇟", "w|-3"));
                    Btns(d, Bt(T("Шаг: ") + (_wMouseStep == 20 ? "20" : _wMouseStep == 60 ? "60" : "200") + " → " + (_wMouseStep == 20 ? "60" : _wMouseStep == 60 ? "200" : "20"), "step"));
                    return d;
                case "typer":
                {
                    PcControl.Win fg = null;
                    try { fg = PcControl.ListWindows().FirstOrDefault(w => w.Active); } catch { }
                    d = Card(kind, "⌨️ Набрать на ПК", fg == null ? null : "▶ " + Cut(fg.Title, 40));
                    Btns(d, Bt("✍️ Написать текст", "in", "p"));
                    for (int i = 0; i < TyperKeys.Length; i += 4)
                        Btns(d, TyperKeys.Skip(i).Take(4).Select((k, j) => Bt(k.Label, "k|" + (i + j), k.Combo == "enter" ? "p" : null)).ToArray());
                    return d;
                }
                case "launcher":
                {
                    var list = (Store.Data?.Launch ?? new()).Take(8).ToList();
                    d = Card(kind, "🚀 Запуск программ", list.Count == 0 ? T("избранное — на странице «Запуск» острова") : null);
                    for (int i = 0; i < list.Count; i++) CRows(d).Add(new object[] { "l", Cut(list[i].Name ?? "", 30), "o|" + i, "▶" });
                    Btns(d, Bt("✍️ Другая программа…", "in"));
                    return d;
                }
                case "audioout":
                {
                    var a = AudioService.I;
                    d = Card(kind, "🔈 Куда идёт звук", a.Muted ? T("звук выключен") : Math.Round(a.Volume * 100) + "%");
                    List<AudioService.OutputDevice> outs;
                    try { outs = a.Outputs().Take(6).ToList(); } catch { outs = new(); }
                    for (int i = 0; i < outs.Count; i++)
                        CRows(d).Add(new object[] { "l", (outs[i].IsDefault ? "🔊 " : "▫ ") + Cut(outs[i].Name, 30), outs[i].IsDefault ? null : "o|" + i, T("Сюда") });
                    Btns(d, Bt("0", "v|0"), Bt("25", "v|25"), Bt("50", "v|50"), Bt("75", "v|75"), Bt("100", "v|100"));
                    Btns(d, Bt(a.Muted ? "🔈 Включить звук" : "🔇 Выключить звук", "mute"));
                    return d;
                }
                case "screenshot":
                    d = Card(kind, "📸 Снимок экрана", T("придёт фото в этот чат"));
                    Btns(d, Bt("🖥 Весь экран", "full", "p"), Bt("🪟 Активное окно", "win"));
                    Btns(d, Bt("🖼 Картинка из буфера ПК", "clip"));
                    return d;
                case "hotspot":
                {
                    var h = WifiTools2.HotspotState();
                    d = Card(kind, "📲 Точка доступа", h == null ? T("недоступна на этом ПК") : h.Value.On ? T("раздаёт интернет") : T("выключена"));
                    if (h is { } hs)
                    {
                        CRows(d).Add(new object[] { "k", T("Сеть"), Cut(hs.Name ?? "", 30) });
                        if (hs.On) CRows(d).Add(new object[] { "k", T("Подключено"), $"{hs.Clients} / {hs.Max}" });
                    }
                    if (Busy(kind)) CRows(d).Add(new object[] { "t", T("⏳ Выполняется…") });
                    Btns(d, h is { On: true } ? Bt("⏹ Выключить", "off", "d") : Bt("▶ Включить", "on", "s"), Bt("⟳", "ref"));
                    return d;
                }
                case "ping":
                {
                    d = Card(kind, "📡 Пинг", DateTime.Now.ToString("HH:mm:ss"));
                    var hosts = PingHosts();
                    var res = hosts.Select(h => Task.Run(() =>
                    {
                        try { using var p = new Ping(); var r = p.Send(h.Host, 1500); return r.Status == IPStatus.Success ? r.RoundtripTime : -1L; } catch { return -1L; }
                    })).ToArray();
                    try { Task.WaitAll(res, 3000); } catch { }
                    for (int i = 0; i < hosts.Length; i++)
                    {
                        long ms = res[i].IsCompleted ? res[i].Result : -1;
                        string key = hosts[i].Host;
                        lock (_pingHist)
                        {
                            if (!_pingHist.TryGetValue(key, out var hst)) hst = (new List<double>(), 0);
                            if (ms >= 0) hst.Ms.Add(ms); else hst.Lost++;
                            while (hst.Ms.Count > 20) hst.Ms.RemoveAt(0);
                            _pingHist[key] = hst;
                            double avg = hst.Ms.Count > 0 ? hst.Ms.Average() : 0;
                            int pct = ms < 0 ? 100 : (int)Math.Clamp(ms / 2.0, 3, 100);
                            CRows(d).Add(new object[] { "b", T(hosts[i].Name), pct, ms < 0 ? "#ff453a" : ms < 40 ? "#30d158" : ms < 120 ? "#ffd60a" : "#ff9f0a",
                                ms < 0 ? T("нет ответа") : $"{ms} {T("мс")} · ⌀{avg:0}" + (hst.Lost > 0 ? $" · ✖{hst.Lost}" : "") });
                        }
                    }
                    Btns(d, Bt("⟳", "ref"));
                    return d;
                }
                case "lan":
                {
                    var devs = WC<List<LanDevice>>("lan");
                    if (devs == null && AutoStart(kind)) Bg(kind, async () => { WCSet("lan", await WifiTools.Devices()); return null; }, true);
                    d = Card(kind, "🏠 Устройства в сети", devs == null ? null : T("найдено: ") + devs.Count + " · " + WCAt("lan").ToString("HH:mm"));
                    if (Busy(kind)) CRows(d).Add(new object[] { "t", T("⏳ Ищу устройства… до минуты") });
                    foreach (var dv in (devs ?? new()).Take(10))
                        CRows(d).Add(new object[] { "k", Cut(string.IsNullOrEmpty(dv.Name) ? dv.Kind ?? "?" : dv.Name, 24), dv.Ip + (dv.Ms > 0 ? $" · {dv.Ms:0} {T("мс")}" : "") });
                    Btns(d, Bt("🔍 Искать снова", "scan"));
                    return d;
                }
                case "crypto":
                {
                    var r = WC<List<string[]>>("crypto");
                    if ((r == null || (DateTime.Now - WCAt("crypto")).TotalMinutes > 5) && AutoStart(kind))
                        Bg(kind, async () =>
                        {
                            var s = await ApiTools.Crypto();
                            WCSet("crypto", s.Split("·", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                .Select(p => p.Split(' ', 2)).Where(p => p.Length == 2).Select(p => new[] { p[0], p[1] }).ToList());
                            return null;
                        }, true);
                    d = Card(kind, "🪙 Криптовалюты", r == null ? null : "USD · " + WCAt("crypto").ToString("HH:mm"));
                    if (r == null) CRows(d).Add(new object[] { "t", Busy(kind) ? T("⏳ Загружаю…") : T("Не загрузились — нажми ⟳") });
                    else foreach (var x in r.Take(8)) CRows(d).Add(new object[] { "k", x[0], x[1] });
                    Btns(d, Bt("⟳", "ref"));
                    return d;
                }
                case "askai":
                {
                    d = Card(kind, "🧠 Спросить ИИ", Ai.Current?.Name);
                    if (WC<string>("askq") is { } q) CRows(d).Add(new object[] { "t", "❓ " + Cut(q, 80) });
                    if (Busy(kind)) CRows(d).Add(new object[] { "t", T("⏳ ИИ думает…") });
                    else if (WC<string>("aska") is { } ans) CRows(d).Add(new object[] { "y", "📋", Cut(ans, 330) });
                    Btns(d, Bt("✍️ Задать вопрос", "in", "p"));
                    if (WC<string>("aska") != null) Btns(d, Bt("📋 В буфер ПК", "put"), Bt("🧹 Очистить", "clear"));
                    return d;
                }
            }
            return null;
        }

        // ------------------------------------------------------------------
        //  Действия
        // ------------------------------------------------------------------

        /// <summary>Полей ввода в виджетах Telegram нет — текст пишут в чат: кнопка «✍️» включает ожидание ответа (3 минуты).</summary>
        static readonly Dictionary<string, string> WInPrompts = new()
        {
            ["typer"] = "⌨️ Напиши текст — напечатаю его в активном окне на ПК (отмена: «-»)",
            ["launcher"] = "🚀 Напиши название программы (можно часть) — запущу на ПК",
            ["askai"] = "🧠 Напиши вопрос — ответ появится в виджете",
            ["todo"] = "✅ Напиши задачу — добавлю в список",
            ["notes"] = "📝 Напиши, что дописать в заметки",
            ["clipboard"] = "📋 Напиши текст — положу в буфер обмена ПК",
            ["timer"] = "⏲ Сколько минут поставить? Напиши число (1–600)",
            ["limits"] = "⏳ Напиши программу и минуты в день, например: chrome 120 (0 — снять лимит)",
            ["procwatch"] = "👁 Напиши имя процесса, например: steam",
            ["alarms"] = "⏰ Напиши время и подпись, например: 07:30 Подъём",
            ["translate"] = "🌐 Напиши текст — переведу (язык меняется кнопкой в виджете)",
            ["browser"] = "🌐 Пришли ссылку или что найти — открою в браузере на ПК (отмена: «-»)",
        };

        async Task<string> CardAction3(string kind, string head, string arg)
        {
            if (head == "in" && WInPrompts.TryGetValue(kind, out var prompt))
            {
                Await("w:" + kind);
                await Message(prompt);
                return T("✍️ Напиши в чат");
            }
            switch (kind, head)
            {
                case ("mouse", "mv") when MouseDirs.TryGetValue(arg, out var dir):
                    PcControl.MouseMove(dir.X * _wMouseStep, dir.Y * _wMouseStep); return null;
                case ("mouse", "c"): PcControl.Click(arg == "r", arg == "d"); return null;
                case ("mouse", "w") when int.TryParse(arg, out var wh) && Math.Abs(wh) <= 10: PcControl.Wheel(wh); return null;
                case ("mouse", "step"): _wMouseStep = _wMouseStep == 20 ? 60 : _wMouseStep == 60 ? 200 : 20; return null;
                case ("typer", "k") when int.TryParse(arg, out var ki) && ki >= 0 && ki < TyperKeys.Length:
                    return PcControl.Hotkey(TyperKeys[ki].Combo);
                case ("launcher", "o") when int.TryParse(arg, out var li):
                {
                    var it = (Store.Data?.Launch ?? new()).ElementAtOrDefault(li);
                    if (it == null || string.IsNullOrWhiteSpace(it.Target)) return T("Нет такого пункта");
                    await Ui(() => SystemService.Run(it.Target));
                    return "▶ " + it.Name;
                }
                case ("audioout", "o") when int.TryParse(arg, out var oi):
                {
                    var o = AudioService.I.Outputs().ElementAtOrDefault(oi);
                    if (o == null) return T("Нет такого устройства");
                    await Ui(() => AudioService.I.SetDefaultOutput(o.Id));
                    await Task.Delay(400);
                    return "🔊 " + Cut(o.Name, 40);
                }
                case ("audioout", "v") when int.TryParse(arg, out var v) && v is >= 0 and <= 100:
                    await Ui(() => { AudioService.I.SetVolume(v / 100.0); if (v > 0 && AudioService.I.Muted) AudioService.I.ToggleMute(); }); return null;
                case ("audioout", "mute"): await Ui(AudioService.I.ToggleMute); return null;
                case ("screenshot", "full" or "win"):
                    if (!Flag("screen")) return T("🔐 «Снимок экрана» выключен — включается на ПК");
                    await Screenshot(head == "win" ? -1 : 0, false);
                    return T("📸 Снимок отправлен");
                case ("screenshot", "clip"):
                {
                    var r = await Command4("clipimg", "");
                    if (r?.Html is { } h) await Message(h, r.Value.Keys);
                    return T("🖼 Готово");
                }
                case ("hotspot", "on" or "off"):
                    Bg("hotspot", async () => T(await WifiTools2.SetHotspot(head == "on")));
                    return null;
                case ("lan", "scan"): Bg("lan", async () => { WCSet("lan", await WifiTools.Devices()); return null; }); return null;
                case ("crypto", "ref"):
                    lock (_wTried) _wTried.Remove("crypto");
                    WCSet("crypto", WC<List<string[]>>("crypto"));
                    lock (_wCache) if (_wCache.TryGetValue("crypto", out var c)) _wCache["crypto"] = (DateTime.MinValue, c.Data);
                    return null;
                case ("askai", "clear"): WCSet("askq", null); WCSet("aska", null); return null;
                case ("askai", "put"):
                    if (WC<string>("aska") is { } a2) { await Ui(() => System.Windows.Clipboard.SetText(a2)); return T("📋 Ответ в буфере обмена ПК"); }
                    return null;
            }
            return null;
        }
    }
}
