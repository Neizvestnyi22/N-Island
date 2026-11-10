using NIsland.Core;
using System.Text.Json;
namespace NIsland.Core { public static class Store { public static string Dir=Path.Combine(Path.GetTempPath(),"nisland-game-audit-"+Guid.NewGuid().ToString("N")); } }
namespace NIsland.Services {
    public static class Log { public static List<string> Errors=new(); public static void Info(string s)=>Errors.Add(s); }
    public static class Integrations { public static string S(JsonElement e,string k)=>e.ValueKind==JsonValueKind.Object&&e.TryGetProperty(k,out var p)&&p.ValueKind==JsonValueKind.String?p.GetString():""; }
    public partial class TelegramHub {
        public List<(string Method,string Json)> Sent=new();
        readonly ReplyLedger replies=new();
        async Task CleanupReplies(string key,long chat,IEnumerable<ReplyLedger.Entry> sent,IEnumerable<long> keep){foreach(var old in replies.Replace(chat+":"+key,sent,keep,DateTime.UtcNow))await Call("deleteMessage",new{chat_id=old.Chat,message_id=old.Id});}
        public static JsonSerializerOptions WidgetJson=new(){Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping};
        public static string V(string key)=>key=="chat"?"42":"1";
        static object B(string text,string data)=>new{text,callback_data=data};
        static object[] Row(params object[] buttons)=>buttons;
        static object K(params object[][] rows)=>new{inline_keyboard=rows};
        public sealed class TgError:Exception{public readonly int Code;public TgError(string m,int code=400):base(m){Code=code;}}
        public Func<string,string,Exception> Failure;
        public long NextMessageId=777;
        public FriendGames.Room Room(string id)=>Games.Get(id);
        public void ClearThrottle()=>_gameLastMenu.Clear();
        Task<JsonElement> Call(string method,object body,CancellationToken ct=default){var json=JsonSerializer.Serialize(body,WidgetJson);Sent.Add((method,json));if(Failure?.Invoke(method,json) is {} error)throw error;return Task.FromResult(JsonSerializer.Deserialize<JsonElement>(method=="getMe"?"{\"username\":\"audit_bot\"}":"{\"message_id\":"+NextMessageId+"}"));}
        public Task<bool> Update(string json){using var d=JsonDocument.Parse(json);return FriendGameUpdate(d.RootElement.Clone());}
    }
}
