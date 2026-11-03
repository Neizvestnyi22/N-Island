using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NIsland.Core
{
    /// <summary>Для какого события звук: у каждого свой выбор в настройках.</summary>
    public enum SoundEvent { Timer, Reminder, Notification, Alert }

    /// <summary>
    /// Звуки N-Island синтезируются прямо в памяти (без файлов): синус с обертонами и мягким затуханием —
    /// колокольчики, маримба, перезвон, будильник. Готовый WAV кэшируется, проигрывается через SoundPlayer.
    /// </summary>
    public static class Sound
    {
        public static readonly (string Key, string Name)[] Themes =
        {
            ("glass", "Стекло"), ("chime", "Перезвон"), ("marimba", "Маримба"), ("soft", "Мягкий"),
            ("bell", "Колокол"), ("digital", "Цифровой"), ("alarm", "Будильник"), ("system", "Windows"), ("none", "Без звука"),
        };

        const int Rate = 22050;
        static readonly Dictionary<string, byte[]> Cache = new();
        static System.Media.SoundPlayer _player;   // держим ссылку, иначе звук может оборваться сборщиком мусора

        static Settings S => Store.Settings;

        public static string ThemeFor(SoundEvent e) => e switch
        {
            SoundEvent.Timer => S.SoundTimer,
            SoundEvent.Reminder => S.SoundReminder,
            SoundEvent.Notification => S.SoundNotify,
            _ => S.SoundAlert,
        };

        /// <summary>Старый вызов — короткий сигнал напоминания.</summary>
        public static void Ding() => Play(SoundEvent.Reminder);

        /// <summary>Таймер и важные события звучат даже в «Не беспокоить» (если звуки вообще включены); остальное — только в обычном режиме.</summary>
        public static void Play(SoundEvent e)
        {
            if (!S.Sounds) return;
            bool important = e is SoundEvent.Timer or SoundEvent.Alert;
            if (!important && Modes.Silent) return;
            Preview(ThemeFor(e), e == SoundEvent.Timer);
        }

        /// <summary>Проиграть звук темы (кнопка «Прослушать» в настройках).</summary>
        public static void Preview(string theme, bool repeat = false)
        {
            try
            {
                if (theme is null or "none") return;
                if (theme == "system") { System.Media.SystemSounds.Asterisk.Play(); return; }
                int vol = Math.Clamp(S.SoundVolume, 0, 100);
                if (vol == 0) return;
                var key = $"{theme}|{repeat}|{vol}";
                if (!Cache.TryGetValue(key, out var wav))
                {
                    if (Cache.Count > 24) Cache.Clear();
                    wav = Cache[key] = Wav(Synth(theme, repeat), vol / 100.0);
                }
                _player?.Stop();
                _player = new System.Media.SoundPlayer(new MemoryStream(wav, false));
                _player.Play();
            }
            catch (Exception ex) { Log.Info("sound: " + ex.Message); }
        }

        // ------------------------------------------------------------------
        //  Синтез
        // ------------------------------------------------------------------

        /// <summary>Нота: частота, начало и длина (с), громкость, тембр.</summary>
        record Note(double Freq, double Start, double Len, double Gain, string Timbre);

        static float[] Synth(string theme, bool repeat)
        {
            var notes = theme switch
            {
                // две прозрачные ноты, как лёгкий звон бокала
                "glass" => new List<Note> { new(1568, 0, 1.1, 0.8, "glass"), new(2093, 0.12, 1.3, 0.7, "glass") },
                // три восходящие ноты колокольчика
                "chime" => new List<Note> { new(1047, 0, 1.0, 0.7, "bell"), new(1319, 0.14, 1.0, 0.7, "bell"), new(1568, 0.28, 1.4, 0.8, "bell") },
                // деревянная маримба: короткие тёплые удары
                "marimba" => new List<Note> { new(880, 0, 0.5, 0.9, "marimba"), new(1109, 0.11, 0.5, 0.8, "marimba"), new(1319, 0.22, 0.7, 0.8, "marimba") },
                // мягкий низкий тон с плавной атакой — не пугает
                "soft" => new List<Note> { new(659, 0, 1.4, 0.8, "soft"), new(988, 0.18, 1.6, 0.5, "soft") },
                // глубокий колокол
                "bell" => new List<Note> { new(523, 0, 2.2, 0.9, "bell") },
                // два чистых коротких сигнала
                "digital" => new List<Note> { new(1760, 0, 0.09, 0.6, "digital"), new(1760, 0.16, 0.09, 0.6, "digital") },
                // будильник: настойчивая трель
                "alarm" => Enumerable.Range(0, 4).SelectMany(i => new[] { new Note(1319, i * 0.32, 0.12, 0.7, "digital"), new Note(1760, i * 0.32 + 0.14, 0.12, 0.7, "digital") }).ToList(),
                _ => new List<Note> { new(1568, 0, 1.0, 0.8, "glass") },
            };
            if (repeat && theme != "alarm")
            {
                // таймер: мелодия звучит трижды, чтобы её точно услышали
                double span = notes.Max(n => n.Start) + 0.55;
                notes = Enumerable.Range(0, 3).SelectMany(i => notes.Select(n => n with { Start = n.Start + i * span })).ToList();
            }
            double total = notes.Max(n => n.Start + n.Len) + 0.05;
            var buf = new float[(int)(total * Rate)];
            foreach (var n in notes) Render(buf, n);
            float peak = buf.Length == 0 ? 1 : buf.Max(Math.Abs);
            if (peak > 0) for (int i = 0; i < buf.Length; i++) buf[i] /= peak;
            return buf;
        }

        static void Render(float[] buf, Note n)
        {
            int start = (int)(n.Start * Rate), len = (int)(n.Len * Rate);
            // тембр = набор обертонов (множитель частоты, громкость, скорость затухания)
            (double Mul, double Amp, double Decay)[] partials = n.Timbre switch
            {
                "glass" => new[] { (1.0, 1.0, 3.5), (2.0, 0.25, 6.0), (3.01, 0.08, 9.0) },
                "bell" => new[] { (1.0, 1.0, 2.2), (2.76, 0.35, 4.5), (5.40, 0.15, 7.0), (0.5, 0.2, 1.8) },
                "marimba" => new[] { (1.0, 1.0, 9.0), (3.93, 0.28, 22.0), (9.2, 0.05, 40.0) },
                "soft" => new[] { (1.0, 1.0, 2.0), (2.0, 0.12, 3.5) },
                _ => new[] { (1.0, 1.0, 1.0), (3.0, 0.18, 1.0), (5.0, 0.06, 1.0) },   // digital: почти прямоугольный, но без резкости
            };
            double attack = n.Timbre == "soft" ? 0.04 : 0.004;
            for (int i = 0; i < len && start + i < buf.Length; i++)
            {
                double t = (double)i / Rate;
                double env = Math.Min(1, t / attack);
                if (n.Timbre == "digital") env *= Math.Min(1, (n.Len - t) / 0.02);   // ровный тон с мягким краем
                double s = 0;
                foreach (var (mul, amp, decay) in partials)
                    s += amp * Math.Sin(2 * Math.PI * n.Freq * mul * t) * (n.Timbre == "digital" ? 1 : Math.Exp(-decay * t));
                buf[start + i] += (float)(s * env * n.Gain);
            }
        }

        static byte[] Wav(float[] samples, double volume)
        {
            using var ms = new MemoryStream(44 + samples.Length * 2);
            using var w = new BinaryWriter(ms);
            w.Write("RIFF"u8.ToArray()); w.Write(36 + samples.Length * 2); w.Write("WAVE"u8.ToArray());
            w.Write("fmt "u8.ToArray()); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
            w.Write("data"u8.ToArray()); w.Write(samples.Length * 2);
            double gain = 0.85 * volume * volume;   // громкость на слух — квадратичная шкала
            foreach (var s in samples) w.Write((short)Math.Clamp(s * gain * short.MaxValue, short.MinValue, short.MaxValue));
            return ms.ToArray();
        }
    }
}
