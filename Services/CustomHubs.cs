using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NIsland.Core;

namespace NIsland.Services
{
    /// <summary>Своя интеграция без программирования: что опрашивать и как показать.</summary>
    public class CustomHubDef
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
        public string Name { get; set; } = "Мой сервис";
        /// <summary>list — список записей из JSON; value — одно число с порогами; page — страница сайта изменилась; status — сайт/API отвечает.</summary>
        public string Kind { get; set; } = "list";
        public string Color { get; set; } = "#8E8E93";
    }

    /// <summary>
    /// Своя интеграция: любой JSON API, число с порогами, изменение страницы или проверка доступности.
    /// Настраивается полями в карточке (адрес, заголовки, пути в JSON), всё остальное — как у встроенных: опрос,
    /// записи на странице «Сервисы», всплывашки о новом и пересылка важного на телефон.
    /// </summary>
    public class CustomHub : Integration
    {
        public readonly CustomHubDef Def;
        public CustomHub(CustomHubDef d) { Def = d; }

        public override string Key => "x:" + Def.Id;
        public override string Name => Def.Name;
        public override string Glyph => Def.Kind switch { "value" => "", "page" => "", "status" => "", _ => "" };
        public override string Color => Def.Color;
        public override string Desc => Def.Kind switch
        {
            "value" => "Своя интеграция: число из JSON (курс, температура, баланс, счётчик) — всплывашка, когда выходит за пороги",
            "page" => "Своя интеграция: следит за страницей сайта и сообщает, когда она изменилась (цена, наличие, расписание)",
            "status" => "Своя интеграция: проверяет, что сайт или API отвечает, и сообщает, когда он упал и поднялся",
            _ => "Своя интеграция: записи из любого JSON API — новые показываются всплывашкой",
        };
        public override string Help => Def.Kind switch
        {
            "list" => "Путь в JSON — через точку, номер в квадратных скобках: data.items, items[0].title, user.login. Пусто в «Список» — ответ сам является списком",
            "value" => "Путь к числу: bitcoin.usd, main.temp, data[0].price",
            "page" => "Необязательное регулярное выражение вырежет нужный кусок (например, цену): (\\d[\\d\\s]*) ₴ — иначе сравнивается весь текст страницы",
            _ => null,
        };
        public override int PollSeconds => Math.Clamp(int.TryParse(V("interval"), out var m) ? m : 15, 1, 1440) * 60;

        public override HubField[] Fields
        {
            get
            {
                var f = new List<HubField> { new("url", "Адрес", "https://…") };
                if (Def.Kind is "list" or "value" or "status")
                    f.Add(new HubField("headers", "Заголовки", "по строке «Имя: значение», например Authorization: Bearer …", "multi", Required: false));
                switch (Def.Kind)
                {
                    case "list":
                        f.Add(new("items", "Список", "путь к массиву, например data.items", Required: false));
                        f.Add(new("title", "Заголовок записи", "например title или name"));
                        f.Add(new("sub", "Подпись", "например author.name", Required: false));
                        f.Add(new("link", "Ссылка", "например url", Required: false));
                        f.Add(new("id", "Уникальный id", "по нему видно новые записи; пусто — заголовок", Required: false));
                        f.Add(new("notify", "Всплывашка о новых записях", "", "bool", Required: false, Default: "1"));
                        break;
                    case "value":
                        f.Add(new("value", "Путь к числу", "например bitcoin.usd"));
                        f.Add(new("label", "Подпись", "например «Биткоин»", Required: false));
                        f.Add(new("unit", "Единица", "$, °C, ₴", Required: false));
                        f.Add(new("above", "Сообщить, если больше", "", Required: false));
                        f.Add(new("below", "Сообщить, если меньше", "", Required: false));
                        f.Add(new("change", "Сообщить при изменении на, %", "например 5", Required: false));
                        break;
                    case "page":
                        f.Add(new("regex", "Что сравнивать (regex)", "необязательно", Required: false));
                        break;
                    case "status":
                        f.Add(new("expect", "Ожидаемый текст в ответе", "необязательно", Required: false));
                        break;
                }
                f.Add(new("important", "Важное — на телефон и со звуком", "", "bool", Required: false, Default: "0"));
                f.Add(new("interval", "Как часто, мин", "15", Required: false, Default: "15"));
                return f.ToArray();
            }
        }

        // ------------------------------------------------------------------
        //  Запрос и пути в JSON
        // ------------------------------------------------------------------

        async Task<(int Code, string Body, double Ms)> Fetch()
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, V("url").Trim());
            foreach (var line in (V("headers") ?? "").Split('\n'))
            {
                int c = line.IndexOf(':');
                if (c > 0) req.Headers.TryAddWithoutValidation(line[..c].Trim(), line[(c + 1)..].Trim());
            }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            using var res = await Http.SendAsync(req);
            var body = await res.Content.ReadAsStringAsync();
            return ((int)res.StatusCode, body, sw.Elapsed.TotalMilliseconds);
        }

        /// <summary>Путь «a.b[0].c» в JSON. Пустой путь — сам элемент.</summary>
        public static JsonElement? Path(JsonElement root, string path)
        {
            var cur = root;
            if (string.IsNullOrWhiteSpace(path)) return cur;
            foreach (Match m in Regex.Matches(path.Trim(), @"([^.\[\]]+)|\[(\d+)\]"))
            {
                if (m.Groups[2].Success)
                {
                    int i = int.Parse(m.Groups[2].Value);
                    if (cur.ValueKind != JsonValueKind.Array || i >= cur.GetArrayLength()) return null;
                    cur = cur[i];
                }
                else
                {
                    var name = m.Groups[1].Value;
                    if (cur.ValueKind == JsonValueKind.Array && int.TryParse(name, out var ix) && ix < cur.GetArrayLength()) { cur = cur[ix]; continue; }
                    if (cur.ValueKind != JsonValueKind.Object || !cur.TryGetProperty(name, out var next)) return null;
                    cur = next;
                }
            }
            return cur;
        }

        static string Text(JsonElement? e) => e is not { } v ? "" : v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => v.ToString(),
            JsonValueKind.Null or JsonValueKind.Undefined => "",
            _ => v.ToString().Length > 120 ? v.ToString()[..120] + "…" : v.ToString(),
        };

        void Say(string title, string sub, string url)
        {
            bool imp = Flag("important");
            Notify(title, sub, url, important: imp);
        }

        public override async Task Poll()
        {
            if (string.IsNullOrWhiteSpace(V("url"))) throw new InvalidOperationException("Укажи адрес");
            switch (Def.Kind)
            {
                case "list": await PollList(); break;
                case "value": await PollValue(); break;
                case "page": await PollPage(); break;
                default: await PollStatus(); break;
            }
        }

        async Task PollList()
        {
            var (code, body, _) = await Fetch();
            if (code >= 400) throw new HttpRequestException(Integrations.Snip(body), null, (System.Net.HttpStatusCode)code);
            using var doc = JsonDocument.Parse(body);
            var arr = Path(doc.RootElement, V("items"));
            if (arr is not { ValueKind: JsonValueKind.Array } list) throw new InvalidOperationException($"По пути «{V("items")}» нет списка — проверь поле «Список»");
            var rows = new List<HubRow>();
            var seen = (St("seen") ?? "").Split('\u0001').ToHashSet();
            bool first = St("seen") == null;
            var ids = new List<string>();
            foreach (var item in list.EnumerateArray().Take(30))
            {
                var title = Text(Path(item, V("title")));
                if (title == "") continue;
                var sub = V("sub") != "" ? Text(Path(item, V("sub"))) : "";
                var link = V("link") != "" ? Text(Path(item, V("link"))) : null;
                var id = V("id") != "" ? Text(Path(item, V("id"))) : title;
                ids.Add(id);
                rows.Add(new HubRow { Title = title, Sub = sub, Url = string.IsNullOrEmpty(link) ? null : link });
                if (!first && Flag("notify") && !seen.Contains(id) && ids.Count <= 3) Say(Name + ": " + title, sub, link);
            }
            SetSt("seen", string.Join('\u0001', ids.Concat(seen.Where(x => x != "")).Distinct().Take(300)));
            Rows = rows;
            Status = $"Записей: {rows.Count} · {DateTime.Now:HH:mm}";
        }

        async Task PollValue()
        {
            var (code, body, _) = await Fetch();
            if (code >= 400) throw new HttpRequestException(Integrations.Snip(body), null, (System.Net.HttpStatusCode)code);
            using var doc = JsonDocument.Parse(body);
            var raw = Text(Path(doc.RootElement, V("value")));
            if (!double.TryParse(raw.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                throw new InvalidOperationException($"По пути «{V("value")}» не число: «{raw}»");
            string label = V("label") is { Length: > 0 } l ? l : Name, unit = V("unit");
            string Show(double x) => x.ToString(Math.Abs(x) >= 100 ? "#,0.##" : "0.####", CultureInfo.InvariantCulture) + (unit != "" ? " " + unit : "");
            double? prev = double.TryParse(St("last"), NumberStyles.Float, CultureInfo.InvariantCulture, out var p) ? p : null;
            double Lim(string k) => double.TryParse(V(k).Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ? x : double.NaN;
            double above = Lim("above"), below = Lim("below"), change = Lim("change");
            // пороги: сообщаем один раз при пересечении, а не на каждом опросе
            string zone = !double.IsNaN(above) && v > above ? "above" : !double.IsNaN(below) && v < below ? "below" : "ok";
            if (zone != (St("zone") ?? "ok") && zone != "ok")
                Say($"{label}: {Show(v)}", zone == "above" ? $"выше {Show(above)}" : $"ниже {Show(below)}", V("url"));
            SetSt("zone", zone);
            if (prev is double pv && pv != 0 && !double.IsNaN(change) && change > 0)
            {
                var basis = double.TryParse(St("base"), NumberStyles.Float, CultureInfo.InvariantCulture, out var b) ? b : pv;
                double pct = (v - basis) / Math.Abs(basis) * 100;
                if (Math.Abs(pct) >= change) { Say($"{label}: {(pct > 0 ? "+" : "")}{pct:0.#}%", $"{Show(basis)} → {Show(v)}", V("url")); SetSt("base", v.ToString(CultureInfo.InvariantCulture)); }
                else if (St("base") == null) SetSt("base", pv.ToString(CultureInfo.InvariantCulture));
            }
            SetSt("last", v.ToString(CultureInfo.InvariantCulture));
            Rows = new List<HubRow> { new() { Title = $"{label}: {Show(v)}", Sub = (prev is double q && q != v ? $"было {Show(q)} · " : "") + DateTime.Now.ToString("HH:mm"), Url = null } };
            Status = Rows[0].Title;
        }

        async Task PollPage()
        {
            var (code, body, _) = await Fetch();
            if (code >= 400) throw new HttpRequestException("страница ответила " + code, null, (System.Net.HttpStatusCode)code);
            string text;
            if (V("regex") is { Length: > 0 } rx)
            {
                var m = Regex.Match(body, rx, RegexOptions.Singleline, TimeSpan.FromSeconds(2));
                text = m.Success ? (m.Groups.Count > 1 ? m.Groups[1].Value : m.Value).Trim() : "(не найдено)";
            }
            else
            {
                // весь текст страницы без разметки, скриптов и пробелов — чтобы не реагировать на смену рекламы и счётчиков
                var t = Regex.Replace(body, @"<(script|style|noscript)[\s\S]*?</\1>", " ", RegexOptions.IgnoreCase);
                t = Regex.Replace(t, "<[^>]+>", " ");
                text = Regex.Replace(System.Net.WebUtility.HtmlDecode(t), @"\s+", " ").Trim();
            }
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];
            var old = St("hash");
            if (old != null && old != hash) Say(Name + " — изменилось", V("regex") != "" ? text : "Страница обновилась", V("url"));
            if (old != hash) { SetSt("hash", hash); SetSt("changed", DateTime.Now.ToString("o")); }
            var changedAt = DateTime.TryParse(St("changed"), null, DateTimeStyles.RoundtripKind, out var ca) ? ca : DateTime.Now;
            Rows = new List<HubRow> { new() { Title = V("regex") != "" ? text : Name, Sub = "изменилось " + changedAt.ToString("d MMM, HH:mm", Fmt.Ru), Url = V("url") } };
            Status = "Проверено " + DateTime.Now.ToString("HH:mm");
        }

        async Task PollStatus()
        {
            bool up;
            string why;
            double ms = 0;
            try
            {
                var (code, body, t) = await Fetch();
                ms = t;
                up = code < 400 && (V("expect") == "" || body.Contains(V("expect"), StringComparison.OrdinalIgnoreCase));
                why = code >= 400 ? "ответ " + code : !up ? "нет ожидаемого текста" : $"{ms:0} мс";
            }
            catch (Exception e) { up = false; why = Integrations.Reason(e); }
            var was = St("up");
            if (was != null && was != (up ? "1" : "0")) Say(up ? $"{Name} снова работает" : $"{Name} не отвечает", why, V("url"));
            SetSt("up", up ? "1" : "0");
            Rows = new List<HubRow> { new() { Title = (up ? "🟢 " : "🔴 ") + Name, Sub = why + " · " + DateTime.Now.ToString("HH:mm"), Url = V("url") } };
            Status = up ? "Работает · " + why : "Не отвечает: " + why;
            if (!up) Ok = false;
        }
    }

    /// <summary>Свои интеграции: создать из шаблона, удалить.</summary>
    public static class CustomHubs
    {
        public record Template(string Name, string Kind, string Color, (string Key, string Value)[] Values, string Note);

        public static readonly Template[] Templates =
        {
            new("Свой JSON API", "list", "#8E8E93", Array.Empty<(string, string)>(), "Пустая — заполни адрес и пути сам"),
            new("Число с порогами", "value", "#30D158", Array.Empty<(string, string)>(), "Курс, температура, баланс — с уведомлением при выходе за пороги"),
            new("Изменилась страница", "page", "#FF9F0A", Array.Empty<(string, string)>(), "Цена товара, наличие, расписание, новости на сайте"),
            new("Сайт работает?", "status", "#FF453A", Array.Empty<(string, string)>(), "Сообщит, когда сайт упал и когда поднялся"),
            new("Биткоин (CoinGecko)", "value", "#F7931A", new[] { ("url", "https://api.coingecko.com/api/v3/simple/price?ids=bitcoin&vs_currencies=usd"), ("value", "bitcoin.usd"), ("label", "Биткоин"), ("unit", "$"), ("change", "5"), ("interval", "30") }, "Курс и скачки больше 5%"),
            new("Задачи (issues) репозитория GitHub", "list", "#24292E", new[] { ("url", "https://api.github.com/repos/OWNER/REPO/issues?state=open"), ("title", "title"), ("sub", "user.login"), ("link", "html_url"), ("id", "id"), ("interval", "30") }, "Замени OWNER/REPO"),
            new("Hacker News по слову", "list", "#FF6600", new[] { ("url", "https://hn.algolia.com/api/v1/search_by_date?tags=story&query=windows"), ("items", "hits"), ("title", "title"), ("sub", "author"), ("link", "url"), ("id", "objectID"), ("interval", "60") }, "Замени слово в конце адреса"),
            new("Погода по координатам (Open-Meteo)", "value", "#0A84FF", new[] { ("url", "https://api.open-meteo.com/v1/forecast?latitude=50.45&longitude=30.52&current=temperature_2m"), ("value", "current.temperature_2m"), ("label", "Температура"), ("unit", "°C"), ("below", "0"), ("interval", "30") }, "Сообщит о морозе"),
            new("Новые вопросы Stack Overflow по тегу", "list", "#F48024", new[] { ("url", "https://api.stackexchange.com/2.3/questions?order=desc&sort=creation&site=stackoverflow&tagged=c%23"), ("items", "items"), ("title", "title"), ("sub", "owner.display_name"), ("link", "link"), ("id", "question_id"), ("interval", "30") }, "Замени тег в адресе"),
        };

        public static CustomHub Create(Template t, string name = null)
        {
            var def = new CustomHubDef { Name = string.IsNullOrWhiteSpace(name) ? t.Name : name.Trim(), Kind = t.Kind, Color = t.Color };
            Store.Settings.CustomHubs ??= new();
            Store.Settings.CustomHubs.Add(def);
            var hub = new CustomHub(def);
            Integrations.All.Add(hub);
            foreach (var (k, v) in t.Values) Integrations.SetValue(hub, k, v);
            Store.SaveSettings();
            return hub;
        }

        public static void Remove(CustomHub hub)
        {
            Integrations.SetEnabled(hub, false);
            Integrations.All.Remove(hub);
            Store.Settings.CustomHubs?.RemoveAll(d => d.Id == hub.Def.Id);
            Store.Settings.Integrations?.Remove(hub.Key);
            Store.SaveSettings();
            Integrations.RaiseChanged();
        }

        public static void Rename(CustomHub hub, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            hub.Def.Name = name.Trim();
            Store.SaveSettings();
        }

        /// <summary>При запуске — добавить сохранённые свои интеграции к встроенным.</summary>
        public static void Load()
        {
            foreach (var d in Store.Settings.CustomHubs ?? new())
                if (Integrations.Get("x:" + d.Id) == null) Integrations.All.Add(new CustomHub(d));
        }
    }
}
