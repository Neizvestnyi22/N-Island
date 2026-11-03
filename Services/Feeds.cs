using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace NIsland.Services
{
    /// <summary>Календари iCal (.ics): разовые события и повторы DAILY / WEEKLY / MONTHLY / YEARLY с INTERVAL, COUNT, UNTIL, BYDAY.</summary>
    public static class Ics
    {
        public static List<(DateTime Start, DateTime End, string Title, string Where, bool AllDay)> Parse(string text, DateTime from, DateTime to)
        {
            var res = new List<(DateTime, DateTime, string, string, bool)>();
            // склеиваем перенесённые строки (продолжение начинается с пробела или табуляции)
            var lines = Regex.Replace(text.Replace("\r\n", "\n"), @"\n[ \t]", "").Split('\n');
            Dictionary<string, (string Params, string Value)> ev = null;
            var exdates = new List<DateTime>();
            foreach (var raw in lines)
            {
                var line = raw.TrimEnd('\r');
                if (line == "BEGIN:VEVENT") { ev = new(StringComparer.OrdinalIgnoreCase); exdates.Clear(); continue; }
                if (line == "END:VEVENT" && ev != null)
                {
                    try { Expand(ev, exdates, from, to, res); } catch { }
                    ev = null;
                    continue;
                }
                if (ev == null) continue;
                int colon = line.IndexOf(':');
                if (colon <= 0) continue;
                var head = line[..colon];
                var value = line[(colon + 1)..];
                int semi = head.IndexOf(';');
                var name = semi < 0 ? head : head[..semi];
                var prms = semi < 0 ? "" : head[(semi + 1)..];
                if (name.Equals("EXDATE", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var v in value.Split(',')) if (TryDate(prms, v, out var d, out _)) exdates.Add(d);
                    continue;
                }
                ev[name] = (prms, value);
            }
            return res;
        }

        static string Unescape(string s) => s.Replace("\\n", " ").Replace("\\N", " ").Replace("\\,", ",").Replace("\\;", ";").Replace("\\\\", "\\").Trim();

        static void Expand(Dictionary<string, (string Params, string Value)> ev, List<DateTime> ex, DateTime from, DateTime to, List<(DateTime, DateTime, string, string, bool)> res)
        {
            if (ev.TryGetValue("STATUS", out var st) && st.Value.Equals("CANCELLED", StringComparison.OrdinalIgnoreCase)) return;
            if (!ev.TryGetValue("DTSTART", out var ds) || !TryDate(ds.Params, ds.Value, out var start, out bool allDay)) return;
            DateTime end = start + (allDay ? TimeSpan.FromDays(1) : TimeSpan.FromHours(1));
            if (ev.TryGetValue("DTEND", out var de) && TryDate(de.Params, de.Value, out var e2, out _)) end = e2;
            else if (ev.TryGetValue("DURATION", out var du)) end = start + ParseDuration(du.Value);
            var len = end - start;
            string title = ev.TryGetValue("SUMMARY", out var s) ? Unescape(s.Value) : "Событие";
            string where = ev.TryGetValue("LOCATION", out var l) ? Unescape(l.Value) : "";

            if (!ev.TryGetValue("RRULE", out var rr))
            {
                if (end >= from && start <= to) res.Add((start, end, title, where, allDay));
                return;
            }
            var rule = rr.Value.Split(';').Select(p => p.Split('=')).Where(p => p.Length == 2).ToDictionary(p => p[0].ToUpperInvariant(), p => p[1], StringComparer.OrdinalIgnoreCase);
            string freq = rule.TryGetValue("FREQ", out var f) ? f.ToUpperInvariant() : "";
            int interval = rule.TryGetValue("INTERVAL", out var iv) && int.TryParse(iv, out var ii) && ii > 0 ? ii : 1;
            int count = rule.TryGetValue("COUNT", out var cv) && int.TryParse(cv, out var cc) ? cc : int.MaxValue;
            DateTime until = rule.TryGetValue("UNTIL", out var uv) && TryDate("", uv, out var ud, out _) ? ud : DateTime.MaxValue;
            var byDay = rule.TryGetValue("BYDAY", out var bd)
                ? bd.Split(',').Select(x => Regex.Match(x, "(MO|TU|WE|TH|FR|SA|SU)$").Value).Where(x => x != "").Select(Day).ToHashSet()
                : null;

            int n = 0;
            for (int step = 0; step < 2000 && n < count; step++)
            {
                DateTime baseDate = freq switch
                {
                    "DAILY" => start.AddDays(step * interval),
                    "WEEKLY" => start.AddDays(7 * step * interval),
                    "MONTHLY" => start.AddMonths(step * interval),
                    "YEARLY" => start.AddYears(step * interval),
                    _ => DateTime.MaxValue,
                };
                if (baseDate == DateTime.MaxValue || baseDate > to || baseDate > until) break;
                IEnumerable<DateTime> occ = new[] { baseDate };
                if (freq == "WEEKLY" && byDay is { Count: > 0 })
                {
                    // все отмеченные дни той же недели (неделя с понедельника)
                    var monday = baseDate.AddDays(-(((int)baseDate.DayOfWeek + 6) % 7));
                    occ = Enumerable.Range(0, 7).Select(d => monday.AddDays(d)).Where(d => byDay.Contains(d.DayOfWeek) && d >= start);
                }
                foreach (var o in occ)
                {
                    if (n >= count || o > until) break;
                    n++;
                    if (ex.Any(x => Math.Abs((x - o).TotalMinutes) < 1)) continue;
                    if (o + len >= from && o <= to) res.Add((o, o + len, title, where, allDay));
                }
            }
        }

        static DayOfWeek Day(string s) => s switch
        {
            "MO" => DayOfWeek.Monday, "TU" => DayOfWeek.Tuesday, "WE" => DayOfWeek.Wednesday, "TH" => DayOfWeek.Thursday,
            "FR" => DayOfWeek.Friday, "SA" => DayOfWeek.Saturday, _ => DayOfWeek.Sunday,
        };

        static TimeSpan ParseDuration(string s)
        {
            var m = Regex.Match(s, @"P(?:(\d+)W)?(?:(\d+)D)?(?:T(?:(\d+)H)?(?:(\d+)M)?(?:(\d+)S)?)?");
            int G(int i) => m.Groups[i].Success ? int.Parse(m.Groups[i].Value) : 0;
            return m.Success ? new TimeSpan(G(1) * 7 + G(2), G(3), G(4), G(5)) : TimeSpan.FromHours(1);
        }

        /// <summary>20261103 (весь день), 20261103T120000Z (UTC), 20261103T120000 с TZID или местное.</summary>
        static bool TryDate(string prms, string v, out DateTime d, out bool allDay)
        {
            v = v.Trim();
            allDay = v.Length == 8;
            if (allDay) return DateTime.TryParseExact(v, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d);
            bool utc = v.EndsWith("Z");
            if (!DateTime.TryParseExact(v.TrimEnd('Z'), "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) return false;
            if (utc) { d = DateTime.SpecifyKind(d, DateTimeKind.Utc).ToLocalTime(); return true; }
            var tz = Regex.Match(prms ?? "", @"TZID=""?([^;:""]+)""?");
            if (tz.Success)
            {
                try
                {
                    var zone = TimeZoneInfo.FindSystemTimeZoneById(tz.Groups[1].Value);
                    d = TimeZoneInfo.ConvertTime(DateTime.SpecifyKind(d, DateTimeKind.Unspecified), zone, TimeZoneInfo.Local);
                }
                catch { }
            }
            return true;
        }
    }

    /// <summary>Ленты RSS 2.0 и Atom.</summary>
    public static class Rss
    {
        public static List<(DateTime At, string Title, string Link, string Feed)> Parse(string xml)
        {
            var res = new List<(DateTime, string, string, string)>();
            var doc = XDocument.Parse(xml);
            XNamespace atom = "http://www.w3.org/2005/Atom";
            var root = doc.Root;
            if (root == null) return res;
            if (root.Name.LocalName == "feed")
            {
                string feed = root.Element(atom + "title")?.Value?.Trim() ?? "Atom";
                foreach (var e in root.Elements(atom + "entry"))
                {
                    var link = e.Elements(atom + "link").FirstOrDefault(x => (string)x.Attribute("rel") is null or "alternate")?.Attribute("href")?.Value ?? "";
                    var when = Date(e.Element(atom + "published")?.Value ?? e.Element(atom + "updated")?.Value);
                    res.Add((when, Clean(e.Element(atom + "title")?.Value), link, feed));
                }
                return res;
            }
            var ch = root.Element("channel") ?? root;
            string name = ch.Element("title")?.Value?.Trim() ?? "RSS";
            foreach (var it in ch.Descendants().Where(x => x.Name.LocalName == "item"))
            {
                var link = it.Elements().FirstOrDefault(x => x.Name.LocalName == "link")?.Value?.Trim() ?? "";
                var date = it.Elements().FirstOrDefault(x => x.Name.LocalName is "pubDate" or "date")?.Value;
                res.Add((Date(date), Clean(it.Elements().FirstOrDefault(x => x.Name.LocalName == "title")?.Value), link, name));
            }
            return res;
        }

        static string Clean(string s) => Regex.Replace(System.Net.WebUtility.HtmlDecode(s ?? ""), @"<[^>]+>|\s+", " ").Trim();

        static DateTime Date(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return DateTime.Now;
            s = Regex.Replace(s.Trim(), @"\s(GMT|UT|UTC)$", " +0000");
            if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var d)) return d.LocalDateTime;
            // RFC 822 с названием зоны, которое .NET не знает («EST») — берём без зоны
            var m = Regex.Match(s, @"\d{1,2}\s\w{3}\s\d{4}\s\d{2}:\d{2}(:\d{2})?");
            return m.Success && DateTime.TryParse(m.Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d2) ? d2 : DateTime.Now;
        }
    }
}
