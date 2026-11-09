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
        readonly Button _play, _prev, _next, _lyricsBtn, _volBtn, _source, _shuffle, _repeat, _back10, _fwd10, _rate, _fav, _vizBtn, _sleep, _ab;

        // визуализатор в колонке справа
        readonly Grid _vizPane = new();
        readonly NIsland.Controls.Visualizer _viz = new() { Fps = 40 };
        readonly TextBlock _vizName = UI.Text("", 11, true);
        /// <summary>Текущая строка песни поверх визуализатора — текст не теряется, когда колонка занята визуализатором.</summary>
        readonly TextBlock _vizLyric = new()
        {
            FontSize = 14, FontWeight = FontWeights.Bold, Foreground = Brushes.White, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(10, 0, 10, 0),
            IsHitTestVisible = false, Tag = "no-translate",
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 10, ShadowDepth = 0, Opacity = 0.95, Color = Colors.Black },
        };
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

        bool VisualOn => S.MediaVisual && M.HasSession;
        bool LyricsOn => S.LyricsOn && M.HasSession && !S.MediaVisual;
        bool FocusMode => LyricsOn && S.LyricsFocus;
        public override double ExtraWidth => VisualOn || (LyricsOn && !FocusMode) ? LyricsW : 0;

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
            _back10 = Label("−10", () => M.SeekStep(-1), "Перемотать назад (шаг — в настройках «Музыка»)");
            _fwd10 = Label("+10", () => M.SeekStep(1), "Перемотать вперёд (шаг — в настройках «Музыка»)");
            _ab = Label("A–B", () =>
            {
                var r = M.CycleLoop();
                App.Island.Toast("\uE8EE", r == "A" ? "Начало фрагмента отмечено" : r == "AB" ? "Фрагмент повторяется" : "Повтор фрагмента выключен",
                    r == "A" ? "Нажми ещё раз в конце фрагмента" : null);
                Refresh();
            }, "Повтор фрагмента: 1-е нажатие — начало, 2-е — конец, 3-е — выключить");
            _rate = UI.Pill("1×", M.CycleRate, false, "Скорость воспроизведения");
            _rate.Padding = new Thickness(8, 2, 8, 2);
            _rate.FontSize = 10.5;
            _fav = Small("", () =>
            {
                bool on = M.ToggleFavorite();
                if (on) App.Island?.MascotLove();
                App.Island.Toast(on ? "" : "", on ? "Добавлено в избранное" : "Убрано из избранного", M.TrackLine);
            }, "В избранное", 12, 26);

            _vizBtn = UI.IconBtn("\uE9D9", ToggleVisual, 13, 30, "Визуализатор под музыку");
            _sleep = Label("", CycleSleep, "Таймер сна: остановить музыку через 15, 30, 60 минут или после этого трека");
            var extras = UI.Row(
                _rate.M(0, 0, 2, 0),
                _sleep,
                _ab,
                _fav,
                Small("", () =>
                {
                    ClipboardService.I.CopyBack(M.TrackLine);
                    App.Island.Toast("", "Название скопировано", M.TrackLine);
                }, "Скопировать «Исполнитель — Название»", 12, 26),
                Small("", () => SystemService.Run("https://www.google.com/search?q=" + Uri.EscapeDataString(M.TrackLine)), "Найти трек в интернете", 12, 26),
                Small("", () => ShowHistory(false), "История и избранное", 12, 26),
                Small("", () => { if (S.PgEq) App.Island.Expand("eq"); else App.ShowSettings("eq"); }, "Эквалайзер", 12, 26),
                Small("", () => App.Island.Expand("sound"), "Микшер и устройство вывода", 12, 26));
            extras.VerticalAlignment = VerticalAlignment.Top;

            var top = UI.Grid("a,*,a").Add(_art, UI.Col(_title, _artist).At(0, 1).M(14, 0, 4, 0), extras.At(0, 2));
            var prog = UI.Grid("a,*,a").M(0, 8, 0, 0).Add(_pos, _seek.At(0, 1).M(8, 0, 8, 0), _rem.At(0, 2));
            var ctrl = UI.Grid("*,a,*").M(0, 2, 0, 0).Add(
                UI.Row(_lyricsBtn, _vizBtn, _source.M(4, 0, 0, 0)),
                UI.Row(_shuffle, _back10, _prev, _play.M(4, 0, 4, 0), _next, _fwd10, _repeat).At(0, 1),
                UI.Row(_volBtn, _vol.M(2, 0, 0, 0)).At(0, 2).HA(HorizontalAlignment.Right));
            _main = UI.Grid(null, "a,a,a").M(6, 0, 4, 0).VA(VerticalAlignment.Center);
            _main.Add(top, prog.At(1, 0), ctrl.At(2, 0));

            // ----- панель текста -----
            // прокрутка строк — отступом, а не RenderTransform: при прозрачности острова < 100 % WPF рисует его в общий слой,
            // и сдвинутый трансформацией блок внутри обрезки пропадал целиком. Анимации по-прежнему идут по _shift.Y.
            _shift.Changed += (_, __) => _lines.Margin = new Thickness(0, _shift.Y, 0, 0);
            _lyricsStatus.Foreground = UI.Dim;
            _lyricsStatus.TextWrapping = TextWrapping.Wrap;
            _lyrInfo.Foreground = UI.Faint;
            _trBtn = Small("", () => { S.LyricsTranslate = !S.LyricsTranslate; _trClicked = S.LyricsTranslate; RenderLyricsTools(); }, "Перевод текста песни · правый клик — выбрать язык");
            _trBtn.MouseRightButtonUp += (_, e) => { ShowLangPick(); e.Handled = true; };
            _focusBtn = Small("", () => { S.LyricsFocus = !S.LyricsFocus; ApplyLayout(); Relayout(); }, "Только текст (крупно)");
            _focusPlay = Small("", M.TogglePlay, "Пауза / воспроизведение", 12, 26);
            var tools = UI.Row(
                Label("−0.5", () => { M.AdjustOffset(-0.5); RenderLyricsTools(); }, "Текст спешит — сдвинуть назад на 0.5 с"),
                Label("+0.5", () => { M.AdjustOffset(0.5); RenderLyricsTools(); }, "Текст опаздывает — сдвинуть вперёд на 0.5 с"),
                Small("", () => { M.ResetOffset(); RenderLyricsTools(); }, "Сбросить сдвиг"),
                _trBtn,
                Small("", ShowPicker, "Не тот текст? Выбрать другой"),
                Small("", CopyLyrics, "Скопировать текст песни (правый клик — сохранить .lrc в «Музыка\\Lyrics»)"),
                StyleBtn(),
                Small("", () => { DesktopLyricsWindow.Toggle(); App.Island.Toast("", S.DesktopLyrics ? "Текст на рабочем столе" : "Текст убран с рабочего стола", S.DesktopLyrics ? "Закрепить и настроить — в настройках «Музыка»" : null); }, "Текст песни на рабочем столе — строка поверх всех окон"),
                _focusBtn);
            // правый клик по «копировать» — сохранить файл
            ((Button)tools.Children[tools.Children.Count - 4]).MouseRightButtonUp += (_, e) => { SaveLyrics(); e.Handled = true; };
            foreach (Button b in tools.Children) { b.Width = 26; b.Height = 24; }
            var lyrHead = UI.Grid("a,*,a").Add(
                UI.Row(_focusPlay, _focusTitle.M(4, 0, 0, 0)),
                _lyrInfo.At(0, 1).M(4, 0, 4, 0),
                tools.At(0, 2));
            _linesHost.Children.Add(_lines);
            _linesHost.SizeChanged += (_, e) => FitFade(e.NewSize.Height);

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
            // выбор языка — поверх всей панели текста (вместе с шапкой), иначе 13 языков не влезают
            _lyricsPane.Children.Add(_langPick.M(12, 0, 4, 2));
            Grid.SetRowSpan(_langPick, 2);
            Panel.SetZIndex(_langPick, 5);

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

            // ----- визуализатор -----
            var vizBar = UI.Row(
                Small("\uE76B", () => StepViz(-1), "Предыдущий стиль", 10, 22),
                _vizName,
                Small("\uE76C", () => StepViz(1), "Следующий стиль", 10, 22),
                Small("\uE790", NextVizPalette, "Палитра", 11, 22),
                Small("\uE740", () => VisualizerWindow.Open(), "Открыть на весь экран", 11, 22));
            _vizName.MinWidth = 96;
            _vizName.TextAlignment = TextAlignment.Center;
            vizBar.HorizontalAlignment = HorizontalAlignment.Center;
            vizBar.VerticalAlignment = VerticalAlignment.Bottom;
            vizBar.Opacity = 0;
            _viz.Cursor = Cursors.Hand;
            _viz.ToolTip = I18n.T("Колесо — сменить стиль, двойной клик — на весь экран");
            _viz.MouseWheel += (_, e) => { StepViz(e.Delta > 0 ? -1 : 1); e.Handled = true; };
            _viz.MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2) { VisualizerWindow.Open(); e.Handled = true; } };
            _vizPane.MouseEnter += (_, __) => vizBar.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)));
            _vizPane.MouseLeave += (_, __) => vizBar.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(400)));
            _vizPane.Add(new Border { CornerRadius = new CornerRadius(12), ClipToBounds = true, Child = _viz }, _vizLyric, vizBar);
            _vizPane.Visibility = Visibility.Collapsed;

            _player = new Grid();
            _player.ColumnDefinitions.Add(_mainCol);
            _player.ColumnDefinitions.Add(_lyricsCol);
            _player.Add(_main, _lyricsPane.At(0, 1).M(8, 0, 4, 0), _vizPane.At(0, 1).M(10, 0, 4, 2));

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
            // поменяли вид текста в настройках — перестраиваем строки
            S.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(Settings.LyricsStyle) or nameof(Settings.LyricsColor) or nameof(Settings.LyricsFont) or nameof(Settings.LyricsAlign)
                    or nameof(Settings.LyricsRoman) or nameof(Settings.LyricsFontSize) or nameof(Settings.LyricsUpper) or nameof(Settings.LyricsLineGap)
                    or nameof(Settings.LyricsActiveScale) or nameof(Settings.LyricsShadow))
                { _linesDirty = true; if (Visible) Dispatcher.BeginInvoke(new Action(RefreshLyrics)); _vizLyric.Text = ""; Dispatcher.BeginInvoke(new Action(UpdateVizLyric)); }
                // текст на рабочем столе — тот же вид, что и в плеере
                if (e.PropertyName is nameof(Settings.LyricsColor) or nameof(Settings.LyricsFont) or nameof(Settings.LyricsKaraoke) or nameof(Settings.LyricsUpper)
                    or nameof(Settings.LyricsShadow) or nameof(Settings.LyricsAnim)) DesktopLyricsWindow.Apply();
            };
            M.LyricIndexChanged += () => { if (Visible) { Highlight(true); UpdateVizLyric(); } };
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
            _vizPane.Show(false);   // визуализатор перестаёт слушать звук, пока чёлка свёрнута
            _artBrush.ImageSource = null; // обложку держит сервис, страница — нет
            _wordTimer.Stop();
            _k1?.BeginAnimation(GradientStop.OffsetProperty, null);
            _k2?.BeginAnimation(GradientStop.OffsetProperty, null);
        }

        void ToggleLyrics()
        {
            // кнопка «текст» при открытом визуализаторе — переключает колонку на текст
            if (S.MediaVisual) { S.MediaVisual = false; S.LyricsOn = true; }
            else S.LyricsOn = !S.LyricsOn;
            RefreshLyrics();
            Relayout();
        }

        void ToggleVisual()
        {
            S.MediaVisual = !S.MediaVisual;
            RefreshLyrics();
            Relayout();
        }

        void StepViz(int dir)
        {
            S.VizStyle = NIsland.Controls.Visualizer.Next(S.VizStyle, dir);
            SyncViz();
        }

        void NextVizPalette()
        {
            var all = NIsland.Controls.Visualizer.Palettes;
            int i = Array.FindIndex(all, p => p.Key == S.VizPalette);
            S.VizPalette = all[(i + 1) % all.Length].Key;
            SyncViz();
            App.Island.Toast("\uE790", "Палитра: " + all[(i + 1) % all.Length].Name, null);
        }

        void SyncViz()
        {
            _viz.VisualStyle = S.VizStyle;
            _viz.Palette = S.VizPalette;
            _viz.BaseColor = M.Art != null ? M.ArtColor : (UI.Res("AccentBrush") as SolidColorBrush)?.Color ?? Colors.DodgerBlue;
            _vizName.Text = I18n.T(NIsland.Controls.Visualizer.NameOf(S.VizStyle));
        }

        void UpdateVizLyric()
        {
            if (!VisualOn) return;
            LyricsAnim.ShowLine(_vizLyric, M.CurrentLyric, centered: true);
        }

        static readonly int[] SleepSteps = { 0, 15, 30, 60, -1 };

        void CycleSleep()
        {
            var t = TimerService.I;
            int cur = t.SleepAfterTrack ? -1 : t.SleepAt is DateTime d ? (int)Math.Round((d - DateTime.Now).TotalMinutes) : 0;
            int i = Array.FindIndex(SleepSteps, s => s == cur || (s > 0 && cur > 0 && cur <= s));
            int next = SleepSteps[(i + 1) % SleepSteps.Length];
            if (next == -1) t.SetSleepAfterTrack(true);
            else t.SetSleep(next);
            App.Island.Toast("\uE708", next == 0 ? "Таймер сна выключен" : next == -1 ? "Музыка остановится после этого трека" : $"Музыка остановится через {next} мин", null);
            UpdateSleep();
        }

        void UpdateSleep()
        {
            var t = TimerService.I;
            bool on = t.SleepAfterTrack || t.SleepAt != null;
            ((TextBlock)_sleep.Content).Text = t.SleepAfterTrack ? "⏾ ♪" : t.SleepAt is DateTime d ? "⏾ " + Math.Max(1, (int)Math.Ceiling((d - DateTime.Now).TotalMinutes)) : "⏾";
            _sleep.Foreground = on ? UI.Res("AccentBrush") : Brushes.White;
        }

        void ApplyLayout()
        {
            bool on = LyricsOn, focus = FocusMode, viz = VisualOn;
            _mainCol.Width = focus ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
            _main.Show(!focus);
            _lyricsCol.Width = viz ? new GridLength(LyricsW) : !on ? new GridLength(0) : focus ? new GridLength(1, GridUnitType.Star) : new GridLength(LyricsW);
            _lyricsPane.Show(on);
            _vizPane.Show(viz && Visible);
            _vizBtn.Foreground = viz ? UI.Res("AccentBrush") : Brushes.White;
            if (viz) { SyncViz(); UpdateVizLyric(); }
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
            _artist.Text = string.Join(" — ", new[] { M.Artist, S.PlayerShowAlbum ? M.Album : null }.Where(s => !string.IsNullOrWhiteSpace(s)))
                + (S.PlayerPlayCount && M.PlayCount is int pc && pc > 1 ? $"  · ×{pc}" : "");
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
            _back10.Content = "−" + MediaService.Step;
            _fwd10.Content = "+" + MediaService.Step;
            _ab.Show(S.PlayerAbButton && M.CanSeek);
            _ab.Content = M.LoopB != null ? "A–B ✓" : M.LoopA != null ? "A…" : "A–B";
            _ab.Foreground = M.LoopA != null ? (Brush)UI.Res("AccentBrush") ?? Brushes.White : Brushes.White;
            bool fav = M.IsFavorite;
            _fav.Content = fav ? "" : "";
            _fav.Foreground = fav ? UI.Hex("#FF375F") : Brushes.White;
            if (S.ProgressArtColor) _seek.Foreground = new SolidColorBrush(M.ArtColor); else _seek.ClearValue(ForegroundProperty);
            _glowStop.Color = ColorUtil.WithAlpha(M.ArtColor, 0.55);
            _focusTitle.Text = M.TrackLine;
            UpdatePlay();
            UpdatePos();
            UpdateVol();
            UpdateSleep();
            if (VisualOn) SyncViz();
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
            var shift = Math.Abs(off) > 0.01 ? $"{(off > 0 ? "+" : "")}{off:0.0} с" : "";
            var full = src + (shift != "" ? " · сдвиг " + shift : "") + (!M.HasTrackOffset && Math.Abs(M.AppOffset) > 0.01 ? " (" + I18n.T("для ") + M.AppName + ")" : "");
            // в шапке мало места рядом с кнопками: коротко — источник и сдвиг, полностью — в подсказке
            // в шапке ~50 px: только самое нужное (сдвиг и перевод), источник и подробности — в подсказке
            _lyrInfo.Text = M.LyricsLoading ? "…" : shift;
            if (S.LyricsTranslate && M.Lyrics != null)
            {
                var code = S.LyricsTranslateTo.Split('-')[0].ToUpperInvariant();
                var tr = M.TranslationLoading ? "→" + code + "…" : M.TranslationNote != null ? "⚠" + code : M.Translation != null ? "→" + code : "";
                if (tr != "") _lyrInfo.Text = string.IsNullOrEmpty(_lyrInfo.Text) ? tr : tr + " " + _lyrInfo.Text;
                if (M.TranslationNote != null) full += (full == "" ? "" : "\n") + M.TranslationNote;
                else if (M.Translation != null && M.TranslationVia != null) full += (full == "" ? "" : "\n") + I18n.T("Перевод: ") + LyricsTranslate.Name(S.LyricsTranslateTo) + " · " + M.TranslationVia;
            }
            _lyrInfo.ToolTip = full == "" ? null : full;
            // после нажатия — сказать, чем кончилось, если перевода не будет
            if (_trClicked && !M.TranslationLoading && (M.TranslationNote != null || M.Translation != null))
            {
                _trClicked = false;
                if (M.TranslationNote != null) App.Island.Toast("", "Перевод текста", M.TranslationNote);
            }
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
                var font = DesktopLyricsWindow.Font(S.LyricsFont);
                var align = FocusMode || S.LyricsAlign == "center" ? TextAlignment.Center : TextAlignment.Left;
                var weight = S.LyricsFont == "light" ? FontWeights.Light : S.LyricsStyle == "classic" ? FontWeights.Normal : FontWeights.SemiBold;
                _linesHost.OpacityMask = synced ? _fade : null;
                if (M.Lyrics != null)
                {
                    for (int i = 0; i < M.Lyrics.Count; i++)
                    {
                        var l = M.Lyrics[i];
                        bool gap = synced && l.Text == "♪";
                        var tb = new TextBlock
                        {
                            FontSize = gap ? size - 3 : size, FontWeight = weight, TextWrapping = TextWrapping.Wrap, FontFamily = font,
                            Foreground = synced ? new SolidColorBrush(Dim) : UI.Hex("#D0FFFFFF"),
                            TextAlignment = align,
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
                                var run = new Run(S.LyricsUpper ? w.Text.ToUpper(CultureInfo.CurrentCulture) : w.Text) { Foreground = br };
                                tb.Inlines.Add(run);
                                runs.Add((run, br, w));
                            }
                        }
                        else tb.Text = S.LyricsUpper ? l.Text.ToUpper(CultureInfo.CurrentCulture) : l.Text;
                        _wordRuns.Add(runs);
                        FrameworkElement row = tb;
                        var tr = M.Translation != null && i < M.Translation.Count ? M.Translation[i] : null;
                        var roman = S.LyricsRoman && !gap ? LyricsTools.Romanize(l.Text) : null;
                        if (!string.IsNullOrEmpty(tr) || roman != null)
                        {
                            var col = UI.Col(tb);
                            if (roman != null)
                                col.Children.Add(new TextBlock { Text = roman, FontSize = Math.Max(10, size - 4), FontStyle = FontStyles.Italic, Foreground = UI.Hex("#70FFFFFF"), TextWrapping = TextWrapping.Wrap, TextAlignment = align, Tag = "no-translate" });
                            if (!string.IsNullOrEmpty(tr))
                                col.Children.Add(new TextBlock
                                {
                                    Text = tr, FontSize = Math.Max(10, size - 3.5), Foreground = UI.Hex("#80FFFFFF"), TextWrapping = TextWrapping.Wrap,
                                    TextAlignment = align, Margin = new Thickness(0, 1, 0, 0), Tag = "no-translate",
                                });
                            row = col;
                        }
                        double lineGap = Math.Clamp(S.LyricsLineGap, 0, 30) / 2.0;
                        // запас справа: текущая строка увеличивается (до 30 %)
                        row.Margin = new Thickness(0, lineGap, Math.Ceiling(size * (0.4 + (Math.Clamp(S.LyricsActiveScale, 100, 130) - 100) / 100.0 * 9)), lineGap);
                        row.RenderTransform = new ScaleTransform(1, 1);
                        row.RenderTransformOrigin = align == TextAlignment.Center ? new Point(0.5, 0.5) : new Point(0, 0.5);
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
                    // новая песня — строки въезжают лесенкой (при перестройке из-за настроек — нет)
                    var key = M.Title + "|" + M.Artist;
                    if (S.LyricsCascade && key != _cascadeKey && Visible)
                        for (int i = 0; i < Math.Min(_lineBlocks.Count, 40); i++) LyricsAnim.Cascade(_lineBlocks[i], i, Math.Max(250, S.LyricsAnimMs));
                    _cascadeKey = key;
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
        readonly DispatcherTimer _wordTimer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        readonly Grid _linesHost = new() { ClipToBounds = true };
        int _activeRow = -1;
        static readonly Color Past = Color.FromArgb(0x45, 0xFF, 0xFF, 0xFF);

        /// <summary>Строки плавно растворяются у верхнего и нижнего края — как в Apple Music.</summary>
        // Маска в абсолютных координатах видимой панели: относительная считается от границ ВСЕХ строк
        // (с учётом сдвига прокрутки — тысячи пикселей), и видимая часть попадала в прозрачный край градиента.
        readonly LinearGradientBrush _fade = MakeFade();
        void FitFade(double h)
        {
            if (h <= 0) return;
            _fade.EndPoint = new Point(0, h);
            // затухание сверху — постоянные ~28 px, а не доля высоты
            _fade.GradientStops[1].Offset = Math.Clamp(28 / h, 0.02, 0.2);
            _fade.GradientStops[2].Offset = Math.Clamp(1 - 40 / h, 0.6, 0.97);
        }
        static LinearGradientBrush MakeFade()
        {
            var b = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(0, 0), EndPoint = new Point(0, 200) };
            b.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0, 0, 0), 0));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(0xFF, 0, 0, 0), 0.12));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(0xFF, 0, 0, 0), 0.8));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(0x10, 0, 0, 0), 1));
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
                var c = Mix(Dim, Hi, p);
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
            // текущая строка чуть крупнее остальных; дальше — по стилю
            var style = S.LyricsStyle ?? "apple";
            if (_activeRow != idx)
            {
                if (_activeRow >= 0 && _activeRow < _lineRows.Count) { Scale(_lineRows[_activeRow], 1, animate); LyricsAnim.Reset(_lineBlocks[_activeRow]); _lineBlocks[_activeRow].Effect = null; }
                if (idx >= 0 && idx < _lineRows.Count && M.Lyrics[idx].Text != "♪")
                {
                    // размер текущей строки — из настройки; «классика» и «минимал» без увеличения, «прыжок» чуть больше
                    double user = Math.Clamp(S.LyricsActiveScale, 100, 130) / 100.0;
                    double to = style switch { "classic" or "minimal" => 1.0, "bounce" => user + 0.05, _ => FocusMode ? 1 + (user - 1) * 0.7 : user };
                    if (style == "bounce" && animate) Bounce(_lineRows[idx], to); else Scale(_lineRows[idx], to, animate);
                    var tb = _lineBlocks[idx];
                    if (style == "neon") tb.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Hi, BlurRadius = 16, ShadowDepth = 0, Opacity = 0.95 };
                    else if (S.LyricsShadow) tb.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.Black, BlurRadius = 10, ShadowDepth = 1.5, Opacity = 0.85, RenderingBias = System.Windows.Media.Effects.RenderingBias.Performance };
                    // появление строки: у «неона» своя подсветка, поэтому «вспышку» там не накладываем
                    var anim = S.LyricsAnim ?? "rise";
                    if (animate && !(style == "neon" && anim == "glow"))
                    {
                        LyricsAnim.Play(tb, anim, S.LyricsAnimMs, Hi, FocusMode || S.LyricsAlign == "center");
                    }
                }
                _activeRow = idx;
            }
            // Apple — дальние строки бледнеют; минимал — видны только текущая и соседние
            for (int i = 0; i < _lineRows.Count; i++)
            {
                int d = idx < 0 ? i : Math.Abs(i - idx);
                double op = style switch
                {
                    "apple" => Math.Max(0.28, 1 - d * 0.16),
                    "minimal" => d == 0 ? 1 : d == 1 ? 0.45 : 0,
                    "neon" => Math.Max(0.35, 1 - d * 0.12),
                    _ => 1,
                };
                if (Math.Abs(_lineRows[i].Opacity - op) > 0.01)
                {
                    if (animate) _lineRows[i].BeginAnimation(OpacityProperty, new DoubleAnimation(op, TimeSpan.FromMilliseconds(380)));
                    else { _lineRows[i].BeginAnimation(OpacityProperty, null); _lineRows[i].Opacity = op; }
                }
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

        /// <summary>Строка «подпрыгивает»: с упругим перелётом.</summary>
        static void Bounce(FrameworkElement row, double to)
        {
            if (row.RenderTransform is not ScaleTransform st) return;
            var a = new DoubleAnimation(0.9, to, TimeSpan.FromMilliseconds(520)) { EasingFunction = new ElasticEase { Oscillations = 1, Springiness = 4, EasingMode = EasingMode.EaseOut } };
            st.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }

        /// <summary>Цвет текущей строки: белый, акцент или из обложки.</summary>
        static Color Hi => DesktopLyricsWindow.Highlight();

        static readonly string[] LyricStyles = { "apple", "classic", "neon", "minimal", "bounce" };
        static readonly string[] LyricStyleNames = { "Apple — плавно", "Классика", "Неон", "Минимал", "Прыжок" };

        /// <summary>Кнопка стиля: клик — следующий стиль, правый клик — следующая анимация появления строк.</summary>
        Button StyleBtn()
        {
            var b = Small("", CycleLyricsStyle, "Стиль текста: Apple, классика, неон, минимал, прыжок · правый клик — анимация появления строк");
            b.MouseRightButtonUp += (_, e) =>
            {
                var kinds = LyricsAnim.Kinds;
                int i = Array.FindIndex(kinds, k => k.Key == S.LyricsAnim);
                var next = kinds[(i + 1) % kinds.Length];
                S.LyricsAnim = next.Key;
                App.Island.Toast("", "Появление строки", next.Name);
                // сразу показать на текущей строке
                int idx = M.LyricIndex;
                if (idx >= 0 && idx < _lineBlocks.Count) LyricsAnim.Play(_lineBlocks[idx], next.Key, S.LyricsAnimMs, Hi, FocusMode || S.LyricsAlign == "center");
                e.Handled = true;
            };
            return b;
        }

        void CycleLyricsStyle()
        {
            int i = Array.IndexOf(LyricStyles, S.LyricsStyle);
            S.LyricsStyle = LyricStyles[(i + 1) % LyricStyles.Length];
            _linesDirty = true;
            RefreshLyrics();
            App.Island.Toast("", "Стиль текста", LyricStyleNames[Array.IndexOf(LyricStyles, S.LyricsStyle)]);
        }

        void CopyLyrics()
        {
            if (M.Lyrics == null || M.Lyrics.Count == 0) { App.Island.Toast("", "Текста нет", null); return; }
            ClipboardService.I.CopyBack(M.TrackLine + Environment.NewLine + Environment.NewLine + LyricsTools.PlainText(M.Lyrics));
            App.Island.Toast("", "Текст песни скопирован", M.TrackLine);
        }

        void SaveLyrics()
        {
            if (M.Lyrics == null || M.Lyrics.Count == 0) { App.Island.Toast("", "Текста нет", null); return; }
            try
            {
                var path = LyricsTools.SaveLrc(M.Lyrics, M.Title, M.Artist, M.LyricsSynced);
                App.Island.Toast("", "Текст сохранён", System.IO.Path.GetFileName(path));
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"")?.Dispose();
            }
            catch (Exception e) { App.Island.Toast("", "Не удалось сохранить", e.Message); }
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
            if (!S.LyricsKaraoke || !M.LyricsSynced) { tb.Foreground = new SolidColorBrush(Hi); return; }
            if (idx != _karaokeLine || resync || _k1 == null)
            {
                _karaokeLine = idx;
                _k1 = new GradientStop(Hi, 0);
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

        // ---------- язык перевода ----------
        readonly Border _langPick = new() { Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Top, CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 8, 6, 8) };
        bool _trClicked;
        string _cascadeKey;

        void ShowLangPick()
        {
            _langPick.Background = UI.Hex("#F2161616");
            var wrap = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            foreach (var (code, name) in LyricsTranslate.Langs)
            {
                var c = code;
                // название языка — на своём языке, переводчик интерфейса его не трогает
                var b = UI.Pill(new TextBlock { Text = name, FontSize = 11.5, Tag = "no-translate" }, () =>
                {
                    _langPick.Visibility = Visibility.Collapsed;
                    _trClicked = true;
                    if (S.LyricsTranslateTo == c && S.LyricsTranslate) _ = M.LoadTranslation();   // тот же язык — просто повторить
                    S.LyricsTranslateTo = c;
                    S.LyricsTranslate = true;
                    RenderLyricsTools();
                }, S.LyricsTranslate && S.LyricsTranslateTo == c);
                // компактные чипы: 13 языков должны влезть в панель текста высотой ~130 px
                b.Height = 24; b.MinHeight = 0; b.Padding = new Thickness(9, 0, 9, 0);
                b.Margin = new Thickness(0, 0, 5, 5);
                wrap.Children.Add(b);
            }
            var off = S.LyricsTranslate ? Small("", () => { _langPick.Visibility = Visibility.Collapsed; S.LyricsTranslate = false; RenderLyricsTools(); }, "Выключить перевод") : null;
            var col = UI.Col(UI.Grid("*,a,a").Add(UI.Text(I18n.T("Перевести текст на…"), 12.5, true).VA(VerticalAlignment.Center), (UIElement)off?.At(0, 1) ?? new Border(),
                                               Small("", () => _langPick.Visibility = Visibility.Collapsed, "Закрыть").At(0, 2)), wrap);
            _langPick.Child = col;
            _langPick.Visibility = Visibility.Visible;
        }

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
