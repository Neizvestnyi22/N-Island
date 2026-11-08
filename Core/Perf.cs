using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;

namespace NIsland.Core
{
    /// <summary>
    /// Счётчики кадровых циклов для поиска подтормаживаний: NISLAND_PERF=1 → раз в 10 с в журнал
    /// «сколько раз сработал каждый цикл». Без переменной — пустые вызовы, ничего не считается.
    /// </summary>
    public static class Perf
    {
        public static readonly bool On = Environment.GetEnvironmentVariable("NISLAND_PERF") == "1";
        static readonly ConcurrentDictionary<string, int> Hits = new();
        static Timer _t;

        public static void Hit(string what)
        {
            if (!On) return;
            Hits.AddOrUpdate(what, 1, (_, v) => v + 1);
            _t ??= new Timer(_ =>
            {
                var snap = Hits.ToArray();
                Hits.Clear();
                Log.Info("PERF 10s: " + string.Join(", ", snap.OrderByDescending(k => k.Value).Select(k => $"{k.Key}={k.Value}")));
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(ScanAnimations));
            }, null, 10000, 10000);
        }

        /// <summary>Какие элементы в окнах сейчас под анимацией (бесконечная анимация = постоянная перерисовка).</summary>
        static void ScanAnimations()
        {
            foreach (System.Windows.Window w in System.Windows.Application.Current.Windows)
            {
                var found = new System.Collections.Generic.List<string>();
                void Walk(System.Windows.DependencyObject d, int depth)
                {
                    if (d is System.Windows.UIElement u && u.HasAnimatedProperties && u.IsVisible)
                        found.Add(u.GetType().Name + (u is System.Windows.FrameworkElement fe && fe.Name != "" ? "#" + fe.Name : "") + "@" + depth);
                    int n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(d);
                    for (int i = 0; i < n; i++) Walk(System.Windows.Media.VisualTreeHelper.GetChild(d, i), depth + 1);
                }
                try { Walk(w, 0); } catch { }
                if (found.Count > 0) Log.Info($"PERF anim [{w.GetType().Name} {w.Title}]: " + string.Join(", ", found.Take(25)) + (found.Count > 25 ? $" …+{found.Count - 25}" : ""));
            }
        }
    }
}
