using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NIsland.Core;
using Windows.Foundation;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace NIsland.Services
{
    /// <summary>
    /// Музыка/видео из ЛЮБОГО приложения, которое сообщает Windows о воспроизведении
    /// (Spotify, браузеры с YouTube/VK/Яндекс Музыкой, Apple Music, VLC, Telegram, медиаплеер…).
    /// </summary>
    public sealed class MediaService
    {
        public static MediaService I { get; } = new();

        GlobalSystemMediaTransportControlsSessionManager _mgr;
        GlobalSystemMediaTransportControlsSession _s;
        string _pinned;
        readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(200) };
        int _tickCount;

        TypedEventHandler<GlobalSystemMediaTransportControlsSession, MediaPropertiesChangedEventArgs> _onProps;
        TypedEventHandler<GlobalSystemMediaTransportControlsSession, PlaybackInfoChangedEventArgs> _onPlay;
        TypedEventHandler<GlobalSystemMediaTransportControlsSession, TimelinePropertiesChangedEventArgs> _onTimeline;

        public bool HasSession => _s != null;
        public string AppId { get; private set; }
        public string AppName => FriendlyName(AppId);
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
        public TimeSpan Duration { get; private set; }
        public bool CanSeek => Duration.TotalSeconds > 1;
        TimeSpan _pos;
        DateTimeOffset _posAt = DateTimeOffset.Now;

        public List<LyricLine> Lyrics { get; private set; }
        public bool LyricsSynced { get; private set; }
        public bool LyricsLoading { get; private set; }
        public string LyricsSource { get; private set; }
        public int LyricIndex { get; private set; } = -1;
        /// <summary>Перевод строк текста (тот же порядок), если включён.</summary>
        public List<string> Translation { get; private set; }

        // ---------- синхронизация текста ----------
        static Dictionary<string, double> _offsets => Store.Data.LyricOffsets;
        string TrackKey => Title + "|" + Artist;
        /// <summary>Сдвиг текста относительно музыки, с: общий из настроек + подстройка для этого трека.</summary>
        public double LyricsOffset => Store.Settings.LyricsOffsetMs / 1000.0 + (_offsets.TryGetValue(TrackKey, out var v) ? v : 0);
        public double TrackOffset => _offsets.TryGetValue(TrackKey, out var v) ? v : 0;
        public void AdjustOffset(double seconds)
        {
            _offsets[TrackKey] = Math.Round(TrackOffset + seconds, 2);
            if (_offsets.Count > 500) _offsets.Remove(_offsets.Keys.First());   // помним последние 500 песен
            Store.SaveData();
            UpdateLyricIndex(true);
        }
        public void ResetOffset() { _offsets.Remove(TrackKey); Store.SaveData(); UpdateLyricIndex(true); }
        /// <summary>Общий сдвиг поменяли в настройках — пересчитать текущую строчку.</summary>
        public void Resync() => UpdateLyricIndex(true);

        /// <summary>Позиция с учётом сдвига текста.</summary>
        public TimeSpan LyricsPosition => Position + TimeSpan.FromSeconds(LyricsOffset);

        /// <summary>Сколько осталось до следующей строки (для «караоке»-заливки).</summary>
        public (double Progress, TimeSpan Left) LineProgress()
        {
            if (Lyrics == null || !LyricsSynced || LyricIndex < 0 || LyricIndex >= Lyrics.Count) return (0, TimeSpan.Zero);
            var start = Lyrics[LyricIndex].Time;
            var end = LyricIndex + 1 < Lyrics.Count ? Lyrics[LyricIndex + 1].Time : start + TimeSpan.FromSeconds(5);
            var pos = LyricsPosition;
            double total = Math.Max(0.1, (end - start).TotalSeconds);
            return (Math.Clamp((pos - start).TotalSeconds / total, 0, 1), end - pos);
        }

        public event Action OffsetChanged;
        public string CurrentLyric => LyricsSynced && Lyrics != null && LyricIndex >= 0 && LyricIndex < Lyrics.Count ? Lyrics[LyricIndex].Text : null;

        public event Action Changed;          // название/обложка/источник
        public event Action PlaybackChanged;  // play/pause
        public event Action TrackChanged;     // новый трек (для всплывашки)
        public event Action Tick;             // позиция
        public event Action LyricsChanged;
        public event Action LyricIndexChanged;

        static Dispatcher D => Application.Current.Dispatcher;

        public TimeSpan Position
        {
            get
            {
                var p = _pos;
                if (IsPlaying) p += DateTimeOffset.Now - _posAt;
                if (p < TimeSpan.Zero) p = TimeSpan.Zero;
                if (Duration > TimeSpan.Zero && p > Duration) p = Duration;
                return p;
            }
        }

        public async void Start()
        {
            _onProps = (s, e) => D.BeginInvoke(new Action(() => { if (Same(s)) _ = LoadProps(); }));
            _onPlay = (s, e) => D.BeginInvoke(new Action(() => { if (Same(s)) LoadPlayback(); }));
            _onTimeline = (s, e) => D.BeginInvoke(new Action(() => { if (Same(s)) LoadTimeline(); }));
            _tick.Tick += (_, __) => OnTick();
            _tick.Start();
            try
            {
                _mgr = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                _mgr.CurrentSessionChanged += (_, __) => D.BeginInvoke(new Action(PickSession));
                _mgr.SessionsChanged += (_, __) => D.BeginInvoke(new Action(PickSession));
                PickSession();
            }
            catch (Exception e) { Log.Error(e); }
        }

        bool Same(GlobalSystemMediaTransportControlsSession s) =>
            _s != null && s != null && s.SourceAppUserModelId == _s.SourceAppUserModelId;

        public List<GlobalSystemMediaTransportControlsSession> Sessions()
        {
            try { return _mgr?.GetSessions().ToList() ?? new(); } catch { return new(); }
        }

        static bool Playing(GlobalSystemMediaTransportControlsSession s)
        {
            try { return s.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing; }
            catch { return false; }
        }

        void PickSession()
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
                if (_s != null && !Playing(s ?? _s) && sessions.Any(x => x.SourceAppUserModelId == AppId))
                    s = sessions.First(x => x.SourceAppUserModelId == AppId);
                s ??= sessions.FirstOrDefault();
            }
            Attach(s);
        }

        void Attach(GlobalSystemMediaTransportControlsSession s)
        {
            if (s != null && _s != null && s.SourceAppUserModelId == _s.SourceAppUserModelId) return;
            if (_s != null)
            {
                try
                {
                    _s.MediaPropertiesChanged -= _onProps;
                    _s.PlaybackInfoChanged -= _onPlay;
                    _s.TimelinePropertiesChanged -= _onTimeline;
                }
                catch { }
            }
            _s = s;
            AppId = s?.SourceAppUserModelId;
            if (s == null)
            {
                Title = Artist = Album = "";
                Art = null; IsPlaying = false; Duration = TimeSpan.Zero; _pos = TimeSpan.Zero;
                Lyrics = null; LyricIndex = -1;
                Changed?.Invoke(); PlaybackChanged?.Invoke(); LyricsChanged?.Invoke();
                return;
            }
            s.MediaPropertiesChanged += _onProps;
            s.PlaybackInfoChanged += _onPlay;
            s.TimelinePropertiesChanged += _onTimeline;
            Title = ""; Artist = "";
            LoadPlayback();
            LoadTimeline();
            _ = LoadProps();
        }

        async Task LoadProps()
        {
            var s = _s;
            if (s == null) return;
            GlobalSystemMediaTransportControlsSessionMediaProperties p;
            try { p = await s.TryGetMediaPropertiesAsync(); }
            catch { return; }
            if (s != _s || p == null) return;

            string title = p.Title ?? "", artist = p.Artist ?? "", album = p.AlbumTitle ?? "";
            if (artist == "" && !string.IsNullOrEmpty(p.AlbumArtist)) artist = p.AlbumArtist;
            bool newTrack = title != Title || artist != Artist;
            Title = title; Artist = artist; Album = album;

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
                TrackChanged?.Invoke();
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
                Rate = pi.PlaybackRate ?? 1;
                if (playing != IsPlaying)
                {
                    _pos = Position; _posAt = DateTimeOffset.Now;
                    IsPlaying = playing;
                }
                PlaybackChanged?.Invoke();
            }
            catch { }
        }

        DateTimeOffset _timelineStamp;

        /// <param name="periodic">Плановая сверка: берём только реально обновлённые данные,
        /// иначе у приложений с «застывшей» меткой времени позиция откатывалась бы назад.</param>
        void LoadTimeline(bool periodic = false)
        {
            try
            {
                var t = _s?.GetTimelineProperties();
                if (t == null) return;
                bool validStamp = t.LastUpdatedTime.Year >= 2000 && t.LastUpdatedTime <= DateTimeOffset.Now.AddSeconds(2);
                if (periodic && (!validStamp || t.LastUpdatedTime == _timelineStamp)) return;
                _timelineStamp = t.LastUpdatedTime;
                Duration = t.EndTime - t.StartTime;
                var pos = t.Position - t.StartTime;
                var at = validStamp ? t.LastUpdatedTime : DateTimeOffset.Now;
                if (at > DateTimeOffset.Now) at = DateTimeOffset.Now;
                // плановая сверка не должна дёргать позицию из-за мелкой разницы часов
                if (periodic && IsPlaying)
                {
                    var predicted = Position;
                    var fresh = pos + (DateTimeOffset.Now - at);
                    if (Math.Abs((fresh - predicted).TotalSeconds) < 0.35) return;
                }
                _pos = pos;
                _posAt = at;
                Tick?.Invoke();
                UpdateLyricIndex(true);
            }
            catch { }
        }

        async Task LoadLyrics()
        {
            string key = Title + "|" + Artist;
            Lyrics = null; LyricIndex = -1; LyricsLoading = true; LyricsSynced = false;
            LyricsChanged?.Invoke(); LyricIndexChanged?.Invoke();
            // дождёмся длительности трека — она приходит отдельным событием
            for (int i = 0; i < 6 && Duration.TotalSeconds < 1; i++) await Task.Delay(250);
            var res = await LyricsService.Get(Title, Artist, Album, Duration);
            if (key != Title + "|" + Artist) return;
            Lyrics = res?.Lines;
            LyricsSynced = res?.Synced ?? false;
            LyricsSource = res?.Source;
            LyricsLoading = false;
            Translation = null;
            LyricsChanged?.Invoke();
            UpdateLyricIndex();
            _ = LoadTranslation();
        }

        void OnTick()
        {
            if (IsPlaying) Tick?.Invoke();
            UpdateLyricIndex();
            // раз в секунду сверяем позицию с приложением — текст не «уплывает» от музыки
            if (IsPlaying && _tickCount % 5 == 0) LoadTimeline(true);
            if (++_tickCount % 10 == 0 && _pinned == null && _mgr != null && !IsPlaying)
            {
                // кто-то другой начал играть — переключаемся на него
                var other = Sessions().FirstOrDefault(Playing);
                if (other != null && other.SourceAppUserModelId != AppId) Attach(other);
            }
        }

        void UpdateLyricIndex(bool resync = false)
        {
            int idx = -1;
            if (Lyrics != null && LyricsSynced)
            {
                // лёгкое опережение: строка появляется чуть раньше, чем её начнут петь
                var pos = LyricsPosition + TimeSpan.FromMilliseconds(150);
                for (int i = 0; i < Lyrics.Count; i++)
                {
                    if (Lyrics[i].Time <= pos) idx = i; else break;
                }
            }
            if (idx != LyricIndex)
            {
                LyricIndex = idx;
                LyricIndexChanged?.Invoke();
            }
            else if (resync) OffsetChanged?.Invoke();
        }

        /// <summary>Поставить выбранный вручную текст (и запомнить его для трека).</summary>
        public void SetLyrics(LyricsResult r)
        {
            Lyrics = r?.Lines;
            LyricsSynced = r?.Synced ?? false;
            LyricsSource = r?.Source;
            LyricsLoading = false;
            LyricIndex = -1;
            if (r != null) LyricsService.Remember(Title, Artist, r);
            LyricsChanged?.Invoke();
            UpdateLyricIndex();
            _ = LoadTranslation();
        }

        /// <summary>Перевод строк песни одним запросом.</summary>
        public async Task LoadTranslation()
        {
            bool had = Translation != null;
            Translation = null;
            var lines = Lyrics;
            if (!Store.Settings.LyricsTranslate || lines == null || lines.Count == 0) { if (had) LyricsChanged?.Invoke(); return; }
            string key = TrackKey;
            try
            {
                var text = string.Join("\n", lines.Select(l => l.Text == "♪" ? "." : l.Text));
                var (res, from) = await TranslateService.Raw(text, Store.Settings.LyricsTranslateTo);
                if (key != TrackKey || lines != Lyrics) return;
                if (string.Equals(from, Store.Settings.LyricsTranslateTo, StringComparison.OrdinalIgnoreCase)) return; // уже на нужном языке
                var parts = res.Split('\n');
                Translation = lines.Select((l, i) => i < parts.Length && l.Text != "♪" ? parts[i].Trim() : "").ToList();
                LyricsChanged?.Invoke();
            }
            catch (Exception e) { Log.Info("lyrics translate: " + e.Message); }
        }

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

        public async void TogglePlay()
        {
            if (_s == null) return;
            _pos = Position; _posAt = DateTimeOffset.Now;
            IsPlaying = !IsPlaying;
            PlaybackChanged?.Invoke();
            try { await _s.TryTogglePlayPauseAsync(); } catch { }
        }

        public async void Next() { try { if (_s != null) await _s.TrySkipNextAsync(); } catch { } }
        public async void Prev() { try { if (_s != null) await _s.TrySkipPreviousAsync(); } catch { } }

        public async void Seek(TimeSpan to)
        {
            if (_s == null) return;
            _pos = to; _posAt = DateTimeOffset.Now;
            Tick?.Invoke();
            UpdateLyricIndex(true); // текст сразу прыгает на нужную строку
            try { await _s.TryChangePlaybackPositionAsync(to.Ticks); } catch { }
        }

        public void SeekBy(double seconds)
        {
            if (!CanSeek) return;
            var to = Position + TimeSpan.FromSeconds(seconds);
            if (to < TimeSpan.Zero) to = TimeSpan.Zero;
            if (to > Duration) to = Duration;
            Seek(to);
        }

        public async void ToggleShuffle()
        {
            if (_s == null) return;
            Shuffle = !Shuffle;
            PlaybackChanged?.Invoke();
            try { await _s.TryChangeShuffleActiveAsync(Shuffle); } catch { }
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
        }

        public async void CycleRate()
        {
            if (_s == null) return;
            double[] rates = { 1, 1.25, 1.5, 2, 0.75 };
            int i = Array.FindIndex(rates, r => Math.Abs(r - Rate) < 0.01);
            Rate = rates[(i + 1) % rates.Length];
            PlaybackChanged?.Invoke();
            try { await _s.TryChangePlaybackRateAsync(Rate); } catch { }
        }

        public string TrackLine => string.IsNullOrEmpty(Artist) ? Title : $"{Artist} — {Title}";

        /// <summary>Переключиться на следующий источник (если играют сразу несколько приложений).</summary>
        public void CycleSource()
        {
            var list = Sessions();
            if (list.Count < 2) return;
            int i = list.FindIndex(x => x.SourceAppUserModelId == AppId);
            var next = list[(i + 1) % list.Count];
            _pinned = next.SourceAppUserModelId;
            Attach(next);
        }

        public int SourceCount => Sessions().Count;

        public static string FriendlyName(string aumid)
        {
            if (string.IsNullOrEmpty(aumid)) return "";
            var a = aumid.ToLowerInvariant();
            if (a.Contains("spotify")) return "Spotify";
            if (a.Contains("msedge")) return "Edge";
            if (a.Contains("chrome")) return "Chrome";
            if (a.Contains("firefox") || a.Contains("308046b0af4a39cb")) return "Firefox";
            if (a.Contains("yandex")) return "Яндекс";
            if (a.Contains("opera")) return "Opera";
            if (a.Contains("brave")) return "Brave";
            if (a.Contains("zunemusic") || a.Contains("media.player")) return "Медиаплеер";
            if (a.Contains("applemusic") || a.Contains("itunes")) return "Apple Music";
            if (a.Contains("telegram")) return "Telegram";
            if (a.Contains("vlc")) return "VLC";
            if (a.Contains("aimp")) return "AIMP";
            if (a.Contains("foobar")) return "foobar2000";
            if (a.Contains("discord")) return "Discord";
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
