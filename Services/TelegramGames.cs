using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NIsland.Core;

namespace NIsland.Services
{
    public partial class TelegramHub
    {
        // An explicit boundary before owner-only routing. Only game prefixes are consumed here.
        // There is no route from this handler to Command, Callback, WidgetAction or PC APIs.
        readonly SemaphoreSlim _gameGate = new(1, 1);
        FriendGames _friendGames;
        string _gameBotName;
        readonly Dictionary<long, DateTime> _gameLastMenu = new();
        readonly Dictionary<string, Dictionary<string,string>> _gameLanguages = new();
        FriendGames Games => _friendGames ??= new FriendGames(Path.Combine(Store.Dir, "friend-games.json"));
        string GameText(string language, string key)
        {
            language=FriendGames.Lang(language);
            if(language=="ru")return key;
            if(!_gameLanguages.TryGetValue(language,out var dict))
            {
                using var stream=typeof(TelegramHub).Assembly.GetManifestResourceStream("i18n."+language+".json");
                dict=stream==null?new():JsonSerializer.Deserialize<Dictionary<string,string>>(stream)??new();
                _gameLanguages[language]=dict;
            }
            return dict.GetValueOrDefault(key,key);
        }
        async Task<string> GameBotName()
        {
            if(_gameBotName!=null)return _gameBotName;
            var me=await Call("getMe",new{});var name=Integrations.S(me,"username");
            if(!Regex.IsMatch(name,@"^[A-Za-z0-9_]{5,32}$"))throw new InvalidOperationException("Bot username unavailable");
            return _gameBotName=name;
        }
        string GameState(FriendGames.Room room,long user)
        {
            var lang=user==room.A?room.LanguageA:room.LanguageB;
            var state=JsonSerializer.Serialize(Games.View(room,user,key=>GameText(lang,key)),WidgetJson);
            if(!WidgetPolicy.ValidData(state))throw new ArgumentException("Game state exceeds 1024 UTF-8 bytes");
            return state;
        }
        async Task GameMenu(long chat,string language,int page=0)
        {
            string TGame(string key)=>GameText(language,key);
            int pages=(FriendGames.Catalog.Length+7)/8;page=Math.Clamp(page,0,pages-1);
            var rows=FriendGames.Catalog.Skip(page*8).Take(8).Select(g=>Row(B(TGame(g.Title),"game:new:"+g.Key))).ToList();
            rows.Add(Row(page>0?B("◀","game:page:"+(page-1)):null,B((page+1)+" / "+pages,"game:page:"+page),page+1<pages?B("▶","game:page:"+(page+1)):null));
            var message=await Call("sendMessage",new{chat_id=chat,text=TGame("Игры с друзьями")+"\n"+TGame("ПК с N-Island должен быть включён. Приглашение действует 24 часа. Игроки не получают доступ к управлению ПК."),reply_markup=K(rows.ToArray())});
            if(message.TryGetProperty("message_id",out var id))await CleanupReplies("games-menu",chat,new[]{new ReplyLedger.Entry(chat,id.GetInt64(),DateTime.UtcNow)},Array.Empty<long>());
        }
        async Task GameInvite(FriendGames.Room room)
        {
            string lang=room.LanguageA;string link="https://t.me/"+await GameBotName()+"?start=game_"+room.Id;
            var message=await Call("sendMessage",new{chat_id=room.A,text=GameText(lang,"Отправь эту ссылку другу:")+"\n"+link+"\n"+GameText(lang,"Один соперник. Ссылка действует 24 часа. ПК должен быть включён."),link_preview_options=new{is_disabled=true}});
            if(message.TryGetProperty("message_id",out var id))await CleanupReplies("invite:"+room.Kind,room.A,new[]{new ReplyLedger.Entry(room.A,id.GetInt64(),DateTime.UtcNow)},Array.Empty<long>());
        }
        async Task GameSend(FriendGames.Room room,long user)
        {
            string state=GameState(room,user);string lang=user==room.A?room.LanguageA:room.LanguageB;
            string html=FriendGameWidget.Html;WidgetPolicy.ValidateHtml(html);
            var result=await Call("sendWidget",new{chat_id=user,html,width=340,allow_queries=true,chat_data=state,caption=GameText(lang,"Игры с друзьями")+" · N-Island"});
            if(result.TryGetProperty("message_id",out var mi))
            {
                Games.RememberMessage(room,user,mi.GetInt64());
                await CleanupReplies("game:"+room.Id,user,new[]{new ReplyLedger.Entry(user,mi.GetInt64(),DateTime.UtcNow)},Array.Empty<long>());
            }
        }
        async Task GamePush(FriendGames.Room room)
        {
            foreach(var (user,message) in new[]{(room.A,room.MessageA),(room.B,room.MessageB)})
            {
                if(user<=0||message<=0)continue;
                try { await Call("editMessageWidgetData",new{chat_id=user,message_id=message,chat_data=GameState(room,user)}); }
                catch(TgError e){if(!e.Message.Contains("not modified",StringComparison.OrdinalIgnoreCase))Log.Info("game update: "+e.Message);}
            }
        }
        async Task<bool> FriendGameUpdate(JsonElement update)
        {
            if(V("friend_games")=="0") return false;
            // Unknown commands from friends are NOT interpreted as game moves.
            if(update.TryGetProperty("message",out var message))
            {
                string text=Integrations.S(message,"text").Trim();var pieces=text.Split(' ',2,StringSplitOptions.RemoveEmptyEntries);
                string command=pieces.Length>0?pieces[0].Split('@')[0].ToLowerInvariant():"";
                bool invite=command=="/start"&&pieces.Length==2&&pieces[1].StartsWith("game_",StringComparison.Ordinal);
                if(command!="/games"&&!invite)return false;
                if(!GamePrivate(message,out long user,out var from))return true;
                string lang=Integrations.S(from,"language_code");
                await _gameGate.WaitAsync();
                try
                {
                    if(_gameLastMenu.TryGetValue(user,out var last)&&(DateTime.UtcNow-last).TotalSeconds<2)return true;
                    if(_gameLastMenu.Count>1000)foreach(var k in _gameLastMenu.Where(x=>(DateTime.UtcNow-x.Value).TotalMinutes>5).Select(x=>x.Key).ToArray())_gameLastMenu.Remove(k);
                    _gameLastMenu[user]=DateTime.UtcNow;
                    if(!invite){await GameMenu(user,lang);return true;}
                    string id=pieces[1][5..];var room=Regex.IsMatch(id,@"^[a-f0-9]{24}$")?Games.Get(id):null;
                    if(room==null){await Call("sendMessage",new{chat_id=user,text=GameText(lang,"Игра не найдена или приглашение истекло.")});return true;}
                    if(room.A==user||room.B==user){await GameSend(room,user);return true;}
                    if(!Games.Join(id,user,lang)){await Call("sendMessage",new{chat_id=user,text=GameText(lang,"В этой игре уже есть соперник.")});return true;}
                    await GameSend(room,user);await GamePush(room);return true;
                }
                catch(Exception e){Log.Info("friend game: "+e.Message);try{await Call("sendMessage",new{chat_id=user,text=GameText(lang,"Игра сейчас недоступна. Обнови Telegram и попробуй /games позже.")});}catch{}return true;}
                finally{_gameGate.Release();}
            }
            if(update.TryGetProperty("callback_query",out var cb))
            {
                string data=Integrations.S(cb,"data");if(!data.StartsWith("game:",StringComparison.Ordinal))return false;
                string qid=Integrations.S(cb,"id");
                try
                {
                    if(!cb.TryGetProperty("message",out var cm)||!cb.TryGetProperty("from",out var from)||!cm.TryGetProperty("chat",out var chat)||Integrations.S(chat,"type")!="private"||!chat.TryGetProperty("id",out var cid)||!from.TryGetProperty("id",out var fid)||cid.ToString()!=fid.ToString()||!cid.TryGetInt64(out long user)||user<=0)return true;
                    string lang=Integrations.S(from,"language_code");
                    await _gameGate.WaitAsync();
                    try
                    {
                        if(data=="game:menu"){await GameMenu(user,lang);return true;}
                        if(data.StartsWith("game:page:")&&int.TryParse(data[10..],out int page)){await GameMenu(user,lang,page);return true;}
                        if(!data.StartsWith("game:new:",StringComparison.Ordinal))return true;
                        if(_gameLastMenu.TryGetValue(user,out var last)&&(DateTime.UtcNow-last).TotalSeconds<2)return true;
                        _gameLastMenu[user]=DateTime.UtcNow;
                        var room=Games.Pending(data[9..],user)??Games.Create(data[9..],user,lang);
                        if(room==null){await Call("sendMessage",new{chat_id=user,text=GameText(lang,"Лимит активных игр. Дождись окончания или истечения приглашений.")});return true;}
                        await GameSend(room,user);await GameInvite(room);
                    }
                    finally{_gameGate.Release();}
                }
                catch(Exception e){Log.Info("friend game creation: "+e.Message);}
                finally{try{await Call("answerCallbackQuery",new{callback_query_id=qid});}catch{}}
                return true;
            }
            if(update.TryGetProperty("widget_query",out var query))
            {
                string data=Integrations.S(query,"data");if(!data.StartsWith("g:",StringComparison.Ordinal))return false;
                string qid=Integrations.S(query,"id"),result=null;FriendGames.Room changed=null;
                try
                {
                    if(!WidgetPolicy.ValidQuery(data)||!query.TryGetProperty("chat",out var chat)||Integrations.S(chat,"type")!="private"||!query.TryGetProperty("from",out var from)||!chat.TryGetProperty("id",out var cid)||!from.TryGetProperty("id",out var fid)||cid.ToString()!=fid.ToString()||!cid.TryGetInt64(out long user)||user<=0)return true;
                    if(query.TryGetProperty("type",out var qt)&&(qt.ValueKind!=JsonValueKind.Object||Integrations.S(qt,"type")!="data"))return true;
                    var p=data.Split(':');if(p.Length!=4||!Regex.IsMatch(p[1],@"^[a-f0-9]{24}$")||!int.TryParse(p[2],out int rev))return true;
                    if (!await _gameGate.WaitAsync(1000)) return true;
                    try
                    {
                        var room=Games.Get(p[1]);if(room==null||(user!=room.A&&user!=room.B))return true;
                        // A forged request from another message cannot move this game.
                        if(!query.TryGetProperty("message_id",out var mid)||mid.GetInt64()!=(user==room.A?room.MessageA:room.MessageB))return true;
                        if(p[3]!="ref"&&Games.Move(room.Id,user,rev,p[3]))changed=room;
                        result=GameState(room,user);
                    }
                    finally{_gameGate.Release();}
                }
                catch(Exception e){Log.Info("friend game move: "+e.Message);}
                finally
                {
                    try{using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(4));await Call("answerWidgetQuery",result!=null?new{widget_query_id=qid,data=result}:(object)new{widget_query_id=qid},deadline.Token);}catch{}
                }
                if(changed!=null)
                {
                    await _gameGate.WaitAsync();try{await GamePush(changed);}finally{_gameGate.Release();}
                }
                return true;
            }
            return false;
        }
        static bool GamePrivate(JsonElement message,out long user,out JsonElement from)
        {
            user=0;from=default;
            return message.TryGetProperty("chat",out var chat)&&Integrations.S(chat,"type")=="private"&&message.TryGetProperty("from",out from)&&chat.TryGetProperty("id",out var cid)&&from.TryGetProperty("id",out var fid)&&cid.ToString()==fid.ToString()&&cid.TryGetInt64(out user)&&user>0;
        }
    }
}
