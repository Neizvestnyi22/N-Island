using System.Text.Json;
namespace NIsland.Core { public static class Store { public static string Dir=Path.Combine(Path.GetTempPath(),"nisland-game-audit-"+Guid.NewGuid().ToString("N")); } }
namespace NIsland.Services {
    public static class Log { public static List<string> Errors=new(); public static void Info(string s)=>Errors.Add(s); }
    public static class Integrations { public static string S(JsonElement e,string k)=>e.ValueKind==JsonValueKind.Object&&e.TryGetProperty(k,out var p)&&p.ValueKind==JsonValueKind.String?p.GetString():""; }
    public partial class TelegramHub {
        public List<(string Method,string Json)> Sent=new();
        public static JsonSerializerOptions WidgetJson=new(){Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping};
        public static string V(string key)=>key=="chat"?"42":"1";
        static object B(string text,string data)=>new{text,callback_data=data};
        static object[] Row(params object[] buttons)=>buttons;
        static object K(params object[][] rows)=>new{inline_keyboard=rows};
        public sealed class TgError:Exception{public TgError(string m):base(m){}}
        Task<JsonElement> Call(string method,object body,CancellationToken ct=default){Sent.Add((method,JsonSerializer.Serialize(body,WidgetJson)));return Task.FromResult(JsonSerializer.Deserialize<JsonElement>(method=="getMe"?"{\"username\":\"audit_bot\"}":"{\"message_id\":777}"));}
        public Task<bool> Update(string json){using var d=JsonDocument.Parse(json);return FriendGameUpdate(d.RootElement.Clone());}
    }
}
