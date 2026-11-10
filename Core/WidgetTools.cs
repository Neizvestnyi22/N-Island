using System;
using System.Collections.Generic;
namespace NIsland.Core
{
    /// <summary>Offline widgets: no bot requests, inputs, network or host commands. Per-message/device storage.</summary>
    public static class WidgetTools
    {
        public record Tool(string Key, string Title, string Html);
        public static readonly Tool[] All =
        {
            new("counter", "🔢 Счётчик", """
<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><style>
html,body{margin:0}body{font:14px/1.4 system-ui,sans-serif;color:var(--tg-theme-text-color,#222);padding:12px 12px max(12px,env(safe-area-inset-bottom))}header{display:flex;align-items:center;justify-content:space-between;gap:8px}h3{margin:0;font-size:16px}#v{font-size:32px;font-weight:700;font-variant-numeric:tabular-nums;margin-top:12px}#sub,small{color:var(--tg-theme-hint-color,#777);font-size:12px}#buttons{display:grid;grid-template-columns:repeat(2,1fr);gap:6px;margin:12px 0}button{border:0;border-radius:12px;padding:9px 8px;background:var(--tg-theme-secondary-bg-color,#eef2f6);color:inherit;font:inherit;min-height:36px}button:active{background:var(--tg-theme-button-color,#3390ec);color:var(--tg-theme-button-text-color,#fff)}#lines{min-height:20px;font-size:12px}#lines p{margin:4px 0;overflow-wrap:anywhere}footer{margin-top:12px;border-top:1px solid #8883;padding-top:6px}header button{font-size:12px} @media(prefers-reduced-motion:reduce){*{animation:none;transition:none}}
</style></head><body><header><h3>🔢 Счётчик</h3><button id="reset">↺ Сброс</button></header><div id="v"></div><div id="sub"></div><div id="buttons"></div><div id="lines"></div><footer><small>N-Island · локально, без запросов к ПК</small></footer><script>
var initial={"n":0,"step":1},S=JSON.parse(JSON.stringify(initial));function $(id){return document.getElementById(id)}
try{var stored=JSON.parse(localStorage.getItem('ni-tool-v1')||'null');if(stored&&typeof stored=='object')Object.keys(initial).forEach(function(k){if(typeof stored[k]==typeof initial[k]&&Array.isArray(stored[k])==Array.isArray(initial[k])&&(typeof stored[k]!='number'||(isFinite(stored[k])&&Math.abs(stored[k])<=1000000000000))&&(!Array.isArray(stored[k])||stored[k].length<=128))S[k]=stored[k]})}catch(e){}
function save(){try{localStorage.setItem('ni-tool-v1',JSON.stringify(S))}catch(e){}}
function show(v,sub){$('v').textContent=v;$('sub').textContent=sub||'';$('buttons').innerHTML='';$('lines').innerHTML=''}
function line(t){var p=document.createElement('p');p.textContent=t;$('lines').appendChild(p)}
function A(t,fn){var b=document.createElement('button');b.textContent=t;b.onclick=fn;$('buttons').appendChild(b)}
function done(){save();draw()}function fmt(n){n=Math.max(0,Math.ceil(n));return Math.floor(n/60)+':'+('0'+n%60).slice(-2)}
function draw(){show(S.n,'Шаг '+S.step);A('−',function(){S.n=Math.max(-999999,S.n-S.step);done()});A('+',function(){S.n=Math.min(999999,S.n+S.step);done()});A('Шаг 1 / 10',function(){S.step=S.step==1?10:1;done()})}
$('reset').onclick=function(){S=JSON.parse(JSON.stringify(initial));if(typeof phase!='undefined'){clearTimeout(wait);phase=0}done()};draw();if(window.TelegramWidget&&TelegramWidget.ready)TelegramWidget.ready();
</script></body></html>
"""),
            new("score", "🏆 Табло", """
<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><style>
html,body{margin:0}body{font:14px/1.4 system-ui,sans-serif;color:var(--tg-theme-text-color,#222);padding:12px 12px max(12px,env(safe-area-inset-bottom))}header{display:flex;align-items:center;justify-content:space-between;gap:8px}h3{margin:0;font-size:16px}#v{font-size:32px;font-weight:700;font-variant-numeric:tabular-nums;margin-top:12px}#sub,small{color:var(--tg-theme-hint-color,#777);font-size:12px}#buttons{display:grid;grid-template-columns:repeat(2,1fr);gap:6px;margin:12px 0}button{border:0;border-radius:12px;padding:9px 8px;background:var(--tg-theme-secondary-bg-color,#eef2f6);color:inherit;font:inherit;min-height:36px}button:active{background:var(--tg-theme-button-color,#3390ec);color:var(--tg-theme-button-text-color,#fff)}#lines{min-height:20px;font-size:12px}#lines p{margin:4px 0;overflow-wrap:anywhere}footer{margin-top:12px;border-top:1px solid #8883;padding-top:6px}header button{font-size:12px} @media(prefers-reduced-motion:reduce){*{animation:none;transition:none}}
</style></head><body><header><h3>🏆 Табло</h3><button id="reset">↺ Сброс</button></header><div id="v"></div><div id="sub"></div><div id="buttons"></div><div id="lines"></div><footer><small>N-Island · локально, без запросов к ПК</small></footer><script>
var initial={"a":0,"b":0,"h":[]},S=JSON.parse(JSON.stringify(initial));function $(id){return document.getElementById(id)}
try{var stored=JSON.parse(localStorage.getItem('ni-tool-v1')||'null');if(stored&&typeof stored=='object')Object.keys(initial).forEach(function(k){if(typeof stored[k]==typeof initial[k]&&Array.isArray(stored[k])==Array.isArray(initial[k])&&(typeof stored[k]!='number'||(isFinite(stored[k])&&Math.abs(stored[k])<=1000000000000))&&(!Array.isArray(stored[k])||stored[k].length<=128))S[k]=stored[k]})}catch(e){}
function save(){try{localStorage.setItem('ni-tool-v1',JSON.stringify(S))}catch(e){}}
function show(v,sub){$('v').textContent=v;$('sub').textContent=sub||'';$('buttons').innerHTML='';$('lines').innerHTML=''}
function line(t){var p=document.createElement('p');p.textContent=t;$('lines').appendChild(p)}
function A(t,fn){var b=document.createElement('button');b.textContent=t;b.onclick=fn;$('buttons').appendChild(b)}
function done(){save();draw()}function fmt(n){n=Math.max(0,Math.ceil(n));return Math.floor(n/60)+':'+('0'+n%60).slice(-2)}
function point(k){S.h.push([S.a,S.b]);S.h=S.h.slice(-10);S[k]=Math.min(999,S[k]+1);done()}function draw(){show(S.a+' : '+S.b,'Команда A · Команда B');A('A +1',function(){point('a')});A('B +1',function(){point('b')});A('Отменить',function(){var h=S.h.pop();if(h){S.a=h[0];S.b=h[1]}done()})}
$('reset').onclick=function(){S=JSON.parse(JSON.stringify(initial));if(typeof phase!='undefined'){clearTimeout(wait);phase=0}done()};draw();if(window.TelegramWidget&&TelegramWidget.ready)TelegramWidget.ready();
</script></body></html>
"""),
            new("habit", "🌱 Привычка", """
<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><style>
html,body{margin:0}body{font:14px/1.4 system-ui,sans-serif;color:var(--tg-theme-text-color,#222);padding:12px 12px max(12px,env(safe-area-inset-bottom))}header{display:flex;align-items:center;justify-content:space-between;gap:8px}h3{margin:0;font-size:16px}#v{font-size:32px;font-weight:700;font-variant-numeric:tabular-nums;margin-top:12px}#sub,small{color:var(--tg-theme-hint-color,#777);font-size:12px}#buttons{display:grid;grid-template-columns:repeat(2,1fr);gap:6px;margin:12px 0}button{border:0;border-radius:12px;padding:9px 8px;background:var(--tg-theme-secondary-bg-color,#eef2f6);color:inherit;font:inherit;min-height:36px}button:active{background:var(--tg-theme-button-color,#3390ec);color:var(--tg-theme-button-text-color,#fff)}#lines{min-height:20px;font-size:12px}#lines p{margin:4px 0;overflow-wrap:anywhere}footer{margin-top:12px;border-top:1px solid #8883;padding-top:6px}header button{font-size:12px} @media(prefers-reduced-motion:reduce){*{animation:none;transition:none}}
</style></head><body><header><h3>🌱 Привычка</h3><button id="reset">↺ Сброс</button></header><div id="v"></div><div id="sub"></div><div id="buttons"></div><div id="lines"></div><footer><small>N-Island · локально, без запросов к ПК</small></footer><script>
var initial={"days":[]},S=JSON.parse(JSON.stringify(initial));function $(id){return document.getElementById(id)}
try{var stored=JSON.parse(localStorage.getItem('ni-tool-v1')||'null');if(stored&&typeof stored=='object')Object.keys(initial).forEach(function(k){if(typeof stored[k]==typeof initial[k]&&Array.isArray(stored[k])==Array.isArray(initial[k])&&(typeof stored[k]!='number'||(isFinite(stored[k])&&Math.abs(stored[k])<=1000000000000))&&(!Array.isArray(stored[k])||stored[k].length<=128))S[k]=stored[k]})}catch(e){}
function save(){try{localStorage.setItem('ni-tool-v1',JSON.stringify(S))}catch(e){}}
function show(v,sub){$('v').textContent=v;$('sub').textContent=sub||'';$('buttons').innerHTML='';$('lines').innerHTML=''}
function line(t){var p=document.createElement('p');p.textContent=t;$('lines').appendChild(p)}
function A(t,fn){var b=document.createElement('button');b.textContent=t;b.onclick=fn;$('buttons').appendChild(b)}
function done(){save();draw()}function fmt(n){n=Math.max(0,Math.ceil(n));return Math.floor(n/60)+':'+('0'+n%60).slice(-2)}
function day(){var d=new Date();return d.getFullYear()+'-'+(d.getMonth()+1)+'-'+d.getDate()}function draw(){var now=day();S.days=S.days.slice(-7);show(S.days.indexOf(now)>=0?'Сегодня ✓':'Сегодня ○','Отметки хранятся на этом устройстве');A('Отметить / отменить',function(){var i=S.days.indexOf(now);if(i<0)S.days.push(now);else S.days.splice(i,1);done()});S.days.forEach(function(d){line('✓ '+d)})}
$('reset').onclick=function(){S=JSON.parse(JSON.stringify(initial));if(typeof phase!='undefined'){clearTimeout(wait);phase=0}done()};draw();if(window.TelegramWidget&&TelegramWidget.ready)TelegramWidget.ready();
</script></body></html>
"""),
            new("packing", "🎒 Чек-лист в дорогу", """
<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><style>
html,body{margin:0}body{font:14px/1.4 system-ui,sans-serif;color:var(--tg-theme-text-color,#222);padding:12px 12px max(12px,env(safe-area-inset-bottom))}header{display:flex;align-items:center;justify-content:space-between;gap:8px}h3{margin:0;font-size:16px}#v{font-size:32px;font-weight:700;font-variant-numeric:tabular-nums;margin-top:12px}#sub,small{color:var(--tg-theme-hint-color,#777);font-size:12px}#buttons{display:grid;grid-template-columns:repeat(2,1fr);gap:6px;margin:12px 0}button{border:0;border-radius:12px;padding:9px 8px;background:var(--tg-theme-secondary-bg-color,#eef2f6);color:inherit;font:inherit;min-height:36px}button:active{background:var(--tg-theme-button-color,#3390ec);color:var(--tg-theme-button-text-color,#fff)}#lines{min-height:20px;font-size:12px}#lines p{margin:4px 0;overflow-wrap:anywhere}footer{margin-top:12px;border-top:1px solid #8883;padding-top:6px}header button{font-size:12px} @media(prefers-reduced-motion:reduce){*{animation:none;transition:none}}
</style></head><body><header><h3>🎒 Чек-лист в дорогу</h3><button id="reset">↺ Сброс</button></header><div id="v"></div><div id="sub"></div><div id="buttons"></div><div id="lines"></div><footer><small>N-Island · локально, без запросов к ПК</small></footer><script>
var initial={"v":0},S=JSON.parse(JSON.stringify(initial));function $(id){return document.getElementById(id)}
try{var stored=JSON.parse(localStorage.getItem('ni-tool-v1')||'null');if(stored&&typeof stored=='object')Object.keys(initial).forEach(function(k){if(typeof stored[k]==typeof initial[k]&&Array.isArray(stored[k])==Array.isArray(initial[k])&&(typeof stored[k]!='number'||(isFinite(stored[k])&&Math.abs(stored[k])<=1000000000000))&&(!Array.isArray(stored[k])||stored[k].length<=128))S[k]=stored[k]})}catch(e){}
function save(){try{localStorage.setItem('ni-tool-v1',JSON.stringify(S))}catch(e){}}
function show(v,sub){$('v').textContent=v;$('sub').textContent=sub||'';$('buttons').innerHTML='';$('lines').innerHTML=''}
function line(t){var p=document.createElement('p');p.textContent=t;$('lines').appendChild(p)}
function A(t,fn){var b=document.createElement('button');b.textContent=t;b.onclick=fn;$('buttons').appendChild(b)}
function done(){save();draw()}function fmt(n){n=Math.max(0,Math.ceil(n));return Math.floor(n/60)+':'+('0'+n%60).slice(-2)}
var list=['Документы','Билеты','Телефон','Зарядка','Ключи','Вода','Лекарства','Наушники'];function draw(){var count=0;list.forEach(function(t,i){if(S.v&(1<<i))count++});show(count+' / '+list.length,'Проверь перед выходом');list.forEach(function(t,i){A((S.v&(1<<i)?'✓ ':'○ ')+t,function(){S.v^=1<<i;done()})})}
$('reset').onclick=function(){S=JSON.parse(JSON.stringify(initial));if(typeof phase!='undefined'){clearTimeout(wait);phase=0}done()};draw();if(window.TelegramWidget&&TelegramWidget.ready)TelegramWidget.ready();
</script></body></html>
"""),
            new("interval", "🏃 Интервалы", """
<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><style>
html,body{margin:0}body{font:14px/1.4 system-ui,sans-serif;color:var(--tg-theme-text-color,#222);padding:12px 12px max(12px,env(safe-area-inset-bottom))}header{display:flex;align-items:center;justify-content:space-between;gap:8px}h3{margin:0;font-size:16px}#v{font-size:32px;font-weight:700;font-variant-numeric:tabular-nums;margin-top:12px}#sub,small{color:var(--tg-theme-hint-color,#777);font-size:12px}#buttons{display:grid;grid-template-columns:repeat(2,1fr);gap:6px;margin:12px 0}button{border:0;border-radius:12px;padding:9px 8px;background:var(--tg-theme-secondary-bg-color,#eef2f6);color:inherit;font:inherit;min-height:36px}button:active{background:var(--tg-theme-button-color,#3390ec);color:var(--tg-theme-button-text-color,#fff)}#lines{min-height:20px;font-size:12px}#lines p{margin:4px 0;overflow-wrap:anywhere}footer{margin-top:12px;border-top:1px solid #8883;padding-top:6px}header button{font-size:12px} @media(prefers-reduced-motion:reduce){*{animation:none;transition:none}}
</style></head><body><header><h3>🏃 Интервалы</h3><button id="reset">↺ Сброс</button></header><div id="v"></div><div id="sub"></div><div id="buttons"></div><div id="lines"></div><footer><small>N-Island · локально, без запросов к ПК</small></footer><script>
var initial={"phase":0,"left":30,"end":0},S=JSON.parse(JSON.stringify(initial));function $(id){return document.getElementById(id)}
try{var stored=JSON.parse(localStorage.getItem('ni-tool-v1')||'null');if(stored&&typeof stored=='object')Object.keys(initial).forEach(function(k){if(typeof stored[k]==typeof initial[k]&&Array.isArray(stored[k])==Array.isArray(initial[k])&&(typeof stored[k]!='number'||(isFinite(stored[k])&&Math.abs(stored[k])<=1000000000000))&&(!Array.isArray(stored[k])||stored[k].length<=128))S[k]=stored[k]})}catch(e){}
function save(){try{localStorage.setItem('ni-tool-v1',JSON.stringify(S))}catch(e){}}
function show(v,sub){$('v').textContent=v;$('sub').textContent=sub||'';$('buttons').innerHTML='';$('lines').innerHTML=''}
function line(t){var p=document.createElement('p');p.textContent=t;$('lines').appendChild(p)}
function A(t,fn){var b=document.createElement('button');b.textContent=t;b.onclick=fn;$('buttons').appendChild(b)}
function done(){save();draw()}function fmt(n){n=Math.max(0,Math.ceil(n));return Math.floor(n/60)+':'+('0'+n%60).slice(-2)}
function tick(){if(!S.end)return;var now=Date.now();while(S.end<=now&&S.phase<16){S.phase++;S.end+=((S.phase%2)?10:30)*1000}if(S.phase>=16){S.end=0;S.left=0}else S.left=Math.ceil((S.end-now)/1000);draw();save()}function draw(){show(fmt(S.end?Math.max(0,(S.end-Date.now())/1000):S.left),S.phase>=16?'Готово':(S.phase%2?'Отдых 10 с':'Движение 30 с')+' · круг '+(1+Math.floor(S.phase/2))+'/8');A(S.end?'Пауза':'Старт',function(){if(S.phase>=16)return;if(S.end){S.left=Math.max(0,(S.end-Date.now())/1000);S.end=0}else S.end=Date.now()+S.left*1000;done()});line('Без фонового сигнала. При боли остановись.')}setInterval(tick,500);
$('reset').onclick=function(){S=JSON.parse(JSON.stringify(initial));if(typeof phase!='undefined'){clearTimeout(wait);phase=0}done()};draw();if(window.TelegramWidget&&TelegramWidget.ready)TelegramWidget.ready();
</script></body></html>
"""),
            new("eyes", "👀 Отдых для глаз", """
<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><style>
html,body{margin:0}body{font:14px/1.4 system-ui,sans-serif;color:var(--tg-theme-text-color,#222);padding:12px 12px max(12px,env(safe-area-inset-bottom))}header{display:flex;align-items:center;justify-content:space-between;gap:8px}h3{margin:0;font-size:16px}#v{font-size:32px;font-weight:700;font-variant-numeric:tabular-nums;margin-top:12px}#sub,small{color:var(--tg-theme-hint-color,#777);font-size:12px}#buttons{display:grid;grid-template-columns:repeat(2,1fr);gap:6px;margin:12px 0}button{border:0;border-radius:12px;padding:9px 8px;background:var(--tg-theme-secondary-bg-color,#eef2f6);color:inherit;font:inherit;min-height:36px}button:active{background:var(--tg-theme-button-color,#3390ec);color:var(--tg-theme-button-text-color,#fff)}#lines{min-height:20px;font-size:12px}#lines p{margin:4px 0;overflow-wrap:anywhere}footer{margin-top:12px;border-top:1px solid #8883;padding-top:6px}header button{font-size:12px} @media(prefers-reduced-motion:reduce){*{animation:none;transition:none}}
</style></head><body><header><h3>👀 Отдых для глаз</h3><button id="reset">↺ Сброс</button></header><div id="v"></div><div id="sub"></div><div id="buttons"></div><div id="lines"></div><footer><small>N-Island · локально, без запросов к ПК</small></footer><script>
var initial={"end":0},S=JSON.parse(JSON.stringify(initial));function $(id){return document.getElementById(id)}
try{var stored=JSON.parse(localStorage.getItem('ni-tool-v1')||'null');if(stored&&typeof stored=='object')Object.keys(initial).forEach(function(k){if(typeof stored[k]==typeof initial[k]&&Array.isArray(stored[k])==Array.isArray(initial[k])&&(typeof stored[k]!='number'||(isFinite(stored[k])&&Math.abs(stored[k])<=1000000000000))&&(!Array.isArray(stored[k])||stored[k].length<=128))S[k]=stored[k]})}catch(e){}
function save(){try{localStorage.setItem('ni-tool-v1',JSON.stringify(S))}catch(e){}}
function show(v,sub){$('v').textContent=v;$('sub').textContent=sub||'';$('buttons').innerHTML='';$('lines').innerHTML=''}
function line(t){var p=document.createElement('p');p.textContent=t;$('lines').appendChild(p)}
function A(t,fn){var b=document.createElement('button');b.textContent=t;b.onclick=fn;$('buttons').appendChild(b)}
function done(){save();draw()}function fmt(n){n=Math.max(0,Math.ceil(n));return Math.floor(n/60)+':'+('0'+n%60).slice(-2)}
function draw(){var left=S.end?Math.max(0,Math.ceil((S.end-Date.now())/1000)):20;show(left+' с','Посмотри вдаль, расслабь взгляд');A('Начать 20 секунд',function(){S.end=Date.now()+20000;done()});if(S.end&&left==0)line('✓ Перерыв закончен');line('Это подсказка, не медицинская рекомендация.')}setInterval(draw,1000);
$('reset').onclick=function(){S=JSON.parse(JSON.stringify(initial));if(typeof phase!='undefined'){clearTimeout(wait);phase=0}done()};draw();if(window.TelegramWidget&&TelegramWidget.ready)TelegramWidget.ready();
</script></body></html>
"""),
            new("tip", "🧾 Чаевые и счёт", """
<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><style>
html,body{margin:0}body{font:14px/1.4 system-ui,sans-serif;color:var(--tg-theme-text-color,#222);padding:12px 12px max(12px,env(safe-area-inset-bottom))}header{display:flex;align-items:center;justify-content:space-between;gap:8px}h3{margin:0;font-size:16px}#v{font-size:32px;font-weight:700;font-variant-numeric:tabular-nums;margin-top:12px}#sub,small{color:var(--tg-theme-hint-color,#777);font-size:12px}#buttons{display:grid;grid-template-columns:repeat(2,1fr);gap:6px;margin:12px 0}button{border:0;border-radius:12px;padding:9px 8px;background:var(--tg-theme-secondary-bg-color,#eef2f6);color:inherit;font:inherit;min-height:36px}button:active{background:var(--tg-theme-button-color,#3390ec);color:var(--tg-theme-button-text-color,#fff)}#lines{min-height:20px;font-size:12px}#lines p{margin:4px 0;overflow-wrap:anywhere}footer{margin-top:12px;border-top:1px solid #8883;padding-top:6px}header button{font-size:12px} @media(prefers-reduced-motion:reduce){*{animation:none;transition:none}}
</style></head><body><header><h3>🧾 Чаевые и счёт</h3><button id="reset">↺ Сброс</button></header><div id="v"></div><div id="sub"></div><div id="buttons"></div><div id="lines"></div><footer><small>N-Island · локально, без запросов к ПК</small></footer><script>
var initial={"bill":100,"pct":10,"people":1},S=JSON.parse(JSON.stringify(initial));function $(id){return document.getElementById(id)}
try{var stored=JSON.parse(localStorage.getItem('ni-tool-v1')||'null');if(stored&&typeof stored=='object')Object.keys(initial).forEach(function(k){if(typeof stored[k]==typeof initial[k]&&Array.isArray(stored[k])==Array.isArray(initial[k])&&(typeof stored[k]!='number'||(isFinite(stored[k])&&Math.abs(stored[k])<=1000000000000))&&(!Array.isArray(stored[k])||stored[k].length<=128))S[k]=stored[k]})}catch(e){}
function save(){try{localStorage.setItem('ni-tool-v1',JSON.stringify(S))}catch(e){}}
function show(v,sub){$('v').textContent=v;$('sub').textContent=sub||'';$('buttons').innerHTML='';$('lines').innerHTML=''}
function line(t){var p=document.createElement('p');p.textContent=t;$('lines').appendChild(p)}
function A(t,fn){var b=document.createElement('button');b.textContent=t;b.onclick=fn;$('buttons').appendChild(b)}
function done(){save();draw()}function fmt(n){n=Math.max(0,Math.ceil(n));return Math.floor(n/60)+':'+('0'+n%60).slice(-2)}
function draw(){show(((S.bill*(1+S.pct/100))/S.people).toFixed(2),'На человека · сумма '+S.bill+' · '+S.pct+'% · людей '+S.people);A('Счёт −10',function(){S.bill=Math.max(0,S.bill-10);done()});A('Счёт +10',function(){S.bill=Math.min(100000,S.bill+10);done()});A('Чаевые +5%',function(){S.pct=(S.pct+5)%35;done()});A('Людей +1',function(){S.people=S.people%20+1;done()});line('Чаевые: '+(S.bill*S.pct/100).toFixed(2)+' · итого '+(S.bill*(1+S.pct/100)).toFixed(2))}
$('reset').onclick=function(){S=JSON.parse(JSON.stringify(initial));if(typeof phase!='undefined'){clearTimeout(wait);phase=0}done()};draw();if(window.TelegramWidget&&TelegramWidget.ready)TelegramWidget.ready();
</script></body></html>
"""),
            new("discount", "🏷 Скидка", """
<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><style>
html,body{margin:0}body{font:14px/1.4 system-ui,sans-serif;color:var(--tg-theme-text-color,#222);padding:12px 12px max(12px,env(safe-area-inset-bottom))}header{display:flex;align-items:center;justify-content:space-between;gap:8px}h3{margin:0;font-size:16px}#v{font-size:32px;font-weight:700;font-variant-numeric:tabular-nums;margin-top:12px}#sub,small{color:var(--tg-theme-hint-color,#777);font-size:12px}#buttons{display:grid;grid-template-columns:repeat(2,1fr);gap:6px;margin:12px 0}button{border:0;border-radius:12px;padding:9px 8px;background:var(--tg-theme-secondary-bg-color,#eef2f6);color:inherit;font:inherit;min-height:36px}button:active{background:var(--tg-theme-button-color,#3390ec);color:var(--tg-theme-button-text-color,#fff)}#lines{min-height:20px;font-size:12px}#lines p{margin:4px 0;overflow-wrap:anywhere}footer{margin-top:12px;border-top:1px solid #8883;padding-top:6px}header button{font-size:12px} @media(prefers-reduced-motion:reduce){*{animation:none;transition:none}}
</style></head><body><header><h3>🏷 Скидка</h3><button id="reset">↺ Сброс</button></header><div id="v"></div><div id="sub"></div><div id="buttons"></div><div id="lines"></div><footer><small>N-Island · локально, без запросов к ПК</small></footer><script>
var initial={"price":100,"pct":10},S=JSON.parse(JSON.stringify(initial));function $(id){return document.getElementById(id)}
try{var stored=JSON.parse(localStorage.getItem('ni-tool-v1')||'null');if(stored&&typeof stored=='object')Object.keys(initial).forEach(function(k){if(typeof stored[k]==typeof initial[k]&&Array.isArray(stored[k])==Array.isArray(initial[k])&&(typeof stored[k]!='number'||(isFinite(stored[k])&&Math.abs(stored[k])<=1000000000000))&&(!Array.isArray(stored[k])||stored[k].length<=128))S[k]=stored[k]})}catch(e){}
function save(){try{localStorage.setItem('ni-tool-v1',JSON.stringify(S))}catch(e){}}
function show(v,sub){$('v').textContent=v;$('sub').textContent=sub||'';$('buttons').innerHTML='';$('lines').innerHTML=''}
function line(t){var p=document.createElement('p');p.textContent=t;$('lines').appendChild(p)}
function A(t,fn){var b=document.createElement('button');b.textContent=t;b.onclick=fn;$('buttons').appendChild(b)}
function done(){save();draw()}function fmt(n){n=Math.max(0,Math.ceil(n));return Math.floor(n/60)+':'+('0'+n%60).slice(-2)}
function draw(){show((S.price*(1-S.pct/100)).toFixed(2),'Цена '+S.price+' · скидка '+S.pct+'%');A('Цена −10',function(){S.price=Math.max(0,S.price-10);done()});A('Цена +10',function(){S.price=Math.min(100000,S.price+10);done()});A('Скидка −5%',function(){S.pct=Math.max(0,S.pct-5);done()});A('Скидка +5%',function(){S.pct=Math.min(100,S.pct+5);done()});line('Экономия '+(S.price*S.pct/100).toFixed(2))}
$('reset').onclick=function(){S=JSON.parse(JSON.stringify(initial));if(typeof phase!='undefined'){clearTimeout(wait);phase=0}done()};draw();if(window.TelegramWidget&&TelegramWidget.ready)TelegramWidget.ready();
</script></body></html>
"""),
            new("units", "📏 Конвертер единиц", """
<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><style>
html,body{margin:0}body{font:14px/1.4 system-ui,sans-serif;color:var(--tg-theme-text-color,#222);padding:12px 12px max(12px,env(safe-area-inset-bottom))}header{display:flex;align-items:center;justify-content:space-between;gap:8px}h3{margin:0;font-size:16px}#v{font-size:32px;font-weight:700;font-variant-numeric:tabular-nums;margin-top:12px}#sub,small{color:var(--tg-theme-hint-color,#777);font-size:12px}#buttons{display:grid;grid-template-columns:repeat(2,1fr);gap:6px;margin:12px 0}button{border:0;border-radius:12px;padding:9px 8px;background:var(--tg-theme-secondary-bg-color,#eef2f6);color:inherit;font:inherit;min-height:36px}button:active{background:var(--tg-theme-button-color,#3390ec);color:var(--tg-theme-button-text-color,#fff)}#lines{min-height:20px;font-size:12px}#lines p{margin:4px 0;overflow-wrap:anywhere}footer{margin-top:12px;border-top:1px solid #8883;padding-top:6px}header button{font-size:12px} @media(prefers-reduced-motion:reduce){*{animation:none;transition:none}}
</style></head><body><header><h3>📏 Конвертер единиц</h3><button id="reset">↺ Сброс</button></header><div id="v"></div><div id="sub"></div><div id="buttons"></div><div id="lines"></div><footer><small>N-Island · локально, без запросов к ПК</small></footer><script>
var initial={"value":10,"mode":0},S=JSON.parse(JSON.stringify(initial));function $(id){return document.getElementById(id)}
try{var stored=JSON.parse(localStorage.getItem('ni-tool-v1')||'null');if(stored&&typeof stored=='object')Object.keys(initial).forEach(function(k){if(typeof stored[k]==typeof initial[k]&&Array.isArray(stored[k])==Array.isArray(initial[k])&&(typeof stored[k]!='number'||(isFinite(stored[k])&&Math.abs(stored[k])<=1000000000000))&&(!Array.isArray(stored[k])||stored[k].length<=128))S[k]=stored[k]})}catch(e){}
function save(){try{localStorage.setItem('ni-tool-v1',JSON.stringify(S))}catch(e){}}
function show(v,sub){$('v').textContent=v;$('sub').textContent=sub||'';$('buttons').innerHTML='';$('lines').innerHTML=''}
function line(t){var p=document.createElement('p');p.textContent=t;$('lines').appendChild(p)}
function A(t,fn){var b=document.createElement('button');b.textContent=t;b.onclick=fn;$('buttons').appendChild(b)}
function done(){save();draw()}function fmt(n){n=Math.max(0,Math.ceil(n));return Math.floor(n/60)+':'+('0'+n%60).slice(-2)}
var modes=['км → мили','кг → фунты','°C → °F'];function draw(){var v=S.mode==0?S.value/1.609344:S.mode==1?S.value/0.45359237:S.value*1.8+32;show(v.toFixed(3),S.value+' · '+modes[S.mode]);A('−1',function(){S.value=Math.max(S.mode==2?-273.15:0,S.value-1);done()});A('+1',function(){S.value=Math.min(100000,S.value+1);done()});A('×10',function(){S.value=Math.min(100000,S.value*10);done()});A('Единицы',function(){S.mode=(S.mode+1)%3;S.value=10;done()})}
$('reset').onclick=function(){S=JSON.parse(JSON.stringify(initial));if(typeof phase!='undefined'){clearTimeout(wait);phase=0}done()};draw();if(window.TelegramWidget&&TelegramWidget.ready)TelegramWidget.ready();
</script></body></html>
"""),
            new("percent", "％ Проценты", """
<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><style>
html,body{margin:0}body{font:14px/1.4 system-ui,sans-serif;color:var(--tg-theme-text-color,#222);padding:12px 12px max(12px,env(safe-area-inset-bottom))}header{display:flex;align-items:center;justify-content:space-between;gap:8px}h3{margin:0;font-size:16px}#v{font-size:32px;font-weight:700;font-variant-numeric:tabular-nums;margin-top:12px}#sub,small{color:var(--tg-theme-hint-color,#777);font-size:12px}#buttons{display:grid;grid-template-columns:repeat(2,1fr);gap:6px;margin:12px 0}button{border:0;border-radius:12px;padding:9px 8px;background:var(--tg-theme-secondary-bg-color,#eef2f6);color:inherit;font:inherit;min-height:36px}button:active{background:var(--tg-theme-button-color,#3390ec);color:var(--tg-theme-button-text-color,#fff)}#lines{min-height:20px;font-size:12px}#lines p{margin:4px 0;overflow-wrap:anywhere}footer{margin-top:12px;border-top:1px solid #8883;padding-top:6px}header button{font-size:12px} @media(prefers-reduced-motion:reduce){*{animation:none;transition:none}}
</style></head><body><header><h3>％ Проценты</h3><button id="reset">↺ Сброс</button></header><div id="v"></div><div id="sub"></div><div id="buttons"></div><div id="lines"></div><footer><small>N-Island · локально, без запросов к ПК</small></footer><script>
var initial={"base":100,"pct":15},S=JSON.parse(JSON.stringify(initial));function $(id){return document.getElementById(id)}
try{var stored=JSON.parse(localStorage.getItem('ni-tool-v1')||'null');if(stored&&typeof stored=='object')Object.keys(initial).forEach(function(k){if(typeof stored[k]==typeof initial[k]&&Array.isArray(stored[k])==Array.isArray(initial[k])&&(typeof stored[k]!='number'||(isFinite(stored[k])&&Math.abs(stored[k])<=1000000000000))&&(!Array.isArray(stored[k])||stored[k].length<=128))S[k]=stored[k]})}catch(e){}
function save(){try{localStorage.setItem('ni-tool-v1',JSON.stringify(S))}catch(e){}}
function show(v,sub){$('v').textContent=v;$('sub').textContent=sub||'';$('buttons').innerHTML='';$('lines').innerHTML=''}
function line(t){var p=document.createElement('p');p.textContent=t;$('lines').appendChild(p)}
function A(t,fn){var b=document.createElement('button');b.textContent=t;b.onclick=fn;$('buttons').appendChild(b)}
function done(){save();draw()}function fmt(n){n=Math.max(0,Math.ceil(n));return Math.floor(n/60)+':'+('0'+n%60).slice(-2)}
function draw(){show((S.base*S.pct/100).toFixed(2),S.pct+'% от '+S.base);A('База −10',function(){S.base=Math.max(0,S.base-10);done()});A('База +10',function(){S.base=Math.min(100000,S.base+10);done()});A('Процент −1',function(){S.pct=Math.max(0,S.pct-1);done()});A('Процент +1',function(){S.pct=Math.min(1000,S.pct+1);done()});line('С прибавкой '+(S.base*(1+S.pct/100)).toFixed(2))}
$('reset').onclick=function(){S=JSON.parse(JSON.stringify(initial));if(typeof phase!='undefined'){clearTimeout(wait);phase=0}done()};draw();if(window.TelegramWidget&&TelegramWidget.ready)TelegramWidget.ready();
</script></body></html>
"""),
            new("random", "🎯 Случайное число", """
<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><style>
html,body{margin:0}body{font:14px/1.4 system-ui,sans-serif;color:var(--tg-theme-text-color,#222);padding:12px 12px max(12px,env(safe-area-inset-bottom))}header{display:flex;align-items:center;justify-content:space-between;gap:8px}h3{margin:0;font-size:16px}#v{font-size:32px;font-weight:700;font-variant-numeric:tabular-nums;margin-top:12px}#sub,small{color:var(--tg-theme-hint-color,#777);font-size:12px}#buttons{display:grid;grid-template-columns:repeat(2,1fr);gap:6px;margin:12px 0}button{border:0;border-radius:12px;padding:9px 8px;background:var(--tg-theme-secondary-bg-color,#eef2f6);color:inherit;font:inherit;min-height:36px}button:active{background:var(--tg-theme-button-color,#3390ec);color:var(--tg-theme-button-text-color,#fff)}#lines{min-height:20px;font-size:12px}#lines p{margin:4px 0;overflow-wrap:anywhere}footer{margin-top:12px;border-top:1px solid #8883;padding-top:6px}header button{font-size:12px} @media(prefers-reduced-motion:reduce){*{animation:none;transition:none}}
</style></head><body><header><h3>🎯 Случайное число</h3><button id="reset">↺ Сброс</button></header><div id="v"></div><div id="sub"></div><div id="buttons"></div><div id="lines"></div><footer><small>N-Island · локально, без запросов к ПК</small></footer><script>
var initial={"max":6,"value":"—"},S=JSON.parse(JSON.stringify(initial));function $(id){return document.getElementById(id)}
try{var stored=JSON.parse(localStorage.getItem('ni-tool-v1')||'null');if(stored&&typeof stored=='object')Object.keys(initial).forEach(function(k){if(typeof stored[k]==typeof initial[k]&&Array.isArray(stored[k])==Array.isArray(initial[k])&&(typeof stored[k]!='number'||(isFinite(stored[k])&&Math.abs(stored[k])<=1000000000000))&&(!Array.isArray(stored[k])||stored[k].length<=128))S[k]=stored[k]})}catch(e){}
function save(){try{localStorage.setItem('ni-tool-v1',JSON.stringify(S))}catch(e){}}
function show(v,sub){$('v').textContent=v;$('sub').textContent=sub||'';$('buttons').innerHTML='';$('lines').innerHTML=''}
function line(t){var p=document.createElement('p');p.textContent=t;$('lines').appendChild(p)}
function A(t,fn){var b=document.createElement('button');b.textContent=t;b.onclick=fn;$('buttons').appendChild(b)}
function done(){save();draw()}function fmt(n){n=Math.max(0,Math.ceil(n));return Math.floor(n/60)+':'+('0'+n%60).slice(-2)}
function draw(){show(S.value,'Целое от 1 до '+S.max);[6,20,100,1000].forEach(function(n){A('1…'+n,function(){S.max=n;done()})});A('Выбрать',function(){var a=new Uint32Array(1),lim=Math.floor(4294967296/S.max)*S.max;do{crypto.getRandomValues(a)}while(a[0]>=lim);S.value=1+a[0]%S.max;done()})}
$('reset').onclick=function(){S=JSON.parse(JSON.stringify(initial));if(typeof phase!='undefined'){clearTimeout(wait);phase=0}done()};draw();if(window.TelegramWidget&&TelegramWidget.ready)TelegramWidget.ready();
</script></body></html>
"""),
            new("reaction", "⚡ Реакция", """
<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><style>
html,body{margin:0}body{font:14px/1.4 system-ui,sans-serif;color:var(--tg-theme-text-color,#222);padding:12px 12px max(12px,env(safe-area-inset-bottom))}header{display:flex;align-items:center;justify-content:space-between;gap:8px}h3{margin:0;font-size:16px}#v{font-size:32px;font-weight:700;font-variant-numeric:tabular-nums;margin-top:12px}#sub,small{color:var(--tg-theme-hint-color,#777);font-size:12px}#buttons{display:grid;grid-template-columns:repeat(2,1fr);gap:6px;margin:12px 0}button{border:0;border-radius:12px;padding:9px 8px;background:var(--tg-theme-secondary-bg-color,#eef2f6);color:inherit;font:inherit;min-height:36px}button:active{background:var(--tg-theme-button-color,#3390ec);color:var(--tg-theme-button-text-color,#fff)}#lines{min-height:20px;font-size:12px}#lines p{margin:4px 0;overflow-wrap:anywhere}footer{margin-top:12px;border-top:1px solid #8883;padding-top:6px}header button{font-size:12px} @media(prefers-reduced-motion:reduce){*{animation:none;transition:none}}
</style></head><body><header><h3>⚡ Реакция</h3><button id="reset">↺ Сброс</button></header><div id="v"></div><div id="sub"></div><div id="buttons"></div><div id="lines"></div><footer><small>N-Island · локально, без запросов к ПК</small></footer><script>
var initial={"best":0},S=JSON.parse(JSON.stringify(initial));function $(id){return document.getElementById(id)}
try{var stored=JSON.parse(localStorage.getItem('ni-tool-v1')||'null');if(stored&&typeof stored=='object')Object.keys(initial).forEach(function(k){if(typeof stored[k]==typeof initial[k]&&Array.isArray(stored[k])==Array.isArray(initial[k])&&(typeof stored[k]!='number'||(isFinite(stored[k])&&Math.abs(stored[k])<=1000000000000))&&(!Array.isArray(stored[k])||stored[k].length<=128))S[k]=stored[k]})}catch(e){}
function save(){try{localStorage.setItem('ni-tool-v1',JSON.stringify(S))}catch(e){}}
function show(v,sub){$('v').textContent=v;$('sub').textContent=sub||'';$('buttons').innerHTML='';$('lines').innerHTML=''}
function line(t){var p=document.createElement('p');p.textContent=t;$('lines').appendChild(p)}
function A(t,fn){var b=document.createElement('button');b.textContent=t;b.onclick=fn;$('buttons').appendChild(b)}
function done(){save();draw()}function fmt(n){n=Math.max(0,Math.ceil(n));return Math.floor(n/60)+':'+('0'+n%60).slice(-2)}
var phase=0,start=0,wait=0;function draw(){show(phase==2?'ЖМИ!':phase==1?'Жди…':'Готов?',S.best?'Лучшее '+S.best+' мс':'Нажми старт, дождись сигнала');A(phase?'Нажать':'Старт',function(){if(phase==2){var dt=Date.now()-start;if(!S.best||dt<S.best)S.best=dt;phase=0;done();line(dt+' мс')}else if(phase==1){clearTimeout(wait);phase=0;draw();line('Рано! Попробуй снова')}else{phase=1;draw();wait=setTimeout(function(){phase=2;start=Date.now();draw()},1200+Math.random()*2000)}})}
$('reset').onclick=function(){S=JSON.parse(JSON.stringify(initial));if(typeof phase!='undefined'){clearTimeout(wait);phase=0}done()};draw();if(window.TelegramWidget&&TelegramWidget.ready)TelegramWidget.ready();
</script></body></html>
"""),
            new("memory", "🧠 Найди пары", """
<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><style>
html,body{margin:0}body{font:14px/1.4 system-ui,sans-serif;color:var(--tg-theme-text-color,#222);padding:12px 12px max(12px,env(safe-area-inset-bottom))}header{display:flex;align-items:center;justify-content:space-between;gap:8px}h3{margin:0;font-size:16px}#v{font-size:32px;font-weight:700;font-variant-numeric:tabular-nums;margin-top:12px}#sub,small{color:var(--tg-theme-hint-color,#777);font-size:12px}#buttons{display:grid;grid-template-columns:repeat(2,1fr);gap:6px;margin:12px 0}button{border:0;border-radius:12px;padding:9px 8px;background:var(--tg-theme-secondary-bg-color,#eef2f6);color:inherit;font:inherit;min-height:36px}button:active{background:var(--tg-theme-button-color,#3390ec);color:var(--tg-theme-button-text-color,#fff)}#lines{min-height:20px;font-size:12px}#lines p{margin:4px 0;overflow-wrap:anywhere}footer{margin-top:12px;border-top:1px solid #8883;padding-top:6px}header button{font-size:12px} @media(prefers-reduced-motion:reduce){*{animation:none;transition:none}}
</style></head><body><header><h3>🧠 Найди пары</h3><button id="reset">↺ Сброс</button></header><div id="v"></div><div id="sub"></div><div id="buttons"></div><div id="lines"></div><footer><small>N-Island · локально, без запросов к ПК</small></footer><script>
var initial={"cards":[],"open":[],"found":[],"moves":0},S=JSON.parse(JSON.stringify(initial));function $(id){return document.getElementById(id)}
try{var stored=JSON.parse(localStorage.getItem('ni-tool-v1')||'null');if(stored&&typeof stored=='object')Object.keys(initial).forEach(function(k){if(typeof stored[k]==typeof initial[k]&&Array.isArray(stored[k])==Array.isArray(initial[k])&&(typeof stored[k]!='number'||(isFinite(stored[k])&&Math.abs(stored[k])<=1000000000000))&&(!Array.isArray(stored[k])||stored[k].length<=128))S[k]=stored[k]})}catch(e){}
function save(){try{localStorage.setItem('ni-tool-v1',JSON.stringify(S))}catch(e){}}
function show(v,sub){$('v').textContent=v;$('sub').textContent=sub||'';$('buttons').innerHTML='';$('lines').innerHTML=''}
function line(t){var p=document.createElement('p');p.textContent=t;$('lines').appendChild(p)}
function A(t,fn){var b=document.createElement('button');b.textContent=t;b.onclick=fn;$('buttons').appendChild(b)}
function done(){save();draw()}function fmt(n){n=Math.max(0,Math.ceil(n));return Math.floor(n/60)+':'+('0'+n%60).slice(-2)}
var faces=['🌙','🍀','🔥','⭐'];function init(){if(S.cards.length!=8){S.cards=[0,0,1,1,2,2,3,3];for(var i=7;i>0;i--){var j=Math.floor(Math.random()*(i+1)),v=S.cards[i];S.cards[i]=S.cards[j];S.cards[j]=v}}}function draw(){init();show(S.found.length==8?'Победа!':S.found.length/2+' / 4 пары','Ходов '+S.moves);S.cards.forEach(function(v,i){var on=S.open.indexOf(i)>=0||S.found.indexOf(i)>=0;A(on?faces[v]:'?',function(){if(on)return;if(S.open.length==2)S.open=[];S.open.push(i);if(S.open.length==2){S.moves++;if(S.cards[S.open[0]]==S.cards[S.open[1]]){S.found=S.found.concat(S.open);S.open=[]}}done()})})}
$('reset').onclick=function(){S=JSON.parse(JSON.stringify(initial));if(typeof phase!='undefined'){clearTimeout(wait);phase=0}done()};draw();if(window.TelegramWidget&&TelegramWidget.ready)TelegramWidget.ready();
</script></body></html>
"""),
            new("palette", "🎨 Палитра", """
<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><style>
html,body{margin:0}body{font:14px/1.4 system-ui,sans-serif;color:var(--tg-theme-text-color,#222);padding:12px 12px max(12px,env(safe-area-inset-bottom))}header{display:flex;align-items:center;justify-content:space-between;gap:8px}h3{margin:0;font-size:16px}#v{font-size:32px;font-weight:700;font-variant-numeric:tabular-nums;margin-top:12px}#sub,small{color:var(--tg-theme-hint-color,#777);font-size:12px}#buttons{display:grid;grid-template-columns:repeat(2,1fr);gap:6px;margin:12px 0}button{border:0;border-radius:12px;padding:9px 8px;background:var(--tg-theme-secondary-bg-color,#eef2f6);color:inherit;font:inherit;min-height:36px}button:active{background:var(--tg-theme-button-color,#3390ec);color:var(--tg-theme-button-text-color,#fff)}#lines{min-height:20px;font-size:12px}#lines p{margin:4px 0;overflow-wrap:anywhere}footer{margin-top:12px;border-top:1px solid #8883;padding-top:6px}header button{font-size:12px} @media(prefers-reduced-motion:reduce){*{animation:none;transition:none}}
</style></head><body><header><h3>🎨 Палитра</h3><button id="reset">↺ Сброс</button></header><div id="v"></div><div id="sub"></div><div id="buttons"></div><div id="lines"></div><footer><small>N-Island · локально, без запросов к ПК</small></footer><script>
var initial={"i":0},S=JSON.parse(JSON.stringify(initial));function $(id){return document.getElementById(id)}
try{var stored=JSON.parse(localStorage.getItem('ni-tool-v1')||'null');if(stored&&typeof stored=='object')Object.keys(initial).forEach(function(k){if(typeof stored[k]==typeof initial[k]&&Array.isArray(stored[k])==Array.isArray(initial[k])&&(typeof stored[k]!='number'||(isFinite(stored[k])&&Math.abs(stored[k])<=1000000000000))&&(!Array.isArray(stored[k])||stored[k].length<=128))S[k]=stored[k]})}catch(e){}
function save(){try{localStorage.setItem('ni-tool-v1',JSON.stringify(S))}catch(e){}}
function show(v,sub){$('v').textContent=v;$('sub').textContent=sub||'';$('buttons').innerHTML='';$('lines').innerHTML=''}
function line(t){var p=document.createElement('p');p.textContent=t;$('lines').appendChild(p)}
function A(t,fn){var b=document.createElement('button');b.textContent=t;b.onclick=fn;$('buttons').appendChild(b)}
function done(){save();draw()}function fmt(n){n=Math.max(0,Math.ceil(n));return Math.floor(n/60)+':'+('0'+n%60).slice(-2)}
var colors=['#3390ec','#30a46c','#d64045','#a65cdb','#ca8a04','#0891b2'];function draw(){var c=colors[S.i];show(c,'Цвет интерфейса');$('v').style.color=c;A('Следующий',function(){S.i=(S.i+1)%colors.length;done()});A('Копировать HEX',function(){var T=window.TelegramWidget;if(T&&T.copyText){try{T.copyText(c);line('✓ Скопировано')}catch(e){line('Копирование недоступно')}}else line(c)})}
$('reset').onclick=function(){S=JSON.parse(JSON.stringify(initial));if(typeof phase!='undefined'){clearTimeout(wait);phase=0}done()};draw();if(window.TelegramWidget&&TelegramWidget.ready)TelegramWidget.ready();
</script></body></html>
"""),
            new("budget", "💰 Карманный бюджет", """
<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><style>
html,body{margin:0}body{font:14px/1.4 system-ui,sans-serif;color:var(--tg-theme-text-color,#222);padding:12px 12px max(12px,env(safe-area-inset-bottom))}header{display:flex;align-items:center;justify-content:space-between;gap:8px}h3{margin:0;font-size:16px}#v{font-size:32px;font-weight:700;font-variant-numeric:tabular-nums;margin-top:12px}#sub,small{color:var(--tg-theme-hint-color,#777);font-size:12px}#buttons{display:grid;grid-template-columns:repeat(2,1fr);gap:6px;margin:12px 0}button{border:0;border-radius:12px;padding:9px 8px;background:var(--tg-theme-secondary-bg-color,#eef2f6);color:inherit;font:inherit;min-height:36px}button:active{background:var(--tg-theme-button-color,#3390ec);color:var(--tg-theme-button-text-color,#fff)}#lines{min-height:20px;font-size:12px}#lines p{margin:4px 0;overflow-wrap:anywhere}footer{margin-top:12px;border-top:1px solid #8883;padding-top:6px}header button{font-size:12px} @media(prefers-reduced-motion:reduce){*{animation:none;transition:none}}
</style></head><body><header><h3>💰 Карманный бюджет</h3><button id="reset">↺ Сброс</button></header><div id="v"></div><div id="sub"></div><div id="buttons"></div><div id="lines"></div><footer><small>N-Island · локально, без запросов к ПК</small></footer><script>
var initial={"sum":0,"history":[]},S=JSON.parse(JSON.stringify(initial));function $(id){return document.getElementById(id)}
try{var stored=JSON.parse(localStorage.getItem('ni-tool-v1')||'null');if(stored&&typeof stored=='object')Object.keys(initial).forEach(function(k){if(typeof stored[k]==typeof initial[k]&&Array.isArray(stored[k])==Array.isArray(initial[k])&&(typeof stored[k]!='number'||(isFinite(stored[k])&&Math.abs(stored[k])<=1000000000000))&&(!Array.isArray(stored[k])||stored[k].length<=128))S[k]=stored[k]})}catch(e){}
function save(){try{localStorage.setItem('ni-tool-v1',JSON.stringify(S))}catch(e){}}
function show(v,sub){$('v').textContent=v;$('sub').textContent=sub||'';$('buttons').innerHTML='';$('lines').innerHTML=''}
function line(t){var p=document.createElement('p');p.textContent=t;$('lines').appendChild(p)}
function A(t,fn){var b=document.createElement('button');b.textContent=t;b.onclick=fn;$('buttons').appendChild(b)}
function done(){save();draw()}function fmt(n){n=Math.max(0,Math.ceil(n));return Math.floor(n/60)+':'+('0'+n%60).slice(-2)}
function add(n){S.history.push(S.sum);S.history=S.history.slice(-10);S.sum=Math.max(-1000000,Math.min(1000000,S.sum+n));done()}function draw(){show(S.sum,'Условные единицы · только на этом устройстве');[-100,-10,10,100].forEach(function(n){A((n>0?'+':'')+n,function(){add(n)})});A('Отменить',function(){if(S.history.length)S.sum=S.history.pop();done()});line('Не подключён к банку. Это ручной счётчик.')}
$('reset').onclick=function(){S=JSON.parse(JSON.stringify(initial));if(typeof phase!='undefined'){clearTimeout(wait);phase=0}done()};draw();if(window.TelegramWidget&&TelegramWidget.ready)TelegramWidget.ready();
</script></body></html>
"""),
            new("dayprogress", "📆 Прогресс дня", """
<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><style>
html,body{margin:0}body{font:14px/1.4 system-ui,sans-serif;color:var(--tg-theme-text-color,#222);padding:12px 12px max(12px,env(safe-area-inset-bottom))}header{display:flex;align-items:center;justify-content:space-between;gap:8px}h3{margin:0;font-size:16px}#v{font-size:32px;font-weight:700;font-variant-numeric:tabular-nums;margin-top:12px}#sub,small{color:var(--tg-theme-hint-color,#777);font-size:12px}#buttons{display:grid;grid-template-columns:repeat(2,1fr);gap:6px;margin:12px 0}button{border:0;border-radius:12px;padding:9px 8px;background:var(--tg-theme-secondary-bg-color,#eef2f6);color:inherit;font:inherit;min-height:36px}button:active{background:var(--tg-theme-button-color,#3390ec);color:var(--tg-theme-button-text-color,#fff)}#lines{min-height:20px;font-size:12px}#lines p{margin:4px 0;overflow-wrap:anywhere}footer{margin-top:12px;border-top:1px solid #8883;padding-top:6px}header button{font-size:12px} @media(prefers-reduced-motion:reduce){*{animation:none;transition:none}}
</style></head><body><header><h3>📆 Прогресс дня</h3><button id="reset">↺ Сброс</button></header><div id="v"></div><div id="sub"></div><div id="buttons"></div><div id="lines"></div><footer><small>N-Island · локально, без запросов к ПК</small></footer><script>
var initial={},S=JSON.parse(JSON.stringify(initial));function $(id){return document.getElementById(id)}
try{var stored=JSON.parse(localStorage.getItem('ni-tool-v1')||'null');if(stored&&typeof stored=='object')Object.keys(initial).forEach(function(k){if(typeof stored[k]==typeof initial[k]&&Array.isArray(stored[k])==Array.isArray(initial[k])&&(typeof stored[k]!='number'||(isFinite(stored[k])&&Math.abs(stored[k])<=1000000000000))&&(!Array.isArray(stored[k])||stored[k].length<=128))S[k]=stored[k]})}catch(e){}
function save(){try{localStorage.setItem('ni-tool-v1',JSON.stringify(S))}catch(e){}}
function show(v,sub){$('v').textContent=v;$('sub').textContent=sub||'';$('buttons').innerHTML='';$('lines').innerHTML=''}
function line(t){var p=document.createElement('p');p.textContent=t;$('lines').appendChild(p)}
function A(t,fn){var b=document.createElement('button');b.textContent=t;b.onclick=fn;$('buttons').appendChild(b)}
function done(){save();draw()}function fmt(n){n=Math.max(0,Math.ceil(n));return Math.floor(n/60)+':'+('0'+n%60).slice(-2)}
function draw(){var n=new Date(),a=new Date(n.getFullYear(),n.getMonth(),n.getDate()),b=new Date(n.getFullYear(),n.getMonth(),n.getDate()+1),p=(n-a)/(b-a);show((p*100).toFixed(1)+'%',n.toLocaleDateString());line('Осталось сегодня: '+fmt((b-n)/1000));A('Обновить',draw)}setInterval(draw,60000);
$('reset').onclick=function(){S=JSON.parse(JSON.stringify(initial));if(typeof phase!='undefined'){clearTimeout(wait);phase=0}done()};draw();if(window.TelegramWidget&&TelegramWidget.ready)TelegramWidget.ready();
</script></body></html>
"""),
        };
        public static Tool Find(string key) => Array.Find(All, t => t.Key == key);
    }
}
