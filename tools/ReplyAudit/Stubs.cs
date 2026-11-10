using System.Text.Json;
using NIsland.Core;
namespace NIsland.Core {public static class Store{public static string Dir=Path.Combine(Path.GetTempPath(),"reply-audit-"+Guid.NewGuid().ToString("N"));}}
namespace NIsland.Services
{
 public static class Log{public static List<string> Errors=new();public static void Info(string s)=>Errors.Add(s);}
 public static class Integrations{public static string S(JsonElement e,string k)=>e.TryGetProperty(k,out var v)&&v.ValueKind==JsonValueKind.String?v.GetString():"";}
 public partial class TelegramHub
 {
  static int nextId=100;
  public object Remote=null;
  readonly Dictionary<long,int> _widgets=new();readonly Dictionary<long,DateTime> _wSentAt=new();readonly Dictionary<long,int> _wBack=new();long _menuWidget;readonly Dictionary<string,(long Id,DateTime At,string Sig)> _menuMsgs=new();
  static (string Label,string Cmd)[] ReplyButtons={ ("Menu","menu") };
  static string T(string s)=>s;
  static string V(string k)=>k=="chat"?"42":"test-token-never-saved";
  public List<(string Method,JsonElement Body)> Sent=new();
  async Task<JsonElement> Call(string method,object body)
  {
   await Task.Yield();var b=JsonSerializer.SerializeToElement(body);lock(Sent)Sent.Add((method,b));
   var result=method.StartsWith("send")?JsonSerializer.SerializeToElement(new{message_id=Interlocked.Increment(ref nextId)}):JsonSerializer.SerializeToElement(true);
   ObserveReply(method,body,result);return result;
  }
  static bool GamePrivate(JsonElement m,out long id,out JsonElement from)
  {
   id=0;from=default;return m.TryGetProperty("chat",out var c)&&Integrations.S(c,"type")=="private"&&m.TryGetProperty("from",out from)&&c.GetProperty("id").GetInt64()==from.GetProperty("id").GetInt64()&&c.GetProperty("id").TryGetInt64(out id);
  }
  async Task HandleCore(JsonElement update)
  {
   var m=update.TryGetProperty("message",out var mm)?mm:update.GetProperty("callback_query").GetProperty("message");
   if(m.GetProperty("chat").GetProperty("id").GetInt64()!=42)return;
   var text=Integrations.S(m,"text");
   if(text.Contains("fail"))throw new InvalidOperationException("send failed");
   if(text.Contains("slow"))await Task.Delay(25);
   await Call("sendMessage",new{chat_id="42",text="result"});
   if(text.Contains("multi"))await Call("sendWidget",new{chat_id="42",html="test"});
  }
  public Task Update(string json){using var d=JsonDocument.Parse(json);return Handle(d.RootElement.Clone());}
 }
}
