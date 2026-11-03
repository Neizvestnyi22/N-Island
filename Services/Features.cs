using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Threading;
using NIsland.Core;

namespace NIsland.Services
{
    public enum PomoPhase { Focus, Short, Long }

    public sealed class PomodoroService
    {
        public static PomodoroService I { get; } = new();

        readonly DispatcherTimer _t = new() { Interval = TimeSpan.FromMilliseconds(500) };
        DateTime _endsAt;

        public PomoPhase Phase { get; private set; } = PomoPhase.Focus;
        public bool Running { get; private set; }
        public TimeSpan Remaining { get; private set; }
        public int Round { get; private set; }

        public event Action Changed;
        public event Action<PomoPhase, PomoPhase> Finished;

        PomodoroService()
        {
            _t.Tick += (_, __) => OnTick();
        }

        static Settings S => Store.Settings;

        public TimeSpan Total => TimeSpan.FromMinutes(Phase switch
        {
            PomoPhase.Focus => S.PomoWork,
            PomoPhase.Short => S.PomoShort,
            _ => S.PomoLong,
        });

        public double Progress => Total.TotalSeconds <= 0 ? 0 : Math.Clamp(1 - Remaining.TotalSeconds / Total.TotalSeconds, 0, 1);
        public bool Active => Running || Remaining < Total;

        public string PhaseName => Phase switch
        {
            PomoPhase.Focus => "Фокус",
            PomoPhase.Short => "Перерыв",
            _ => "Длинный перерыв",
        };

        public static int TodayCount
        {
            get
            {
                Store.Data.PomodoroDone.TryGetValue(DateTime.Today.ToString("yyyy-MM-dd"), out var n);
                return n;
            }
        }

        public void Init() => Remaining = Total;

        public void Start()
        {
            if (Remaining <= TimeSpan.Zero) Remaining = Total;
            _endsAt = DateTime.Now + Remaining;
            Running = true;
            _t.Start();
            Changed?.Invoke();
        }

        public void Pause()
        {
            if (!Running) return;
            Remaining = _endsAt - DateTime.Now;
            Running = false;
            _t.Stop();
            Changed?.Invoke();
        }

        public void Toggle() { if (Running) Pause(); else Start(); }

        public void Reset()
        {
            Running = false; _t.Stop();
            Remaining = Total;
            Changed?.Invoke();
        }

        public void SetPhase(PomoPhase p)
        {
            Running = false; _t.Stop();
            Phase = p; Remaining = Total;
            Changed?.Invoke();
        }

        public void Skip() => Advance(false);

        /// <summary>Длительности поменяли в настройках.</summary>
        public void SettingsChanged()
        {
            if (!Running) { Remaining = Total; Changed?.Invoke(); }
        }

        void OnTick()
        {
            if (!Running) return;
            Remaining = _endsAt - DateTime.Now;
            if (Remaining <= TimeSpan.Zero) Advance(true);
            else Changed?.Invoke();
        }

        void Advance(bool completed)
        {
            var prev = Phase;
            if (Phase == PomoPhase.Focus)
            {
                if (completed)
                {
                    var key = DateTime.Today.ToString("yyyy-MM-dd");
                    Store.Data.PomodoroDone[key] = TodayCount + 1;
                    Store.SaveData();
                }
                Round++;
                Phase = Round % Math.Max(1, S.PomoRounds) == 0 ? PomoPhase.Long : PomoPhase.Short;
            }
            else Phase = PomoPhase.Focus;

            Remaining = Total;
            Running = completed && S.PomoAuto;
            if (Running) _endsAt = DateTime.Now + Remaining; else _t.Stop();
            Changed?.Invoke();
            if (completed) Finished?.Invoke(prev, Phase);
        }
    }

    public static class Water
    {
        static Settings S => Store.Settings;
        public static DateTime LastReminder = DateTime.Now;

        public static int TodayMl => Store.Data.Water.Where(w => w.Time.Date == DateTime.Today).Sum(w => w.Ml);
        public static double Progress => S.WaterGoal <= 0 ? 0 : Math.Clamp(TodayMl / (double)S.WaterGoal, 0, 1);
        public static DateTime? LastDrink => Store.Data.Water.Count == 0 ? null : Store.Data.Water.Max(w => w.Time);

        public static event Action Changed;

        public static void Drink(int ml)
        {
            Store.Data.Water.Add(new WaterEntry { Time = DateTime.Now, Ml = ml });
            LastReminder = DateTime.Now;
            Store.SaveData();
            Changed?.Invoke();
        }

        public static void Undo()
        {
            var last = Store.Data.Water.Where(w => w.Time.Date == DateTime.Today).OrderBy(w => w.Time).LastOrDefault();
            if (last == null) return;
            Store.Data.Water.Remove(last);
            Store.SaveData();
            Changed?.Invoke();
        }

        public static int[] Hourly()
        {
            var a = new int[24];
            foreach (var w in Store.Data.Water.Where(w => w.Time.Date == DateTime.Today)) a[w.Time.Hour] += w.Ml;
            return a;
        }

        public static DateTime? NextReminder
        {
            get
            {
                if (!S.WaterRemind) return null;
                var baseT = LastDrink is DateTime d && d > LastReminder ? d : LastReminder;
                var next = baseT.AddMinutes(Math.Max(5, S.WaterInterval));
                if (next.Hour < S.WaterFrom) next = next.Date.AddHours(S.WaterFrom);
                if (next.Hour >= S.WaterTo) next = next.Date.AddDays(1).AddHours(S.WaterFrom);
                return next;
            }
        }

        public static bool Due()
        {
            if (!S.WaterRemind || !S.PgWater) return false;
            var now = DateTime.Now;
            if (now.Hour < S.WaterFrom || now.Hour >= S.WaterTo) return false;
            if (TodayMl >= S.WaterGoal) return false;
            return NextReminder is DateTime n && now >= n;
        }
    }

    public static class PrayerService
    {
        static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
        static string _key;
        static List<(string Name, TimeSpan At)> _cache;

        public static readonly string[] Methods =
        {
            "Шиитский (Джафари)", "Карачи", "ISNA (Сев. Америка)", "Всемирная исламская лига", "Умм аль-Кура (Мекка)",
            "Египет", "", "Тегеран", "Персидский залив", "Кувейт", "Катар", "Сингапур", "Франция", "Турция (Диянет)", "Россия (ДУМ)",
        };

        public static string MethodName(int m) => m >= 0 && m < Methods.Length && Methods[m] != "" ? Methods[m] : "Метод " + m;

        public static List<(string Name, TimeSpan At)> Cached => _cache;

        public static async Task<List<(string Name, TimeSpan At)>> Get(bool force = false)
        {
            var s = Store.Settings;
            var key = $"{DateTime.Today:yyyy-MM-dd}|{s.PrayerCity}|{s.PrayerCountry}|{s.PrayerMethod}";
            if (!force && key == _key && _cache != null) return _cache;
            var url = $"https://api.aladhan.com/v1/timingsByCity?city={Uri.EscapeDataString(s.PrayerCity)}&country={Uri.EscapeDataString(s.PrayerCountry)}&method={s.PrayerMethod}";
            var json = await Http.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);
            var t = doc.RootElement.GetProperty("data").GetProperty("timings");
            var names = new[] { ("Fajr", "Фаджр"), ("Sunrise", "Восход"), ("Dhuhr", "Зухр"), ("Asr", "Аср"), ("Maghrib", "Магриб"), ("Isha", "Иша") };
            var list = new List<(string, TimeSpan)>();
            foreach (var (en, ru) in names)
            {
                var v = t.GetProperty(en).GetString() ?? "";
                if (v.Length >= 5 && TimeSpan.TryParse(v[..5], out var ts)) list.Add((ru, ts));
            }
            _key = key; _cache = list;
            return list;
        }
    }
}
