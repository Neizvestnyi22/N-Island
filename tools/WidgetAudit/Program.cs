using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using NIsland.Core;
var root = Path.GetFullPath(args.Length > 0 ? args[0] : ".");
var output = Path.GetFullPath(args.Length > 1 ? args[1] : "widget-audit");
Directory.CreateDirectory(output);
int checks = 0;
void Check(bool ok, string reason) { checks++; if (!ok) throw new Exception(reason); }
Check(WidgetTools.All.Length >= 16, "fewer than 16 new tools");
Check(WidgetTools.All.Select(t => t.Key).Distinct().Count() == WidgetTools.All.Length, "duplicate tool keys");
Check(WidgetPolicy.ValidQuery(new string('я',32)), "64-byte query rejected");
Check(!WidgetPolicy.ValidQuery(new string('я',33)), "66-byte query accepted");
Check(!WidgetPolicy.ValidQuery(""), "empty query accepted");
Check(!WidgetPolicy.ValidQuery("a\0b"), "NUL query accepted");
Check(WidgetPolicy.ValidData(new string('я',512)), "1024-byte data rejected");
Check(!WidgetPolicy.ValidData(new string('я',513)), "1026-byte data accepted");
Check(!WidgetPolicy.ValidData("a\0b"), "NUL data accepted");
foreach (string text in new[] {
    JsonSerializer.Serialize(new { k="player", h=string.Concat(Enumerable.Repeat("🫧Тест",100)), s=new string('ы',800), r=new object[]{new object[]{"t",new string('я',4000)}}, b=new object[]{new object[]{new object[]{"Play","play"}}} }),
    JsonSerializer.Serialize(new { k="menu", h="Каталог", r=Array.Empty<object>(), b=Enumerable.Range(0,20).Select(i=>new object[]{new object[]{"Очень длинная кнопка "+i,"go|counter"}}).ToArray() }) })
{
    var fitted = WidgetPolicy.FitState(text);
    Check(Encoding.UTF8.GetByteCount(fitted) <= 1000, "fit > 1000 bytes");
    Check(fitted.Contains("play") || fitted.Contains("go|counter"), "fitter corrupted actions");
    JsonDocument.Parse(fitted).Dispose();
}
var templates = new Dictionary<string,string>();
foreach (var file in Directory.GetFiles(Path.Combine(root,"Services"),"Telegram*.cs"))
{
    var source=File.ReadAllText(file);
    foreach(Match m in Regex.Matches(source,"const string (\\w+Html) = \"\"\"\\r?\\n(.*?)\\r?\\n\"\"\";", RegexOptions.Singleline)) templates[m.Groups[1].Value]=m.Groups[2].Value;
    // Initializer duplicate keys were a runtime type-initializer failure, not a compiler error.
    foreach(Match m in Regex.Matches(source,"(?:static readonly|readonly) Dictionary<[^;=]+> (\\w+) = new[^;]*?\\{(.*?)\\n        \\};",RegexOptions.Singleline))
    {
        var keys=Regex.Matches(m.Groups[2].Value,"\\[\"([^\"]+)\"\\]\\s*=").Select(x=>x.Groups[1].Value).ToArray();
        Check(keys.Distinct().Count()==keys.Length,"duplicate dictionary key in "+file+":"+m.Groups[1].Value);
    }
}
var manifest=new List<object>();
void Export(string key,string html)
{
    if (new[] { "ClockHtml", "CalcHtml", "DiceHtml", "CalendarHtml", "BreathHtml", "TttHtml", "CountdownHtml", "VizHtml" }.Any(k => key == k || key.StartsWith(k+"-"))) html = WidgetPolicy.RefreshButton(html, "top:6px;right:6px");
    html=WidgetPolicy.ProfileHtml(html); WidgetPolicy.ValidateHtml(html);
    File.WriteAllText(Path.Combine(output,key+".html"),html);
    manifest.Add(new{key,bytes=Encoding.UTF8.GetByteCount(html),zlib=WidgetPolicy.CompressedBytes(html)});
    checks++;
}
foreach(var tool in WidgetTools.All) Export(tool.Key,tool.Html);
foreach (var language in new[] { "en", "uk" })
{
    var dict = JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(Path.Combine(root,"Assets","i18n",language+".json")));
    foreach (var key in WidgetText.Keys) Check(dict.ContainsKey(key) && !string.IsNullOrWhiteSpace(dict[key]), language+" missing: "+key);
    string Translate(string key) => dict.TryGetValue(key,out var val) ? val : key;
    foreach(var tool in WidgetTools.All) Export(tool.Key+"-"+language,WidgetText.Localize(tool.Html,Translate));
    foreach(var tpl in templates.Where(t=>t.Key!="PetWidgetHtml")) Export(tpl.Key+"-"+language,WidgetText.Localize(tpl.Value,Translate));
}
foreach(var kv in templates.Where(k=>k.Key!="PetWidgetHtml")) Export(kv.Key,kv.Value);
// Same branch selection as PetHtml (no Windows types needed); all supported skins/accessories/moods.
var pet=templates["PetWidgetHtml"];
int worst=0; string worstName="";
foreach(var skin in new[]{"blob","cat","ghost","robot","bunny"})
foreach(var hat in new[]{"auto","none","cap","crown","bow","party","santa","headphones","glasses","flower"})
foreach(var mood in new[]{"idle","amazed","sneeze","headbang","scared","hot","sick","angry","cool"})
{
    var moods=new HashSet<string>(new[]{"idle","happy","love","party","laugh","dance","sleep","wink","surprised","kiss",mood});
    var hats=hat=="auto"?new[]{"headphones","party"}:new[]{hat=="santa"?"party":hat};
    var sb=new StringBuilder();
    foreach(var line in pet.Split('\n'))
    {
        var l=line.TrimEnd('\r');
        var mk=Regex.Match(l,@"^/\*([kh]):(\w+)\*/");
        if(mk.Success){if(mk.Groups[1].Value=="k"?mk.Groups[2].Value!=skin:!hats.Contains(mk.Groups[2].Value))continue;l=l[mk.Length..];}
        var cm=Regex.Match(l,@"^(?:case'(\w+)':)+");
        if(cm.Success&&!cm.Groups[1].Captures.Any(c=>moods.Contains(c.Value)))continue;
        sb.AppendLine(l);
    }
    var html=WidgetPolicy.ProfileHtml(sb.ToString()); WidgetPolicy.ValidateHtml(html); checks++;
    int z=WidgetPolicy.CompressedBytes(html);if(z>worst){worst=z;worstName=skin+"/"+hat+"/"+mood;}
    if(skin=="blob"&&hat=="auto"&&mood=="idle") File.WriteAllText(Path.Combine(output,"pet.html"),html);
}
File.WriteAllText(Path.Combine(output,"manifest.json"),JsonSerializer.Serialize(new{checks,tools=WidgetTools.All.Length,templates=templates.Count,worstPet=new{name=worstName,zlib=worst},widgets=manifest},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"PASS {checks} checks; {WidgetTools.All.Length} tools; {templates.Count} existing templates; worst pet {worstName}: {worst} zlib bytes");
