using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using NIsland.Core;
using NIsland.Services;

namespace NIsland.Pages
{
    /// <summary>Плеер для любого приложения + синхронный текст песни (караоке, сдвиг, выбор, перевод).</summary>
    public class MediaPage : PageBase
    {
        const double LyricsW = 300;
        static MediaService M => MediaService.I;
        static readonly Color Dim = Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF);

        readonly Border _art = new() { Width = 64, Height = 64, CornerRadius = new CornerRadius(12) };
        readonly ImageBrush _artBrush = new() { Stretch = Stretch.UniformToFill };
        readonly TextBlock _artPh = UI.Icon("", 24, UI.Hex("#80FFFFFF"));
        readonly TextBlock _title = UI.Text("", 17, true);
        readonly TextBlock _artist = UI.Text("", 13);
        readonly TextBlock _pos = UI.Text("0:00", 12);
        readonly TextBlock _rem = UI.Text("-0:00", 12);
        readonly Slider _seek = UI.Slider();
        readonly Button _play, _prev, _next, _lyricsBtn, _volBtn, _source, _shuffle, _repeat, _back10, _fwd10, _rate, _fav;
        readonly Slider _vol = UI.Slider();
        readonly Grid _player;
        readonly ColumnDefinition _mainCol = new(), _lyricsCol = new() { Width = new GridLength(0) };
        readonly Grid _main;

        // текст песни
        readonly Grid _lyricsPane = new();
        readonly Grid _lyricsBody = new() { ClipToBounds = true };
        readonly StackPanel _lines = new() { VerticalAlignment = VerticalAlignment.Top };
        readonly TranslateTransform _shift = new();
        readonly TextBlock _lyricsStatus = UI.Text("", 12.5);
        readonly TextBlock _lyrInfo = UI.Text("", 10.5);
        readonly TextBlock _focusTitle = UI.Text("", 12, true);
        readonly Button _trBtn, _focusBtn, _focusPlay;
        readonly List<TextBlock> _lineBlocks = new();
        readonly List<FrameworkElement> _lineRows = new();
        GradientStop _k1, _k2;
        int _karaokeLine = -2;

        // выбор другого текста
        readonly Grid _picker = new() { Visibility = Visibility.Collapsed };
        readonly StackPanel _pickList = new();
        readonly TextBox _pickQuery = UI.Box("Название и исполнитель…");

        // история и избранное
        readonly Grid _history = new() { Visibility = Visibility.Collapsed };
        readonly StackPanel _histList = new();
        bool _histFav;

        readonly StackPanel _empty;
        readonly Border _glow = new() { IsHitTestVisible = false, Height = 110, VerticalAlignment = VerticalAlignment.Bottom, Opacity = 0 };
        readonly GradientStop _glowStop = new(Colors.Transparent, 1);
        bool _seeking, _volSetting, _linesDirty = true;

        bool LyricsOn => S.LyricsOn && M.HasSession;
        bool FocusMode => LyricsOn && S.LyricsFocus;
        public override double ExtraWidth => LyricsOn && !FocusMode ? LyricsW : 0;

        static Button Small(string glyph, Action click, string tip, double size = 11, double box = 24) => UI.IconBtn(glyph, click, size, box, tip);

        static Button Label(string text, Action click, string tip)
        {
            var b = UI.IconBtn("", click, 10, 30, tip);
            b.Content = new TextBlock { Text = text, FontSize = 10.5, FontWeight = FontWeights.SemiBold, FontFamily = (FontFamily)Application.Current.Resources["UiFont"] };
            return b;
        }

        public MediaPage()
        {
            _art.Background = UI.Hex("#22FFFFFF");
            _art.Child = new Grid().Add(_artPh, new Border { CornerRadius = new CornerRadius(12), Background = _artBrush });
            _artist.Foreground = UI.Dim;
            _pos.Foreground = _rem.Foreground = UI.Dim;
            _pos.MinWidth = 38;
            _rem.MinWidth = 38;
            _rem.TextAlignment = TextAlignment.Right;

            _play = UI.IconBtn("", M.TogglePlay, 22, 44, "Пауза / воспроизведение");
            _prev = UI.IconBtn("", M.Prev, 17, 38, "Назад");
            _next = UI.IconBtn("", M.Next, 17, 38, "Вперёд");
            _lyricsBtn = UI.IconBtn("", ToggleLyrics, 14, 30, "Текст песни");
            _volBtn = UI.IconBtn("", () => AudioService.I.ToggleMute(), 14, 30, "Звук");
            _source = UI.Pill("", M.CycleSource, false, "Источник — нажми, чтобы переключить приложение");
            _source.Padding = new Thickness(9, 3, 9, 3);
            _source.FontSize = 11;
            _vol.Width = 74;
            _shuffle = UI.IconBtn("", M.ToggleShuffle, 13, 30, "Перемешать");
            _repeat = UI.IconBtn("", M.CycleRepeat, 13, 30, "Повтор: выкл → трек → весь список");
            _back10 = Label("−10", () => M.SeekBy(-10), "Назад на 10 секунд");
            _fwd10 = Label("+10", () => M.SeekBy(10), "Вперёд на 10 секунд");
            _rate = UI.Pill("1×", M.CycleRate, false, "Скорость воспроизведения");
            _rate.Padding = new Thickness(8, 2, 8, 2);
            _rate.FontSize = 10.5;
            _fav = Small("", () =>
            {
                bool on = M.ToggleFavorite();
                App.Island.Toast(on ? "" : "", on ? "Добавлено в избранное" : "Убрано из избранного", M.TrackLine);
            }, "В избранное", 12, 26);

            var extras = UI.Row(
                _rate.M(0, 0, 2, 0),
                _fav,
                Small("", () =>
                {
                    ClipboardService.I.CopyBack(M.TrackLine);
                    App.Island.Toast("", "Название скопировано", M.TrackLine);
                }, "Скопировать «Исполнитель — Название»", 12, 26),
                Small("", () => SystemService.Run("https://www.google.com/search?q=" + Uri.EscapeDataString(M.TrackLine)), "Найти трек в интернете", 12, 26),
                Small("", () => ShowHistory(false), "История и избранное", 12, 26),
                Small("", () => App.Island.Expand("sound"), "Микшер и устройство вывода", 12, 26));
            extras.VerticalAlignment = VerticalAlignment.Top;

            var top = UI.Grid("a,*,a").Add(_art, UI.Col(_title, _artist).At(0, 1).M(14, 0, 4, 0), extras.At(0, 2));
            var prog = UI.Grid("a,*,a").M(0, 8, 0, 0).Add(_pos, _seek.At(0, 1).M(8, 0, 8, 0), _rem.At(0, 2));
            var ctrl = UI.Grid("*,a,*").M(0, 2, 0, 0).Add(
                UI.Row(_lyricsBtn, _source.M(4, 0, 0, 0)),
                UI.Row(_shuffle, _back10, _prev, _play.M(4, 0, 4, 0), _next, _fwd10, _repeat).At(0, 1),
                UI.Row(_volBtn, _vol.M(2, 0, 0, 0)).At(0, 2).HA(HorizontalAlignment.Right));
            _main = UI.Grid(null, "a,a,a").M(6, 0, 4, 0).VA(VerticalAlignment.Center);
            _main.Add(top, prog.At(1, 0), ctrl.At(2, 0));

            // ----- панель текста -----
            _lines.RenderTransform = _shift;
            _lyricsStatus.Foreground = UI.Dim;
            _lyricsStatus.TextWrapping = TextWrapping.Wrap;
            _lyrInfo.Foreground = UI.Faint;
            _trBtn = Small("", () => { S.LyricsTranslate = !S.LyricsTranslate; RenderLyricsTools(); }, "Перевод текста песни");
            _focusBtn = Small("", () => { S.LyricsFocus = !S.LyricsFocus; ApplyLayout(); Relayout(); }, "Только текст (крупно)");
            _focusPlay = Small("", M.TogglePlay, "Пауза / воспроизведение", 12, 26);
            var tools = UI.Row(
                Label("−0.5", () => { M.AdjustOffset(-0.5); RenderLyricsTools(); }, "Текст спешит — сдвинуть назад на 0.5 с"),
                Label("+0.5", () => { M.AdjustOffset(0.5); RenderLyricsTools(); }, "Текст опаздывает — сдвинуть вперёд на 0.5 с"),
                Small("", () => { M.ResetOffset(); RenderLyricsTools(); }, "Сбросить сдвиг"),
                _trBtn,
                Small("", ShowPicker, "Не тот текст? Выбрать другой"),
                _focusBtn);
            foreach (Button b in tools.Children) { b.Width = 26; b.Height = 24; }
            var lyrHead = UI.Grid("a,*,a").Add(
                UI.Row(_focusPlay, _focusTitle.M(4, 0, 0, 0)),
                _lyrInfo.At(0, 1).M(4, 0, 4, 0),
                tools.At(0, 2));
            _linesHost.Children.Add(_lines);
            _lyricsBody.Add(_linesHost.M(0, 0, 6, 0), _lyricsStatus.M(0, 6, 6, 0), _picker);
            _wordTimer.Tick += (_, __) => OnWordTick();
            _lyricsBody.MouseWheel += (_, e) =>
            {
                if (_picker.Visibility == Visibility.Visible) return;
                _shift.BeginAnimation(TranslateTransform.YProperty, null);
                _shift.Y = Math.Min(0, _shift.Y + e.Delta / 3.0);
                _manualScrollUntil = DateTime.Now.AddSeconds(4);
                e.Handled = true;
            };
            var divider = new Border { Width = 1, HorizontalAlignment = HorizontalAlignment.Left, Background = UI.Hex("#22FFFFFF"), Margin = new Thickness(0, 4, 0, 4) };
            _lyricsPane.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _lyricsPane.RowDefinitions.Add(new RowDefinition());
            _lyricsPane.Add(lyrHead.M(14, 0, 2, 2), _lyricsBody.At(1, 0).M(16, 0, 0, 0), divider);
            Grid.SetRowSpan(divider, 2);

            // ----- выбор другого текста -----
            _pickQuery.FontSize = 11.5;
            _pickQuery.KeyDown += (_, e) => { if (e.Key == Key.Enter) LoadCandidates(_pickQuery.Text); };
            var pickHead = UI.Grid("*,a,a").Add(_pickQuery,
                Small("", () => LoadCandidates(_pickQuery.Text), "Искать").At(0, 1),
                Small("", HidePicker, "Закрыть").At(0, 2));
            _picker.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _picker.RowDefinitions.Add(new RowDefinition());
            _picker.Background = UI.Hex("#F0141418");
            _picker.Add(pickHead, new ScrollViewer { Content = _pickList, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 4, 0, 0) }.At(1, 0));

            _player = new Grid();
            _player.ColumnDefinitions.Add(_mainCol);
            _player.ColumnDefinitions.Add(_lyricsCol);
            _player.Add(_main, _lyricsPane.At(0, 1).M(8, 0, 4, 0));

            // ----- история -----
            BuildHistory();

            _empty = UI.Col(
                UI.Icon("", 26, UI.Faint),
                UI.Text("Сейчас ничего не играет", 15, true).HA(HorizontalAlignment.Center).M(0, 8, 0, 2),
                new TextBlock
                {
                    Text = "Включи музыку или видео в любом приложении или на сайте — Spotify, YouTube, Яндекс Музыка, VK, Telegram…",
                    Foreground = UI.Dim, FontSize = 12, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, MaxWidth = 420,
                },
                UI.Pill(UI.IconLabel("", "История прослушиваний", 11), () => ShowHistory(false)).HA(HorizontalAlignment.Center).M(0, 8, 0, 0));
            _empty.HorizontalAlignment = HorizontalAlignment.Center;

            _glow.Background = new LinearGradientBrush(new GradientStopCollection { new(Colors.Transparent, 0), _glowStop }, 90);
            _glow.Margin = new Thickness(-60, 0, -60, -34);

            Content = new Grid().Add(_glow, _player, _empty, _history);

            // события
            _seek.PreviewMouseLeftButtonDown += (_, __) => _seeking = true;
            _seek.PreviewMouseLeftButtonUp += (_, __) =>
            {
                if (_seeking && M.CanSeek) M.Seek(TimeSpan.FromSeconds(_seek.Value));
                _seeking = false;
            };
            _vol.ValueChanged += (_, __) => { if (!_volSetting) AudioService.I.SetVolume(_vol.Value); };
            _vol.MouseWheel += (_, e) => { AudioService.I.SetVolume(AudioService.I.Volume + (e.Delta > 0 ? 0.05 : -0.05)); e.Handled = true; };

            M.Changed += () => { _linesDirty = true; if (Visible) Refresh(); };
            M.PlaybackChanged += () => { if (Visible) { UpdatePlay(); Karaoke(true); } };
            M.Tick += () => { if (Visible) UpdatePos(); };
            M.LyricsChanged += () => { _linesDirty = true; if (Visible) RefreshLyrics(); };
            M.LyricIndexChanged += () => { if (Visible) Highlight(true); };
            M.OffsetChanged += () => { if (Visible) { Karaoke(true); RenderLyricsTools(); } };
            AudioService.I.Changed += (_, __, ___) => { if (Visible) UpdateVol(); };
        }

        DateTime _manualScrollUntil;

        public override void OnShown()
        {
            _history.Visibility = Visibility.Collapsed;
            HidePicker();
            Refresh();
            RefreshLyrics();
        }

        public override void OnHidden()
        {
            _artBrush.ImageSource = null; // обложку держит сервис, страница — нет
            _wordTimer.Stop();
            _k1?.BeginAnimation(GradientStop.OffsetProperty, null);
            _k2?.BeginAnimation(GradientStop.OffsetProperty, null);
        }

        void ToggleLyrics()
        {
            S.LyricsOn = !S.LyricsOn;
            RefreshLyrics();
            Relayout();
        }

        void ApplyLayout()
        {
            bool on = LyricsOn, focus = FocusMode;
            _mainCol.Width = focus ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
            _main.Show(!focus);
            _lyricsCol.Width = !on ? new GridLength(0) : focus ? new GridLength(1, GridUnitType.Star) : new GridLength(LyricsW);
            _lyricsPane.Show(on);
            _focusPlay.Show(focus);
            _focusTitle.Show(focus);
            _focusTitle.Text = M.TrackLine;
            _focusTitle.MaxWidth = 260;
            _focusBtn.Foreground = focus ? UI.Res("AccentBrush") : Brushes.White;
            _linesDirty = true;
        }

        void Refresh()
        {
            bool has = M.HasSession;
            _player.Show(has);
            _empty.Show(!has);
            if (!has) { _glow.Opacity = 0; return; }
            _title.Text = string.IsNullOrEmpty(M.Title) ? "Без названия" : M.Title;
            _artist.Text = string.Join(" — ", new[] { M.Artist, S.PlayerShowAlbum ? M.Album : null }.Where(s => !string.IsNullOrWhiteSpace(s)));
            _artBrush.ImageSource = M.Art;
            _artPh.Show(M.Art == null);
            double art = Math.Clamp(S.PlayerArtSize, 40, 80);
            _art.Width = _art.Height = art;
            _source.Content = M.AppName;
            _source.Show(!string.IsNullOrEmpty(M.AppName));
            _prev.IsEnabled = M.CanPrev;
            _next.IsEnabled = M.CanNext;
            _back10.Show(S.PlayerSeekButtons && M.CanSeek);
            _fwd10.Show(S.PlayerSeekButtons && M.CanSeek);
            bool fav = M.IsFavorite;
            _fav.Content = fav ? "" : "";
            _fav.Foreground = fav ? UI.Hex("#FF375F") : Brushes.White;
            if (S.ProgressArtColor) _seek.Foreground = new SolidColorBrush(M.ArtColor); else _seek.ClearValue(ForegroundProperty);
            _glowStop.Color = ColorUtil.WithAlpha(M.ArtColor, 0.55);
            _focusTitle.Text = M.TrackLine;
            UpdatePlay();
            UpdatePos();
            UpdateVol();
            Relayout();
        }

        void UpdatePlay()
        {
            _play.Content = _focusPlay.Content = M.IsPlaying ? "" : "";
            _shuffle.Show(M.CanShuffle);
            _repeat.Show(M.CanRepeat);
            _rate.Show(M.CanRate);
            _shuffle.Foreground = M.Shuffle ? UI.Res("AccentBrush") : Brushes.White;
            _repeat.Foreground = M.Repeat > 0 ? UI.Res("AccentBrush") : Brushes.White;
            _repeat.Content = M.Repeat == 1 ? "" : "";
            _rate.Content = M.Rate.ToString("0.##", CultureInfo.InvariantCulture) + "×";
        }

        void UpdatePos()
        {
            bool seek = M.CanSeek, has = M.HasTime;
            _seek.IsEnabled = seek;
            _seek.Maximum = Math.Max(1, M.Duration.TotalSeconds);
            var p = M.Position;
            if (!_seeking) _seek.Value = has ? Math.Min(p.TotalSeconds, _seek.Maximum) : 0;
            _seek.Opacity = seek ? 1 : has ? 0.75 : 0.35;
            // прошедшее время знаем всегда (считаем сами), длительность — от приложения или из каталога (≈)
            _pos.Text = Fmt.Time(p);
            string approx = M.TimeEstimated && has ? "≈" : "";
            _rem.Text = !has ? "--:--" : S.ShowRemaining ? "-" + approx + Fmt.Time(M.Duration - p) : approx + Fmt.Time(M.Duration);
            string tip = M.TimeEstimated
                ? (has ? "Приложение не сообщает время трека — N-Island считает его сам, длительность взята из каталога"
                       : "Приложение не сообщает время трека — N-Island считает прошедшее время сам")
                : null;
            if (_tipRaw != tip) { _tipRaw = tip; _rem.ToolTip = _pos.ToolTip = tip == null ? null : I18n.T(tip); }
        }

        string _tipRaw;

        void UpdateVol()
        {
            var a = AudioService.I;
            _volSetting = true;
            _vol.Value = a.Muted ? 0 : a.Volume;
            _volSetting = false;
            _volBtn.Content = a.Muted || a.Volume < 0.01 ? "" : "";
        }

        void RenderLyricsTools()
        {
            double off = M.LyricsOffset;
            var src = M.LyricsLoading ? "ищу…" : M.Lyrics == null ? "" : (M.LyricsSynced ? "синхр." : "без синхр.") + (M.LyricsSource != null ? " · " + M.LyricsSource : "");
            _lyrInfo.Text = src + (Math.Abs(off) > 0.01 ? $" · сдвиг {(off > 0 ? "+" : "")}{off:0.0} с" : "");
            _trBtn.Foreground = S.LyricsTranslate ? UI.Res("AccentBrush") : Brushes.White;
        }

        void RefreshLyrics()
        {
            bool on = LyricsOn;
            ApplyLayout();
            _lyricsBtn.Foreground = on ? Brushes.White : UI.Dim;
            double glow = !S.PlayerGlow || !M.HasSession ? 0 : on ? 1 : 0.5;
            _glow.BeginAnimation(OpacityProperty, new DoubleAnimation(glow, TimeSpan.FromMilliseconds(400)));
            RenderLyricsTools();
            if (!on) return;

            if (_linesDirty)
            {
                _linesDirty = false;
                _lines.Children.Clear();
                _lineBlocks.Clear();
                _lineRows.Clear();
                _wordRuns.Clear();
                _karaokeLine = -2;
                _activeRow = -1;
                _shift.BeginAnimation(TranslateTransform.YProperty, null);
                _shift.Y = 0;
                double size = Math.Clamp(S.LyricsFontSize, 10, 24) + (FocusMode ? 5 : 0);
                bool synced = M.LyricsSynced;
                _linesHost.OpacityMask = synced ? FadeMask : null;
                if (M.Lyrics != null)
                {
                    for (int i = 0; i < M.Lyrics.Count; i++)
                    {
                        var l = M.Lyrics[i];
                        bool gap = synced && l.Text == "♪";
                        var tb = new TextBlock
                        {
                            FontSize = gap ? size - 3 : size, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap,
                            Foreground = synced ? new SolidColorBrush(Dim) : UI.Hex("#D0FFFFFF"),
                            TextAlignment = FocusMode ? TextAlignment.Center : TextAlignment.Left,
                            Tag = "no-translate",   // строчки песни — не интерфейс, переводчик интерфейса их не трогает
                        };
                        List<(Run, SolidColorBrush, LyricWord)> runs = null;
                        if (gap) tb.Text = "●  ●  ●";   // проигрыш: точки заливаются, пока не начнут петь
                        else if (synced && S.LyricsWords && l.Words is { Count: > 0 })
                        {
                            runs = new();
                            foreach (var w in l.Words)
                            {
                                var br = new SolidColorBrush(Dim);
                                var run = new Run(w.Text) { Foreground = br };
                                tb.Inlines.Add(run);
                                runs.Add((run, br, w));
                            }
                        }
                        else tb.Text = l.Text;
                        _wordRuns.Add(runs);
                        FrameworkElement row = tb;
                        var tr = M.Translation != null && i < M.Translation.Count ? M.Translation[i] : null;
                        if (!string.IsNullOrEmpty(tr))
                        {
                            row = UI.Col(tb, new TextBlock
                            {
                                Text = tr, FontSize = Math.Max(10, size - 3.5), Foreground = UI.Hex("#80FFFFFF"), TextWrapping = TextWrapping.Wrap,
                                TextAlignment = FocusMode ? TextAlignment.Center : TextAlignment.Left, Margin = new Thickness(0, 1, 0, 0),
                            });
                        }
                        row.Margin = new Thickness(0, 3, 0, 3);
                        row.RenderTransform = new ScaleTransform(1, 1);
                        row.RenderTransformOrigin = FocusMode ? new Point(0.5, 0.5) : new Point(0, 0.5);
                        if (synced && M.CanSeek)
                        {
                            var t = l.Time - TimeSpan.FromSeconds(M.LyricsOffset);
                            row.Cursor = Cursors.Hand;
                            row.ToolTip = I18n.T("Перемотать сюда");
                            row.MouseLeftButtonUp += (_, e) => { M.Seek(t < TimeSpan.Zero ? TimeSpan.Zero : t); e.Handled = true; };
                        }
                        _lines.Children.Add(row);
                        _lineBlocks.Add(tb);
                        _lineRows.Add(row);
                    }
                }
            }
            _lyricsStatus.Text = M.LyricsLoading ? "Ищу текст песни…"
                : M.LyricsInstrumental ? "Инструментальная композиция — без слов ♪"
                : M.Lyrics == null ? "Текст для этого трека не найден.\nНажми 🔍 сверху, чтобы поискать вручную." : "";
            _lyricsStatus.Show(_lyricsStatus.Text != "");
            if (_wordRuns.Any(r => r != null)) _wordTimer.Start(); else _wordTimer.Stop();
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => Highlight(false)));
        }

        // ---------- пословная подсветка ----------
        readonly List<List<(Run Run, SolidColorBrush Brush, LyricWord Word)>> _wordRuns = new();
        readonly DispatcherTimer _wordTimer = new() { Interval = TimeSpan.FromMilliseconds(40) };
        readonly Grid _linesHost = new() { ClipToBounds = true };
        int _activeRow = -1;
        static readonly Color Past = Color.FromArgb(0x45, 0xFF, 0xFF, 0xFF);

        /// <summary>Строки плавно растворяются у верхнего и нижнего края — как в Apple Music.</summary>
        static readonly LinearGradientBrush FadeMask = MakeFade();
        static LinearGradientBrush MakeFade()
        {
            var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            b.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0, 0, 0), 0));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(0xFF, 0, 0, 0), 0.12));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(0xFF, 0, 0, 0), 0.8));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(0x10, 0, 0, 0), 1));
            b.Freeze();
            return b;
        }

        static Color Mix(Color a, Color b, double t) => Color.FromArgb(
            (byte)(a.A + (b.A - a.A) * t), (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

        /// <summary>Каждое слово текущей строки загорается, когда его поют (если у текста есть пословная синхронизация).</summary>
        void OnWordTick()
        {
            if (!Visible || !M.LyricsSynced) { _wordTimer.Stop(); return; }
            int idx = M.LyricIndex;
            if (idx < 0 || idx >= _wordRuns.Count || _wordRuns[idx] is not { } runs) return;
            var pos = M.LyricsPosition + TimeSpan.FromMilliseconds(Math.Clamp(S.LyricsLeadMs, 0, 1000) / 3.0);
            foreach (var (_, br, w) in runs)
            {
                double p;
                if (!S.LyricsKaraoke) p = 1;
                else
                {
                    double len = Math.Max(0.08, (w.End - w.Start).TotalSeconds);
                    p = Math.Clamp((pos - w.Start).TotalSeconds / Math.Min(len, 0.9), 0, 1);
                }
                var c = Mix(Dim, Colors.White, p);
                if (br.Color != c) br.Color = c;
            }
        }

        void Highlight(bool animate)
        {
            if (!M.LyricsSynced || _lineBlocks.Count == 0 || !_lyricsPane.IsVisible) return;
            int idx = M.LyricIndex;
            for (int i = 0; i < _lineBlocks.Count; i++)
            {
                if (i == idx) continue;
                var c = i < idx ? Past : Dim;
                if (_wordRuns[i] is { } runs) foreach (var (_, br, _) in runs) br.Color = c;
                else _lineBlocks[i].Foreground = new SolidColorBrush(c);
            }
            // текущая строка чуть крупнее остальных
            if (_activeRow != idx)
            {
                if (_activeRow >= 0 && _activeRow < _lineRows.Count) Scale(_lineRows[_activeRow], 1, animate);
                if (idx >= 0 && idx < _lineRows.Count && M.Lyrics[idx].Text != "♪") Scale(_lineRows[idx], FocusMode ? 1.03 : 1.045, animate);
                _activeRow = idx;
            }
            Karaoke(false);
            if (idx < 0 || idx >= _lineRows.Count || DateTime.Now < _manualScrollUntil) return;
            var row = _lineRows[idx];
            double y = row.TranslatePoint(new Point(0, 0), _lines).Y;
            double anchor = FocusMode ? Math.Max(20, _lyricsBody.ActualHeight / 2 - row.ActualHeight / 2) : 30;
            double target = Math.Min(0, -(y - anchor));
            if (animate)
                _shift.BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(target, TimeSpan.FromMilliseconds(520)) { EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseOut } });
            else
            {
                _shift.BeginAnimation(TranslateTransform.YProperty, null);
                _shift.Y = target;
            }
        }

        static void Scale(FrameworkElement row, double to, bool animate)
        {
            if (row.RenderTransform is not ScaleTransform st) return;
            var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(animate ? 320 : 0)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            st.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }

        /// <summary>Караоке: текущая строка плавно заливается белым до конца строки (по словам — см. OnWordTick).</summary>
        void Karaoke(bool resync)
        {
            int idx = M.LyricIndex;
            if (idx < 0 || idx >= _lineBlocks.Count) return;
            var tb = _lineBlocks[idx];
            if (_wordRuns[idx] != null) { OnWordTick(); if (!_wordTimer.IsEnabled && Visible) _wordTimer.Start(); return; }
            if (!S.LyricsKaraoke || !M.LyricsSynced) { tb.Foreground = Brushes.White; return; }
            if (idx != _karaokeLine || resync || _k1 == null)
            {
                _karaokeLine = idx;
                _k1 = new GradientStop(Colors.White, 0);
                _k2 = new GradientStop(Color.FromArgb(0xA0, 0xFF, 0xFF, 0xFF), 0);
                tb.Foreground = new LinearGradientBrush(new GradientStopCollection { _k1, _k2 }, new Point(0, 0), new Point(1, 0));
            }
            var (p, left) = M.LineProgress();
            _k1.BeginAnimation(GradientStop.OffsetProperty, null);
            _k2.BeginAnimation(GradientStop.OffsetProperty, null);
            _k1.Offset = _k2.Offset = p;
            if (M.IsPlaying && left.TotalSeconds > 0.05)
            {
                var a = new DoubleAnimation(p, 1, left);
                _k1.BeginAnimation(GradientStop.OffsetProperty, a);
                _k2.BeginAnimation(GradientStop.OffsetProperty, a);
            }
        }

        // ---------- выбор другого текста ----------
        void ShowPicker()
        {
            _picker.Visibility = Visibility.Visible;
            _pickQuery.Text = M.TrackLine;
            LoadCandidates(null);
        }

        void HidePicker() => _picker.Visibility = Visibility.Collapsed;

        async void LoadCandidates(string query)
        {
            _pickList.Children.Clear();
            _pickList.Children.Add(UI.Text("Ищу варианты в LRCLIB, NetEase и Kugou…", 11.5, false, UI.Dim));
            List<LyricsCandidate> list;
            var forTrack = M.TrackKeyForUi;
            try
            {
                list = await LyricsService.Candidates(M.Title, M.Artist, M.CanSeek ? M.Duration : TimeSpan.Zero,
                    string.IsNullOrWhiteSpace(query) || query == M.TrackLine ? null : query.Trim(), M.FromBrowser);
            }
            catch (Exception e) { _pickList.Children.Clear(); _pickList.Children.Add(UI.Text("Ошибка: " + e.Message, 11.5, false, UI.Dim)); return; }
            if (forTrack != M.TrackKeyForUi) return;   // пока искали, трек сменился
            _pickList.Children.Clear();
            if (list.Count == 0) _pickList.Children.Add(UI.Text("Ничего не нашлось — попробуй другой запрос", 11.5, false, UI.Dim));
            foreach (var c in list)
            {
                var cand = c;
                var dur = c.Duration > 0 ? $"{(int)c.Duration / 60}:{(int)c.Duration % 60:00}" : "";
                double diff = M.CanSeek && c.Duration > 0 ? Math.Abs(c.Duration - M.Duration.TotalSeconds) : -1;
                var info = UI.Text(string.Join(" · ", new[] { c.Source, c.Album, dur, c.Synced ? "синхронный ✓" : "без синхронизации",
                    diff >= 0 && diff < 3 ? "длина совпадает" : diff >= 10 ? "другая длина" : null }.Where(x => !string.IsNullOrEmpty(x))), 10.5, false, UI.Dim);
                var row = new Border
                {
                    CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 0, 4, 3), Cursor = Cursors.Hand,
                    Background = UI.Res("CardBrush"),
                    Child = UI.Col(new TextBlock { Text = $"{c.Artist} — {c.Track}", FontSize = 12, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Tag = "no-translate" }, info),
                };
                bool busy = false;
                row.MouseLeftButtonUp += async (_, __) =>
                {
                    if (busy) return;
                    busy = true;
                    info.Text = I18n.T("Загружаю текст…");
                    LyricsResult r = null;
                    try { r = await cand.Load(); } catch (Exception e) { Log.Info("lyrics pick: " + e.Message); }
                    busy = false;
                    if (forTrack != M.TrackKeyForUi) return;
                    if (r == null || r.Lines == null || r.Lines.Count == 0) { info.Text = I18n.T("Не удалось загрузить этот вариант — выбери другой"); return; }
                    M.SetLyrics(r);
                    HidePicker();
                    App.Island.Toast("", "Текст выбран", $"{cand.Artist} — {cand.Track}");
                };
                _pickList.Children.Add(row);
            }
        }

        // ---------- история и избранное ----------
        void BuildHistory()
        {
            var tabs = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var (fav, name) in new[] { (false, "История"), (true, "Избранное ♥") })
            {
                var f = fav;
                var rb = new RadioButton { Style = UI.St("Segment"), Content = name, GroupName = "mhist", IsChecked = !fav, FontSize = 11 };
                rb.Checked += (_, __) => { _histFav = f; RenderHistory(); };
                tabs.Children.Add(rb);
            }
            var head = UI.Grid("a,*,a").Add(
                Small("", () => _history.Visibility = Visibility.Collapsed, "Назад", 11, 26),
                new Border { Background = UI.Hex("#22FFFFFF"), CornerRadius = new CornerRadius(8), Padding = new Thickness(2), Child = tabs, HorizontalAlignment = HorizontalAlignment.Left }.At(0, 1).M(6, 0, 0, 0),
                Small("", () =>
                {
                    if (_histFav) Store.Data.Favorites.Clear(); else Store.Data.Played.Clear();
                    Store.SaveData();
                    RenderHistory();
                }, "Очистить список", 11, 26).At(0, 2));
            _history.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _history.RowDefinitions.Add(new RowDefinition());
            _history.Background = UI.Hex("#01000000");
            _history.Add(head, new ScrollViewer { Content = _histList, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 4, 0, 0) }.At(1, 0));
            _history.Margin = new Thickness(4, 0, 4, 0);
        }

        void ShowHistory(bool fav)
        {
            _histFav = fav;
            _history.Visibility = Visibility.Visible;
            RenderHistory();
        }

        void RenderHistory()
        {
            _histList.Children.Clear();
            var list = _histFav ? Store.Data.Favorites : Store.Data.Played;
            if (list.Count == 0)
                _histList.Children.Add(UI.Text(_histFav ? "Нажми ♥ в плеере, чтобы добавить трек в избранное" : "Здесь появятся треки и видео, которые ты слушаешь", 12, false, UI.Dim).M(4, 6, 0, 0));
            foreach (var it in list.Take(60))
            {
                var item = it;
                var row = UI.Grid("*,a,a,a").M(0, 1, 4, 1).Add(
                    UI.Col(UI.Text(it.Title, 12, true), UI.Text($"{it.Artist}{(string.IsNullOrEmpty(it.Artist) ? "" : " · ")}{it.App} · {Ago.Of(it.Time)}", 10.5, false, UI.Dim)),
                    Small("", () => SystemService.Run("https://www.youtube.com/results?search_query=" + Uri.EscapeDataString(item.Line)), "Найти на YouTube", 11, 24).At(0, 1),
                    Small("", () => { ClipboardService.I.CopyBack(item.Line); App.Island.Toast("", "Скопировано", item.Line); }, "Скопировать", 11, 24).At(0, 2),
                    Small("", () => { list.Remove(item); Store.SaveData(); RenderHistory(); }, "Убрать", 9, 24).At(0, 3));
                _histList.Children.Add(new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 3, 4, 3), Margin = new Thickness(0, 0, 0, 3), Background = UI.Res("CardBrush"), Child = row });
            }
        }
    }
}
