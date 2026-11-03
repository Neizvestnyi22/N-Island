using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using NIsland.Core;

namespace NIsland.Services
{
    /// <summary>
    /// То, что сейчас звучит в колонках или наушниках (WASAPI loopback): спектр по полосам, форма волны, громкость и удары (бит).
    /// Для визуализаторов, живых полосок в чёлке и подсветки эквалайзера. Захват идёт, только пока кто-то смотрит (<see cref="Use"/>);
    /// в остальное время поток спит и ничего не тратит.
    /// </summary>
    public sealed class AudioCapture
    {
        public static AudioCapture I { get; } = new();

        public const int BandCount = 64;
        public const int WaveCount = 512;
        const int N = 2048;                       // окно БПФ: 2048 отсчётов ≈ 43 мс при 48 кГц
        const double MinHz = 32, MaxHz = 16000;

        // ------------------------------------------------------------------
        //  Core Audio (без сторонних библиотек)
        // ------------------------------------------------------------------

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDeviceEnumerator
        {
            int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
            int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDevice
        {
            int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
            int OpenPropertyStore(int access, out IntPtr store);
            int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        }

        [ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioClient
        {
            int Initialize(int shareMode, int streamFlags, long bufferDuration, long periodicity, IntPtr format, IntPtr sessionGuid);
            int GetBufferSize(out uint frames);
            int GetStreamLatency(out long latency);
            int GetCurrentPadding(out uint padding);
            int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closest);
            int GetMixFormat(out IntPtr format);
            int GetDevicePeriod(out long def, out long min);
            int Start();
            int Stop();
            int Reset();
            int SetEventHandle(IntPtr handle);
            int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
        }

        [ComImport, Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioCaptureClient
        {
            int GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong devicePosition, out ulong qpcPosition);
            int ReleaseBuffer(uint frames);
            int GetNextPacketSize(out uint frames);
        }

        // ------------------------------------------------------------------
        //  Состояние
        // ------------------------------------------------------------------

        readonly object _gate = new();
        readonly float[] _bands = new float[BandCount], _peaks = new float[BandCount], _raw = new float[BandCount];
        readonly float[] _wave = new float[WaveCount];
        readonly float[] _ring = new float[N * 2];
        int _ringPos;
        readonly float[] _re = new float[N], _im = new float[N], _win = new float[N];
        int[] _binLo = new int[BandCount], _binHi = new int[BandCount];
        readonly double[] _tilt = new double[BandCount];
        int _rate;
        int _users;
        volatile bool _restart;
        readonly AutoResetEvent _wake = new(false);
        Thread _thread;
        static readonly Stopwatch Mono = Stopwatch.StartNew();

        // бит и темп
        readonly Queue<double> _bassHist = new();
        readonly List<double> _intervals = new();
        double _lastBeat = -10;

        /// <summary>Номер кадра анализа — растёт, пока идёт звук.</summary>
        public int Frame { get; private set; }
        /// <summary>Общая громкость 0…1 (сглаженная).</summary>
        public float Level { get; private set; }
        /// <summary>Сколько ударов найдено с начала — визуализаторы сравнивают с прошлым значением.</summary>
        public int Beats { get; private set; }
        /// <summary>Секунд с последнего удара.</summary>
        public double SinceBeat => Mono.Elapsed.TotalSeconds - _lastBeat;
        /// <summary>Примерный темп, ударов в минуту (0 — не определён).</summary>
        public double Bpm { get; private set; }
        public bool Running { get; private set; }
        /// <summary>Почему не работает (нет устройства вывода и т. п.) — для подсказки в интерфейсе.</summary>
        public string Error { get; private set; }
        public int SampleRate => _rate;

        AudioCapture()
        {
            for (int i = 0; i < N; i++) _win[i] = (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (N - 1)));
        }

        /// <summary>Попросить захват звука. Пока жив хоть один «арендатор», анализ идёт; Dispose — отпустить.</summary>
        public IDisposable Use()
        {
            if (Interlocked.Increment(ref _users) == 1)
            {
                if (_thread == null)
                {
                    lock (_gate)
                    {
                        if (_thread == null)
                        {
                            _thread = new Thread(Loop) { IsBackground = true, Name = "audio-capture", Priority = ThreadPriority.AboveNormal };
                            _thread.Start();
                            AudioService.I.OutputChanged += _ => _restart = true;   // наушники ↔ колонки: слушаем новое устройство
                        }
                    }
                }
                _wake.Set();
            }
            return new Lease(this);
        }

        sealed class Lease : IDisposable
        {
            AudioCapture _owner;
            public Lease(AudioCapture o) => _owner = o;
            public void Dispose()
            {
                var o = Interlocked.Exchange(ref _owner, null);
                if (o != null) Interlocked.Decrement(ref o._users);
            }
        }

        /// <summary>Снимок для отрисовки. Любой массив может быть null.</summary>
        public int Read(float[] bands, float[] peaks, float[] wave)
        {
            lock (_gate)
            {
                if (bands != null) Array.Copy(_bands, bands, Math.Min(bands.Length, BandCount));
                if (peaks != null) Array.Copy(_peaks, peaks, Math.Min(peaks.Length, BandCount));
                if (wave != null) Array.Copy(_wave, wave, Math.Min(wave.Length, WaveCount));
                return Frame;
            }
        }

        /// <summary>Средний уровень полос в диапазоне частот (для 4–5 полосок в чёлке).</summary>
        public float Range(double fromHz, double toHz)
        {
            lock (_gate)
            {
                double sum = 0; int n = 0;
                for (int b = 0; b < BandCount; b++)
                {
                    double f = BandHz(b);
                    if (f < fromHz || f >= toHz) continue;
                    sum += _bands[b]; n++;
                }
                return n == 0 ? 0 : (float)(sum / n);
            }
        }

        /// <summary>Центральная частота полосы (логарифмическая шкала 32 Гц … 16 кГц).</summary>
        public static double BandHz(int b) => MinHz * Math.Pow(MaxHz / MinHz, (b + 0.5) / BandCount);

        // ------------------------------------------------------------------
        //  Поток захвата
        // ------------------------------------------------------------------

        void Loop()
        {
            int fails = 0;
            while (true)
            {
                if (Volatile.Read(ref _users) <= 0) { Running = false; Decay(true); _wake.WaitOne(); continue; }
                try
                {
                    _restart = false;
                    Session();
                    fails = 0;
                }
                catch (Exception e)
                {
                    Running = false;
                    Error = e is COMException c && (uint)c.HResult == 0x88890004 ? "Устройство вывода отключили — жду новое" : "Не удалось слушать звук: " + e.Message;
                    if (fails++ == 0) Log.Info("audio capture: " + e.Message);
                    Thread.Sleep(Math.Min(5000, 400 * fails));
                }
            }
        }

        static void Check(int hr) { if (hr < 0) throw new COMException("wasapi", hr); }

        void Session()
        {
            IMMDeviceEnumerator en = null; IMMDevice dev = null; IAudioClient ac = null; IAudioCaptureClient cc = null;
            IntPtr fmt = IntPtr.Zero;
            try
            {
                en = (IMMDeviceEnumerator)AudioService.NewEnumerator();
                if (en.GetDefaultAudioEndpoint(0 /*render*/, 1 /*multimedia*/, out dev) != 0 || dev == null)
                {
                    Error = "Нет устройства вывода звука";
                    Thread.Sleep(1500);
                    return;
                }
                var iid = typeof(IAudioClient).GUID;
                Check(dev.Activate(ref iid, 23, IntPtr.Zero, out var o));
                ac = (IAudioClient)o;
                Check(ac.GetMixFormat(out fmt));
                int tag = (ushort)Marshal.ReadInt16(fmt, 0);
                int ch = Math.Max(1, (int)Marshal.ReadInt16(fmt, 2));
                int rate = Marshal.ReadInt32(fmt, 4);
                int block = Marshal.ReadInt16(fmt, 12);
                int bits = Marshal.ReadInt16(fmt, 14);
                int sub = tag == 0xFFFE ? Marshal.ReadInt32(fmt, 24) : tag;   // WAVEFORMATEXTENSIBLE: подформат
                bool isFloat = sub == 3;
                Check(ac.Initialize(0 /*shared*/, 0x00020000 /*loopback*/, 1_000_000 /*100 мс*/, 0, fmt, IntPtr.Zero));
                var ciid = typeof(IAudioCaptureClient).GUID;
                Check(ac.GetService(ref ciid, out var co));
                cc = (IAudioCaptureClient)co;
                if (rate != _rate) SetupBands(rate);
                Check(ac.Start());
                Running = true;
                Error = null;
                double lastData = Mono.Elapsed.TotalSeconds, lastAnalyze = 0;

                while (Volatile.Read(ref _users) > 0 && !_restart)
                {
                    Thread.Sleep(8);
                    bool got = false;
                    while (true)
                    {
                        Check(cc.GetNextPacketSize(out uint packet));
                        if (packet == 0) break;
                        Check(cc.GetBuffer(out var data, out uint frames, out uint flags, out _, out _));
                        if ((flags & 2) != 0 || data == IntPtr.Zero) PushSilence((int)frames);
                        else Push(data, (int)frames, ch, block, bits, isFloat);
                        cc.ReleaseBuffer(frames);
                        got = true;
                    }
                    double now = Mono.Elapsed.TotalSeconds;
                    if (got) lastData = now;
                    else if (now - lastData > 0.06)
                    {
                        // в тишине Windows не присылает пакеты вовсе — подаём нули, чтобы полоски плавно опали
                        PushSilence((int)(rate * (now - lastData)));
                        lastData = now;
                    }
                    // Sleep(8) у Windows длится ~15,6 мс — анализируем на каждом шаге (~60 кадров в секунду)
                    if (now - lastAnalyze >= 0.012) { lastAnalyze = now; Analyze(now); }
                }
                try { ac.Stop(); } catch { }
            }
            finally
            {
                Running = false;
                if (fmt != IntPtr.Zero) Marshal.FreeCoTaskMem(fmt);
                if (cc != null) Marshal.ReleaseComObject(cc);
                if (ac != null) Marshal.ReleaseComObject(ac);
                if (dev != null) Marshal.ReleaseComObject(dev);
                if (en != null) Marshal.ReleaseComObject(en);
            }
        }

        void SetupBands(int rate)
        {
            _rate = rate;
            double top = Math.Min(MaxHz, rate / 2.0 * 0.95);
            var lo = new int[BandCount];
            var hi = new int[BandCount];
            for (int b = 0; b < BandCount; b++)
            {
                double f0 = MinHz * Math.Pow(top / MinHz, (double)b / BandCount);
                double f1 = MinHz * Math.Pow(top / MinHz, (double)(b + 1) / BandCount);
                int k0 = Math.Max(1, (int)Math.Floor(f0 * N / rate));
                int k1 = Math.Max(k0 + 1, (int)Math.Ceiling(f1 * N / rate));
                lo[b] = k0;
                hi[b] = Math.Min(N / 2, k1);
                // высокие частоты в музыке тише — поднимаем их на 3 дБ на октаву, иначе правая половина всегда пустая
                _tilt[b] = 3.0 * Math.Log2(Math.Max(1, Math.Sqrt(f0 * f1) / 200.0));
            }
            _binLo = lo;
            _binHi = hi;
        }

        unsafe void Push(IntPtr data, int frames, int ch, int block, int bits, bool isFloat)
        {
            byte* p = (byte*)data;
            for (int f = 0; f < frames; f++)
            {
                byte* s = p + f * block;
                float sum = 0;
                for (int c = 0; c < ch && c < 2; c++)
                {
                    if (isFloat && bits == 32) sum += ((float*)s)[c];
                    else if (bits == 16) sum += ((short*)s)[c] / 32768f;
                    else if (bits == 24) { byte* q = s + c * 3; sum += ((q[0] << 8) | (q[1] << 16) | (q[2] << 24)) / 2147483648f; }
                    else if (bits == 32) sum += ((int*)s)[c] / 2147483648f;
                }
                PushOne(sum / Math.Min(ch, 2));
            }
        }

        void PushSilence(int frames)
        {
            frames = Math.Min(frames, N);
            for (int i = 0; i < frames; i++) PushOne(0);
        }

        void PushOne(float v)
        {
            _ring[_ringPos] = v;
            _ring[_ringPos + N] = v;   // двойной буфер: последние N отсчётов всегда лежат подряд
            _ringPos = (_ringPos + 1) % N;
        }

        // ------------------------------------------------------------------
        //  Анализ: БПФ → полосы, волна, громкость, удары
        // ------------------------------------------------------------------

        void Analyze(double now)
        {
            int start = _ringPos;   // самый старый отсчёт окна
            double energy = 0;
            for (int i = 0; i < N; i++)
            {
                float v = _ring[start + i];
                if (i >= N / 2) energy += v * v;
                _re[i] = v * _win[i];
                _im[i] = 0;
            }
            Fft(_re, _im);
            double norm = 4.0 / N;   // 2/N на одну сторону спектра и ×2 за окно Ханна

            var lo = _binLo; var hi = _binHi;
            for (int b = 0; b < BandCount; b++)
            {
                double m = 0;
                for (int k = lo[b]; k < hi[b]; k++)
                {
                    double mag = Math.Sqrt(_re[k] * _re[k] + _im[k] * _im[k]) * norm;
                    if (mag > m) m = mag;
                }
                double db = 20 * Math.Log10(m + 1e-9) + _tilt[b];
                _raw[b] = (float)Math.Clamp((db + 68) / 58, 0, 1);
            }

            double rms = Math.Sqrt(energy / (N / 2));
            float level = (float)Math.Clamp((20 * Math.Log10(rms + 1e-9) + 50) / 44, 0, 1);

            // волна: последние 1024 отсчёта через один
            lock (_gate)
            {
                for (int i = 0; i < WaveCount; i++) _wave[i] = _ring[start + N - WaveCount * 2 + i * 2];
                for (int b = 0; b < BandCount; b++)
                {
                    float v = _raw[b], cur = _bands[b];
                    cur = v > cur ? cur + (v - cur) * 0.65f : cur - (cur - v) * 0.16f;   // быстро вверх, плавно вниз
                    _bands[b] = cur;
                    _peaks[b] = Math.Max(_peaks[b] - 0.008f, cur);
                }
                Level = Level + (level - Level) * (level > Level ? 0.5f : 0.12f);
                Frame++;
            }
            DetectBeat(now);
        }

        void DetectBeat(double now)
        {
            double bass = 0; int n = 0;
            for (int b = 0; b < BandCount && BandHz(b) < 160; b++) { bass += _raw[b]; n++; }
            bass = n > 0 ? bass / n : 0;
            double avg = _bassHist.Count > 0 ? _bassHist.Average() : bass;
            _bassHist.Enqueue(bass);
            if (_bassHist.Count > 45) _bassHist.Dequeue();
            if (_bassHist.Count < 20 || bass < 0.3 || bass < avg * 1.18 + 0.04 || now - _lastBeat < 0.27) return;
            double gap = now - _lastBeat;
            _lastBeat = now;
            Beats++;
            if (gap > 0.25 && gap < 2.0)
            {
                _intervals.Add(gap);
                if (_intervals.Count > 24) _intervals.RemoveAt(0);
                if (_intervals.Count >= 6)
                {
                    var sorted = _intervals.OrderBy(x => x).ToList();
                    double bpm = 60 / sorted[sorted.Count / 2];
                    while (bpm < 75) bpm *= 2;
                    while (bpm > 180) bpm /= 2;
                    Bpm = Math.Round(bpm);
                }
            }
        }

        /// <summary>Тишина: полоски плавно опускаются до нуля.</summary>
        void Decay(bool reset)
        {
            lock (_gate)
            {
                Array.Clear(_bands); Array.Clear(_peaks); Array.Clear(_wave);
                Level = 0;
                if (reset) { _bassHist.Clear(); _intervals.Clear(); Bpm = 0; }
            }
        }

        /// <summary>Классическое итерационное БПФ по основанию 2 (на месте).</summary>
        static void Fft(float[] re, float[] im)
        {
            int n = re.Length;
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
            }
            for (int len = 2; len <= n; len <<= 1)
            {
                double ang = -2 * Math.PI / len;
                float wr = (float)Math.Cos(ang), wi = (float)Math.Sin(ang);
                int half = len >> 1;
                for (int i = 0; i < n; i += len)
                {
                    float cr = 1, ci = 0;
                    for (int k = 0; k < half; k++)
                    {
                        int a = i + k, b = a + half;
                        float tr = re[b] * cr - im[b] * ci, ti = re[b] * ci + im[b] * cr;
                        re[b] = re[a] - tr; im[b] = im[a] - ti;
                        re[a] += tr; im[a] += ti;
                        float ncr = cr * wr - ci * wi;
                        ci = cr * wi + ci * wr;
                        cr = ncr;
                    }
                }
            }
        }
    }
}
