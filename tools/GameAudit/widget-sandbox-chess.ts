import fs from 'node:fs';
import {runWidget} from './packages/core/scripts/sandbox-node.ts';
const folder=process.env.NISLAND_WIDGET_DIR||'/workspace/w16/widget-audit';
const html=fs.readFileSync(folder+'/game.html','utf8');const results:any[]=[];
for(const lang of ['ru','en','uk'])for(const theme of ['light','dark'] as const){
 const data=fs.readFileSync(folder+'/chess-state-'+lang+'.json','utf8');
 const r=await runWidget(html,{locale:lang,theme,policy:'strict',frames:0,timerRounds:0,data:{chatData:data,publicData:null},probes:["String(document.querySelectorAll('#g button').length)","document.querySelectorAll('#g button')[0].getAttribute('aria-label')","String(document.querySelectorAll('#g button')[0].disabled)"],script:[]});
 results.push({lang,theme,diags:r.diags,logs:r.logs,probes:r.probes,stopped:r.stopped});
 if(r.diags.length||r.logs.some((x:any)=>x.level==='error')||r.stopped||r.probes[0]!='68'||r.probes[1]!='a8')throw new Error(JSON.stringify(results.at(-1)));
}
fs.writeFileSync('/workspace/w16/game-sandbox-results.json',JSON.stringify(results,null,2));console.log('PASS 6 chess widget initial states, 3 languages x light/dark, strict profile, 64 rendered squares and coordinates; not trusted native touches.');
