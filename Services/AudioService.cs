using System;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using NIsland.Core;

namespace NIsland.Services
{
    /// <summary>Громкость системы через Core Audio (без сторонних библиотек).</summary>
    public sealed class AudioService
    {
        public static AudioService I { get; } = new();

        static readonly Type EnumeratorType = Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"));

        /// <summary>
        /// Перечислитель устройств звука. Создаём через общий тип COM, а не через свой класс-обёртку: Windows отдаёт один и тот же объект
        /// в одном потоке, и если две службы объявят по своему классу, вторая получит чужую обёртку и упадёт на приведении типа.
        /// Приведение к интерфейсу работает всегда.
        /// </summary>
        internal static object NewEnumerator() => Activator.CreateInstance(EnumeratorType);

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
            int OpenPropertyStore(int access, out IPropertyStore store);
            int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        }

        [StructLayout(LayoutKind.Sequential)]
        struct PROPERTYKEY { public Guid fmtid; public int pid; }

        [StructLayout(LayoutKind.Explicit, Size = 24)]
        struct PROPVARIANT { [FieldOffset(0)] public ushort vt; [FieldOffset(8)] public IntPtr p; }

        [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IPropertyStore
        {
            int GetCount(out uint n);
            int GetAt(uint i, out PROPERTYKEY key);
            int GetValue(ref PROPERTYKEY key, out PROPVARIANT value);
        }

        [DllImport("ole32.dll")] static extern int PropVariantClear(ref PROPVARIANT v);

        string _outId;
        public string OutputName { get; private set; }
        /// <summary>Идентификатор устройства вывода по умолчанию ({0.0.0.00000000}.{guid}).</summary>
        public string OutputId => _outId;
        /// <summary>Сменилось устройство вывода звука (наушники, колонки, Bluetooth).</summary>
        public event Action<string> OutputChanged;

        static string FriendlyName(IMMDevice dev) => StringProp(dev, 14);

        /// <summary>Устройство вывода — Bluetooth (наушники, колонка): звук слышен на 150–250 мс позже, текст песни это учитывает.</summary>
        public bool OutputBluetooth { get; private set; }

        /// <summary>Строковое свойство устройства из группы {a45c254e…}: 14 — имя, 24 — шина (BTHENUM, HDAUDIO, USB).</summary>
        static string StringProp(IMMDevice dev, int pid)
        {
            try
            {
                if (dev.OpenPropertyStore(0, out var store) != 0 || store == null) return null;
                try
                {
                    var key = new PROPERTYKEY { fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), pid = pid };
                    if (store.GetValue(ref key, out var v) != 0) return null;
                    string name = v.vt == 31 ? Marshal.PtrToStringUni(v.p) : null;
                    PropVariantClear(ref v);
                    return name;
                }
                finally { Marshal.ReleaseComObject(store); }
            }
            catch { return null; }
        }

        [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDeviceCollection
        {
            int GetCount(out uint n);
            int Item(uint i, out IMMDevice d);
        }

        [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioSessionManager2
        {
            int GetAudioSessionControl(IntPtr guid, int flags, out IntPtr ctl);
            int GetSimpleAudioVolume(IntPtr guid, int flags, out IntPtr vol);
            int GetSessionEnumerator(out IAudioSessionEnumerator e);
        }

        [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioSessionEnumerator
        {
            int GetCount(out int n);
            int GetSession(int i, out IAudioSessionControl2 s);
        }

        [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioSessionControl2
        {
            int GetState(out int state);
            int GetDisplayName(out IntPtr name);
            int SetDisplayName(IntPtr n, IntPtr ctx);
            int GetIconPath(out IntPtr p);
            int SetIconPath(IntPtr p, IntPtr ctx);
            int GetGroupingParam(out Guid g);
            int SetGroupingParam(IntPtr g, IntPtr ctx);
            int RegisterAudioSessionNotification(IntPtr n);
            int UnregisterAudioSessionNotification(IntPtr n);
            int GetSessionIdentifier(out IntPtr id);
            int GetSessionInstanceIdentifier(out IntPtr id);
            int GetProcessId(out uint pid);
        }

        [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface ISimpleAudioVolume
        {
            int SetMasterVolume(float level, ref Guid ctx);
            int GetMasterVolume(out float level);
            int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
            int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        }

        [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
        class PolicyConfigClient { }

        /// <summary>Недокументированный, но стабильный с Windows 7 интерфейс — им пользуются все переключатели звука.</summary>
        [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IPolicyConfig
        {
            int GetMixFormat(IntPtr a, IntPtr b);
            int GetDeviceFormat(IntPtr a, int b, IntPtr c);
            int ResetDeviceFormat(IntPtr a);
            int SetDeviceFormat(IntPtr a, IntPtr b, IntPtr c);
            int GetProcessingPeriod(IntPtr a, int b, IntPtr c, IntPtr d);
            int SetProcessingPeriod(IntPtr a, IntPtr b);
            int GetShareMode(IntPtr a, IntPtr b);
            int SetShareMode(IntPtr a, IntPtr b);
            int GetPropertyValue(IntPtr a, int b, IntPtr c, IntPtr d);
            int SetPropertyValue(IntPtr a, int b, IntPtr c, IntPtr d);
            int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
            int SetEndpointVisibility(IntPtr a, int b);
        }

        public record AppVolume(uint Pid, string Name, float Volume, bool Muted);
        public record OutputDevice(string Id, string Name, bool IsDefault);

        /// <summary>Перебрать сеансы звука на устройстве вывода по умолчанию.</summary>
        void ForEachRenderSession(Action<uint, ISimpleAudioVolume> act)
        {
            IMMDeviceEnumerator en = null; IMMDevice dev = null; IAudioSessionManager2 mgr = null; IAudioSessionEnumerator list = null;
            try
            {
                en = (IMMDeviceEnumerator)AudioService.NewEnumerator();
                if (en.GetDefaultAudioEndpoint(0, 1, out dev) != 0 || dev == null) return;
                var iid = typeof(IAudioSessionManager2).GUID;
                if (dev.Activate(ref iid, 23, IntPtr.Zero, out var o) != 0) return;
                mgr = (IAudioSessionManager2)o;
                if (mgr.GetSessionEnumerator(out list) != 0 || list == null) return;
                list.GetCount(out var n);
                for (int i = 0; i < n; i++)
                {
                    if (list.GetSession(i, out var s) != 0 || s == null) continue;
                    try
                    {
                        s.GetState(out var state);
                        if (state == 2) continue; // сеанс завершён
                        s.GetProcessId(out var pid);
                        act(pid, (ISimpleAudioVolume)s);
                    }
                    catch { }
                    finally { Marshal.ReleaseComObject(s); }
                }
            }
            catch { }
            finally
            {
                if (list != null) Marshal.ReleaseComObject(list);
                if (mgr != null) Marshal.ReleaseComObject(mgr);
                if (dev != null) Marshal.ReleaseComObject(dev);
                if (en != null) Marshal.ReleaseComObject(en);
            }
        }

        /// <summary>Громкость каждого приложения, которое сейчас может играть звук.</summary>
        public System.Collections.Generic.List<AppVolume> Apps()
        {
            var map = new System.Collections.Generic.Dictionary<string, AppVolume>();
            ForEachRenderSession((pid, v) =>
            {
                v.GetMasterVolume(out var vol);
                v.GetMute(out var mute);
                string name;
                if (pid == 0) name = "Системные звуки";
                else
                {
                    try { using var p = System.Diagnostics.Process.GetProcessById((int)pid); name = Fmt.Cap(p.ProcessName); }
                    catch { return; }
                }
                if (!map.ContainsKey(name)) map[name] = new AppVolume(pid, name, vol, mute);
            });
            return System.Linq.Enumerable.ToList(System.Linq.Enumerable.OrderBy(map.Values, a => a.Pid == 0 ? 1 : 0));
        }

        public void SetAppVolume(string name, float vol, bool? mute = null)
        {
            var g = Guid.Empty;
            ForEachRenderSession((pid, v) =>
            {
                string n;
                if (pid == 0) n = "Системные звуки";
                else
                {
                    try { using var p = System.Diagnostics.Process.GetProcessById((int)pid); n = Fmt.Cap(p.ProcessName); }
                    catch { return; }
                }
                if (n != name) return;
                if (vol >= 0) v.SetMasterVolume(Math.Clamp(vol, 0, 1), ref g);
                if (mute is bool m) v.SetMute(m, ref g);
            });
        }

        public System.Collections.Generic.List<OutputDevice> Outputs()
        {
            var res = new System.Collections.Generic.List<OutputDevice>();
            IMMDeviceEnumerator en = null; IMMDeviceCollection col = null;
            try
            {
                en = (IMMDeviceEnumerator)AudioService.NewEnumerator();
                string def = null;
                if (en.GetDefaultAudioEndpoint(0, 1, out var d) == 0 && d != null) { d.GetId(out def); Marshal.ReleaseComObject(d); }
                if (en.EnumAudioEndpoints(0, 1, out var ptr) != 0 || ptr == IntPtr.Zero) return res;
                col = (IMMDeviceCollection)Marshal.GetObjectForIUnknown(ptr);
                Marshal.Release(ptr);
                col.GetCount(out var n);
                for (uint i = 0; i < n; i++)
                {
                    if (col.Item(i, out var dev) != 0 || dev == null) continue;
                    try
                    {
                        dev.GetId(out var id);
                        res.Add(new OutputDevice(id, FriendlyName(dev) ?? "Устройство", id == def));
                    }
                    finally { Marshal.ReleaseComObject(dev); }
                }
            }
            catch { }
            finally
            {
                if (col != null) Marshal.ReleaseComObject(col);
                if (en != null) Marshal.ReleaseComObject(en);
            }
            return res;
        }

        /// <summary>Сделать устройство выводом по умолчанию (для всех ролей: музыка, игры, звонки).</summary>
        public bool SetDefaultOutput(string id)
        {
            try
            {
                var pc = (IPolicyConfig)new PolicyConfigClient();
                for (int role = 0; role < 3; role++) pc.SetDefaultEndpoint(id, role);
                Marshal.ReleaseComObject(pc);
                _n = 19; // переподключимся к новому устройству на следующем опросе
                return true;
            }
            catch (Exception e) { Log.Info("set output: " + e.Message); return false; }
        }

        /// <summary>Какие процессы прямо сейчас пишут звук с любого микрофона (звонок в приложении или на сайте).</summary>
        public System.Collections.Generic.List<uint> ActiveCapturePids()
        {
            var pids = new System.Collections.Generic.List<uint>();
            IMMDeviceEnumerator en = null;
            IMMDeviceCollection col = null;
            try
            {
                en = (IMMDeviceEnumerator)AudioService.NewEnumerator();
                if (en.EnumAudioEndpoints(1 /*capture*/, 1 /*active*/, out var colPtr) != 0 || colPtr == IntPtr.Zero) return pids;
                col = (IMMDeviceCollection)Marshal.GetObjectForIUnknown(colPtr);
                Marshal.Release(colPtr);
                col.GetCount(out var n);
                var iid = typeof(IAudioSessionManager2).GUID;
                for (uint i = 0; i < n; i++)
                {
                    IMMDevice dev = null;
                    IAudioSessionManager2 mgr = null;
                    IAudioSessionEnumerator sessions = null;
                    try
                    {
                        if (col.Item(i, out dev) != 0 || dev == null) continue;
                        if (dev.Activate(ref iid, 23, IntPtr.Zero, out var o) != 0) continue;
                        mgr = (IAudioSessionManager2)o;
                        if (mgr.GetSessionEnumerator(out sessions) != 0 || sessions == null) continue;
                        sessions.GetCount(out var count);
                        for (int k = 0; k < count; k++)
                        {
                            if (sessions.GetSession(k, out var s) != 0 || s == null) continue;
                            try
                            {
                                s.GetState(out var state);
                                if (state == 1 /*active*/ && s.GetProcessId(out var pid) == 0 && pid != 0 && !pids.Contains(pid)) pids.Add(pid);
                            }
                            finally { Marshal.ReleaseComObject(s); }
                        }
                    }
                    catch { }
                    finally
                    {
                        if (sessions != null) Marshal.ReleaseComObject(sessions);
                        if (mgr != null) Marshal.ReleaseComObject(mgr);
                        if (dev != null) Marshal.ReleaseComObject(dev);
                    }
                }
            }
            catch { }
            finally
            {
                if (col != null) Marshal.ReleaseComObject(col);
                if (en != null) Marshal.ReleaseComObject(en);
            }
            return pids;
        }

        [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioEndpointVolume
        {
            int RegisterControlChangeNotify(IntPtr p);
            int UnregisterControlChangeNotify(IntPtr p);
            int GetChannelCount(out uint c);
            int SetMasterVolumeLevel(float db, ref Guid ctx);
            int SetMasterVolumeLevelScalar(float level, ref Guid ctx);
            int GetMasterVolumeLevel(out float db);
            int GetMasterVolumeLevelScalar(out float level);
            int SetChannelVolumeLevel(uint ch, float db, ref Guid ctx);
            int SetChannelVolumeLevelScalar(uint ch, float level, ref Guid ctx);
            int GetChannelVolumeLevel(uint ch, out float db);
            int GetChannelVolumeLevelScalar(uint ch, out float level);
            int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
            int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        }

        [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioMeterInformation
        {
            int GetPeakValue(out float peak);
        }

        IAudioEndpointVolume _vol;
        IAudioMeterInformation _meter;
        readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromMilliseconds(150) };
        double _silentSince = -1;

        /// <summary>Монотонные часы в секундах (общие для всех служб).</summary>
        public static double Clock => System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency;
        /// <summary>Пиковый уровень того, что сейчас звучит (0…1).</summary>
        public float Peak { get; private set; }
        /// <summary>Когда (по <see cref="Clock"/>) звук появился после тишины от 0,6 с — так ловим начало трека у приложений, которые не сообщают время.</summary>
        public double SoundStartedAt { get; private set; } = -1;
        /// <summary>Сейчас тишина (дольше 0,6 с).</summary>
        public bool Silent => _silentSince >= 0 && Clock - _silentSince >= 0.6;
        int _n;
        bool _init;
        DateTime _selfChange;

        public double Volume { get; private set; }
        public bool Muted { get; private set; }

        /// <summary>(громкость, mute, изменено_нами)</summary>
        public event Action<double, bool, bool> Changed;

        public void Start()
        {
            Acquire();
            _poll.Tick += (_, __) => Poll();
            _poll.Start();
        }

        void Acquire()
        {
            try
            {
                if (_vol != null) { Marshal.ReleaseComObject(_vol); _vol = null; }
                if (_meter != null) { Marshal.ReleaseComObject(_meter); _meter = null; }
                var en = (IMMDeviceEnumerator)AudioService.NewEnumerator();
                if (en.GetDefaultAudioEndpoint(0 /*render*/, 1 /*multimedia*/, out var dev) != 0 || dev == null) return;
                var iid = typeof(IAudioEndpointVolume).GUID;
                dev.Activate(ref iid, 23, IntPtr.Zero, out var o);
                _vol = (IAudioEndpointVolume)o;
                var miid = typeof(IAudioMeterInformation).GUID;
                if (dev.Activate(ref miid, 23, IntPtr.Zero, out var mo) == 0) _meter = mo as IAudioMeterInformation;
                if (dev.GetId(out var id) == 0 && id != _outId)
                {
                    bool first = _outId == null;
                    _outId = id;
                    OutputName = FriendlyName(dev) ?? "Динамики";
                    var bus = StringProp(dev, 24) ?? "";
                    OutputBluetooth = bus.StartsWith("BTH", StringComparison.OrdinalIgnoreCase);
                    if (!first) OutputChanged?.Invoke(OutputName);
                }
                Marshal.ReleaseComObject(dev);
                Marshal.ReleaseComObject(en);
            }
            catch (Exception e) { _vol = null; Log.Info("audio: " + e.Message); }
        }

        void Poll()
        {
            if (++_n % 20 == 0) Acquire(); // смена устройства вывода
            if (_meter != null)
            {
                try
                {
                    _meter.GetPeakValue(out var pk);
                    Peak = pk;
                    double t = Clock;
                    if (pk < 0.003) { if (_silentSince < 0) _silentSince = t; }
                    else
                    {
                        // звук после паузы: начало трека где-то между прошлым опросом и этим — берём середину
                        if (_silentSince >= 0 && t - _silentSince >= 0.6) SoundStartedAt = t - 0.075;
                        _silentSince = -1;
                    }
                }
                catch { _meter = null; }
            }
            if (_vol == null) return;
            try
            {
                _vol.GetMasterVolumeLevelScalar(out var v);
                _vol.GetMute(out var m);
                bool changed = Math.Abs(v - Volume) > 0.004 || m != Muted;
                Volume = v; Muted = m;
                if (changed && _init) Changed?.Invoke(Volume, Muted, (DateTime.Now - _selfChange).TotalMilliseconds < 600);
                _init = true;
            }
            catch { _vol = null; }
        }

        public void SetVolume(double v)
        {
            if (_vol == null) return;
            _selfChange = DateTime.Now;
            var g = Guid.Empty;
            try
            {
                _vol.SetMasterVolumeLevelScalar((float)Math.Clamp(v, 0, 1), ref g);
                if (Muted && v > 0) _vol.SetMute(false, ref g);
            }
            catch { }
        }

        public void ToggleMute()
        {
            if (_vol == null) return;
            var g = Guid.Empty;
            try { _vol.SetMute(!Muted, ref g); } catch { }
        }

        /// <summary>Микрофон по умолчанию (устройство связи). Берём на время вызова и сразу отпускаем.</summary>
        IAudioEndpointVolume Mic()
        {
            try
            {
                var en = (IMMDeviceEnumerator)AudioService.NewEnumerator();
                if (en.GetDefaultAudioEndpoint(1 /*capture*/, 2 /*communications*/, out var dev) != 0 || dev == null) return null;
                var iid = typeof(IAudioEndpointVolume).GUID;
                dev.Activate(ref iid, 23, IntPtr.Zero, out var o);
                Marshal.ReleaseComObject(dev);
                Marshal.ReleaseComObject(en);
                return (IAudioEndpointVolume)o;
            }
            catch { return null; }
        }

        public bool MicMuted
        {
            get
            {
                var m = Mic();
                if (m == null) return false;
                try { m.GetMute(out var v); return v; }
                catch { return false; }
                finally { Marshal.ReleaseComObject(m); }
            }
        }

        /// <summary>Громкость микрофона 0–1 (−1 — микрофона нет).</summary>
        public double MicLevel
        {
            get
            {
                var m = Mic();
                if (m == null) return -1;
                try { m.GetMasterVolumeLevelScalar(out var v); return v; }
                catch { return -1; }
                finally { Marshal.ReleaseComObject(m); }
            }
            set
            {
                var m = Mic();
                if (m == null) return;
                try { var g = Guid.Empty; m.SetMasterVolumeLevelScalar((float)Math.Clamp(value, 0, 1), ref g); }
                catch { }
                finally { Marshal.ReleaseComObject(m); }
            }
        }

        public void SetMicMute(bool mute)
        {
            var m = Mic();
            if (m == null) return;
            try { var g = Guid.Empty; m.SetMute(mute, ref g); }
            catch { }
            finally { Marshal.ReleaseComObject(m); }
        }

        public bool ToggleMicMute()
        {
            var m = Mic();
            if (m == null) return false;
            try
            {
                m.GetMute(out var v);
                var g = Guid.Empty;
                m.SetMute(!v, ref g);
                return !v;
            }
            catch { return false; }
            finally { Marshal.ReleaseComObject(m); }
        }
    }
}
