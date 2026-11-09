using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NIsland.Core;

namespace NIsland.Services
{
    // =====================================================================
    //  Без спама и уведомления виджетом.
    //  • Новый виджет заменяет прежний такой же: старое сообщение удаляется (настройка «Один виджет каждого вида»).
    //  • Уведомления (события ПК, вход и блокировка, сторожа: лимиты, IP, загрузки, процессы, нагрузка, будильник, отчёт) приходят
    //    одним виджетом «🔔 События ПК»: новое событие сверху, история, кнопки действий. Важные — новым виджетом (со звуком),
    //    а прежний удаляется; тихие (трек, уведомления приложений, блокировка) — правкой того же виджета.
    //  • Текст песни: поправка часов ПК по времени сервера Telegram, чтобы строки на телефоне не уезжали.
    // =====================================================================
    public partial class TelegramHub
    {
        public record PcEvent(DateTime At, string Kind, string Emoji, string Title, string Text, string Data);

        readonly List<PcEvent> _events = new();
        readonly Dictionary<long, DateTime> _wSentAt = new();
        long _eventsMsg, _lastSentWidget;
        DateTime _eventsAt;

        bool SingleWidgets => V("widgets_single") != "0";
        bool EventsWidgetOn => V("events_widget") != "0" && !_noWidgets && Remote == null;

        /// <summary>Последние n сообщений с виджетом этого вида (новые первыми).</summary>
        List<long> LatestOf(string kind, int n) { lock (_widgets) return _widgets.Where(w => w.Value.Kind == kind).Select(w => w.Key).OrderByDescending(x => x).Take(n).ToList(); }

        /// <summary>Новый виджет заменяет прежние такие же: старые сообщения удаляются из чата (только свои и моложе 47 часов — дальше Telegram не даёт).</summary>
        async Task DropOlder(string kind, long keep)
        {
            if (!SingleWidgets || keep == 0) return;
            List<long> old;
            lock (_widgets)
                lock (_wSentAt)
                    old = _widgets.Where(w => w.Value.Kind == kind && w.Key != keep && _wSentAt.TryGetValue(w.Key, out var at) && (DateTime.Now - at).TotalHours < 47)
                                  .Select(w => w.Key).ToList();
            foreach (var id in old)
            {
                try { await Call("deleteMessage", new { chat_id = V("chat"), message_id = id }); }
                catch (TgError e) { Log.Info("telegram widget delete: " + e.Message); }
                lock (_widgets) _widgets.Remove(id);
                lock (_wSentAt) _wSentAt.Remove(id);
                lock (_wBack) _wBack.Remove(id);
                if (_menuWidget == id) _menuWidget = 0;
                if (_eventsMsg == id) _eventsMsg = 0;
            }
        }

        void SentWidget(string kind, long id)
        {
            _lastSentWidget = id;
            if (id == 0) return;
            lock (_wSentAt) { _wSentAt[id] = DateTime.Now; if (_wSentAt.Count > 400) _wSentAt.Remove(_wSentAt.Keys.Min()); }
            _ = DropOlder(kind, id);
        }

        // ------------------------------------------------------------------
        //  «🔔 События ПК»
        // ------------------------------------------------------------------

        /// <summary>Событие в виджет. false — виджеты выключены/недоступны (тогда отправляется обычное сообщение).</summary>
        async Task<bool> PushEvent(string kind, string emoji, string title, string text, string data, bool silent)
        {
            if (!EventsWidgetOn || string.IsNullOrEmpty(V("chat"))) return false;
            lock (_events)
            {
                _events.Insert(0, new PcEvent(DateTime.Now, kind, emoji, title ?? "", text ?? "", data));
                if (_events.Count > 20) _events.RemoveRange(20, _events.Count - 20);
            }
            WStateDirty("events");
            // правим тот же виджет, только если он всё ещё «События ПК» (его могли превратить в другой кнопкой «↗»)
            bool alive; lock (_widgets) alive = _eventsMsg != 0 && _widgets.TryGetValue(_eventsMsg, out var ew) && ew.Kind == "events";
            if (silent && alive && (DateTime.Now - _eventsAt).TotalMinutes < 30)
            {
                try
                {
                    await Call("editMessageWidgetData", new { chat_id = V("chat"), message_id = _eventsMsg, chat_data = Wrap(_eventsMsg, WState("events")) });
                    return true;
                }
                catch (TgError e) when (e.Message.Contains("not modified")) { return true; }
                catch (TgError e) { Log.Info("telegram events edit: " + e.Message); }
            }
            if (!await SendW("events", silent)) return false;
            _eventsMsg = _lastSentWidget;
            _eventsAt = DateTime.Now;
            return true;
        }

        /// <summary>Сообщение сторожей ПК (PcWatch) → событие: вид по значку, без разметки.</summary>
        async Task<bool> PushPcText(string html)
        {
            var plain = System.Net.WebUtility.HtmlDecode(Regex.Replace(html ?? "", "<[^>]+>", "")).Trim();
            var m = Regex.Match(plain, @"^(\S+)\s+(.*)$", RegexOptions.Singleline);
            string emoji = m.Success ? m.Groups[1].Value : "🔔", body = m.Success ? m.Groups[2].Value : plain;
            string kind = emoji switch
            {
                "⏳" => "limit", "🌐" => "ip", "📥" => "download", "▶" or "⏹" => "proc", "🔥" or "🧠" => "load", "⏰" => "alarm", "📊" => "report", "🎯" => "focus", _ => "pc",
            };
            string data = kind switch
            {
                "download" => PcWatch.RecentDownloads(1).Select(d => d.Path).FirstOrDefault(),
                "alarm" => Regex.Match(html ?? "", "<b>(.*?)</b>").Groups[1].Value is { Length: > 0 } lb ? System.Net.WebUtility.HtmlDecode(lb) : null,
                _ => null,
            };
            var lines = body.Split('\n', 2);
            return await PushEvent(kind, emoji, lines[0], lines.Length > 1 ? lines[1] : "", data, false);
        }

        static string EventLink(string kind) => kind switch
        {
            "track" => "player", "limit" => "limits", "ip" => "iplog", "load" => "ramtop", "proc" => "procwatch", "report" => "report", "focus" => "focus",
            "session" => "sessions", "timer" => "timer", "reminder" => "agenda", "notify" => "notif", "battery" => "batlog", "net" => "network", "vpn" => "vpn",
            "agent" => "agents", "download" => "downloads", "alarm" => "alarms", _ => null,
        };

        Dictionary<string, object> CardState7(string kind)
        {
            if (kind != "events") return null;
            List<PcEvent> list; lock (_events) list = _events.ToList();
            var d = Card(kind, "🔔 События ПК", list.Count == 0 ? T("пока ничего — сюда придут уведомления с ПК") : T("последнее") + " · " + list[0].At.ToString("HH:mm"));
            if (list.Count > 0)
            {
                var e = list[0];
                CRows(d).Add(new object[] { "t", "🆕 " + e.At.ToString("HH:mm") });
                CRows(d).Add(new object[] { "l", e.Emoji + " " + Cut(T(e.Title), 80), null, null });
                if (e.Text != "") CRows(d).Add(new object[] { "t", Cut(T(e.Text), 200) });
                var acts = new List<object[]>();
                if (e.Kind == "alarm" && e.Data != null) acts.Add(Bt("😴 Отложить 10 мин", "snooze", "p"));
                if (e.Kind == "download" && e.Data != null) acts.Add(Bt("📤 Прислать файл", "dl", "p"));
                if (EventLink(e.Kind) is { } link) acts.Add(Bt("↗ " + (WK(link)?.Name ?? link), "open|" + link));
                if (acts.Count > 0) Btns(d, acts.ToArray());
                foreach (var h in list.Skip(1).Take(6)) CRows(d).Add(new object[] { "k", h.At.ToString("HH:mm"), h.Emoji + " " + Cut(T(h.Title), 34) });
            }
            Btns(d, Bt("🧹 Очистить", "clear"), Bt("🧩 Все виджеты", "open|menu"));
            return d;
        }

        async Task<string> CardAction7(string kind, string head, string arg)
        {
            if (kind != "events") return null;
            PcEvent e; lock (_events) e = _events.FirstOrDefault();
            switch (head)
            {
                case "clear": lock (_events) _events.Clear(); return null;
                case "snooze":
                    if (e?.Kind != "alarm" || e.Data == null) return T("Будильника уже нет");
                    await Ui(() => PcWatch.Snooze(e.Data));
                    return T("😴 Отложено на 10 минут");
                case "dl":
                {
                    if (e?.Kind != "download" || e.Data == null) return T("Файла уже нет");
                    if (!Flag("files")) return T("🔐 «Файлы» выключено — включается на ПК");
                    var r = await SendPath(e.Data);
                    return r == null ? T("📤 Файл в чате") : Cut(r, 120);
                }
            }
            return null;
        }

        // ------------------------------------------------------------------
        //  Часы ПК против времени сервера Telegram (для текста песни)
        // ------------------------------------------------------------------

        static long _clockFix;
        static DateTime _clockAt = DateTime.MinValue;

        /// <summary>На сколько миллисекунд часы ПК отстают от точного времени (по заголовку Date сервера Telegram, раз в 6 часов).</summary>
        static long PcClockFix()
        {
            if ((DateTime.Now - _clockAt).TotalHours > 6)
            {
                _clockAt = DateTime.Now;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
                        var t0 = DateTimeOffset.UtcNow;
                        using var res = await http.SendAsync(new HttpRequestMessage(HttpMethod.Head, "https://api.telegram.org/"));
                        var t1 = DateTimeOffset.UtcNow;
                        if (res.Headers.Date is DateTimeOffset srv)
                        {
                            // Date — с точностью до секунды: правим только явное расхождение
                            long diff = (long)(srv.AddMilliseconds(500) - (t0 + (t1 - t0) / 2)).TotalMilliseconds;
                            _clockFix = Math.Abs(diff) > 1500 ? diff : 0;
                        }
                    }
                    catch { }
                });
            }
            return _clockFix;
        }

        // самопроверка
        internal Task<bool> TestPushEvent(string kind, string title, bool silent) => PushEvent(kind, "🔔", title, "", null, silent);
        internal Task<bool> TestPushPcText(string html) => PushPcText(html);
        internal void TestRememberSent(long id, string kind) { lock (_widgets) _widgets[id] = (kind, DateTime.Now); lock (_wSentAt) _wSentAt[id] = DateTime.Now; }
        internal long TestEventsMsg => _eventsMsg;
    }
}
