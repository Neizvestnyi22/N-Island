using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Threading;

namespace NIsland.Core
{
    /// <summary>
    /// Общий кадровый такт острова (до 30 кадров/с). Персонаж, полоски эквалайзера и ободок в такт раньше тикали
    /// каждый своим таймером — и каждый вызывал отдельную перерисовку всего окна (до ~80 в секунду).
    /// Теперь они получают кадры в одни и те же моменты, и WPF рисует их изменения одной перерисовкой.
    /// Подписчик с меньшей частотой получает каждый 2-й, 3-й, 5-й такт (30 / 15 / 10 / 6 кадров/с).
    /// </summary>
    public static class FrameClock
    {
        public const int BaseFps = 30;
        static readonly DispatcherTimer Timer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(1000.0 / BaseFps) };
        static readonly List<Sub> Subs = new();
        static long _tick;

        sealed class Sub : IDisposable
        {
            public Action<double> Fn;
            public int Every = 1;
            public DateTime Last;
            public void Dispose() => Remove(this);
        }

        static FrameClock() => Timer.Tick += (_, __) => Tick();

        /// <summary>Подписаться на кадры с частотой fps (округляется до делителя 30). Dispose — отписаться.</summary>
        public static IDisposable Add(Action<double> fn, double fps)
        {
            var s = new Sub { Fn = fn, Last = DateTime.Now };
            SetFps(s, fps);
            Subs.Add(s);
            if (!Timer.IsEnabled) Timer.Start();
            return s;
        }

        /// <summary>Сменить частоту уже подписанного.</summary>
        public static void SetFps(IDisposable handle, double fps)
        {
            if (handle is not Sub s) return;
            s.Every = fps >= 30 ? 1 : fps >= 15 ? 2 : fps >= 10 ? 3 : fps >= 6 ? 5 : 10;
        }

        static void Remove(Sub s)
        {
            Subs.Remove(s);
            if (Subs.Count == 0) Timer.Stop();
        }

        static void Tick()
        {
            _tick++;
            var now = DateTime.Now;
            foreach (var s in Subs.ToArray())
            {
                if (_tick % s.Every != 0) continue;
                double dt = Math.Clamp((now - s.Last).TotalSeconds, 0, 0.25);
                s.Last = now;
                try { s.Fn(dt); } catch (Exception e) { Log.Error(e); Remove(s); }
            }
        }
    }
}
