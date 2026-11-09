using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NIsland.Core;

namespace NIsland.Services
{
    // =====================================================================
    //  Браузер с телефона: ссылки из чата сразу открываются на ПК (подозрительные — только после подтверждения),
    //  команды и пульт браузера (вкладки, назад/вперёд, прокрутка, масштаб, видео, инкогнито, избранное, недавние) и виджет «🧭 Браузер».
    // =====================================================================
    public partial class TelegramHub
    {
        static readonly (string Cmd, string Desc)[] Commands6 =
        {
            ("browser", "Пульт браузера"), ("tabs", "Окна браузера"), ("tab", "Вкладка: /tab next · prev · new · close · reopen · 3"),
            ("web", "Открыть ссылку или найти: /web котики"), ("incognito", "Инкогнито: /incognito сайт"), ("links", "Недавние ссылки"),
            ("fav", "Избранное: /fav add YouTube youtube.com · del 1"), ("back", "Браузер: назад"), ("forward", "Браузер: вперёд"),
            ("reload", "Обновить страницу"), ("scroll", "Прокрутка: /scroll down · up · top · bottom"), ("zoom", "Масштаб: /zoom in · out · 0"),
        };

        bool OpenLinksOn => V("open_links") != "0";

        static object BrowserKeys() => K(
            Row(B("◀", "bw:k:back"), B("↻", "bw:k:reload"), B("▶", "bw:k:fwd")),
            Row(B("⇤", "bw:k:prev"), B("➕", "bw:k:newtab"), B("✖", "bw:k:close"), B("⇥", "bw:k:next")),
            Row(B("⏫", "bw:k:top"), B("⬆", "bw:k:up"), B("⬇", "bw:k:down"), B("⏬", "bw:k:bottom")),
            Row(B("🔍−", "bw:k:zout"), B("100%", "bw:k:z0"), B("🔍+", "bw:k:zin"), B("⛶", "bw:k:full")),
            Row(B("⏯ Видео", "bw:k:play"), B("🔇", "bw:k:vmute"), B("↩ Вкладку", "bw:k:reopen")),
            Row(B("📑 Вкладки", "bw:tl"), B("📸 Снимок", "bw:s"), B("🔗 Ссылку сюда", "bw:l")),
            Row(B("🧠 Кратко", "bw:sum"), B("🔎 Найти", "ask:pagefind"), B("⌨️ Ввести", "ask:typein")),
            Row(B("⏪ 10", "bw:v:rw"), B("⏩ 10", "bw:v:ff"), B("🐢", "bw:v:slower"), B("🐇", "bw:v:faster")),
            Row(B("📋 Из буфера ПК", "bw:clip"), B("⭐ Сюда", "bw:fc"), B("⏻ Закрыть", "bw:q")),
            Row(B("🌐 Открыть ссылку", "ask:web"), B("🕵 Инкогнито", "ask:incognito")),
            Row(B("🧩 Виджет", "wg:browser"), B("🔄", "bw:p"), B("⬅️ Назад", "nav:main")));

        /// <summary>Текст пульта браузера: активная вкладка, адрес, окна.</summary>
        async Task<string> BrowserPanel()
        {
            var ws = await Task.Run(Browser.Windows);
            if (ws.Count == 0) return "🧭 <b>Браузер</b>\nНе открыт. Пришли ссылку — откроется в " + H(Browser.DefaultName() ?? "браузере по умолчанию") + ".";
            var t = ws.FirstOrDefault(w => w.Active) ?? ws[0];
            var url = await Task.Run(() => Browser.CurrentUrl(t));
            var sb = new StringBuilder($"🧭 <b>{H(Browser.NameOf(t.Process))}</b> · окон: {ws.Count}\n📄 {H(Cut(Browser.TabTitle(t), 120))}");
            if (url != null) sb.Append($"\n<code>{H(Cut(url, 300))}</code>");
            if (ws.Count > 1) sb.Append("\n\nДругие окна:\n" + string.Join("\n", ws.Where(w => w.Handle != t.Handle).Take(6).Select(w => "• " + H(Cut(Browser.TabTitle(w), 70)))));
            return sb.ToString();
        }

        async Task<(string Html, object Keys)?> Command6(string cmd, string arg)
        {
            string no;
            arg = arg.Trim();
            switch (cmd)
            {
                case "browser":
                    return (await BrowserPanel(), BrowserKeys());
                case "tab":
                {
                    string act = arg.ToLowerInvariant() switch { "next" or "след" => "next", "prev" or "пред" => "prev", "new" or "новая" => "newtab", "close" or "закрыть" => "close", "reopen" or "вернуть" => "reopen", "last" => "last", _ => null };
                    if (act == null && !int.TryParse(arg, out _)) return ("Напиши так: /tab next · prev · new · close · reopen · 3 (номер вкладки)", null);
                    if ((no = Need("control")) != null) return (no, null);
                    var r = await Task.Run(() => act != null ? Browser.Press(act) : Browser.Tab(int.Parse(arg)));
                    await Task.Delay(350);
                    return (r != null ? "⚠️ " + H(r) : await BrowserPanel(), BrowserKeys());
                }
                case "back": case "forward": case "fwd": case "reload":
                {
                    if ((no = Need("control")) != null) return (no, null);
                    var r = await Task.Run(() => Browser.Press(cmd == "back" ? "back" : cmd == "reload" ? "reload" : "fwd"));
                    await Task.Delay(500);
                    return (r != null ? "⚠️ " + H(r) : await BrowserPanel(), BrowserKeys());
                }
                case "scroll": case "zoom":
                {
                    string act = (cmd, arg.ToLowerInvariant()) switch
                    {
                        ("scroll", "down" or "вниз") => "down", ("scroll", "up" or "вверх") => "up", ("scroll", "top" or "начало") => "top", ("scroll", "bottom" or "конец") => "bottom",
                        ("zoom", "in" or "+") => "zin", ("zoom", "out" or "-") => "zout", ("zoom", "0" or "100") => "z0", _ => null,
                    };
                    if (act == null) return (cmd == "scroll" ? "Напиши так: /scroll down · up · top · bottom" : "Напиши так: /zoom in · out · 0", null);
                    if ((no = Need("control")) != null) return (no, null);
                    var r = await Task.Run(() => Browser.Press(act));
                    return (r != null ? "⚠️ " + H(r) : "✓", null);
                }
                case "web": case "incognito": case "private":
                {
                    if ((no = Need("control")) != null) return (no, null);
                    bool priv = cmd != "web";
                    if (arg == "") { Await(cmd); return (priv ? "🕵 Пришли ссылку или что найти — открою в режиме инкогнито" : "🌐 Пришли ссылку или что найти — открою в браузере на ПК", null); }
                    var r = await Ui(() => Browser.OpenOrSearch(arg, priv ? Browser.Mode.Private : Browser.Mode.Normal));
                    return (r != null ? "⚠️ " + H(r) : (priv ? "🕵 Открыто в режиме инкогнито" : "🌐 Открыто на ПК") + ": " + H(Cut(arg, 120)), K(Row(B("🧭 Пульт браузера", "bw:p"))));
                }
                case "links":
                {
                    var rec = Browser.Recent();
                    if (rec.Count == 0) return ("🕘 Ссылок ещё не открывали. Пришли ссылку — откроется на ПК", null);
                    return ("🕘 <b>Недавние ссылки</b>\n" + string.Join("\n", rec.Take(10).Select((u, i) => $"{i + 1}. {H(Cut(Browser.Short(u), 80))}")),
                            K(rec.Take(10).Select((u, i) => Row(B($"↗ {i + 1}. " + Cut(Browser.Short(u), 30), "bw:o:" + Put(u)))).ToArray()));
                }
                case "fav":
                {
                    var favs = Browser.Favs();
                    var parts = arg.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 0 && parts[0] is "add" or "добавить")
                    {
                        if (parts.Length < 3) return ("Напиши так: /fav add YouTube youtube.com", null);
                        if ((no = Need("control")) != null) return (no, null);
                        var links = Browser.FindLinks(parts[2]);
                        if (links.Count == 0) return ("Это не адрес сайта", null);
                        favs.RemoveAll(f => f.Name.Equals(parts[1], StringComparison.OrdinalIgnoreCase));
                        favs.Add((Cut(parts[1], 20), links[0]));
                        await Ui(() => Browser.SetFavs(favs.Take(8)));
                        return ("⭐ Добавлено: " + H(parts[1]), null);
                    }
                    if (parts.Length > 0 && parts[0] is "del" or "удалить")
                    {
                        if (parts.Length < 2 || !int.TryParse(parts[1], out var di) || di < 1 || di > favs.Count) return ("Напиши так: /fav del 1", null);
                        if ((no = Need("control")) != null) return (no, null);
                        favs.RemoveAt(di - 1);
                        await Ui(() => Browser.SetFavs(favs));
                        return ("⭐ Удалено", null);
                    }
                    if (parts.Length > 0) return ("Есть: /fav · /fav add Имя сайт · /fav del 1", null);
                    return ("⭐ <b>Избранное</b>\n" + (favs.Count == 0 ? "Пусто" : string.Join("\n", favs.Select((f, i) => $"{i + 1}. {H(f.Name)} — {H(Browser.Short(f.Url))}"))) +
                            "\n\nДобавить: /fav add YouTube youtube.com · удалить: /fav del 1",
                            favs.Count == 0 ? null : K(favs.Chunk(3).Select(ch => ch.Select(f => B("⭐ " + Cut(f.Name, 14), "bw:o:" + Put(f.Url))).ToArray()).ToArray()));
                }
            }
            return null;
        }

        /// <summary>Обычное сообщение со ссылками. true — обработано (ответ отправлен).</summary>
        async Task<bool> HandleLinks(string text)
        {
            var links = Browser.FindLinks(text);
            if (links.Count == 0) return false;
            bool only = Browser.OnlyLinks(text.Trim(), links);
            if (!only || !OpenLinksOn || !Flag("control"))
            {
                if (only && !Flag("control")) return false;   // как раньше — в остров
                if (!only && Flag("aitext")) return false;    // текст с ссылкой — вопрос ИИ
                // текст со ссылками или автооткрытие выключено — кнопки «Открыть на ПК»
                await Message(only ? "🔗 Ссылка" : "🔗 В сообщении ссылки:", K(links.Select(u => Row(B("🌐 " + Cut(Browser.Short(u), 32), "bw:o:" + Put(u)), B("🕵", "bw:i:" + Put(u)))).ToArray()));
                return only;
            }
            var opened = new List<string>();
            var warn = new StringBuilder();
            foreach (var u in links)
            {
                // фишинг: ссылка из чата открывается сама, только если проверка не нашла ничего опасного
                Osint.LinkReport rep = null;
                try { var tk = Osint.CheckLink(u); if (await Task.WhenAny(tk, Task.Delay(6000)) == tk) rep = await tk; } catch { }
                if (rep != null && rep.Risk >= 50)
                {
                    warn.Append($"\n⚠️ <b>{H(Browser.Short(u))}</b> — риск {rep.Risk}/100: {H(T(rep.Verdict))}");
                    await Message($"⚠️ Не открыл сам: <b>{H(Cut(Browser.Short(u), 80))}</b> похожа на опасную (риск {rep.Risk}/100)\n" +
                                  string.Join("\n", rep.Signals.Where(s => s.Bad).Take(4).Select(s => "• " + H(T(s.Text)))),
                        K(Row(B("Всё равно открыть", "bw:o:" + Put(u)), B("🕵 В инкогнито", "bw:i:" + Put(u)))));
                    continue;
                }
                var err = await Ui(() => Browser.Open(u));
                if (err == null) opened.Add(u);
            }
            if (opened.Count > 0)
            {
                OnUi(() => App.Island?.Toast("", "Открыто из Telegram", Browser.Short(opened[0])));
                await Message("🌐 Открыто на ПК: " + string.Join(", ", opened.Select(u => "<b>" + H(Cut(Browser.Short(u), 60)) + "</b>")),
                    K(Row(B("🕵 Инкогнито", "bw:i:" + Put(opened[0])), B("🪟 Новое окно", "bw:n:" + Put(opened[0]))),
                      Row(B("🧭 Пульт браузера", "bw:p"), B("⭐ В избранное", "bw:f:" + Put(opened[0])))), silent: true);
            }
            return true;
        }

        /// <summary>Кнопки «bw:…» в сообщениях.</summary>
        async Task<string> BrowserCallback(string rest, long msgId)
        {
            int c = rest.IndexOf(':');
            string op = c > 0 ? rest[..c] : rest, val = c > 0 ? rest[(c + 1)..] : "";
            if (op == "p")
            {
                await Edit(msgId, await BrowserPanel(), BrowserKeys());
                return "";
            }
            if (Need("control") is { } no) return no;
            switch (op)
            {
                case "k":
                {
                    var r = await Task.Run(() => Browser.Press(val));
                    if (r != null) return T(r);
                    await Task.Delay(val is "reload" or "back" or "fwd" or "newtab" or "close" or "reopen" or "next" or "prev" ? 500 : 150);
                    await Edit(msgId, await BrowserPanel(), BrowserKeys());
                    return Browser.Keys.TryGetValue(val, out var k) ? T(k.Label) : "✓";
                }
                case "o": case "i": case "n":
                {
                    var u = Get(val);
                    if (u == null) return Stale;
                    var r = await Ui(() => Browser.Open(u, op == "i" ? Browser.Mode.Private : op == "n" ? Browser.Mode.NewWindow : Browser.Mode.Normal));
                    return r != null ? T(r) : op == "i" ? T("🕵 Открыто в инкогнито") : T("🌐 Открыто на ПК");
                }
                case "f":
                {
                    var u = Get(val);
                    if (u == null) return Stale;
                    var favs = Browser.Favs();
                    if (favs.Any(f => f.Url == u)) return T("⭐ Уже в избранном");
                    favs.Add((Cut(Browser.Short(u).Split('/')[0], 20), u));
                    await Ui(() => Browser.SetFavs(favs.TakeLast(8)));
                    return T("⭐ В избранном");
                }
            }
            return await BrowserCallback2(op, val, msgId) ?? Stale;
        }

        async Task<(string Html, object Keys)?> WidgetInput5(string kind, string text)
        {
            if (kind is "browserf" or "browsert") return await WidgetInput7(kind, text);
            if (kind is not ("browser" or "browserp")) return null;
            var r = await Ui(() => Browser.OpenOrSearch(text, kind == "browserp" ? Browser.Mode.Private : Browser.Mode.Normal));
            WCSet("burl", null);
            _ = Task.Run(async () => { await Task.Delay(1500); await PushKind("browser"); });
            return (r != null ? "⚠️ " + H(r) : (kind == "browserp" ? "🕵 Открыто в инкогнито: " : "🌐 Открыто на ПК: ") + H(Cut(text, 100)), null);
        }

        // самопроверка: разбор ссылок без открытия браузера
        internal static List<string> TestFindLinks(string text) => Browser.FindLinks(text);
        internal static bool TestOnlyLinks(string text) => Browser.OnlyLinks(text, Browser.FindLinks(text));
    }
}
