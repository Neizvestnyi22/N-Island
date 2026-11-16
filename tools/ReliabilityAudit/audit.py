from pathlib import Path
import tempfile,subprocess,os
root=Path(__file__).resolve().parents[2]
s=(root/'Services/Telegram.cs').read_text(encoding='utf-8-sig')
call=s[s.index('        async Task<JsonElement> Call('):s.index('        static string H(')]
u=s[s.index('        async Task<long> Upload('):s.index('        static ByteArrayContent Bytes(')]
eq=(root/'Services/Equalizer.cs').read_text(encoding='utf-8-sig');pre=eq[eq.index('        public static double Preamp('):eq.index('        static void EnsureInclude(')]
prefix='''using System;using System.IO;using System.Linq;using System.Collections.Generic;using System.Net;using System.Net.Http;using System.Net.Http.Headers;using System.Text;using System.Text.Json;using System.Threading;using System.Threading.Tasks;using NIsland.Core;
class Integrations{public static string S(JsonElement e,string k)=>e.TryGetProperty(k,out var p)?p.GetString():"";public static string Snip(string s)=>s;}
class RemoteReply{public void Add(string s,bool replaceLast){}public void AddFile(string n,byte[] b,string c){}}
class Settings{public bool EqAutoGain=true,EqKeepLevel=false;public int EqBoost;}
class Equalizer{public static Settings S=new();
'''
suffix='''}
class Fake:HttpMessageHandler{public int Calls;public List<byte[]> Files=new();public bool DocumentFlag;public string Filename;public Queue<Func<HttpResponseMessage>> Responses=new();protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct){Calls++;if(r.Content is MultipartFormDataContent form){foreach(var p in form){var n=p.Headers.ContentDisposition?.Name?.Trim('"');if(n=="document"){Files.Add(await p.ReadAsByteArrayAsync(ct));Filename=p.Headers.ContentDisposition.FileName;}if(n=="disable_content_type_detection")DocumentFlag=await p.ReadAsStringAsync(ct)=="true";}}return Responses.Count>0?Responses.Dequeue()():new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\\"ok\\":true,\\"result\\":{\\"message_id\\":777}}")};}}
class Hub{
 public static Fake Net=new();public static HttpClient Long=new(Net),Big=new(Net);
 public RemoteReply Remote=null;public Func<string,string,string> TestSink=null;
 public static JsonSerializerOptions JsonOpts=new();static string V(string s)=>"42";static string Api(string m)=>"https://unit.test/"+m;static string Legacy(string s)=>s;static string Tr(string s)=>s;static bool QuietNow()=>false;static void ObserveReply(string m,object b,JsonElement r){}
 class TgError:Exception{public int Code,RetryAfter;public TgError(string m,int c,int r=0):base(m){Code=c;RetryAfter=r;}}
 public Task<JsonElement> TestCall(string m)=>Call(m,new{});public Task<long> TestUpload(byte[] b,string name)=>Upload("sendDocument","document",new ByteArrayContent(b),name);
'''
tests='''}
class Program{static int checks;static void Check(bool c,string m){checks++;if(!c)throw new Exception(m);}static async Task Main(){
var h=new Hub();Hub.Net.Responses.Enqueue(()=>throw new HttpRequestException("offline"));await h.TestCall("getMe");Check(Hub.Net.Calls==2,"safe read not retried");
int before=Hub.Net.Calls;Hub.Net.Responses.Enqueue(()=>throw new HttpRequestException("ambiguous send"));try{await h.TestCall("sendMessage");throw new Exception("send accepted");}catch(HttpRequestException){}Check(Hub.Net.Calls==before+1,"send replayed");
Hub.Net.Responses.Enqueue(()=>new HttpResponseMessage(HttpStatusCode.TooManyRequests){Content=new StringContent("{\\"ok\\":false,\\"error_code\\":429,\\"description\\":\\"rate\\",\\"parameters\\":{\\"retry_after\\":17}}")});before=Hub.Net.Calls;try{await h.TestCall("getMe");throw new Exception("429 ignored");}catch(Exception e){Check(e.Message=="rate","API error lost");}Check(Hub.Net.Calls==before+1,"429 immediate retry");
Check(BotRetryPolicy.Delay(1,429,17).TotalSeconds==17,"retry_after lost");Check(BotRetryPolicy.Delay(10).TotalSeconds==30,"backoff cap");Check(!BotRetryPolicy.MayRetry("sendWidget")&&!BotRetryPolicy.MayRetry("sendDocument"),"unsafe replay permitted");
foreach(var extension in new[]{".pdf",".docx",".png",".mp3",".mp4",".zip",".xyz"}){var data=Enumerable.Range(0,256).Select(i=>(byte)i).ToArray();long id=await h.TestUpload(data,"Оригинал"+extension);Check(id==777&&Hub.Net.Files.Last().SequenceEqual(data),"file bytes changed "+extension);Check(Hub.Net.DocumentFlag&&Hub.Net.Filename.Contains(extension),"native document metadata "+extension);}
Hub.Net.Responses.Enqueue(()=>new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\\"ok\\":false,\\"description\\":\\"failed\\"}")});try{await h.TestUpload(new byte[]{1},"bad.pdf");throw new Exception("false success");}catch(Exception e){Check(e.Message.Contains("подтвердить"),"failed upload accepted");}
Check(Equalizer.Preamp(new double[]{0,0})==0,"flat attenuated");Equalizer.S.EqAutoGain=false;Check(Equalizer.Preamp(new double[]{-3,-5})==0,"cuts attenuated unnecessarily");Check(Equalizer.Preamp(new double[]{6,3})==-6.5,"safe headroom changed");Equalizer.S.EqKeepLevel=true;Check(Equalizer.Preamp(new double[]{6,3})==0,"explicit level mode attenuated");Equalizer.S.EqBoost=0;Check(Equalizer.Preamp(new double[]{0})==0,"silent gain introduced");Equalizer.S.EqBoost=99;Check(Equalizer.Preamp(new double[]{0})==12,"boost cap");
Console.WriteLine($"PASS {checks} transport/native-file/EQ checks using actual methods; fake HTTP only, no Telegram or audio hardware.");}}
'''
with tempfile.TemporaryDirectory(prefix='nisland-reliability-') as td:
 p=Path(td);(p/'audit.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>')
 (p/'Program.cs').write_text(prefix+pre+suffix+call+u+tests)
 (p/'BotRetryPolicy.cs').write_text((root/'Core/BotRetryPolicy.cs').read_text())
 subprocess.run([os.environ.get('DOTNET','dotnet'),'run','--project',str(p/'audit.csproj')],check=True)
