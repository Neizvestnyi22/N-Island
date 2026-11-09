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
    //  Синхронизация с ПК: нажали паузу, сменили трек, громкость, эквалайзер, режим на самом ПК — последние виджеты
    //  этого вида и последнее сообщение-пульт («Музыка», «Звук», «Эквалайзер», «Громкость программ») обновляются сами,
    //  не дожидаясь нажатия и не только в «живые» минуты. События склеиваются (0,8 с), правка уходит, только если
    //  состояние правда изменилось — так не упираемся в ограничения Telegram и ничего не дёргается.
    // =====================================================================
    public partial class TelegramHub
    {
        static readonly string[] SyncMenus = { "music", "sound", "eq", "mixer" };
        readonly Dictionary<string, (long Id, DateTime At, string Sig)> _menuMsgs = new();
        readonly Dictionary<long, string> _pushedKey = new();
        readonly HashSet<string> _dirtyKinds = new(), _dirtyMenus = new();
        int _syncHooked;
        bool _syncScheduled;

        /// <summary>Запомнить сообщение-пульт, которое надо обновлять при изменениях на ПК (последнее каждого вида).</summary>
        void TrackMenu(string menu, long id)
        {
            if (id == 0 || Array.IndexOf(SyncMenus, menu) < 0) return;
            lock (_menuMsgs) _menuMsgs[menu] = (id, DateTime.Now, null);
        }

        static string MenuOfCommand(string cmd) => cmd switch { "music" or "now" => "music", "sound" or "output" => "sound", "eq" => "eq", "mixer" => "mixer", _ => null };

        void HookPcSync()
        {
            if (Interlocked.Exchange(ref _syncHooked, 1) == 1) return;
            string[] media = { "player", "lyrics", "remote", "history" }, vol = { "player", "mixer", "audioout", "remote" };
            try
            {
                var m = MediaService.I;
                m.PlaybackChanged += () => Dirty(media, "music");
                m.TrackChanged += () => Dirty(media, "music");
                m.Changed += () => Dirty(media, "music");
                AudioService.I.Changed += (_, __, ___) => Dirty(vol, "music", "sound", "mixer");
                AudioService.I.OutputChanged += _ => Dirty(new[] { "audioout", "mixer" }, "sound");
                Store.Settings.PropertyChanged += (_, e) =>
                {
                    switch (e.PropertyName)
                    {
                        case nameof(Settings.EqOn): case nameof(Settings.EqPreset): case nameof(Settings.EqGains): case nameof(Settings.EqBoost): Dirty(new[] { "eq" }, "eq"); break;
                        case nameof(Settings.Mode): Dirty(new[] { "modes", "remote" }); break;
                        case nameof(Settings.IpAlert): Dirty(new[] { "iplog" }); break;
                        case nameof(Settings.DownloadsNotify): Dirty(new[] { "downloads" }); break;
                        case nameof(Settings.LoadAlertCpu): case nameof(Settings.LoadAlertRam): Dirty(new[] { "guard" }); break;
                        case nameof(Settings.AppLimits): case nameof(Settings.LimitAction): Dirty(new[] { "limits", "screentime" }); break;
                        case nameof(Settings.WatchProcs): Dirty(new[] { "procwatch" }); break;
                        case nameof(Settings.Alarms): Dirty(new[] { "alarms" }); break;
                        case nameof(Settings.DailyReportTime): Dirty(new[] { "report" }); break;
                        case nameof(Settings.FocusBlockApps): Dirty(new[] { "focus" }); break;
                    }
                };
                PcWatch.Changed += () => Dirty(new[] { "timeline", "focus", "awake", "sessions", "usblog", "downloads" });
            }
            catch (Exception e) { Log.Info("telegram sync hook: " + e.Message); }
        }

        void Dirty(string[] kinds, params string[] menus)
        {
            bool start;
            lock (_dirtyKinds)
            {
                foreach (var k in kinds) _dirtyKinds.Add(k);
                foreach (var mn in menus) _dirtyMenus.Add(mn);
                start = !_syncScheduled;
                _syncScheduled = true;
            }
            if (start) _ = Task.Run(async () => { await Task.Delay(800); await FlushSync(); });
        }

        async Task FlushSync()
        {
            List<string> kinds, menus;
            lock (_dirtyKinds)
            {
                kinds = _dirtyKinds.ToList(); menus = _dirtyMenus.ToList();
                _dirtyKinds.Clear(); _dirtyMenus.Clear();
                _syncScheduled = false;
            }
            if (Remote != null || !Enabled || !Ready || string.IsNullOrEmpty(V("chat"))) return;

            // виджеты: последние 3 каждого вида
            if (!_noWidgets)
                foreach (var kind in kinds)
                {
                    List<long> ids;
                    lock (_widgets) ids = _widgets.Where(w => w.Value.Kind == kind).Select(w => w.Key).OrderByDescending(x => x).Take(3).ToList();
                    if (ids.Count == 0) continue;
                    WStateDirty(kind);
                    string st;
                    try { st = WState(kind); } catch (Exception e) { Log.Info("telegram sync state: " + e.Message); continue; }
                    var key = LiveKey(kind, st);
                    foreach (var id in ids)
                    {
                        lock (_pushedKey) if (_pushedKey.TryGetValue(id, out var was) && was == key) continue;
                        try
                        {
                            await Call("editMessageWidgetData", new { chat_id = V("chat"), message_id = id, chat_data = st });
                            lock (_pushedKey) { _pushedKey[id] = key; if (_pushedKey.Count > 200) _pushedKey.Remove(_pushedKey.Keys.Min()); }
                        }
                        catch (TgError e) when (e.Message.Contains("not modified")) { lock (_pushedKey) _pushedKey[id] = key; }
                        catch (TgError e) when (e.Code == 429) { break; }
                        catch (TgError e) { Log.Info("telegram sync widget: " + e.Message); lock (_widgets) _widgets.Remove(id); }
                    }
                }

            // обычные сообщения-пульты: последнее каждого вида, если им пользовались в последний час
            foreach (var menu in menus)
            {
                (long Id, DateTime At, string Sig) mm;
                lock (_menuMsgs) if (!_menuMsgs.TryGetValue(menu, out mm)) continue;
                if ((DateTime.Now - mm.At).TotalHours > 1) { lock (_menuMsgs) _menuMsgs.Remove(menu); continue; }
                try
                {
                    var (h, k) = await Menu(menu);
                    var sig = System.Text.RegularExpressions.Regex.Replace(h, "<code>.*?</code>|<i>.*?</i>", "") + JsonSerializer.Serialize(k);   // позиция и строка текста идут сами — из-за них не правим
                    if (sig == mm.Sig) continue;
                    await Edit(mm.Id, h, k);
                    lock (_menuMsgs) if (_menuMsgs.TryGetValue(menu, out var cur) && cur.Id == mm.Id) _menuMsgs[menu] = (mm.Id, mm.At, sig);
                }
                catch (TgError e) when (e.Message.Contains("not found") || e.Message.Contains("can't be edited")) { lock (_menuMsgs) _menuMsgs.Remove(menu); }
                catch (Exception e) { Log.Info("telegram sync menu: " + e.Message); }
            }
        }

        // самопроверка: изменение на ПК → правка виджета и сообщения без нажатия
        internal void TestHookSync() => HookPcSync();
        internal void TestTrackMenu(string menu, long id) => TrackMenu(menu, id);
        internal Task TestFlush(string[] kinds, string[] menus) { Dirty(kinds, menus); return Task.Delay(2500); }
        internal void TestRememberWidget(long id, string kind) { lock (_widgets) _widgets[id] = (kind, DateTime.Now); }
    }
}
