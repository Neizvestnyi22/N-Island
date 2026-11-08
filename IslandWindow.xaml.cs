using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
using NIsland.Core;
using NIsland.Pages;
using NIsland.Services;
using WinForms = System.Windows.Forms;

namespace NIsland
{
    /// <summary>Всплывашка в свёрнутой чёлке: громкость, яркость, батарея, трек, звонки, уведомления…</summary>
    public class Activity
    {
        public string Kind;
        public string Glyph;
        public string GlyphFont;
        public string Title;
        public string Sub;
        public ImageSource Image;
        public Color? Tint;
        public Color? Swatch;
        public double? Progress;
        public double Seconds = 2.5;
        public string ActionText;
        public Action Action;
        public Action OnClick;
        public string Page;
        public bool Wide;
    }

    /// <summary>Описание страницы; сам UserControl создаётся только при первом показе (меньше памяти).</summary>
    class PageInfo
    {
        public string Key, Title, Glyph, GlyphFont = "Segoe Fluent Icons, Segoe MDL2 Assets";
        public Func<bool> On;
        public Func<PageBase> Make;
        PageBase _inst;
        public PageBase Instance => _inst ??= Make();
        public bool Created => _inst != null;
    }

    public partial class IslandWindow : Window
    {
        enum Mode { Collapsed, Activity, Expanded }

        const double WinW = 1500, WinH = 300, FloatTop = 7, ExpandedH = 212, BubW = 128, BubGap = 10;
        static Settings S => Store.Settings;
        static StylePreset P => Presets.Current;
        static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");
        static readonly Color Green = Color.FromRgb(0x30, 0xD1, 0x58), Red = Color.FromRgb(0xFF, 0x45, 0x3A), Orange = Color.FromRgb(0xFF, 0x9F, 0x0A);

        Mode _mode = Mode.Collapsed;
        IntPtr _hwnd;
        double _dpi = 1;

        // пружина размера и плавные прозрачности
        double _w = 190, _h = 32, _vw, _vh, _tw = 190, _th = 32;
        double _opC = 1, _opA, _opE, _opB, _bgA = 1, _tbgA = 1;
        bool _ticking;
        TimeSpan _last;

        // кисти пресета
        readonly GradientStop _s1 = new(Colors.Black, 0), _s2 = new(Colors.Black, 1);
        readonly LinearGradientBrush _bg;
        readonly GradientStop _r1 = new(Colors.Transparent, 0), _r2 = new(Colors.Transparent, 1);
        readonly GradientStop _sh1 = new(Colors.Transparent, 0), _sh2 = new(Colors.Transparent, 1);
        Color _c1 = Colors.Black, _c2 = Colors.Black;

        bool _hover, _dragInside, _hiddenFs, _blur, _micMuted, _pinned;
        string _rgnKey;
        int _sec;
        bool _batInit, _lastCharging;
        int _lowStage;
        DateTime _wheelAt, _collapsedAt = DateTime.Now, _glassAt, _eyeAt = DateTime.Now, _standAt = DateTime.Now;
        readonly HashSet<string> _prayerDone = new();
        WinForms.Screen _screen;

        readonly List<PageInfo> _all;
        List<PageInfo> _pages = new();
        int _idx;

        readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
        readonly DispatcherTimer _openT = new() { Interval = TimeSpan.FromMilliseconds(130) };
        readonly DispatcherTimer _closeT = new() { Interval = TimeSpan.FromMilliseconds(420) };
        readonly DispatcherTimer _actT = new();

        Activity _act, _pending;

        /// <summary>Не сворачивать (идёт перетаскивание файла, открыт диалог…).</summary>
        public bool Hold { get; set; }

        public IslandWindow()
        {
            InitializeComponent();
            Width = WinW;
            Height = WinH;
            _bg = new LinearGradientBrush(new GradientStopCollection { _s1, _s2 }, new Point(0, 0), new Point(1, 1));
            Island.Background = _bg;
            Bubble.Background = _bg;
            EarL.Fill = _bg;
            EarR.Fill = _bg;
            var rim = new LinearGradientBrush(new GradientStopCollection { _r1, _r2 }, 90);
            Rim.BorderBrush = rim;
            BubRim.BorderBrush = rim;
            Sheen.Background = new LinearGradientBrush(new GradientStopCollection { _sh1, _sh2 }, 90);

            _all = new List<PageInfo>
            {
                new() { Key = "media", Title = "Музыка", Glyph = "", On = () => S.PgMedia, Make = () => new MediaPage() },
                new() { Key = "ai", Title = "ИИ", Glyph = "", On = () => S.PgAi, Make = () => new AiPage() },
                new() { Key = "sound", Title = "Звук", Glyph = "", On = () => S.PgSound, Make = () => new SoundPage() },
                new() { Key = "eq", Title = "Эквалайзер", Glyph = "", On = () => S.PgEq, Make = () => new EqPage() },
                new() { Key = "wifi", Title = "Wi-Fi", Glyph = "", On = () => S.PgWifi, Make = () => new WifiPage() },
                new() { Key = "vpn", Title = "VPN", Glyph = "", On = () => S.PgVpn, Make = () => new VpnPage() },
                new() { Key = "hub", Title = "Сервисы", Glyph = "", On = () => S.PgHub, Make = () => new HubPage() },
                new() { Key = "center", Title = "Уведомления", Glyph = "", On = () => S.PgCenter, Make = () => new CenterPage() },
                new() { Key = "modes", Title = "Режимы и стиль", Glyph = "", On = () => S.PgModes, Make = () => new ModesPage() },
                new() { Key = "calendar", Title = "Календарь", Glyph = "", On = () => S.PgCalendar, Make = () => new CalendarPage() },
                new() { Key = "todo", Title = "Задачи", Glyph = "", On = () => S.PgTodo, Make = () => new TodoPage() },
                new() { Key = "timer", Title = "Таймер", Glyph = "", On = () => S.PgTimer, Make = () => new TimerPage() },
                new() { Key = "pomodoro", Title = "Помодоро", Glyph = "", On = () => S.PgPomodoro, Make = () => new PomodoroPage() },
                new() { Key = "weather", Title = "Погода", Glyph = "", On = () => S.PgWeather, Make = () => new WeatherPage() },
                new() { Key = "water", Title = "Вода", Glyph = "💧", GlyphFont = "Segoe UI Emoji", On = () => S.PgWater, Make = () => new WaterPage() },
                new() { Key = "clipboard", Title = "Буфер обмена", Glyph = "", On = () => S.PgClipboard, Make = () => new ClipboardPage() },
                new() { Key = "notes", Title = "Заметки", Glyph = "", On = () => S.PgNotes, Make = () => new NotesPage() },
                new() { Key = "translate", Title = "Перевод", Glyph = "", On = () => S.PgTranslate, Make = () => new TranslatePage() },
                new() { Key = "tools", Title = "Инструменты", Glyph = "", On = () => S.PgTools, Make = () => new ToolsPage() },
                new() { Key = "maint", Title = "Обслуживание", Glyph = "", On = () => S.PgMaint, Make = () => new MaintPage() },
                new() { Key = "launcher", Title = "Запуск", Glyph = "", On = () => S.PgLauncher, Make = () => new LauncherPage() },
                new() { Key = "shelf", Title = "Полка", Glyph = "", On = () => S.PgShelf, Make = () => new ShelfPage() },
                new() { Key = "quick", Title = "Пульт", Glyph = "", On = () => S.PgQuick, Make = () => new QuickPage() },
                new() { Key = "prayer", Title = "Намаз", Glyph = "", On = () => S.PgPrayer, Make = () => new PrayerPage() },
            };
            BuildPages();

            SourceInitialized += (_, __) =>
            {
                _hwnd = new WindowInteropHelper(this).Handle;
                Native.MakeToolWindow(_hwnd, true);
                _dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
                var src = HwndSource.FromHwnd(_hwnd);
                src.CompositionTarget.BackgroundColor = Colors.Transparent;
                src.AddHook(WndProc);
                Native.MakeGlassSheet(_hwnd);
                RegisterHotkey();
                ClipboardService.I.Start(_hwnd);
                Reposition();
                ApplyStyle();
                OnClock();
                UpdateCollapsed();
                Retarget(true);
            };
            SystemEvents.DisplaySettingsChanged += (_, __) => Dispatcher.BeginInvoke(new Action(() =>
            {
                _dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
                Reposition();
            }));
            // после сна мониторы и Проводник могли «переехать» — через пару секунд приводим остров в порядок
            SystemEvents.PowerModeChanged += (_, e) =>
            {
                if (e.Mode != PowerModes.Resume) return;
                Dispatcher.BeginInvoke(new Action(async () => { await System.Threading.Tasks.Task.Delay(3000); Recover(); }));
            };
            SystemEvents.SessionSwitch += (_, e) =>
            {
                if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.ConsoleConnect or SessionSwitchReason.RemoteConnect)
                    Dispatcher.BeginInvoke(new Action(Recover));
            };

            // мышь
            Island.MouseEnter += (_, __) =>
            {
                _hover = true;
                _closeT.Stop();
                if (_mode != Mode.Expanded && !Modes.Hidden && S.OpenMode == "hover" && _act?.Action == null && _act?.OnClick == null) _openT.Start();
                if (_mode == Mode.Collapsed) Retarget();
            };
            Island.MouseLeave += (_, __) =>
            {
                _hover = false;
                _openT.Stop();
                if (_mode == Mode.Expanded) _closeT.Start();
                else if (_mode == Mode.Collapsed) Retarget();
            };
            Island.MouseLeftButtonUp += (_, e) =>
            {
                if (_mode == Mode.Collapsed)
                {
                    // клик точно по персонажу — он реагирует, остров не раскрывается
                    if (!Modes.Hidden && PokeMascot(e)) return;
                    if (Modes.Hidden) Unhide();
                    else Expand();
                }
                else if (_mode == Mode.Activity)
                {
                    var a = _act;
                    if (a?.OnClick != null) { EndActivity(); a.OnClick(); }
                    else if (!Modes.Hidden) Expand(a?.Page);
                    else EndActivity();
                }
            };
            Island.MouseRightButtonUp += (_, e) => { App.ShowSettings(); e.Handled = true; };
            Island.MouseWheel += OnWheel;
            Bubble.MouseEnter += (_, __) => _closeT.Stop();
            Bubble.MouseLeave += (_, __) => { if (_mode == Mode.Expanded) _closeT.Start(); };
            _openT.Tick += (_, __) => { _openT.Stop(); if (_hover && !Modes.Hidden) Expand(); };
            _closeT.Tick += (_, __) =>
            {
                _closeT.Stop();
                if (!Island.IsMouseOver && !Bubble.IsMouseOver && !_dragInside) Collapse();
            };
            _actT.Tick += (_, __) =>
            {
                if (Island.IsMouseOver && (_act?.Action != null || _act?.OnClick != null)) return;
                if (_act?.Kind == "pick" && ColorPicker.I.Active) return;
                EndActivity();
            };
            Deactivated += (_, __) => { if (_mode == Mode.Expanded && !Island.IsMouseOver && !Bubble.IsMouseOver) Collapse(); };
            PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { Collapse(true); e.Handled = true; } };
            PreviewMouseLeftButtonDown += (_, e) =>
            {
                // клик в поле ввода (или в пароль) — чёлке нужен фокус клавиатуры
                var box = (Control)FindParent<TextBox>(e.OriginalSource as DependencyObject) ?? FindParent<PasswordBox>(e.OriginalSource as DependencyObject);
                if (box != null) TakeKeyboard(box);
            };

            // перетаскивание файлов на чёлку → полка (или «Запуск», если он открыт)
            Island.DragEnter += (_, e) =>
            {
                _dragInside = true;
                _closeT.Stop();
                bool files = e.Data.GetDataPresent(DataFormats.FileDrop);
                if (files && _mode != Mode.Expanded && S.PgShelf && S.ShelfAutoOpen && !Modes.Hidden) Expand("shelf");
                e.Effects = files ? DragDropEffects.Copy : DragDropEffects.None;
                e.Handled = true;
            };
            Island.DragOver += (_, e) =>
            {
                _dragInside = true;
                e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
                e.Handled = true;
            };
            Island.DragLeave += (_, __) => { _dragInside = false; _closeT.Start(); };
            Island.Drop += (_, e) =>
            {
                _dragInside = false;
                if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
                {
                    if (_mode == Mode.Expanded && Current?.Key == "launcher")
                    {
                        LauncherPage.Add(files);
                        Current.Instance.OnShown();
                    }
                    else if (S.PgShelf)
                    {
                        ShelfPage.Add(files);
                        Expand("shelf");
                    }
                }
                e.Handled = true;
            };

            // навигация и пузырёк
            PrevPage.Click += (_, __) => ShowPage(_idx - 1, -1);
            NextPage.Click += (_, __) => ShowPage(_idx + 1, 1);
            GearBtn.Click += (_, __) => App.ShowSettings();
            Dots.MouseEnter += (_, __) => RenderDots(true);
            Dots.MouseLeave += (_, __) => RenderDots(false);
            PageTitle.MouseLeftButtonUp += (_, e) =>
            {
                if (CallService.I.InCall) { CallService.I.FocusCallApp(); e.Handled = true; }
            };
            ActButton.Click += (_, __) =>
            {
                var a = _act;
                EndActivity();
                a?.Action?.Invoke();
            };
            BubHide.Click += (_, __) => HideIsland();
            BubPin.Click += (_, __) => { _pinned = !_pinned; if (S.RememberPin) S.IslandPinned = _pinned; UpdateBubble(); };
            _pinned = S.RememberPin && S.IslandPinned;
            BubDnd.Click += (_, __) => SetMode(Modes.Dnd ? "normal" : "dnd");
            BubModes.Click += (_, __) => { if (S.PgModes) Expand("modes"); else App.ShowSettings("modes"); };

            _clock.Tick += (_, __) => OnClock();
            _clock.Start();

            HookServices();
            S.PropertyChanged += OnSetting;
            ApplyDelays();
            ApplyPreset();
            Bars.Live = S.LiveBars;
            InitLive();
        }

        // ================== сервисы ==================

        void HookServices()
        {
            var m = MediaService.I;
            m.Changed += () =>
            {
                App.ApplyAccent(S.ArtColors && m.Art != null ? m.ArtColor : null);
                UpdateCollapsed();
                if (_mode == Mode.Collapsed) Retarget();
            };
            m.PlaybackChanged += () => { UpdateCollapsed(); if (_mode == Mode.Collapsed) Retarget(); };
            m.LyricIndexChanged += () => { UpdateCollapsed(); if (_mode == Mode.Collapsed) Retarget(); };
            m.TrackChanged += () =>
            {
                if (!S.TrackPopup || _mode != Mode.Collapsed || CallService.I.InCall || (DateTime.Now - App.Started).TotalSeconds < 4) return;
                ShowActivity(new Activity
                {
                    Kind = "track", Image = m.Art, Glyph = "", Title = m.Title, Sub = m.Artist,
                    Seconds = 3, Page = "media", Tint = m.ArtColor,
                });
            };

            AudioService.I.Changed += (v, muted, self) =>
            {
                if (!self && S.DedupeOsd) Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(Dedupe.TouchOsd));
                if (self || !S.HudVolume) return;
                string glyph = muted || v < 0.01 ? "" : v < 0.34 ? "" : v < 0.67 ? "" : "";
                ShowActivity(new Activity
                {
                    Kind = "volume", Glyph = glyph, Title = "Громкость",
                    Sub = muted ? "Без звука" : $"{Math.Round(v * 100)}%",
                    Progress = muted ? 0 : v, Seconds = S.HudSeconds,
                });
            };
            AudioService.I.OutputChanged += name =>
            {
                if (!S.HudOutput) return;
                bool phones = name.Contains("Head", StringComparison.OrdinalIgnoreCase) || name.Contains("Науш", StringComparison.OrdinalIgnoreCase)
                              || name.Contains("AirPods", StringComparison.OrdinalIgnoreCase) || name.Contains("Buds", StringComparison.OrdinalIgnoreCase);
                ShowActivity(new Activity
                {
                    Kind = "output", Glyph = phones ? "" : "", Title = phones ? "Наушники подключены" : "Вывод звука",
                    Sub = name, Seconds = 2.5, Wide = true,
                });
            };
            SystemService.I.BrightnessChanged += v =>
            {
                if (S.DedupeOsd) Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(Dedupe.TouchOsd));
                if (!S.HudBrightness) return;
                ShowActivity(new Activity
                {
                    Kind = "brightness", Glyph = "", Title = "Яркость", Sub = v + "%",
                    Progress = v / 100.0, Seconds = S.HudSeconds, Tint = Color.FromRgb(0xFF, 0xD6, 0x0A),
                });
            };
            PomodoroService.I.Finished += (prev, next) =>
            {
                Sound.Play(SoundEvent.Timer);
                var p = PomodoroService.I;
                ShowActivity(new Activity
                {
                    Kind = "pomodoro", Glyph = "", Tint = Red,
                    Title = prev == PomoPhase.Focus ? "Фокус завершён — отдохни" : "Перерыв окончен",
                    Sub = $"Дальше: {p.PhaseName.ToLower()} · {p.Total.TotalMinutes:0} мин",
                    ActionText = p.Running ? null : "Старт", Action = p.Running ? null : p.Start,
                    Seconds = 8, Page = "pomodoro", Wide = true,
                });
            };
            PomodoroService.I.Changed += UpdateRing;
            Water.Changed += UpdateRing;

            // таймер и сон
            var t = TimerService.I;
            t.Finished += () =>
            {
                Sound.Play(SoundEvent.Timer);
                ShowActivity(new Activity
                {
                    Kind = "timer", Glyph = "", Tint = Orange, Title = "Таймер завершён",
                    Sub = $"{t.Set.TotalMinutes:0.#} мин", ActionText = "+1 мин",
                    Action = () => { t.SetMinutes(1); t.Start(); }, Seconds = 12, Page = "timer", Wide = true,
                });
            };
            t.SleepFired += () =>
            {
                if (MediaService.I.IsPlaying) MediaService.I.TogglePlay();
                Toast("", "Сон: музыка на паузе", "Спокойной ночи");
            };

            // звонки и уведомления
            var calls = CallService.I;
            calls.CallChanged += () =>
            {
                bool started = calls.InCall && (DateTime.Now - calls.MicSince).TotalSeconds < 3;
                _micMuted = calls.InCall && AudioService.I.MicMuted;
                UpdateCollapsed();
                if (_mode == Mode.Collapsed) Retarget();
                if (started && _mode != Mode.Expanded)
                {
                    bool web = CallService.IsBrowser(calls.MicApp);
                    ShowActivity(new Activity
                    {
                        Kind = "callstart", Glyph = calls.CameraOn ? "" : "", Tint = Green,
                        Title = (calls.CameraOn ? "Видеозвонок · " : "Звонок · ") + calls.MicApp,
                        Sub = web ? "Звонок на сайте — микрофон включён" : "Микрофон включён",
                        ActionText = "Выкл. микрофон", Action = ToggleMic, OnClick = calls.FocusCallApp,
                        Seconds = 4, Wide = true,
                    });
                }
            };
            calls.ToastArrived += OnToast;

            // VPN включили или выключили — в любой программе
            Vpn.ActiveChanged += (on, name) =>
            {
                UpdateRight();
                if (!S.VpnNotify) return;
                ShowActivity(new Activity
                {
                    Kind = "vpn", Glyph = on ? "" : "", Tint = on ? Green : Orange,
                    Title = on ? "VPN включён" : "VPN выключен", Sub = on ? name : "Соединение идёт напрямую",
                    Seconds = 3, Wide = true, Page = S.PgVpn ? "vpn" : null,
                });
            };
            NotificationLog.Changed += () => { UpdateCollapsed(); if (_mode == Mode.Collapsed) Retarget(); };

            // буфер, клавиатура, пипетка
            ClipboardService.I.Copied += c =>
            {
                if (!S.ClipHud) return;
                var one = c.Text.Replace("\r", " ").Replace("\n", " ").Trim();
                ShowActivity(new Activity { Kind = "clip", Glyph = "", Title = "Скопировано", Sub = one.Length > 70 ? one[..70] + "…" : one, Seconds = 1.6, Page = "clipboard" });
            };
            KeyboardWatch.I.LayoutChanged += (shortName, full) =>
                ShowActivity(new Activity { Kind = "kbd", Glyph = "", Title = full, Sub = "Раскладка · " + shortName, Seconds = 1.2 });
            KeyboardWatch.I.CapsChanged += on =>
                ShowActivity(new Activity { Kind = "caps", Glyph = "", Title = on ? "Caps Lock включён" : "Caps Lock выключен", Tint = on ? Orange : Colors.White, Seconds = 1.2 });
            KeyboardWatch.I.Start();
            ColorPicker.I.Preview += c => ShowActivity(new Activity
            {
                Kind = "pick", Swatch = c, Title = ColorPicker.Hex(c), Sub = "Клик — скопировать · правый клик — отмена", Seconds = 25, Wide = true,
            });
            ColorPicker.I.Done += c =>
            {
                if (c is { } col)
                    ShowActivity(new Activity { Kind = "pick", Swatch = col, Title = ColorPicker.Hex(col) + " скопирован", Sub = $"RGB {col.R}, {col.G}, {col.B}", Seconds = 3, Wide = true });
                else if (_act?.Kind == "pick") EndActivity();
            };
        }

        void ToggleMic()
        {
            _micMuted = AudioService.I.ToggleMicMute();
            UpdateCollapsed();
            ShowActivity(new Activity
            {
                Kind = "mic", Glyph = _micMuted ? "" : "", Tint = _micMuted ? Red : Green,
                Title = _micMuted ? "Микрофон выключен" : "Микрофон включён", Seconds = 1.6,
            });
        }

        string _lastToastSig;
        DateTime _lastToastAt;

        void OnToast(Toast t)
        {
            NotificationLog.Add(t);
            Dedupe.OnApp(t.AppId);   // новое приложение — его баннер Windows тоже выключаем, если включено «Без дублей»
            if (t.IsCall && S.CallDetect)
            {
                if (_mode == Mode.Expanded) Collapse(true);
                ShowActivity(new Activity
                {
                    Kind = "incall", Glyph = "", Tint = Green,
                    Title = "Входящий звонок · " + t.App,
                    Sub = t.Title + (t.Site != null ? " · " + t.Site : ""),
                    ActionText = "Открыть", Action = () => CallService.OpenApp(t.AppId),
                    Seconds = 25, Wide = true,
                });
                return;
            }
            if (!S.Notifications || S.NotifyOnlyCalls) return;
            // устройства: системные дубли Windows («Автозапуск», «Устройство подключено») не показываем —
            // у нас уже есть своя всплывашка о подключении, она в приоритете
            var id = t.AppId ?? "";
            bool sysDevice = id.Contains("AutoPlay", StringComparison.OrdinalIgnoreCase) || id.Contains("Windows.SystemToast", StringComparison.OrdinalIgnoreCase)
                             || id.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase) || id.Contains("Explorer", StringComparison.OrdinalIgnoreCase);
            if (S.DevicePriority && (id.Contains("AutoPlay", StringComparison.OrdinalIgnoreCase) || (sysDevice && DateTime.Now < _devQuietUntil))) return;
            // одно и то же уведомление дважды подряд — показываем один раз
            var sig = t.App + "|" + t.Title + "|" + t.Body;
            if (sig == _lastToastSig && (DateTime.Now - _lastToastAt).TotalSeconds < 5) return;
            _lastToastSig = sig;
            _lastToastAt = DateTime.Now;
            bool web = t.Site != null || CallService.IsBrowser(t.App);
            if (web && !S.NotifyBrowsers) return;
            // чёрный список: такие приложения/сайты попадают только в центр уведомлений
            if (Terms(S.NotifyBlock).Any(b => (t.App + " " + t.AppId + " " + t.Site).Contains(b, StringComparison.OrdinalIgnoreCase))) return;
            var all = t.Title + " " + t.Body;
            bool key = Terms(S.NotifyKeywords).Any(k => all.Contains(k, StringComparison.OrdinalIgnoreCase));
            if (!key && (_mode == Mode.Expanded || (CallService.I.InCall && _mode == Mode.Activity))) return;
            if (key && _mode == Mode.Expanded) Collapse(true);

            // код подтверждения из SMS / 2FA — кнопка «Скопировать»
            string code = S.NotifyCodes ? FindCode(all) : null;
            string from = t.Site ?? t.App;
            var a = new Activity
            {
                Kind = key ? "notifkey" : "notif", Glyph = code != null ? "" : web ? "" : "",
                Tint = key ? Orange : (Application.Current.Resources["AccentBrush"] as SolidColorBrush)?.Color,
                Title = S.NotifyHideText ? from : t.Title,
                Sub = S.NotifyHideText ? "Новое уведомление" : (string.IsNullOrEmpty(t.Body) ? "" : t.Body + " · ") + from,
                Seconds = key ? Math.Max(8, S.NotifySeconds * 2) : S.NotifySeconds, Wide = true, OnClick = () => CallService.OpenApp(t.AppId),
            };
            if (code != null)
            {
                a.ActionText = "Копировать " + code;
                a.Action = () => { ClipboardService.I.CopyBack(code); Toast("", "Код скопирован", code); };
                a.Seconds = Math.Max(a.Seconds, 15);
            }
            if (S.NotifySound || key) Sound.Play(key ? SoundEvent.Alert : SoundEvent.Notification);
            ShowActivity(a);
        }

        static IEnumerable<string> Terms(string list) =>
            (list ?? "").Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        static readonly System.Text.RegularExpressions.Regex CodeWords = new(@"код|code|парол|otp|подтвержд|verif|одноразов|2fa|pin",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);
        static readonly System.Text.RegularExpressions.Regex CodeRx = new(@"(?<![\d\-])(\d{3}[\- ]?\d{3}|\d{4,8})(?![\d\-])",
            System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>Ищет одноразовый код подтверждения в тексте уведомления.</summary>
        static string FindCode(string text)
        {
            if (string.IsNullOrEmpty(text) || !CodeWords.IsMatch(text)) return null;
            var m = CodeRx.Match(text);
            return m.Success ? m.Value.Replace("-", "").Replace(" ", "") : null;
        }

        // ================== публичное API ==================

        public void Welcome()
        {
            ShowActivity(new Activity
            {
                Kind = "hello", Glyph = "", Title = "N-Island запущен",
                Sub = "Наведи курсор на чёлку · Ctrl+Alt+N", Seconds = 6, Wide = true,
            });
        }

        /// <summary>Короткое подтверждение действия.</summary>
        public void Toast(string glyph, string title, string sub) =>
            ShowActivity(new Activity { Kind = "toast", Glyph = glyph, Title = title, Sub = sub, Seconds = 1.8, Wide = sub != null });

        public bool IsShowing(PageBase p) => _mode == Mode.Expanded && Current?.Created == true && Current.Instance == p;

        public void Relayout() => Retarget();

        /// <summary>Мышь могла уйти, пока чёлка была «удержана» — проверить, не пора ли свернуться.</summary>
        public void Nudge()
        {
            if (_mode == Mode.Expanded && !Island.IsMouseOver && !Bubble.IsMouseOver) _closeT.Start();
        }

        public void SetMode(string key)
        {
            if (S.Mode == key) return;
            S.Mode = key;
            if (key == "focus")
            {
                var p = PomodoroService.I;
                if (!p.Running) { if (p.Phase != PomoPhase.Focus) p.SetPhase(PomoPhase.Focus); p.Start(); }
            }
            if (key is "game" or "present") Collapse(true);
            var m = Modes.Current;
            ShowActivity(new Activity { Kind = "mode", Glyph = m.Glyph, Title = "Режим: " + m.Name, Sub = m.Desc, Seconds = 2.2, Wide = true });
        }

        public void HideIsland()
        {
            _pinned = false;
            Collapse(true);
            S.Hidden = true;
            Toast("", "Чёлка скрыта", "Клик по полоске вверху — вернуть");
        }

        public void Unhide()
        {
            if (S.Mode is "game" or "present") S.Mode = "normal";
            S.Hidden = false;
        }

        PageInfo Current => _idx >= 0 && _idx < _pages.Count ? _pages[_idx] : null;

        /// <summary>Все страницы в текущем порядке (для настройки порядка).</summary>
        public List<(string Key, string Title, bool On)> PageList()
        {
            var order = (S.PageOrder ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            return _all.OrderBy(p => { int i = order.IndexOf(p.Key); return i < 0 ? 1000 + _all.IndexOf(p) : i; })
                       .Select(p => (p.Key, p.Title, p.On())).ToList();
        }

        public void Expand(string page = null, bool activate = false)
        {
            if (_pages.Count == 0 || _hiddenFs) return;
            if (Modes.Hidden) Unhide();
            if (page != null)
            {
                int i = _pages.FindIndex(p => p.Key == page);
                if (i >= 0 && i != _idx) ShowPage(i, 0);
            }
            if (activate) TakeKeyboard();
            if (_mode == Mode.Expanded) return;
            _openT.Stop();
            _actT.Stop();
            _mode = Mode.Expanded;
            AllowKeyboard();
            ExpandedView.Visibility = Visibility.Visible;
            Bubble.Visibility = S.SideBubble ? Visibility.Visible : Visibility.Collapsed;
            UpdateBubble();
            if (Current != null)
            {
                if (PageHost.Content == null) PageHost.Content = Current.Instance;
                Current.Instance.OnShown();
            }
            Retarget();
        }

        IntPtr _prevForeground;
        bool _keyboard;

        /// <summary>Чёлка забирает клавиатуру: снимаем «не активировать», становимся активным окном, ставим курсор в поле.</summary>
        /// <summary>
        /// Раскрытая чёлка — обычное окно: клик по ней Windows активирует сама (это разрешено всегда),
        /// поэтому в полях ввода работает клавиатура. Свёрнутая — снова «не активируемая».
        /// </summary>
        void AllowKeyboard()
        {
            if (_hwnd == IntPtr.Zero || _keyboard) return;
            _keyboard = true;
            var fg = Native.GetForegroundWindow();
            if (fg != _hwnd) _prevForeground = fg;
            Native.SetNoActivate(_hwnd, false);
        }

        public void TakeKeyboard(Control box = null)
        {
            if (_hwnd == IntPtr.Zero) return;
            AllowKeyboard();
            if (!IsActive)
            {
                var prev = Native.ForceForeground(_hwnd);
                if (prev != IntPtr.Zero) _prevForeground = prev;
                Activate();
            }
            if (box != null)
                Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => { box.Focus(); Keyboard.Focus(box); }));
        }

        /// <summary>Отдать клавиатуру обратно: снова «не активируемся» и возвращаем фокус прежнему окну.</summary>
        void ReleaseKeyboard()
        {
            if (!_keyboard) return;
            _keyboard = false;
            bool wasActive = IsActive;
            if (IsKeyboardFocusWithin) Keyboard.ClearFocus();
            Native.SetNoActivate(_hwnd, true);
            if (wasActive) Native.RestoreForeground(_prevForeground);
            _prevForeground = IntPtr.Zero;
        }

        public void Collapse(bool force = false)
        {
            if (_mode != Mode.Expanded) return;
            if (!force && (_pinned || Hold || _dragInside || (IsActive && Keyboard.FocusedElement is TextBox or PasswordBox))) return;
            if (force) { _pinned = false; if (S.IslandPinned) S.IslandPinned = false; }
            _mode = Mode.Collapsed;
            _collapsedAt = DateTime.Now;
            if (Current?.Created == true) Current.Instance.OnHidden();
            if (IsKeyboardFocusWithin) Keyboard.ClearFocus();
            ReleaseKeyboard();
            if (_pending != null)
            {
                var a = _pending;
                _pending = null;
                ShowActivity(a);
                return;
            }
            Retarget();
        }

        bool Allowed(Activity a)
        {
            bool userAction = a.Kind is "pick" or "toast" or "mode";
            if (S.Mode == "present") return userAction || a.Kind == "alert";
            bool important = userAction || a.Kind is "incall" or "lowbat" or "timer" or "event" or "notifkey" or "usb" or "cam" or "alert" or "vpn";
            if (Modes.Hidden) return important || (a.Action != null && a.Kind == "pomodoro");
            if (Modes.Dnd) return important || a.Kind is "volume" or "brightness" or "battery" or "callstart" or "mic" or "pomodoro" or "kbd" or "caps" or "clip";
            return true;
        }

        /// <summary>Важность всплывашки: более важная не перебивается менее важной.</summary>
        static int Prio(string kind) => kind switch
        {
            "incall" or "alert" => 100,
            "callstart" => 90,
            "vpn" => 78,
            "hub" => 45,
            "usb" or "output" or "lowbat" or "battery" or "cam" => 80,
            "pc" => 50,
            "pick" or "toast" or "mode" => 75,
            "timer" or "event" or "pomodoro" or "prayer" or "notifkey" => 70,
            "water" or "eye" or "stand" => 60,
            "notif" => 40,
            "track" => 30,
            _ => 20,
        };

        DateTime _actEnds, _devQuietUntil;

        public void ShowActivity(Activity a)
        {
            // наружу (Telegram, ntfy, Discord, вебхук) — даже в «Не беспокоить»: туда это и нужно
            if (a.Kind != "alert") Integrations.Publish(Integrations.KindOf(a.Kind), a.Title, a.Sub);
            if (!Allowed(a)) return;
            OnLiveActivity(a);
            if (_hiddenFs && a.Action == null && a.Kind != "pick") return;
            if (_mode == Mode.Activity && _act != null && _act.Kind != a.Kind && Prio(a.Kind) < Prio(_act.Kind) && DateTime.Now < _actEnds)
            {
                // сейчас на экране что-то важнее — напоминания подождут, мелочь отбрасываем
                if (a.Kind is "water" or "pomodoro" or "event" or "prayer" or "timer" or "eye" or "stand") _pending = a;
                return;
            }
            if (a.Kind is "usb" or "output" or "battery") _devQuietUntil = DateTime.Now.AddSeconds(6);
            _actEnds = DateTime.Now.AddSeconds(Math.Max(1, a.Seconds));
            if (_mode == Mode.Expanded)
            {
                if (a.Kind is "water" or "pomodoro" or "event" or "prayer" or "timer" or "usb") _pending = a;
                else if (a.Kind is "pick") Collapse(true);
                else return;
                if (_mode == Mode.Expanded) return;
            }
            bool same = _mode == Mode.Activity && _act?.Kind == a.Kind;
            bool wasWide = _act?.Wide == true || _act?.Action != null;
            _act = a;

            var tint = a.Tint ?? Colors.White;
            ActGlyph.Text = a.Swatch != null ? "" : a.Glyph ?? "";
            ActGlyph.FontFamily = a.GlyphFont != null ? new FontFamily(a.GlyphFont) : IconFont;
            ActGlyph.Foreground = new SolidColorBrush(tint);
            ActImage.Visibility = a.Image != null ? Visibility.Visible : Visibility.Collapsed;
            ActImageBrush.ImageSource = a.Image;
            ActIconBox.Background = a.Swatch is Color sw ? new SolidColorBrush(sw)
                : a.Image != null ? Brushes.Transparent : new SolidColorBrush(ColorUtil.WithAlpha(tint, 0.2));
            ActTitle.Text = a.Title ?? "";
            ActSub.Text = a.Sub ?? "";
            ActSub.Visibility = string.IsNullOrEmpty(a.Sub) ? Visibility.Collapsed : Visibility.Visible;
            ActBar.Visibility = a.Progress.HasValue ? Visibility.Visible : Visibility.Collapsed;
            if (a.Progress.HasValue)
            {
                ActBarFill.Width = Math.Max(6, 110 * Math.Clamp(a.Progress.Value, 0, 1));
                ActBarFill.Background = new SolidColorBrush(tint);
            }
            ActButton.Visibility = a.Action != null ? Visibility.Visible : Visibility.Collapsed;
            ActButton.Content = a.ActionText ?? "OK";
            if (a.Kind is "incall" or "callstart") ActButton.Background = new SolidColorBrush(a.Kind == "incall" ? Green : Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
            else ActButton.ClearValue(BackgroundProperty);

            _mode = Mode.Activity;
            ActivityView.Visibility = Visibility.Visible;
            _actT.Stop();
            _actT.Interval = TimeSpan.FromSeconds(Math.Max(1, a.Seconds));
            _actT.Start();
            bool wide = a.Wide || a.Action != null;
            if (!same || wide != wasWide) Retarget();
        }

        void EndActivity()
        {
            _actT.Stop();
            if (_mode != Mode.Activity) return;
            _mode = Mode.Collapsed;
            _collapsedAt = DateTime.Now;
            _act = null;
            if (_pending != null)
            {
                var p = _pending;
                _pending = null;
                ShowActivity(p);
                return;
            }
            Retarget();
        }

        // ================== страницы ==================

        void BuildPages()
        {
            var cur = Current;
            // свой порядок страниц из настроек; неупомянутые идут следом в обычном порядке
            var order = (S.PageOrder ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            _pages = _all.Where(p => p.On())
                         .OrderBy(p => { int i = order.IndexOf(p.Key); return i < 0 ? 1000 + _all.IndexOf(p) : i; })
                         .ToList();
            _idx = cur != null && _pages.Contains(cur) ? _pages.IndexOf(cur) : 0;
            // при запуске открываемся на той странице, где были в прошлый раз
            if (cur == null && S.RememberPage && !string.IsNullOrEmpty(S.LastPage))
            {
                int last = _pages.FindIndex(p => p.Key == S.LastPage);
                if (last >= 0) _idx = last;
            }
            var p0 = Current;
            PageHost.Content = p0 != null && (p0.Created || _mode == Mode.Expanded) ? p0.Instance : null;
            if (_mode == Mode.Expanded && p0 != null && p0 != cur) p0.Instance.OnShown();
            UpdateTitle();
            PrevPage.Visibility = NextPage.Visibility = _pages.Count > 1 ? Visibility.Visible : Visibility.Hidden;
            RenderDots(false);
            if (_pages.Count == 0) Collapse(true);
        }

        void UpdateTitle()
        {
            var c = CallService.I;
            if (c.InCall)
            {
                PageTitle.Text = $"● {c.MicApp} {Fmt.Time(DateTime.Now - c.MicSince)}";
                PageTitle.Foreground = new SolidColorBrush(Green);
                PageTitle.Cursor = Cursors.Hand;
                PageTitle.ToolTip = "Открыть окно звонка";
            }
            else
            {
                PageTitle.Text = Current?.Title ?? "";
                PageTitle.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
                PageTitle.Cursor = null;
                PageTitle.ToolTip = null;
            }
        }

        void ShowPage(int i, int dir)
        {
            int n = _pages.Count;
            if (n == 0) return;
            i = ((i % n) + n) % n;
            if (i == _idx && PageHost.Content != null && PageHost.Content == _pages[i].Instance) return;
            if (dir == 0) dir = i > _idx ? 1 : -1;
            if (_mode == Mode.Expanded && Current?.Created == true) Current.Instance.OnHidden();
            _idx = i;
            if (S.RememberPage) S.LastPage = _pages[i].Key;
            var p = _pages[i].Instance;
            PageHost.Content = p;
            UpdateTitle();
            if (_mode == Mode.Expanded) p.OnShown();
            RenderDots(Dots.IsMouseOver);

            var tt = (TranslateTransform)PageHost.RenderTransform;
            var ease = new QuinticEase { EasingMode = EasingMode.EaseOut };
            tt.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(28 * dir, 0, TimeSpan.FromMilliseconds(460)) { EasingFunction = ease });
            PageHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(380)) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut } });
            Retarget();
        }

        void RenderDots(bool icons)
        {
            Dots.Children.Clear();
            // страниц бывает 20+: точками показываем окно из 9 вокруг текущей, крайние меньше (как в iOS); иконки при наведении — все
            const int Win = 9;
            bool window = !(icons && S.PageIcons) && _pages.Count > Win;
            int from = window ? Math.Clamp(_idx - Win / 2, 0, _pages.Count - Win) : 0, to = window ? from + Win : _pages.Count;
            for (int i = from; i < to; i++)
            {
                var p = _pages[i];
                bool active = i == _idx;
                int idx = i;
                FrameworkElement el;
                if (icons && S.PageIcons)
                {
                    el = new TextBlock
                    {
                        Text = p.Glyph,
                        FontFamily = new FontFamily(p.GlyphFont),
                        FontSize = 12,
                        Foreground = active ? Brushes.White : new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF)),
                        Margin = new Thickness(5, 1, 5, 1),
                        Cursor = Cursors.Hand,
                        ToolTip = p.Title,
                        Background = Brushes.Transparent,
                    };
                }
                else
                {
                    bool edge = window && !active && ((i == from && from > 0) || (i == to - 1 && to < _pages.Count));
                    double d = edge ? 4 : 6;
                    el = new Border
                    {
                        Width = active ? 14 : d,
                        Height = d,
                        CornerRadius = new CornerRadius(d / 2),
                        Background = active ? Brushes.White : new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF)),
                        Margin = new Thickness(3, 5 + (6 - d) / 2, 3, 5 + (6 - d) / 2),
                        Cursor = Cursors.Hand,
                        ToolTip = p.Title,
                    };
                    // активная точка плавно растягивается
                    if (active && !icons)
                        el.BeginAnimation(WidthProperty, new DoubleAnimation(6, 14, TimeSpan.FromMilliseconds(320)) { EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseOut } });
                }
                el.MouseLeftButtonUp += (_, e) => { ShowPage(idx, 0); e.Handled = true; };
                Dots.Children.Add(el);
            }
        }

        void OnWheel(object sender, MouseWheelEventArgs e)
        {
            if (_mode != Mode.Expanded || _pages.Count < 2) return;
            e.Handled = true;
            if ((DateTime.Now - _wheelAt).TotalMilliseconds < 300) return;
            _wheelAt = DateTime.Now;
            int d = e.Delta < 0 ? 1 : -1;
            ShowPage(_idx + d, d);
        }

        void UpdateBubble()
        {
            BubPin.Content = _pinned ? "" : "";
            BubPin.ToolTip = _pinned ? "Открепить" : "Закрепить открытой";
            BubPin.Foreground = _pinned ? (Brush)Application.Current.Resources["AccentBrush"] : Brushes.White;
            BubDnd.Foreground = Modes.Dnd ? (Brush)Application.Current.Resources["AccentBrush"] : Brushes.White;
            BubDnd.ToolTip = Modes.Dnd ? "Выключить «Не беспокоить»" : "Не беспокоить";
        }

        // ================== геометрия и анимация ==================

        bool Floating => S.Style == "floating";
        static double BaseH => Math.Clamp(S.NotchHeight, 24, 44);
        double CollapsedH => Modes.Hidden ? 6 : BaseH + (Floating ? 2 : 0);
        static double Radius(double h) => Math.Min(h / 2, Math.Clamp(S.MaxRadius, 6, 40));
        static double GlassAlpha => Math.Clamp(0.06 + 0.9 * S.GlassTint / 100.0, 0.05, 1);
        // «Лёгкий режим» для слабых ПК: стекло — это захват экрана и размытие, самое дорогое в чёлке
        bool GlassNow => S.Glass && !S.LiteMode && !Modes.Hidden && (_mode != Mode.Collapsed || P.GlassCollapsed);

        bool LyricVisible
        {
            get
            {
                var m = MediaService.I;
                var l = m.CurrentLyric;
                return S.LiveLyrics && m.IsPlaying && !CallService.I.InCall && S.Mode != "minimal" && !string.IsNullOrWhiteSpace(l) && l != "♪";
            }
        }

        double CollapsedWidth()
        {
            if (Modes.Hidden) return _hover ? 84 : 64;
            var m = MediaService.I;
            double w = S.Mode == "minimal" ? 110 : S.NotchWidth + (S.ClockSeconds ? 18 : 0) + (S.Clock12h ? 22 : 0);
            if (CallService.I.InCall) w = Math.Max(w + 70, 260);
            else if (LyricVisible)
            {
                var ft = new FormattedText(ColLyric.Text ?? "", CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    new Typeface(ColLyric.FontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
                    ColLyric.FontSize, Brushes.White, _dpi);
                w = Math.Clamp(ft.Width + 92, Math.Max(w, 240), 620);
            }
            else if (m.HasSession && m.IsPlaying && S.Mode != "minimal") w += 36;
            if (ColBadge.Visibility == Visibility.Visible && S.Mode != "minimal") w += NotificationLog.Unread > 0 ? 30 : 18;
            if (_hover && _mode == Mode.Collapsed) w += 14;
            if (MascotOn) w += 28;
            return w;
        }

        double ExpandedWidth() => S.ExpandedWidth + (Current?.Created == true ? Current.Instance.ExtraWidth : 0);

        void Retarget(bool instant = false)
        {
            switch (_mode)
            {
                case Mode.Expanded:
                    _tw = ExpandedWidth();
                    _th = ExpandedH + Math.Clamp(Current?.Created == true ? Current.Instance.ExtraHeight : 0, 0, 40);
                    ExpandedView.Width = _tw;
                    ExpandedView.Height = _th;
                    break;
                case Mode.Activity:
                    _tw = _act?.Wide == true || _act?.Action != null ? 440 : 370;
                    _th = 46;
                    break;
                default:
                    _tw = CollapsedWidth();
                    _th = CollapsedH + (_hover ? 2 : 0);
                    break;
            }
            bool glass = GlassNow;
            SyncLive();
            _tbgA = glass ? GlassAlpha : Math.Clamp(S.BgOpacity / 100.0, 0.1, 1);
            if (glass) EnsureGlass(false);
            ExpandedView.IsHitTestVisible = _mode == Mode.Expanded;
            ActivityView.IsHitTestVisible = _mode == Mode.Activity;
            CollapsedView.IsHitTestVisible = false;
            if (_mode == Mode.Expanded) ExpandedView.Visibility = Visibility.Visible;
            // прозрачность: общая × «в покое» (свёрнута и без наведения) × ночной режим
            double op = Math.Clamp(S.IslandOpacity / 100.0, 0.2, 1);
            if (_mode == Mode.Collapsed && !_hover) op *= Math.Clamp(S.IdleOpacity / 100.0, 0.1, 1);
            if (S.Mode == "night") op *= 0.72;
            Root.Opacity = S.Mode == "present" && _mode == Mode.Collapsed ? 0 : op;

            if (instant)
            {
                _w = _tw; _h = _th; _vw = _vh = 0;
                _opC = _mode == Mode.Collapsed && !Modes.Hidden ? 1 : 0;
                _opA = _mode == Mode.Activity ? 1 : 0;
                _opE = _mode == Mode.Expanded ? 1 : 0;
                _opB = BubbleTarget();
                _bgA = _tbgA;
                Apply();
                AfterSettle();
                return;
            }
            if (!_ticking)
            {
                _ticking = true;
                _last = TimeSpan.Zero;
                CompositionTarget.Rendering += OnFrame;
            }
        }

        double BubbleTarget() => _mode == Mode.Expanded && S.SideBubble && _opE > 0.3 ? 1 : 0;

        void OnFrame(object sender, EventArgs e)
        {
            var t = ((RenderingEventArgs)e).RenderingTime;
            Perf.Hit("island-frame");
            if (t == _last) return;
            double dt = _last == TimeSpan.Zero ? 1 / 60.0 : Math.Clamp((t - _last).TotalSeconds, 0.001, 0.05);
            _last = t;

            // пружина в терминах SwiftUI: response (период, с) и dampingFraction.
            // «Обычная» — стандартная пружина Apple (0.5 / 0.825), как в iOS.
            var (response, zeta) = S.AnimSpeed switch
            {
                "fast" => (0.34, 0.86),
                "smooth" => (0.62, 0.9),
                _ => (0.5, 0.825),
            };
            double k = Math.Pow(2 * Math.PI / response, 2);
            double damp = 4 * Math.PI * zeta / response;
            Spring(ref _w, ref _vw, _tw, k, damp, dt);
            Spring(ref _h, ref _vh, _th, k, damp, dt);
            bool fast = S.AnimSpeed == "fast";

            double rate = (fast ? 20 : S.AnimSpeed == "smooth" ? 10 : 13) * dt;
            double cT = _mode == Mode.Collapsed && !Modes.Hidden && _h < _th + 26 ? 1 : 0;
            double aT = _mode == Mode.Activity && Math.Abs(_h - _th) < 18 ? 1 : 0;
            double eT = _mode == Mode.Expanded && _h > _th * 0.55 ? 1 : 0;
            double bT = BubbleTarget();
            _opC = Approach(_opC, cT, rate * (cT > _opC ? 1 : 2.2));
            _opA = Approach(_opA, aT, rate * (aT > _opA ? 1 : 2.2));
            _opE = Approach(_opE, eT, rate * (eT > _opE ? 0.9 : 2.6));
            _opB = Approach(_opB, bT, rate * (bT > _opB ? 0.8 : 3));
            _bgA = Approach(_bgA, _tbgA, rate);
            Apply();

            bool done = Math.Abs(_w - _tw) < 0.25 && Math.Abs(_h - _th) < 0.25 && Math.Abs(_vw) < 2 && Math.Abs(_vh) < 2
                        && _opC == cT && _opA == aT && _opE == eT && _opB == bT && _bgA == _tbgA;
            if (done)
            {
                _w = _tw; _h = _th; _vw = _vh = 0;
                Apply();
                CompositionTarget.Rendering -= OnFrame;
                _ticking = false;
                AfterSettle();
            }
        }

        static void Spring(ref double x, ref double v, double target, double k, double d, double dt)
        {
            // полунеявный Эйлер с подшагами — стабильно при любом FPS
            int n = (int)Math.Ceiling(dt / 0.004);
            double h = dt / n;
            for (int i = 0; i < n; i++)
            {
                double a = -k * (x - target) - d * v;
                v += a * h;
                x += v * h;
            }
        }

        /// <summary>Плавная S-кривая для прозрачности (мягкий старт и финиш).</summary>
        static double Smooth(double x) => x <= 0 ? 0 : x >= 1 ? 1 : x * x * (3 - 2 * x);

        static void Settle(FrameworkElement el, double p, double fromScale, double fromY)
        {
            if (el.RenderTransform is not TransformGroup g)
            {
                g = new TransformGroup();
                g.Children.Add(new ScaleTransform());
                g.Children.Add(new TranslateTransform());
                el.RenderTransform = g;
                el.RenderTransformOrigin = new Point(0.5, 0);
            }
            double e = Smooth(p);
            var s = (ScaleTransform)g.Children[0];
            s.ScaleX = s.ScaleY = fromScale + (1 - fromScale) * e;
            ((TranslateTransform)g.Children[1]).Y = fromY * (1 - e);
        }

        static double Approach(double c, double t, double k)
        {
            c += (t - c) * Math.Min(1, k);
            return Math.Abs(t - c) < 0.012 ? t : c;
        }

        void Apply()
        {
            double w = Math.Max(24, _w), h = Math.Max(4, _h);
            Island.Width = w;
            Island.Height = h;
            double r = Radius(h);
            bool fl = Floating || Modes.Hidden;
            var cr = fl ? new CornerRadius(r) : new CornerRadius(0, 0, r, r);
            Island.CornerRadius = cr;
            Rim.CornerRadius = cr;
            Sheen.CornerRadius = fl ? new CornerRadius(r, r, 0, 0) : new CornerRadius(0);
            Sheen.Height = h * 0.55;
            var rect = new Rect(0, 0, w, h);
            Stage.Clip = fl
                ? new RectangleGeometry(rect, r, r)
                : new CombinedGeometry(GeometryCombineMode.Union, new RectangleGeometry(new Rect(0, 0, w, h / 2)), new RectangleGeometry(rect, r, r));
            ((TranslateTransform)EarL.RenderTransform).X = -(w / 2 + 6);
            ((TranslateTransform)EarR.RenderTransform).X = w / 2 + 6;
            byte a = (byte)Math.Round(Math.Clamp(_bgA, 0, 1) * 255);
            _s1.Color = Color.FromArgb(a, _c1.R, _c1.G, _c1.B);
            _s2.Color = Color.FromArgb(a, _c2.R, _c2.G, _c2.B);

            CollapsedView.Opacity = Smooth(_opC);
            CollapsedView.Visibility = _opC < 0.01 ? Visibility.Hidden : Visibility.Visible;
            ActivityView.Opacity = Smooth(_opA);
            ExpandedView.Opacity = Smooth(_opE);
            // содержимое «проявляется слоем»: чуть растёт и съезжает на место вместе с прозрачностью
            Settle(ExpandedView, _opE, 0.94, -10);
            Settle(ActivityView, _opA, 0.9, -4);
            Settle(CollapsedView, _opC, 0.86, 0);

            Bubble.Opacity = _opB;
            Bubble.Width = BubW;
            Bubble.Margin = new Thickness(0, Floating ? FloatTop : 6, 0, 0);
            ((TranslateTransform)Bubble.RenderTransform).X = w / 2 + BubGap + BubW / 2;
            Bubble.IsHitTestVisible = _opB > 0.5;
            UpdateRegion(w, h, r);
            ApplyLens(w, h);
        }

        void AfterSettle()
        {
            if (_mode != Mode.Expanded && _opE <= 0)
            {
                ExpandedView.Visibility = Visibility.Collapsed;
                // свёрнутая чёлка не держит в дереве тяжёлую страницу
                if (S.LowMemory) PageHost.Content = null;
            }
            if (_opB <= 0) Bubble.Visibility = Visibility.Collapsed;
            if (_mode != Mode.Activity && _opA <= 0)
            {
                ActivityView.Visibility = Visibility.Hidden;
                ActImageBrush.ImageSource = null;
            }
            if (!GlassNow) DropGlass();
        }

        int Px(double v) => (int)Math.Round(v * _dpi);

        [DllImport("gdi32.dll")] static extern IntPtr CreatePolygonRgn(int[] pts, int n, int mode);

        /// <summary>Регион окна = форма острова (+ пузырёк): клики мимо проходят насквозь.</summary>
        void UpdateRegion(double w, double h, double r)
        {
            if (_hwnd == IntPtr.Zero || _hiddenFs) return;
            bool fl = Floating || Modes.Hidden;
            double top = Floating ? FloatTop : 0;
            double x = (Width - w) / 2;
            int L = Px(x), T = Px(top), R = Px(x + w), B = Px(top + h), D = Px(r * 2), E = fl ? 0 : Px(12);
            bool bub = _opB > 0.01;
            int bx = Px(x + w + BubGap), by = Px(Floating ? FloatTop : 6);
            string key = $"{L},{T},{R},{B},{D},{E},{bub}";
            if (key == _rgnKey) return;
            _rgnKey = key;

            IntPtr rgn = Native.CreateRoundRectRgn(L, T, R + 1, B + 1, D, D);
            if (!fl)
            {
                Merge(rgn, Native.CreateRectRgn(L, T, R + 1, T + Math.Max(1, (B - T) / 2)));
                Merge(rgn, EarRegion(L, true, E));
                Merge(rgn, EarRegion(R, false, E));
            }
            if (bub) Merge(rgn, Native.CreateRoundRectRgn(bx, by, bx + Px(BubW) + 1, by + Px(40) + 1, Px(40), Px(40)));
            Native.SetWindowRgn(_hwnd, rgn, true); // регион теперь принадлежит системе
        }

        static void Merge(IntPtr dst, IntPtr src)
        {
            Native.CombineRgn(dst, dst, src, 2 /* RGN_OR */);
            Native.DeleteObject(src);
        }

        /// <summary>Вогнутое «плечо» у верхнего края.</summary>
        static IntPtr EarRegion(int edge, bool left, int e)
        {
            var pts = new List<int>();
            int s = left ? -1 : 1;
            pts.Add(edge); pts.Add(0);
            const int steps = 8;
            for (int i = 0; i <= steps; i++)
            {
                double a = Math.PI / 2 * i / steps;
                pts.Add((int)Math.Round(edge + s * e - s * e * Math.Cos(a)));
                pts.Add((int)Math.Round(e - e * Math.Sin(a)));
            }
            return CreatePolygonRgn(pts.ToArray(), pts.Count / 2, 2);
        }

        double _glassW;

        /// <summary>Снимок под чёлкой → размытое «стекло».</summary>
        void EnsureGlass(bool force)
        {
            if (_hwnd == IntPtr.Zero || _hiddenFs) return;
            if (!force && _blur && _tw <= _glassW) return;
            try
            {
                double w = Math.Min(Width, Math.Max(_tw, 440) + 2 * (BubW + BubGap + 24)), h = ExpandedH + 64;
                double x = (Width - w) / 2;
                var p = PointToScreen(new Point(x, 0));
                // жидкое стекло — снимок чётче (меньше уменьшение) и ярче, края преломляет линза
                var shot = Native.CaptureBehind(_hwnd, (int)p.X, (int)p.Y, Px(w), Px(h), S.LiquidGlass ? 3 : 6);
                GlassImg.Source = S.LiquidGlass ? Vibrant(shot, Math.Clamp(S.GlassVibrancy, 50, 220) / 100.0, 6) : shot;
                GlassImg.Margin = new Thickness(x, 0, 0, 0);
                GlassImg.Width = w;
                GlassImg.Height = h;
                GlassImg.Visibility = Visibility.Visible;
                _glassW = w - 2 * (BubW + BubGap + 24);
                _glassAt = DateTime.Now;
                _blur = true;
            }
            catch (Exception e) { Log.Error(e); }
        }

        void DropGlass()
        {
            if (!_blur) return;
            _blur = false;
            _glassW = 0;
            GlassImg.Visibility = Visibility.Collapsed;
            GlassImg.Source = null;
        }

        /// <summary>Цвета, ободок и блик из текущего пресета.</summary>
        void ApplyPreset()
        {
            var p = P;
            _c1 = ColorUtil.Parse(p.Bg1, Colors.Black);
            _c2 = ColorUtil.Parse(p.Bg2, Colors.Black);
            var rim = ColorUtil.Parse(p.RimColor, Colors.White);
            _r1.Color = ColorUtil.WithAlpha(rim, p.Rim);
            _r2.Color = ColorUtil.WithAlpha(rim, p.Rim * 0.3);
            _sh1.Color = ColorUtil.WithAlpha(Colors.White, p.Sheen);
            _sh2.Color = Colors.Transparent;
            Rim.Visibility = p.Rim > 0 ? Visibility.Visible : Visibility.Collapsed;
            Sheen.Visibility = p.Sheen > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (GlassImg.Effect is System.Windows.Media.Effects.BlurEffect blur) blur.Radius = Math.Clamp(S.BlurRadius, 0, 60);
            _rgnKey = null;
            Apply();
        }

        // ================== содержимое свёрнутой чёлки ==================

        /// <summary>Службы (тревога, VPN) поменяли то, что видно в свёрнутой чёлке.</summary>
        public void RefreshCollapsed()
        {
            UpdateCollapsed();
            if (_mode == Mode.Collapsed) Retarget();
        }

        void UpdateCollapsed()
        {
            var m = MediaService.I;
            var c = CallService.I;
            bool call = c.InCall;
            bool playing = m.HasSession && m.IsPlaying;
            bool lyric = LyricVisible;
            bool minimal = S.Mode == "minimal";

            ColMic.Visibility = call ? Visibility.Visible : Visibility.Collapsed;
            if (call)
            {
                ColMic.Background = new SolidColorBrush(_micMuted ? Red : Green);
                ColMicGlyph.Text = _micMuted ? "" : c.CameraOn ? "" : "";
                ColLyric.Text = c.MicApp;
            }
            // строка песни в свёрнутом острове — в том же виде, что и в плеере (шрифт, цвет, заглавные, анимация появления)
            else if (lyric) NIsland.Pages.LyricsAnim.ShowLine(ColLyric, m.CurrentLyric, centered: true, animate: !S.LiteMode);
            ColLyric.Visibility = call || lyric ? Visibility.Visible : Visibility.Collapsed;
            ColTime.Visibility = call || lyric || minimal ? Visibility.Collapsed : Visibility.Visible;

            bool art = !call && playing && S.ArtInNotch && S.LeftSlot == "auto" && m.Art != null && !lyric;
            ColArt.Visibility = art ? Visibility.Visible : Visibility.Collapsed;
            ColArtBrush.ImageSource = art ? m.Art : null;
            ColNote.Visibility = !call && lyric ? Visibility.Visible : Visibility.Collapsed;

            bool bars = !call && playing && S.Visualizer && !S.LiteMode;
            Bars.IsPlaying = playing;
            Bars.Visibility = bars ? Visibility.Visible : Visibility.Collapsed;
            ColRight.Visibility = bars ? Visibility.Collapsed : Visibility.Visible;

            // значок «не беспокоить» / непрочитанные
            int n = NotificationLog.Unread;
            bool unread = n > 0 && (S.NotifyBadge || Modes.Dnd) && S.PgCenter;
            ColBadge.Visibility = (Modes.Dnd || unread) && !minimal ? Visibility.Visible : Visibility.Collapsed;
            if (unread)
            {
                ColBadge.FontFamily = (FontFamily)Application.Current.Resources["UiFont"];
                ColBadge.FontWeight = FontWeights.Bold;
                ColBadge.Text = "● " + (n > 9 ? "9+" : n.ToString());
                ColBadge.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
            }
            else
            {
                ColBadge.FontFamily = IconFont;
                ColBadge.FontWeight = FontWeights.Normal;
                ColBadge.Text = "";
                ColBadge.SetResourceReference(TextBlock.ForegroundProperty, "DimBrush");
            }

            UpdateRight();
            UpdateRing();
            UpdateTitle();
            if (Bubble.Visibility == Visibility.Visible) UpdateBubble();
        }

        /// <summary>Правая часть свёрнутой чёлки: звонок → таймер → погода → сеть → батарея.</summary>
        void UpdateRight()
        {
            var c = CallService.I;
            var t = TimerService.I;
            var sys = SystemService.I;
            string text;
            Color? color = null;
            if (c.InCall) { text = Fmt.Time(DateTime.Now - c.MicSince); color = Green; }
            else if (t.Running) { text = "⏱ " + Fmt.Time(t.Left); color = Orange; }
            else if (S.Mode == "focus" && PomodoroService.I.Active) { text = Fmt.Time(PomodoroService.I.Remaining); color = Red; }
            else if (S.RightSlot != "auto") text = S.RightSlot switch
            {
                "battery" => sys.HasBattery ? (sys.Charging ? "⚡" : "") + sys.BatteryPercent + "%" : "",
                "weather" => WeatherService.Cached is { } ww ? $"{WeatherService.Describe(ww.Code).Icon} {Math.Round(ww.Temp)}°" : "",
                "net" => "↓" + NetSpeed.Fmt(NetSpeed.Down),
                "date" => DateTime.Now.ToString("d MMM", Fmt.Ru).Replace(".", ""),
                "ram" => SystemService.I.RamPercent() + "% ОЗУ",
                _ => "",
            };
            else if (S.WeatherInNotch && WeatherService.Cached is { } w) text = $"{WeatherService.Describe(w.Code).Icon} {Math.Round(w.Temp)}°";
            else if (S.NetInNotch) text = "↓" + NetSpeed.Fmt(NetSpeed.Down);
            else text = sys.HasBattery && S.ShowBattery ? (sys.Charging ? "⚡" : "") + sys.BatteryPercent + "%" : "";
            if (S.Mode == "minimal" && !c.InCall && !t.Running) text = "";
            if (S.VpnInNotch && Vpn.Active && S.Mode != "minimal") text = "🔒 " + text;
            // идёт воздушная тревога в «моём месте» — красный значок, пока не будет отбоя
            ColRight.Text = text;
            if (color is Color col) ColRight.Foreground = new SolidColorBrush(col);
            else ColRight.SetResourceReference(TextBlock.ForegroundProperty, "DimBrush");
        }

        Color _ringColor;

        /// <summary>Полоска прогресса трека в свёрнутой чёлке.</summary>
        void UpdateNotchProgress()
        {
            var m = MediaService.I;
            bool show = S.NotchProgress && m.HasSession && m.HasTime && !CallService.I.InCall && S.Mode != "minimal" && _mode == Mode.Collapsed;
            if (!show) { if (ColProgress.Width != 0) ColProgress.Width = 0; return; }
            double w = Math.Max(0, CollapsedView.ActualWidth - 4);
            double p = Math.Clamp(m.Position.TotalSeconds / m.Duration.TotalSeconds, 0, 1);
            ColProgress.Width = Math.Round(w * p);
            ColProgress.Opacity = m.IsPlaying ? 0.85 : 0.4;
        }

        (double P, Color C)? RingState()
        {
            var src = S.RingSource;
            var pomo = PomodoroService.I;
            var m = MediaService.I;
            var t = TimerService.I;
            if (src == "none") return null;
            if (src == "pomodoro" || (src == "auto" && pomo.Active))
                return (pomo.Progress, pomo.Phase == PomoPhase.Focus ? Red : Green);
            if (src == "auto" && t.Active) return (t.Progress, Orange);
            if (src == "media")
                return (m.HasTime ? Math.Clamp(m.Position.TotalSeconds / m.Duration.TotalSeconds, 0, 1) : 0, m.ArtColor);
            if (src == "water" || (src == "auto" && S.PgWater))
                return (Water.Progress, Color.FromRgb(0x0A, 0x84, 0xFF));
            var sys = SystemService.I;
            if (!sys.HasBattery) return null;
            int pct = sys.BatteryPercent;
            return (pct / 100.0, pct <= 20 && !sys.Charging ? Red : Green);
        }

        void UpdateRing()
        {
            var st = RingState();
            if (st is { } s)
            {
                if (s.C != _ringColor)
                {
                    _ringColor = s.C;
                    var b = new SolidColorBrush(s.C);
                    b.Freeze();
                    ColRing.RingBrush = HeadRing.RingBrush = b;
                }
                if (Math.Abs(ColRing.Progress - s.P) > 0.001) ColRing.Progress = HeadRing.Progress = s.P;
            }
            bool otherLeft = ColArt.Visibility == Visibility.Visible || ColNote.Visibility == Visibility.Visible || ColMic.Visibility == Visibility.Visible;
            ColRing.Visibility = st != null && !otherLeft && S.LeftSlot != "none" ? Visibility.Visible : Visibility.Collapsed;
            HeadRing.Visibility = st != null && S.RingInHeader ? Visibility.Visible : Visibility.Collapsed;
        }

        // ================== раз в секунду ==================

        void OnClock()
        {
            var now = DateTime.Now;
            if (now.Second % 2 == 0) UpdateMascot();
            var pomo = PomodoroService.I;
            ColTime.Text = S.Mode == "focus" && pomo.Active ? "🍅 " + Fmt.Time(pomo.Remaining) : ClockText(now);
            TimeText.Text = ClockText(now);
            DateText.Text = Fmt.Date(now);
            DateText.Visibility = S.HeaderDate ? Visibility.Visible : Visibility.Collapsed;

            var sys = SystemService.I;
            HeadBattery.Text = sys.HasBattery && S.ShowBattery ? (sys.Charging ? "⚡" : "") + sys.BatteryPercent + "%" : "";
            if (S.NetInNotch || S.RightSlot == "net" || (Current?.Key == "quick" && _mode == Mode.Expanded)) NetSpeed.Sample();
            HeadExtra.Text = WeatherService.Cached is { } w ? $"{WeatherService.Describe(w.Code).Icon} {Math.Round(w.Temp)}°" : "";
            UpdateRight();
            UpdateNotchProgress();
            if (CallService.I.InCall && _mode == Mode.Expanded) UpdateTitle();
            UpdateRing();

            _sec++;
            if (_hwnd != IntPtr.Zero && _sec % 4 == 0) Native.KeepTopmost(_hwnd);
            if ((S.WeatherInNotch || S.PgWeather) && _sec % 600 == 5) _ = WeatherService.Get();

            // прятать при полноэкранных приложениях: пустой регион — ни одного перехваченного клика
            bool fs = S.HideInFullscreen && Native.IsForegroundFullscreen(_hwnd, _screen);
            if (fs != _hiddenFs)
            {
                if (fs)
                {
                    Collapse(true);
                    EndActivity();
                    DropGlass();
                    _hiddenFs = true;
                    Root.Visibility = Visibility.Hidden;
                    Native.SetWindowRgn(_hwnd, Native.CreateRectRgn(0, 0, 0, 0), true);
                    _rgnKey = "empty";
                }
                else
                {
                    _hiddenFs = false;
                    Root.Visibility = Visibility.Visible;
                    _rgnKey = null;
                    Retarget();
                }
            }

            // «живое» стекло: обновляем снимок под чёлкой
            if (_blur && S.LiveGlass && !_ticking && (now - _glassAt).TotalSeconds >= (_mode == Mode.Collapsed ? 6 : 2.5)) EnsureGlass(true);

            if (sys.HasBattery) WatchBattery(sys);
            Reminders(now);
            if (_sec % 2 == 0) WatchDrives();
            if (_sec % 30 == 1) AutoSchedule(now);

            // экономия памяти: чёлка простаивает → чистим
            if (S.LowMemory && _mode == Mode.Collapsed && !_ticking)
            {
                double idle = (now - _collapsedAt).TotalSeconds;
                if ((idle > 8 && idle < 10) || _sec % 180 == 0) MemoryTrim.Now();
            }
        }

        void WatchDrives()
        {
            var (added, removed) = Drives.Poll();
            if (!S.HudUsb) return;
            // несколько дисков разом — одна всплывашка, а не пачка
            if (added.Count == 1)
            {
                var d = added[0];
                var root = d.Name;
                ShowActivity(new Activity
                {
                    Kind = "usb", Glyph = "", Tint = Color.FromRgb(0x64, 0xD2, 0xFF), Title = "Подключено: " + Drives.Label(d),
                    Sub = Drives.Free(d), ActionText = "Открыть", Action = () => SystemService.Run(root), Seconds = 6, Wide = true,
                });
            }
            else if (added.Count > 1)
            {
                var first = added[0].Name;
                ShowActivity(new Activity
                {
                    Kind = "usb", Glyph = "", Tint = Color.FromRgb(0x64, 0xD2, 0xFF), Title = $"Подключено дисков: {added.Count}",
                    Sub = string.Join(", ", added.Select(Drives.Label)), ActionText = "Открыть", Action = () => SystemService.Run(first), Seconds = 6, Wide = true,
                });
            }
            if (removed.Count > 0 && added.Count == 0)
                ShowActivity(new Activity { Kind = "usb", Glyph = "", Title = "Диск отключён", Sub = string.Join(", ", removed.Select(r => r.TrimEnd('\\'))), Seconds = 2 });
        }

        bool _autoApplied;

        /// <summary>Расписание: вечером сам включается «Ночь» / «Не беспокоить», утром — выключается.</summary>
        void AutoSchedule(DateTime now)
        {
            if (!S.AutoNight) { _autoApplied = false; return; }
            int h = now.Hour, from = S.NightFrom, to = S.NightTo;
            bool inside = from == to ? false : from < to ? h >= from && h < to : h >= from || h < to;
            var mode = S.AutoNightMode is "dnd" or "night" ? S.AutoNightMode : "night";
            if (inside && S.Mode == "normal" && !_autoApplied) { _autoApplied = true; SetMode(mode); }
            else if (!inside && _autoApplied)
            {
                _autoApplied = false;
                if (S.Mode == mode) SetMode("normal");
            }
        }

        void WatchBattery(SystemService sys)
        {
            bool ch = sys.Charging;
            int pct = sys.BatteryPercent;
            if (_batInit && ch != _lastCharging && S.HudBattery)
            {
                ShowActivity(new Activity
                {
                    Kind = "battery", Glyph = ch ? "" : "",
                    Title = ch ? "Зарядка подключена" : "Работа от батареи", Sub = pct + "%",
                    Progress = pct / 100.0, Tint = ch ? Green : Colors.White, Seconds = 2.5,
                });
            }
            if (ch) _lowStage = 0;
            else if (S.HudLowBattery && ((pct <= 10 && _lowStage < 2) || (pct <= 20 && _lowStage < 1)))
            {
                _lowStage = pct <= 10 ? 2 : 1;
                ShowActivity(new Activity
                {
                    Kind = "lowbat", Glyph = "", Title = "Низкий заряд", Sub = $"Осталось {pct}% — подключи зарядку",
                    Progress = pct / 100.0, Tint = Red, Seconds = 5, Wide = true,
                });
            }
            _lastCharging = ch;
            _batInit = true;
        }

        void Reminders(DateTime now)
        {
            if (Water.Due())
            {
                Water.LastReminder = now;
                if (!Modes.Dnd) Sound.Ding();
                ShowActivity(new Activity
                {
                    Kind = "water", Glyph = "💧", GlyphFont = "Segoe UI Emoji", Tint = Color.FromRgb(0x64, 0xD2, 0xFF),
                    Title = "Пора выпить воды", Sub = $"Сегодня {Fmt.Liters(Water.TodayMl)} из {Fmt.Liters(S.WaterGoal)} л",
                    ActionText = $"+{S.WaterCup} мл", Action = () => Water.Drink(S.WaterCup), Seconds = 10, Page = "water", Wide = true,
                });
            }

            if (S.EyeCare && (now - _eyeAt).TotalMinutes >= Math.Max(5, S.EyeInterval))
            {
                _eyeAt = now;
                ShowActivity(new Activity
                {
                    Kind = "eye", Glyph = "", Tint = Color.FromRgb(0x64, 0xD2, 0xFF), Title = "Отдохни глазами",
                    Sub = "Правило 20-20-20: посмотри вдаль 20 секунд", ActionText = "Готово", Action = () => { }, Seconds = 20, Wide = true,
                });
            }
            if (S.StandUp && (now - _standAt).TotalMinutes >= Math.Max(15, S.StandInterval))
            {
                _standAt = now;
                ShowActivity(new Activity
                {
                    Kind = "stand", Glyph = "", Tint = Green, Title = "Пора размяться",
                    Sub = "Встань, потянись, пройдись пару минут", ActionText = "Ок", Action = () => { }, Seconds = 20, Wide = true,
                });
            }

            if (S.CalRemind && S.PgCalendar)
            {
                foreach (var ev in Store.Data.Events)
                {
                    if (ev.Notified || ev.At is not DateTime at) continue;
                    if (now >= at.AddMinutes(-S.CalRemindBefore) && now < at.AddMinutes(10))
                    {
                        ev.Notified = true;
                        Store.SaveData();
                        Sound.Play(SoundEvent.Alert);
                        ShowActivity(new Activity
                        {
                            Kind = "event", Glyph = "", Tint = Red,
                            Title = ev.Title, Sub = at > now ? $"Через {(int)Math.Ceiling((at - now).TotalMinutes)} мин · {ev.Time}" : $"Сейчас · {ev.Time}",
                            Seconds = 10, Page = "calendar", Wide = true, ActionText = "OK", Action = () => { },
                        });
                        break;
                    }
                }
            }

            if (S.PgPrayer && S.PrayerRemind && PrayerService.Cached is { } times)
            {
                foreach (var (name, at) in times)
                {
                    if (name == "Восход") continue;
                    var key = now.ToString("yyyyMMdd") + name;
                    if (_prayerDone.Contains(key)) continue;
                    var d = now.TimeOfDay - at;
                    if (d >= TimeSpan.Zero && d < TimeSpan.FromMinutes(2))
                    {
                        _prayerDone.Add(key);
                        Sound.Ding();
                        ShowActivity(new Activity
                        {
                            Kind = "prayer", Glyph = "", Tint = Color.FromRgb(0x5E, 0xE6, 0xC8),
                            Title = "Время намаза: " + name, Sub = at.ToString(@"hh\:mm"), Seconds = 10, Page = "prayer",
                            ActionText = "OK", Action = () => { },
                        });
                    }
                }
            }
        }

        // ================== окружение ==================

        void ApplyStyle()
        {
            bool fl = Floating;
            Island.Margin = new Thickness(0, fl ? FloatTop : 0, 0, 0);
            bool ears = !fl && !Modes.Hidden;
            EarL.Visibility = EarR.Visibility = ears ? Visibility.Visible : Visibility.Collapsed;
            CollapsedView.Height = BaseH + (fl ? 2 : 0);
            _rgnKey = null;
        }

        void ApplyDelays()
        {
            _openT.Interval = TimeSpan.FromMilliseconds(Math.Clamp(S.HoverDelay, 0, 2000));
            _closeT.Interval = TimeSpan.FromMilliseconds(Math.Clamp(S.CloseDelay, 100, 5000));
        }

        string ClockText(DateTime t) =>
            t.ToString((S.Clock12h ? "h:mm" : "HH:mm") + (S.ClockSeconds ? ":ss" : "") + (S.Clock12h ? " tt" : ""), CultureInfo.InvariantCulture);

        void Reposition()
        {
            var screens = WinForms.Screen.AllScreens;
            _screen = S.Monitor > 0 && S.Monitor <= screens.Length ? screens[S.Monitor - 1] : WinForms.Screen.PrimaryScreen;
            var b = _screen.Bounds;
            Left = b.Left / _dpi + (b.Width / _dpi - Width) / 2 + S.OffsetX;
            Top = b.Top / _dpi;
            _rgnKey = null;
            Apply();
        }

        void OnSetting(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(Settings.Mascot):
                case nameof(Settings.MascotColor):
                case nameof(Settings.MascotMoods):
                case nameof(Settings.MascotHat):
                case nameof(Settings.MascotSize):
                case nameof(Settings.MascotSpeed):
                case nameof(Settings.MascotIdle):
                case nameof(Settings.MascotDance):
                case nameof(Settings.MascotReactPc):
                case nameof(Settings.MascotNight):
                    UpdateMascot();
                    Retarget();
                    break;
                case nameof(Settings.LiquidGlass):
                case nameof(Settings.GlassVibrancy):
                    if (_blur) EnsureGlass(true);
                    Apply();
                    break;
                case nameof(Settings.LiquidRefract):
                    Apply();
                    break;
                case nameof(Settings.IslandGlint):
                case nameof(Settings.BeatRim):
                    SyncLive();
                    break;
                case nameof(Settings.PgMedia):
                case nameof(Settings.PgCalendar):
                case nameof(Settings.PgTodo):
                case nameof(Settings.PgPomodoro):
                case nameof(Settings.PgWater):
                case nameof(Settings.PgPrayer):
                case nameof(Settings.PgShelf):
                case nameof(Settings.PgQuick):
                case nameof(Settings.PgModes):
                case nameof(Settings.PgCenter):
                case nameof(Settings.PgClipboard):
                case nameof(Settings.PgNotes):
                case nameof(Settings.PgTimer):
                case nameof(Settings.PgWeather):
                case nameof(Settings.PgLauncher):
                case nameof(Settings.PgTranslate):
                case nameof(Settings.PgAi):
                case nameof(Settings.PgSound):
                case nameof(Settings.PgMaint):
                case nameof(Settings.PgTools):
                case nameof(Settings.PgVpn):
                case nameof(Settings.PgWifi):
                case nameof(Settings.PgHub):
                case nameof(Settings.PgEq):
                case nameof(Settings.PageOrder):
                    BuildPages();
                    break;
                case nameof(Settings.LyricsTranslate):
                case nameof(Settings.LyricsTranslateTo):
                    _ = MediaService.I.LoadTranslation();
                    break;
                // вид текста песни поменяли в настройках — строка в острове перерисовывается сразу, а не со следующей строкой песни
                case nameof(Settings.LyricsFont):
                case nameof(Settings.LyricsColor):
                case nameof(Settings.LyricsUpper):
                case nameof(Settings.LyricsShadow):
                case nameof(Settings.LyricsAnim):
                    ColLyric.Text = "";
                    UpdateCollapsed();
                    if (_mode == Mode.Collapsed) Retarget();
                    break;
                case nameof(Settings.NotchProgress):
                    UpdateNotchProgress();
                    break;
                case nameof(Settings.LyricsFocus):
                case nameof(Settings.MediaVisual):
                    Retarget();
                    break;
                case nameof(Settings.LiveBars):
                    Bars.Live = S.LiveBars;
                    break;
                case nameof(Settings.CustomBg1):
                case nameof(Settings.CustomBg2):
                case nameof(Settings.CustomRim):
                case nameof(Settings.CustomRimColor):
                case nameof(Settings.CustomSheen):
                case nameof(Settings.CustomGlassCollapsed):
                case nameof(Settings.BlurRadius):
                case nameof(Settings.MaxRadius):
                    ApplyPreset();
                    break;
                case nameof(Settings.NotchHeight):
                    ApplyStyle();
                    break;
                case nameof(Settings.HoverDelay):
                case nameof(Settings.CloseDelay):
                    ApplyDelays();
                    break;
                case nameof(Settings.ClockSeconds):
                case nameof(Settings.Clock12h):
                case nameof(Settings.HeaderDate):
                    OnClock();
                    break;
                case nameof(Settings.Style):
                case nameof(Settings.Hidden):
                case nameof(Settings.Mode):
                    ApplyStyle();
                    if (Modes.Hidden) Collapse(true);
                    break;
                case nameof(Settings.Preset):
                    ApplyPreset();
                    break;
                case nameof(Settings.Monitor):
                case nameof(Settings.OffsetX):
                    Reposition();
                    break;
                case nameof(Settings.Accent):
                case nameof(Settings.ArtColors):
                    var m = MediaService.I;
                    App.ApplyAccent(S.ArtColors && m.Art != null ? m.ArtColor : null);
                    break;
                case nameof(Settings.Hotkey):
                case nameof(Settings.Spotlight):
                    RegisterHotkey();
                    break;
                case nameof(Settings.StartWithWindows):
                    AutoStart.Apply(S.StartWithWindows);
                    break;
                case nameof(Settings.PomoWork):
                case nameof(Settings.PomoShort):
                case nameof(Settings.PomoLong):
                    PomodoroService.I.SettingsChanged();
                    break;
                case nameof(Settings.PageIcons):
                    RenderDots(false);
                    break;
                case nameof(Settings.Glass):
                case nameof(Settings.LiteMode):
                    if (!S.Glass || S.LiteMode) DropGlass();
                    break;
                case nameof(Settings.ClipWatch):
                case nameof(Settings.ClipSave):
                    ClipboardService.I.Apply();
                    break;
                case nameof(Settings.HudKeyboard):
                case nameof(Settings.HudCaps):
                    KeyboardWatch.I.Apply();
                    break;
                case nameof(Settings.WeatherInNotch):
                case nameof(Settings.WeatherCity):
                    if (S.WeatherInNotch || S.PgWeather) _ = WeatherService.Get(true);
                    break;
            }
            UpdateCollapsed();
            Retarget();
        }

        /// <summary>Ctrl+Alt+ N — чёлка, H — скрыть, D — не беспокоить, C — пипетка, V — буфер, T — перевод.</summary>
        static readonly (int Id, uint Vk)[] Hotkeys = { (1, 0x4E), (2, 0x48), (3, 0x44), (4, 0x43), (5, 0x56), (6, 0x54), (7, 0x41), (8, 0x4C), (9, 0x20) };

        /// <summary>Горячие клавиши, которые не удалось занять (их уже держит другая программа) — показываем в настройках.</summary>
        public static readonly List<string> BusyHotkeys = new();

        void RegisterHotkey()
        {
            if (_hwnd == IntPtr.Zero) return;
            BusyHotkeys.Clear();
            foreach (var (id, vk) in Hotkeys)
            {
                Native.UnregisterHotKey(_hwnd, id);
                bool want = S.Hotkey && (id != 9 || S.Spotlight);
                if (want && !Native.RegisterHotKey(_hwnd, id, 0x2 | 0x1 | 0x4000, vk))
                    BusyHotkeys.Add("Ctrl+Alt+" + (vk == 0x20 ? "Space" : ((char)vk).ToString()));
            }
        }

        void OnHotkey(int id)
        {
            switch (id)
            {
                case 1:
                    if (_mode == Mode.Expanded) Collapse(true);
                    else Expand(null, true);
                    break;
                case 2:
                    if (Modes.Hidden) Unhide(); else HideIsland();
                    break;
                case 3:
                    SetMode(Modes.Dnd ? "normal" : "dnd");
                    break;
                case 4:
                    Collapse(true);
                    ColorPicker.I.Start();
                    break;
                case 5:
                    if (S.PgClipboard) Expand("clipboard", true);
                    break;
                case 6:
                    if (S.PgTranslate) Expand("translate", true);
                    break;
                case 7:
                    if (S.PgAi) Expand("ai", true);
                    break;
                case 8:
                    App.ShowCenter();
                    break;
                case 9:
                    SpotlightWindow.Toggle();
                    break;
            }
        }

        static readonly int _taskbarCreated = Native.RegisterWindowMessage("TaskbarCreated");

        /// <summary>После сна, смены мониторов или перезапуска Проводника: остров на место, поверх всех, стекло заново.</summary>
        void Recover()
        {
            if (_hwnd == IntPtr.Zero) return;
            _dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
            Reposition();
            Native.KeepTopmost(_hwnd);
            RegisterHotkey();
            _rgnKey = null;
            if (_blur) EnsureGlass(true);
            Retarget(true);
        }

        IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == Native.WM_HOTKEY)
            {
                OnHotkey(wParam.ToInt32());
                handled = true;
            }
            else if (msg == ClipboardService.WM_CLIPBOARDUPDATE)
            {
                ClipboardService.I.OnUpdate();
            }
            else if (msg == 0x0219 && wParam.ToInt32() is 0x8000 or 0x8004) // WM_DEVICECHANGE: подключили/отключили — сразу проверяем диски
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(WatchDrives));
            }
            else if (msg == 0x02E0) // WM_DPICHANGED: окно переехало на монитор с другим масштабом
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    _dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
                    Reposition();
                    Retarget(true);
                }));
            }
            else if (msg == _taskbarCreated && _taskbarCreated != 0) // перезапустился Проводник — возвращаем остров поверх всех окон
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(Recover));
            }
            return IntPtr.Zero;
        }

        static T FindParent<T>(DependencyObject d) where T : DependencyObject
        {
            while (d != null)
            {
                if (d is T t) return t;
                d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            }
            return null;
        }
    }
}
