// Exercise the actual shipped game script and request wrapper with a small DOM/fake clock.
const vm=require('node:vm'),fs=require('node:fs');const dir=process.argv[2]||'/workspace/w16/widget-audit';
let checks=0;function check(v,m){checks++;if(!v)throw Error(m)}
function environment(){let timers=new Map(),next=0,calls=0,pending=[];const elements={};function element(){return {style:{},children:[],textContent:'',setAttribute(){},appendChild(e){this.children.push(e)},set innerHTML(s){this.children=[]}}}
const document={getElementById(id){return elements[id]||(elements[id]=element())},createElement:element};
const T={chatData:'{}',request(q){calls++;return new Promise((resolve,reject)=>pending.push({resolve,reject,q}))},onEvent(){}};
let ctx=vm.createContext({window:{TelegramWidget:T},document,navigator:{language:'ru'},setTimeout(f){let n=++next;timers.set(n,f);return n},clearTimeout(n){timers.delete(n)},Date,console});
const html=fs.readFileSync(dir+'/game.html','utf8');vm.runInContext(html.match(/<script>([\s\S]*?)<\/script>/)[1],ctx);
function state(v){return JSON.stringify({id:'abc',k:'ttt',v,h:'Game',s:'Turn',l:['A'],o:['0'],c:1,joined:true})}ctx.show(state(1));
return {ctx,T,timers,pending,state,calls:()=>calls,elements};}
(async()=>{
let e=environment();e.ctx.ask('0');e.ctx.ask('0');check(e.calls()==1&&e.ctx.busy===1,'duplicate tap not guarded');for(const [id,f] of [...e.timers]){e.timers.delete(id);f();}await new Promise(setImmediate);check(e.ctx.busy===0,'timeout leaves busy');check(e.elements.e.textContent.length>0,'no timeout feedback');e.pending[0].resolve({result:e.state(9)});await new Promise(setImmediate);check(e.ctx.D.v===1,'late timeout result applied');
e.ctx.ask('ref');check(e.calls()==2,'could not recover after timeout');e.ctx.show(e.state(5));e.pending[1].resolve({result:e.state(2)});await new Promise(setImmediate);check(e.ctx.D.v===5,'old response rolled state back');check(e.timers.size===0,'completed timer leaked');
e=environment();e.T.request=()=>{throw Error('sync')};e.ctx.ask('0');await new Promise(setImmediate);check(e.ctx.busy===0,'sync failure locks buttons');check(e.timers.size===0,'sync failure timer leak');
e=environment();e.ctx.ask('0');e.pending[0].reject({code:'BUSY'});await new Promise(setImmediate);check(e.ctx.busy===0&&e.calls()==1,'failure auto-replayed action');check(e.timers.size===0,'failure timer leak');
console.log(`PASS ${checks} widget request checks: duplicate guard, timeout/recovery, late response ignored, no state rollback, sync throw, timer cleanup, no action replay. Mock DOM/clock, not native Telegram touches.`);
})().catch(e=>{console.error(e);process.exit(1)});
