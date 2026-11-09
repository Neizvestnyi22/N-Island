namespace NIsland.Core
{
    public static class FriendGameWidget
    {
        public const string Html = """
<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><style>
html,body{margin:0}body{font:13px/1.35 system-ui,sans-serif;color:var(--tg-theme-text-color,#222);padding:12px 12px max(14px,env(safe-area-inset-bottom))}h3{margin:0;font-size:16px}#s{font-weight:600;margin:6px 0;color:var(--tg-theme-link-color,#3390ec)}#i{min-height:18px;font-variant-numeric:tabular-nums}#g{display:grid;gap:4px;margin:10px 0}button{border:0;border-radius:8px;min-height:30px;padding:5px 2px;background:var(--tg-theme-secondary-bg-color,#eef2f6);color:inherit;font:inherit}button.live{background:var(--tg-theme-button-color,#3390ec);color:var(--tg-theme-button-text-color,#fff)}#rule{font-size:11px;color:var(--tg-theme-hint-color,#777)}footer{display:flex;gap:8px;align-items:center;margin-top:8px}footer button{padding:5px 12px}#e{font-size:11px;min-height:16px;margin-top:6px}
</style></head><body><h3 id="h">N-Island</h3><div id="s"></div><div id="i"></div><div id="g"></div><div id="rule"></div><footer><button id="refresh">⟳</button><span id="me"></span></footer><div id="e"></div><script>
var T=window.TelegramWidget,D={},busy=0;function $(i){return document.getElementById(i)}
function show(s){try{D=JSON.parse(s||'{}')}catch(e){return}$('h').textContent=D.h||'N-Island';$('s').textContent=D.s||'';$('i').textContent=D.i||'';$('me').textContent=D.me||'';$('rule').textContent=D.rule||'';$('g').style.gridTemplateColumns='repeat('+(D.c||3)+',1fr)';$('g').innerHTML='';(D.l||[]).forEach(function(label,n){var b=document.createElement('button');b.textContent=label;var op=(D.o||[])[n];if(op){b.className='live';b.onclick=function(){ask(op)}}$('g').appendChild(b)})}
var L=navigator.language||'',E=/^uk/.test(L)?'Не вдалося. Натисни ⟳ або спробуй пізніше.':/^ru/.test(L)?'Не получилось. Нажми ⟳ или попробуй позже.':'Could not complete. Tap ⟳ or try again later.';
function ask(op){if(!T||busy||!D.id)return;busy=1;$('e').textContent='…';T.request('g:'+D.id+':'+D.v+':'+op).then(function(a){busy=0;$('e').textContent='';if(a.result)show(a.result)},function(){busy=0;$('e').textContent=E})}
$('refresh').onclick=function(){ask('ref')};if(T){show(T.chatData);T.onEvent('chatDataChanged',function(){show(T.chatData)})}else $('e').textContent='TelegramWidget unavailable';
</script></body></html>
""";
    }
}
