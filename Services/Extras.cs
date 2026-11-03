using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using NIsland.Core;

namespace NIsland.Services
{
    // =====================================================================
    //  История буфера обмена (только текст; на диск — только если разрешено)
    // =====================================================================
    public sealed class ClipboardService
    {
        public static ClipboardService I { get; } = new();

        [DllImport("user32.dll")] static extern bool AddClipboardFormatListener(IntPtr h);
        [DllImport("user32.dll")] static extern bool RemoveClipboardFormatListener(IntPtr h);
        public const int WM_CLIPBOARDUPDATE = 0x031D;

        public ObservableCollection<ClipItem> Items { get; private set; } = new();
        public event Action<ClipItem> Copied;
        IntPtr _hwnd;
        string _selfText;

        public void Start(IntPtr hwnd)
        {
            _hwnd = hwnd;
            if (Store.Settings.ClipSave) Items = Store.Data.Clips;
            Apply();
        }

        public void Apply()
        {
            if (_hwnd == IntPtr.Zero) return;
            RemoveClipboardFormatListener(_hwnd);
            if (Store.Settings.ClipWatch) AddClipboardFormatListener(_hwnd);
            // сохранять на диск только с разрешения
            if (Store.Settings.ClipSave) { Store.Data.Clips = Items; Store.SaveData(); }
            else if (Store.Data.Clips != Items || Store.Data.Clips.Count > 0)
            {
                Store.Data.Clips = new ObservableCollection<ClipItem>();
                Store.SaveData();
            }
        }

        public void OnUpdate()
        {
            Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(Read));
        }

        void Read()
        {
            string text = null;
            for (int i = 0; i < 3 && text == null; i++)
            {
                try { if (Clipboard.ContainsText()) text = Clipboard.GetText(); else return; }
                catch { System.Threading.Thread.Sleep(30); }
            }
            if (string.IsNullOrWhiteSpace(text) || text.Length > 20000) return;
            if (text == _selfText) { _selfText = null; return; }
            var existing = Items.FirstOrDefault(x => x.Text == text);
            if (existing != null) Items.Remove(existing);
            var item = new ClipItem { Text = text, Pinned = existing?.Pinned ?? false };
            Items.Insert(0, item);
            while (Items.Count > 40)
            {
                var old = Items.LastOrDefault(x => !x.Pinned);
                if (old == null) break;
                Items.Remove(old);
            }
            if (Store.Settings.ClipSave) Store.SaveData();
            Copied?.Invoke(item);
        }

        public void CopyBack(string text)
        {
            _selfText = text;
            try { Clipboard.SetText(text); } catch { }
        }

        public void Clear()
        {
            foreach (var x in Items.Where(x => !x.Pinned).ToList()) Items.Remove(x);
            if (Store.Settings.ClipSave) Store.SaveData();
        }
    }

    // =====================================================================
    //  Таймер, секундомер и «сон» для музыки
    // =====================================================================
    public sealed class TimerService
    {
        public static TimerService I { get; } = new();
        readonly DispatcherTimer _t = new() { Interval = TimeSpan.FromMilliseconds(250) };

        // таймер
        public TimeSpan Set { get; set; } = TimeSpan.FromMinutes(5);
        public bool Running { get; private set; }
        public bool Paused => !Running && _left > TimeSpan.Zero && _left < Set;
        DateTime _endsAt;
        TimeSpan _left;
        public TimeSpan Left => Running ? Max0(_endsAt - DateTime.Now) : (_left > TimeSpan.Zero ? _left : Set);
        public double Progress => Set.TotalSeconds <= 0 ? 0 : Math.Clamp(1 - Left.TotalSeconds / Set.TotalSeconds, 0, 1);
        public bool Active => Running || Paused;

        // секундомер
        public bool SwRunning { get; private set; }
        DateTime _swStart;
        TimeSpan _swAcc;
        public TimeSpan SwElapsed => _swAcc + (SwRunning ? DateTime.Now - _swStart : TimeSpan.Zero);
        public List<TimeSpan> Laps { get; } = new();

        // сон музыки
        public DateTime? SleepAt { get; private set; }

        public event Action Changed;
        public event Action Finished;
        public event Action SleepFired;

        TimerService() { _t.Tick += (_, __) => Tick(); }

        static TimeSpan Max0(TimeSpan t) => t < TimeSpan.Zero ? TimeSpan.Zero : t;

        void Kick()
        {
            if (Running || SwRunning || SleepAt != null) _t.Start(); else _t.Stop();
            Changed?.Invoke();
        }

        public void SetMinutes(double m) { if (!Running) { Set = TimeSpan.FromMinutes(Math.Clamp(m, 0.5, 600)); _left = TimeSpan.Zero; Changed?.Invoke(); } }
        public void Add(int minutes)
        {
            if (Running)
            {
                _endsAt = _endsAt.AddMinutes(minutes);
                if (_endsAt < DateTime.Now.AddSeconds(5)) _endsAt = DateTime.Now.AddSeconds(5);
                var total = Set + TimeSpan.FromMinutes(minutes);
                Set = total < TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(30) : total;
                if (Left > Set) Set = Left;
            }
            else SetMinutes(Math.Max(1, Set.TotalMinutes + minutes));
            Changed?.Invoke();
        }

        public void Start()
        {
            var left = _left > TimeSpan.Zero ? _left : Set;
            _endsAt = DateTime.Now + left;
            Running = true;
            Kick();
        }

        public void Pause()
        {
            if (!Running) return;
            _left = Max0(_endsAt - DateTime.Now);
            Running = false;
            Kick();
        }

        public void Toggle() { if (Running) Pause(); else Start(); }
        public void Reset() { Running = false; _left = TimeSpan.Zero; Kick(); }

        public void SwToggle()
        {
            if (SwRunning) { _swAcc += DateTime.Now - _swStart; SwRunning = false; }
            else { _swStart = DateTime.Now; SwRunning = true; }
            Kick();
        }

        public void SwLap() { if (SwRunning) { Laps.Insert(0, SwElapsed); if (Laps.Count > 20) Laps.RemoveAt(20); Changed?.Invoke(); } }
        public void SwReset() { SwRunning = false; _swAcc = TimeSpan.Zero; Laps.Clear(); Kick(); }

        public void SetSleep(int minutes)
        {
            SleepAt = minutes <= 0 ? null : DateTime.Now.AddMinutes(minutes);
            Kick();
        }

        void Tick()
        {
            if (Running && DateTime.Now >= _endsAt)
            {
                Running = false;
                _left = TimeSpan.Zero;
                Finished?.Invoke();
            }
            if (SleepAt is DateTime s && DateTime.Now >= s)
            {
                SleepAt = null;
                SleepFired?.Invoke();
            }
            Kick();
        }
    }

    // =====================================================================
    //  Погода (open-meteo.com, без ключей) и мировые часы
    // =====================================================================
    public class WeatherNow
    {
        public string City;
        public double Lat, Lon;
        public double Temp, Feels, Wind, Humidity;
        public int Code;
        public List<(DateTime Day, double Max, double Min, int Code)> Days = new();
        public DateTime At;
    }

    public static class WeatherService
    {
        static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
        static WeatherNow _cache;
        static string _cacheKey;

        public static WeatherNow Cached => _cache;
        public static event Action Updated;

        /// <summary>Город по умолчанию — из часового пояса Windows (без определения по IP).</summary>
        public static string DefaultCity()
        {
            try
            {
                if (TimeZoneInfo.TryConvertWindowsIdToIanaId(TimeZoneInfo.Local.Id, out var iana) && iana.Contains('/'))
                    return iana.Split('/').Last().Replace('_', ' ') switch { "Kiev" => "Kyiv", var c => c };
            }
            catch { }
            return "Kyiv";
        }

        public static string City => string.IsNullOrWhiteSpace(Store.Settings.WeatherCity) ? DefaultCity() : Store.Settings.WeatherCity.Trim();

        /// <summary>Почему погоды нет: город не найден или нет связи (для страницы погоды).</summary>
        public static string LastError { get; private set; }

        static readonly Dictionary<string, (double Lat, double Lon, string Name)> GeoCache = new(StringComparer.OrdinalIgnoreCase);
        static Task<WeatherNow> _inflight;

        /// <summary>
        /// Координаты города. Ищем на нескольких языках (украинское «Миколаїв» на русском поиске не находится),
        /// из найденного берём самый крупный город — в первую очередь в стране из настроек Windows
        /// (иначе на «Mykolaiv» первым приходит село во Львовской области).
        /// </summary>
        static async Task<(double Lat, double Lon, string Name)?> Geocode(string city)
        {
            if (GeoCache.TryGetValue(city, out var hit)) return hit;
            var langs = new List<string>();
            if (city.IndexOfAny("їЇєЄіІґҐ".ToCharArray()) >= 0) langs.Add("uk");
            langs.AddRange(new[] { I18n.Current, "ru", "uk", "en" });
            string region = "";
            try { region = new RegionInfo(CultureInfo.CurrentCulture.Name).TwoLetterISORegionName; } catch { }
            foreach (var lang in langs.Distinct())
            {
                using var g = JsonDocument.Parse(await Http.GetStringAsync(
                    $"https://geocoding-api.open-meteo.com/v1/search?count=10&language={lang}&name=" + Uri.EscapeDataString(city)));
                if (!g.RootElement.TryGetProperty("results", out var res) || res.GetArrayLength() == 0) continue;
                var best = res.EnumerateArray()
                    .Select(r => (r, pop: r.TryGetProperty("population", out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt64() : 0,
                                  home: r.TryGetProperty("country_code", out var cc) && string.Equals(cc.GetString(), region, StringComparison.OrdinalIgnoreCase)))
                    .OrderByDescending(x => x.home && x.pop > 0).ThenByDescending(x => x.pop).First().r;
                var found = (best.GetProperty("latitude").GetDouble(), best.GetProperty("longitude").GetDouble(),
                             best.TryGetProperty("name", out var n) ? n.GetString() : city);
                GeoCache[city] = found;
                return found;
            }
            return null;
        }

        public static Task<WeatherNow> Get(bool force = false)
        {
            var key = City;
            if (!force && _cache != null && _cacheKey == key && (DateTime.Now - _cache.At).TotalMinutes < 20) return Task.FromResult(_cache);
            // уже грузим — ждём ту же загрузку, а не отвечаем «нет погоды»
            if (_inflight != null && !_inflight.IsCompleted) return _inflight;
            return _inflight = Load(key);
        }

        static async Task<WeatherNow> Load(string key)
        {
            try
            {
                var geo = await Geocode(key);
                if (geo == null) { LastError = $"Город «{key}» не найден. Напиши его по-другому, например по-английски, в «Настройки → Погода и часы»."; return _cache; }
                var (lat, lon, name) = geo.Value;
                var url = string.Format(CultureInfo.InvariantCulture,
                    "https://api.open-meteo.com/v1/forecast?latitude={0}&longitude={1}&current=temperature_2m,apparent_temperature,relative_humidity_2m,wind_speed_10m,weather_code&daily=weather_code,temperature_2m_max,temperature_2m_min&timezone=auto&forecast_days=5&wind_speed_unit=ms",
                    lat, lon);
                using var d = JsonDocument.Parse(await Http.GetStringAsync(url));
                var cur = d.RootElement.GetProperty("current");
                var w = new WeatherNow
                {
                    City = name,
                    Lat = lat,
                    Lon = lon,
                    Temp = cur.GetProperty("temperature_2m").GetDouble(),
                    Feels = cur.GetProperty("apparent_temperature").GetDouble(),
                    Humidity = cur.GetProperty("relative_humidity_2m").GetDouble(),
                    Wind = cur.GetProperty("wind_speed_10m").GetDouble(),
                    Code = cur.GetProperty("weather_code").GetInt32(),
                    At = DateTime.Now,
                };
                var daily = d.RootElement.GetProperty("daily");
                var times = daily.GetProperty("time");
                for (int i = 0; i < times.GetArrayLength(); i++)
                {
                    w.Days.Add((DateTime.Parse(times[i].GetString(), CultureInfo.InvariantCulture),
                        daily.GetProperty("temperature_2m_max")[i].GetDouble(),
                        daily.GetProperty("temperature_2m_min")[i].GetDouble(),
                        daily.GetProperty("weather_code")[i].GetInt32()));
                }
                _cache = w;
                _cacheKey = key;
                LastError = null;
                Updated?.Invoke();
                return w;
            }
            catch (Exception e)
            {
                Log.Info("weather: " + e.Message);
                LastError = "Нет связи с сервисом погоды — попробую ещё раз через несколько минут.";
                return _cache;
            }
        }

        /// <summary>Значок (Segoe UI Emoji) и описание по коду WMO.</summary>
        public static (string Icon, string Text) Describe(int code) => code switch
        {
            0 => ("☀", "Ясно"),
            1 => ("🌤", "Почти ясно"),
            2 => ("⛅", "Переменная облачность"),
            3 => ("☁", "Пасмурно"),
            45 or 48 => ("☁", "Туман"),
            51 or 53 or 55 or 56 or 57 => ("🌦", "Морось"),
            61 or 63 or 65 or 66 or 67 => ("🌧", "Дождь"),
            71 or 73 or 75 or 77 => ("❄", "Снег"),
            80 or 81 or 82 => ("🌧", "Ливень"),
            85 or 86 => ("🌨", "Снегопад"),
            95 or 96 or 99 => ("⛈", "Гроза"),
            _ => ("🌡", "—"),
        };

        public static List<(string Name, DateTime Time)> WorldClocks()
        {
            var list = new List<(string, DateTime)>();
            foreach (var part in (Store.Settings.WorldClocks ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = part.Split('|');
                try
                {
                    var tz = TimeZoneInfo.FindSystemTimeZoneById(kv[0].Trim());
                    list.Add((kv.Length > 1 ? kv[1].Trim() : kv[0].Split('/').Last(), TimeZoneInfo.ConvertTime(DateTime.Now, tz)));
                }
                catch { }
            }
            return list;
        }
    }

    // =====================================================================
    //  Раскладка клавиатуры и Caps Lock
    // =====================================================================
    public sealed class KeyboardWatch
    {
        public static KeyboardWatch I { get; } = new();
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, IntPtr pid);
        [DllImport("user32.dll")] static extern IntPtr GetKeyboardLayout(uint thread);
        [DllImport("user32.dll")] static extern short GetKeyState(int key);

        readonly DispatcherTimer _t = new() { Interval = TimeSpan.FromMilliseconds(250) };
        IntPtr _lastFg;
        int _lastLang = -1;
        bool? _caps;

        public event Action<string, string> LayoutChanged; // (короткое, полное)
        public event Action<bool> CapsChanged;

        public void Start()
        {
            _t.Tick += (_, __) => Poll();
            Apply();
        }

        public void Apply()
        {
            var s = Store.Settings;
            if (s.HudKeyboard || s.HudCaps) _t.Start(); else _t.Stop();
        }

        void Poll()
        {
            var s = Store.Settings;
            if (s.HudCaps)
            {
                bool caps = (GetKeyState(0x14) & 1) != 0;
                if (_caps != null && caps != _caps) CapsChanged?.Invoke(caps);
                _caps = caps;
            }
            if (s.HudKeyboard)
            {
                var fg = GetForegroundWindow();
                int lang = (int)((long)GetKeyboardLayout(GetWindowThreadProcessId(fg, IntPtr.Zero)) & 0xFFFF);
                // смена окна с другой раскладкой — не событие; смена раскладки в том же окне — да
                if (fg == _lastFg && _lastLang >= 0 && lang != _lastLang && lang != 0)
                {
                    try
                    {
                        var ci = new CultureInfo(lang);
                        LayoutChanged?.Invoke(ci.TwoLetterISOLanguageName.ToUpperInvariant(), Fmt.Cap(ci.NativeName.Split(' ', '(')[0]));
                    }
                    catch { }
                }
                _lastFg = fg;
                if (lang != 0) _lastLang = lang;
            }
        }
    }

    // =====================================================================
    //  Скорость сети
    // =====================================================================
    public static class NetSpeed
    {
        static long _rx, _tx;
        static DateTime _at;
        public static double Down { get; private set; }
        public static double Up { get; private set; }

        public static void Sample()
        {
            long rx = 0, tx = 0;
            try
            {
                foreach (var n in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (n.OperationalStatus != OperationalStatus.Up || n.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                    var st = n.GetIPStatistics();
                    rx += st.BytesReceived;
                    tx += st.BytesSent;
                }
            }
            catch { return; }
            var now = DateTime.Now;
            double sec = (now - _at).TotalSeconds;
            if (_at != default && sec > 0.2)
            {
                Down = Math.Max(0, (rx - _rx) / sec);
                Up = Math.Max(0, (tx - _tx) / sec);
            }
            _rx = rx; _tx = tx; _at = now;
        }

        public static string Fmt(double bps) =>
            bps >= 1024 * 1024 ? (bps / 1024 / 1024).ToString("0.0", CultureInfo.InvariantCulture) + " МБ/с"
            : bps >= 1024 ? (bps / 1024).ToString("0", CultureInfo.InvariantCulture) + " КБ/с"
            : bps.ToString("0", CultureInfo.InvariantCulture) + " Б/с";
    }

    // =====================================================================
    //  Пипетка: цвет под курсором → HEX в буфер
    // =====================================================================
    public sealed class ColorPicker
    {
        public static ColorPicker I { get; } = new();

        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] struct MSLL { public POINT pt; public uint data, flags, time; public IntPtr extra; }
        delegate IntPtr HookProc(int code, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr mod, uint thread);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr h, int code, IntPtr w, IntPtr l);
        [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("gdi32.dll")] static extern uint GetPixel(IntPtr dc, int x, int y);

        IntPtr _hook;
        HookProc _proc;
        readonly DispatcherTimer _t = new() { Interval = TimeSpan.FromMilliseconds(70) };
        DateTime _until;

        public bool Active => _hook != IntPtr.Zero;
        public event Action<System.Windows.Media.Color> Preview;
        public event Action<System.Windows.Media.Color?> Done; // null — отмена

        ColorPicker() { _t.Tick += (_, __) => { if (DateTime.Now > _until) Finish(null); else Preview?.Invoke(Under()); }; }

        public void Start()
        {
            if (Active) return;
            _swallowUp = false;
            _proc = Hook;
            _hook = SetWindowsHookEx(14 /*WH_MOUSE_LL*/, _proc, GetModuleHandle(null), 0);
            _until = DateTime.Now.AddSeconds(20);
            _t.Start();
        }

        bool _swallowUp;

        IntPtr Hook(int code, IntPtr w, IntPtr l)
        {
            if (code >= 0)
            {
                int msg = w.ToInt32();
                if (msg is 0x0201 or 0x0204 && !_swallowUp) // кнопка нажата: левая — взять цвет, правая — отмена
                {
                    _swallowUp = true;
                    System.Windows.Media.Color? c = msg == 0x0201 ? Under() : null;
                    Application.Current.Dispatcher.BeginInvoke(new Action(() => Finish(c)));
                    return new IntPtr(1);
                }
                if (msg is 0x0202 or 0x0205 && _swallowUp) // отпускание того же клика тоже глотаем
                {
                    _swallowUp = false;
                    return new IntPtr(1);
                }
            }
            return CallNextHookEx(_hook, code, w, l);
        }

        static System.Windows.Media.Color Under()
        {
            GetCursorPos(out var p);
            var dc = GetDC(IntPtr.Zero);
            uint c = GetPixel(dc, p.X, p.Y);
            ReleaseDC(IntPtr.Zero, dc);
            return System.Windows.Media.Color.FromRgb((byte)(c & 0xFF), (byte)((c >> 8) & 0xFF), (byte)((c >> 16) & 0xFF));
        }

        void Finish(System.Windows.Media.Color? c)
        {
            if (!Active) return;
            _t.Stop();
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
            if (c is { } col)
            {
                try { Clipboard.SetText(Hex(col)); } catch { }
            }
            Done?.Invoke(c);
        }

        public static string Hex(System.Windows.Media.Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    }

    // =====================================================================
    //  Центр уведомлений (в памяти, последние 60)
    // =====================================================================
    public class LogItem
    {
        public string App, AppId, Title, Body;
        public DateTime Time = DateTime.Now;
        public bool IsCall;
    }

    public static class NotificationLog
    {
        public static ObservableCollection<LogItem> Items { get; } = new();
        public static int Unread { get; private set; }
        public static event Action Changed;

        public static void Add(Toast t)
        {
            Items.Insert(0, new LogItem { App = t.Site ?? t.App, AppId = t.AppId, Title = t.Title, Body = t.Body, IsCall = t.IsCall });
            while (Items.Count > 60) Items.RemoveAt(Items.Count - 1);
            Unread++;
            Changed?.Invoke();
        }

        public static void MarkRead() { if (Unread == 0) return; Unread = 0; Changed?.Invoke(); }
        public static void Remove(LogItem i) { if (Items.Remove(i)) Changed?.Invoke(); }
        public static void Clear() { Items.Clear(); Unread = 0; Changed?.Invoke(); }
    }

    // =====================================================================
    //  Калькулятор для строки запуска
    // =====================================================================
    public static class Calc
    {
        static readonly Regex Allowed = new(@"^[\d\s\.,\+\-\*/%\(\)\^]+$", RegexOptions.Compiled);

        public static string TryEval(string expr)
        {
            if (string.IsNullOrWhiteSpace(expr) || !Allowed.IsMatch(expr) || !Regex.IsMatch(expr, @"\d\s*[\+\-\*/%\^]\s*[\d\(]")) return null;
            try
            {
                var p = new Parser(expr.Replace(',', '.'));
                double v = p.Expr();
                if (p.Pos < p.S.Length || double.IsNaN(v) || double.IsInfinity(v)) return null;
                return Math.Round(v, 10).ToString("G12", CultureInfo.InvariantCulture);
            }
            catch { return null; }
        }

        /// <summary>Маленький рекурсивный разбор: + - * / % ^ и скобки.</summary>
        class Parser
        {
            public readonly string S;
            public int Pos;
            public Parser(string s) { S = s.Replace(" ", ""); }
            char Peek => Pos < S.Length ? S[Pos] : '\0';

            public double Expr()
            {
                double v = Term();
                while (Peek is '+' or '-') { char op = S[Pos++]; double r = Term(); v = op == '+' ? v + r : v - r; }
                return v;
            }

            double Term()
            {
                double v = Power();
                while (Peek is '*' or '/' or '%') { char op = S[Pos++]; double r = Power(); v = op == '*' ? v * r : op == '/' ? v / r : v % r; }
                return v;
            }

            double Power()
            {
                double v = Unary();
                if (Peek == '^') { Pos++; v = Math.Pow(v, Power()); }
                return v;
            }

            double Unary()
            {
                if (Peek == '-') { Pos++; return -Unary(); }
                if (Peek == '+') { Pos++; return Unary(); }
                if (Peek == '(') { Pos++; double v = Expr(); if (Peek != ')') throw new FormatException(); Pos++; return v; }
                int start = Pos;
                while (char.IsDigit(Peek) || Peek == '.') Pos++;
                return double.Parse(S[start..Pos], CultureInfo.InvariantCulture);
            }
        }
    }
}
