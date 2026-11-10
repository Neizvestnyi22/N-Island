using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NIsland.Core;
namespace NIsland.Services
{
    public partial class TelegramHub
    {
        sealed class ReplyScope
        {
            public bool Closed;
            public long Chat;
            public List<ReplyLedger.Entry> Sent=new();
            public HashSet<long> Keep=new();
        }
        sealed class ReplyTurn { public int Users; public SemaphoreSlim Gate=new(1,1); }
        readonly Dictionary<string,ReplyTurn> _replyTurns=new();
        async Task AcquireReplyTurn(string key)
        {
            ReplyTurn turn;lock(_replyTurns){if(!_replyTurns.TryGetValue(key,out turn))_replyTurns[key]=turn=new();turn.Users++;}
            await turn.Gate.WaitAsync();
        }
        void ReleaseReplyTurn(string key)
        {
            lock(_replyTurns){var turn=_replyTurns[key];turn.Gate.Release();if(--turn.Users==0){_replyTurns.Remove(key);turn.Gate.Dispose();}}
        }
        readonly AsyncLocal<ReplyScope> _replyScope=new();
        ReplyLedger _replyLedger;
        readonly SemaphoreSlim _replyGate=new(1,1);
        string ReplyKey(long chat,string purpose)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(V("token")+":"+chat+":"+purpose)));
        void ObserveReply(string method,object body,JsonElement result)
        {
            var scope=_replyScope.Value;if(scope==null||Remote!=null||scope.Closed)return;
            var b=JsonSerializer.SerializeToElement(body);
            if(!b.TryGetProperty("chat_id",out var c)||c.ToString()!=scope.Chat.ToString())return;
            if(method.StartsWith("send",StringComparison.Ordinal)&&result.ValueKind==JsonValueKind.Object&&result.TryGetProperty("message_id",out var mid)&&mid.TryGetInt64(out var id)&&id>0)
                lock(scope)scope.Sent.Add(new(scope.Chat,id,DateTime.UtcNow));
            if(method is "editMessageText" or "editMessageCaption" or "editMessageWidget" or "editMessageWidgetData")
                if(b.TryGetProperty("message_id",out var mid2)&&mid2.TryGetInt64(out long id2))lock(scope)scope.Keep.Add(id2);
        }
        async Task CleanupReplies(string purpose,long chat,IEnumerable<ReplyLedger.Entry> sent,IEnumerable<long> keep)
        {
            await _replyGate.WaitAsync();
            try
            {
                _replyLedger??=new ReplyLedger(Path.Combine(Store.Dir,"bot-replies.json"));
                foreach(var old in _replyLedger.Replace(ReplyKey(chat,purpose),sent,keep,DateTime.UtcNow))
                {
                    try { await Call("deleteMessage",new{chat_id=old.Chat,message_id=old.Id}); }
                    catch(Exception e){Log.Info("telegram previous reply delete: "+e.Message);}
                    if(old.Chat.ToString()==V("chat"))
                    {
                        lock(_widgets)_widgets.Remove(old.Id);
                        lock(_wSentAt)_wSentAt.Remove(old.Id);
                        lock(_wBack)_wBack.Remove(old.Id);
                        if(_menuWidget==old.Id)_menuWidget=0;
                        lock(_menuMsgs)foreach(var mn in _menuMsgs.Where(m=>m.Value.Id==old.Id).Select(m=>m.Key).ToArray())_menuMsgs.Remove(mn);
                    }
                }
            }
            catch(Exception e){Log.Info("telegram reply index: "+e.Message);}
            finally{_replyGate.Release();}
        }
        async Task Handle(JsonElement update)
        {
            // Scope only owner requests. Game chats are separately indexed; notifications are never swept.
            string purpose=null;long chat=0;
            if(update.TryGetProperty("message",out var message)&&GamePrivate(message,out chat,out _)&&chat.ToString()==V("chat"))
            {
                var text=Integrations.S(message,"text").Trim();
                if(text.StartsWith("/")&&!text.StartsWith("/games")&&!text.StartsWith("/start game_"))
                {var p=text.Split(new[]{' ','\n'},2,StringSplitOptions.RemoveEmptyEntries);purpose="cmd:"+p[0].Split('@')[0].ToLowerInvariant();if(p[0]=="/widget"&&p.Length>1)purpose+=":"+p[1].ToLowerInvariant();}
                else if(ReplyButtons.FirstOrDefault(b=>b.Label==text||T(b.Label)==text).Cmd is {} cmd)purpose="cmd:/"+cmd;
            }
            if(update.TryGetProperty("callback_query",out var cb)&&cb.TryGetProperty("message",out var cm)&&cb.TryGetProperty("from",out var from)&&cm.TryGetProperty("chat",out var ch)&&Integrations.S(ch,"type")=="private"&&ch.TryGetProperty("id",out var cid)&&from.TryGetProperty("id",out var fid)&&cid.ToString()==fid.ToString()&&cid.ToString()==V("chat")&&cid.TryGetInt64(out chat))
            {string data=Integrations.S(cb,"data");if(!data.StartsWith("game:"))purpose=data.StartsWith("do:")?"cmd:/"+data[3..].Split(' ')[0]:data.StartsWith("do2:")?"cmd:/"+data[4..].Split(' ')[0]:"button:"+data;}
            if(purpose==null||Remote!=null){await HandleCore(update);return;}
            await AcquireReplyTurn(ReplyKey(chat,purpose));
            var previous=_replyScope.Value;var scope=new ReplyScope{Chat=chat};_replyScope.Value=scope;
            try
            {
                await HandleCore(update);
                ReplyLedger.Entry[] sent;long[] keep;lock(scope){scope.Closed=true;sent=scope.Sent.ToArray();keep=scope.Keep.ToArray();}
                await CleanupReplies(purpose,chat,sent,keep);
            }
            finally{scope.Closed=true;_replyScope.Value=previous;ReleaseReplyTurn(ReplyKey(chat,purpose));}
        }
    }
}
