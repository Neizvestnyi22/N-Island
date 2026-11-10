using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace NIsland.Core
{
 public static class MenuRichRenderer
 {
  static List<string[]> ReadRows(string s){try{return JsonSerializer.Deserialize<List<string[]>>(s[1..^1])??new();}catch{return new();}}
        public static string Render(string html)
        {
            html ??= "";
            var sb = new StringBuilder();
            // блочные куски, внутри которых переносы трогать нельзя
            var parts = Regex.Split(html, @"(<pre[^>]*>.*?</pre>|<blockquote[^>]*>.*?</blockquote>|\u0002.*?\u0003)", RegexOptions.Singleline);
            bool first = true;
            foreach (var part in parts)
            {
                if (part.Length == 0) continue;
                if (part.StartsWith("<pre", StringComparison.Ordinal)) { sb.Append(part); first = false; continue; }
                if (part.StartsWith("<blockquote", StringComparison.Ordinal)) { sb.Append(Regex.Replace(part, "\r?\n", "<br>")); first = false; continue; }
                if (part[0] == '\u0002')   // не StartsWith(string): управляющие символы при сравнении по культуре игнорируются, и подходил любой текст
                {
                    var rows = ReadRows(part);
                    if (rows.Count == 0) continue;
                    sb.Append("<table bordered striped compact>");
                    for (int i = 0; i < rows.Count; i++)
                    {
                        sb.Append("<tr>");
                        foreach (var c in rows[i]) sb.Append(i == 0 ? "<th>" : "<td>").Append(System.Net.WebUtility.HtmlEncode(c)).Append(i == 0 ? "</th>" : "</td>");
                        sb.Append("</tr>");
                    }
                    sb.Append("</table>");
                    first = false;
                    continue;
                }
                foreach (var para in Regex.Split(part.Trim('\n'), @"\n\s*\n"))
                {
                    var p = para.Trim('\n');
                    if (p.Trim().Length == 0) continue;
                    // «🎛 <b>Заголовок</b>» в самом начале — настоящий заголовок
                    var head = Regex.Match(p, @"^([^\n<]{0,6})<b>([^<\n]{1,80})</b>([^\n<]{0,40})(\n|$)");
                    if (head.Success)
                    {
                        sb.Append(first?"<h3>":"<h4>").Append(head.Groups[1].Value).Append(head.Groups[2].Value).Append(head.Groups[3].Value).Append(first?"</h3>":"</h4>");
                        p = p[head.Length..];
                        if (p.Trim().Length == 0) { first = false; continue; }
                    }
                    var lines=p.Split('\n');
                    if(lines.Length>1&&lines.All(l=>Regex.IsMatch(l,@"^\s*[•·]\s+")))
                    {sb.Append("<ul>");foreach(var line in lines)sb.Append("<li>").Append(Regex.Replace(line,@"^\s*[•·]\s+", "")).Append("</li>");sb.Append("</ul>");}
                    else sb.Append("<p>").Append(p.Replace("\n", "<br>")).Append("</p>");
                    first = false;
                }
            }
            return sb.ToString();
        }


 }
}
