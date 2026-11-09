// Copy to the official widget-sandbox root before running with pnpm exec tsx.
import fs from 'node:fs';
import {runWidget} from './packages/core/scripts/sandbox-node.ts';
const folder=process.env.NISLAND_WIDGET_DIR || './widget-audit';
const output=process.env.NISLAND_TEST_OUTPUT || '.';
let count=0;const results:any[]=[];
async function test(file:string,expressions:string[],want:string[],locale='ru'){
 const r=await runWidget(fs.readFileSync(`${folder}/${file}.html`,'utf8'),{locale,theme:'dark',policy:'strict',timerRounds:0,probes:expressions.map(e=>{const i=e.lastIndexOf(';');return i<0||e.startsWith('(()=>')?e:'(()=>{'+e.slice(0,i+1)+'return '+e.slice(i+1)+'})()'})});
 const errors=r.logs.filter(x=>x.level==='error');
 const ok=!r.stopped&&!errors.length&&!r.diags.length&&want.every((s,i)=>r.probes[i]===s);
 results.push({file,ok,probes:r.probes,want,logs:r.logs,diags:r.diags,stopped:r.stopped});
 if(!ok)console.log('FAIL',JSON.stringify(results.at(-1)));
 count++;
}
// Real QuickJS/DOM script handlers; not native trusted touch/geometry tests.
for(const lang of ['ru','en','uk']){
 const suffix=lang==='ru'?'':'-'+lang;
 const click=(n:number)=>`document.querySelectorAll('#buttons button')[${n}].onclick();`;
 await test('counter'+suffix,[click(1)+`$('v').textContent`,click(2)+click(1)+`$('v').textContent`,`$('reset').onclick();$('v').textContent`],['1','11','0'],lang);
 await test('score'+suffix,[click(0)+`$('v').textContent`,click(1)+`$('v').textContent`,click(2)+`$('v').textContent`],['1 : 0','1 : 1','1 : 0'],lang);
 await test('habit'+suffix,[click(0)+`String(S.days.length)`,click(0)+`String(S.days.length)`],['1','0'],lang);
 await test('packing'+suffix,[click(0)+`$('v').textContent`,click(7)+`$('v').textContent`,click(0)+`$('v').textContent`],['1 / 8','2 / 8','1 / 8'],lang);
 await test('interval'+suffix,[click(0)+`String(S.end>0)`,click(0)+`String(S.end==0)`,`$('reset').onclick();String(S.left)`],['true','true','30'],lang);
 await test('eyes'+suffix,[click(0)+`String(S.end>Date.now())`],['true'],lang);
 await test('tip'+suffix,[`$('v').textContent`,click(3)+`$('v').textContent`,click(2)+`$('v').textContent`],['110.00','55.00','57.50'],lang);
 await test('discount'+suffix,[`$('v').textContent`,click(3)+`$('v').textContent`],['90.00','85.00'],lang);
 await test('units'+suffix,[`$('v').textContent`,click(3)+`$('v').textContent`,click(3)+`$('v').textContent`],['6.214','22.046','50.000'],lang);
 await test('percent'+suffix,[`$('v').textContent`,click(3)+`$('v').textContent`],['15.00','16.00'],lang);
 await test('random'+suffix,[click(4)+`String(S.value>=1&&S.value<=S.max)`],['true'],lang);
 await test('reaction'+suffix,[click(0)+`String(phase)`,click(0)+`String(phase)`],['1','0'],lang);
 await test('memory'+suffix,[`String(S.cards.length)`, `var i=0,j=S.cards.indexOf(S.cards[0],1);document.querySelectorAll('#buttons button')[i].onclick();document.querySelectorAll('#buttons button')[j].onclick();String(S.found.length)`],['8','2'],lang);
 await test('palette'+suffix,[`$('v').textContent`,click(0)+`$('v').textContent`],['#3390ec','#30a46c'],lang);
 await test('budget'+suffix,[click(2)+`$('v').textContent`,click(3)+`$('v').textContent`,click(4)+`$('v').textContent`],['10','110','10'],lang);
 await test('dayprogress'+suffix,[`String(parseFloat($('v').textContent)>=0&&parseFloat($('v').textContent)<=100)`],['true'],lang);
 await test('CalcHtml'+suffix,[`String(ev('2+3*4'))`,`String(ev('(2+3)*4'))`,`(()=>{try{ev('2(3)');return 'accepted'}catch(e){return 'rejected'}})()`,`(()=>{try{ev('(2+3');return 'accepted'}catch(e){return 'rejected'}})()`,`(()=>{try{ev('1÷0');return 'accepted'}catch(e){return 'rejected'}})()`],['14','20','rejected','rejected','rejected'],lang);
 for(const file of ['ClockHtml','DiceHtml','CalendarHtml','BreathHtml','TttHtml','CountdownHtml','VizHtml']){
  // actual injected onclick attr compiled/executed as a browser would do.
  await test(file+suffix,[`var b=document.querySelector('button[onclick]');new Function(b.getAttribute('onclick'))();'ok'`],['ok'],lang);
 }
}
// Card and remote: no timer-driven privileged retry when request fails.
for(const file of ['CardHtml','RemoteWidgetHtml']){
 let r=await runWidget(fs.readFileSync(`${folder}/${file}.html`,'utf8'),{policy:'strict',timerRounds:0,data:{chatData:JSON.stringify({k:'player',h:'Title',r:[],b:[[['Play','play']]]}),publicData:null},probes:[file==='CardHtml'?`ask('play');'ok'`:`go(document.querySelector('button[data-q]'));'ok'`],onRequest:()=>{throw 'RATE_LIMITED'}});
 results.push({file,requestTest:true,logs:r.logs,requests:r.requests,stopped:r.stopped});
}
fs.writeFileSync(output+'/interaction-results.json',JSON.stringify({count,failed:results.filter(r=>r.ok===false).length,results},null,2));
console.log('Interaction cases',count,'failed',results.filter(r=>r.ok===false).length);
if(results.some(r=>r.ok===false))process.exitCode=1;
