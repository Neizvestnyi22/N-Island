using System;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using NIsland.Core;
using NIsland.Services;
using WinForms = System.Windows.Forms;

namespace NIsland
{
    public partial class App : Application
    {
        Mutex _mutex;
        WinForms.NotifyIcon _tray;
        static SettingsWindow _settings;

        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int pid);

        public static IslandWindow Island { get; private set; }
        public static readonly DateTime Started = DateTime.Now;

        protected override void OnStartup(StartupEventArgs e)
        {
            // второй процесс с правами администратора: чистит системные папки и сразу выходит (без чёлки и трея)
            if (e.Args.Length >= 3 && e.Args[0] == "--elevated-clean")
            {
                Shutdown(Elevated.RunCleanJob(e.Args));
                return;
            }
            // самопроверка без интерфейса (поиск текстов и т. п.) — не мешает запущенной копии
            if (e.Args.Length >= 3 && e.Args[0] == "--selftest")
            {
                Shutdown(SelfTest.Run(e.Args));
                return;
            }
            _mutex = new Mutex(true, "NIsland.SingleInstance.v1", out bool created);
            // перезапуск (смена языка): старая копия ещё закрывается — ждём её до 8 секунд
            if (!created && Array.IndexOf(e.Args, "--restart") >= 0)
            {
                try { created = _mutex.WaitOne(8000); } catch (AbandonedMutexException) { created = true; }
            }
            if (!created)
            {
                // уже запущено — передаём ключи запуска той копии (или просим открыть настройки) и разрешаем ей выйти на передний план
                AllowSetForegroundWindow(-1);
                try { System.IO.File.WriteAllLines(SignalFile, e.Args); } catch { }
                try { EventWaitHandle.OpenExisting("NIsland.ShowSettings.v1").Set(); } catch { }
                Shutdown();
                return;
            }
            var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "NIsland.ShowSettings.v1");
            new Thread(() =>
            {
                while (showSignal.WaitOne()) Current?.Dispatcher.BeginInvoke(new Action(OnSignal));
            }) { IsBackground = true, Name = "show-signal" }.Start();

            DispatcherUnhandledException += (_, a) => { Log.Error(a.Exception); a.Handled = true; };
            // выключение/перезагрузка Windows: успеть сохранить настройки и данные
            SessionEnding += (_, __) => { Store.Flush(); try { Dedupe.Suspend(); } catch { } };
            AppDomain.CurrentDomain.UnhandledException += (_, a) => Log.Error(a.ExceptionObject as Exception);
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, a) => { Log.Error(a.Exception); a.SetObserved(); };

            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            Store.Load();
            Updater.Cleanup();   // после обновления: старый exe и временные файлы больше не нужны
            I18n.Init();
            if (Store.Settings.LiteMode && (System.Windows.Media.RenderCapability.Tier >> 16) == 0)
                System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;   // без видеоускорения — не мучаем драйвер
            ApplyAccent(null);
            AutoStart.Apply(Store.Settings.StartWithWindows);

            // каждая служба запускается отдельно: если на этом ПК чего-то нет (Windows N без медиа-компонентов,
            // нет батареи, нет WMI яркости) — чёлка всё равно запустится, просто без этой функции
            Safe("pomodoro", PomodoroService.I.Init);
            Safe("audio", AudioService.I.Start);
            Safe("media", MediaService.I.Start);
            Safe("brightness", SystemService.I.WatchBrightness);
            Safe("calls", CallService.I.Start);
            Safe("laptop", LaptopService.I.Start);
            Safe("vpn", Vpn.Watch);
            Safe("dedupe", Dedupe.Start);
            Safe("hub", Integrations.Start);
            Safe("eq", Equalizer.Start);
            Safe("windows", WinShell.Start);
            Safe("weather", WeatherService.Start);

            Island = new IslandWindow();
            Island.Show();
            CreateTray();
            // VPN-программы ищем в фоне через несколько секунд — чтобы всплывашка знала, какой именно VPN включился
            var vpnScan = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
            vpnScan.Tick += async (_, __) => { vpnScan.Stop(); try { await Vpn.Detect(); } catch (Exception ex) { Log.Info("vpn detect: " + ex.Message); } };
            vpnScan.Start();

            // запуск из меню Проводника, по ссылке nisland:// или из трея другого окна — те же команды, что и у второй копии
            if (Array.Exists(e.Args, a => a is "--check-file" or "--strip-photo" or "--url" or "--visualizer"))
            {
                try { System.IO.File.WriteAllLines(SignalFile, e.Args); } catch { }
                var st = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.2) };
                st.Tick += (_, __) => { st.Stop(); OnSignal(); };
                st.Start();
            }

            int open = Array.IndexOf(e.Args, "--open");
            int ai = Array.IndexOf(e.Args, "--ai");
            if (ai >= 0 && ai + 1 < e.Args.Length)
            {
                // NIsland.exe --ai "вопрос" — открыть чат и сразу спросить
                NIsland.Pages.AiPage.PendingPrompt = e.Args[ai + 1];
                var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.2) };
                t.Tick += (_, __) => { t.Stop(); Island.Expand("ai"); };
                t.Start();
            }
            int set = Array.IndexOf(e.Args, "--settings");
            int center = Array.IndexOf(e.Args, "--center");
            if (set >= 0) ShowSettings(set + 1 < e.Args.Length ? e.Args[set + 1] : null);
            else if (center >= 0) ShowCenter(center + 1 < e.Args.Length ? e.Args[center + 1] : null);
            else if (open >= 0)
            {
                // N-Island.exe --open media|calendar|todo|pomodoro|water|prayer|shelf|quick
                string page = open + 1 < e.Args.Length ? e.Args[open + 1] : null;
                var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.2) };
                t.Tick += (_, __) => { t.Stop(); Island.Expand(page); };
                t.Start();
            }
            else if (Store.Settings.FirstRun && Array.IndexOf(e.Args, "--autostart") < 0)
            {
                Store.Settings.FirstRun = false;
                Island.Welcome();
            }
        }

        static void Safe(string name, Action start)
        {
            try { start(); }
            catch (Exception ex) { Log.Info($"start {name} failed: {ex.Message}"); }
        }

        static string SignalFile => System.IO.Path.Combine(Store.Dir, "signal.txt");

        /// <summary>Повторный запуск exe: выполняем его ключи (--center, --settings, --open, --ai), без ключей — открываем настройки.</summary>
        static void OnSignal()
        {
            string[] args = Array.Empty<string>();
            try { if (System.IO.File.Exists(SignalFile)) { args = System.IO.File.ReadAllLines(SignalFile); System.IO.File.Delete(SignalFile); } } catch { }
            string Arg(string key) { int i = Array.IndexOf(args, key); return i < 0 ? null : i + 1 < args.Length ? args[i + 1] : ""; }
            if (Arg("--center") is string c) ShowCenter(c == "" ? null : c);
            else if (Arg("--open") is string page) Island?.Expand(page == "" ? null : page, true);
            else if (Arg("--ai") is string q && q != "") { NIsland.Pages.AiPage.PendingPrompt = q; Island?.Expand("ai", true); }
            else if (Arg("--spotlight") is string sq) SpotlightWindow.Toggle(sq);
            else if (Arg("--visualizer") != null) VisualizerWindow.Open();
            else if (Arg("--check-file") is string cf && cf != "") { Osint.PendingFile = cf; ShowCenter("osint"); }
            else if (Arg("--strip-photo") is string sp && sp != "") WinShell.StripPhoto(sp);
            else if (Arg("--url") is string url && url != "") WinShell.HandleUrl(url);
            else ShowSettings(Arg("--settings") is string s && s != "" ? s : null);
        }

        /// <summary>Акцентный цвет: из настроек или из обложки текущего трека.</summary>
        public static void ApplyAccent(Color? fromArt)
        {
            var c = ColorUtil.Parse(Store.Settings.Accent, Color.FromRgb(0x0A, 0x84, 0xFF));
            Current.Resources["AccentBrush"] = new SolidColorBrush(c);
            // на светлом акценте (белый, жёлтый, мятный) белый текст не виден — берём тёмный
            double lum = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255;
            Current.Resources["AccentTextBrush"] = new SolidColorBrush(lum > 0.68 ? Color.FromRgb(0x12, 0x12, 0x14) : Colors.White);
            Current.Resources["ArtBrush"] = new SolidColorBrush(fromArt ?? c);
        }

        void CreateTray()
        {
            _tray = new WinForms.NotifyIcon
            {
                Text = "N-Island",
                Visible = true,
                Icon = LoadIcon(),
            };
            var menu = new WinForms.ContextMenuStrip();
            menu.Items.Add("Открыть чёлку", null, (_, __) => Island.Expand(null, true));
            var hide = new WinForms.ToolStripMenuItem("Скрыть чёлку");
            hide.Click += (_, __) => { if (Modes.Hidden) Island.Unhide(); else Island.HideIsland(); };
            menu.Items.Add(hide);
            var modes = new WinForms.ToolStripMenuItem("Режим");
            foreach (var m in Modes.All)
            {
                var key = m.Key;
                var item = new WinForms.ToolStripMenuItem(m.Name);
                item.Click += (_, __) => Island.SetMode(key);
                modes.DropDownItems.Add(item);
            }
            menu.Items.Add(modes);
            var styles = new WinForms.ToolStripMenuItem("Стиль");
            foreach (var p in Presets.All)
            {
                var preset = p;
                var item = new WinForms.ToolStripMenuItem(p.Name);
                item.Click += (_, __) => Presets.Apply(preset);
                styles.DropDownItems.Add(item);
            }
            menu.Items.Add(styles);
            menu.Opening += (_, __) =>
            {
                hide.Text = Modes.Hidden ? "Показать чёлку" : "Скрыть чёлку";
                foreach (WinForms.ToolStripMenuItem i in modes.DropDownItems) i.Checked = i.Text == I18n.T(Modes.Current.Name) || i.Text == Modes.Current.Name;
                foreach (WinForms.ToolStripMenuItem i in styles.DropDownItems) i.Checked = i.Text == I18n.T(Presets.Current.Name) || i.Text == Presets.Current.Name;
                I18n.Apply(menu.Items);
            };
            menu.Items.Add(new WinForms.ToolStripSeparator());
            menu.Items.Add("Быстрый поиск (Ctrl+Alt+Space)", null, (_, __) => SpotlightWindow.Toggle());
            menu.Items.Add("Визуализатор музыки", null, (_, __) => VisualizerWindow.Open());
            menu.Items.Add("Центр управления ноутбуком", null, (_, __) => ShowCenter());
            menu.Items.Add("Настройки", null, (_, __) => ShowSettings());
            menu.Items.Add(new WinForms.ToolStripSeparator());
            menu.Items.Add("Выход", null, (_, __) => Quit());
            _tray.ContextMenuStrip = menu;
            _tray.MouseClick += (_, a) => { if (a.Button == WinForms.MouseButtons.Left) ShowSettings(); };
        }

        static System.Drawing.Icon LoadIcon()
        {
            try
            {
                var s = GetResourceStream(new Uri("pack://application:,,,/Assets/island.ico"))?.Stream;
                if (s != null) return new System.Drawing.Icon(s);
            }
            catch { }
            return System.Drawing.SystemIcons.Application;
        }

        public static void ShowSettings(string section = null)
        {
            if (_settings == null || !_settings.IsLoaded)
            {
                _settings = new SettingsWindow();
                _settings.Closed += (_, __) =>
                {
                    _settings = null;
                    if (Store.Settings.LowMemory) Current.Dispatcher.BeginInvoke(new Action(() => MemoryTrim.Now(true)));
                };
                _settings.Show();
            }
            if (section != null) _settings.Open(section);
            if (_settings.WindowState == WindowState.Minimized) _settings.WindowState = WindowState.Normal;
            // гарантированно поверх остальных окон
            _settings.Topmost = true;
            _settings.Activate();
            _settings.Topmost = false;
        }

        static SettingsWindow _centerWin;

        /// <summary>Центр управления ноутбуком — отдельное окно с очисткой, защитой от слежки, питанием и исправлениями.</summary>
        public static void ShowCenter(string section = null)
        {
            if (_centerWin == null || !_centerWin.IsLoaded)
            {
                _centerWin = new SettingsWindow(true);
                _centerWin.Closed += (_, __) =>
                {
                    _centerWin = null;
                    if (Store.Settings.LowMemory) Current.Dispatcher.BeginInvoke(new Action(() => MemoryTrim.Now(true)));
                };
                _centerWin.Show();
            }
            if (section != null && section != _centerWin.CurrentSection) _centerWin.Open(section);
            if (_centerWin.WindowState == WindowState.Minimized) _centerWin.WindowState = WindowState.Normal;
            _centerWin.Topmost = true;
            _centerWin.Activate();
            _centerWin.Topmost = false;
        }

        /// <summary>Перезапуск N-Island (после смены языка): новая копия ждёт, пока эта закроется.</summary>
        public static void Restart(string args = "")
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath, ("--restart --autostart " + args).Trim()) { UseShellExecute = false }); }
            catch { return; }
            Quit();
        }

        public static void Quit()
        {
            Store.Flush();
            // без острова Windows снова показывает свои баннеры и окна — ничего не пропадёт
            try { Dedupe.Suspend(); } catch (Exception e) { Log.Info("dedupe suspend: " + e.Message); }
            SystemService.I.Stop();
            CallService.I.Stop();
            if (Current is App app && app._tray != null)
            {
                app._tray.Visible = false;
                app._tray.Dispose();
            }
            Current.Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Store.Flush();
            base.OnExit(e);
        }
    }
}
