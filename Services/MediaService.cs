using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using NIsland.Core;
using Windows.Foundation;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace NIsland.Services
{
    /// <summary>
    /// Музыка/видео из ЛЮБОГО приложения, которое сообщает Windows о воспроизведении
    /// (Spotify, браузеры с YouTube/VK/Яндекс Музыкой, Apple Music, VLC, Telegram, медиаплеер…).
    /// Позиция считается по монотонным часам с учётом скорости; если приложение не сообщает время трека
    /// (Firefox, часть плееров), время считаем сами от начала трека, а длительность берём из каталога.
    /// </summary>
    public sealed class MediaService
    {
        public static MediaService I { get; } = new();

        GlobalSystemMediaTransportControlsSessionManager _mgr;
        GlobalSystemMediaTransportControlsSession _s;
        string _pinned;
        readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(200) };
        readonly DispatcherTimer _confirm = new() { Interval = TimeSpan.FromMilliseconds(900) };
        int _tickCount;
        bool _starting;

        TypedEventHandler<GlobalSystemMediaTransportControlsSession, MediaPropertiesChangedEventArgs> _onProps;
        TypedEventHandler<GlobalSystemMediaTransportControlsSession, PlaybackInfoChangedEventArgs> _onPlay;
        TypedEventHandler<GlobalSystemMediaTransportControlsSession, TimelinePropertiesChangedEventArgs> _onTimeline;

        public bool HasSession => _s != null;
        public string AppId { get; private set; }
        public string AppName => FriendlyName(AppId);
        public bool FromBrowser => IsBrowser(AppId);
        public string Title { get; private set; } = "";
        public string Artist { get; private set; } = "";
        public string Album { get; private set; } = "";
        public BitmapSource Art { get; private set; }
        public Color ArtColor { get; private set; } = Color.FromRgb(0x0A, 0x84, 0xFF);
        public bool IsPlaying { get; private set; }
        public bool CanNext { get; private set; } = true;
        public bool CanPrev { get; private set; } = true;
        public bool CanShuffle { get; private set; }
        public bool CanRepeat { get; private set; }
        public bool CanRate { get; private set; }
        public bool Shuffle { get; private set; }
        /// <summary>0 — нет, 1 — трек, 2 — список.</summary>
        public int Repeat { get; private set; }
        public double Rate { get; private set; } = 1;

        // ---------- время трека ----------
        static readonly Stopwatch Mono = Stopwatch.StartNew();
        static double Now => Mono.Elapsed.TotalSeconds;
        double _posSec, _posAt;               // позиция _posSec была в момент _posAt (монотонные секунды)
        TimeSpan _appDuration;                // длительность от самого приложения
        double _estDuration;                  // из каталога, если приложение молчит
        bool _timelineFresh, _firstProps;
        DateTimeOffset _trackAt, _timelineStamp;
        TimeSpan _timelinePos;

        /// <summary>Приложение не сообщает время трека — позицию считаем сами от начала трека.</summary>
        public bool TimeEstimated => _appDuration.TotalSeconds <= 1;
        public TimeSpan Duration => _appDuration.TotalSeconds > 1 ? _appDuration : TimeSpan.FromSeconds(_estDuration);
        /// <summary>Длительность известна (от приложения или из каталога) — можно рисовать прогресс.</summary>
        public bool HasTime => Duration.TotalSeconds > 1;
        /// <summary>Перематывать можно только если приложение само сообщает время.</summary>
        public bool CanSeek => _appDuration.TotalSeconds > 1;

        public TimeSpan Position
        {
            get
            {
                double p = _posSec;
                if (IsPlaying) p += (Now - _posAt) * EffRate;
                double d = Duration.TotalSeconds;
                if (p < 0) p = 0;
                if (d > 1 && p > d) p = d;
                return TimeSpan.FromSeconds(p);
            }
        }

        double EffRate => Rate > 0.05 && Rate < 8 ? Rate : 1;
        int _driftSeen;
        void SetPos(double sec, double age = 0) { _posSec = Math.Max(0, sec); _posAt = Now - Math.Max(0, age); }
        void Rebase() => SetPos(Position.TotalSeconds);

        // ---------- текст песни ----------
        public List<LyricLine> Lyrics { get; private set; }
        public bool LyricsSynced { get; private set; }
        public bool LyricsLoading { get; private set; }
        public bool LyricsInstrumental { get; private set; }
        public string LyricsSource { get; private set; }
        public int LyricIndex { get; private set; } = -1;
        /// <summary>Перевод строк текста (тот же порядок), если включён.</summary>
        public List<string> Translation { get; private set; }

        static Dictionary<string, double> _offsets => Store.Data.LyricOffsets;
        string TrackKey => Title + "|" + Artist;
        public string TrackKeyForUi => AppId + "|" + TrackKey;
        /// <summary>Сдвиг текста относительно музыки, с: общий из настроек + подстройка для этого трека (или выученная для приложения).</summary>
        public double LyricsOffset => Store.Settings.LyricsOffsetMs / 1000.0 + (HasTrackOffset ? TrackOffset : AppOffset);
        public double TrackOffset => _offsets.TryGetValue(TrackKey, out var v) ? v : 0;
        public bool HasTrackOffset => _offsets.ContainsKey(TrackKey);
        /// <summary>
        /// Выученный сдвиг для приложения: если в Chrome текст всегда спешит на 0,5 с — после пары подстроек
        /// новые песни из Chrome сразу идут с поправкой.
        /// </summary>
        public double AppOffset => Store.Settings.LyricsAppOffset && !string.IsNullOrEmpty(AppName) && Store.Data.AppLyricOffsets.TryGetValue(AppName, out var v) ? v : 0;
        public void ResetAppOffset() { Store.Data.AppLyricOffsets.Remove(AppName ?? ""); Store.SaveData(); UpdateLyricIndex(true); }
        /// <summary>Строка появляется чуть раньше, чем её начнут петь (глаз успевает прочитать).</summary>
        static TimeSpan Lead => TimeSpan.FromMilliseconds(Math.Clamp(Store.Settings.LyricsLeadMs, 0, 1000));

        public void AdjustOffset(double seconds)
        {
            double now = Math.Round((HasTrackOffset ? TrackOffset : AppOffset) + seconds, 2);
            _offsets[TrackKey] = now;
            if (_offsets.Count > 500) _offsets.Remove(_offsets.Keys.First());   // помним последние 500 песен
            if (Store.Settings.LyricsAppOffset && !string.IsNullOrEmpty(AppName) && LyricsSynced)
            {
                // плавное среднее: одна странная песня не ломает поправку для всего приложения
                var app = Store.Data.AppLyricOffsets;
                app[AppName] = Math.Round(app.TryGetValue(AppName, out var old) ? old * 0.6 + now * 0.4 : now * 0.5, 2);
            }
            Store.SaveData();
            UpdateLyricIndex(true);
        }

        public void ResetOffset() { _offsets.Remove(TrackKey); Store.SaveData(); UpdateLyricIndex(true); }
        /// <summary>Общий сдвиг поменяли в настройках — пересчитать текущую строчку.</summary>
        public void Resync() => UpdateLyricIndex(true);

        /// <summary>Позиция с учётом сдвига текста.</summary>
        /// <summary>
        /// Позиция для текста песни: что сейчас СЛЫШНО. Плеер сообщает позицию декодера, а звук доходит до ушей позже —
        /// у Bluetooth на 150–250 мс (кодек и буфер радиоканала). Без поправки строки загорались заметно раньше голоса.
        /// </summary>
        public TimeSpan LyricsPosition => Position + TimeSpan.FromSeconds(LyricsOffset - OutputLatency);

        /// <summary>Задержка устройства вывода, с: «авто» — 0,2 с для Bluetooth, иначе 0; или своё значение из настроек.</summary>
        public static double OutputLatency => Store.Settings.LyricsLatencyMs >= 0
            ? Store.Settings.LyricsLatencyMs / 1000.0
            : AudioService.I.OutputBluetooth ? 0.2 : 0;

        /// <summary>Сколько пройдено текущей строки (для «караоке»-заливки) и сколько осталось до следующей.</summary>
        public (double Progress, TimeSpan Left) LineProgress()
        {
            if (Lyrics == null || !LyricsSynced || LyricIndex < 0 || LyricIndex >= Lyrics.Count) return (0, TimeSpan.Zero);
            var start = Lyrics[LyricIndex].Time;
            var end = LineEnd(LyricIndex);
            var pos = LyricsPosition + Lead;
            double total = Math.Max(0.1, (end - start).TotalSeconds);
            return (Math.Clamp((pos - start).TotalSeconds / total, 0, 1), end - pos);
        }

        /// <summary>Когда строка заканчивается: по последнему слову, иначе — начало следующей (не дольше 8 с на строку).</summary>
        public TimeSpan LineEnd(int i)
        {
            var l = Lyrics[i];
            var next = i + 1 < Lyrics.Count ? Lyrics[i + 1].Time : l.Time + TimeSpan.FromSeconds(6);
            if (l.Words is { Count: > 0 } w && w[^1].End > l.Time && w[^1].End < next) return w[^1].End;
            return next - l.Time > TimeSpan.FromSeconds(8) && l.Text != "♪" ? l.Time + TimeSpan.FromSeconds(8) : next;
        }

        public event Action OffsetChanged;
        public string CurrentLyric => LyricsSynced && Lyrics != null && LyricIndex >= 0 && LyricIndex < Lyrics.Count && Lyrics[LyricIndex].Text != "♪" ? Lyrics[LyricIndex].Text : null;

        public event Action Changed;          // название/обложка/источник
        public event Action PlaybackChanged;  // play/pause
        public event Action TrackChanged;     // новый трек (для всплывашки)
        public event Action Tick;             // позиция
        public event Action LyricsChanged;
        public event Action LyricIndexChanged;

        static Dispatcher D => Application.Current.Dispatcher;

        // ------------------------------------------------------------------
        //  Подключение к Windows
        // ------------------------------------------------------------------

        public void Start()
        {
            _onProps = (s, e) => D.BeginInvoke(new Action(() => { if (Same(s)) _ = LoadProps(); }));
            _onPlay = (s, e) => D.BeginInvoke(new Action(() => { if (Same(s)) LoadPlayback(); }));
            _onTimeline = (s, e) => D.BeginInvoke(new Action(() => { if (Same(s)) LoadTimeline(); }));
            _tick.Tick += (_, __) => OnTick();
            _tick.Start();
            _lineT.Tick += (_, __) => { _lineT.Stop(); UpdateLyricIndex(); };
            // наушники ↔ колонки: у Bluetooth своя задержка звука — строки пересчитываем сразу
            AudioService.I.OutputChanged += _ => System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() => UpdateLyricIndex(true)));
            _confirm.Tick += (_, __) => { _confirm.Stop(); LoadPlayback(); LoadTimeline(); };
            // после сна Windows иногда «теряет» подписку на медиасессии — переподключаемся
            SystemEvents.PowerModeChanged += (_, e) =>
            {
                if (e.Mode == PowerModes.Resume) D.BeginInvoke(new Action(async () => { await Task.Delay(2500); await Connect(); }));
            };
            _ = Connect();
        }

        async Task Connect()
        {
            if (_starting) return;
            _starting = true;
            try
            {
                var mgr = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                if (_mgr != null) { try { _mgr.CurrentSessionChanged -= OnMgrChanged; _mgr.SessionsChanged -= OnMgrChanged; } catch { } }
                _mgr = mgr;
                _mgr.CurrentSessionChanged += OnMgrChanged;
                _mgr.SessionsChanged += OnMgrChanged;
                _failures = 0;
                PickSession(true);
            }
            catch (Exception e) { Log.Info("media: " + e.Message); }
            finally { _starting = false; }
        }

        void OnMgrChanged(GlobalSystemMediaTransportControlsSessionManager m, object e) => D.BeginInvoke(new Action(() => PickSession()));

        bool Same(GlobalSystemMediaTransportControlsSession s) =>
            _s != null && s != null && (ReferenceEquals(s, _s) || s.SourceAppUserModelId == _s.SourceAppUserModelId);

        int _failures;

        public List<GlobalSystemMediaTransportControlsSession> Sessions()
        {
            try { var list = _mgr?.GetSessions().ToList() ?? new(); _failures = 0; return list; }
            catch
            {
                if (++_failures == 5) _ = Connect();   // менеджер «умер» (перезапуск explorer, сон) — создаём заново
                return new();
            }
        }

        static bool Playing(GlobalSystemMediaTransportControlsSession s)
        {
            try { return s.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing; }
            catch { return false; }
        }

        void PickSession(bool force = false)
        {
            if (_mgr == null) return;
            var sessions = Sessions();
            GlobalSystemMediaTransportControlsSession s = null;
            if (_pinned != null) s = sessions.FirstOrDefault(x => x.SourceAppUserModelId == _pinned);
            if (s == null)
            {
                _pinned = null;
                try { s = _mgr.GetCurrentSession(); } catch { }
                if (s == null || !Playing(s))
                {
                    var playing = sessions.FirstOrDefault(Playing);
                    if (playing != null) s = playing;
                }
                // не прыгаем с текущего источника, если он ещё жив и ничто другое не играет
                if (_s != null && (s == null || !Playing(s)))
                {
                    var alive = sessions.FirstOrDefault(x => ReferenceEquals(x, _s)) ?? sessions.FirstOrDefault(x => x.SourceAppUserModelId == AppId);
                    if (alive != null) s = alive;
                }
                s ??= sessions.FirstOrDefault();
            }
            Attach(s, force);
        }

        void Subscribe(GlobalSystemMediaTransportControlsSession s, bool on)
        {
            if (s == null) return;
            try
            {
                if (on)
                {
                    s.MediaPropertiesChanged += _onProps;
                    s.PlaybackInfoChanged += _onPlay;
                    s.TimelinePropertiesChanged += _onTimeline;
                }
                else
                {
                    s.MediaPropertiesChanged -= _onProps;
                    s.PlaybackInfoChanged -= _onPlay;
                    s.TimelinePropertiesChanged -= _onTimeline;
                }
            }
            catch { }
        }

        void Attach(GlobalSystemMediaTransportControlsSession s, bool force = false)
        {
            if (ReferenceEquals(s, _s) && !force) return;
            bool sameApp = s != null && _s != null && s.SourceAppUserModelId == _s.SourceAppUserModelId;
            Subscribe(_s, false);
            _s = s;
            if (s != null && sameApp)
            {
                // та же программа (например, другая вкладка браузера): состояние не сбрасываем — LoadProps сам заметит смену трека
                Subscribe(s, true);
                LoadPlayback();
                LoadTimeline();
                _ = LoadProps();
                return;
            }
            AppId = s?.SourceAppUserModelId;
            _appDuration = TimeSpan.Zero; _estDuration = 0; _timelineFresh = false; _timelineStamp = default; SetPos(0);
            if (s == null)
            {
                Title = Artist = Album = "";
                Art = null; IsPlaying = false;
                Lyrics = null; LyricIndex = -1; LyricsLoading = false;
                Changed?.Invoke(); PlaybackChanged?.Invoke(); LyricsChanged?.Invoke();
                return;
            }
            Subscribe(s, true);
            Title = ""; Artist = "";
            _firstProps = true;
            LoadPlayback();
            LoadTimeline();
            _ = LoadProps();
        }

        // ------------------------------------------------------------------
        //  Название, обложка
        // ------------------------------------------------------------------

        async Task LoadProps()
        {
            var s = _s;
            if (s == null) return;
            GlobalSystemMediaTransportControlsSessionMediaProperties p;
            try { p = await s.TryGetMediaPropertiesAsync(); }
            catch { return; }
            if (s != _s || p == null) return;

            string title = (p.Title ?? "").Trim(), artist = (p.Artist ?? "").Trim(), album = (p.AlbumTitle ?? "").Trim();
            if (artist == "" && !string.IsNullOrEmpty(p.AlbumArtist)) artist = p.AlbumArtist.Trim();
            bool newTrack = title != Title || artist != Artist;
            bool realChange = newTrack && !_firstProps;   // первый показ сессии — не «смена трека»: время уже идёт
            _firstProps = false;
            Title = title; Artist = artist; Album = album;

            if (realChange)
            {
                // новый трек: пока приложение не прислало свежее время, считаем от нуля и длительность не знаем
                _trackAt = DateTimeOffset.Now;
                _trackAtClock = AudioService.Clock;
                _timelineFresh = false;
                _estDuration = 0;
                LoadTimeline(trackChange: true);
                _ = AlignByAudio(TrackKey);
                // таймер сна «после этого трека»: начался следующий — ставим паузу
                if (TimerService.I.SleepAfterTrack)
                {
                    TimerService.I.SetSleepAfterTrack(false);
                    Pause();
                    App.Island?.Toast("", "Таймер сна", "Трек закончился — музыка на паузе");
                }
            }

            BitmapSource art = null;
            if (p.Thumbnail != null) art = await LoadThumb(p.Thumbnail);
            if (s != _s) return;
            if (art != null || newTrack)
            {
                Art = art;
                ArtColor = art != null ? ColorUtil.Dominant(art) : Color.FromRgb(0x0A, 0x84, 0xFF);
            }
            Changed?.Invoke();
            if (newTrack && title != "")
            {
                if (realChange) { LoopA = LoopB = null; TrackChanged?.Invoke(); }
                AddHistory();
                _ = LoadLyrics();
            }
        }

        static async Task<BitmapSource> LoadThumb(IRandomAccessStreamReference r)
        {
            try
            {
                using var ras = await r.OpenReadAsync();
                using var s = ras.AsStreamForRead();
                var ms = new MemoryStream();
                await s.CopyToAsync(ms);
                if (ms.Length == 0) return null;
                ms.Position = 0;
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 300;
                bmp.StreamSource = ms;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch { return null; }
        }

        // ------------------------------------------------------------------
        //  Состояние и время
        // ------------------------------------------------------------------

        void LoadPlayback()
        {
            try
            {
                var pi = _s?.GetPlaybackInfo();
                if (pi == null) return;
                bool playing = pi.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                CanNext = pi.Controls?.IsNextEnabled ?? true;
                CanPrev = pi.Controls?.IsPreviousEnabled ?? true;
                CanShuffle = pi.Controls?.IsShuffleEnabled ?? false;
                CanRepeat = pi.Controls?.IsRepeatEnabled ?? false;
                CanRate = pi.Controls?.IsPlaybackRateEnabled ?? false;
                Shuffle = pi.IsShuffleActive ?? false;
                Repeat = pi.AutoRepeatMode switch
                {
                    Windows.Media.MediaPlaybackAutoRepeatMode.Track => 1,
                    Windows.Media.MediaPlaybackAutoRepeatMode.List => 2,
                    _ => 0,
                };
                double rate = pi.PlaybackRate ?? 1;
                if (Math.Abs(rate - Rate) > 0.001) { Rebase(); Rate = rate; }
                if (playing != IsPlaying)
                {
                    Rebase();
                    IsPlaying = playing;
                }
                PlaybackChanged?.Invoke();
            }
            catch { }
        }

        /// <param name="periodic">Плановая сверка: берём только реально обновлённые данные,
        /// иначе у приложений с «застывшей» меткой времени позиция откатывалась бы назад.</param>
        /// <param name="trackChange">Только что сменился трек: время от прошлого трека не берём.</param>
        void LoadTimeline(bool periodic = false, bool trackChange = false)
        {
            GlobalSystemMediaTransportControlsSessionTimelineProperties t;
            try { t = _s?.GetTimelineProperties(); }
            catch { return; }
            if (t == null) return;
            var end = t.EndTime > t.MaxSeekTime ? t.EndTime : t.MaxSeekTime;
            var dur = end - t.StartTime;
            var pos = t.Position - t.StartTime;
            bool validStamp = t.LastUpdatedTime.Year >= 2000 && t.LastUpdatedTime <= DateTimeOffset.Now.AddSeconds(2);
            bool same = t.LastUpdatedTime == _timelineStamp && pos == _timelinePos;
            if (periodic && (!validStamp || same)) return;

            if (trackChange)
            {
                // время, обновлённое задолго до смены трека, относится к прошлому треку
                bool stale = validStamp ? t.LastUpdatedTime < _trackAt.AddSeconds(-5) : pos.TotalSeconds > 5;
                if (stale || same)
                {
                    _appDuration = TimeSpan.Zero;
                    SetPos(0);
                    Tick?.Invoke();
                    UpdateLyricIndex(true);
                    return;
                }
            }

            _timelineStamp = t.LastUpdatedTime;
            _timelinePos = pos;
            _timelineFresh = true;
            bool hasDur = dur.TotalSeconds > 1 && dur.TotalHours < 24;
            _appDuration = hasDur ? dur : TimeSpan.Zero;
            // приложение без длительности, но с позицией (эфир, подкаст) — позицию всё равно берём; без позиции — считаем сами
            if (!hasDur && pos.TotalSeconds < 0.5) { Tick?.Invoke(); return; }

            double age = validStamp && IsPlaying ? (DateTimeOffset.Now - t.LastUpdatedTime).TotalSeconds : 0;
            if (age < 0) age = 0;
            if (age > 6 * 3600) age = 0;   // метке «сутки назад» не верим
            if (periodic && IsPlaying)
            {
                // плановая сверка: мелкую разницу часов (до 0,08 с) не трогаем; заметную — исправляем, но только если она
                // подтвердилась двумя сверками подряд (одна свежая метка от браузера бывает неточной); большую (> 0,6 с) — сразу
                double fresh = pos.TotalSeconds + age * EffRate;
                double diff = Math.Abs(fresh - Position.TotalSeconds);
                if (diff < 0.08) { _driftSeen = 0; return; }
                if (diff < 0.6 && ++_driftSeen < 2) return;
                _driftSeen = 0;
            }
            SetPos(pos.TotalSeconds, age * EffRate);
            Tick?.Invoke();
            UpdateLyricIndex(true);
        }

        void OnTick()
        {
            if (IsPlaying) Tick?.Invoke();
            CheckLoop();
            UpdateLyricIndex();
            _tickCount++;
            // раз в секунду сверяем позицию с приложением — текст не «уплывает» от музыки
            if (IsPlaying && _tickCount % 5 == 0) LoadTimeline(true);
            if (_tickCount % 10 == 0 && _mgr != null)
            {
                var sessions = Sessions();
                // наша сессия исчезла (закрыли вкладку, а событие не пришло) — выбираем заново
                if (_s != null && !sessions.Any(x => ReferenceEquals(x, _s)) && !sessions.Any(x => x.SourceAppUserModelId == AppId)) PickSession();
                else if (_pinned == null && !IsPlaying)
                {
                    // кто-то другой начал играть — переключаемся на него
                    var other = sessions.FirstOrDefault(Playing);
                    if (other != null && other.SourceAppUserModelId != AppId) Attach(other);
                }
            }
            if (_mgr == null && _tickCount % 150 == 0) _ = Connect();   // не удалось подключиться при старте — пробуем раз в 30 с
        }

        // ------------------------------------------------------------------
        //  Текст песни
        // ------------------------------------------------------------------

        async Task LoadLyrics()
        {
            string key = TrackKey;
            Lyrics = null; LyricIndex = -1; LyricsLoading = true; LyricsSynced = false; LyricsInstrumental = false; Translation = null;
            LyricsChanged?.Invoke(); LyricIndexChanged?.Invoke();
            // дождёмся длительности трека — она приходит отдельным событием
            for (int i = 0; i < 8 && !_timelineFresh; i++) await Task.Delay(250);
            if (key != TrackKey) return;
            var res = await LyricsService.Get(Title, Artist, Album, _appDuration, FromBrowser);
            if (key != TrackKey) return;
            Lyrics = res?.Lines is { Count: > 0 } ? res.Lines : null;
            LyricsSynced = Lyrics != null && res.Synced;
            LyricsInstrumental = res?.Instrumental == true;
            LyricsSource = res?.Source;
            LyricsLoading = false;
            LyricsChanged?.Invoke();
            UpdateLyricIndex(true);
            _ = LoadTranslation();
            if (TimeEstimated) await EstimateDuration(res, key);
        }

        /// <summary>Приложение не сообщило длительность — берём из найденного текста или каталога iTunes.</summary>
        async Task EstimateDuration(LyricsResult res, string key)
        {
            double d = res?.Duration ?? 0;
            if (d <= 1) d = await LyricsService.LookupDuration(Title, Artist, FromBrowser);
            if (key != TrackKey || !TimeEstimated || d <= 1) return;
            _estDuration = d;
            Tick?.Invoke();
            Changed?.Invoke();
        }

        void UpdateLyricIndex(bool resync = false)
        {
            int idx = -1;
            var lines = Lyrics;
            if (lines != null && LyricsSynced && lines.Count > 0)
            {
                var pos = LyricsPosition + Lead;
                int lo = 0, hi = lines.Count - 1;
                while (lo <= hi)
                {
                    int mid = (lo + hi) / 2;
                    if (lines[mid].Time <= pos) { idx = mid; lo = mid + 1; } else hi = mid - 1;
                }
            }
            if (idx != LyricIndex)
            {
                LyricIndex = idx;
                LyricIndexChanged?.Invoke();
            }
            else if (resync) OffsetChanged?.Invoke();
            ScheduleLine();
        }

        /// <summary>
        /// Точный момент следующей строки: таймер ставится ровно на её начало, а не ждёт плановой проверки раз в 0,2 с —
        /// строка загорается вовремя, без запаздывания до 200 мс.
        /// </summary>
        readonly DispatcherTimer _lineT = new(DispatcherPriority.Render);

        void ScheduleLine()
        {
            _lineT.Stop();
            var lines = Lyrics;
            if (lines == null || !LyricsSynced || !IsPlaying) return;
            int next = LyricIndex + 1;
            if (next >= lines.Count) return;
            double wait = (lines[next].Time - (LyricsPosition + Lead)).TotalSeconds / EffRate;
            if (wait < 0.005 || wait > 1.5) return;   // далёкие строки поймает обычная проверка
            _lineT.Interval = TimeSpan.FromSeconds(wait + 0.004);
            _lineT.Start();
        }

        double _trackAtClock;

        /// <summary>
        /// Приложение не сообщает время (Firefox, часть плееров): ловим момент, когда после тишины пошёл звук, —
        /// это и есть начало трека. Иначе отсчёт шёл от прихода названия, а оно у браузеров опаздывает на 0,5–2 с.
        /// </summary>
        async Task AlignByAudio(string key)
        {
            if (!Store.Settings.LyricsAutoAlign) return;
            await Task.Delay(1500);
            if (key != TrackKey || !TimeEstimated || !IsPlaying) return;
            double st = AudioService.I.SoundStartedAt;
            if (st < 0 || st < _trackAtClock - 4 || st > _trackAtClock + 1.4) return;
            double pos = (AudioService.Clock - st) * EffRate;
            if (Math.Abs(pos - Position.TotalSeconds) < 0.15) return;
            SetPos(pos);
            Tick?.Invoke();
            UpdateLyricIndex(true);
        }

        /// <summary>Поставить выбранный вручную текст (и запомнить его для трека).</summary>
        public void SetLyrics(LyricsResult r)
        {
            Lyrics = r?.Lines is { Count: > 0 } ? r.Lines : null;
            LyricsSynced = Lyrics != null && r.Synced;
            LyricsInstrumental = r?.Instrumental == true;
            LyricsSource = r?.Source;
            LyricsLoading = false;
            LyricIndex = -1;
            if (r != null) LyricsService.Remember(Title, Artist, r, FromBrowser);
            if (TimeEstimated && r?.Duration > 1) _estDuration = r.Duration;
            LyricsChanged?.Invoke();
            UpdateLyricIndex(true);
            _ = LoadTranslation();
        }

        /// <summary>Ещё раз поискать текст (например, после смены сети).</summary>
        public void ReloadLyrics() { if (HasSession && Title != "") _ = LoadLyrics(); }

        /// <summary>Перевод строк песни одним запросом.</summary>
        public async Task LoadTranslation()
        {
            bool had = Translation != null || TranslationLoading || TranslationNote != null;
            Translation = null;
            TranslationNote = null;
            var lines = Lyrics;
            if (!Store.Settings.LyricsTranslate || lines == null || lines.Count == 0) { TranslationLoading = false; if (had) LyricsChanged?.Invoke(); return; }
            string key = TrackKey, to = Store.Settings.LyricsTranslateTo;
            int gen = ++_trGen;
            TranslationLoading = true;
            LyricsChanged?.Invoke();
            try
            {
                var (res, via) = await LyricsTranslate.Run(key, lines.Select(l => l.Text).ToList(), to);
                if (gen != _trGen || key != TrackKey || lines != Lyrics) return;
                Translation = res;
                if (via != "cache") TranslationVia = via;
            }
            catch (LyricsTranslate.SameLanguageException)
            {
                if (gen != _trGen) return;
                TranslationNote = I18n.T("Текст уже на этом языке") + " (" + LyricsTranslate.Name(to) + ")";
            }
            catch (Exception e)
            {
                if (gen != _trGen) return;
                TranslationNote = e.Message;
            }
            finally
            {
                if (gen == _trGen) { TranslationLoading = false; LyricsChanged?.Invoke(); }
            }
        }
        int _trGen;
        /// <summary>Перевод строк ещё идёт.</summary>
        public bool TranslationLoading { get; private set; }
        /// <summary>Почему перевода нет (сервисы недоступны, текст уже на этом языке) — показывается в плеере.</summary>
        public string TranslationNote { get; private set; }
        /// <summary>Кто перевёл: Google, MyMemory, ИИ…</summary>
        public string TranslationVia { get; private set; }

        // ---------- история и избранное ----------
        void AddHistory()
        {
            var list = Store.Data.Played;
            var last = list.FirstOrDefault();
            if (last != null && last.Title == Title && last.Artist == Artist) return;
            list.Insert(0, new PlayedItem { Title = Title, Artist = Artist, App = AppName });
            int keep = Math.Clamp(Store.Settings.HistoryKeep, 10, 500);
            if (list.Count > keep) list.RemoveRange(keep, list.Count - keep);
            Store.SaveData();
        }

        public bool IsFavorite => Store.Data.Favorites.Any(f => f.Title == Title && f.Artist == Artist);

        public bool ToggleFavorite()
        {
            if (string.IsNullOrEmpty(Title)) return false;
            var fav = Store.Data.Favorites.FirstOrDefault(f => f.Title == Title && f.Artist == Artist);
            if (fav != null) Store.Data.Favorites.Remove(fav);
            else Store.Data.Favorites.Insert(0, new PlayedItem { Title = Title, Artist = Artist, App = AppName });
            Store.SaveData();
            Changed?.Invoke();
            return fav == null;
        }

        // ------------------------------------------------------------------
        //  Управление (сразу показываем результат, потом сверяемся с приложением)
        // ------------------------------------------------------------------

        void Confirm() { _confirm.Stop(); _confirm.Start(); }

        public async void TogglePlay()
        {
            if (_s == null) return;
            Rebase();
            IsPlaying = !IsPlaying;
            PlaybackChanged?.Invoke();
            try { await _s.TryTogglePlayPauseAsync(); } catch { }
            Confirm();
        }

        public async void Play() { if (_s != null && !IsPlaying) { try { await _s.TryPlayAsync(); } catch { } Confirm(); } }
        public async void Pause() { if (_s != null && IsPlaying) { try { await _s.TryPauseAsync(); } catch { } Confirm(); } }
        public async void Next() { try { if (_s != null) await _s.TrySkipNextAsync(); } catch { } Confirm(); }
        public async void Prev() { try { if (_s != null) await _s.TrySkipPreviousAsync(); } catch { } Confirm(); }

        public async void Seek(TimeSpan to)
        {
            if (_s == null || !CanSeek) return;
            if (to < TimeSpan.Zero) to = TimeSpan.Zero;
            if (to > Duration) to = Duration;
            SetPos(to.TotalSeconds);
            Tick?.Invoke();
            UpdateLyricIndex(true); // текст сразу прыгает на нужную строку
            try { await _s.TryChangePlaybackPositionAsync(to.Ticks); } catch { }
            Confirm();
        }

        public void SeekBy(double seconds)
        {
            if (!CanSeek) return;
            Seek(Position + TimeSpan.FromSeconds(seconds));
        }

        /// <summary>2.2: шаг перемотки из настроек (5–60 с).</summary>
        public static int Step => Math.Clamp(Store.Settings.PlayerSeekStep, 5, 60);
        public void SeekStep(int dir) => SeekBy(dir * Step);

        // ---- повтор фрагмента A–B ----
        public TimeSpan? LoopA { get; private set; }
        public TimeSpan? LoopB { get; private set; }

        /// <summary>1-е нажатие — начало фрагмента, 2-е — конец (и сразу повтор), 3-е — выключить. Возвращает "A", "AB" или null.</summary>
        public string CycleLoop()
        {
            if (!CanSeek) { LoopA = LoopB = null; return null; }
            if (LoopA == null) { LoopA = Position; return "A"; }
            if (LoopB == null)
            {
                if (Position <= LoopA.Value + TimeSpan.FromSeconds(1)) { LoopA = null; return null; }
                LoopB = Position;
                Seek(LoopA.Value);
                return "AB";
            }
            LoopA = LoopB = null;
            return null;
        }

        void CheckLoop()
        {
            if (LoopA is TimeSpan a && LoopB is TimeSpan b && IsPlaying && Position >= b) Seek(a);
        }

        /// <summary>Сколько раз этот трек есть в истории прослушиваний.</summary>
        public int PlayCount
        {
            get
            {
                try { var t = Title; var ar = Artist; return Store.Data.Played.Count(p => p.Title == t && p.Artist == ar); }
                catch { return 0; }
            }
        }

        public async void ToggleShuffle()
        {
            if (_s == null) return;
            Shuffle = !Shuffle;
            PlaybackChanged?.Invoke();
            try { await _s.TryChangeShuffleActiveAsync(Shuffle); } catch { }
            Confirm();
        }

        public async void CycleRepeat()
        {
            if (_s == null) return;
            Repeat = (Repeat + 1) % 3;
            PlaybackChanged?.Invoke();
            var mode = Repeat switch
            {
                1 => Windows.Media.MediaPlaybackAutoRepeatMode.Track,
                2 => Windows.Media.MediaPlaybackAutoRepeatMode.List,
                _ => Windows.Media.MediaPlaybackAutoRepeatMode.None,
            };
            try { await _s.TryChangeAutoRepeatModeAsync(mode); } catch { }
            Confirm();
        }

        public async void CycleRate()
        {
            if (_s == null) return;
            double[] rates = { 1, 1.25, 1.5, 2, 0.75 };
            int i = Array.FindIndex(rates, r => Math.Abs(r - Rate) < 0.01);
            Rebase();
            Rate = rates[(i + 1) % rates.Length];
            PlaybackChanged?.Invoke();
            try { await _s.TryChangePlaybackRateAsync(Rate); } catch { }
            Confirm();
        }

        public string TrackLine => string.IsNullOrEmpty(Artist) ? Title : $"{Artist} — {Title}";

        /// <summary>Переключиться на следующий источник (если играют сразу несколько приложений).</summary>
        public void CycleSource()
        {
            var list = Sessions();
            if (list.Count < 2) return;
            int i = list.FindIndex(x => ReferenceEquals(x, _s));
            if (i < 0) i = list.FindIndex(x => x.SourceAppUserModelId == AppId);
            var next = list[(i + 1) % list.Count];
            _pinned = next.SourceAppUserModelId;
            Attach(next);
        }

        public int SourceCount => Sessions().Count;

        public static bool IsBrowser(string aumid)
        {
            var a = (aumid ?? "").ToLowerInvariant();
            return a.Contains("chrome") || a.Contains("msedge") || a.Contains("firefox") || a.Contains("308046b0af4a39cb") || a.Contains("opera")
                   || a.Contains("brave") || a.Contains("vivaldi") || (a.Contains("yandex") && !a.Contains("music")) || a.Contains("thebrowsercompany") || a.Contains("browser");
        }

        public static string FriendlyName(string aumid)
        {
            if (string.IsNullOrEmpty(aumid)) return "";
            var a = aumid.ToLowerInvariant();
            if (a.Contains("spotify")) return "Spotify";
            if (a.Contains("msedge")) return "Edge";
            if (a.Contains("chrome")) return "Chrome";
            if (a.Contains("firefox") || a.Contains("308046b0af4a39cb")) return "Firefox";
            if (a.Contains("yandex.music") || a.Contains("yandexmusic")) return "Яндекс Музыка";
            if (a.Contains("yandex")) return "Яндекс";
            if (a.Contains("opera")) return "Opera";
            if (a.Contains("brave")) return "Brave";
            if (a.Contains("vivaldi")) return "Vivaldi";
            if (a.Contains("zunemusic") || a.Contains("media.player")) return "Медиаплеер";
            if (a.Contains("applemusic") || a.Contains("itunes")) return "Apple Music";
            if (a.Contains("telegram")) return "Telegram";
            if (a.Contains("vlc")) return "VLC";
            if (a.Contains("aimp")) return "AIMP";
            if (a.Contains("foobar")) return "foobar2000";
            if (a.Contains("discord")) return "Discord";
            if (a.Contains("deezer")) return "Deezer";
            if (a.Contains("tidal")) return "TIDAL";
            if (a.Contains("soundcloud")) return "SoundCloud";
            if (a.Contains("musicbee")) return "MusicBee";
            if (a.Contains("winamp")) return "Winamp";
            if (a.Contains("potplayer")) return "PotPlayer";
            if (a.Contains("mpc-hc") || a.Contains("mpc-be")) return "MPC";
            var n = aumid;
            int bang = n.LastIndexOf('!');
            if (bang >= 0) n = n[(bang + 1)..];
            n = Path.GetFileNameWithoutExtension(n.Split('\\', '/').Last());
            int us = n.IndexOf('_');
            if (us > 0) n = n[..us];
            if (n.Contains('.')) n = n.Split('.').Last();
            return Fmt.Cap(n);
        }
    }
}
