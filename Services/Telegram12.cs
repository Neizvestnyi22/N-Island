using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NIsland.Core;

namespace NIsland.Services
{
    // Bot API 10.4 production: private chats, protected widget content; legacy share callbacks fail closed.
    public partial class TelegramHub
    {
        /// <summary>Bot API 10.4 production: widget messages are protected and cannot be forwarded.</summary>
        Task<object> ShareKeys(string kind) => Task.FromResult<object>(_noWidgetKeys ? null : K(Row(B("🧩 Каталог", "wg:menu"))));

        Task<string> StartForward(long msgId) => Task.FromResult(T("В Bot API 10.4 виджеты защищены от пересылки. Открой /widgets в личном чате с ботом."));

        async Task<bool> ForwardPicked(JsonElement m, string text)
        {
            if (!m.TryGetProperty("chat_shared", out _) && !m.TryGetProperty("users_shared", out _)) return false;
            await Message(T("Пересылка виджетов недоступна в Bot API 10.4."), Flag("kbd") ? ReplyKeys() : new { remove_keyboard = true });
            return true;
        }

        // ------------------------------------------------------------------
        //  Нажатия в виджетах: по очереди на сообщение, без двойных повторов, с сохранением нового состояния
        // ------------------------------------------------------------------

        sealed class WidgetGate { public readonly SemaphoreSlim Gate = new(1, 1); public int Users; }
        readonly Dictionary<long, WidgetGate> _wGates = new();
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
        async Task<string> Serialized(long msgId, Func<Task<string>> work)
        {
            WidgetGate entry;
            lock (_wGates)
            {
                if (!_wGates.TryGetValue(msgId, out entry)) _wGates[msgId] = entry = new WidgetGate();
                entry.Users++;
            }
            await entry.Gate.WaitAsync();
            try { return await work(); }
            finally
            {
                entry.Gate.Release();
                lock (_wGates) if (--entry.Users == 0) { _wGates.Remove(msgId); entry.Gate.Dispose(); }
            }
        }

        /// <summary>Состояние виджета без действия (для повторного тапа).</summary>
        string CurrentWidgetState(string data, long msgId = 0)
        {
            if (data.StartsWith("w:")) return RemoteState();
            if (data.StartsWith("p:")) return PetState();
            if (data.StartsWith("c:") && data.IndexOf(':', 2) is int p and > 2 && WK(data[2..p]) != null)
                return data[2..p] == "menu" ? Wrap(msgId, Fit(MenuState(_hubCat.GetValueOrDefault(msgId, -1), HubPage(msgId)))) : Wrap(msgId, WState(data[2..p]));
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
