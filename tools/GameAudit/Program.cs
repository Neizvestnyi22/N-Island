using System.Text.Json;
using System.Text;
using NIsland.Core;
using NIsland.Services;
int checks=0;
void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
var games=new FriendGames();
Check(FriendGames.Catalog.Length>=11,"game count");
var sample=games.Create("ttt",42);
Check(sample.Id.Length==24,"invite entropy");
Check(!games.Join(sample.Id,42,"ru"),"self join");
Check(games.Join(sample.Id,84,"uk"),"join");
Check(!games.Join(sample.Id,85,"en"),"third join");
Check(!games.Move(sample.Id,99,sample.Revision,"0"),"stranger move");
Check(!games.Move(sample.Id,84,sample.Revision,"0"),"wrong turn");
int first=sample.Revision;Check(games.Move(sample.Id,42,first,"0"),"first move");
Check(!games.Move(sample.Id,42,first,"0"),"replay");
Check(!games.Move(sample.Id,84,sample.Revision,"0"),"occupied");
Check(!games.Move(sample.Id,84,sample.Revision,"/cmd"),"command injection");
foreach(var (user,cell) in new[]{(84L,"3"),(42L,"1"),(84L,"4"),(42L,"2")})Check(games.Move(sample.Id,user,sample.Revision,cell),"winning sequence");
Check(sample.Winner==1,"ttt winner");Check(!games.Move(sample.Id,84,sample.Revision,"8"),"postgame move");
// All games: membership, state privacy, UTF-8 budget in all supported languages, legal random complete play.
foreach(var kind in FriendGames.Catalog.Select(g=>g.Key))
{
 var room=games.Create(kind,100+checks);long a=room.A,b=a+1;Check(games.Join(room.Id,b,"en"),kind+" join");
 for(int turn=0;turn<800&&room.Winner==-1;turn++)
 {
  long user=room.Turn==0?a:b;if(kind=="rps")user=room.Aux[0]<0?a:b;
  foreach(var lang in new[]{"ru","en","uk"})
  {
   var dict=lang=="ru"?new Dictionary<string,string>():JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(Path.Combine("Assets","i18n",lang+".json")));
   string Tr(string s)=>dict.GetValueOrDefault(s,s);
   var view=JsonSerializer.Serialize(games.View(room,user,Tr),TelegramHub.WidgetJson);
   Check(WidgetPolicy.ValidData(view),kind+" state overflow "+lang+":"+Encoding.UTF8.GetByteCount(view));
   Check(!view.Contains("Secret")&&!view.Contains("Aux"),kind+" leaked internal state");
  }
  var document=JsonSerializer.SerializeToElement(games.View(room,user,s=>s));
  var ops=document.GetProperty("o").EnumerateArray().Select(x=>x.GetString()).Where(s=>s!="").Distinct().ToList();
  Check(ops.Count>0,kind+" no legal moves");
  var op=ops[Random.Shared.Next(ops.Count)];int revision=room.Revision;
  Check(games.Move(room.Id,user,revision,op),kind+" offered illegal op "+op);
  Check(!games.Move(room.Id,user,revision,op),kind+" stale op accepted");
 }
 Check(room.Winner!=-1,kind+" did not finish");
 Console.WriteLine(kind+" complete, revision="+room.Revision+", winner="+room.Winner);
}
var rps=games.Create("rps",51);games.Join(rps.Id,52,"en");games.Move(rps.Id,51,rps.Revision,"2");
var opponent=JsonSerializer.Serialize(games.View(rps,52,s=>s),TelegramHub.WidgetJson);Check(!opponent.Contains("✂️ :"),"rps revealed selection");
// Sea opponent's unhit ship positions must never be sent in the player's state.
var sea=games.Create("sea",61);games.Join(sea.Id,62,"en");var vsea=JsonSerializer.SerializeToElement(games.View(sea,61,s=>s));
Check(vsea.GetProperty("l").EnumerateArray().All(x=>x.GetString()=="·"),"sea hidden ships leaked");
var file=Path.Combine(Store.Dir,"persist.json");var persistent=new FriendGames(file);var pr=persistent.Create("ttt",71);persistent.Join(pr.Id,72,"uk");persistent.Move(pr.Id,71,pr.Revision,"4");
var restored=new FriendGames(file).Get(pr.Id);Check(restored.B==72&&restored.Board[4]==1,"restart loses state");
pr.Expires=DateTime.UtcNow.AddSeconds(-1);persistent.Save();Check(new FriendGames(file).Get(pr.Id)==null,"expired invite accepted");
// Execute actual isolated Telegram handler against an in-memory Bot API, not a rewritten mock handler.
var hub=new TelegramHub();
string Msg(long id,string text)=>JsonSerializer.Serialize(new{message=new{chat=new{id,type="private"},from=new{id,language_code="ru"},text}});
Check(!await hub.Update(Msg(99,"/cmd whoami")),"game boundary consumed PC command");
Check(await hub.Update(Msg(42,"/games")),"games menu not handled");
Check(hub.Sent.Any(s=>s.Method=="sendMessage"&&s.Json.Contains("game:new:ttt")),"menu has no game buttons");
Check(await hub.Update(JsonSerializer.Serialize(new{callback_query=new{id="c1",from=new{id=50,language_code="ru"},message=new{chat=new{id=50,type="private"}},data="game:new:ttt"}})),"create route");
var linkJson=hub.Sent.Last(s=>s.Method=="sendMessage").Json;
using var lj=JsonDocument.Parse(linkJson);var text=lj.RootElement.GetProperty("text").GetString();
var match=System.Text.RegularExpressions.Regex.Match(text,@"https://t.me/audit_bot\?start=game_([a-f0-9]{24})");Check(match.Success,"actual invite link missing");string gameId=match.Groups[1].Value;
hub.Sent.Clear();Check(await hub.Update(Msg(60,"/start game_"+gameId)),"invite route");Check(hub.Sent.Any(s=>s.Method=="sendWidget")&&hub.Sent.Any(s=>s.Method=="editMessageWidgetData"),"join did not update both players");
var state=JsonDocument.Parse(hub.Sent.First(s=>s.Method=="sendWidget").Json).RootElement.GetProperty("chat_data").GetString();
int rev=JsonDocument.Parse(state).RootElement.GetProperty("v").GetInt32();
string Query(long user,string data,long messageId=777)=>JsonSerializer.Serialize(new{widget_query=new{id="q1",from=new{id=user},chat=new{id=user,type="private"},type=new{type="data"},message_id=messageId,data}});
hub.Sent.Clear();Check(await hub.Update(Query(50,"g:"+gameId+":"+rev+":0")),"move route");
Check(hub.Sent.Count(s=>s.Method=="editMessageWidgetData")==2,"move did not push both states");
Check(hub.Sent.Any(s=>s.Method=="answerWidgetQuery"&&s.Json.Contains("data")),"move unacknowledged");
hub.Sent.Clear();await hub.Update(Query(70,"g:"+gameId+":"+(rev+1)+":1"));Check(hub.Sent.Count==1&&hub.Sent[0].Method=="answerWidgetQuery"&&!hub.Sent[0].Json.Contains("data"),"stranger received state or changed game");
hub.Sent.Clear();await hub.Update(Query(60,"g:"+gameId+":"+(rev+1)+":1",999));Check(hub.Sent.Count==1&&!hub.Sent[0].Json.Contains("data"),"forged message allowed move");
Check(Log.Errors.Count==0,"handler exception: "+string.Join(";",Log.Errors));
WidgetPolicy.ValidateHtml(FriendGameWidget.Html);
File.WriteAllText(Path.Combine(args.Length>0?args[0]:Store.Dir,"game.html"),FriendGameWidget.Html);
Console.WriteLine($"PASS {checks} assertions; 11 rulesets; invite/join/move sync via actual handler; privacy, expiry, replay, persistence checked. No real Telegram network calls.");
Directory.Delete(Store.Dir,true);
