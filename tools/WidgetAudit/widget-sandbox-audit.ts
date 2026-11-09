// Copy to the official widget-sandbox root before running with pnpm exec tsx.
import fs from 'node:fs';
import path from 'node:path';
import {runWidget} from './packages/core/scripts/sandbox-node.ts';
const folder=process.env.NISLAND_WIDGET_DIR || './widget-audit';
const output=process.env.NISLAND_TEST_OUTPUT || '.';
const files=fs.readdirSync(folder).filter(f=>f.endsWith('.html'));
const results:any[]=[];
for(const file of files){
 for(const theme of ['light','dark'] as const){
  let data:any={k:'player',h:'🎵 Длинный тестовый трек',s:'Исполнитель',r:[['p',25,180,1,1.5],['b','🔊',60,null,'60%'],['t','Текст песни']],b:[[['▶','play','p'],['⏭','next']]],pm:'#3390ec'};
  if(file==='SysmonHtml.html')data={n:'PC',t:'12:00',c:[5,10,70],m:[40,50,45]};
  if(file==='RemoteWidgetHtml.html')data={t:'Трек',a:'Артист',p:1,v:60,c:12,r:40};
  if(file==='pet.html')data={m:'idle',k:'blob',h:'auto',n:'N-Island'};
  if(file==='ClockHtml.html')data={n:'PC',o:120,z:[['Киев',120]]};
  if(file==='VizHtml.html')data={b:[10,20,40,80],p:1,c:'#3390ec'};
  const r=await runWidget(fs.readFileSync(path.join(folder,file),'utf8'),{theme,locale:'ru',policy:'strict',frames:3,timerRounds:3,data:{chatData:JSON.stringify(data),publicData:null},probes:['document.body.textContent'],script:[{theme:theme==='light'?'dark':'light'}]});
  results.push({file,theme,diags:r.diags,logs:r.logs,stopped:r.stopped,nodes:r.nodes,timers:r.timers,probes:r.probes});
 }
}
fs.writeFileSync(output+'/sandbox-results.json',JSON.stringify(results,null,2));
for(const r of results){const bad=r.logs.filter((x:any)=>x.level==='error');console.log(r.file,r.theme,`nodes=${r.nodes}`,`diags=${r.diags.length}`,r.stopped??'',bad);if(r.diags.length)console.log(JSON.stringify(r.diags).slice(0,1800));}
