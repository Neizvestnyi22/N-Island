using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NIsland.Core;

namespace NIsland.Services
{
    // =====================================================================
    //  Браузер, вторая часть: настоящий список вкладок (переключить / закрыть любую), снимок страницы, ссылка на телефон,
    //  пересказ страницы ИИ, поиск по странице, ввод текста, видео (перемотка, скорость), ссылка из буфера ПК, в избранное,
    //  закрыть браузер (с подтверждением). Виджет «🧭 Браузер» с разделами: главное · вкладки · ещё.
    // =====================================================================
    public partial class TelegramHub
    {
        static readonly (string Cmd, string Desc)[] Commands7 =
        {
            ("pagefind", "Найти на странице: /pagefind слово"), ("typein", "Напечатать в браузере: /typein текст"), ("pageshot", "Снимок браузера"),
            ("pagelink", "Ссылку на открытую страницу"), ("summary", "Кратко о странице (ИИ)"), ("fromclip", "Открыть ссылку из буфера ПК"),
            ("video", "Видео: /video rw · ff · faster · slower · play"), ("closebrowser", "Закрыть браузер"),
        };

        static string _bwView = "main";
        DateTime _bwQuitAt;

        static string Lang() => I18n.Current switch { "uk" => "українською", "en" => "in English", _ => "на русском" };

        /// <summary>Список вкладок текущего окна браузера текстом и кнопками (переключить / закрыть).</summary>
        async Task<(string Html, object Keys)> TabsList()
        {
            var w = await Task.Run(Browser.Target);
            if (w == null) return ("🧭 Браузер не открыт. Пришли ссылку — откроется на ПК", null);
            var tabs = await Task.Run(() => Browser.Tabs(w));
            if (tabs.Count == 0) return ($"📑 {H(Browser.NameOf(w.Process))} не показывает список вкладок. Переключать можно кнопками ⇤ ⇥ в /browser", K(Row(B("🧭 Пульт браузера", "bw:p"))));
            var rows = new List<object[]>();
            var sb = new StringBuilder($"📑 <b>Вкладки</b> · {H(Browser.NameOf(w.Process))} · {tabs.Count}\n");
            for (int i = 0; i < tabs.Count && i < 20; i++)
            {
                sb.Append($"\n{(tabs[i].Selected ? "▶" : $"{i + 1}.")} {H(Cut(tabs[i].Name, 70))}");
                var id = Put($"{w.Handle}|{i}|{tabs[i].Name}");
                rows.Add(Row(B((tabs[i].Selected ? "▶ " : $"{i + 1}. ") + Cut(tabs[i].Name, 28), "bw:t:" + id), B("✖", "bw:x:" + id)));
            }
            rows.Add(Row(B("➕ Новая", "bw:k:newtab"), B("🔄", "bw:tl"), B("🧭 Пульт", "bw:p")));
            return (sb.ToString(), K(rows.ToArray()));
        }

        /// <summary>Вкладка из кнопки: то же окно, тот же номер и то же имя — иначе список устарел.</summary>
        static (PcControl.Win W, int Index)? TabFromKey(string v)
        {
            var p = v?.Split('|', 3);
            if (p == null || p.Length < 3 || !long.TryParse(p[0], out var h) || !int.TryParse(p[1], out var i)) return null;
            var w = Browser.Windows().FirstOrDefault(x => (long)x.Handle == h);
            if (w == null) return null;
            var tabs = Browser.Tabs(w);
            if (i < tabs.Count && tabs[i].Name == p[2]) return (w, i);
            int j = tabs.FindIndex(t => t.Name == p[2]);
            return j >= 0 ? (w, j) : null;
        }

        async Task<string> Summary(string url)
        {
            var page = await Browser.PageText(url);
            if (page == null || page.Value.Text.Length < 200) return null;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            var prompt = $"Перескажи страницу {Lang()}: 3–6 коротких пунктов, главное и цифры, без вступления.\n\nЗаголовок: {page.Value.Title}\nАдрес: {url}\n\n{page.Value.Text}";
            var ans = await Ai.Ask(new[] { new AiMessage("user", prompt) }, null, cts.Token, channel: "telegram");
            return Ai.StripThink(ans ?? "").Trim();
        }

        async Task<(string Html, object Keys)?> Command7(string cmd, string arg)
        {
            string no;
            arg = arg.Trim();
            switch (cmd)
            {
                case "tabs": return await TabsList();
                case "pagefind":
                    if ((no = Need("control")) != null) return (no, null);
                    if (arg == "") { Await("pagefind"); return ("🔎 Что найти на странице?", null); }
                    return (await Task.Run(() => Browser.Find(arg)) is { } fe ? "⚠️ " + H(fe) : "🔎 Ищу на странице: " + H(Cut(arg, 80)), null);
                case "typein":
                    if ((no = Need("input")) != null) return (no, null);
                    if (arg == "") { Await("typein"); return ("⌨️ Что напечатать в браузере? (в поле, где курсор; Enter нажму сам)", null); }
                    return (await Task.Run(() => Browser.TypeText(arg.Length > 1000 ? arg[..1000] : arg, true)) is { } te ? "⚠️ " + H(te) : "⌨️ Напечатано в браузере", null);
                case "pageshot":
                {
                    if ((no = Need("screen")) != null) return (no, null);
                    var w = await Task.Run(Browser.Target);
                    if (w == null) return ("🧭 Браузер не открыт", null);
                    await Task.Run(() => Browser.FocusWindow(w.Handle));
                    await Task.Delay(400);
                    await Screenshot(-1, false);
                    return null;
                }
                case "pagelink":
                {
                    var w = await Task.Run(Browser.Target);
                    if (w == null) return ("🧭 Браузер не открыт", null);
                    var url = await Task.Run(() => Browser.CurrentUrl(w));
                    if (url == null) return ("🔗 Браузер не показал адрес. Открыто: " + H(Browser.TabTitle(w)), null);
                    var full = url.StartsWith("http") ? url : "https://" + url;
                    return ($"🔗 <b>{H(Cut(Browser.TabTitle(w), 100))}</b>\n{H(full)}", K(Row(B("⭐ В избранное", "bw:f:" + Put(full)), B("🕵 Инкогнито", "bw:i:" + Put(full)))));
                }
                case "summary":
                {
                    if (arg == "")
                    {
                        var w = await Task.Run(Browser.Target);
                        if (w == null) return ("🧭 Браузер не открыт. Или пришли адрес: /summary сайт", null);
                        arg = await Task.Run(() => Browser.CurrentUrl(w)) ?? "";
                        if (arg == "") return ("🧠 Браузер не показал адрес страницы. Пришли его: /summary сайт", null);
                    }
                    await Action("typing");
                    var s = await Summary(arg);
                    return (s == null ? "🧠 Не получилось прочитать страницу (она закрыта для роботов или вся на JavaScript)" : $"🧠 <b>Кратко</b> · {H(Browser.Short(arg.StartsWith("http") ? arg : "https://" + arg))}\n\n{H(s)}", null);
                }
                case "fromclip":
                {
                    if ((no = Need("control")) != null) return (no, null);
                    string clip = null;
                    try { clip = await Ui(() => System.Windows.Clipboard.ContainsText() ? System.Windows.Clipboard.GetText() : null); } catch { }
                    var links = Browser.FindLinks(clip ?? "");
                    if (links.Count == 0) return ("📋 В буфере обмена ПК нет ссылки", null);
                    var r = await Ui(() => Browser.Open(links[0]));
                    return (r != null ? "⚠️ " + H(r) : "🌐 Открыто из буфера ПК: " + H(Browser.Short(links[0])), null);
                }
                case "video":
                {
                    var op = arg.ToLowerInvariant() switch { "rw" or "назад" => "rw", "ff" or "вперёд" or "вперед" => "ff", "faster" or "быстрее" => "faster", "slower" or "медленнее" => "slower", "play" or "пауза" => "play", _ => null };
                    if (op == null) return ("Напиши так: /video rw · ff · faster · slower · play", null);
                    if ((no = Need("control")) != null) return (no, null);
                    var r = await Task.Run(() => op == "play" ? Browser.Press("play") : Browser.Video(op));
                    return (r != null ? "⚠️ " + H(r) : "✓", null);
                }
                case "closebrowser":
                {
                    if ((no = Need("control")) != null) return (no, null);
                    if ((DateTime.Now - _bwQuitAt).TotalSeconds > 15)
                    {
                        _bwQuitAt = DateTime.Now;
                        return ("⏻ Закрыть все окна браузера? Повтори /closebrowser в течение 15 секунд (несохранённое браузер спросит сам)", K(Row(B("⏻ Да, закрыть", "bw:q"))));
                    }
                    _bwQuitAt = DateTime.MinValue;
                    return (await Task.Run(Browser.CloseBrowser) is { } ce ? "⚠️ " + H(ce) : "⏻ Браузер закрывается", null);
                }
            }
            return null;
        }

        /// <summary>Кнопки «bw:…», которых нет в первой части.</summary>
        async Task<string> BrowserCallback2(string op, string val, long msgId)
        {
            switch (op)
            {
                case "tl":
                {
                    var (h, k) = await TabsList();
                    await Edit(msgId, h, k);
                    return "";
                }
                case "t": case "x":
                {
                    var key = Get(val);
                    if (key == null) return Stale;
                    var tab = await Task.Run(() => TabFromKey(key));
                    if (tab == null) return T("Вкладки уже нет — обнови список");
                    var r = await Task.Run(() => op == "t" ? Browser.SelectTab(tab.Value.Index, tab.Value.W) : Browser.CloseTab(tab.Value.Index));
                    await Task.Delay(400);
                    var (h, k) = await TabsList();
                    await Edit(msgId, h, k);
                    return r != null ? T(r) : op == "t" ? "▶" : "✖";
                }
                case "s":
                {
                    if (Need("screen") is { } ns) return ns;
                    await Command7("pageshot", "");
                    return "📸";
                }
                case "l":
                {
                    var r = await Command7("pagelink", "");
                    if (r?.Html != null) await Message(r.Value.Html, r.Value.Keys);
                    return "🔗";
                }
                case "sum":
                    _ = Task.Run(async () => { try { var r = await Command7("summary", ""); if (r?.Html != null) await Message(r.Value.Html, r.Value.Keys); } catch (Exception e) { Log.Info("browser summary: " + e.Message); } });
                    return T("🧠 Читаю страницу…");
                case "v":
                {
                    var r = await Task.Run(() => Browser.Video(val));
                    return r != null ? T(r) : "✓";
                }
                case "clip":
                {
                    var r = await Command7("fromclip", "");
                    return r?.Html != null ? T(System.Text.RegularExpressions.Regex.Replace(r.Value.Html, "<[^>]+>", "")) : "✓";
                }
                case "fc":
                {
                    var w = await Task.Run(Browser.Target);
                    var url = w == null ? null : await Task.Run(() => Browser.CurrentUrl(w));
                    if (url == null) return T("Браузер не показал адрес страницы");
                    var full = url.StartsWith("http") ? url : "https://" + url;
                    var favs = Browser.Favs();
                    if (favs.Any(f => f.Url == full)) return T("⭐ Уже в избранном");
                    favs.Add((Cut(Browser.TabTitle(w), 20), full));
                    await Ui(() => Browser.SetFavs(favs.TakeLast(8)));
                    return T("⭐ В избранном");
                }
                case "q":
                {
                    var r = await Task.Run(Browser.CloseBrowser);
                    return r != null ? T(r) : T("⏻ Браузер закрывается");
                }
            }
            return null;
        }

        // ------------------------------------------------------------------
        //  Виджет «🧭 Браузер»: главное · вкладки · ещё
        // ------------------------------------------------------------------

        Dictionary<string, object> CardState5(string kind)
        {
            if (kind != "browser") return null;
            var ws = Browser.Windows();
            var t = ws.FirstOrDefault(w => w.Active) ?? ws.FirstOrDefault(w => !w.Minimized) ?? ws.FirstOrDefault();
            var d = Card(kind, "🧭 Браузер", t == null ? T("не открыт — пришли ссылку в чат") : Browser.NameOf(t.Process) + " · " + T("окон") + ": " + ws.Count);
            if (t != null)
            {
                CRows(d).Add(new object[] { "t", "📄 " + Cut(Browser.TabTitle(t), 70) });
                var url = WC<string>("burl");
                if (url == null || (DateTime.Now - WCAt("burl")).TotalSeconds > 4) WCSet("burl", url = Browser.CurrentUrl(t, 1500) ?? "");
                if (url != "" && _bwView != "tabs") CRows(d).Add(new object[] { "y", "📋", Cut(url, 110) });
            }
            if (WC<string>("bsum") is { } sum && _bwView == "more") CRows(d).Add(new object[] { "y", "📋", Cut(sum, 300) });
            if (Busy("browser")) CRows(d).Add(new object[] { "t", T("⏳ Читаю страницу…") });
            switch (_bwView)
            {
                case "tabs":
                {
                    var tabs = t == null ? new() : Browser.Tabs(t);
                    for (int i = 0; i < tabs.Count && i < 9; i++)
                        CRows(d).Add(new object[] { "l", (tabs[i].Selected ? "▶ " : $"{i + 1}. ") + Cut(tabs[i].Name, 30), tabs[i].Selected ? null : "tab|" + i, "→" });
                    if (t != null && tabs.Count == 0) CRows(d).Add(new object[] { "t", T("Браузер не показывает список вкладок — переключай ⇤ ⇥") });
                    Btns(d, Bt("⇤", "prev"), Bt("➕", "newtab"), Bt("✖", "close"), Bt("↩", "reopen"), Bt("⇥", "next"));
                    Btns(d, Bt("⬅ Пульт", "view|main"));
                    break;
                }
                case "more":
                    Btns(d, Bt("📸 Снимок", "shot"), Bt("🔗 Ссылку", "link"), Bt("🧠 Кратко", "sum"));
                    Btns(d, Bt("🔎 Найти", "find"), Bt("⌨️ Ввести", "type"), Bt("📋 Из буфера ПК", "clip"));
                    Btns(d, Bt("⏪10", "rw"), Bt("⏩10", "ff"), Bt("🐢", "slower"), Bt("🐇", "faster"));
                    Btns(d, Bt("⭐ Сюда", "fc"), Bt("🕵 Инкогнито", "inc"), Bt("⏻ Закрыть", "quit", "d"));
                    Btns(d, Bt("⬅ Пульт", "view|main"));
                    break;
                default:
                {
                    Btns(d, Bt("◀", "back"), Bt("↻", "reload"), Bt("▶", "fwd"));
                    Btns(d, Bt("⇤", "prev"), Bt("➕", "newtab"), Bt("✖", "close"), Bt("⇥", "next"));
                    Btns(d, Bt("⏫", "top"), Bt("⬆", "up"), Bt("⬇", "down"), Bt("⏬", "bottom"));
                    Btns(d, Bt("−", "zout"), Bt("100%", "z0"), Bt("+", "zin"), Bt("⛶", "full"), Bt("⏯", "play"));
                    Btns(d, Bt("✍️ Ссылка или поиск", "in", "p"), Bt("📑", "view|tabs"), Bt("🧩 Ещё", "view|more"));
                    var favs = Browser.Favs().Take(4).ToList();
                    if (favs.Count > 0) Btns(d, favs.Select((f, i) => Bt("⭐" + Cut(f.Name, 9), "fav|" + i)).ToArray());
                    foreach (var w in ws.Where(w => t != null && w.Handle != t.Handle).Take(2))
                        CRows(d).Add(new object[] { "l", "🪟 " + Cut(Browser.TabTitle(w), 30), "w|" + WHash(w.Handle.ToString()), "→" });
                    foreach (var u in Browser.Recent().Take(3))
                        CRows(d).Add(new object[] { "l", "🕘 " + Cut(Browser.Short(u), 30), "o|" + WHash(u), "↗" });
                    break;
                }
            }
            return d;
        }

        async Task<string> CardAction5(string kind, string head, string arg)
        {
            if (kind != "browser") return null;
            WCSet("burl", null);
            switch (head)
            {
                case "view": _bwView = arg is "tabs" or "more" ? arg : "main"; return null;
                case "inc":
                    Await("w:browserp");
                    await Message("🕵 Пришли ссылку или что найти — открою в режиме инкогнито (отмена: «-»)");
                    return T("✍️ Напиши в чат");
                case "find":
                    Await("w:browserf");
                    await Message("🔎 Что найти на странице? (отмена: «-»)");
                    return T("✍️ Напиши в чат");
                case "type":
                    if (!Flag("input")) return T("🔐 «Клавиатура и мышь» выключено — включается на ПК");
                    Await("w:browsert");
                    await Message("⌨️ Что напечатать в браузере? В поле, где курсор; Enter нажму сам (отмена: «-»)");
                    return T("✍️ Напиши в чат");
                case "tab" when int.TryParse(arg, out var ti):
                {
                    var r = await Task.Run(() => Browser.SelectTab(ti));
                    await Task.Delay(400);
                    return r != null ? T(r) : null;
                }
                case "shot":
                    if (!Flag("screen")) return T("🔐 «Снимок экрана» выключено — включается на ПК");
                    await Command7("pageshot", "");
                    return T("📸 Снимок в чате");
                case "link":
                {
                    var r = await Command7("pagelink", "");
                    if (r?.Html != null) await Message(r.Value.Html, r.Value.Keys);
                    return T("🔗 Ссылка в чате");
                }
                case "sum":
                {
                    var w = Browser.Target();
                    var url = w == null ? null : Browser.CurrentUrl(w);
                    if (url == null) return T("Браузер не показал адрес страницы");
                    Bg("browser", async () => { WCSet("bsum", await Summary(url) ?? T("Не получилось прочитать страницу")); return null; });
                    return null;
                }
                case "clip":
                {
                    var r = await Command7("fromclip", "");
                    await Task.Delay(600);
                    return r?.Html != null ? Cut(System.Text.RegularExpressions.Regex.Replace(r.Value.Html, "<[^>]+>", ""), 80) : null;
                }
                case "rw" or "ff" or "faster" or "slower":
                {
                    var r = await Task.Run(() => Browser.Video(head));
                    return r != null ? T(r) : null;
                }
                case "fc": return await BrowserCallback2("fc", "", 0);
                case "quit":
                    if ((DateTime.Now - _bwQuitAt).TotalSeconds > 10) { _bwQuitAt = DateTime.Now; return T("Нажми ещё раз в течение 10 секунд — все окна браузера закроются"); }
                    _bwQuitAt = DateTime.MinValue;
                    return await Task.Run(Browser.CloseBrowser) is { } qe ? T(qe) : T("⏻ Браузер закрывается");
                case "fav" when int.TryParse(arg, out var fi):
                {
                    var f = Browser.Favs().ElementAtOrDefault(fi);
                    if (f.Url == null) return T("Нет такого сайта в избранном");
                    var r = await Ui(() => Browser.Open(f.Url));
                    await Task.Delay(600);
                    return r != null ? T(r) : "⭐ " + f.Name;
                }
                case "o":
                {
                    var u = Browser.Recent().FirstOrDefault(x => WHash(x) == arg);
                    if (u == null) return T("Ссылки уже нет в списке");
                    var r = await Ui(() => Browser.Open(u));
                    await Task.Delay(600);
                    return r != null ? T(r) : "↗ " + Cut(Browser.Short(u), 40);
                }
                case "w":
                {
                    var w = Browser.Windows().FirstOrDefault(x => WHash(x.Handle.ToString()) == arg);
                    if (w == null) return T("Окно уже закрыто");
                    var r = await Task.Run(() => Browser.FocusWindow(w.Handle));
                    await Task.Delay(300);
                    return r != null ? T(r) : "🪟";
                }
                default:
                    if (!Browser.Keys.ContainsKey(head)) return null;
                    var res = await Task.Run(() => Browser.Press(head));
                    await Task.Delay(head is "reload" or "back" or "fwd" or "newtab" or "close" or "reopen" or "next" or "prev" or "full" ? 600 : 150);
                    return res != null ? T(res) : null;
            }
        }

        async Task<(string Html, object Keys)?> WidgetInput7(string kind, string text)
        {
            string r = kind == "browserf" ? await Task.Run(() => Browser.Find(text))
                     : Flag("input") ? await Task.Run(() => Browser.TypeText(text.Length > 1000 ? text[..1000] : text, true)) : "🔐 «Клавиатура и мышь» выключено";
            _ = Task.Run(async () => { await Task.Delay(800); await PushKind("browser"); });
            return (r != null ? "⚠️ " + H(r) : kind == "browserf" ? "🔎 Ищу на странице: " + H(Cut(text, 80)) : "⌨️ Напечатано в браузере", null);
        }
    }
}
