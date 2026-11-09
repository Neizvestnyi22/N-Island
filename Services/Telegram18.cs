using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using NIsland.Core;

namespace NIsland.Services
{
    // =====================================================================
    //  Виджеты обновляются на месте, а не новыми сообщениями.
    //  • «🧩 Все виджеты» — один виджет-каталог по категориям: категория → список → нажал — этот же виджет становится выбранным.
    //  • Переходы внутри карточек («🎤 Текст», «🎛 EQ», «⏳ Лимиты»…) меняют этот же виджет, «⬅ Назад» возвращает прежний.
    //  • Меню /widgets — по категориям, листается в одном сообщении; нажатие на виджет обновляет последний виджет из меню,
    //    если оба — карточки. Новое сообщение — только для виджетов со своим оформлением (часы, калькулятор, игры, пульт, персонаж…).
    // =====================================================================
    public partial class TelegramHub
    {
        /// <summary>Виджеты со своим HTML — их нельзя превратить в другой на месте (у карточек HTML общий).</summary>
        static readonly HashSet<string> OwnHtml = new() { "remote", "pet", "sysmon", "clock", "calc", "dice", "calendar", "breath", "ttt", "g2048", "countdown", "viz" };
        static bool IsCard(string kind) => WK(kind) != null && !OwnHtml.Contains(kind) && WidgetTools.Find(kind) == null;

        /// <summary>Сообщение → откуда пришли (стек видов для «⬅ Назад»).</summary>
        readonly Dictionary<long, List<string>> _wBack = new();
        /// <summary>Каталог: какая категория открыта в этом сообщении (−1 — список категорий).</summary>
        readonly System.Collections.Concurrent.ConcurrentDictionary<long, int> _hubCat = new();
        /// <summary>Последний виджет, открытый из меню /widgets, — его и обновляем при следующем выборе.</summary>
        long _menuWidget;

        static string[] Cats() => WKinds.Where(k => k.Key != "menu").Select(k => k.Group).Distinct().ToArray();

        readonly System.Collections.Concurrent.ConcurrentDictionary<long, int> _hubPage = new();
        int HubPage(long msgId) => _hubPage.GetValueOrDefault(msgId, 0);

        /// <summary>Six entries per page fit chat_data and a 600px bubble without scrolling.</summary>
        Dictionary<string, object> MenuState(int cat, int page = 0)
        {
            var cats = Cats();
            bool root = cat < 0 || cat >= cats.Length;
            var list = root ? Array.Empty<WKind>() : WKinds.Where(k => k.Group == cats[cat] && k.Key != "menu").ToArray();
            int count = root ? cats.Length : list.Length;
            int pages = Math.Max(1, (count + 5) / 6);
            page = Math.Clamp(page, 0, pages - 1);
            var d = Card("menu", root ? "🧩 Все виджеты" : "🧩 " + T(cats[cat]), (page + 1) + "/" + pages + " · " + T("↗ — отдельное сообщение"));
            var buttons = root
                ? cats.Select((c, i) => Bt(T(c) + " · " + WKinds.Count(k => k.Group == c && k.Key != "menu"), "cat|" + i)).ToArray()
                : list.Select(k => Bt(k.Name + (IsCard(k.Key) ? "" : " ↗"), "go|" + k.Key)).ToArray();
            foreach (var pair in buttons.Skip(page * 6).Take(6).Chunk(2)) Btns(d, pair);
            if (pages > 1)
            {
                var nav = new List<object[]>();
                if (page > 0) nav.Add(Bt("‹", "page|" + (page - 1)));
                if (page + 1 < pages) nav.Add(Bt("›", "page|" + (page + 1)));
                Btns(d, nav.ToArray());
            }
            if (!root) Btns(d, Bt("⬅ Категории", "nav"));
            return d;
        }

        /// <summary>Кнопка «⬅ Назад» для сообщения, в котором виджет сменили на месте (с ужатием под 1000 байт).</summary>
        string Wrap(long msgId, string st)
        {
            List<string> stack;
            lock (_wBack)
            {
                if (!_wBack.TryGetValue(msgId, out var current) || current.Count == 0) return st;
                stack = current.ToList();
            }
            try
            {
                var n = JsonNode.Parse(st)!.AsObject();
                var prev = stack[^1];
                var label = prev == "menu" ? T("⬅ Все виджеты") : "⬅ " + (WK(prev)?.Name ?? T("Назад"));
                if (n["b"] is not JsonArray b) n["b"] = b = new JsonArray();
                b.Add(new JsonArray(new JsonArray(Cut(label, 26), "nav")));
                var s = n.ToJsonString(WidgetJson);
                while (System.Text.Encoding.UTF8.GetByteCount(s) > 1000 && n["r"] is JsonArray r && r.Count > 1) { r.RemoveAt(r.Count - 1); s = n.ToJsonString(WidgetJson); }
                return WidgetPolicy.FitState(s);
            }
            catch { return st; }
        }

        /// <summary>Превратить виджет в этом сообщении в другой вид (оба — карточки).</summary>
        string Morph(long msgId, string from, string to, string note = null)
        {
            lock (_wBack)
            {
                if (!_wBack.TryGetValue(msgId, out var stack)) _wBack[msgId] = stack = new();
                if (stack.Count == 0 || stack[^1] != from) stack.Add(from);
                if (stack.Count > 8) stack.RemoveAt(0);
                if (_wBack.Count > 200) _wBack.Remove(_wBack.Keys.Min());
            }
            lock (_widgets) _widgets[msgId] = (to, DateTime.Now);
            WStateDirty(to);
            Live(msgId, to);
            return Wrap(msgId, to == "menu" ? Fit(MenuState(_hubCat.GetValueOrDefault(msgId, -1), HubPage(msgId))) : WState(to, note));
        }

        /// <summary>«⬅ Назад»: в каталоге — к категориям, иначе к прежнему виду этого сообщения.</summary>
        string NavBack(long msgId, string kind)
        {
            if (kind == "menu" && _hubCat.GetValueOrDefault(msgId, -1) >= 0)
            {
                _hubCat[msgId] = -1;
                _hubPage[msgId] = 0;
                return Wrap(msgId, Fit(MenuState(-1)));
            }
            string prev = null;
            lock (_wBack)
                if (_wBack.TryGetValue(msgId, out var stack) && stack.Count > 0) { prev = stack[^1]; stack.RemoveAt(stack.Count - 1); }
            if (prev == null || WK(prev) == null) return Wrap(msgId, kind == "menu" ? Fit(MenuState(-1)) : WState(kind));
            lock (_widgets) _widgets[msgId] = (prev, DateTime.Now);
            WStateDirty(prev);
            Live(msgId, prev);
            return Wrap(msgId, prev == "menu" ? Fit(MenuState(_hubCat.GetValueOrDefault(msgId, -1), HubPage(msgId))) : WState(prev));
        }

        /// <summary>Нажатия в каталоге.</summary>
        async Task<string> MenuAction(string head, string arg, long msgId)
        {
            switch (head)
            {
                case "cat" when int.TryParse(arg, out var ci):
                    _hubCat[msgId] = ci;
                    _hubPage[msgId] = 0;
                    return Wrap(msgId, Fit(MenuState(ci)));
                case "page" when int.TryParse(arg, out var page):
                    _hubPage[msgId] = Math.Clamp(page, 0, 100);
                    return Wrap(msgId, Fit(MenuState(_hubCat.GetValueOrDefault(msgId, -1), HubPage(msgId))));
                case "go" when WK(arg) != null:
                {
                    if (IsCard(arg) && msgId != 0) return Morph(msgId, "menu", arg);
                    var ok = await SendW(arg);
                    var d = MenuState(_hubCat.GetValueOrDefault(msgId, -1), HubPage(msgId));
                    d["e"] = ok ? T("🧩 Виджет — ниже в чате") : T("Виджеты недоступны в этом Telegram");
                    return Wrap(msgId, Fit(d));
                }
                case "nav": return NavBack(msgId, "menu");
            }
            return Wrap(msgId, Fit(MenuState(_hubCat.GetValueOrDefault(msgId, -1), HubPage(msgId))));
        }

        Dictionary<string, object> CardState6(string kind) => kind == "menu" ? MenuState(-1) : null;

        // ------------------------------------------------------------------
        //  Меню /widgets по категориям — в одном сообщении
        // ------------------------------------------------------------------

        (string, object) WidgetsCats(string title = null)
        {
            int live; lock (_widgets) live = _widgets.Count(w => w.Value.Until > DateTime.Now);
            var cats = Cats();
            var sb = new StringBuilder($"🧩 <b>{T(title ?? "Виджеты в чате")}</b>\n" +
                T("Интерактивные мини-приложения прямо в сообщении (Bot API 10.4): нажимаешь — меняется на ПК, данные обновляются сами.") +
                $"\n\n{T("Всего")}: {WKinds.Length} · {T("живых сейчас")}: {live} · /widget имя\n" +
                T("Выбранный виджет обновляет последний открытый отсюда — новые сообщения не копятся (кроме ↗ — у них своё оформление)."));
            var rows = new List<object[]> { Row(B("🧩 Все виджеты в одном", "wg:menu")) };
            foreach (var ch in cats.Select((c, i) => B($"{T(c)} · {WKinds.Count(k => k.Group == c && k.Key != "menu")}", "wg:@c:" + i)).Chunk(2)) rows.Add(ch.ToArray());
            rows.Add(Row(B("🔄 Обновить живые", "wg:!ref"), B("⏹ Остановить обновления", "wg:!stop")));
            rows.Add(Row(B("⬅️ Назад", "nav:main")));
            return (sb.ToString(), K(rows.ToArray()));
        }

        (string, object) WidgetsCat(int ci)
        {
            var cats = Cats();
            if (ci < 0 || ci >= cats.Length) return WidgetsCats();
            var list = WKinds.Where(k => k.Group == cats[ci] && k.Key != "menu").ToList();
            var rows = list.Select(k => B(k.Name + (IsCard(k.Key) ? "" : " ↗"), "wg:" + k.Key)).Chunk(2).Select(c => c.ToArray()).ToList();
            rows.Add(Row(B("⬅️ Категории", "wg:@c:-1"), B("🧩 Все в одном", "wg:menu")));
            return ($"🧩 <b>{H(T(cats[ci]))}</b> · {list.Count}\n" + T("Нажми — виджет откроется (или обновится последний открытый отсюда)."), K(rows.ToArray()));
        }

        /// <summary>Виджет из меню /widgets: карточку — в последний виджет из меню (на месте), иначе новым сообщением.</summary>
        async Task<string> OpenFromMenu(string kind)
        {
            long target = _menuWidget;
            string cur = null;
            if (target != 0) lock (_widgets) cur = _widgets.TryGetValue(target, out var w) ? w.Kind : null;
            if (cur != null && IsCard(kind) && IsCard(cur) && cur != kind && (WFlag(kind) is not { } need || Flag(need)))
            {
                try
                {
                    var st = Morph(target, cur, kind);
                    await Call("editMessageWidgetData", new { chat_id = V("chat"), message_id = target, chat_data = st });
                    return T("🧩 Обновил виджет выше: ") + (WK(kind)?.Name ?? kind);
                }
                catch (TgError e) { Log.Info("telegram widget morph: " + e.Message); lock (_widgets) _widgets.Remove(target); _menuWidget = 0; }
            }
            if (!await SendW(kind)) return T("Виджеты недоступны в этом Telegram");
            if (IsCard(kind)) lock (_widgets) _menuWidget = _widgets.Where(w => w.Value.Kind == kind).Select(w => w.Key).DefaultIfEmpty(0).Max();
            return "🧩";
        }

        // самопроверка
        internal void TestForgetMenuWidget() => _menuWidget = 0;
        internal long TestMenuWidget => _menuWidget;
    }
}
