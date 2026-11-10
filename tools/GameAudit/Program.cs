using System.Text.Json;
using System.Text;
using NIsland.Core;
using NIsland.Services;
int checks=0;
void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
var games=new FriendGames();
Check(FriendGames.Catalog.Length>=29,"game count");
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
foreach(var kind in FriendGames.Catalog.Select(g=>g.Key).Where(k=>k!="chess"))
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

// Deterministic win/loss, phase transitions and all 26 persisted rulesets.
FriendGames.Room R(string kind){var r=games.Create(kind,9000+checks);Check(r!=null,kind+" create");Check(games.Join(r.Id,r.A+1,"en"),kind+" join deterministic");return r;}
bool M(FriendGames.Room r,string op)=>games.Move(r.Id,r.Turn==0?r.A:r.B,r.Revision,op);
var hx=R("hex");hx.Board=new[]{1,0,0,0,1,0,0,0,1,0,0,0,0,0,0,0};Check(M(hx,"12")&&hx.Winner==1,"hex path winner");
var gm=R("gomoku");gm.Board[0]=gm.Board[1]=gm.Board[2]=gm.Board[3]=1;Check(M(gm,"4")&&gm.Winner==1,"gomoku five");
var ch=R("chomp");Check(M(ch,"0")&&ch.Winner==2,"poison loser");
var ob=R("obstruction");Check(M(ob,"5")&&ob.Board.Count(v=>v==3)==8,"obstruction adjacency");
var iso=R("isolation");Check(M(iso,"1")&&iso.Turn==0&&iso.Pot==1,"isolation move phase");Check(!M(iso,"15"),"delete opponent");Check(M(iso,"0")&&iso.Turn==1&&iso.Pot==0,"isolation remove phase");
var bt=R("breakthrough");bt.Board=new int[16];bt.Board[4]=1;bt.Board[15]=2;Check(M(bt,"s4")&&M(bt,"0")&&bt.Winner==1,"breakthrough promotion");
var sos=R("sos");sos.Board[0]=1;sos.Board[1]=2;Check(M(sos,"2")&&sos.Scores[0]==1&&sos.Turn==0,"sos score extra turn");
var nt=R("numberttt");Check(!M(nt,"n2"),"odd player chose even");nt.Board[0]=1;nt.Board[1]=6;Check(M(nt,"n8")==false,"odd cannot choose eight");nt.Board[1]=5;Check(M(nt,"n9")&&M(nt,"2")&&nt.Winner==1,"sum15 win");
var ka=R("kayles");ka.Board=Enumerable.Repeat(1,12).ToArray();ka.Board[3]=ka.Board[4]=0;Check(M(ka,"3.2")&&ka.Winner==1,"kayles double");
var wy=R("wythoff");wy.Board=new[]{3,3};Check(M(wy,"d3")&&wy.Winner==1,"wythoff diagonal");
var eu=R("euclid");Check(!M(eu,"2"),"euclid excessive multiple");eu.Board=new[]{6,18};Check(M(eu,"3")&&eu.Winner==1,"euclid exact");
var tr=R("treble");tr.Board[0]=tr.Board[1]=1;Check(M(tr,"2")&&tr.Winner==1,"treble shared line");
var si=R("silver");Check(!M(si,"s0")&&M(si,"s2")&&!M(si,"5")&&M(si,"1"),"silver cannot jump/right");
var resign=R("ttt");Check(games.Move(resign.Id,resign.B,resign.Revision,"resign")&&resign.Winner==1,"off-turn resignation");
var pf=Path.Combine(Store.Dir,"all-rules.json");var pg=new FriendGames(pf);foreach(var k in FriendGames.Catalog.Select(g=>g.Key)){var x=pg.Create(k,40000+checks);Check(x!=null,"persist create");Check(pg.Join(x.Id,x.A+1,"ru"),"persist join");}
var restoredAll=new FriendGames(pf);foreach(var x in JsonSerializer.Deserialize<List<FriendGames.Room>>(File.ReadAllText(pf)))Check(restoredAll.Get(x.Id)!=null,x.Kind+" restore schema");

var chess=R("chess");
void ChessOp(string op){Check(M(chess,op),"chess "+op);}
foreach(var fen in new[]{new Chess.ChessBoard().ToFen(),"r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1"})
{
 var cb=Chess.ChessBoard.LoadFromFen(fen,Chess.AutoEndgameRules.None);
 long Perft(Chess.ChessBoard b,int depth){if(depth==0)return 1;long n=0;foreach(var move in b.Moves(generateSan:false)){if(!b.Move(move))throw new Exception("generated illegal");n+=Perft(b,depth-1);b.Cancel();}return n;}
 long expected=fen.StartsWith("rnbq")?8902:97862;Check(Perft(cb,3)==expected,"perft depth3 "+fen);
}
Check(!M(chess,"36"),"unselected chess move");ChessOp("s53");ChessOp("45");ChessOp("s12");ChessOp("28");ChessOp("s54");ChessOp("38");ChessOp("s3");ChessOp("39");Check(chess.Winner==2,"fools mate");
void Position(string fen){chess=R("chess");chess.Fen=fen;chess.Pot=-1;chess.Turn=fen.Contains(" b ")?1:0;chess.Positions=new(){FriendGames.ChessPositionKey(Chess.ChessBoard.LoadFromFen(fen,Chess.AutoEndgameRules.None))};}
Position("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1");ChessOp("s60");ChessOp("62");Check(chess.Fen.StartsWith("r3k2r/8/8/8/8/8/8/R4RK1"),"castle rook");
Position("4kr2/8/8/8/8/8/8/4K2R w K - 0 1");ChessOp("s60");Check(!M(chess,"62"),"castle through check");
Position("4k3/8/8/3pP3/8/8/8/4K3 w - d6 0 1");ChessOp("s28");ChessOp("19");Check(chess.Fen.Contains("3P4/8"),"en passant capture");
foreach(var promotion in new[]{"q","r","b","n"}){Position("7k/P7/8/8/8/8/8/7K w - - 0 1");ChessOp("s8");ChessOp("p"+promotion);ChessOp("0");Check(char.ToLowerInvariant(chess.Fen[0])==promotion[0],"promotion "+promotion);}
Position("7k/4Q3/6K1/8/8/8/8/8 w - - 0 1");ChessOp("s12");ChessOp("13");Check(chess.Winner==0,"stalemate");
Position(new Chess.ChessBoard().ToFen());foreach(var op in new[]{"s62","45","s6","21","s45","62","s21","6","s62","45","s6","21","s45","62","s21","6"})ChessOp(op);Check(chess.Winner==0,"repetition");
Position("7k/8/8/8/8/8/R7/K7 w - - 99 1");ChessOp("s48");ChessOp("49");Check(chess.Winner==0,"fifty moves");
Position("7k/8/8/8/8/8/N7/K7 w - - 0 1");ChessOp("s48");ChessOp("33");Check(chess.Winner==0,"dead material");
Position(new Chess.ChessBoard().ToFen());Check(games.Move(chess.Id,chess.A,chess.Revision,"draw")&&games.Move(chess.Id,chess.B,chess.Revision,"draw")&&chess.Winner==0,"draw agreement");
foreach(var lang in new[]{"ru","en","uk"}){
 var dict=lang=="ru"?new Dictionary<string,string>():JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText("Assets/i18n/"+lang+".json"));
 var c=R("chess");string view=JsonSerializer.Serialize(games.View(c,c.A,k=>dict.GetValueOrDefault(k,k)),TelegramHub.WidgetJson);Check(WidgetPolicy.ValidData(view),"chess state "+lang+" bytes="+Encoding.UTF8.GetByteCount(view));
 File.WriteAllText(Path.Combine(args[0],"chess-state-"+lang+".json"),view);
}
var cards=R("eights");var cardView=JsonSerializer.Serialize(games.View(cards,cards.A,x=>x),TelegramHub.WidgetJson);Check(!cardView.Contains("Deck")&&!cardView.Contains("Hands"),"card internal state leak");
Check(cards.Deck.Concat(cards.Discard).Concat(cards.Hands[0]).Concat(cards.Hands[1]).Distinct().Count()==52,"deck duplicate");
WidgetPolicy.ValidateHtml(FriendGameWidget.Html);
File.WriteAllText(Path.Combine(args.Length>0?args[0]:Store.Dir,"game.html"),FriendGameWidget.Html);
Console.WriteLine($"PASS {checks} assertions; 29 rulesets; invite/join/move sync via actual handler; privacy, expiry, replay, persistence checked. No real Telegram network calls.");
Directory.Delete(Store.Dir,true);
