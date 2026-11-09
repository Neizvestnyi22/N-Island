using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NIsland.Core;

namespace NIsland.Services
{
    // =====================================================================
    //  Виджеты Telegram: синхронный текст песни, визуализатор, «Переслать», бережная нагрузка на ПК.
    //  • Текст песни приходит пачкой строк со сдвигом по времени — виджет сам переключает строки в такт
    //    (бот обновляет его только при смене трека, паузе, перемотке и раз в несколько строк).
    //  • Визуализатор: снимок спектра (16 полос), громкость и темп раз в 4 с; анимацию рисует телефон.
    //    Захват звука включается только пока виджет живой, и с низкой частотой (15 Гц).
    //  • Нагрузка: одно состояние на вид раз в 1,5 с для всех копий, не больше 8 живых виджетов сразу.
    // =====================================================================
    public partial class TelegramHub
    {
        const string VizHtml = """
<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><style>
html,body{margin:0}body{font:12px system-ui,sans-serif;color:var(--tg-theme-text-color,#eee);padding:8px 10px}
#h{display:flex;justify-content:space-between;gap:8px;color:var(--tg-theme-hint-color,#999)}#n{white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
canvas{width:100%;height:140px;display:block;margin-top:4px;cursor:pointer}
</style></head><body><div id="h"><span id="n"></span><span id="m"></span></div><canvas id="c" width="640" height="280"></canvas><script>
var T=window.TelegramWidget,D={b:[],l:0,bpm:0,p:0,c:'#64d2ff'},C=new Array(16).fill(0),M=0,N=['▮▮▮','◐','〰','✦'],t0=performance.now(),X=document.getElementById('c').getContext('2d');
function $(i){return document.getElementById(i)}
function load(s){try{D=JSON.parse(s||'{}')}catch(e){}$('n').textContent=(D.p?'▶ ':'⏸ ')+(D.t||'');$('m').textContent=(D.bpm?D.bpm+' BPM · ':'')+N[M]}
function f(now){var w=640,h=280,s=(now-t0)/1e3,bt=D.bpm>40?60/D.bpm:.5,pu=Math.exp(-(s%bt)/bt*5),i,v;X.clearRect(0,0,w,h);
for(i=0;i<16;i++){v=D.p?(D.b[i]||0)*(.55+.45*pu)+(Math.sin(s*5+i*1.7)*.5+.5)*14*(D.l||0)/100:0;C[i]+=(v-C[i])*.22}
X.fillStyle=X.strokeStyle=D.c||'#64d2ff';X.lineWidth=5;X.lineCap='round';
if(M==0)for(i=0;i<16;i++){var bh=Math.max(4,C[i]/100*h*.95);X.globalAlpha=.35+.65*C[i]/100;X.fillRect(i*40+6,h-bh,28,bh)}
else if(M==1){X.beginPath();for(i=0;i<=32;i++){var a=i/32*Math.PI*2-Math.PI/2,r=60+C[i%16]/100*70*(.8+.2*pu);X.lineTo(w/2+Math.cos(a)*r*1.6,h/2+Math.sin(a)*r)}X.globalAlpha=.9;X.stroke();X.globalAlpha=.2;X.fill()}
else if(M==2){X.globalAlpha=.9;X.beginPath();for(i=0;i<=w;i+=8){var k=i/w*15,j=k|0,e=k-j,a2=C[j]*(1-e)+C[Math.min(15,j+1)]*e;X.lineTo(i,h/2+Math.sin(i/30+s*6)*a2/100*h*.45)}X.stroke()}
else for(i=0;i<16;i++){var a3=s*.6+i*.393,r3=30+C[i]*1.1;X.globalAlpha=.3+.7*C[i]/100;X.beginPath();X.arc(w/2+Math.cos(a3)*r3*1.7,h/2+Math.sin(a3)*r3*.8,4+C[i]/12,0,7);X.fill()}
X.globalAlpha=1;requestAnimationFrame(f)}
$('c').onclick=function(){M=(M+1)%4;load(JSON.stringify(D))};
if(T){load(T.chatData||T.publicData);T.onEvent('chatDataChanged',function(){load(T.chatData)});T.onEvent('publicDataChanged',function(){load(T.publicData)})}requestAnimationFrame(f);
</script></body></html>
""";

        // ------------------------------------------------------------------
        //  Синхронный текст песни: ["L", играет, [[сдвиг_с, строка], …]]
        // ------------------------------------------------------------------

        static object[] LyricsRow(int maxBytes)
        {
            var m = MediaService.I;
            var lines = m.Lyrics;
            if (!m.LyricsSynced || lines == null || lines.Count == 0) return null;
            double now = m.LyricsPosition.TotalSeconds;
            int i = Math.Max(0, m.LyricIndex);
            var list = new List<object[]>();
            int bytes = 0;
            for (int k = Math.Max(0, i - 1); k < lines.Count && list.Count < 14; k++)
            {
                var txt = Cut(lines[k].Text ?? "", 70);
                bytes += Encoding.UTF8.GetByteCount(txt) + 12;
                if (bytes > maxBytes && list.Count >= 3) break;
                list.Add(new object[] { Math.Round(lines[k].Time.TotalSeconds - now, 1), txt });
            }
            return new object[] { "L", m.IsPlaying ? 1 : 0, list };
        }

        // ------------------------------------------------------------------
        //  Визуализатор
        // ------------------------------------------------------------------

        static IDisposable _vizLease;
        static DateTime _vizUntil;
        static readonly float[] _vb = new float[AudioCapture.BandCount], _vp = new float[AudioCapture.BandCount], _vw = new float[AudioCapture.WaveCount];

        static string VizState()
        {
            var cap = AudioCapture.I;
            var bands = new int[16];
            lock (_vb)
            {
                _vizUntil = DateTime.Now.AddMinutes(LiveMin).AddSeconds(30);
                if (_vizLease == null)
                {
                    try { _vizLease = System.Windows.Application.Current.Dispatcher.Invoke(() => cap.Use(15)); } catch { }
                    _ = Task.Run(async () =>
                    {
                        while (DateTime.Now < _vizUntil) await Task.Delay(5000);
                        lock (_vb) { try { System.Windows.Application.Current.Dispatcher.Invoke(() => _vizLease?.Dispose()); } catch { } _vizLease = null; }
                    });
                }
                try { cap.Read(_vb, _vp, _vw); } catch { }
                int per = AudioCapture.BandCount / 16;
                for (int b = 0; b < 16; b++)
                {
                    double s = 0;
                    for (int k = 0; k < per; k++) s += Math.Max(_vb[b * per + k], _vp[b * per + k] * 0.6f);
                    bands[b] = (int)Math.Clamp(Math.Round(s / per * 100), 0, 100);
                }
            }
            var m = MediaService.I;
            var c = m.HasSession ? m.ArtColor : System.Windows.Media.Color.FromRgb(0x64, 0xD2, 0xFF);
            double lum = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255;
            if (lum < 0.35) c = System.Windows.Media.Color.FromRgb((byte)(c.R + (255 - c.R) / 2), (byte)(c.G + (255 - c.G) / 2), (byte)(c.B + (255 - c.B) / 2));
            return JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["b"] = bands, ["l"] = (int)Math.Clamp(cap.Level * 100, 0, 100), ["bpm"] = (int)Math.Round(cap.Bpm), ["p"] = m.IsPlaying ? 1 : 0,
                ["c"] = $"#{c.R:X2}{c.G:X2}{c.B:X2}", ["t"] = m.HasSession ? Cut(m.TrackLine, 44) : T("Ничего не играет"),
            }, WidgetJson);
        }

        // ------------------------------------------------------------------
        //  Бережные живые обновления
        // ------------------------------------------------------------------

        static readonly Dictionary<string, (DateTime At, string State)> _wsCache = new();

        /// <summary>Одно состояние на вид раз в 1,5 с — для всех живых копий этого вида.</summary>
        string WStateCached(string kind)
        {
            lock (_wsCache) if (_wsCache.TryGetValue(kind, out var c) && (DateTime.Now - c.At).TotalSeconds < 1.5) return c.State;
            var s = WState(kind);
            lock (_wsCache) _wsCache[kind] = (DateTime.Now, s);
            return s;
        }

        static void WStateDirty(string kind) { lock (_wsCache) _wsCache.Remove(kind); }

        const int MaxLive = 8;

        /// <summary>Не больше 8 живых виджетов: у самых старых обновления останавливаются.</summary>
        void TrimLive()
        {
            lock (_widgets)
            {
                // список отправленных виджетов не растёт бесконечно: помним последние 300
                if (_widgets.Count > 300) foreach (var old in _widgets.Keys.OrderBy(k => k).Take(_widgets.Count - 300).ToList()) _widgets.Remove(old);
                var live = _widgets.Where(w => w.Value.Until > DateTime.Now).OrderByDescending(w => w.Key).Skip(MaxLive).Select(w => w.Key).ToList();
                foreach (var id in live) _widgets[id] = (_widgets[id].Kind, DateTime.Now);
            }
        }

        static double _mpPos;
        static DateTime _mpAt;
        static int _seekGen;

        /// <summary>Что должно вызвать обновление музыкальных виджетов: трек, пауза, перемотка, каждые 6 строк текста.</summary>
        static string MediaKey()
        {
            var m = MediaService.I;
            double pos = m.Position.TotalSeconds, expect = _mpPos + (m.IsPlaying ? (DateTime.Now - _mpAt).TotalSeconds : 0);
            if (Math.Abs(pos - expect) > 2.5) _seekGen++;
            _mpPos = pos; _mpAt = DateTime.Now;
            return $"{m.TrackLine}|{m.IsPlaying}|{m.LyricIndex / 6}|{_seekGen}|{m.LyricsSynced}";
        }

        /// <summary>Ключ сравнения: без меняющихся сами по себе чисел (позиция, время, строки текста) — их виджет считает сам.</summary>
        static string LiveKey(string kind, string st)
        {
            var k = System.Text.RegularExpressions.Regex.Replace(st, "\"(tm|t|pos)\":[^,}]*", "");
            k = System.Text.RegularExpressions.Regex.Replace(k, @"\[""p"",[^\]]*\]", "");
            k = System.Text.RegularExpressions.Regex.Replace(k, @"\[""L"",\d,\[.*?\]\]\]", "");
            // строка батареи в плеере: оценка «до полной / осталось» меняется каждую минуту — правим виджет только при смене процента
            k = System.Text.RegularExpressions.Regex.Replace(k, @"\[""b"",""[^""]*"",(\d+),""[^""]*"",""\d+% · [^""]*""\]", "bat$1");
            return kind is "player" or "lyrics" or "remote" ? k + MediaKey() : k;
        }

        // ------------------------------------------------------------------
        //  «Переслать»
        // ------------------------------------------------------------------

        static bool _noWidgetKeys;

        /// <summary>Короткий отпечаток имени для запроса из виджета (имена программ бывают длинными, запрос должен быть коротким).</summary>
        static string WHash(string s) => Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(Encoding.UTF8.GetBytes(s ?? "")))[..8].ToLowerInvariant();

        internal Task TestInline(string json) { using var d = JsonDocument.Parse(json); var el = d.RootElement.Clone(); return Task.Run(() => HandleInline(el)); }
        internal static void TestInlineMode(bool on) { _inlineOk = on; _inlineAt = DateTime.Now; }

        static bool? _inlineOk;
        static DateTime _inlineAt;

        /// <summary>Включён ли у бота инлайн-режим (getMe → supports_inline_queries); раз в 10 минут.</summary>
        async Task<bool> InlineOk()
        {
            if (_inlineOk is bool b && (DateTime.Now - _inlineAt).TotalMinutes < 10) return b;
            try
            {
                var me = await Call("getMe", new { });
                _inlineOk = me.ValueKind == JsonValueKind.Object && me.TryGetProperty("supports_inline_queries", out var s) && s.ValueKind == JsonValueKind.True;
            }
            catch { _inlineOk = false; }
            _inlineAt = DateTime.Now;
            return _inlineOk.Value;
        }

        /// <summary>Только для чтения: у карточки в чужом чате нет кнопок управления (копирование остаётся).</summary>
        static string ReadOnlyState(string st) =>
            st.StartsWith("{\"k\":") ? "{\"ro\":1," + st[1..] : st;

        static readonly string[] InlineFavorites = { "player", "viz", "weather", "sysmon", "clock", "pet", "calendar", "countdown", "ttt", "g2048", "dice", "calc", "battery", "network" };

        /// <summary>Инлайн-запрос «@бот w player» из выбора чата: только владелец получает виджеты, остальным — пусто.</summary>
        async Task HandleInline(JsonElement q)
        {
            var qid = Integrations.S(q, "id");
            string from = q.TryGetProperty("from", out var f) && f.TryGetProperty("id", out var fid) ? fid.ToString() : "";
            var results = new List<object>();
            if (from != "" && from == V("chat") && Remote == null)
            {
                var text = Integrations.S(q, "query").Trim();
                var want = text.StartsWith("w ") ? text[2..].Trim() : text;
                var kind = want == "" ? null : WAliases.TryGetValue(want, out var al) ? al : WK(want.ToLowerInvariant())?.Key;
                IEnumerable<WKind> kinds = kind != null ? new[] { WK(kind) }.Concat(InlineFavorites.Where(k => k != kind).Select(WK))
                    : want == "" ? InlineFavorites.Select(WK)
                    : WKinds.Where(k => T(k.Name).Contains(want, StringComparison.OrdinalIgnoreCase) || k.Name.Contains(want, StringComparison.OrdinalIgnoreCase) || k.Key.Contains(want, StringComparison.OrdinalIgnoreCase));
                foreach (var wk in kinds.Where(k => k != null && k.Key != "remote" && WFlag(k.Key) is null or "control").Take(10))
                {
                    string st;
                    try { st = ReadOnlyState(WState(wk.Key)); } catch (Exception e) { Log.Info("inline widget: " + e.Message); continue; }
                    var content = new Dictionary<string, object> { ["html"] = HtmlOf(wk.Key), ["width"] = wk.W, ["allow_queries"] = false, ["chat_data"] = st };
                    if (wk.H > 0) content["height"] = wk.H;
                    bool local = wk.Key is "calc" or "dice" or "clock" or "calendar" or "breath" or "ttt" or "g2048" or "countdown";
                    results.Add(new Dictionary<string, object>
                    {
                        ["type"] = "article", ["id"] = wk.Key, ["title"] = T(wk.Name),
                        ["description"] = T(local ? "Работает у всех в чате" : "Снимок без управления ПК"),
                        ["input_message_content"] = content,
                    });
                }
            }
            try { await Call("answerInlineQuery", new { inline_query_id = qid, results, cache_time = 0, is_personal = true }); }
            catch (TgError e) { Log.Info("telegram inline: " + e.Message); }
        }

        // ---- живые обновления: один цикл на сообщение ----
        static readonly HashSet<long> _freshRunning = new();

        /// <summary>Продлить живые обновления виджета и запустить цикл, если он уже закончился.</summary>
        void Live(long id, string kind)
        {
            var wk = WK(kind);
            if (wk == null || wk.LiveSec <= 0 || id == 0) return;
            lock (_widgets) _widgets[id] = (kind, DateTime.Now.AddMinutes(LiveMin));
            bool start;
            lock (_freshRunning) start = _freshRunning.Add(id);
            if (start) _ = KeepFreshLoop(id, kind);
            TrimLive();
        }

        async Task KeepFreshLoop(long id, string kind)
        {
            try { await KeepFresh(id, kind); }
            finally { lock (_freshRunning) _freshRunning.Remove(id); }
        }
    }
}
