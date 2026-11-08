using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using NIsland.Core;

namespace NIsland.Services
{
    // =====================================================================
    //  1.8: игры, наука, разработка — всё без ключей
    // =====================================================================

    /// <summary>Бесплатные игры: раздачи Epic, Steam, GOG и других (GamerPower).</summary>
    public class FreeGamesHub : Integration
    {
        public override string Key => "games";
        public override string Name => "Бесплатные игры";
        public override string Glyph => "";
        public override string Color => "#7C4DFF";
        public override string Desc => "Раздачи игр в Epic Games, Steam, GOG и других магазинах: всплывашка о новой бесплатной игре. Без регистрации";
        public override string HelpUrl => "https://www.gamerpower.com";
        public override HubField[] Fields => new[]
        {
            new HubField("platforms", "Магазины", "epic-games-store, steam, gog — через запятую; пусто — все для ПК", Required: false),
        };
        public override int PollSeconds => 3600;

        public override async Task Poll()
        {
            var plats = Lines("platforms").ToList();
            var url = "https://www.gamerpower.com/api/" + (plats.Count > 0 ? "filter?platform=" + Esc(string.Join(".", plats)) + "&type=game" : "giveaways?platform=pc&type=game");
            using var doc = await Integrations.GetJson(url);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) { Rows.Clear(); Status = "Сейчас раздач нет"; return; }
            var items = doc.RootElement.EnumerateArray().Take(15).Select(g => (
                Id: Integrations.S(g, "id"), Title: Integrations.S(g, "title").Replace(" Giveaways", "").Replace(" Giveaway", ""),
                Worth: Integrations.S(g, "worth"), End: Integrations.S(g, "end_date"), Url: Integrations.S(g, "open_giveaway_url"), Where: Integrations.S(g, "platforms"))).ToList();
            var seen = (St("seen") ?? "").Split(',').ToHashSet();
            if (St("seen") != null)
                foreach (var g in items.Where(g => !seen.Contains(g.Id)).Take(2))
                    Notify("Бесплатно: " + g.Title, (g.Worth is "" or "N/A" ? "" : "обычно " + g.Worth + " · ") + g.Where, g.Url, important: false);
            SetSt("seen", string.Join(",", items.Select(g => g.Id).Concat(seen.Where(x => x != "")).Distinct().Take(300)));
            Rows = items.Select(g => new HubRow
            {
                Title = g.Title,
                Sub = string.Join(" · ", new[] { g.Where, g.Worth is "" or "N/A" ? null : g.Worth, g.End is "" or "N/A" ? null : "до " + ShortDate(g.End) }.Where(x => !string.IsNullOrEmpty(x))),
                Url = g.Url, ActionText = "Забрать", Action = () => { SystemService.Run(g.Url); return Task.FromResult<string>(null); },
            }).ToList();
            Status = items.Count == 0 ? "Сейчас раздач нет" : $"Раздач: {items.Count}";
        }

        static string ShortDate(string s) => DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d.ToString("d MMM", Fmt.Ru) : s;
    }

    /// <summary>Скидки на игры (CheapShark: Steam, GOG, Humble и др.).</summary>
    public class GameDealsHub : Integration
    {
        public override string Key => "deals";
        public override string Name => "Скидки на игры";
        public override string Glyph => "";
        public override string Color => "#00B894";
        public override string Desc => "Лучшие скидки в Steam, GOG, Humble и других магазинах; можно следить за конкретными играми. Без регистрации";
        public override string HelpUrl => "https://www.cheapshark.com";
        public override HubField[] Fields => new[]
        {
            new HubField("max", "Цена до, $", "5", Required: false, Default: "10"),
            new HubField("watch", "Следить за играми", "названия через запятую — всплывашка, когда на них скидка от 50%", Required: false),
        };
        public override int PollSeconds => 3 * 3600;

        public override async Task Poll()
        {
            var max = double.TryParse(V("max").Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var m) ? m : 10;
            using var doc = await Integrations.GetJson($"https://www.cheapshark.com/api/1.0/deals?upperPrice={max.ToString(CultureInfo.InvariantCulture)}&pageSize=15&sortBy=Deal%20Rating&onSale=1");
            var rows = new List<HubRow>();
            foreach (var d in doc.RootElement.EnumerateArray())
            {
                var id = Integrations.S(d, "dealID");
                rows.Add(new HubRow
                {
                    Title = Integrations.S(d, "title"),
                    Sub = $"${Integrations.S(d, "salePrice")} вместо ${Integrations.S(d, "normalPrice")} · −{Math.Round(double.Parse(Integrations.S(d, "savings") is { Length: > 0 } sv ? sv : "0", CultureInfo.InvariantCulture))}%",
                    Url = "https://www.cheapshark.com/redirect?dealID=" + id,
                });
            }
            // слежка за играми
            var seen = (St("seen") ?? "").Split('|').ToHashSet();
            foreach (var game in Lines("watch").Take(8))
            {
                try
                {
                    using var g = await Integrations.GetJson("https://www.cheapshark.com/api/1.0/deals?title=" + Esc(game) + "&pageSize=3&sortBy=Savings");
                    foreach (var d in g.RootElement.EnumerateArray())
                    {
                        double.TryParse(Integrations.S(d, "savings"), NumberStyles.Float, CultureInfo.InvariantCulture, out var save);
                        var key = Integrations.S(d, "dealID");
                        if (save < 50 || seen.Contains(key)) continue;
                        seen.Add(key);
                        Notify($"Скидка −{Math.Round(save)}%: {Integrations.S(d, "title")}", $"${Integrations.S(d, "salePrice")} вместо ${Integrations.S(d, "normalPrice")}", "https://www.cheapshark.com/redirect?dealID=" + key);
                        break;
                    }
                }
                catch { }
            }
            SetSt("seen", string.Join("|", seen.Where(x => x != "").TakeLast(200)));
            Rows = rows;
            Status = $"Скидок: {rows.Count}";
        }
    }

    /// <summary>Землетрясения рядом и сильные в мире (USGS).</summary>
    public class QuakesHub : Integration
    {
        public override string Key => "quakes";
        public override string Name => "Землетрясения";
        public override string Glyph => "";
        public override string Color => "#E17055";
        public override string Desc => "Землетрясения рядом с тобой (город из погоды) и сильные по всему миру — от Геологической службы США. Без регистрации";
        public override HubField[] Fields => new[]
        {
            new HubField("radius", "Рядом — в радиусе, км", "1000", Required: false, Default: "1000"),
            new HubField("mag", "Сообщать от магнитуды", "4.5", Required: false, Default: "4.5"),
            new HubField("world", "Сильные в мире (от 6.5)", "", "bool", Required: false, Default: "1"),
        };
        public override int PollSeconds => 600;

        public override async Task Poll()
        {
            using var doc = await Integrations.GetJson("https://earthquake.usgs.gov/earthquakes/feed/v1.0/summary/2.5_day.geojson");
            var w = await WeatherService.Get();
            double radius = double.TryParse(V("radius"), out var r) ? r : 1000;
            double minMag = double.TryParse(V("mag").Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var mm) ? mm : 4.5;
            var list = new List<(string Id, double Mag, string Place, DateTime At, double Km, string Url)>();
            foreach (var f in doc.RootElement.GetProperty("features").EnumerateArray())
            {
                var p = f.GetProperty("properties");
                if (!p.TryGetProperty("mag", out var me) || me.ValueKind != JsonValueKind.Number) continue;
                var c = f.GetProperty("geometry").GetProperty("coordinates");
                double lon = c[0].GetDouble(), lat = c[1].GetDouble();
                double km = w != null ? Distance(w.Lat, w.Lon, lat, lon) : double.MaxValue;
                list.Add((f.GetProperty("id").GetString(), me.GetDouble(), Integrations.S(p, "place"),
                    DateTimeOffset.FromUnixTimeMilliseconds(p.GetProperty("time").GetInt64()).LocalDateTime, km, Integrations.S(p, "url")));
            }
            var near = list.Where(q => q.Km <= radius).OrderByDescending(q => q.At).ToList();
            var big = list.Where(q => q.Mag >= 6.5).OrderByDescending(q => q.At).ToList();
            var seen = (St("seen") ?? "").Split(',').ToHashSet();
            if (St("seen") != null)
            {
                foreach (var q in near.Where(q => q.Mag >= minMag && !seen.Contains(q.Id)).Take(2))
                    Notify($"Землетрясение M{q.Mag:0.0} в {Math.Round(q.Km)} км", q.Place + " · " + q.At.ToString("HH:mm"), q.Url, important: q.Km < 300 && q.Mag >= 5);
                if (Flag("world"))
                    foreach (var q in big.Where(q => !seen.Contains(q.Id) && q.Km > radius).Take(1))
                        Notify($"Сильное землетрясение M{q.Mag:0.0}", q.Place + " · " + q.At.ToString("HH:mm"), q.Url);
            }
            SetSt("seen", string.Join(",", list.Select(q => q.Id).Concat(seen.Where(x => x != "")).Distinct().Take(400)));
            Rows = near.Take(6).Concat(big.Take(4)).Distinct().Select(q => new HubRow
            {
                Title = $"M{q.Mag:0.0} · {q.Place}", Sub = (q.Km < 20000 ? $"{Math.Round(q.Km)} км от тебя · " : "") + q.At.ToString("d MMM, HH:mm", Fmt.Ru), Url = q.Url,
            }).ToList();
            Status = near.Count == 0 ? "Рядом спокойно" : $"Рядом за сутки: {near.Count}";
        }

        static double Distance(double lat1, double lon1, double lat2, double lon2)
        {
            double R = 6371, dLat = (lat2 - lat1) * Math.PI / 180, dLon = (lon2 - lon1) * Math.PI / 180;
            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        }
    }

    /// <summary>Магнитные бури и шанс северного сияния (NOAA, индекс Kp).</summary>
    public class SpaceWeatherHub : Integration
    {
        public override string Key => "kp";
        public override string Name => "Магнитные бури";
        public override string Glyph => "";
        public override string Color => "#00CEC9";
        public override string Desc => "Индекс Kp от NOAA: всплывашка о магнитной буре (Kp от 5) и о шансе увидеть северное сияние. Без регистрации";
        public override HubField[] Fields => new[] { new HubField("kp", "Сообщать от Kp", "5", Required: false, Default: "5") };
        public override int PollSeconds => 1800;

        static string Level(double kp) => kp >= 8 ? "очень сильная буря (G4–G5)" : kp >= 7 ? "сильная буря (G3)" : kp >= 6 ? "умеренная буря (G2)" : kp >= 5 ? "слабая буря (G1)" : kp >= 4 ? "возмущённо" : "спокойно";

        public override async Task Poll()
        {
            using var doc = await Integrations.GetJson("https://services.swpc.noaa.gov/products/noaa-planetary-k-index.json");
            var pts = new List<(DateTime T, double Kp)>();
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty("Kp", out var k) && DateTime.TryParse(Integrations.S(e, "time_tag"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t))
                    pts.Add((t.ToLocalTime(), k.GetDouble()));
                else if (e.ValueKind == JsonValueKind.Array && e.GetArrayLength() > 1 && double.TryParse(e[1].GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var k2)
                         && DateTime.TryParse(e[0].GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t2))
                    pts.Add((t2.ToLocalTime(), k2));
            }
            if (pts.Count == 0) throw new InvalidOperationException("NOAA не вернула данные");
            var last = pts[^1];
            double max24 = pts.Where(p => p.T > DateTime.Now.AddHours(-24)).Select(p => p.Kp).DefaultIfEmpty(last.Kp).Max();
            double notify = double.TryParse(V("kp").Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var nk) ? nk : 5;
            var stamp = last.T.ToString("yyyyMMddHH");
            if (last.Kp >= notify && St("last") != stamp)
            {
                SetSt("last", stamp);
                Notify($"Магнитная буря: Kp {last.Kp:0.#}", Level(last.Kp) + (last.Kp >= 7 ? " · возможно северное сияние в средних широтах" : ""), "https://www.swpc.noaa.gov/products/planetary-k-index", important: last.Kp >= 7);
            }
            Rows = new List<HubRow>
            {
                new() { Title = $"Сейчас Kp {last.Kp:0.#} — {Level(last.Kp)}", Sub = "обновлено " + last.T.ToString("HH:mm"), Url = "https://www.swpc.noaa.gov/products/planetary-k-index" },
                new() { Title = $"Максимум за сутки: Kp {max24:0.#}", Sub = string.Join("  ", pts.TakeLast(8).Select(p => p.Kp.ToString("0", CultureInfo.InvariantCulture))) },
            };
            Status = $"Kp {last.Kp:0.#} · {Level(last.Kp)}";
        }
    }

    /// <summary>Картинка дня NASA (APOD).</summary>
    public class ApodHub : Integration
    {
        public override string Key => "apod";
        public override string Name => "NASA: фото дня";
        public override string Glyph => "";
        public override string Color => "#0B3D91";
        public override string Desc => "Астрономическая картинка дня от NASA: открыть, поставить обоями. Работает с общим ключом; свой бесплатный ключ — больше запросов";
        public override string HelpUrl => "https://api.nasa.gov";
        public override HubField[] Fields => new[]
        {
            new HubField("key", "Ключ NASA API", "необязательно — без него DEMO_KEY", "secret", Required: false),
            new HubField("wall", "Каждый день — обоями рабочего стола", "", "bool", Required: false, Default: "0"),
        };
        public override int PollSeconds => 6 * 3600;

        public override async Task Poll()
        {
            var key = V("key") is { Length: > 0 } k ? k : "DEMO_KEY";
            using var doc = await Integrations.GetJson("https://api.nasa.gov/planetary/apod?api_key=" + Esc(key));
            var r = doc.RootElement;
            string title = Integrations.S(r, "title"), date = Integrations.S(r, "date"), url = Integrations.S(r, "url"), hd = Integrations.S(r, "hdurl"), type = Integrations.S(r, "media_type");
            var page = "https://apod.nasa.gov/apod/ap" + (DateTime.TryParse(date, out var d) ? d.ToString("yyMMdd") : "") + ".html";
            bool image = type == "image";
            if (St("date") != date)
            {
                SetSt("date", date);
                if (St("first") != null) Notify("NASA: фото дня", title, page);
                SetSt("first", "1");
                if (image && Flag("wall")) _ = SetWallpaper(hd != "" ? hd : url);
            }
            Rows = new List<HubRow>
            {
                new()
                {
                    Title = title, Sub = date + (image ? "" : " · видео"), Url = page,
                    ActionText = image ? "Обоями" : null,
                    Action = image ? async () => await SetWallpaper(hd != "" ? hd : url) : null,
                },
            };
            Status = "Обновлено " + DateTime.Now.ToString("HH:mm");
        }

        static async Task<string> SetWallpaper(string url)
        {
            try
            {
                var bytes = await Integrations.Http.GetByteArrayAsync(url);
                var err = PcControl.SetWallpaper(bytes, System.IO.Path.GetExtension(new Uri(url).AbsolutePath) is { Length: > 1 } e ? e : ".jpg");
                return err ?? "Обои изменены";
            }
            catch (Exception e) { return "Не удалось: " + Integrations.Reason(e); }
        }
    }

    /// <summary>Новые версии программ на GitHub.</summary>
    public class ReleasesHub : Integration
    {
        public override string Key => "releases";
        public override string Name => "Новые версии (GitHub)";
        public override string Glyph => "";
        public override string Color => "#24292E";
        public override string Desc => "Следит за выходом новых версий программ на GitHub: всплывашка со ссылкой на скачивание. Без регистрации";
        public override HubField[] Fields => new[]
        {
            new HubField("repos", "Репозитории", "владелец/проект по строке, например: obsproject/obs-studio", "multi"),
        };
        public override int PollSeconds => 3 * 3600;

        public override async Task Poll()
        {
            var rows = new List<HubRow>();
            foreach (var repo in Lines("repos").Take(20))
            {
                var name = repo.Replace("https://github.com/", "").Trim('/');
                try
                {
                    using var doc = await Integrations.GetJson($"https://api.github.com/repos/{name}/releases/latest");
                    var r = doc.RootElement;
                    string tag = Integrations.S(r, "tag_name"), url = Integrations.S(r, "html_url"), pub = Integrations.S(r, "published_at");
                    var key = "v:" + name;
                    if (St(key) != null && St(key) != tag) Notify($"Вышла {name.Split('/').Last()} {tag}", Integrations.S(r, "name"), url, important: false);
                    if (St(key) != tag) SetSt(key, tag);
                    rows.Add(new HubRow { Title = $"{name.Split('/').Last()} {tag}", Sub = name + (DateTime.TryParse(pub, out var d) ? " · " + d.ToString("d MMM yyyy", Fmt.Ru) : ""), Url = url });
                }
                catch (Exception e) { rows.Add(new HubRow { Title = name, Sub = "Ошибка: " + Integrations.Reason(e) }); }
            }
            Rows = rows;
            Status = $"Слежу за {rows.Count}";
        }
    }

    /// <summary>Этот день в истории (Википедия).</summary>
    public class OnThisDayHub : Integration
    {
        public override string Key => "onthisday";
        public override string Name => "Этот день в истории";
        public override string Glyph => "";
        public override string Color => "#636E72";
        public override string Desc => "События этого дня из Википедии на языке интерфейса. Без регистрации";
        public override HubField[] Fields => new[] { new HubField("morning", "Утром — всплывашка с фактом дня", "", "bool", Required: false, Default: "0") };
        public override int PollSeconds => 3 * 3600;

        public override async Task Poll()
        {
            var lang = I18n.Current is "uk" or "en" ? I18n.Current : "ru";
            var now = DateTime.Now;
            using var doc = await Integrations.GetJson($"https://api.wikimedia.org/feed/v1/wikipedia/{lang}/onthisday/selected/{now:MM}/{now:dd}");
            var items = doc.RootElement.TryGetProperty("selected", out var sel) ? sel.EnumerateArray().Take(10).Select(e => (
                Year: Integrations.S(e, "year"), Text: Integrations.S(e, "text"),
                Url: e.TryGetProperty("pages", out var pg) && pg.GetArrayLength() > 0 && pg[0].TryGetProperty("content_urls", out var cu)
                     && cu.TryGetProperty("desktop", out var dk) ? Integrations.S(dk, "page") : null)).ToList() : new();
            if (Flag("morning") && St("day") != now.ToString("yyyyMMdd") && now.Hour >= 8 && items.Count > 0)
            {
                SetSt("day", now.ToString("yyyyMMdd"));
                var it = items[new Random().Next(items.Count)];
                Notify("Этот день: " + it.Year, it.Text, it.Url);
            }
            Rows = items.Select(i => new HubRow { Title = i.Year + " — " + i.Text, Sub = "Википедия", Url = i.Url }).ToList();
            Status = now.ToString("d MMMM", Fmt.Ru);
        }
    }
}
