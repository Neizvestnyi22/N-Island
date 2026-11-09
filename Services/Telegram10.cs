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
            return kind is "player" or "lyrics" or "remote" ? k + MediaKey() : k;
        }

        // ------------------------------------------------------------------
        //  «Переслать»
        // ------------------------------------------------------------------

        static bool _noWidgetKeys;

        /// <summary>Короткий отпечаток имени для запроса из виджета (имена программ бывают длинными, запрос должен быть коротким).</summary>
        static string WHash(string s) => Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(Encoding.UTF8.GetBytes(s ?? "")))[..8].ToLowerInvariant();

        object ShareKeys(string kind) => _noWidgetKeys ? null : K(Row(B("📤 Переслать", "wg:share:" + kind)));

        /// <summary>Копия для пересылки: общий снимок (public_data) без управления ПК — у получателя нажатия не работают.</summary>
        async Task<string> ShareWidget(string kind)
        {
            var wk = WK(kind);
            if (wk == null) return Stale;
            try
            {
                var body = new Dictionary<string, object> { ["chat_id"] = V("chat"), ["html"] = HtmlOf(kind), ["width"] = wk.W, ["is_shared"] = true, ["allow_queries"] = false, ["public_data"] = WState(kind) };
                if (wk.H > 0) body["height"] = wk.H;
                await Call("sendWidget", body);
                await Message("⬆️ Эту копию можно переслать в любой чат: зажми её → «Переслать». У получателя будет снимок без управления ПК.");
                return "📤";
            }
            catch (TgError e)
            {
                Log.Info("telegram widget share: " + e.Message);
                await Message("Telegram не дал сделать копию для пересылки — перешли сам виджет: зажми его → «Переслать» (у получателя кнопки работать не будут).");
                return "📤";
            }
        }
    }
}
