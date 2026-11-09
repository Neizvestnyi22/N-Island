using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NIsland.Core;

namespace NIsland.Services
{
    // =====================================================================
    //  «📤 Переслать» без инлайн-режима: кнопка под виджетом → нижняя клавиатура с системным выбором чата
    //  (request_chat / request_users) → Telegram присылает боту выбранный чат → бот пересылает сам виджет (forwardMessage).
    //  Виджеты пересылать можно, даже в каналы. Нажатия в пересланной копии бот не обслуживает (чужой чат).
    // =====================================================================
    public partial class TelegramHub
    {
        long _fwdMsg;
        DateTime _fwdAt;

        const int ReqGroup = 1, ReqChannel = 2, ReqUser = 3;

        /// <summary>Кнопка под виджетом: обычная кнопка бота, без инлайн-режима.</summary>
        Task<object> ShareKeys(string kind) => Task.FromResult(_noWidgetKeys ? null : K(Row(B("📤 Переслать", "wg:fw"))));

        object ForwardPicker() => new
        {
            keyboard = new object[][]
            {
                new object[]
                {
                    new { text = T("👥 В группу"), request_chat = new { request_id = ReqGroup, chat_is_channel = false, bot_is_member = true, request_title = true } },
                    new { text = T("📢 В канал"), request_chat = new { request_id = ReqChannel, chat_is_channel = true, bot_is_member = true, request_title = true } },
                },
                new object[]
                {
                    new { text = T("👤 Человеку"), request_users = new { request_id = ReqUser, user_is_bot = false, max_quantity = 1, request_name = true } },
                    new { text = T("✖ Отмена") },
                },
            },
            resize_keyboard = true,
            one_time_keyboard = true,
            input_field_placeholder = T("Выбери, куда переслать виджет"),
        };

        /// <summary>Клавиатура после выбора: обычные кнопки бота (если включены) или убрать.</summary>
        object AfterPicker() => Flag("kbd") ? ReplyKeys() : new { remove_keyboard = true };

        /// <summary>Нажали «📤 Переслать» под сообщением-виджетом msgId.</summary>
        async Task<string> StartForward(long msgId)
        {
            if (msgId == 0) return Stale;
            _fwdMsg = msgId;
            _fwdAt = DateTime.Now;
            await Message("📤 <b>" + T("Куда переслать виджет?") + "</b>\n" +
                T("Кнопки внизу откроют список твоих чатов. В группу и канал бот должен быть добавлен (в канал — админом с правом публикации), человек — хоть раз написать боту. У получателя будет снимок без управления ПК."),
                ForwardPicker());
            return "📤";
        }

        /// <summary>Сообщение с выбранным чатом (chat_shared / users_shared) или «Отмена». true — обработано.</summary>
        async Task<bool> ForwardPicked(JsonElement m, string text)
        {
            bool shared = m.TryGetProperty("chat_shared", out var cs);
            bool users = m.TryGetProperty("users_shared", out var us);
            if (!shared && !users)
            {
                if (_fwdMsg != 0 && (text == "✖ Отмена" || text == T("✖ Отмена")))
                {
                    _fwdMsg = 0;
                    await Message(T("Пересылка отменена"), AfterPicker());
                    return true;
                }
                return false;
            }
            long msg = _fwdMsg;
            _fwdMsg = 0;
            if (msg == 0 || (DateTime.Now - _fwdAt).TotalMinutes > 10)
            {
                await Message(T("Сначала нажми «📤 Переслать» под нужным виджетом"), AfterPicker());
                return true;
            }
            string target, name;
            int req;
            if (shared)
            {
                target = cs.GetProperty("chat_id").ToString();
                req = cs.TryGetProperty("request_id", out var r1) ? r1.GetInt32() : 0;
                name = cs.TryGetProperty("title", out var t1) ? t1.GetString() : cs.TryGetProperty("username", out var u1) ? "@" + u1.GetString() : target;
            }
            else
            {
                var u = us.GetProperty("users")[0];
                target = u.GetProperty("user_id").ToString();
                req = us.TryGetProperty("request_id", out var r2) ? r2.GetInt32() : ReqUser;
                name = u.TryGetProperty("first_name", out var f) ? f.GetString() : target;
            }
            try
            {
                await Call("forwardMessage", new { chat_id = target, from_chat_id = V("chat"), message_id = msg });
                await Message("📤 " + T("Переслано") + ": " + H(name ?? ""), AfterPicker());
            }
            catch (TgError e)
            {
                Log.Info("telegram forward: " + e.Message);
                var why = e.Message.ToLowerInvariant();
                string hint = req == ReqUser || why.Contains("initiate") || why.Contains("blocked")
                        ? T("Этот человек ещё не писал боту (или заблокировал его). Пусть откроет бота и нажмёт «Старт» — или перешли сам: зажми виджет → «Переслать».")
                    : req == ReqChannel || why.Contains("rights") || why.Contains("administrator")
                        ? T("Сделай бота админом канала с правом публиковать сообщения и попробуй ещё раз — или перешли сам: зажми виджет → «Переслать».")
                        : T("Добавь бота в эту группу и попробуй ещё раз — или перешли сам: зажми виджет → «Переслать».");
                await Message("⚠️ " + T("Не получилось переслать в") + " «" + H(name ?? "") + "»: " + H(Cut(e.Message, 120)) + "\n" + hint, AfterPicker());
            }
            return true;
        }

        // ------------------------------------------------------------------
        //  Нажатия в виджетах: по очереди на сообщение, без двойных повторов, с сохранением нового состояния
        // ------------------------------------------------------------------

        static readonly Dictionary<long, SemaphoreSlim> _wGates = new();
        static readonly Dictionary<string, DateTime> _wLastTap = new();
        static readonly HashSet<string> RepeatableHeads = new() { "vu", "vd", "mv", "bu", "bd", "w", "k", "add", "back", "fwd", "c", "#", "left", "right", "space" };

        static bool DoubleTap(long msgId, string data)
        {
            var parts = data.Split(':');
            var head = parts.Length >= 3 ? parts[2].Split('|')[0] : parts.Length == 2 ? parts[1] : data;
            if (RepeatableHeads.Contains(head) || int.TryParse(head, out _) || head is "ref" or "") return false;
            var key = msgId + "|" + data;
            lock (_wLastTap)
            {
                bool dup = _wLastTap.TryGetValue(key, out var at) && (DateTime.Now - at).TotalMilliseconds < 400;
                _wLastTap[key] = DateTime.Now;
                if (_wLastTap.Count > 300) foreach (var k in _wLastTap.Where(x => (DateTime.Now - x.Value).TotalMinutes > 1).Select(x => x.Key).ToList()) _wLastTap.Remove(k);
                return dup;
            }
        }

        /// <summary>Нажатия в одном виджете — по очереди: «вперёд, вперёд» не перемешиваются, ответы приходят в порядке нажатий.</summary>
        static async Task<string> Serialized(long msgId, Func<Task<string>> work)
        {
            SemaphoreSlim gate;
            lock (_wGates)
            {
                if (!_wGates.TryGetValue(msgId, out gate)) _wGates[msgId] = gate = new SemaphoreSlim(1, 1);
                if (_wGates.Count > 100) foreach (var k in _wGates.Keys.OrderBy(x => x).Take(_wGates.Count - 100).ToList()) _wGates.Remove(k);
            }
            await gate.WaitAsync();
            try { return await work(); }
            finally { gate.Release(); }
        }

        /// <summary>Состояние виджета без действия (для повторного тапа).</summary>
        string CurrentWidgetState(string data)
        {
            if (data.StartsWith("w:")) return RemoteState();
            if (data.StartsWith("p:")) return PetState();
            if (data.StartsWith("c:") && data.IndexOf(':', 2) is int p and > 2 && WK(data[2..p]) != null) return WState(data[2..p]);
            return null;
        }

        async Task PersistWidget(long msgId, string state)
        {
            try { await Call("editMessageWidgetData", new { chat_id = V("chat"), message_id = msgId, chat_data = state }); }
            catch (TgError) { }   // «не изменилось» и т. п. — не важно
        }

        internal Task TestUpdate(string json) { using var d = JsonDocument.Parse(json); var el = d.RootElement.Clone(); return Task.Run(() => Handle(el)); }
        internal Task<string> TestCallbackAt(string data, long msgId) => Task.Run(() => Callback(data, msgId));
    }
}
