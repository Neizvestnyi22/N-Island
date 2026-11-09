using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NIsland.Controls;
using NIsland.Core;
using NIsland.Pages;
using NIsland.Services;

namespace NIsland
{
    // 1.8: «живой» остров — персонаж, жидкое стекло с преломлением, блик за курсором, ободок в такт, отблеск на уведомлениях
    public partial class IslandWindow
    {
        Mascot _mascot;
        Image _lens;
        Border _glint, _shine;
        Canvas _glintHost;
        (double X, double Y)? _glintTest;
        RadialGradientBrush _lensMask;
        bool _liveHooked;
        double _mouseX = -1, _mouseY = -1, _glintX, _glintY = -1, _liveT, _rimLevel;

        bool MascotOn => S.Mascot != "none" && S.Mode != "minimal" && !Modes.Hidden;

        void InitLive()
        {
            // персонаж — отдельной колонкой слева в свёрнутой чёлке
            CollapsedView.ColumnDefinitions.Insert(0, new ColumnDefinition { Width = GridLength.Auto });
            foreach (UIElement ch in CollapsedView.Children) Grid.SetColumn(ch, Grid.GetColumn(ch) + 1);
            Grid.SetColumn(ColProgress, 0);
            Grid.SetColumnSpan(ColProgress, 4);
            _mascot = new Mascot { Width = 22, Height = 22, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            CollapsedView.Children.Add(_mascot);

            // линза: тот же снимок под стеклом, чуть увеличенный и видимый только у краёв — так стекло «преломляет» фон
            _lensMask = new RadialGradientBrush { MappingMode = BrushMappingMode.Absolute };
            _lensMask.GradientStops.Add(new GradientStop(Colors.Transparent, 0));
            _lensMask.GradientStops.Add(new GradientStop(Colors.Transparent, 0.62));
            _lensMask.GradientStops.Add(new GradientStop(Color.FromArgb(0xF0, 0, 0, 0), 1));
            _lens = new Image
            {
                Stretch = Stretch.Fill, IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                Visibility = Visibility.Collapsed, OpacityMask = _lensMask, Effect = new BlurEffect { Radius = 6, RenderingBias = RenderingBias.Performance },
            };
            RenderOptions.SetBitmapScalingMode(_lens, BitmapScalingMode.Linear);
            Stage.Children.Insert(0, _lens);

            // блик, который следует за курсором
            var gb = new RadialGradientBrush();
            gb.GradientStops.Add(new GradientStop(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF), 0));
            gb.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 1));
            _glint = new Border { IsHitTestVisible = false, Width = 160, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Background = gb, RenderTransform = new TranslateTransform(), Opacity = 0 };
            _glint.HorizontalAlignment = HorizontalAlignment.Left;
            _glint.VerticalAlignment = VerticalAlignment.Top;
            // холст не обрезает детей по своей ячейке — остров сам обрежет блик по своей форме
            _glintHost = new Canvas { IsHitTestVisible = false };
            _glintHost.Children.Add(_glint);
            Stage.Children.Insert(Math.Min(Stage.Children.Count, 3), _glintHost);
            // тестовый режим: NISLAND_GLINT=x,y (доли ширины и высоты) — «курсор» в этой точке без мыши, для скриншотов
            var gt = Environment.GetEnvironmentVariable("NISLAND_GLINT");
            if (gt != null && gt.Split(',') is { Length: 2 } gp && double.TryParse(gp[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var gx)
                && double.TryParse(gp[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var gy))
                _glintTest = (gx, gy);

            // отблеск, пробегающий по острову при новом событии
            var sb = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            sb.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0));
            sb.GradientStops.Add(new GradientStop(Color.FromArgb(0x55, 255, 255, 255), 0.5));
            sb.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1));
            _shine = new Border
            {
                IsHitTestVisible = false, Width = 90, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Stretch, Background = sb,
                RenderTransform = new TransformGroup { Children = { new SkewTransform(-20, 0), new TranslateTransform(-200, 0) } }, Visibility = Visibility.Collapsed,
            };
            Stage.Children.Add(_shine);

            Island.MouseMove += (_, e) =>
            {
                var pt = e.GetPosition(Island); _mouseX = pt.X; _mouseY = pt.Y;
                if (OverMascot(e)) { if (S.MascotPlay) _mascot.Stroke(e.GetPosition(_mascot).X); } else _mascot?.StrokeEnd();
            };
            Island.MouseLeave += (_, __) => { _mouseX = -1; _mouseY = -1; _mascot?.StrokeEnd(); Grow(false); };
            Island.MouseEnter += (_, __) => Grow(true);
            ApplyShadow();
            // колесо над персонажем — щекотка (остров при этом не листает страницы и не меняет громкость)
            Island.PreviewMouseWheel += (_, e) =>
            {
                if (_mode == Mode.Expanded || !S.MascotPlay || !OverMascot(e)) return;
                _mascot.Tickle();
                e.Handled = true;
            };
            Ai.Failed += _ => Dispatcher.BeginInvoke(new Action(() => React("confused", 2.4)));
            Island.MouseEnter += (_, __) => { SyncLive(); if (MascotOn && S.MascotMoods) _mascot.Flash("happy", 1.2, jump: false); };
            MediaService.I.Changed += () => Dispatcher.BeginInvoke(new Action(() => { SyncLive(); UpdateMascot(); }));
            Ai.BusyChanged += () => Dispatcher.BeginInvoke(new Action(UpdateMascot));
            // прямые реакции — работают, даже если всплывашки этих событий выключены
            ClipboardService.I.Copied += _ => Dispatcher.BeginInvoke(new Action(() => React("wink", 1.6)));
            NotificationLog.Changed += () => Dispatcher.BeginInvoke(new Action(() =>
            {
                int n = NotificationLog.Items.Count;
                if (n > _notifSeen) React("curious", 2);
                _notifSeen = n;
            }));
            // строка песни про любовь — сердечки (не чаще раза в 20 с)
            MediaService.I.LyricIndexChanged += () => Dispatcher.BeginInvoke(new Action(() =>
            {
                var line = MediaService.I.CurrentLyric;
                if (line != null && (DateTime.Now - _loveAt).TotalSeconds > 20 && LoveWords.IsMatch(line)) { _loveAt = DateTime.Now; React("love", 2.2); }
            }));
            // звук: выключили — зевает, очень громко — удивляется, сменили устройство (наушники) — прислушивается
            AudioService.I.Changed += (vol, muted, _) => Dispatcher.BeginInvoke(new Action(() =>
            {
                if (muted && !_wasMuted) React("yawn", 1.8);
                // на полную: под музыку — качает головой, без музыки — поражён
                else if (!muted && vol > 0.9 && _lastVol <= 0.9 && _lastVol >= 0) React(MediaService.I.IsPlaying ? "headbang" : "amazed", 2);
                _wasMuted = muted;
                _lastVol = vol;
            }));
            AudioService.I.OutputChanged += _ => Dispatcher.BeginInvoke(new Action(() => React("listen", 2)));
            // интернет пропал — грустит, вернулся — радуется
            System.Net.NetworkInformation.NetworkChange.NetworkAvailabilityChanged += (_, e) =>
                Dispatcher.BeginInvoke(new Action(() => React(e.IsAvailable ? "happy" : "sad", e.IsAvailable ? 2 : 3)));
            // разблокировали ПК — машет; проснулся после сна — зевает, потом машет
            Microsoft.Win32.SystemEvents.SessionSwitch += (_, e) =>
            {
                if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionLock) _lockedAt = DateTime.Now;
                // разблокировали: после долгого отсутствия — воздушный поцелуй, иначе машет
                if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionUnlock)
                    Dispatcher.BeginInvoke(new Action(() => React(_lockedAt != DateTime.MinValue && (DateTime.Now - _lockedAt).TotalMinutes > 30 ? "kiss" : "wave", 2.4)));
            };
            Microsoft.Win32.SystemEvents.PowerModeChanged += (_, e) =>
            {
                if (e.Mode != Microsoft.Win32.PowerModes.Resume) return;
                Dispatcher.BeginInvoke(new Action(() => React("yawn", 2.5)));
                _ = System.Threading.Tasks.Task.Delay(3000).ContinueWith(_ => Dispatcher.BeginInvoke(new Action(() => React("wave", 2))));
            };
            // музыку продолжили после паузы — радуется
            // новый трек — прислушивается; трек пролистали за пару секунд — удивляется
            MediaService.I.TrackChanged += () => Dispatcher.BeginInvoke(new Action(() =>
            {
                var now = DateTime.Now;
                // пролистал три трека подряд за полминуты — озадачен: «да что ж тебе всё не то?»
                _skips = (now - _trackAt).TotalSeconds < 10 ? _skips + 1 : 0;
                React(_skips >= 3 ? "confused" : (now - _trackAt).TotalSeconds < 8 ? "surprised" : "listen", _skips >= 3 ? 2.2 : 1.5);
                if (_skips >= 3) _skips = 0;
                _trackAt = now;
            }));
            // избранное и много копирований подряд
            ClipboardService.I.Copied += _ => Dispatcher.BeginInvoke(new Action(() =>
            {
                var now = DateTime.Now;
                _copies = (now - _copyAt).TotalSeconds < 12 ? _copies + 1 : 1;
                _copyAt = now;
                if (_copies == 5) React("excited", 2);
            }));
            MediaService.I.PlaybackChanged += () => Dispatcher.BeginInvoke(new Action(() =>
            {
                bool p = MediaService.I.IsPlaying;
                if (p && !_wasPlaying && MediaService.I.HasSession) React("happy", 1.4);
                _wasPlaying = p;
            }));
            UpdateMascot();
        }

        bool _wasMuted, _wasPlaying;
        int _skips, _copies;
        DateTime _copyAt, _lockedAt = DateTime.MinValue, _boredAt, _sickAt, _scaredAt;

        bool OverMascot(System.Windows.Input.MouseEventArgs e)
        {
            if (_mascot == null || !MascotOn || _mascot.Visibility != Visibility.Visible || _mascot.ActualWidth < 4) return false;
            var p = e.GetPosition(_mascot);
            const double pad = 4;
            return p.X >= -pad && p.Y >= -pad && p.X <= _mascot.ActualWidth + pad && p.Y <= _mascot.ActualHeight + pad;
        }
        double _lastVol = -1;
        int _lastBattery = -1, _lastHourCheer = -1;
        DateTime _hoverSince = DateTime.MinValue, _dizzyAt, _heatAt, _trackAt;

        /// <summary>Реакция персонажа по запросу других частей приложения (Telegram, MCP, агенты). Можно звать из любого потока.</summary>
        public void MascotReact(string mood, double seconds = 2) => Dispatcher.BeginInvoke(new Action(() => React(mood, seconds)));

        /// <summary>Тык из Telegram-виджета: та же реакция, что от клика по персонажу.</summary>
        public void MascotPoke() => Dispatcher.BeginInvoke(new Action(() => { if (_mascot != null && MascotOn) _mascot.Poke(); }));

        /// <summary>Текущее настроение персонажа (с учётом короткой эмоции) — для виджета в Telegram.</summary>
        public string MascotMood => Dispatcher.Invoke(() => _mascot?.Current ?? "happy");

        public string MascotTintHex => Dispatcher.Invoke(() => _mascot == null ? null : $"#{_mascot.Tint.R:X2}{_mascot.Tint.G:X2}{_mascot.Tint.B:X2}");

        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);

        /// <summary>Чем сейчас занят пользователь: редактор кода и офис — сосредоточен.</summary>
        static IntPtr _fgWnd;
        static bool _fgWorking;
        static bool UserIsWorking()
        {
            try
            {
                var w = GetForegroundWindow();
                if (w == _fgWnd) return _fgWorking;   // то же окно — не спрашиваем процесс заново
                _fgWnd = w;
                GetWindowThreadProcessId(w, out var pid);
                if (pid == 0) return _fgWorking = false;
                using var p = System.Diagnostics.Process.GetProcessById((int)pid);
                return _fgWorking = p.ProcessName.ToLowerInvariant() is "code" or "devenv" or "rider64" or "idea64" or "pycharm64" or "webstorm64" or "notepad++" or "sublime_text"
                    or "winword" or "excel" or "powerpnt" or "cursor" or "windsurf" or "zed" or "obsidian" or "notion" or "unity" or "blender";
            }
            catch { return false; }
        }

        /// <summary>События по времени и состоянию ПК: батарея, память, жара, начало часа, вечер, пятница, долгий взгляд курсора.</summary>
        void MascotTicks(string mood)
        {
            var sys = SystemService.I;
            var now = DateTime.Now;
            if (S.MascotReactPc && sys.HasBattery)
            {
                int b = sys.BatteryPercent;
                if (_lastBattery > 20 && b <= 20 && !sys.Charging) React("sad", 3);                 // заряд упал ниже 20 %
                // 5 % и меньше без зарядки — пугается (не чаще раза в 3 минуты)
                if (b <= 5 && !sys.Charging && (now - _scaredAt).TotalMinutes > 3) { _scaredAt = now; React("scared", 3); }
                if (_lastBattery >= 0 && _lastBattery < 100 && b >= 100 && sys.Charging) React("party", 3);   // зарядился полностью
                _lastBattery = b;
            }
            if (S.MascotReactPc && sys.RamPercent() > 90 && (now - _dizzyAt).TotalMinutes > 5) { _dizzyAt = now; React("dizzy", 2.5); }
            if (S.MascotReactPc && mood == "idle" && WeatherService.Cached is { } w && w.Temp >= 28 && (now - _heatAt).TotalMinutes > 5) { _heatAt = now; React("hot", 3); }
            // память почти кончилась или системный диск забит — «болеет» (раз в 20 минут)
            if (S.MascotReactPc && (now - _sickAt).TotalMinutes > 20 && (sys.RamPercent() > 95 || SysDriveLow()))
            {
                _sickAt = now; React("sick", 3);
            }
            // долго ничего не происходит (3–10 мин без ввода, без музыки) — скучает
            if (mood == "idle" && (now - _boredAt).TotalMinutes > 4 && IdleMinutes() is > 3 and < 10 && !MediaService.I.IsPlaying)
            {
                _boredAt = now; React("bored", 3);
            }
            // начало часа — радуется; поздний вечер — зевает; пятница вечер — праздник
            if (now.Minute == 0 && now.Hour != _lastHourCheer)
            {
                _lastHourCheer = now.Hour;
                React(now.DayOfWeek == DayOfWeek.Friday && now.Hour == 18 ? "party" : now.Hour is 22 or 23 ? "yawn" : "happy", 2.4);
            }
            // курсор долго держат над островом — подмигивает или влюбляется
            if (_hover) { if (_hoverSince == DateTime.MinValue) _hoverSince = now; else if ((now - _hoverSince).TotalSeconds > 4) { _hoverSince = now.AddSeconds(6); React(now.Second % 2 == 0 ? "wink" : "love", 2); } }
            else _hoverSince = DateTime.MinValue;
        }

        static DateTime _driveAt;
        static bool _driveLow;
        static bool SysDriveLow()
        {
            if ((DateTime.Now - _driveAt).TotalMinutes < 10) return _driveLow;
            _driveAt = DateTime.Now;
            try
            {
                var d = new System.IO.DriveInfo(System.IO.Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\");
                _driveLow = d.IsReady && d.TotalSize > 0 && d.AvailableFreeSpace < d.TotalSize / 20;
            }
            catch { _driveLow = false; }
            return _driveLow;
        }

        static readonly System.Text.RegularExpressions.Regex LoveWords = new(@"\b(love|lover|кохаю|кохання|люблю|любовь|любви|любимая|любимый|сердце|серце|heart|amor|liebe)\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);
        DateTime _loveAt, _spinAt;
        int _notifSeen = -1;

        /// <summary>Короткая эмоция персонажа, если он включён и реагирует на события.</summary>
        void React(string mood, double seconds)
        {
            if (_mascot == null || !MascotOn || !S.MascotMoods || !S.MascotReactEvents) return;
            if (_mascot.Current is "sleep" && mood is not ("love" or "wave")) return;   // спящего мелочами не будим
            _mascot.Flash(mood, seconds, jump: mood is "love" or "party");
        }

        // ------------------------------------------------------------------
        //  Персонаж
        // ------------------------------------------------------------------

        [StructLayout(LayoutKind.Sequential)] struct LASTINPUTINFO { public uint cbSize, dwTime; }
        [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO p);

        static double IdleMinutes()
        {
            var li = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
            return GetLastInputInfo(ref li) ? unchecked((uint)Environment.TickCount - li.dwTime) / 60000.0 : 0;
        }

        void UpdateMascot()
        {
            if (_mascot == null) return;
            bool on = MascotOn;
            _mascot.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            if (!on) return;
            var m = MediaService.I;
            var sys = SystemService.I;
            _mascot.Skin = S.Mascot;
            var accent = (UI.Res("AccentBrush") as SolidColorBrush)?.Color ?? Colors.DodgerBlue;
            _mascot.Tint = S.MascotColor switch
            {
                "accent" => accent,
                "art" => m.HasSession && m.Art != null ? Soft(m.ArtColor) : accent,
                _ => ColorUtil.Parse(S.MascotColor, accent),
            };
            // вид: аксессуар, размер, скорость, «живёт сам»
            _mascot.Hat = S.MascotHat;
            _mascot.Speed = Math.Clamp(S.MascotSpeed, 50, 200) / 100.0;
            _mascot.IdleAnims = S.MascotIdle && S.MascotMoods;
            double size = Math.Round(22 * Math.Clamp(S.MascotSize, 70, 140) / 100.0);
            if (Math.Abs(_mascot.Width - size) > 0.5) { _mascot.Width = _mascot.Height = size; Retarget(); }
            if (!S.MascotMoods) { _mascot.Mood = "idle"; return; }

            int hour = DateTime.Now.Hour;
            double idle = IdleMinutes();
            bool pc = S.MascotReactPc;
            // нагрузка: «жарко», если процессор занят больше 85 % дольше 10 секунд
            int cpu = pc ? sys.CpuPercent() : 0;
            if (cpu > 85) { if (_hotSince == DateTime.MinValue) _hotSince = DateTime.Now; } else _hotSince = DateTime.MinValue;
            bool hot = _hotSince != DateTime.MinValue && (DateTime.Now - _hotSince).TotalSeconds > 10;
            var mood =
                CallService.I.InCall ? "listen"
                : Ai.Busy > 0 ? "think"
                : S.Mode is "dnd" or "night" || (S.MascotNight && hour < 6 && !m.IsPlaying) || idle > 10 ? "sleep"
                : pc && sys.HasBattery && sys.BatteryPercent <= 8 && !sys.Charging ? "cry"
                : pc && sys.HasBattery && sys.BatteryPercent <= 15 && !sys.Charging ? "sad"
                : hot ? "hot"
                : S.MascotDance && m.HasSession && m.IsPlaying ? "dance"
                : pc && S.Mode == "game" ? "cool"
                : (S.Mode == "focus" && PomodoroService.I.Active) || TimerService.I.Running ? "focus"
                : _hover ? "happy"
                : pc && UserIsWorking() ? "focus"   // пишешь код или документ — сосредоточен вместе с тобой
                : "idle";
            _mascot.Mood = mood;
            MascotTicks(mood);

            // переходы — короткие эмоции
            if (_wasAway && idle < 0.2) _mascot.Flash("wave", 2.4);                         // пользователь вернулся
            _wasAway = idle > 10;
            bool charging = sys.HasBattery && sys.Charging;
            if (pc && charging && !_wasCharging && _seenBattery) _mascot.Flash("charging", 3);  // подключили зарядку
            _wasCharging = charging;
            _seenBattery = true;
            bool aiBusy = Ai.Busy > 0;
            if (_wasAiBusy && !aiBusy) _mascot.Flash(DateTime.Now.Second % 3 == 0 ? "proud" : "happy", 1.6);   // ИИ ответил
            _wasAiBusy = aiBusy;
            // мороз за окном — иногда ёжится
            if (pc && mood == "idle" && WeatherService.Cached is { } w && w.Temp <= 0 && (DateTime.Now - _coldAt).TotalMinutes > 3)
            {
                _coldAt = DateTime.Now;
                _mascot.Flash("cold", 3, jump: false);
            }
        }

        DateTime _hotSince = DateTime.MinValue, _coldAt;
        bool _wasAway, _wasCharging, _seenBattery, _wasAiBusy;

        /// <summary>Цвет обложки бывает слишком тёмным — персонаж должен быть виден на тёмной чёлке.</summary>
        static Color Soft(Color c)
        {
            double l = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255;
            double k = l < 0.45 ? 0.45 - l + 0.15 : 0.05;
            return Color.FromRgb((byte)(c.R + (255 - c.R) * k), (byte)(c.G + (255 - c.G) * k), (byte)(c.B + (255 - c.B) * k));
        }

        /// <summary>Событие острова → короткая эмоция и отблеск.</summary>
        void OnLiveActivity(Activity a)
        {
            if (a == null) return;
            if (_mascot != null && MascotOn && S.MascotMoods)
            {
                // на всё своя реакция; громкость и клавиши (частые) — без эмоций
                var mood = a.Kind switch
                {
                    "volume" or "kbd" or "mic" or "pick" => null,
                    "lowbat" => S.MascotReactPc ? "cry" : null,
                    "battery" => SystemService.I.Charging ? "charging" : "happy",
                    "alert" => "alert",
                    "track" or "media" => "happy",
                    "incall" or "callstart" => "listen",
                    "timer" or "pomodoro" or "event" => "party",
                    "agent" => (a.Title ?? "").Contains("законч", StringComparison.OrdinalIgnoreCase) || (a.Title ?? "").Contains("готов", StringComparison.OrdinalIgnoreCase) ? "party" : "curious",
                    "clip" => "wink",
                    "usb" or "output" => "surprised",
                    "vpn" or "wireguard" or "openvpn" => "cool",
                    "mode" => S.Mode == "game" ? "cool" : S.Mode is "dnd" or "night" ? "yawn" : "happy",
                    "water" or "stand" or "eye" or "hello" => "wave",
                    "hub" or "toast" or "app" or "text" or "windows" or "prayer" => "curious",
                    _ => "surprised",
                };
                if (mood != null && (S.MascotReactEvents || a.Kind is "track" or "incall")) _mascot.Flash(mood, mood is "party" or "charging" ? 3 : 2.2);
            }
            Shimmer();
        }

        /// <summary>Клик по персонажу: реакция вместо раскрытия острова. true — клик был по нему.</summary>
        bool PokeMascot(System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_mascot == null || !S.MascotPoke || !MascotOn || _mascot.Visibility != Visibility.Visible || _mascot.ActualWidth < 4) return false;
            var p = e.GetPosition(_mascot);
            const double pad = 4;   // персонаж маленький — даём попасть
            if (p.X < -pad || p.Y < -pad || p.X > _mascot.ActualWidth + pad || p.Y > _mascot.ActualHeight + pad) return false;
            if (e.ClickCount >= 2 && S.MascotPlay) _mascot.DoublePoke();
            else _mascot.Poke();
            return true;
        }

        /// <summary>Сердечки, когда трек добавили в избранное.</summary>
        public void MascotLove()
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(MascotLove)); return; }
            if (_mascot != null && MascotOn) _mascot.Flash("love", 2.5);
        }

        // ------------------------------------------------------------------
        //  Отблеск
        // ------------------------------------------------------------------

        void Shimmer()
        {
            if (!S.IslandShimmer || _shine == null || S.LiteMode) return;
            var tr = (TranslateTransform)((TransformGroup)_shine.RenderTransform).Children[1];
            _shine.Visibility = Visibility.Visible;
            var a = new DoubleAnimation(-120, Math.Max(_tw, 300) + 60, TimeSpan.FromMilliseconds(900)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }, BeginTime = TimeSpan.FromMilliseconds(120) };
            a.Completed += (_, __) => _shine.Visibility = Visibility.Collapsed;
            tr.BeginAnimation(TranslateTransform.XProperty, a);
        }

        // ------------------------------------------------------------------
        //  Кадры: блик за курсором и ободок в такт
        // ------------------------------------------------------------------

        void SyncLive()
        {
            // кадры нужны только под курсором (блик) или пока играет музыка (ободок в такт); таймер, а не CompositionTarget.Rendering —
            // тот заставляет WPF перерисовывать окно на частоте монитора даже без изменений
            bool want = !S.LiteMode && !_hiddenFs && ((S.IslandGlint && (_hover || _glintTest != null)) || (S.BeatRim && MediaService.I.IsPlaying));
            if (want && !_liveHooked)
            {
                _liveHooked = true;
                _liveClock = FrameClock.Add(OnLiveFrame, 30);
            }
            else if (!want && _liveHooked)
            {
                _liveHooked = false;
                _liveClock?.Dispose();
                _liveClock = null;
                Rim.Opacity = 1;
                _glint.Opacity = 0;
            }
        }

        IDisposable _liveClock;
        Color _glintCol = Colors.White;

        // ---- 2.2: свёрнутый остров чуть подрастает под курсором; мягкая тень ----
        readonly ScaleTransform _grow = new(1, 1);

        void Grow(bool on)
        {
            if (Island.RenderTransform != _grow) { Island.RenderTransformOrigin = new Point(0.5, 0); Island.RenderTransform = _grow; }
            double to = on && S.IslandHoverGrow && _mode == Mode.Collapsed && !Modes.Hidden && !S.LiteMode ? 1.045 : 1;
            // всегда запускаем анимацию к цели: раньше «уже почти 1» при раскрытии наведением пропускало сжатие, рост доигрывал до 104,5 %,
            // и нижний ряд раскрытого острова (кнопки плеера) выходил за область окна, по которой Windows отдаёт клики
            var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(on ? 220 : 160)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            _grow.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            _grow.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }

        void ApplyShadow()
        {
            Island.Effect = S.IslandShadow && !S.LiteMode ? new DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Direction = 270, Opacity = 0.45, Color = Colors.Black, RenderingBias = RenderingBias.Performance } : null;
        }

        void OnLiveFrame(double dt)
        {
            Perf.Hit("live-frame");
            dt = Math.Min(dt, 0.1);
            _liveT += dt;
            double w = Island.ActualWidth, h = Island.ActualHeight;

            if (S.IslandGlint)
            {
                // 2.2: блик ходит за курсором по ВСЕЙ панели — и по горизонтали, и по вертикали (раньше был прибит к верхнему краю
                // и в раскрытом острове закрывал только верхнюю половину); размер — от короткой стороны, мягкий край
                bool mouse = _mouseX >= 0 || _glintTest != null;
                double mx = _glintTest is { } gt0 ? gt0.X * w : _mouseX, my = _glintTest is { } gt1 ? gt1.Y * h : _mouseY;
                // центр не уходит за край панели — яркое место доходит до каждого угла, а не обрывается
                double tx = mouse ? Math.Clamp(mx, 0, w) : w / 2 + Math.Sin(_liveT * 0.35) * w * 0.35;
                double ty = mouse ? Math.Clamp(my, 0, h) : h / 2 + Math.Cos(_liveT * 0.27) * h * 0.25;
                if (_glintY < 0) _glintY = ty;
                double k = Math.Min(1, dt * 9);
                _glintX += (tx - _glintX) * k;
                _glintY += (ty - _glintY) * k;
                double gk = S.GlintSize / 100.0;
                if (_mode == Mode.Expanded)
                {
                    double size = Math.Clamp(Math.Min(w, h) * 1.1, 90, 360) * gk;
                    _glint.Width = size * 1.35;
                    _glint.Height = size;
                }
                else
                {
                    // свёрнутая чёлка низкая и широкая: пятно вытянуто по ней и выше неё — светится вся высота, края мягкие
                    _glint.Width = Math.Clamp(w * 0.55, 110, 300) * gk;
                    _glint.Height = Math.Max(h * 2.4, 64) * gk;
                }
                ((TranslateTransform)_glint.RenderTransform).X = _glintX - _glint.Width / 2;
                ((TranslateTransform)_glint.RenderTransform).Y = _glintY - _glint.Height / 2;
                double op = (mouse ? 0.95 : _mode == Mode.Expanded ? 0.3 : 0.5) * S.GlintStrength / 100.0;
                // цвет блика: белый, акцент или цвет обложки — кисть пересобирается только при смене цвета
                var gc = S.GlintColor switch
                {
                    "accent" => (UI.Res("AccentBrush") as SolidColorBrush)?.Color ?? Colors.White,
                    "art" => MediaService.I.HasSession && MediaService.I.Art != null ? Soft(MediaService.I.ArtColor) : (UI.Res("AccentBrush") as SolidColorBrush)?.Color ?? Colors.White,
                    _ => Colors.White,
                };
                if (gc != _glintCol)
                {
                    _glintCol = gc;
                    var gb2 = new RadialGradientBrush();
                    gb2.GradientStops.Add(new GradientStop(Color.FromArgb(gc == Colors.White ? (byte)0x60 : (byte)0x80, gc.R, gc.G, gc.B), 0));
                    gb2.GradientStops.Add(new GradientStop(Color.FromArgb(0, gc.R, gc.G, gc.B), 1));
                    gb2.Freeze();
                    _glint.Background = gb2;
                }
                _glint.Opacity += (op - _glint.Opacity) * Math.Min(1, dt * 5);
            }
            else _glint.Opacity = 0;

            if (S.BeatRim && MediaService.I.IsPlaying && AudioCapture.I.Running)
            {
                double lv = Math.Min(1, AudioCapture.I.Level * 1.4);
                _rimLevel = lv > _rimLevel ? lv : _rimLevel + (lv - _rimLevel) * Math.Min(1, dt * 4);
                Rim.Opacity = 0.55 + _rimLevel * 0.9;
                if (_mascot != null)
                {
                    _mascot.Level = _rimLevel;
                    _mascot.Bpm = AudioCapture.I.Bpm;
                    // мощный удар (дроп) — иногда кружится от восторга
                    if (S.MascotDance && _mascot.Current == "dance" && lv > 0.92 && AudioCapture.I.SinceBeat < 0.06 && (DateTime.Now - _spinAt).TotalSeconds > 9)
                    {
                        _spinAt = DateTime.Now;
                        _mascot.Flash("spin", 1.0, jump: true);
                    }
                }
            }
            else Rim.Opacity = 1;

            if (_mascot != null && _mascot.IsVisible)
                _mascot.LookX = _mouseX >= 0 && w > 0 ? Math.Clamp(_mouseX / w * 2 - 1, -1, 1) : Math.Sin(_liveT * 0.5) * 0.4;

            // музыка кончилась, курсор ушёл — перестаём рисовать
            if (!_hover && !(S.BeatRim && MediaService.I.IsPlaying)) SyncLive();
        }

        // ------------------------------------------------------------------
        //  Жидкое стекло
        // ------------------------------------------------------------------

        /// <summary>Линза по краям: подогнать под текущий размер острова (вызывается из Apply на каждом кадре анимации).</summary>
        void ApplyLens(double w, double h)
        {
            if (_lens == null) return;
            bool on = _blur && S.LiquidGlass && !S.LiteMode && GlassImg.Source != null && S.LiquidRefract > 0;
            _lens.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            if (!on) return;
            _lens.Source = GlassImg.Source;
            _lens.Width = GlassImg.Width;
            _lens.Height = GlassImg.Height;
            double islandLeft = (Root.ActualWidth - w) / 2, islandTop = Island.Margin.Top;
            double lx = GlassImg.Margin.Left - islandLeft, ly = -islandTop;
            _lens.Margin = new Thickness(lx, ly, 0, 0);
            double k = 1 + Math.Clamp(S.LiquidRefract, 0, 100) / 100.0 * 0.14;
            double cx = w / 2 - lx, cy = h / 2 - ly;
            _lens.RenderTransform = new ScaleTransform(k, k, cx, cy);
            _lensMask.Center = _lensMask.GradientOrigin = new Point(cx, cy);
            _lensMask.RadiusX = w / 2;
            _lensMask.RadiusY = Math.Max(h / 2, 8);
            if (_lens.Effect is BlurEffect be) be.Radius = Math.Clamp(S.BlurRadius * 0.45, 2, 30);
            _lens.Opacity = Math.Clamp(1.05 - _bgA * 0.7, 0.25, 1);
        }

        /// <summary>Яркость и насыщенность фона под стеклом — как «вибрантность» в Apple.</summary>
        static BitmapSource Vibrant(BitmapSource src, double sat, double lift)
        {
            try
            {
                var conv = new FormatConvertedBitmap(src, PixelFormats.Bgr32, null, 0);
                int w = conv.PixelWidth, h = conv.PixelHeight;
                var px = new int[w * h];
                conv.CopyPixels(px, w * 4, 0);
                for (int i = 0; i < px.Length; i++)
                {
                    int p = px[i];
                    double r = (p >> 16) & 255, g = (p >> 8) & 255, b = p & 255;
                    double l = 0.299 * r + 0.587 * g + 0.114 * b;
                    r = l + (r - l) * sat + lift; g = l + (g - l) * sat + lift; b = l + (b - l) * sat + lift;
                    px[i] = (int)(0xFF000000u | (uint)Math.Clamp(r, 0, 255) << 16 | (uint)Math.Clamp(g, 0, 255) << 8 | (uint)Math.Clamp(b, 0, 255));
                }
                var res = BitmapSource.Create(w, h, src.DpiX, src.DpiY, PixelFormats.Bgr32, null, px, w * 4);
                res.Freeze();
                return res;
            }
            catch { return src; }
        }
    }
}
