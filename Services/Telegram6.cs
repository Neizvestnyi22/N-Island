using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NIsland.Core;

namespace NIsland.Services
{
    // =====================================================================
    //  Telegram Widgets (Bot API 10.4): интерактивные мини-HTML прямо в сообщении.
    //  /widget — живой пульт ПК (трек с прогрессом, плеер, громкость, процессор/память/батарея),
    //  /pet — персонаж острова в чате: тыкаешь в Telegram — он реагирует и на ПК.
    //  HTML фиксирован; меняется только состояние chat_data (до 1024 байт), которое пишет бот.
    //  Кнопки виджета шлют TelegramWidget.request("w:…") → update widget_query → answerWidgetQuery (свежее состояние сразу).
    //  Нет поддержки у Telegram/клиента — бот отвечает обычной живой панелью.
    // =====================================================================
    public partial class TelegramHub
    {
        static volatile bool _noWidgets;
        // кириллица как есть (UTF-8), а не К: у состояния лимит 1024 байта
        static readonly JsonSerializerOptions WidgetJson = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        readonly Dictionary<long, (string Kind, DateTime Until)> _widgets = new();   // message_id → какой виджет и до когда обновлять

        // Пульт ПК. Всё рисует JS по JSON из chatData; прогресс трека идёт сам по секундам между обновлениями от бота.
        const string RemoteWidgetHtml = """
<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<style>
html,body{margin:0}body{font:13px/1.35 system-ui,sans-serif;color:var(--tg-theme-text-color,#eee);padding:10px 12px}
.h{display:flex;justify-content:space-between;font-weight:600;font-size:15px}.d{color:var(--tg-theme-hint-color,#999);font-size:12px}
.t{margin:8px 0 2px;font-weight:600;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}.p{height:4px;border-radius:2px;background:#8884;overflow:hidden}
.p i{display:block;height:100%;background:var(--tg-theme-button-color,#3390ec);width:0}.m{display:flex;justify-content:space-between;font-size:11px;color:var(--tg-theme-hint-color,#999)}
.r{display:flex;gap:6px;margin:8px 0}.r button{flex:1;border:0;border-radius:10px;padding:8px 0;font-size:17px;background:#8882;color:inherit}
.r button:active{background:#8885}.g{display:grid;grid-template-columns:34px 1fr 38px;gap:3px 6px;align-items:center;font-size:11px}
.b{height:6px;border-radius:3px;background:#8883;overflow:hidden}.b i{display:block;height:100%;border-radius:3px}#s{min-height:15px;font-size:11px;color:var(--tg-theme-hint-color,#999)}
</style></head><body>
<div class="h"><span id="pc">ПК</span><span class="d" id="w"></span></div>
<div class="t" id="tr">—</div><div class="p"><i id="pr"></i></div><div class="m"><span id="a"></span><span id="z"></span></div>
<div class="r"><button data-q="prev">⏮</button><button data-q="play" id="pl">⏯</button><button data-q="next">⏭</button><button data-q="like">♥</button></div>
<div class="r"><button data-q="vd">🔉</button><button data-q="mute" id="mu">🔇</button><button data-q="vu">🔊</button><button data-q="dnd" id="dn">🌙</button><button data-q="ref">⟳</button></div>
<div class="g" id="g"></div><div id="s"></div>
<script>
var T=window.TelegramWidget,D={},t0=Date.now();
function $(i){return document.getElementById(i)}
function f(s){s=Math.max(0,s|0);return (s/60|0)+':'+('0'+s%60).slice(-2)}
function bar(n,v,c,x){return '<span>'+n+'</span><div class="b"><i style="width:'+Math.min(100,v)+'%;background:'+c+'"></i></div><span>'+(x||v+'%')+'</span>'}
function col(v){return v>85?'#ff453a':v>60?'#ffd60a':'#30d158'}
function show(){var d=D;$('pc').textContent=d.pc||'ПК';$('w').textContent=(d.w||'')+' '+(d.tm||'');$('tr').textContent=d.t||'Ничего не играет';
$('pl').textContent=d.p?'⏸':'▶';$('mu').textContent=d.mu?'🔈':'🔇';$('dn').style.opacity=d.dnd?1:.45;
var g=bar('CPU',d.cpu|0,col(d.cpu))+bar('RAM',d.ram|0,col(d.ram));if(d.bat>=0)g+=bar('BAT',d.bat,d.bat<20?'#ff453a':'#30d158',d.bat+'%'+(d.chg?'⚡':''));
g+=bar('VOL',d.mu?0:d.vol|0,'var(--tg-theme-button-color,#3390ec)',d.mu?'off':(d.vol|0)+'%');$('g').innerHTML=g;tick()}
function tick(){var d=D;if(!d.dur){$('pr').style.width=0;$('a').textContent=$('z').textContent='';return}
var p=d.pos+(d.p?(Date.now()-t0)/1e3*(d.rate||1):0);p=Math.min(p,d.dur);$('pr').style.width=(p/d.dur*100)+'%';$('a').textContent=f(p);$('z').textContent=f(d.dur)}
function load(s){try{D=JSON.parse(s||'{}');t0=Date.now();show();$('s').textContent=D.e||''}catch(e){}}
if(T){load(T.chatData||T.publicData);T.onEvent('chatDataChanged',function(){load(T.chatData)})}
function go(b,n){if(!T||b.B)return;b.B=1;b.style.opacity=.5;$('s').textContent='…';
T.request('w:'+b.dataset.q).then(function(a){b.B=0;b.style.opacity='';$('s').textContent='';if(a.result)load(a.result)},function(e){e=e||{};b.B=0;b.style.opacity='';
$('s').textContent=e.code==='NOT_ALLOWED'?'🔐':e.code==='RATE_LIMITED'?'⏳ Нажми ещё раз позже':'⚠ '+e.code})}
document.querySelectorAll('button').forEach(function(b){b.onclick=function(){go(b)}});
setInterval(tick,1000);
</script></body></html>
""";

        // Персонаж: капля с глазами и ртом по настроению, тап по нему — тык (персонаж на ПК тоже реагирует)
        const string PetWidgetHtml = """
<!doctype html><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><style>
body{margin:0;font:13px system-ui;color:var(--tg-theme-text-color,#eee);text-align:center;padding:6px}canvas{width:220px;height:170px;display:block;margin:auto}#h{font-size:11px;opacity:.6;min-height:14px}
button{border:0;border-radius:10px;padding:7px 10px;margin:6px 2px 0;font-size:17px;background:#8882;color:inherit}
</style><body><canvas id="v" width="440" height="340"></canvas><b id="n"></b><div id="h">тыкни меня</div><button>❤</button><button>🎉</button><button>😂</button><button>💃</button><button>😴</button><button>⟳</button>
<script>
var T=window.TelegramWidget,S={m:'idle',k:'blob',h:'auto',c:'#7ab8ff'},t=0,J=0,ba=2,F,FU=0,X=document.getElementById('v').getContext('2d'),M=Math,PI=M.PI,sn=M.sin,ab=M.abs,dk='#14141a',lk=0;
function $(i){return document.getElementById(i)}
function hx(c){return[1,3,5].map(function(i){return parseInt(c.substr(i,2),16)})}
function rg(a,o){return'rgba('+a[0]+','+a[1]+','+a[2]+','+(o==null?1:o)+')'}
function E(x,y,a,b,f,st){X.beginPath();X.ellipse(x,y,M.max(a,.1),M.max(b,.1),0,0,7);if(st){X.strokeStyle=f;X.stroke()}else{X.fillStyle=f;X.fill()}}
function L(a,b,c,d){X.beginPath();X.moveTo(a,b);X.lineTo(c,d);X.stroke()}
function Pl(p,f){X.beginPath();p.forEach(function(q,i){i?X.lineTo(q[0],q[1]):X.moveTo(q[0],q[1])});if(f){X.fillStyle=f;X.fill()}else X.stroke()}
function A(x,y,r,u){X.beginPath();X.ellipse(x,y,r,r*.8,0,u?PI:0,u?2*PI:PI);X.stroke()}
function Sm(x,y,w,h){X.beginPath();X.ellipse(x,y,w/2,h,0,0,PI);X.fillStyle=dk;X.fill()}
function RR(x,y,w,h,r,f){X.beginPath();X.roundRect?X.roundRect(x,y,w,h,r):X.rect(x,y,w,h);X.fillStyle=f;X.fill()}
function Ht(x,y,r,f){X.beginPath();X.moveTo(x,y+r*.8);X.bezierCurveTo(x-r*1.2,y,x-r*.6,y-r*.9,x,y-r*.3);X.bezierCurveTo(x+r*.6,y-r*.9,x+r*1.2,y,x,y+r*.8);X.fillStyle=f;X.fill()}
function St(x,y,r,f,o){var p=[];for(var i=0;i<10;i++){var a=o-PI/2+i*PI/5,q=i%2?r*.45:r;p.push([x+M.cos(a)*q,y+sn(a)*q])}Pl(p,f)}
function Tx(s,x,y,z,o){X.font='600 '+z+'px system-ui';X.textAlign='center';X.textBaseline='middle';X.fillStyle='rgba(255,255,255,'+o+')';X.fillText(s,x,y)}
function Ey(x,y,a,b,f,bl){E(x,y,a,b,f);bl||E(x-a*.35+lk*.2,y-b*.4,a*.32,a*.32,'#fff')}
function draw(){var m=F&&Date.now()<FU?F:S.m,k=S.k,s=112,cx=110,by=104,bb=0,q=1,tl=0,i,e,p;
X.setTransform(2,0,0,2,0,0);X.clearRect(0,0,220,170);X.lineCap='round';X.lineJoin='round';
switch(m){
case'dance':case'headbang':p=t*(m=='dance'?11.5:12.6);if(m=='dance'){bb=-ab(sn(p/2))*s*.1;q=1+M.cos(p)*.04;tl=sn(p/2)*8}else{tl=sn(p)*22;bb=-ab(sn(p))*s*.06}break;
case'party':bb=-ab(sn(t*9))*s*.12;tl=sn(t*9)*10;break;
case'sleep':q=1+sn(t*1.6)*.035;tl=6;break;
case'alert':case'cold':bb=sn(t*40)*s*.02;break;
case'angry':tl=sn(t*30)*3;break;
case'yawn':q=1+M.max(0,sn(t*2.4))*.08;break;
case'dizzy':tl=sn(t*6)*14;break;
case'wave':tl=-6;break;
case'curious':tl=12;break;
case'hot':q=1+sn(t*8)*.03;break;
case'laugh':bb=-ab(sn(t*16))*s*.04;q=1+sn(t*16)*.05;tl=sn(t*5)*6;break;
case'scared':bb=sn(t*48)*s*.025+s*.04;q=.9;break;
case'bored':tl=10+sn(t*.9)*4;q=.97;break;
case'proud':tl=-5;bb=-s*.03;q=1.04;break;
case'shy':tl=9+sn(t*2)*2;q=.96;break;
case'sick':tl=sn(t*1.3)*7;q=1+sn(t*1.3)*.03;break;
case'confused':tl=sn(t*3)*12;break;
case'excited':bb=-ab(sn(t*12))*s*.14;q=1+M.cos(t*24)*.04;break;
case'kiss':tl=8;bb=-s*.02;break;
case'amazed':bb=-s*.05-sn(t*6)*s*.01;q=1.06;break;
case'sneeze':p=t*1.6%1;q=p<.7?1+p*.12:.85;tl=p<.7?-p*8:14;break;
default:q=1+sn(t*2.2)*.02}
if(J>0)bb-=sn(J*PI)*s*.18;
var C={hot:[255,90,60,.45],angry:[255,48,48,.55],cold:[159,216,255,.55],sick:[156,208,106,.55],scared:[232,236,245,.45],shy:[255,159,184,.25],kiss:[255,159,184,.25]}[m],
tn=hx(S.c),bd=C?tn.map(function(v,i){return M.round(v+(C[i]-v)*C[3])}):tn,B=rg(bd),bw=s*.86,bh=s*.74,top=by-bh/2,sy=by+s*.34;
X.translate(cx,by+bb);X.rotate(tl*PI/180);X.translate(-cx,-by);X.translate(cx,sy);X.scale(1/q,q);X.translate(-cx,-sy);
/*k:cat*/{for(e=-1;e<2;e+=2){var x0=cx+e*bw*.28;Pl([[x0-bw*.17,top+s*.08],[x0+e*bw*.06,top-s*.2+sn(t*2+e)*s*.01],[x0+bw*.17,top+s*.08]],B)}E(cx,by,bw/2,bh/2,B)}
/*k:bunny*/{var dr=/sad|cry|sleep/.test(m);for(e=-1;e<2;e+=2){X.save();X.translate(cx+e*bw*.2,top);X.rotate(e*(dr?25:8-sn(t*3)*4)*PI/180);RR(e<0?-bw*.1:-bw*.07,-s*.42,bw*.17,s*.55,bw*.08,B);X.restore()}E(cx,by,bw/2,bh/2,B)}
/*k:ghost*/{var l=cx-bw/2,r=cx+bw/2,bo=by+bh*.55;X.beginPath();X.moveTo(l,bo);X.ellipse(cx,by,bw/2,bh*.6,0,PI,2*PI);X.lineTo(r,bo);for(i=0;i<3;i++){var a0=r-bw*i/3,a1=r-bw*(i+1)/3;X.quadraticCurveTo((a0+a1)/2,bo-s*.1+sn(t*4+i)*s*.04,a1,bo)}X.fillStyle=rg(bd,.92);X.fill()}
/*k:robot*/{X.strokeStyle=B;X.lineWidth=s*.05;L(cx,top,cx,top-s*.16);var lp=/think|alert|angry/.test(m)?(t*4|0)%2==0:(t*.8|0)%2==0;E(cx,top-s*.18,s*.06,s*.06,lp?(m=='charging'?'#30d158':'#ff453a'):rg(bd,.63));RR(cx-bw/2,top,bw,bh,s*.16,B)}
/*k:blob*/{var w=sn(t*3.1)*s*.02;E(cx,by+w*.5,bw/2+w,bh/2-w*.5,B);E(cx-bw*.22,top+bh*.22,bw*.1,bh*.07,'rgba(255,255,255,.24)')}
var ex=bw*.2,ey=by-bh*.06,er=s*.085,bl=0;lk=m=='idle'?(sn(t*.37)*.6+sn(t*.11)*.3)*s*.05:0;if(t>ba){bl=1;if(t>ba+.13)ba=t+2.5+M.random()*4}
X.strokeStyle=dk;X.lineWidth=s*.055;
for(e=-1;e<2;e+=2){var x=cx+e*ex+lk,y=ey;switch(m){
case'sleep':A(x,y,er,0);break;
case'happy':case'dance':case'party':case'wave':case'charging':bl?L(x-er,y,x+er,y):A(x,y+er*.3,er,1);break;
case'wink':e>0?A(x,y+er*.3,er,1):Ey(x,y,er,er,dk);break;
case'love':Ht(x,y,er*1.5,'#ff375f');break;
case'focus':case'yawn':L(x-er,y,x+er,y);break;
case'dizzy':X.lineWidth=s*.033;p=[];for(i=0;i<17;i++){var aa=t*8*e+i*.75,rr=er*1.2*i/16;p.push([x+M.cos(aa)*rr,y+sn(aa)*rr])}Pl(p);X.lineWidth=s*.055;break;
case'cry':A(x,y,er,1);break;
case'laugh':case'proud':case'headbang':Pl([[x-er,y+er*.4],[x,y-er*.4],[x+er,y+er*.4]]);break;
case'sneeze':t*1.6%1>=.7?Pl([[x-er*e,y-er*.5],[x+er*e,y],[x-er*e,y+er*.5]]):Ey(x,y,er,er*.45,dk);break;
case'scared':Ey(x,y,er*1.25,er*1.4,'#fff',1);E(x+lk*.4,y,er*.35,er*.35,dk);X.lineWidth=s*.02;E(x,y,er*1.25,er*1.4,dk,1);X.lineWidth=s*.055;break;
case'bored':Ey(x,y+er*.3,er,er*.55,dk,bl);L(x-er*1.1,y-er*.2,x+er*1.1,y-er*.2);break;
case'shy':Ey(x,y+er*.35,er*.8,er*.8,dk,bl);break;
case'sick':L(x-er,y-er*.2,x+er,y+er*.2);break;
case'confused':var rc=e<0?er*1.2:er*.6;Ey(x,y,rc,rc,dk,bl);e>0&&L(x-er,y-er*1.8,x+er,y-er*2.3);break;
case'excited':case'amazed':St(x,y,er*(m=='amazed'?1.6:1.35),'#ffd60a',t*2*e);break;
case'kiss':e<0?A(x,y+er*.3,er,1):Ey(x,y,er,er,dk,bl);break;
case'angry':Ey(x,y,er*.9,er*.7,dk);L(x-er*1.2,y-er*(e<0?1.8:1.1),x+er*1.2,y-er*(e<0?1.1:1.8));break;
default:var su=/surprised|alert/.test(m);Ey(x,y,su?er*1.15:er,bl?er*.12:su?er*1.35:/sad|hot/.test(m)?er*.8:er,dk,bl);
m=='sad'&&L(x-er*1.1,y-er*(e<0?1:1.6),x+er*1.1,y-er*(e<0?1.6:1));m=='curious'&&e>0&&L(x-er,y-er*2,x+er,y-er*2.4)}}
if(m=='cool'){for(e=-1;e<2;e+=2)RR(cx+e*ex-er*1.6+lk,ey-er*1.1,er*3.2,er*2.1,er*.6,'#0a0a10');X.strokeStyle='#0a0a10';X.lineWidth=s*.04;L(cx-ex+er*1.6+lk,ey-er*.4,cx+ex-er*1.6+lk,ey-er*.4)}
var my=by+bh*.2,mw=s*.12,o;X.strokeStyle=dk;X.lineWidth=s*.045;x=cx+lk;
switch(m){
case'surprised':case'alert':E(x,my,mw*.45,mw*.6,dk);break;
case'yawn':E(x,my,mw*.5,mw*.75*(.3+.7*M.max(0,sn(t*2.4))),dk);break;
case'hot':E(x,my,mw*.4,mw*(.3+.2*ab(sn(t*8))),dk);E(x,my+mw*.35,mw*.25,mw*.2,'#ff6b8b');break;
case'sad':case'cry':A(x,my+mw*.5,mw*.7,1);break;
case'angry':A(x,my+mw*.4,mw*.55,1);break;
case'focus':case'listen':L(x-mw*.5,my,x+mw*.5,my);break;
case'laugh':case'excited':o=m=='laugh'?.8+.25*ab(sn(t*16)):.9;Sm(x,my-mw*.25,mw*1.05,mw*o);E(x,my+mw*.35*o,mw*.28,mw*.16,'#ff6b8b');break;
case'scared':p=[];for(i=0;i<7;i++)p.push([x-mw*.7+mw*.233*i,my+(i%2?mw*.22:0)]);Pl(p);break;
case'bored':L(x-mw*.45,my+mw*.1,x+mw*.45,my-mw*.05);break;
case'proud':A(x,my-mw*.25,mw*.6,0);break;
case'shy':A(x-mw*.1,my-mw*.1,mw*.3,0);break;
case'sick':X.beginPath();X.moveTo(x-mw*.5,my+mw*.1);X.bezierCurveTo(x-mw*.2,my-mw*.2,x+mw*.2,my+mw*.35,x+mw*.5,my);X.stroke();break;
case'confused':L(x-mw*.45,my-mw*.1,x+mw*.45,my+mw*.15);break;
case'kiss':E(x+mw*.1,my-mw*.05,mw*.18,mw*.14,dk,1);E(x+mw*.1,my+mw*.2,mw*.18,mw*.14,dk,1);break;
case'headbang':E(x,my,mw*.45,mw*.5,dk);break;
case'amazed':E(x,my+mw*.05,mw*.35,mw*.5,dk);break;
case'sneeze':p=t*1.6%1;p>=.7?E(x,my,mw*.5,mw*.55,dk):E(x,my,mw*.18,mw*(.15+p*.3),dk);break;
case'sleep':E(x,my,mw*.22,mw*.18,dk);break;
case'dizzy':case'cold':p=[];for(i=0;i<5;i++)p.push([x-mw*.6+mw*.3*i,my+(i%2?mw*.25:0)]);Pl(p);break;
case'cool':case'wink':case'curious':A(x+mw*.2,my-mw*.15,mw*.45,0);break;
case'dance':case'happy':case'love':case'party':case'wave':case'charging':Sm(x,my-mw*.2,mw*.8,mw*.65);break;
default:if(k=='cat'||k=='bunny'){A(x-mw*.32,my-mw*.1,mw*.32,0);A(x+mw*.32,my-mw*.1,mw*.32,0)}else A(x,my-mw*.25,mw*.55,0)}
for(e=-1;e<2;e+=2){x=cx+e*bw*.32+lk;if(/shy|kiss/.test(m)){E(x,my-s*.04,s*.09,s*.055,'rgba(255,90,122,.75)');X.strokeStyle='rgba(208,48,80,.8)';X.lineWidth=s*.015;for(i=-1;i<2;i++)L(x+i*s*.03-s*.01,my-s*.06,x+i*s*.03+s*.01,my-s*.02)}
else if(/^(happy|dance|love|party|wink|wave|charging|laugh|excited|proud)$/.test(m))E(x,my-s*.04,s*.07,s*.045,'rgba(255,107,139,.43)')}
if(m=='wave'){X.save();X.translate(cx+bw*.45,by);X.rotate(sn(t*14)*25*PI/180);E(bw*.1,-bh*.35,s*.09,s*.12,B);X.restore()}
var h=S.h,d=new Date(),ht=/cat|bunny|robot/.test(k)?top-s*.02:top;if(h=='auto')h=m=='dance'?'headphones':m=='party'?'party':(d.getMonth()==11&&d.getDate()>=15)||(d.getMonth()==0&&d.getDate()<=10)?'santa':'';
/*h:cap*/if(h=='cap'){X.beginPath();X.ellipse(cx,ht+s*.06,bw*.36,s*.22,0,PI,2*PI);X.fillStyle='#ff453a';X.fill();RR(cx+bw*.1,ht+s*.03,bw*.48,s*.07,s*.03,'#ff453a')}
/*h:crown*/if(h=='crown'){var cw=bw*.56,c0=cx-cw/2,cb=ht+s*.04,ct=cb-s*.22;Pl([[c0,cb],[c0,ct+s*.06],[c0+cw*.25,ct+s*.13],[cx,ct],[c0+cw*.75,ct+s*.13],[c0+cw,ct+s*.06],[c0+cw,cb]],'#ffd60a');E(cx,cb-s*.06,s*.03,s*.03,'#ff375f')}
/*h:bow*/if(h=='bow'){x=cx+bw*.28;y=ht+s*.02;for(e=-1;e<2;e+=2)Pl([[x,y],[x+e*s*.16,y-s*.09],[x+e*s*.16,y+s*.09]],'#ff5ca8');E(x,y,s*.04,s*.04,'#ff5ca8')}
/*h:party*/if(h=='party'||h=='santa'){var sa=h=='santa',hb=ht+s*.05,hw=bw*.42,tx=cx+(sa?hw*.7:0),tyy=hb-s*(sa?.32:.38);Pl([[cx-hw/2,hb],[tx,tyy],[cx+hw/2,hb]],sa?'#e02020':'#5e5ce6');/*h:party*/if(sa)RR(cx-hw/2-s*.02,hb-s*.04,hw+s*.04,s*.07,s*.03,'#fff');else{X.strokeStyle='#ffd60a';X.lineWidth=s*.03;L(cx-hw*.25,hb-s*.1,cx+hw*.2,hb-s*.18)}E(tx,tyy,s*.05,s*.05,sa?'#fff':'#ffd60a')}
/*h:headphones*/if(h=='headphones'){X.strokeStyle='#2c2c34';X.lineWidth=s*.06;X.beginPath();X.ellipse(cx,ey,bw*.5,ey-ht+s*.12,0,PI,2*PI);X.stroke();for(e=-1;e<2;e+=2)RR(cx+e*bw*.5-s*.07,ey-s*.1,s*.14,s*.22,s*.05,'#2c2c34')}
/*h:glasses*/if(h=='glasses'&&m!='cool'){X.strokeStyle=dk;X.lineWidth=s*.035;for(e=-1;e<2;e+=2)E(cx+e*ex+lk,ey,er*1.6,er*1.5,dk,1);L(cx-ex+er*1.6+lk,ey,cx+ex-er*1.6+lk,ey)}
/*h:flower*/if(h=='flower'){for(i=0;i<5;i++){var fa=i*PI*.4+t*.3;E(cx-bw*.3+M.cos(fa)*s*.07,ht+s*.02+sn(fa)*s*.07,s*.055,s*.055,'#ff9fd6')}E(cx-bw*.3,ht+s*.02,s*.045,s*.045,'#ffd60a')}
X.setTransform(2,0,0,2,0,bb*2);p=t*.7%1;
switch(m){
case'sleep':p=t*.5%1;Tx('z',cx+s*.38+p*s*.2,top-p*s*.5,s*(.22+p*.12),1-p);break;
case'dance':Tx('♪',cx+s*.42,top+s*.1-p*s*.45,s*.3,.86*(1-p));break;
case'think':for(i=0;i<3;i++)E(cx-s*.18+i*s*.18,top-s*.12,s*.05,s*.05,'rgba(255,255,255,'+(.35+.65*M.max(0,sn(t*5-i*.9)))+')');break;
case'alert':case'scared':E(cx+bw*.45,top+s*.08,s*.05,s*.075,'#64d2ff');m=='scared'&&Tx('!',cx-s*.42,top,s*.3,.95);break;
case'cold':Tx('❄',cx+s*.42,top,s*.26,.86);break;
case'cry':case'laugh':for(e=-1;e<2;e+=2){p=(t*1.2+(e>0?.5:0))%1;E(cx+e*bw*(m=='cry'?.24:.42+p*.1),top+bh*(m=='cry'?.45:.3)+p*s*(m=='cry'?.4:-.05),s*.045,s*.07,'rgba(232,246,255,'+(1-p)+')')}m=='laugh'&&Tx('ха',cx+s*.44,top-ab(sn(t*8))*s*.05,s*.2,.86);break;
case'love':case'kiss':Ht(cx+s*.42,top-p*s*.35,s*.1,'rgba(255,55,95,'+(1-p)+')');break;
case'charging':Tx('⚡',cx+s*.42,top,s*.3,1);break;
case'angry':Tx('💢',cx+s*.4,top,s*.26,1);break;
case'curious':case'confused':Tx('?',cx+s*.42,top-ab(sn(t*4))*s*.06,s*.32,.9);break;
case'yawn':Tx('z',cx+s*.4,top,s*.18,.6);break;
case'dizzy':for(i=0;i<3;i++){var da=t*5+i*PI*2/3;Tx('★',cx+M.cos(da)*s*.4,top-s*.05+sn(da)*s*.1,s*.18,.9)}break;
case'party':for(i=0;i<8;i++){p=(t*.8+i*.13)%1;X.fillStyle=['#ff453a','#ffd60a','#30d158','#0a84ff','#bf5af2'][i%5];X.globalAlpha=1-p;X.fillRect(cx+sn(i*2.3)*s*.55,top-s*.3+p*s*.9,s*.06,s*.06);X.globalAlpha=1}break;
case'proud':case'excited':case'amazed':for(i=0;i<5;i++){var sa2=t*2.2+i*PI*.4,tw=.5+.5*sn(t*9+i*2);St(cx+M.cos(sa2)*s*.5,top+bh*.4+sn(sa2)*s*.27,s*.05*(.6+tw*.6),'rgba(255,240,128,'+(.5+tw/2)+')',sa2)}break;
case'sick':Tx('🌡',cx-s*.44,top+s*.08,s*.22,.86);break;
case'headbang':Tx('🤘',cx+s*.44,top+s*.3,s*.22,.9);break;
case'sneeze':if(t*1.6%1>=.7)Tx('апчхи!',cx+s*.1,top-s*.12,s*.17,1);break;
case'bored':Tx('…',cx+s*.38,top,s*.25,.7);break;
}}
var rm=matchMedia('(prefers-reduced-motion:reduce)'),lastDraw=0;setInterval(function(){if(document.hidden)return;var now=Date.now(),slow=rm.matches||S.m=='sleep';if(slow&&now-lastDraw<160)return;lastDraw=now;t+=slow?.16:1/30;J=M.max(0,J-.08);if(rm.matches){t=0;J=0}draw()},33);
function fl(m){F=m;FU=Date.now()+1800;J=1}
function show(s,tap){var d={};try{d=JSON.parse(s||'{}')}catch(e){return}if(tap&&d.m)fl(d.m);for(var k in d)if(!tap||k!='m')S[k]=d[k];$('n').textContent=S.n||''}
var petBusy=0;function ask(q){if(petBusy)return;fl(q=='poke'?'happy':q);if(!T)return;petBusy=1;$('h').textContent='…';T.request('p:'+q).then(function(a){petBusy=0;$('h').textContent='';a&&a.result&&show(a.result,1)},function(e){petBusy=0;$('h').textContent='⚠ '+(e&&e.code||'ERROR')})}
if(T){show(T.chatData);T.onEvent('chatDataChanged',function(){show(T.chatData)})}
$('v').onclick=function(){ask('poke')};document.querySelectorAll('button').forEach(function(b,i){b.onclick=function(){ask(['love','party','laugh','dance','sleep','ref'][i])}});
</script></body></html>
""";

        // Персонаж в виджете рисуется тем же кодом, что на острове (скины, аксессуары, эмоции, значки вокруг), но целиком
        // он в 4 КБ сжатого HTML не помещается. Поэтому бот собирает HTML под этого пользователя: только его скин и аксессуар,
        // эмоции из виджета (кнопки, тык) и та, что на острове сейчас. Незнакомую эмоцию виджет рисует спокойным лицом
        // с цветом этой эмоции. Строки шаблона: /*k:скин*/, /*h:аксессуар*/, case'эмоция': — по одной ветке на строку.
        static readonly string[] PetMoods = { "idle", "happy", "love", "party", "laugh", "dance", "sleep", "wink", "surprised", "kiss" };
        static readonly string[] PetPokes = { "wink", "love", "happy", "surprised", "laugh", "kiss", "party" };

        static string PetHtml(string skin = null, string hat = null, string mood = null)
        {
            var st = Store.Settings;
            skin ??= st.Mascot;
            if (!Controls.Mascot.Skins.Any(k => k.Key == skin)) skin = "blob";
            hat ??= st.MascotHat ?? "auto";
            var hats = hat == "auto" ? new[] { "headphones", "party" } : new[] { hat == "santa" ? "party" : hat };
            var moods = new HashSet<string>(PetMoods);
            try { mood ??= App.Island?.MascotMood; } catch { }
            if (mood != null) moods.Add(mood);
            var sb = new StringBuilder();
            foreach (var line in PetWidgetHtml.Split('\n'))
            {
                var l = line.TrimEnd('\r');
                var mk = System.Text.RegularExpressions.Regex.Match(l, @"^/\*([kh]):(\w+)\*/");
                if (mk.Success)
                {
                    if (mk.Groups[1].Value == "k" ? mk.Groups[2].Value != skin : !hats.Contains(mk.Groups[2].Value)) continue;
                    l = l[mk.Length..];
                }
                var cm = System.Text.RegularExpressions.Regex.Match(l, @"^(?:case'(\w+)':)+");
                if (cm.Success && !cm.Groups[1].Captures.Any(c => moods.Contains(c.Value))) continue;
                sb.Append(l).Append('\n');
            }
            return sb.ToString().Replace("тыкни меня", T("тыкни меня"));
        }

        internal static string TestPetHtml(string skin, string hat, string mood) => PetHtml(skin, hat, mood);

        /// <summary>Состояние пульта (≤1024 байт): ПК, трек, позиция, громкость, нагрузка.</summary>
        static string RemoteState(string note = null)
        {
            var sys = SystemService.I; var m = MediaService.I; var a = AudioService.I;
            var w = WeatherService.Cached;
            var d = new Dictionary<string, object>
            {
                ["pc"] = Cut(Environment.MachineName, 20), ["tm"] = DateTime.Now.ToString("HH:mm"),
                ["w"] = w != null && (DateTime.Now - w.At).TotalHours < 3 ? $"{Math.Round(w.Temp):0}°" : "",
                ["cpu"] = sys.CpuPercent(), ["ram"] = sys.RamPercent(), ["bat"] = sys.HasBattery ? sys.BatteryPercent : -1, ["chg"] = sys.Charging ? 1 : 0,
                ["vol"] = (int)Math.Round(a.Volume * 100), ["mu"] = a.Muted ? 1 : 0, ["dnd"] = Store.Settings.Mode == "dnd" ? 1 : 0,
                ["t"] = m.HasSession ? Cut(m.TrackLine, 70) : "", ["p"] = m.IsPlaying ? 1 : 0,
                ["rate"] = m.Rate, ["pos"] = m.HasTime ? (int)m.Position.TotalSeconds : 0, ["dur"] = m.HasTime ? (int)m.Duration.TotalSeconds : 0,
            };
            if (note != null) d["e"] = note;
            return JsonSerializer.Serialize(d, WidgetJson);
        }

        static string PetState(string mood = null)
        {
            var s = Store.Settings;
            var tint = App.Island?.MascotTintHex ?? "#7ab8ff";
            var d = new Dictionary<string, object>
            {
                ["m"] = mood ?? App.Island?.MascotMood ?? "happy",
                ["k"] = s.Mascot ?? "blob", ["h"] = s.MascotHat ?? "auto",
                ["n"] = T(Controls.Mascot.Skins.FirstOrDefault(k => k.Key == s.Mascot).Name ?? "Капля") + " · " + Cut(Environment.MachineName, 16),
                ["c"] = tint,
            };
            return JsonSerializer.Serialize(d, WidgetJson);
        }

        /// <summary>Нажатие в виджете (update widget_query). Только наш чат; копии из пересылок (shared_widget_query) не обслуживаем.</summary>
        async Task HandleWidgetQuery(JsonElement q)
        {
            var qid = Integrations.S(q, "id");
            if (string.IsNullOrEmpty(qid)) return;
            string result = null;
            long msgId = 0;
            try
            {
                string chat = q.TryGetProperty("chat", out var c) && c.TryGetProperty("id", out var cid) ? cid.ToString() : "";
                string from = q.TryGetProperty("from", out var f) && f.TryGetProperty("id", out var fid) ? fid.ToString() : "";
                if (chat != V("chat") || from != chat || Integrations.S(c, "type") != "private") return;
                if (q.TryGetProperty("type", out var qt) && (qt.ValueKind != JsonValueKind.Object || Integrations.S(qt, "type") != "data")) return;
                msgId = q.TryGetProperty("message_id", out var mi) && mi.TryGetInt64(out var mid) ? mid : 0;
                var data = Integrations.S(q, "data");
                if (msgId <= 0 || !WidgetPolicy.ValidQuery(data)) return;
                var work = DoubleTap(msgId, data) ? Task.Run(() => CurrentWidgetState(data, msgId)) : Serialized(msgId, () => WidgetAction(data, msgId));
                if (await Task.WhenAny(work, Task.Delay(9000)) == work) result = await work;
                else
                {
                    // Acknowledge before the 15-second deadline. Slow work persists its state later.
                    _ = CompleteWidgetWork(work, msgId);
                }
                if (result != null && !WidgetPolicy.ValidData(result)) { Log.Info("widget answer exceeded 1024 bytes"); result = null; }
            }
            catch (Exception e) { Log.Info("telegram widget query: " + e.Message); }
            finally
            {
                try { using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(4)); await Call("answerWidgetQuery", result != null ? new { widget_query_id = qid, data = result } : (object)new { widget_query_id = qid }, deadline.Token); }
                catch (Exception e) { Log.Info("telegram widget answer: " + e.Message); }
            }
            if (result != null && msgId != 0 && result.StartsWith("{")) _ = PersistWidget(msgId, result);
        }

        async Task CompleteWidgetWork(Task<string> work, long msgId)
        {
            try { var state = await work; if (state != null && WidgetPolicy.ValidData(state)) await PersistWidget(msgId, state); }
            catch (Exception e) { Log.Info("telegram delayed widget: " + e.Message); }
        }

        internal Task<string> TestWidgetAction(string data) => Task.Run(() => WidgetAction(data, 7));

        async Task<string> WidgetAction(string data, long msgId)
        {
            var m = MediaService.I; var a = AudioService.I;
            if (data.StartsWith("w:"))
            {
                var op = data[2..];
                if (op != "ref" && !Flag("control")) return RemoteState(T("🔐 «Управление» выключено — включается на ПК"));
                switch (op)
                {
                    case "prev": await Ui(m.Prev); break;
                    case "next": await Ui(m.Next); break;
                    case "play": await Ui(m.TogglePlay); break;
                    case "like": await Ui(() => m.ToggleFavorite()); break;
                    case "vu": await Ui(() => a.SetVolume(Math.Min(1, a.Volume + 0.1))); break;
                    case "vd": await Ui(() => a.SetVolume(Math.Max(0, a.Volume - 0.1))); break;
                    case "mute": await Ui(a.ToggleMute); break;
                    case "dnd": await Ui(() => App.Island?.SetMode(Store.Settings.Mode == "dnd" ? "normal" : "dnd")); break;
                }
                if (op is "prev" or "next" or "play") await Task.Delay(700);   // плеер обновляет трек не мгновенно
                Live(msgId, "remote");
                return RemoteState();
            }
            if (data.StartsWith("c:")) return await CardAction(data[2..], msgId);
            if (data.StartsWith("p:"))
            {
                var op = data[2..];
                string mood = op switch { "poke" => null, "love" => "love", "party" => "party", "laugh" => "laugh", "dance" => "dance", "sleep" => "sleep", _ => null };
                // тык: реакция из тех, что умеет рисовать виджет, — на острове та же самая
                if (op == "poke") { mood = PetPokes[Random.Shared.Next(PetPokes.Length)]; App.Island?.MascotReact(mood, 1.8); }
                else if (mood != null) App.Island?.MascotReact(mood, mood == "sleep" ? 6 : 3);
                if (Store.Settings.TgWidgetToasts && op != "ref") Toast("🫧 Из Telegram", op == "poke" ? "Тебя потыкали в Telegram" : "Персонаж: " + op);
                return PetState(mood ?? App.Island?.MascotMood);
            }
            return null;
        }

    }
}
