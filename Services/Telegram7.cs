using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NIsland.Core;

namespace NIsland.Services
{
    // =====================================================================
    //  Каталог виджетов Telegram (Bot API 10.4): 59 штук.
    //  • 11 своих HTML: пульт, персонаж, нагрузка (график), мировые часы, калькулятор, кубики, календарь, дыхание,
    //    обратный отсчёт, крестики-нолики, 2048.
    //  • 37 карточек на одном универсальном HTML: что в карточке (полоски, строки, таймеры, списки, кнопки) —
    //    описывает состояние от бота (chat_data ≤ 1024 байт), поэтому любую функцию пульта легко сделать виджетом.
    //  Управление через бота: /widgets — каталог, живые виджеты, обновить всё, остановить обновления.
    // =====================================================================
    public partial class TelegramHub
    {
        /// <summary>Вид виджета. H = 0 — высота по содержимому (у карточек), прокрутки внутри виджета в Telegram нет.</summary>
        public record WKind(string Key, string Name, string Group, int W, int H, int LiveSec);

        static readonly WKind[] WKinds =
        {
            new("remote", "🎛 Пульт ПК", "Главное", 340, 300, 8), new("pet", "🫧 Персонаж", "Главное", 260, 262, 6),
            new("sysmon", "📈 Нагрузка (график)", "Система", 340, 236, 5), new("player", "🎵 Плеер", "Музыка", 340, 0, 8),
            new("lyrics", "🎤 Текст песни", "Музыка", 340, 0, 8), new("mixer", "🎚 Громкость программ", "Музыка", 340, 0, 10),
            new("eq", "🎛 Эквалайзер", "Музыка", 340, 0, 0), new("timer", "⏲ Таймер", "Время", 300, 0, 0),
            new("stopwatch", "⏱ Секундомер", "Время", 300, 0, 0), new("pomodoro", "🍅 Помодоро", "Время", 300, 0, 0),
            new("clock", "🕐 Мировые часы", "Время", 300, 240, 0), new("weather", "🌦 Погода", "Сводки", 340, 0, 0),
            new("battery", "🔋 Батарея", "Система", 320, 0, 30), new("network", "📶 Сеть", "Система", 340, 0, 10),
            new("disks", "💽 Диски", "Система", 320, 0, 0), new("apps", "📊 Кто грузит ПК", "Система", 340, 0, 15),
            new("todo", "✅ Задачи", "Дела", 340, 0, 0), new("notes", "📝 Заметки", "Дела", 340, 0, 0),
            new("clipboard", "📋 Буфер обмена", "Дела", 340, 0, 0), new("modes", "🌙 Режимы острова", "Управление", 320, 0, 0),
            new("scenes", "🎬 Сценарии", "Управление", 320, 0, 0), new("display", "🖥 Экран", "Управление", 320, 0, 0),
            new("power", "⏻ Питание", "Управление", 320, 0, 0), new("calc", "🧮 Калькулятор", "Игры и утилиты", 280, 336, 0),
            new("dice", "🎲 Кубики и монетка", "Игры и утилиты", 280, 230, 0),
            // вторая партия (Telegram8.cs)
            new("water", "💧 Вода", "Дела", 320, 0, 0), new("ai", "✨ ИИ над буфером", "Дела", 340, 0, 0),
            new("history", "🕘 Недавно играло", "Музыка", 340, 0, 0), new("jobs", "⏰ Отложенные команды", "Время", 320, 0, 15),
            new("calendar", "📅 Календарь", "Время", 300, 290, 0), new("notif", "🔔 Уведомления", "Сводки", 340, 0, 15),
            new("agents", "🤖 ИИ-агенты", "Сводки", 340, 0, 30), new("rates", "💱 Курсы валют", "Сводки", 320, 0, 0),
            new("sky", "🌗 Солнце и луна", "Сводки", 320, 0, 0), new("vpn", "🔒 VPN", "Сеть", 340, 0, 15),
            new("wifiscan", "📡 Wi-Fi вокруг", "Сеть", 340, 0, 0), new("speed", "🚀 Скорость интернета", "Сеть", 320, 0, 0),
            new("health", "🩺 Здоровье ПК", "Система", 340, 0, 0), new("defender", "🛡 Защита", "Система", 340, 0, 0),
            new("startup", "🚀 Автозагрузка", "Программы", 340, 0, 0), new("windows", "🪟 Окна", "Программы", 340, 0, 15),
            new("upgrades", "⬆️ Обновления программ", "Программы", 340, 0, 0), new("keys", "⌨️ Клавиши", "Управление", 340, 0, 0),
            new("slides", "📽 Презентация", "Управление", 320, 0, 0), new("breath", "🫁 Дыхание", "Игры и утилиты", 280, 250, 0),
            // третья партия (Telegram9.cs)
            new("mouse", "🖱 Тачпад", "Управление", 320, 0, 0), new("typer", "⌨️ Набрать на ПК", "Управление", 320, 0, 0),
            new("screenshot", "📸 Снимок экрана", "Управление", 320, 0, 0), new("launcher", "🚀 Запуск программ", "Программы", 320, 0, 0),
            new("audioout", "🔈 Куда идёт звук", "Музыка", 340, 0, 0), new("hotspot", "📲 Точка доступа", "Сеть", 320, 0, 0),
            new("ping", "📡 Пинг", "Сеть", 320, 0, 15), new("lan", "🏠 Устройства в сети", "Сеть", 340, 0, 0),
            new("crypto", "🪙 Криптовалюты", "Сводки", 320, 0, 0), new("askai", "🧠 Спросить ИИ", "Дела", 340, 0, 0),
            new("countdown", "⏳ Обратный отсчёт", "Время", 300, 230, 0), new("ttt", "❌ Крестики-нолики", "Игры и утилиты", 280, 330, 0),
            new("g2048", "🔢 2048", "Игры и утилиты", 280, 370, 0), new("viz", "🌈 Визуализатор", "Музыка", 340, 200, 4),
            // 2.4: отслеживание (Telegram14.cs)
            new("screentime", "⏱ Экранное время", "Отслеживание", 340, 0, 30), new("week", "📅 Неделя за ПК", "Отслеживание", 340, 0, 0),
            new("sessions", "🔐 Сессии", "Отслеживание", 320, 0, 0), new("batlog", "🔋 История заряда", "Отслеживание", 320, 0, 60),
            new("traffic", "🌐 Трафик", "Отслеживание", 320, 0, 30), new("wifilog", "📶 История Wi-Fi", "Отслеживание", 320, 0, 0),
            new("usblog", "🔌 USB-журнал", "Отслеживание", 340, 0, 0), new("iplog", "🌍 Внешний IP", "Отслеживание", 320, 0, 0),
            new("downloads", "📥 Загрузки", "Отслеживание", 340, 0, 30), new("notifstats", "🔔 Кто шлёт уведомления", "Отслеживание", 320, 0, 0),
            new("speedlog", "🚀 История скорости", "Отслеживание", 320, 0, 0), new("temps", "🌡 Температура", "Отслеживание", 320, 0, 15),
            new("ramtop", "🧠 Кто занял память", "Отслеживание", 340, 0, 15), new("timeline", "🗓 События дня", "Отслеживание", 340, 0, 30),
            new("report", "📊 Отчёт за день", "Отслеживание", 340, 0, 0),
            // 2.4: контроль
            new("mic", "🎙 Микрофон", "Контроль", 320, 0, 10), new("wall", "🖼 Обои", "Контроль", 320, 0, 0),
            new("limits", "⏳ Лимиты времени", "Контроль", 340, 0, 30), new("focus", "🎯 Фокус", "Контроль", 320, 0, 15),
            new("procwatch", "👁 Слежка за программами", "Контроль", 320, 0, 15), new("alarms", "⏰ Будильники", "Контроль", 320, 0, 0),
            new("fixes", "🛠 Быстрые починки", "Контроль", 320, 0, 0), new("awake", "☕ Не засыпать", "Контроль", 300, 0, 30),
            new("guard", "🚨 Сторож нагрузки", "Контроль", 320, 0, 10),
            // 2.4: для того, что уже было
            new("radios", "📻 Wi-Fi и Bluetooth", "Сеть", 320, 0, 0), new("privacy", "🕵 Кто слушает и смотрит", "Система", 340, 0, 15),
            new("prayer", "🕌 Время молитв", "Время", 300, 0, 0), new("agenda", "📅 Ближайшие события", "Время", 320, 0, 0),
            new("shelf", "📦 Полка", "Дела", 320, 0, 0), new("translate", "🌐 Переводчик", "Дела", 340, 0, 0),
            new("cleanup", "🧹 Очистка", "Система", 340, 0, 0), new("news", "📰 Новости", "Сводки", 340, 0, 0),
            new("browser", "🧭 Браузер", "Управление", 340, 0, 6),
            new("menu", "🧩 Все виджеты", "Главное", 340, 0, 0),
        };

        static WKind WK(string key) => WKinds.FirstOrDefault(k => k.Key == key);

        /// <summary>Сколько минут живые виджеты обновляются сами (настройка, 1–60).</summary>
        static int LiveMin => Math.Clamp(Store.Settings.WidgetLiveMin, 1, 60);

        internal static IEnumerable<WKind> TestKinds => WKinds;
        internal static string TestHtml(string kind) => HtmlOf(kind);
        internal string TestWState(string kind) => Task.Run(() => WState(kind)).GetAwaiter().GetResult();

        // ------------------------------------------------------------------
        //  Универсальная карточка
        //  Состояние: {"k":вид,"h":заголовок,"s":подзаголовок,"r":[строки],"b":[[кнопки ряда]],"e":примечание}
        //  Строки: ["b",имя,%,цвет,текст] полоска · ["t",текст] · ["T",крупно] · ["k",ключ,значение] · ["p",позиция,длина,играет]
        //  ["d",секунд,идёт,подпись] обратный отсчёт · ["u",секунд,идёт,подпись] секундомер · ["l",текст,запрос,кнопка] · ["y",кнопка,текст] скопировать
        //  Кнопки: [подпись, запрос, стиль d|s|p]
        // ------------------------------------------------------------------
        const string CardHtml = """
<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><style>
html,body{margin:0}body{font:13px/1.35 system-ui,sans-serif;color:var(--tg-theme-text-color,#eee);padding:10px 12px}
.h{font-weight:600;font-size:15px}.s,.t{color:var(--tg-theme-hint-color,#999);font-size:12px}.T{font-size:26px;font-weight:700;margin:2px 0;font-variant-numeric:tabular-nums}
.k{display:flex;justify-content:space-between;gap:8px;padding:2px 0}.k b{font-weight:600;text-align:right}
.g{display:grid;grid-template-columns:auto 1fr auto;gap:4px 8px;align-items:center;font-size:12px;margin:3px 0}
.bb{height:7px;border-radius:4px;background:#8883;overflow:hidden}.bb i{display:block;height:100%;border-radius:4px}
.l{display:flex;align-items:center;gap:6px;padding:2px 0}.l span{flex:1;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
.r{display:flex;gap:6px;margin-top:6px}button{flex:1;border:0;border-radius:10px;padding:7px 4px;font-size:13px;background:#8882;color:inherit}
.l button{flex:0 0 auto;padding:4px 9px}button.d{background:#ff453a40}button.s{background:#30d15840}button.p{background:var(--tg-theme-button-color,#3390ec);color:#fff}
.hd{display:flex;align-items:flex-start;gap:6px}.hd>div{flex:1;min-width:0}.hd .rf{flex:0 0 auto;padding:3px 10px;font-size:15px}#pm{width:46px;height:46px;flex:0 0 auto;margin:-6px 0 -4px}
svg{display:block;width:100%;height:30px}.Lc{font-size:17px;font-weight:700;margin:3px 0}button:active{filter:brightness(1.4)}button.w{opacity:.5}#e{min-height:14px;font-size:11px;color:var(--tg-theme-hint-color,#999);margin-top:4px}
</style></head><body><div id="c"></div><div id="e"></div><script>
var T=window.TelegramWidget,D={},t0=Date.now();function $(i){return document.getElementById(i)}
function f(s){s=Math.max(0,Math.round(s));var h=s/3600|0,m=(s/60|0)%60;return(h?h+':'+('0'+m).slice(-2):m)+':'+('0'+s%60).slice(-2)}
function x(s){return String(s==null?'':s).replace(/[&<>"]/g,function(c){return{'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;'}[c]})}
function el(){return(Date.now()-t0)/1e3}
function row(r,i){var t=r[0];
if(t=='b')return'<div class="g"><span>'+x(r[1])+'</span><div class="bb"><i style="width:'+Math.min(100,r[2])+'%;background:'+(r[3]||'var(--tg-theme-button-color,#3390ec)')+'"></i></div><span>'+x(r[4]!=null?r[4]:r[2]+'%')+'</span></div>';
if(t=='t')return'<div class="t">'+x(r[1])+'</div>';if(t=='T')return'<div class="T">'+x(r[1])+'</div>';
if(t=='k')return'<div class="k"><span>'+x(r[1])+'</span><b>'+x(r[2])+'</b></div>';
if(t=='p')return'<div class="g"><span id="pa'+i+'"></span><div class="bb"><i id="pb'+i+'"></i></div><span>'+f(r[2])+'</span></div>';
if(t=='d'||t=='u')return'<div class="T" id="cd'+i+'"></div>'+(r[3]?'<div class="t">'+x(r[3])+'</div>':'');
if(t=='l')return'<div class="l"><span>'+x(r[1])+'</span>'+(r[2]&&!D.ro?'<button data-q="'+x(r[2])+'">'+x(r[3]||'›')+'</button>':'')+'</div>';
if(t=='y')return'<div class="l"><span>'+x(r[2])+'</span><button data-y="'+i+'">'+x(r[1])+'</button></div>';
if(t=='g'){var a=r[2]||[],mx=Math.max.apply(0,a.concat([r[5]||1])),n=Math.max(1,a.length-1);return'<div class="k"><span>'+x(r[1])+'</span><b>'+x(r[4]||'')+'</b></div><svg viewBox="0 0 100 30" preserveAspectRatio="none"><polyline fill="none" stroke-width="2" vector-effect="non-scaling-stroke" stroke="'+(r[3]||'#3390ec')+'" points="'+a.map(function(v,j){return(j/n*100).toFixed(1)+','+(29-v/mx*27).toFixed(1)}).join(' ')+'"/></svg>'}if(t=='L')return'<div id="ly'+i+'"></div>';return''}
function tick(){(D.r||[]).forEach(function(r,i){var t=r[0],e;
if(t=='p'&&(e=$('pa'+i))){var p=Math.min(r[2],r[1]+(r[3]?el():0));e.textContent=f(p);var b=$('pb'+i);b.style.width=(r[2]?p/r[2]*100:0)+'%';b.style.background='var(--tg-theme-button-color,#3390ec)'}
if((t=='d'||t=='u')&&(e=$('cd'+i)))e.textContent=f(t=='d'?r[1]-(r[2]?el():0):r[1]+(r[2]?el():0));
if(t=='L'&&(e=$('ly'+i))){var s=r[1]?el():0,c=-1,a=r[2];a.forEach(function(l,j){if(l[0]<=s)c=j});if(e.dataset.c!=String(c)){e.dataset.c=c;
e.innerHTML=(c>0?'<div class="t">'+x(a[c-1][1])+'</div>':'')+'<div class="Lc">'+x(c>=0?a[c][1]:'♪')+'</div>'+(a[c+1]?'<div class="t">'+x(a[c+1][1])+'</div>':'')}}})}
function show(){var h='<div class="hd"><div><div class="h">'+x(D.h)+'</div>'+(D.s?'<div class="s">'+x(D.s)+'</div>':'')+'</div>'+(D.pm?'<canvas id="pm" width="92" height="92"></canvas>':'')+(D.ro?'':'<button class="rf" data-q="ref">⟳</button>')+'</div>';(D.r||[]).forEach(function(r,i){h+=row(r,i)});
(D.ro?[]:D.b||[]).forEach(function(w){w=w.filter(function(b){return!(b[1]=='ref'&&/^⟳/.test(b[0]))});if(!w.length)return;h+='<div class="r">';w.forEach(function(b){h+='<button class="'+(b[2]||'')+'" data-q="'+x(b[1])+'">'+x(b[0])+'</button>'});h+='</div>'});
$('c').innerHTML=h;note(D.e||'',D.e?6000:0);tick();
document.querySelectorAll('button[data-q]').forEach(function(b){b.onclick=function(){ask(b.getAttribute('data-q'),b)}});
document.querySelectorAll('button[data-y]').forEach(function(b){b.onclick=function(){if(T&&T.copyText)T.copyText(D.r[+b.getAttribute('data-y')][2]);b.textContent='✓'}})}
function load(s){try{D=JSON.parse(s||'{}');t0=Date.now();show()}catch(e){}}
var B=0,et,L=navigator.language||'',E=/^uk/.test(L)?{RATE_LIMITED:'Занадто часто — повторюю…',BUSY:'Бот зайнятий — повторюю…',TIMEOUT:'ПК не відповів — натисни ще раз',NOT_ALLOWED:'У цій копії кнопки не працюють',FAILED:'Не вийшло'}
:/^(ru|be|kk)/.test(L)?{RATE_LIMITED:'Слишком часто — повторяю…',BUSY:'Бот занят — повторяю…',TIMEOUT:'ПК не ответил — нажми ещё раз',NOT_ALLOWED:'В этой копии кнопки не работают',FAILED:'Не получилось'}
:{RATE_LIMITED:'Too fast — retrying…',BUSY:'Busy — retrying…',TIMEOUT:'The PC did not answer — tap again',NOT_ALLOWED:'Buttons do not work in this copy',FAILED:'Failed'};
function note(s,k){$('e').textContent=s;clearTimeout(et);if(k)et=setTimeout(function(){$('e').textContent=''},k)}
function ask(q,b,n){if(!T||B)return;B=1;if(b)b.classList.add('w');note('…');
T.request('c:'+D.k+':'+q).then(function(a){B=0;if(b)b.classList.remove('w');note('');if(a.result)load(a.result)},function(e){B=0;if(b)b.classList.remove('w');
if((e.code=='RATE_LIMITED'||e.code=='BUSY')&&!n){note(E[e.code]);setTimeout(function(){ask(q,b,1)},1600)}else note('⚠ '+(E[e.code]||e.code),6000)})}
function E(c,X,Y,a,b){c.beginPath();c.ellipse(X,Y,a,b,0,0,7);c.fill()}
function pm(){var c=$('pm');if(c){var g=c.getContext('2d'),s=Date.now()/1e3,u=Math.abs(Math.sin(s*3.7));g.clearRect(0,0,92,92);g.save();g.translate(46,82-u*11);g.rotate(Math.sin(s*1.85)*.2);g.scale(1+u*.07,1-u*.07);
g.fillStyle=D.pm;E(g,0,-26,25,24);g.fillStyle='#111';E(g,-9,-30,3.4,4.2);E(g,9,-30,3.4,4.2);g.strokeStyle='#111';g.lineWidth=2.6;g.beginPath();g.arc(0,-22,7,.25,2.9);g.stroke();
g.strokeStyle='#333';g.lineWidth=4.5;g.beginPath();g.arc(0,-28,28,3.5,5.93);g.stroke();g.fillStyle='#333';E(g,-26,-28,5,8);E(g,26,-28,5,8);g.restore();
g.fillStyle=D.pm;g.globalAlpha=.4+.6*u;g.font='15px sans-serif';g.fillText('♪',4,24-u*8);g.fillText('♫',74,18-(1-u)*8);g.globalAlpha=1}requestAnimationFrame(pm)}
if(T){load(T.chatData||T.publicData);T.onEvent('chatDataChanged',function(){load(T.chatData)})}setInterval(tick,250);requestAnimationFrame(pm);
</script></body></html>
""";

        // График нагрузки: история процессора и памяти (по 30 точек) на canvas
        const string SysmonHtml = """
<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><style>
html,body{margin:0}body{font:13px system-ui,sans-serif;color:var(--tg-theme-text-color,#eee);padding:10px 12px}.h{display:flex;justify-content:space-between;font-weight:600;font-size:15px}
.n{display:flex;gap:14px;margin:4px 0;font-size:12px}.n b{font-size:18px}canvas{width:100%;height:120px;display:block}
.r{display:flex;gap:6px;margin-top:6px}button{flex:1;border:0;border-radius:10px;padding:7px;background:#8882;color:inherit}
</style></head><body><div class="h"><span id="pc"></span><span id="tm"></span></div>
<div class="n"><span style="color:#64d2ff">CPU <b id="c"></b></span><span style="color:#bf5af2">RAM <b id="m"></b></span><span id="o"></span></div>
<canvas id="g" width="632" height="240"></canvas><div class="r"><button id="u">⟳ обновить</button></div><script>
var T=window.TelegramWidget;function $(i){return document.getElementById(i)}
function line(x,a,col){var w=632,h=240;x.strokeStyle=col;x.lineWidth=4;x.beginPath();a.forEach(function(v,i){var X=i/(Math.max(1,a.length-1))*w,Y=h-v/100*h*.92-4;i?x.lineTo(X,Y):x.moveTo(X,Y)});x.stroke();
x.globalAlpha=.15;x.lineTo(w,h);x.lineTo(0,h);x.fillStyle=col;x.fill();x.globalAlpha=1}
function show(s){var d={};try{d=JSON.parse(s||'{}')}catch(e){}var c=d.c||[],m=d.m||[];$('pc').textContent=d.n||'ПК';$('tm').textContent=d.t||'';
$('c').textContent=(c[c.length-1]||0)+'%';$('m').textContent=(m[m.length-1]||0)+'%';$('o').textContent=d.o||'';
var x=$('g').getContext('2d');x.clearRect(0,0,632,240);x.strokeStyle='#8884';x.lineWidth=1;for(var i=1;i<4;i++){x.beginPath();x.moveTo(0,i*60);x.lineTo(632,i*60);x.stroke()}
line(x,m,'#bf5af2');line(x,c,'#64d2ff')}
if(T){show(T.chatData||T.publicData);T.onEvent('chatDataChanged',function(){show(T.chatData)})}
$('u').onclick=function(){T&&T.request('c:sysmon:ref').then(function(a){a.result&&show(a.result)},function(){})};
</script></body></html>
""";

        // Мировые часы: идут сами в виджете, бот только присылает города и смещения
        const string ClockHtml = """
<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><style>
html,body{margin:0}body{font:13px system-ui,sans-serif;color:var(--tg-theme-text-color,#eee);padding:10px 12px}
.big{font-size:40px;font-weight:700;font-variant-numeric:tabular-nums;line-height:1.1}.d{color:var(--tg-theme-hint-color,#999)}
.c{display:flex;justify-content:space-between;padding:4px 0;border-top:1px solid #8883;font-variant-numeric:tabular-nums}.c b{font-size:16px}
</style></head><body><div class="d" id="me"></div><div class="big" id="t"></div><div class="d" id="dt"></div><div id="l" style="margin-top:6px"></div><script>
var T=window.TelegramWidget,Z={};function $(i){return document.getElementById(i)}
function p(n){return('0'+n).slice(-2)}
function at(off){var d=new Date(Date.now()+new Date().getTimezoneOffset()*6e4+off*6e4);return d}
function draw(){var me=at(Z.o||0);$('t').textContent=p(me.getHours())+':'+p(me.getMinutes())+':'+p(me.getSeconds());
$('dt').textContent=me.toLocaleDateString('ru-RU',{weekday:'long',day:'numeric',month:'long'});
$('l').innerHTML=(Z.z||[]).map(function(c){var d=at(c[1]),df=Math.round((c[1]-(Z.o||0))/60);return'<div class="c"><span>'+c[0]+'</span><span class="d">'+(df>=0?'+':'')+df+' ч</span><b>'+p(d.getHours())+':'+p(d.getMinutes())+'</b></div>'}).join('')}
function load(s){try{Z=JSON.parse(s||'{}')}catch(e){}$('me').textContent=Z.n||'';draw()}
if(T){load(T.chatData||T.publicData);T.onEvent('chatDataChanged',function(){load(T.chatData)})}setInterval(draw,1000);
</script></body></html>
""";

        // Калькулятор: целиком в виджете (кнопки, без клавиатуры), считает с приоритетом операций
        const string CalcHtml = """
<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><style>
html,body{margin:0}body{font:15px system-ui,sans-serif;color:var(--tg-theme-text-color,#eee);padding:10px}
#s{text-align:right;color:var(--tg-theme-hint-color,#999);min-height:18px;font-size:13px;overflow:hidden;white-space:nowrap}#v{text-align:right;font-size:32px;font-weight:600;overflow:hidden;white-space:nowrap}
.k{display:grid;grid-template-columns:repeat(4,1fr);gap:6px;margin-top:6px}button{border:0;border-radius:12px;padding:11px 0;font-size:18px;background:#8882;color:inherit}
.o{background:var(--tg-theme-button-color,#3390ec);color:#fff}
</style></head><body><div id="s"></div><div id="v">0</div><div class="k" id="k"></div><script>
var e='',L='C ( ) ÷ 7 8 9 × 4 5 6 − 1 2 3 + 0 . ⌫ ='.split(' ');function $(i){return document.getElementById(i)}
function ev(s){var t=s.replace(/×/g,'*').replace(/÷/g,'/').replace(/−/g,'-').match(/\d+\.?\d*|[-+*/()]/g)||[],i=0;
function P(){var x=M();while(t[i]=='+'||t[i]=='-'){var o=t[i++],y=M();x=o=='+'?x+y:x-y}return x}
function M(){var x=U();while(t[i]=='*'||t[i]=='/'){var o=t[i++],y=U();x=o=='*'?x*y:x/y}return x}
function U(){if(t[i]=='-'){i++;return-U()}if(t[i]=='('){i++;var x=P();i++;return x}return parseFloat(t[i++])}
var r=P();if(!isFinite(r))throw 0;return Math.round(r*1e10)/1e10}
L.forEach(function(c){var b=document.createElement('button');b.textContent=c;if('÷×−+='.indexOf(c)>=0)b.className='o';b.onclick=function(){
if(c=='C')e='';else if(c=='⌫')e=e.slice(0,-1);else if(c=='='){try{$('s').textContent=e+' =';e=String(ev(e))}catch(x){$('s').textContent='ошибка'}}else e+=c;
$('v').textContent=e||'0'};$('k').appendChild(b)});
</script></body></html>
""";

        // Кубики и монетка: всё случайное — в виджете, с анимацией
        const string DiceHtml = """
<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><style>
html,body{margin:0}body{font:14px system-ui,sans-serif;color:var(--tg-theme-text-color,#eee);padding:10px;text-align:center}
#v{font-size:56px;font-weight:700;min-height:80px;line-height:80px}#v.r{animation:sp .45s}@keyframes sp{50%{transform:rotate(200deg) scale(.6);opacity:.3}}
#h{color:var(--tg-theme-hint-color,#999);font-size:12px;min-height:16px}.k{display:grid;grid-template-columns:repeat(3,1fr);gap:6px;margin-top:8px}
button{border:0;border-radius:12px;padding:9px 0;font-size:15px;background:#8882;color:inherit}
</style></head><body><div id="v">🎲</div><div id="h">выбери бросок</div><div class="k">
<button data-n="6">🎲 1d6</button><button data-n="66">🎲🎲 2d6</button><button data-n="2">🪙 монетка</button>
<button data-n="20">d20</button><button data-n="100">1–100</button><button data-n="8">🎱 шар</button></div><script>
var A=['Да','Нет','Скорее да','Вряд ли','Спроси позже','Точно!','Без шансов','Знаки говорят да'];
function r(n){return 1+Math.floor(Math.random()*n)}
document.querySelectorAll('button').forEach(function(b){b.onclick=function(){var n=+b.dataset.n,v,h='';
if(n==66){var a=r(6),c=r(6);v=a+' + '+c;h='сумма '+(a+c)}else if(n==2){v=r(2)==1?'Орёл':'Решка'}else if(n==8){v='🎱';h=A[r(A.length)-1]}else{v=r(n);h='из '+n}
var e=document.getElementById('v');e.classList.remove('r');void e.offsetWidth;e.classList.add('r');e.textContent=v;document.getElementById('h').textContent=h}});
</script></body></html>
""";

        static string HtmlOf(string kind) => kind switch
        {
            "remote" => RemoteWidgetHtml, "pet" => PetHtml(), "sysmon" => SysmonHtml, "clock" => Rf(ClockHtml, "top:6px;right:6px"), "calc" => Rf(CalcHtml, "top:6px;left:6px"),
            "dice" => Rf(DiceHtml, "top:6px;right:6px"), "calendar" => Rf(CalendarHtml, "bottom:4px;right:6px"), "breath" => Rf(BreathHtml, "top:6px;right:6px"),
            "ttt" => Rf(TttHtml, "top:6px;right:6px"), "g2048" => G2048Html, "countdown" => Rf(CountdownHtml, "top:6px;right:6px"), "viz" => Rf(VizHtml, "bottom:6px;right:8px"),
            _ => CardHtml,
        };

        /// <summary>Кнопка «⟳» у виджетов, которые работают сами, без бота: перезагрузить виджет (часы и визуализатор заново берут данные).
        /// Вставляется после скрипта — обработчики «все кнопки» в самих виджетах её не трогают. У 2048 это «↺» в шапке.</summary>
        static string Rf(string html, string pos) => html.Replace("</body>",
            $"<button onclick=\"location.reload()\" style=\"position:fixed;{pos};border:0;border-radius:8px;background:#8882;color:inherit;padding:2px 8px;font-size:13px;flex:none\">⟳</button></body>");

        // ------------------------------------------------------------------
        //  Состояния
        // ------------------------------------------------------------------

        static readonly List<int> _cpuHist = new(), _ramHist = new();
        static DateTime _histAt;

        static void SampleHist()
        {
            lock (_cpuHist)
            {
                if ((DateTime.Now - _histAt).TotalSeconds < 3) return;
                _histAt = DateTime.Now;
                _cpuHist.Add(SystemService.I.CpuPercent()); _ramHist.Add(SystemService.I.RamPercent());
                while (_cpuHist.Count > 30) { _cpuHist.RemoveAt(0); _ramHist.RemoveAt(0); }
            }
        }

        static string Heat(double v) => v > 85 ? "#ff453a" : v > 60 ? "#ffd60a" : "#30d158";

        /// <summary>Сериализация с ужатием под 1024 байта: длинные списки строк обрезаются с конца.</summary>
        static string Fit(Dictionary<string, object> d)
        {
            var s = JsonSerializer.Serialize(d, WidgetJson);
            while (Encoding.UTF8.GetByteCount(s) > 1000 && d.TryGetValue("r", out var r) && r is List<object[]> rows && rows.Count > 1)
            {
                rows.RemoveAt(rows.Count - 1);
                s = JsonSerializer.Serialize(d, WidgetJson);
            }
            return s;
        }

        static Dictionary<string, object> Card(string k, string h, string s = null) => new() { ["k"] = k, ["h"] = T(h), ["s"] = s, ["r"] = new List<object[]>(), ["b"] = new List<object[]>() };
        static List<object[]> CRows(Dictionary<string, object> d) => (List<object[]>)d["r"];
        static void Btns(Dictionary<string, object> d, params object[][] row) => ((List<object[]>)d["b"]).Add(row);
        static object[] Bt(string label, string op, string style = null) => style == null ? new object[] { T(label), op } : new object[] { T(label), op, style };

        string WState(string kind, string note = null)
        {
            switch (kind)
            {
                case "remote": return RemoteState(note);
                case "pet": return PetState();
                case "sysmon":
                {
                    SampleHist();
                    lock (_cpuHist)
                        return JsonSerializer.Serialize(new Dictionary<string, object>
                        {
                            ["n"] = Cut(Environment.MachineName, 20), ["t"] = DateTime.Now.ToString("HH:mm:ss"), ["c"] = _cpuHist.ToArray(), ["m"] = _ramHist.ToArray(),
                            ["o"] = SystemService.I.HasBattery ? $"🔋 {SystemService.I.BatteryPercent}%" : "",
                        }, WidgetJson);
                }
                case "clock":
                {
                    var off = (int)TimeZoneInfo.Local.GetUtcOffset(DateTime.Now).TotalMinutes;
                    var z = WeatherService.WorldClocks().Take(6).Select(c => new object[] { c.Name, (int)Math.Round((c.Time - DateTime.UtcNow).TotalMinutes) }).ToList();
                    return JsonSerializer.Serialize(new Dictionary<string, object> { ["n"] = T("Время на ПК") + " · " + Cut(Environment.MachineName, 16), ["o"] = off, ["z"] = z }, WidgetJson);
                }
                case "calc": case "dice": case "calendar": case "breath": case "ttt": case "g2048": case "countdown": return "{}";
                case "viz": return VizState();
            }
            var d = CardState(kind);
            if (note != null) d["e"] = note;
            return Fit(d);
        }

        Dictionary<string, object> CardState(string kind)
        {
            var m = MediaService.I; var a = AudioService.I; var sys = SystemService.I;
            Dictionary<string, object> d;
            switch (kind)
            {
                case "player":
                    d = Card(kind, "🎵 " + (m.HasSession ? Cut(m.Title ?? m.TrackLine, 50) : "Ничего не играет"), m.HasSession ? Cut(m.Artist, 50) : null);
                    if (m.HasTime) CRows(d).Add(new object[] { "p", (int)m.Position.TotalSeconds, (int)m.Duration.TotalSeconds, m.IsPlaying ? 1 : 0 });
                    if (LyricsRow(280) is { } plr) CRows(d).Add(plr);
                    // мини-персонаж в шапке плеера: танцует, пока играет музыка; на паузе его нет. Цвет — как у персонажа на острове
                    if (m.IsPlaying && Store.Settings.WidgetPetDance) { string tint = null; try { tint = App.Island?.MascotTintHex; } catch { } d["pm"] = tint ?? "#7ab8ff"; }
                    CRows(d).Add(new object[] { "b", "🔊", Math.Round(a.Volume * 100), null, a.Muted ? "off" : Math.Round(a.Volume * 100) + "%" });
                    // батарея: сколько осталось до разряда, а на зарядке — до полной
                    var bs = SystemService.I;
                    if (bs.HasBattery)
                    {
                        int bp = bs.BatteryPercent;
                        string bt = bs.Charging
                            ? (bp >= 100 ? T("заряжен") : PcWatch.ChargeLeft() is TimeSpan cl ? T("до полной") + " " + Du((int)cl.TotalSeconds) : T("заряжается"))
                            : PcWatch.DischargeLeft() is TimeSpan dl ? T("осталось") + " " + Du((int)dl.TotalSeconds) : T("от батареи");
                        CRows(d).Add(new object[] { "b", bs.Charging ? "⚡" : "🔋", bp, bs.Charging ? "#30d158" : bp <= 15 ? "#ff453a" : bp <= 30 ? "#ffd60a" : "#30d158", bp + "% · " + bt });
                    }
                    Btns(d, Bt("⏮", "prev"), Bt(m.IsPlaying ? "⏸" : "▶", "play", "p"), Bt("⏭", "next"), Bt("♥", "like"));
                    Btns(d, Bt($"⏪ {MediaService.Step}с", "back"), Bt($"⏩ {MediaService.Step}с", "fwd"), Bt("🔀", "shuffle"), Bt("🔁", "repeat"), Bt(m.LoopB != null ? "A–B ✓" : m.LoopA != null ? "A…" : "A–B", "ab", m.LoopA != null ? "p" : null));
                    Btns(d, Bt("🔉", "vd"), Bt(a.Muted ? "🔈" : "🔇", "mute"), Bt("🔊", "vu"), Bt(TimerService.I.SleepAt != null ? "💤 ✓" : "💤 30", "sleep"));
                    Btns(d, Bt("🎤 Текст", "open|lyrics"), Bt("🌈 Виз", "open|viz"), Bt("🎛 EQ", "open|eq"));
                    return d;
                case "lyrics":
                {
                    d = Card(kind, "🎤 " + (m.HasSession ? Cut(m.TrackLine, 50) : "Ничего не играет"));
                    var lines = m.Lyrics; int i = m.LyricIndex;
                    if (LyricsRow(620) is { } llr) CRows(d).Add(llr);
                    else if (lines == null || lines.Count == 0) CRows(d).Add(new object[] { "t", T(m.LyricsLoading ? "Ищу текст…" : "Текст не найден") });
                    else
                    {
                        if (i > 0) CRows(d).Add(new object[] { "t", Cut(lines[i - 1].Text, 80) });
                        CRows(d).Add(new object[] { "T", Cut(i >= 0 ? lines[i].Text : lines[0].Text, 60) });
                        for (int k = i + 1; k < Math.Min(lines.Count, i + 3); k++) CRows(d).Add(new object[] { "t", Cut(lines[k].Text, 80) });
                    }
                    Btns(d, Bt("⏮", "prev"), Bt(m.IsPlaying ? "⏸" : "▶", "play", "p"), Bt("⏭", "next"));
                    Btns(d, Bt("⏪", "back"), Bt("♥", "like"), Bt("⏩", "fwd"));
                    return d;
                }
                case "mixer":
                    d = Card(kind, "🎚 Громкость программ");
                    CRows(d).Add(new object[] { "b", T("Общая"), Math.Round(a.Volume * 100), null, a.Muted ? "off" : Math.Round(a.Volume * 100) + "%" });
                    foreach (var app in a.Apps().Take(5))
                        CRows(d).Add(new object[] { "l", $"{Cut(app.Name, 18)} · {(app.Muted ? "off" : Math.Round(app.Volume * 100) + "%")}", "m|" + WHash(app.Name), app.Muted ? "🔈" : "🔇" });
                    Btns(d, Bt("−10", "vd"), Bt("🔇", "mute"), Bt("+10", "vu"));
                    Btns(d, Bt("0", "v|0"), Bt("25", "v|25"), Bt("50", "v|50"), Bt("75", "v|75"), Bt("100", "v|100"));
                    Btns(d, Bt("🎧 Куда идёт звук", "open|audioout"));
                    return d;
                case "eq":
                    d = Card(kind, "🎛 Эквалайзер", T(Store.Settings.EqOn ? "включён · " : "выключен · ") + T(Equalizer.PresetName(Store.Settings.EqPreset)));
                    foreach (var chunk in Equalizer.Presets.Take(12).Chunk(3))
                        Btns(d, chunk.Select(p => Bt((p.Key == Store.Settings.EqPreset ? "✓ " : "") + Cut(T(p.Name), 14), "p|" + p.Key)).ToArray());
                    Btns(d, Bt(Store.Settings.EqOn ? "Выключить" : "Включить", "toggle", Store.Settings.EqOn ? null : "s"));
                    return d;
                case "timer":
                {
                    var t = TimerService.I;
                    d = Card(kind, "⏲ Таймер на ПК");
                    CRows(d).Add(new object[] { "d", (int)t.Left.TotalSeconds, t.Running ? 1 : 0, T(t.Running ? "идёт" : t.Paused ? "на паузе" : "не запущен") });
                    Btns(d, Bt("1 мин", "1"), Bt("5", "5"), Bt("10", "10"), Bt("25", "25"));
                    Btns(d, Bt("+1", "add|1"), Bt("+5", "add|5"), Bt("+15", "add|15"), Bt("60", "60"));
                    Btns(d, Bt(t.Running ? "⏸ Пауза" : "▶ Старт", "toggle", "p"), Bt("⟲ Сброс", "reset"), Bt("✍️ Мин.", "in"));
                    return d;
                }
                case "stopwatch":
                {
                    var t = TimerService.I;
                    d = Card(kind, "⏱ Секундомер на ПК");
                    CRows(d).Add(new object[] { "u", (int)t.SwElapsed.TotalSeconds, t.SwRunning ? 1 : 0, t.Laps.Count > 0 ? T("круги: ") + string.Join(" · ", t.Laps.Take(3).Select(l => $"{(int)l.TotalMinutes}:{l.Seconds:00}")) : null });
                    Btns(d, Bt(t.SwRunning ? "⏸ Стоп" : "▶ Старт", "toggle", "p"), Bt("🏁 Круг", "lap"), Bt("⟲ Сброс", "reset"));
                    return d;
                }
                case "pomodoro":
                {
                    var p = PomodoroService.I;
                    d = Card(kind, "🍅 Помодоро", T(p.Phase switch { PomoPhase.Focus => "фокус", PomoPhase.Short => "короткий перерыв", _ => "длинный перерыв" }) + $" · раунд {p.Round + 1}");
                    CRows(d).Add(new object[] { "d", (int)p.Remaining.TotalSeconds, p.Running ? 1 : 0, T("сегодня: ") + PomodoroService.TodayCount });
                    Btns(d, Bt(p.Running ? "⏸ Пауза" : "▶ Старт", "toggle", "p"), Bt("⏭ Дальше", "skip"), Bt("⟲", "reset"));
                    return d;
                }
                case "weather":
                {
                    var w = WeatherService.Cached;
                    if (w == null) { d = Card(kind, "🌦 Погода", T("ещё не загружена")); Btns(d, Bt("⟳ Обновить", "ref")); return d; }
                    var (icon, text) = WeatherService.Describe(w.Code);
                    d = Card(kind, $"{icon} {Math.Round(w.Temp):0}° · {w.City}", T(text) + " · " + T("ощущается") + $" {Math.Round(w.Feels):0}°");
                    CRows(d).Add(new object[] { "k", T("Ветер"), $"{w.Wind:0} м/с" });
                    CRows(d).Add(new object[] { "k", T("Влажность"), $"{w.Humidity:0}%" });
                    if (w.Sunrise is { } sr) CRows(d).Add(new object[] { "k", T("Восход и закат"), $"{sr:HH:mm} – {w.Sunset:HH:mm}" });
                    foreach (var hr in w.Hours.Where(h => h.T > DateTime.Now).Take(4))
                        CRows(d).Add(new object[] { "k", hr.T.ToString("HH:mm"), $"{WeatherService.Describe(hr.Code).Icon} {Math.Round(hr.Temp):0}°" + (hr.Rain > 0 ? $" · ☔ {hr.Rain}%" : "") });
                    Btns(d, Bt("⟳ Обновить", "ref"), Bt("🌗 Солнце и луна", "open|sky"));
                    return d;
                }
                case "battery":
                    d = Card(kind, "🔋 Батарея");
                    if (!sys.HasBattery) CRows(d).Add(new object[] { "t", T("У этого ПК нет батареи") });
                    else
                    {
                        CRows(d).Add(new object[] { "b", sys.Charging ? "⚡" : "🔋", sys.BatteryPercent, sys.BatteryPercent < 20 ? "#ff453a" : "#30d158", sys.BatteryPercent + "%" });
                        CRows(d).Add(new object[] { "k", T("Состояние"), T(sys.Charging ? "заряжается" : "от батареи") });
                    }
                    foreach (var pl in Maint.PowerPlans().Take(3)) CRows(d).Add(new object[] { "l", (pl.Active ? "✓ " : "") + Cut(pl.Name, 30), pl.Active ? null : "pl|" + pl.Guid, T("Включить") });
                    Btns(d, Bt("⏻ Питание", "open|power"), Bt("🩺 Здоровье ПК", "open|health"));
                    return d;
                case "network":
                {
                    NetSpeed.Sample();
                    d = Card(kind, "📶 Сеть", Vpn.Active ? "🔒 VPN" : null);
                    var link = WifiTools.Current().GetAwaiter().GetResult();
                    if (link != null) CRows(d).Add(new object[] { "b", "Wi-Fi", link.Signal, link.Signal < 40 ? "#ff453a" : "#30d158", $"{link.Signal}%" });
                    if (link != null) CRows(d).Add(new object[] { "k", Cut(link.Ssid, 24), $"{link.Band} · к{link.Channel} · {link.RxMbps:0} Мбит/с" });
                    CRows(d).Add(new object[] { "k", "↓ / ↑", $"{NetSpeed.Fmt(NetSpeed.Down)} / {NetSpeed.Fmt(NetSpeed.Up)}" });
                    Btns(d, Bt("🔄 Переподключить", "reconnect"), Bt("⟳", "ref"));
                    Btns(d, Bt("📡 Вокруг", "open|wifiscan"), Bt("🚀 Скорость", "open|speed"), Bt("📶 Пинг", "open|ping"));
                    return d;
                }
                case "disks":
                    d = Card(kind, "💽 Диски");
                    foreach (var dk in Maint.Disks().Take(5))
                    {
                        double used = dk.Total > 0 ? 100.0 * (dk.Total - dk.Free) / dk.Total : 0;
                        CRows(d).Add(new object[] { "b", dk.Name.TrimEnd('\\'), Math.Round(used), Heat(used), Maint.Size(dk.Free) + " " + T("своб.") });
                    }
                    var (bb, bi) = Maint.RecycleBin();
                    CRows(d).Add(new object[] { "k", T("Корзина"), $"{bi} · {Maint.Size(bb)}" });
                    return d;
                case "apps":
                    d = Card(kind, "📊 Кто грузит ПК", $"CPU {sys.CpuPercent()}% · RAM {sys.RamPercent()}%");
                    foreach (var (name, pct) in TopCpu(6))
                        CRows(d).Add(new object[] { "l", $"{Cut(name, 20)} · {pct:0.#}%", Critical.Contains(name) ? null : "k|" + WHash(name), "✖" });
                    Btns(d, Bt("⟳ Обновить", "ref"), Bt("🪟 Окна", "open|windows"), Bt("📈 График", "open|sysmon"));
                    return d;
                case "todo":
                {
                    d = Card(kind, "✅ Задачи");
                    var list = Store.Data?.Todos?.ToList() ?? new();
                    if (list.Count == 0) CRows(d).Add(new object[] { "t", T("Задач нет. Добавить: /todo текст") });
                    for (int i = 0; i < Math.Min(8, list.Count); i++)
                        CRows(d).Add(new object[] { "l", (list[i].Done ? "☑️ " : "⬜ ") + Cut(list[i].Text, 40), "t|" + list[i].Id, list[i].Done ? "↺" : "✓" });
                    Btns(d, Bt("✍️ Добавить", "in", "p"), Bt("🧹 Убрать сделанные", "clean"), Bt("⟳", "ref"));
                    return d;
                }
                case "notes":
                {
                    d = Card(kind, "📝 Заметки");
                    var n = (Store.Data?.Notes ?? "").Trim();
                    CRows(d).Add(new object[] { "t", n == "" ? T("Пусто. Добавить: /note текст") : Cut(n, 600) });
                    if (n != "") CRows(d).Add(new object[] { "y", T("Копировать"), Cut(n, 256) });
                    Btns(d, Bt("✍️ Дописать", "in", "p"), Bt("⟳", "ref"));
                    return d;
                }
                case "clipboard":
                {
                    d = Card(kind, "📋 Буфер обмена ПК");
                    var items = ClipboardService.I.Items.Take(5).ToList();
                    if (items.Count == 0) CRows(d).Add(new object[] { "t", T("История пуста") });
                    foreach (var it in items) CRows(d).Add(new object[] { "y", "📱", Cut(it.Text.Replace('\n', ' '), 120) });
                    Btns(d, Bt("✍️ Текст в буфер ПК", "in", "p"), Bt("⟳", "ref"));
                    return d;
                }
                case "modes":
                    d = Card(kind, "🌙 Режим острова", T(Modes.Current.Name));
                    foreach (var chunk in Modes.All.Chunk(3))
                        Btns(d, chunk.Select(md => Bt((md.Key == Store.Settings.Mode ? "✓ " : "") + Cut(T(md.Name), 14), "m|" + md.Key, md.Key == Store.Settings.Mode ? "p" : null)).ToArray());
                    return d;
                case "scenes":
                    d = Card(kind, "🎬 Сценарии", T("питание, яркость, режим острова — одним нажатием"));
                    foreach (var chunk in Laptop.Scenarios.Chunk(2))
                        Btns(d, chunk.Select(sc => Bt(Cut(T(sc.Title), 18), "s|" + sc.Key)).ToArray());
                    return d;
                case "display":
                {
                    int? br = sys.GetBrightness();
                    d = Card(kind, "🖥 Экран");
                    if (br is int b) CRows(d).Add(new object[] { "b", "☀", b, "#ffd60a", b + "%" });
                    else CRows(d).Add(new object[] { "t", T("Яркость этого монитора не регулируется") });
                    Btns(d, Bt("−10", "bd"), Bt("+10", "bu"));
                    if (br is int) Btns(d, Bt("10", "b|10"), Bt("40", "b|40"), Bt("70", "b|70"), Bt("100", "b|100"));
                    Btns(d, Bt("🌗 Тема", "theme"), Bt("⬛ Выкл. экран", "off"));
                    Btns(d, Bt("🖥 Только ПК", "pr|pc"), Bt("⧉ Повтор", "pr|clone"), Bt("⇆ Расширить", "pr|extend"));
                    return d;
                }
                case "power":
                    d = Card(kind, "⏻ Питание", sys.HasBattery ? $"🔋 {sys.BatteryPercent}%" : null);
                    if (Laptop.OffAt is DateTime off) CRows(d).Add(new object[] { "d", (int)Math.Max(0, (off - DateTime.Now).TotalSeconds), 1, T("до выключения") });
                    Btns(d, Bt("🔒 Блок", "lock"), Bt("😴 Сон", "sleep"));
                    Btns(d, Bt("⏲ 15 мин", "in15"), Bt("⏲ 30 мин", "in30"), Bt("⏲ 60 мин", "in60"), Bt("✖ Отменить", "cancel"));
                    Btns(d, Bt("⏰ Отложенные команды", "open|jobs"));
                    Btns(d, Bt("🔄 Перезагрузка", "restart", "d"), Bt("⏻ Выключить", "shutdown", "d"));
                    return d;
            }
            return CardState2(kind) ?? CardState3(kind) ?? CardState4(kind) ?? CardState5(kind) ?? CardState6(kind) ?? Card(kind, "Неизвестный виджет");
        }

        // ------------------------------------------------------------------
        //  Действия карточек
        // ------------------------------------------------------------------

        async Task<string> CardAction(string q, long msgId)
        {
            int c = q.IndexOf(':');
            if (c <= 0) return null;
            string kind = q[..c], op = q[(c + 1)..];
            if (WK(kind) == null) return null;
            string flag = WFlag(kind) ?? "control";
            WStateDirty(kind);
            string head0 = op.Contains('|') ? op[..op.IndexOf('|')] : op;
            // каталог «🧩 Все виджеты» и «⬅ Назад» — меняют этот же виджет на месте
            if (kind == "menu")
                return head0 is "ref" or "" ? Wrap(msgId, Fit(MenuState(_hubCat.GetValueOrDefault(msgId, -1)))) : await MenuAction(head0, op.Contains('|') ? op[(op.IndexOf('|') + 1)..] : "", msgId);
            if (head0 == "nav") return NavBack(msgId, kind);
            if (head0 == "open")
            {
                var target = op[5..];
                if (WK(target) == null) return WState(kind, T("Нет такого виджета"));
                // карточка → карточка: тот же виджет становится нужным (без нового сообщения)
                if (msgId != 0 && IsCard(target) && IsCard(kind) && (WFlag(target) is not { } tneed || Flag(tneed))) return Morph(msgId, kind, target);
                return WState(kind, await SendW(target) ? T("🧩 Виджет — ниже в чате") : T("Виджеты недоступны в этом Telegram"));
            }
            // нажатие, которого у этого вида нет (старый виджет, подделка) — ничего не делаем
            if (!KnownOp(kind, head0)) return Wrap(msgId, WState(kind, T("Неизвестное действие")));
            if (op is not ("ref" or "") && !Flag(flag)) return Wrap(msgId, WState(kind, T($"🔐 «{FlagNames[flag]}» выключено — включается на ПК")));
            string note = null;
            var m = MediaService.I; var a = AudioService.I; var sys = SystemService.I;
            string arg = op.Contains('|') ? op[(op.IndexOf('|') + 1)..] : "";
            string head = op.Contains('|') ? op[..op.IndexOf('|')] : op;
            switch (kind, head)
            {
                case ("player" or "lyrics", "prev"): await Ui(m.Prev); await Task.Delay(700); break;
                case ("player" or "lyrics", "next"): await Ui(m.Next); await Task.Delay(700); break;
                case ("player" or "lyrics", "play"): await Ui(m.TogglePlay); await Task.Delay(300); break;
                case ("player", "like"): note = await Ui(() => m.ToggleFavorite()) ? "💖" : null; break;
                case ("player" or "lyrics", "back"): await Ui(() => m.SeekStep(-1)); break;
                case ("player" or "lyrics", "fwd"): await Ui(() => m.SeekStep(1)); break;
                case ("lyrics", "like"): note = await Ui(() => m.ToggleFavorite()) ? "💖" : null; break;
                case ("player", "vu"): await Ui(() => a.SetVolume(Math.Min(1, a.Volume + 0.1))); break;
                case ("player", "vd"): await Ui(() => a.SetVolume(Math.Max(0, a.Volume - 0.1))); break;
                case ("player", "mute"): await Ui(a.ToggleMute); break;
                case ("player", "sleep"):
                {
                    bool on = TimerService.I.SleepAt == null;
                    await Ui(() => TimerService.I.SetSleep(on ? 30 : 0));
                    note = T(on ? "💤 Музыка остановится через 30 минут" : "💤 Таймер сна выключен");
                    break;
                }
                case ("mixer", "v") when int.TryParse(arg, out var mv) && mv is >= 0 and <= 100:
                    await Ui(() => { a.SetVolume(mv / 100.0); if (mv > 0 && a.Muted) a.ToggleMute(); }); break;
                case ("timer", "add") when int.TryParse(arg, out var tadd) && tadd is > 0 and <= 120:
                    await Ui(() => { var tm = TimerService.I; if (tm.Running || tm.Paused) tm.Add(tadd); else { tm.SetMinutes(tadd); tm.Start(); } }); break;
                case ("display", "b") when int.TryParse(arg, out var bv) && bv is >= 0 and <= 100:
                    await Ui(() => sys.SetBrightness(bv)); break;
                case ("display", "pr") when arg is "pc" or "clone" or "extend" or "second":
                    note = Cut(System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace((await Command4("project", arg))?.Html ?? "", "<[^>]+>", "")), 80); break;
                case ("power", "in15" or "in60"): await Ui(() => Laptop.ScheduleOff("shutdown", head == "in15" ? 15 : 60)); break;
                case ("player", "ab"): note = await Ui(() => m.CycleLoop()) switch { "A" => T("🅰 Начало отмечено — нажми ещё раз в конце"), "AB" => T("🔂 Фрагмент повторяется"), _ => T("Повтор фрагмента выключен") }; break;
                case ("player", "shuffle"): await Command2("shuffle", ""); break;
                case ("player", "repeat"): await Command2("repeat", ""); break;
                case ("mixer", "vu"): await Ui(() => a.SetVolume(Math.Min(1, a.Volume + 0.1))); break;
                case ("mixer", "vd"): await Ui(() => a.SetVolume(Math.Max(0, a.Volume - 0.1))); break;
                case ("mixer", "mute"): await Ui(a.ToggleMute); break;
                case ("mixer", "m"):
                {
                    var app = a.Apps().FirstOrDefault(x => WHash(x.Name) == arg);
                    if (app != null) a.SetAppVolume(app.Name, app.Volume, !app.Muted);
                    break;
                }
                case ("eq", "p"): await Ui(() => { Equalizer.SetPreset(arg); if (!Store.Settings.EqOn) Equalizer.SetOn(true); }); break;
                case ("eq", "toggle"): await Ui(() => Equalizer.SetOn(!Store.Settings.EqOn)); break;
                case ("timer", "toggle"): await Ui(TimerService.I.Toggle); break;
                case ("timer", "reset"): await Ui(TimerService.I.Reset); break;
                case ("timer", _) when int.TryParse(head, out var min):
                    await Ui(() => { var t = TimerService.I; t.Reset(); t.SetMinutes(min); t.Start(); }); break;
                case ("stopwatch", "toggle"): await Ui(TimerService.I.SwToggle); break;
                case ("stopwatch", "lap"): await Ui(TimerService.I.SwLap); break;
                case ("stopwatch", "reset"): await Ui(TimerService.I.SwReset); break;
                case ("pomodoro", "toggle"): await Ui(PomodoroService.I.Toggle); break;
                case ("pomodoro", "skip"): await Ui(PomodoroService.I.Skip); break;
                case ("pomodoro", "reset"): await Ui(PomodoroService.I.Reset); break;
                case ("weather", "ref"): await WeatherService.Get(true); break;
                case ("battery", "pl"): if (System.Text.RegularExpressions.Regex.IsMatch(arg, "^[0-9a-fA-F-]{36}$")) await Task.Run(() => Maint.SetPowerPlan(arg)); break;
                case ("network", "reconnect"): note = T(await WifiTools.Reconnect()); break;
                case ("apps", "k"):
                {
                    var pname = TopCpu(30).Select(x => x.Name).FirstOrDefault(x => WHash(x) == arg);
                    if (pname != null && !Critical.Contains(pname)) note = $"✖ {pname}: " + await Task.Run(() => Kill(pname));
                    else note = T("Программа уже закрыта");
                }
                    break;
                case ("todo", "t"):
                    await Ui(() => { var it = Store.Data.Todos.FirstOrDefault(x => x.Id == arg); if (it != null) { it.Done = !it.Done; Store.SaveData(); } });
                    break;
                case ("todo", "clean"):
                    await Ui(() => { foreach (var it in Store.Data.Todos.Where(x => x.Done).ToList()) Store.Data.Todos.Remove(it); Store.SaveData(); });
                    break;
                case ("modes", "m"): if (Modes.All.Any(x => x.Key == arg)) await Ui(() => App.Island?.SetMode(arg)); break;
                case ("scenes", "s"):
                {
                    var sc = Laptop.Scenarios.FirstOrDefault(x => x.Key == arg);
                    if (sc.Key != null) { await Ui(() => Laptop.ApplyScenario(sc.Key)); note = "🎬 " + T(sc.Title); }
                    break;
                }
                case ("display", "bu" or "bd"):
                {
                    int cur = sys.GetBrightness() ?? 50;
                    await Ui(() => sys.SetBrightness(Math.Clamp(cur + (head == "bu" ? 10 : -10), 0, 100)));
                    break;
                }
                case ("display", "theme"): await Ui(sys.ToggleTheme); break;
                case ("display", "off"): PcControl.MonitorOff(); note = T("Экран выключен"); break;
                case ("power", "lock"): Native.LockWorkStation(); note = "🔒"; break;
                case ("power", "sleep"): note = T("😴 Сон через 3 секунды"); _ = Task.Delay(3000).ContinueWith(_ => OnUi(() => System.Windows.Forms.Application.SetSuspendState(System.Windows.Forms.PowerState.Suspend, false, false))); break;
                case ("power", "in30"): await Ui(() => Laptop.ScheduleOff("shutdown", 30)); break;
                case ("power", "cancel"): await Ui(Laptop.CancelOff); Run("shutdown", "/a"); note = T("Выключение отменено"); break;
                case ("power", "restart" or "shutdown"):
                    // одно нажатие — только предупреждение; второе в течение 10 с — выполнить
                    if (_powerConfirm == head && (DateTime.Now - _powerConfirmAt).TotalSeconds < 10)
                    {
                        _powerConfirm = null;
                        Run("shutdown", head == "restart" ? "/r /t 60" : "/s /t 60");
                        note = T(head == "restart" ? "🔄 Перезагрузка через минуту (✖ Отменить)" : "⏻ Выключение через минуту (✖ Отменить)");
                    }
                    else { _powerConfirm = head; _powerConfirmAt = DateTime.Now; note = T("Нажми ещё раз в течение 10 секунд, чтобы подтвердить"); }
                    break;
                default: note = await CardAction2(kind, head, arg) ?? await CardAction3(kind, head, arg) ?? await CardAction4(kind, head, arg) ?? await CardAction5(kind, head, arg) ?? note; break;
            }
            // нажали — живой виджет обновляется ещё 5 минут
            var wk = WK(kind);
            Live(msgId, kind);
            return Wrap(msgId, WState(kind, note));
        }

        /// <summary>Какое разрешение нужно виду (null — «Управление с телефона» для нажатий, просмотр свободный).</summary>
        static string WFlag(string kind) => kind switch
        {
            "power" => "power", "apps" or "windows" or "startup" or "upgrades" or "launcher" => "apps", "keys" or "slides" or "mouse" or "typer" => "input",
            "screenshot" => "screen", "downloads" or "shelf" => "files", _ => null,
        };

        string _powerConfirm;
        DateTime _powerConfirmAt;

        // ------------------------------------------------------------------
        //  Отправка, живые обновления, меню
        // ------------------------------------------------------------------

        async Task<bool> SendW(string kind)
        {
            if (Remote != null || _noWidgets) return false;
            var wk = WK(kind);
            if (wk == null) return false;
            if (WFlag(kind) is { } need && !Flag(need)) { await Message(Need(need)); return true; }
            try
            {
                bool queries = kind is not ("calc" or "dice" or "clock" or "calendar" or "breath" or "ttt" or "g2048" or "countdown" or "viz");
                // высоту 0 («по содержимому») не передаём — так делает и тестовый бот Telegram
                var body = new Dictionary<string, object> { ["chat_id"] = V("chat"), ["html"] = HtmlOf(kind), ["width"] = wk.W, ["allow_queries"] = queries, ["chat_data"] = WState(kind) };
                if (wk.H > 0) body["height"] = wk.H;
                if (await ShareKeys(kind) is { } sk) body["reply_markup"] = sk;
                JsonElement r;
                try { r = await Call("sendWidget", body); }
                catch (TgError e) when (body.ContainsKey("reply_markup") && e.Code == 400)
                {
                    // кнопку под виджетом Telegram может не принять — тогда без неё
                    _noWidgetKeys = true;
                    body.Remove("reply_markup");
                    r = await Call("sendWidget", body);
                }
                long id = r.ValueKind == JsonValueKind.Object && r.TryGetProperty("message_id", out var mid) ? mid.GetInt64() : 0;
                if (id != 0) lock (_widgets) _widgets[id] = (kind, DateTime.Now);
                Live(id, kind);
                return true;
            }
            catch (TgError e)
            {
                Log.Info("telegram widget: " + e.Message);
                if (e.Code == 404 || e.Message.Contains("not found", StringComparison.OrdinalIgnoreCase) || e.Message.Contains("NOT_ALLOWED")) _noWidgets = true;
                return false;
            }
        }

        /// <summary>Живой виджет: бот обновляет состояние с шагом вида (4–30 с), пока не истекло время и только если оно изменилось.</summary>
        async Task KeepFresh(long id, string kind)
        {
            var wk = WK(kind);
            if (wk == null || wk.LiveSec <= 0) return;
            string last = null;
            try { last = LiveKey(kind, WStateCached(kind)); } catch { }
            while (true)
            {
                (string Kind, DateTime Until) w;
                lock (_widgets) if (!_widgets.TryGetValue(id, out w)) return;
                if (DateTime.Now > w.Until) return;
                // виджет сменили на месте — дальше обновляем уже новый вид
                if (w.Kind != kind) { kind = w.Kind; wk = WK(kind); last = null; if (wk == null || wk.LiveSec <= 0) return; }
                await Task.Delay(wk.LiveSec * 1000);
                lock (_widgets) if (!_widgets.TryGetValue(id, out w) || w.Kind != kind) continue;
                string st;
                try { st = WStateCached(kind); } catch (Exception e) { Log.Info("widget state: " + e.Message); continue; }
                var key = LiveKey(kind, st);
                if (key == last) continue;
                last = key;
                try { await Call("editMessageWidgetData", new { chat_id = V("chat"), message_id = id, chat_data = Wrap(id, st) }); }
                catch (TgError e) when (e.Message.Contains("not modified")) { }
                // «слишком часто» — подождать и продолжить, а не бросать живой виджет
                catch (TgError e) when (e.Code == 429) { await Task.Delay(15000); }
                catch (TgError e) { Log.Info("telegram widget live: " + e.Message); lock (_widgets) _widgets.Remove(id); return; }
            }
        }

        static readonly Dictionary<string, string> WAliases = new(StringComparer.OrdinalIgnoreCase)
        {
            ["пульт"] = "remote", ["персонаж"] = "pet", ["нагрузка"] = "sysmon", ["плеер"] = "player", ["музыка"] = "player", ["текст"] = "lyrics",
            ["громкость"] = "mixer", ["эквалайзер"] = "eq", ["таймер"] = "timer", ["секундомер"] = "stopwatch", ["помодоро"] = "pomodoro",
            ["часы"] = "clock", ["погода"] = "weather", ["батарея"] = "battery", ["сеть"] = "network", ["диски"] = "disks", ["процессы"] = "apps",
            ["задачи"] = "todo", ["заметки"] = "notes", ["буфер"] = "clipboard", ["режимы"] = "modes", ["сценарии"] = "scenes", ["экран"] = "display",
            ["питание"] = "power", ["калькулятор"] = "calc", ["кубики"] = "dice",
            ["вода"] = "water", ["уведомления"] = "notif", ["агенты"] = "agents", ["ии"] = "ai", ["впн"] = "vpn", ["wifi"] = "wifiscan",
            ["вайфай"] = "wifiscan", ["скорость"] = "speed", ["курсы"] = "rates", ["валюта"] = "rates", ["луна"] = "sky", ["солнце"] = "sky",
            ["здоровье"] = "health", ["автозагрузка"] = "startup", ["окна"] = "windows", ["обновления"] = "upgrades", ["история"] = "history",
            ["клавиши"] = "keys", ["клавиатура"] = "keys", ["презентация"] = "slides", ["слайды"] = "slides", ["отложенные"] = "jobs",
            ["защита"] = "defender", ["антивирус"] = "defender", ["календарь"] = "calendar", ["дыхание"] = "breath",
            ["мышь"] = "mouse", ["тачпад"] = "mouse", ["набрать"] = "typer", ["печать"] = "typer", ["запуск"] = "launcher", ["программы"] = "launcher",
            ["вывод"] = "audioout", ["звук"] = "audioout", ["снимок"] = "screenshot", ["скриншот"] = "screenshot", ["хотспот"] = "hotspot", ["точка"] = "hotspot",
            ["пинг"] = "ping", ["устройства"] = "lan", ["крипта"] = "crypto", ["криптовалюты"] = "crypto", ["спросить"] = "askai", ["вопрос"] = "askai",
            ["визуализатор"] = "viz", ["спектр"] = "viz", ["отсчёт"] = "countdown", ["отсчет"] = "countdown", ["крестики"] = "ttt", ["2048"] = "g2048",
            ["экранное"] = "screentime", ["время"] = "screentime", ["неделя"] = "week", ["сессии"] = "sessions", ["заряд"] = "batlog", ["трафик"] = "traffic",
            ["флешки"] = "usblog", ["usb"] = "usblog", ["ip"] = "iplog", ["загрузки"] = "downloads", ["температура"] = "temps", ["память"] = "ramtop",
            ["журнал"] = "timeline", ["события"] = "agenda", ["отчёт"] = "report", ["отчет"] = "report", ["микрофон"] = "mic", ["обои"] = "wall",
            ["лимиты"] = "limits", ["фокус"] = "focus", ["слежка"] = "procwatch", ["будильник"] = "alarms", ["будильники"] = "alarms",
            ["починки"] = "fixes", ["починить"] = "fixes", ["незасыпать"] = "awake", ["сторож"] = "guard", ["радио"] = "radios", ["bluetooth"] = "radios",
            ["приватность"] = "privacy", ["слежка камеры"] = "privacy", ["молитвы"] = "prayer", ["намаз"] = "prayer", ["полка"] = "shelf",
            ["переводчик"] = "translate", ["перевод"] = "translate", ["очистка"] = "cleanup", ["мусор"] = "cleanup", ["новости"] = "news", ["каталог"] = "menu", ["меню"] = "menu",
            ["браузер"] = "browser", ["вкладки"] = "browser", ["ссылки"] = "browser", ["chrome"] = "browser",
        };

        async Task<(string Html, object Keys)?> WidgetsCommand(string cmd, string arg)
        {
            if (cmd == "pet") arg = "pet";
            if (cmd == "widgets" || (cmd == "widget" && arg is "list" or "все" or "?")) return WidgetsMenu();
            var kind = arg == "" ? "remote" : WAliases.TryGetValue(arg.Trim(), out var al) ? al : arg.Trim().ToLowerInvariant();
            if (WK(kind) == null) return WidgetsMenu("Нет такого виджета. Вот все:");
            if (await SendW(kind)) return null;
            return ("🧩 Виджеты Telegram (Bot API 10.4) пока не работают у этого бота или в твоём приложении Telegram. Вместо пульта-виджета — живая панель:\n\n" + Dashboard(),
                    K(Row(B("📡 Живая панель", "do2:live"))));
        }

        (string, object) WidgetsMenu(string title = null) => WidgetsCats(title);

        async Task<string> WidgetsCallback(string rest, long msgId = 0)
        {
            // «📤 Переслать» под виджетом (и старые кнопки «share:» в чате) — выбор чата и пересылка самого виджета
            if (rest == "fw" || rest.StartsWith("share:")) return await StartForward(msgId);
            if (rest == "!stop") { lock (_widgets) _widgets.Clear(); return T("⏹ Живые виджеты больше не обновляются"); }
            if (rest == "!ref")
            {
                // последние 20 виджетов (а не все когда-либо отправленные); живые продлеваются, цикл на сообщение — один
                List<(long, string)> list;
                lock (_widgets) list = _widgets.OrderByDescending(w => w.Key).Take(20).Select(w => (w.Key, w.Value.Kind)).ToList();
                foreach (var (id, kind) in list)
                {
                    try { await Call("editMessageWidgetData", new { chat_id = V("chat"), message_id = id, chat_data = WState(kind) }); } catch (TgError) { }
                    Live(id, kind);
                }
                return T("🔄 Обновлено: ") + list.Count;
            }
            if (rest.StartsWith("@c:") && int.TryParse(rest[3..], out var ci))
            {
                var (h, k) = ci < 0 ? WidgetsCats() : WidgetsCat(ci);
                await Edit(msgId, h, k);
                return "";
            }
            if (WK(rest) == null) return Stale;
            return await OpenFromMenu(rest);
        }
    }
}
