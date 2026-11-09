using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Threading;

namespace NIsland.Core
{
    public class Bindable : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected bool Set<T>(ref T field, T value, [CallerMemberName] string name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            return true;
        }

        protected void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>Все настройки приложения. Каждое свойство сохраняется в settings.json.</summary>
    public class Settings : Bindable
    {
        // Общие
        public bool FirstRun { get => _firstRun; set => Set(ref _firstRun, value); } bool _firstRun = true;
        public bool StartWithWindows { get => _startWin; set => Set(ref _startWin, value); } bool _startWin;
        public bool Hotkey { get => _hotkey; set => Set(ref _hotkey, value); } bool _hotkey = true;
        public bool Sounds { get => _sounds; set => Set(ref _sounds, value); } bool _sounds = true;

        // Страницы
        public bool PgMedia { get => _pgMedia; set => Set(ref _pgMedia, value); } bool _pgMedia = true;
        public bool PgCalendar { get => _pgCal; set => Set(ref _pgCal, value); } bool _pgCal = true;
        public bool PgTodo { get => _pgTodo; set => Set(ref _pgTodo, value); } bool _pgTodo = true;
        public bool PgPomodoro { get => _pgPomo; set => Set(ref _pgPomo, value); } bool _pgPomo = true;
        public bool PgWater { get => _pgWater; set => Set(ref _pgWater, value); } bool _pgWater = true;
        public bool PgPrayer { get => _pgPrayer; set => Set(ref _pgPrayer, value); } bool _pgPrayer;
        public bool PgShelf { get => _pgShelf; set => Set(ref _pgShelf, value); } bool _pgShelf = true;
        public bool PgQuick { get => _pgQuick; set => Set(ref _pgQuick, value); } bool _pgQuick = true;

        // Кольцо статуса
        public string RingSource { get => _ring; set => Set(ref _ring, value); } string _ring = "auto";
        public bool RingInHeader { get => _ringHeader; set => Set(ref _ringHeader, value); } bool _ringHeader = true;
        public bool ShowBattery { get => _showBat; set => Set(ref _showBat, value); } bool _showBat = true;

        // Экран
        public int Monitor { get => _monitor; set => Set(ref _monitor, value); } int _monitor;
        public bool HideInFullscreen { get => _hideFs; set => Set(ref _hideFs, value); } bool _hideFs = true;
        public int NotchWidth { get => _notchW; set => Set(ref _notchW, value); } int _notchW = 190;
        public int ExpandedWidth { get => _expW; set => Set(ref _expW, value); } int _expW = 640;
        public int OffsetX { get => _offX; set => Set(ref _offX, value); } int _offX;

        // Внешний вид
        public string Style { get => _style; set => Set(ref _style, value); } string _style = "classic";
        public bool Glass { get => _glass; set => Set(ref _glass, value); } bool _glass = true;
        public int GlassTint { get => _tint; set => Set(ref _tint, value); } int _tint = 30;
        public string OpenMode { get => _open; set => Set(ref _open, value); } string _open = "hover";
        public bool PageIcons { get => _pageIcons; set => Set(ref _pageIcons, value); } bool _pageIcons = true;
        public string AnimSpeed { get => _anim; set => Set(ref _anim, value); } string _anim = "normal";
        public string Accent { get => _accent; set => Set(ref _accent, value); } string _accent = "#0A84FF";
        public bool ArtColors { get => _artColors; set => Set(ref _artColors, value); } bool _artColors = true;

        // Музыка
        public bool LyricsOn { get => _lyricsOn; set => Set(ref _lyricsOn, value); } bool _lyricsOn;
        public bool LiveLyrics { get => _liveLyrics; set => Set(ref _liveLyrics, value); } bool _liveLyrics = true;
        public bool TrackPopup { get => _trackPopup; set => Set(ref _trackPopup, value); } bool _trackPopup = true;
        public bool Visualizer { get => _viz; set => Set(ref _viz, value); } bool _viz = true;
        public bool ArtInNotch { get => _artNotch; set => Set(ref _artNotch, value); } bool _artNotch = true;

        // Задачи
        public bool TodoHideDone { get => _todoHide; set => Set(ref _todoHide, value); } bool _todoHide;

        // Календарь
        public bool CalMondayFirst { get => _calMon; set => Set(ref _calMon, value); } bool _calMon = true;
        public bool CalRemind { get => _calRemind; set => Set(ref _calRemind, value); } bool _calRemind = true;
        public int CalRemindBefore { get => _calBefore; set => Set(ref _calBefore, value); } int _calBefore = 5;

        // Помодоро
        public int PomoWork { get => _pomoWork; set => Set(ref _pomoWork, value); } int _pomoWork = 25;
        public int PomoShort { get => _pomoShort; set => Set(ref _pomoShort, value); } int _pomoShort = 5;
        public int PomoLong { get => _pomoLong; set => Set(ref _pomoLong, value); } int _pomoLong = 15;
        public int PomoRounds { get => _pomoRounds; set => Set(ref _pomoRounds, value); } int _pomoRounds = 4;
        public bool PomoAuto { get => _pomoAuto; set => Set(ref _pomoAuto, value); } bool _pomoAuto;

        // Вода
        public int WaterGoal { get => _waterGoal; set => Set(ref _waterGoal, value); } int _waterGoal = 2000;
        public int WaterCup { get => _waterCup; set => Set(ref _waterCup, value); } int _waterCup = 250;
        public bool WaterRemind { get => _waterRemind; set => Set(ref _waterRemind, value); } bool _waterRemind = true;
        public int WaterInterval { get => _waterInt; set => Set(ref _waterInt, value); } int _waterInt = 60;
        public int WaterFrom { get => _waterFrom; set => Set(ref _waterFrom, value); } int _waterFrom = 9;
        public int WaterTo { get => _waterTo; set => Set(ref _waterTo, value); } int _waterTo = 22;

        // Намаз
        public string PrayerCity { get => _pCity; set => Set(ref _pCity, value); } string _pCity = "Kyiv";
        public string PrayerCountry { get => _pCountry; set => Set(ref _pCountry, value); } string _pCountry = "Ukraine";
        public int PrayerMethod { get => _pMethod; set => Set(ref _pMethod, value); } int _pMethod = 3;
        public bool PrayerRemind { get => _pRemind; set => Set(ref _pRemind, value); } bool _pRemind = true;

        // Полка
        public bool ShelfAutoOpen { get => _shelfAuto; set => Set(ref _shelfAuto, value); } bool _shelfAuto = true;

        // Звонки и уведомления
        public bool CallDetect { get => _callDetect; set => Set(ref _callDetect, value); } bool _callDetect = true;
        public bool Notifications { get => _notif; set => Set(ref _notif, value); } bool _notif = true;
        public bool NotifyOnlyCalls { get => _notifCalls; set => Set(ref _notifCalls, value); } bool _notifCalls;
        public bool NotifyBrowsers { get => _notifWeb; set => Set(ref _notifWeb, value); } bool _notifWeb = true;
        public int NotifySeconds { get => _notifSec; set => Set(ref _notifSec, value); } int _notifSec = 4;

        // Память
        public bool LowMemory { get => _lowMem; set => Set(ref _lowMem, value); } bool _lowMem = true;

        // Стиль и режимы
        public string Preset { get => _preset; set => Set(ref _preset, value); } string _preset = "liquid";
        public string Mode { get => _mode; set => Set(ref _mode, value); } string _mode = "normal";
        public bool Hidden { get => _hidden; set => Set(ref _hidden, value); } bool _hidden;
        public bool SideBubble { get => _bubble; set => Set(ref _bubble, value); } bool _bubble = true;
        public bool LiveGlass { get => _liveGlass; set => Set(ref _liveGlass, value); } bool _liveGlass = true;
        public bool PresetMigrated { get => _presetMig; set => Set(ref _presetMig, value); } bool _presetMig;

        // Новые страницы
        public bool PgModes { get => _pgModes; set => Set(ref _pgModes, value); } bool _pgModes = true;
        public bool PgCenter { get => _pgCenter; set => Set(ref _pgCenter, value); } bool _pgCenter = true;
        public bool PgClipboard { get => _pgClip; set => Set(ref _pgClip, value); } bool _pgClip = true;
        public bool PgNotes { get => _pgNotes; set => Set(ref _pgNotes, value); } bool _pgNotes = true;
        public bool PgTimer { get => _pgTimer; set => Set(ref _pgTimer, value); } bool _pgTimer = true;
        public bool PgWeather { get => _pgWeather; set => Set(ref _pgWeather, value); } bool _pgWeather = true;
        public bool PgLauncher { get => _pgLaunch; set => Set(ref _pgLaunch, value); } bool _pgLaunch = true;

        // Буфер обмена
        public bool ClipWatch { get => _clipWatch; set => Set(ref _clipWatch, value); } bool _clipWatch = true;
        public bool ClipSave { get => _clipSave; set => Set(ref _clipSave, value); } bool _clipSave;
        public bool ClipHud { get => _clipHud; set => Set(ref _clipHud, value); } bool _clipHud;

        // Погода и часы
        public string WeatherCity { get => _wCity; set => Set(ref _wCity, value); } string _wCity = "";
        public bool WeatherInNotch { get => _wNotch; set => Set(ref _wNotch, value); } bool _wNotch;
        public string WorldClocks { get => _clocks; set => Set(ref _clocks, value); } string _clocks = "Europe/Moscow|Москва;Europe/London|Лондон;America/New_York|Нью-Йорк;Asia/Tokyo|Токио";

        // Клавиатура и сеть
        public bool HudKeyboard { get => _hudKbd; set => Set(ref _hudKbd, value); } bool _hudKbd = true;
        public bool HudCaps { get => _hudCaps; set => Set(ref _hudCaps, value); } bool _hudCaps = true;
        public bool NetInNotch { get => _netNotch; set => Set(ref _netNotch, value); } bool _netNotch;

        // Здоровье
        public bool EyeCare { get => _eye; set => Set(ref _eye, value); } bool _eye;
        public int EyeInterval { get => _eyeInt; set => Set(ref _eyeInt, value); } int _eyeInt = 20;
        public bool StandUp { get => _stand; set => Set(ref _stand, value); } bool _stand;
        public int StandInterval { get => _standInt; set => Set(ref _standInt, value); } int _standInt = 60;

        // Поиск в запуске
        public string SearchEngine { get => _search; set => Set(ref _search, value); } string _search = "google";

        // Переводчик
        public bool PgTranslate { get => _pgTr; set => Set(ref _pgTr, value); } bool _pgTr = true;
        public string TranslateTo { get => _trTo; set => Set(ref _trTo, value); } string _trTo = "ru";

        // Расписание «ночь / не беспокоить»
        public bool AutoNight { get => _autoNight; set => Set(ref _autoNight, value); } bool _autoNight;
        public string AutoNightMode { get => _autoNightMode; set => Set(ref _autoNightMode, value); } string _autoNightMode = "night";
        public int NightFrom { get => _nightFrom; set => Set(ref _nightFrom, value); } int _nightFrom = 23;
        public int NightTo { get => _nightTo; set => Set(ref _nightTo, value); } int _nightTo = 7;

        // Устройства
        public bool HudOutput { get => _hudOut; set => Set(ref _hudOut, value); } bool _hudOut = true;
        public bool HudUsb { get => _hudUsb; set => Set(ref _hudUsb, value); } bool _hudUsb = true;
        /// <summary>При подключении устройства — показывать только нашу всплывашку, без дублей от Windows.</summary>
        public bool DevicePriority { get => _devPrio; set => Set(ref _devPrio, value); } bool _devPrio = true;

        // ИИ
        public bool PgAi { get => _pgAi; set => Set(ref _pgAi, value); } bool _pgAi = true;
        public string AiProvider { get => _aiProv; set => Set(ref _aiProv, value); } string _aiProv = "free";
        public string AiSystem { get => _aiSys; set => Set(ref _aiSys, value); } string _aiSys = "";
        public Dictionary<string, string> AiKeys { get; set; } = new();   // зашифровано DPAPI
        public Dictionary<string, string> AiModels { get; set; } = new();
        public Dictionary<string, string> AiUrls { get; set; } = new();
        public List<AiCustomProvider> AiCustom { get; set; } = new();
        /// <summary>Свои интеграции (настройки полей — в Integrations, как у встроенных).</summary>
        public List<NIsland.Services.CustomHubDef> CustomHubs { get; set; } = new();
        public int AiTemperature { get => _aiTemp; set => Set(ref _aiTemp, value); } int _aiTemp = -10;     // < 0 — по умолчанию модели
        public int AiMaxTokens { get => _aiMax; set => Set(ref _aiMax, value); } int _aiMax;                 // 0 — по умолчанию
        public int AiHistory { get => _aiHist; set => Set(ref _aiHist, value); } int _aiHist = 12;
        public bool AiStream { get => _aiStream; set => Set(ref _aiStream, value); } bool _aiStream = true;
        public int AiTimeout { get => _aiTimeout; set => Set(ref _aiTimeout, value); } int _aiTimeout = 120;
        public string AiPersona { get => _aiPersona; set => Set(ref _aiPersona, value); } string _aiPersona = "default";
        public string AiEffort { get => _aiEffort; set => Set(ref _aiEffort, value); } string _aiEffort = "default";
        /// <summary>Запасной провайдер: если основной не ответил. «auto» — бесплатные, «» — нет.</summary>
        public string AiFallback { get => _aiFb; set => Set(ref _aiFb, value); } string _aiFb = "auto";
        /// <summary>Сам подобрать модель, если выбранной больше нет у провайдера.</summary>
        public bool AiAutoModel { get => _aiAuto; set => Set(ref _aiAuto, value); } bool _aiAuto = true;
        /// <summary>Сохранять диалог между запусками и делить его с Telegram-ботом.</summary>
        public bool AiKeepChat { get => _aiKeep; set => Set(ref _aiKeep, value); } bool _aiKeep = true;
        public bool AiShareTelegram { get => _aiShareTg; set => Set(ref _aiShareTg, value); } bool _aiShareTg = true;
        /// <summary>Показывать рассуждения модели (если она их присылает).</summary>
        public bool AiShowReasoning { get => _aiReason; set => Set(ref _aiReason, value); } bool _aiReason;
        // 1.8: живой остров
        /// <summary>Персонаж в чёлке: none | blob | cat | ghost | robot | bunny</summary>
        public string Mascot { get => _mascot; set => Set(ref _mascot, value); } string _mascot = "blob";
        /// <summary>Цвет персонажа: art | accent | #RRGGBB</summary>
        public string MascotColor { get => _mascotColor; set => Set(ref _mascotColor, value); } string _mascotColor = "art";
        public bool MascotMoods { get => _mascotMoods; set => Set(ref _mascotMoods, value); } bool _mascotMoods = true;
        /// <summary>Аксессуар: auto, none, cap, crown, bow, party, headphones, glasses, santa, flower.</summary>
        public string MascotHat { get => _mascotHat; set => Set(ref _mascotHat, value); } string _mascotHat = "auto";
        /// <summary>Размер персонажа, % (100 — 22 px).</summary>
        public int GlintSize { get => _glintSize; set => Set(ref _glintSize, Math.Clamp(value, 40, 250)); } int _glintSize = 100;           // размер блика, %
        public int GlintStrength { get => _glintStr; set => Set(ref _glintStr, Math.Clamp(value, 10, 150)); } int _glintStr = 100;        // яркость блика, %
        public int MascotSize { get => _mascotSize; set => Set(ref _mascotSize, value); } int _mascotSize = 100;
        /// <summary>Скорость анимаций, %.</summary>
        public int MascotSpeed { get => _mascotSpeed; set => Set(ref _mascotSpeed, value); } int _mascotSpeed = 100;
        /// <summary>В покое сам подмигивает, машет, зевает, оглядывается.</summary>
        public bool MascotIdle { get => _mascotIdle; set => Set(ref _mascotIdle, value); } bool _mascotIdle = true;
        /// <summary>Реагирует на уведомления и события острова.</summary>
        public bool MascotReactEvents { get => _mascotEv; set => Set(ref _mascotEv, value); } bool _mascotEv = true;
        /// <summary>Реагирует на компьютер: нагрузка, батарея, зарядка, погода, режим игры.</summary>
        public bool MascotReactPc { get => _mascotPc; set => Set(ref _mascotPc, value); } bool _mascotPc = true;
        /// <summary>Танцует под музыку.</summary>
        public bool MascotDance { get => _mascotDance; set => Set(ref _mascotDance, value); } bool _mascotDance = true;
        /// <summary>Спит ночью (0–6).</summary>
        public bool MascotNight { get => _mascotNight; set => Set(ref _mascotNight, value); } bool _mascotNight = true;
        /// <summary>Клик по персонажу — реакция вместо открытия острова.</summary>
        public bool MascotPoke { get => _mascotPoke; set => Set(ref _mascotPoke, value); } bool _mascotPoke = true;
        /// <summary>Щекотка колесом, поглаживание курсором, двойной клик.</summary>
        public bool MascotPlay { get => _mascotPlay; set => Set(ref _mascotPlay, value); } bool _mascotPlay = true;
        // 2.2: остров, плеер, виджеты Telegram, Wi-Fi
        /// <summary>Цвет блика за курсором: white | accent | art.</summary>
        public string GlintColor { get => _glintColor; set => Set(ref _glintColor, value); } string _glintColor = "white";
        /// <summary>Свёрнутый остров чуть подрастает под курсором.</summary>
        public bool IslandHoverGrow { get => _hoverGrow; set => Set(ref _hoverGrow, value); } bool _hoverGrow = true;
        /// <summary>Мягкая тень под островом.</summary>
        public bool IslandShadow { get => _islShadow; set => Set(ref _islShadow, value); } bool _islShadow;
        /// <summary>Шаг перемотки, секунды (кнопки плеера, бот, виджет, MCP).</summary>
        public int PlayerSeekStep { get => _seekStep; set => Set(ref _seekStep, value); } int _seekStep = 10;
        /// <summary>Сколько раз уже слушал этот трек — рядом с исполнителем.</summary>
        public bool PlayerPlayCount { get => _playCount; set => Set(ref _playCount, value); } bool _playCount = true;
        /// <summary>Кнопка «повтор фрагмента A–B» в плеере.</summary>
        public bool PlayerAbButton { get => _abBtn; set => Set(ref _abBtn, value); } bool _abBtn = true;
        /// <summary>Сколько минут виджеты в Telegram обновляются сами после отправки или нажатия.</summary>
        public int WidgetLiveMin { get => _wLive; set => Set(ref _wLive, value); } int _wLive = 5;
        /// <summary>Всплывашка на острове, когда в Telegram нажимают в виджете персонажа.</summary>
        public bool TgWidgetToasts { get => _wToast; set => Set(ref _wToast, value); } bool _wToast = true;
        /// <summary>Предупредить, если сигнал Wi-Fi ниже N % (0 — не следить).</summary>
        public int WifiSignalAlert { get => _wifiAlert; set => Set(ref _wifiAlert, value); } int _wifiAlert;
        /// <summary>Жидкое стекло: чёткий яркий фон и преломление по краям.</summary>
        public bool LiquidGlass { get => _liquid; set => Set(ref _liquid, value); } bool _liquid = true;
        public int LiquidRefract { get => _liqRef; set => Set(ref _liqRef, value); } int _liqRef = 60;
        public int GlassVibrancy { get => _glassVib; set => Set(ref _glassVib, value); } int _glassVib = 140;
        public bool IslandGlint { get => _glint; set => Set(ref _glint, value); } bool _glint = true;
        public bool BeatRim { get => _beatRim; set => Set(ref _beatRim, value); } bool _beatRim = true;
        public bool IslandShimmer { get => _shimmer; set => Set(ref _shimmer, value); } bool _shimmer = true;
        public string WolMac { get => _wolMac; set => Set(ref _wolMac, value); } string _wolMac = "";
        /// <summary>Папки проектов для ИИ-агентов (последние), через «|».</summary>
        public string AgentFolders { get => _agFolders; set => Set(ref _agFolders, value); } string _agFolders = "";
        public bool AgentSound { get => _agSound; set => Set(ref _agSound, value); } bool _agSound = true;
        /// <summary>Турбо-режим: что вернуть при выключении («схема|режим|не засыпать»), пусто — выключен.</summary>
        public string TurboPrev { get => _turbo; set => Set(ref _turbo, value); } string _turbo = "";
        public string AgentDefault { get => _agDef; set => Set(ref _agDef, value); } string _agDef = "claude";
        // MCP-сервер для ИИ-агентов
        public bool McpOn { get => _mcpOn; set => Set(ref _mcpOn, value); } bool _mcpOn;
        public int McpPort { get => _mcpPort; set => Set(ref _mcpPort, value); } int _mcpPort = 7385;
        public string McpToken { get => _mcpToken; set => Set(ref _mcpToken, value); } string _mcpToken = "";
        /// <summary>0 — только чтение, 1 — управление, 2 — ещё и настройка острова.</summary>
        public int McpLevel { get => _mcpLevel; set => Set(ref _mcpLevel, value); } int _mcpLevel = 1;
        // Защитник N-Island
        public bool AvRealtime { get => _avRt; set => Set(ref _avRt, value); } bool _avRt = true;
        public bool AvWatchStartup { get => _avStart; set => Set(ref _avStart, value); } bool _avStart = true;
        /// <summary>SHA-256 файлов, которым пользователь доверяет (через запятую).</summary>
        public string AvAllow { get => _avAllow; set => Set(ref _avAllow, value); } string _avAllow = "";
        public string AvLastScan { get => _avLast; set => Set(ref _avLast, value); } string _avLast = "";
        public int AvLastCount { get => _avLastN; set => Set(ref _avLastN, value); } int _avLastN;
        public int AiFontSize { get => _aiFont; set => Set(ref _aiFont, value); } int _aiFont = 12;
        public string AiActions { get => _aiActions; set => Set(ref _aiActions, value); } string _aiActions =
            "Кратко | Перескажи кратко, по пунктам:\n{clip}\n" +
            "Исправь | Исправь ошибки и улучши текст. Верни только исправленный текст:\n{clip}\n" +
            "Переведи | Переведи на русский (если текст уже на русском — на английский):\n{clip}\n" +
            "Объясни | Объясни простыми словами:\n{clip}\n" +
            "Ответ | Напиши вежливый ответ на это сообщение:\n{clip}";

        // Обслуживание и инструменты
        public bool PgMaint { get => _pgMaint; set => Set(ref _pgMaint, value); } bool _pgMaint = true;
        public bool PgTools { get => _pgTools; set => Set(ref _pgTools, value); } bool _pgTools = true;
        public int TempAgeDays { get => _tempAge; set => Set(ref _tempAge, value); } int _tempAge = 1;
        public string Crypto { get => _crypto; set => Set(ref _crypto, value); } string _crypto = "bitcoin,ethereum,the-open-network,solana";
        public string HolidayCountry { get => _holCountry; set => Set(ref _holCountry, value); } string _holCountry = "";

        // Глубокая кастомизация
        public string CustomBg1 { get => _cBg1; set => Set(ref _cBg1, value); } string _cBg1 = "#121220";
        public string CustomBg2 { get => _cBg2; set => Set(ref _cBg2, value); } string _cBg2 = "#24243A";
        public string CustomRimColor { get => _cRimC; set => Set(ref _cRimC, value); } string _cRimC = "#FFFFFF";
        public int CustomRim { get => _cRim; set => Set(ref _cRim, value); } int _cRim = 50;
        public int CustomSheen { get => _cSheen; set => Set(ref _cSheen, value); } int _cSheen = 15;
        public bool CustomGlassCollapsed { get => _cGlassCol; set => Set(ref _cGlassCol, value); } bool _cGlassCol = true;
        public int MaxRadius { get => _maxR; set => Set(ref _maxR, value); } int _maxR = 26;
        public int NotchHeight { get => _notchH; set => Set(ref _notchH, value); } int _notchH = 32;
        public int BlurRadius { get => _blurR; set => Set(ref _blurR, value); } int _blurR = 22;
        public int HoverDelay { get => _hoverD; set => Set(ref _hoverD, value); } int _hoverD = 130;
        public int CloseDelay { get => _closeD; set => Set(ref _closeD, value); } int _closeD = 420;
        public bool ClockSeconds { get => _clockSec; set => Set(ref _clockSec, value); } bool _clockSec;
        public bool Clock12h { get => _clock12; set => Set(ref _clock12, value); } bool _clock12;
        public bool HeaderDate { get => _headDate; set => Set(ref _headDate, value); } bool _headDate = true;
        public string RightSlot { get => _rightSlot; set => Set(ref _rightSlot, value); } string _rightSlot = "auto";
        public string LeftSlot { get => _leftSlot; set => Set(ref _leftSlot, value); } string _leftSlot = "auto";
        public string PageOrder { get => _pageOrder; set => Set(ref _pageOrder, value); } string _pageOrder = "";

        // Плеер — кастомизация
        public int PlayerArtSize { get => _artSize; set => Set(ref _artSize, value); } int _artSize = 64;
        public bool PlayerGlow { get => _pGlow; set => Set(ref _pGlow, value); } bool _pGlow = true;
        public bool PlayerSeekButtons { get => _pSeek; set => Set(ref _pSeek, value); } bool _pSeek = true;
        public bool PlayerShowAlbum { get => _pAlbum; set => Set(ref _pAlbum, value); } bool _pAlbum = true;
        public bool ShowRemaining { get => _pRem; set => Set(ref _pRem, value); } bool _pRem = true;
        public int LyricsFontSize { get => _lyrSize; set => Set(ref _lyrSize, value); } int _lyrSize = 14;
        public bool ProgressArtColor { get => _progArt; set => Set(ref _progArt, value); } bool _progArt;

        // Текст песни
        public int LyricsOffsetMs { get => _lyrOff; set => Set(ref _lyrOff, value); } int _lyrOff;
        /// <summary>На сколько раньше подсвечивать строку, мс: глаз успевает прочитать до того, как её споют.</summary>
        public int LyricsLeadMs { get => _lyrLead; set => Set(ref _lyrLead, value); } int _lyrLead = 200;
        /// <summary>Задержка звука устройства для текста песни, мс; меньше нуля — авто (0,2 с для Bluetooth).</summary>
        public int LyricsLatencyMs { get => _lyrLat; set => Set(ref _lyrLat, value); } int _lyrLat = -50;
        /// <summary>Подсвечивать слова по одному, если у текста есть пословная синхронизация.</summary>
        public bool LyricsWords { get => _lyrWords; set => Set(ref _lyrWords, value); } bool _lyrWords = true;
        public bool LyricsKaraoke { get => _lyrKar; set => Set(ref _lyrKar, value); } bool _lyrKar = true;
        public bool LyricsTranslate { get => _lyrTr; set => Set(ref _lyrTr, value); } bool _lyrTr;
        public string LyricsTranslateTo { get => _lyrTrTo; set => Set(ref _lyrTrTo, value); } string _lyrTrTo = "ru";
        public bool LyricsCache { get => _lyrCache; set => Set(ref _lyrCache, value); } bool _lyrCache = true;
        public bool LyricsFocus { get => _lyrFocus; set => Set(ref _lyrFocus, value); } bool _lyrFocus;
        public bool LyricsFallback { get => _lyrFb; set => Set(ref _lyrFb, value); } bool _lyrFb = true;
        // 1.8: стили текста, транслитерация, текст на рабочем столе
        /// <summary>apple | classic | neon | minimal | bounce</summary>
        public string LyricsStyle { get => _lyrStyle; set => Set(ref _lyrStyle, value); } string _lyrStyle = "apple";
        /// <summary>Цвет текущей строки: white | accent | art</summary>
        public string LyricsColor { get => _lyrColor; set => Set(ref _lyrColor, value); } string _lyrColor = "white";
        public string LyricsAlign { get => _lyrAlign2; set => Set(ref _lyrAlign2, value); } string _lyrAlign2 = "left";
        /// <summary>default | serif | mono | rounded | light</summary>
        public string LyricsFont { get => _lyrFont; set => Set(ref _lyrFont, value); } string _lyrFont = "default";
        /// <summary>Под строкой — латиницей (кириллица, греческий, корейский).</summary>
        public bool LyricsRoman { get => _lyrRoman; set => Set(ref _lyrRoman, value); } bool _lyrRoman;
        /// <summary>Как появляется новая текущая строка: none, fade, rise, drop, slide, zoom, flip, blur, glow, wipe, typewriter, random.</summary>
        public string LyricsAnim { get => _lyrAnim; set => Set(ref _lyrAnim, value); } string _lyrAnim = "rise";
        public int LyricsAnimMs { get => _lyrAnimMs; set => Set(ref _lyrAnimMs, value); } int _lyrAnimMs = 420;
        /// <summary>При смене песни строки въезжают лесенкой.</summary>
        public bool LyricsCascade { get => _lyrCascade; set => Set(ref _lyrCascade, value); } bool _lyrCascade = true;
        /// <summary>Размер текущей строки относительно остальных, %.</summary>
        public int LyricsActiveScale { get => _lyrActScale; set => Set(ref _lyrActScale, value); } int _lyrActScale = 105;
        /// <summary>Расстояние между строками, px.</summary>
        public int LyricsLineGap { get => _lyrGap; set => Set(ref _lyrGap, value); } int _lyrGap = 6;
        public bool LyricsUpper { get => _lyrUpper; set => Set(ref _lyrUpper, value); } bool _lyrUpper;
        /// <summary>Мягкая тень под текущей строкой — читается на любом фоне.</summary>
        public bool LyricsShadow { get => _lyrShadow; set => Set(ref _lyrShadow, value); } bool _lyrShadow;
        public bool DesktopLyrics { get => _deskLyr; set => Set(ref _deskLyr, value); } bool _deskLyr;
        public int DeskLyricsSize { get => _deskSize; set => Set(ref _deskSize, value); } int _deskSize = 30;
        public bool DeskLyricsLock { get => _deskLock; set => Set(ref _deskLock, value); } bool _deskLock = true;
        public bool DeskLyricsNext { get => _deskNext; set => Set(ref _deskNext, value); } bool _deskNext = true;
        public int DeskLyricsOpacity { get => _deskOp; set => Set(ref _deskOp, value); } int _deskOp = 100;
        public string DeskLyricsPos { get => _deskPos; set => Set(ref _deskPos, value); } string _deskPos = "";
        public bool DeskLyricsHideFull { get => _deskHideFull; set => Set(ref _deskHideFull, value); } bool _deskHideFull = true;
        public bool NotchProgress { get => _notchProg; set => Set(ref _notchProg, value); } bool _notchProg = true;
        public int HistoryKeep { get => _histKeep; set => Set(ref _histKeep, value); } int _histKeep = 60;
        public bool PgSound { get => _pgSound; set => Set(ref _pgSound, value); } bool _pgSound = true;
        public string NewsFeed { get => _news; set => Set(ref _news, value); } string _news = "https://lenta.ru/rss/news";

        // Звуки (Core/Sound.cs): тема звука для каждого события и общая громкость
        public string SoundTimer { get => _sndTimer; set => Set(ref _sndTimer, value); } string _sndTimer = "chime";
        public string SoundReminder { get => _sndRem; set => Set(ref _sndRem, value); } string _sndRem = "glass";
        public string SoundNotify { get => _sndNot; set => Set(ref _sndNot, value); } string _sndNot = "soft";
        public string SoundAlert { get => _sndAlert; set => Set(ref _sndAlert, value); } string _sndAlert = "marimba";
        public int SoundVolume { get => _sndVol; set => Set(ref _sndVol, value); } int _sndVol = 70;

        /// <summary>Язык интерфейса: auto — как в Windows, иначе код (ru, en, uk…). null — ещё не выбран (см. миграцию в Load).</summary>
        public string Language { get => _lang; set => Set(ref _lang, value); } string _lang;
        /// <summary>Лёгкий режим для слабых ПК: без стекла, эквалайзера и теней. null — ещё не определён (включается сам на слабом железе).</summary>
        public bool? LiteModeSetting { get => _lite; set { if (Set(ref _lite, value)) Raise(nameof(LiteMode)); } } bool? _lite;
        [System.Text.Json.Serialization.JsonIgnore] public bool LiteMode { get => _lite == true; set => LiteModeSetting = value; }

        /// <summary>Раз в сутки спрашивать GitHub о новой версии и показывать уведомление в чёлке.</summary>
        public bool AutoUpdate { get => _autoUpd; set => Set(ref _autoUpd, value); } bool _autoUpd = true;
        public DateTime LastUpdateCheck { get => _lastUpd; set => Set(ref _lastUpd, value); } DateTime _lastUpd;
        public string SkippedVersion { get => _skipVer; set => Set(ref _skipVer, value); } string _skipVer = "";

        /// <summary>Тема окон настроек и центра: dark — графит, amoled — чистый чёрный.</summary>
        public string WindowTheme { get => _winTheme; set => Set(ref _winTheme, value); } string _winTheme = "dark";

        // Центр управления ноутбуком
        public bool CenterBeginner { get => _cBeg; set => Set(ref _cBeg, value); } bool _cBeg = true;
        public DateTime LastCleanup { get => _lastClean; set => Set(ref _lastClean, value); } DateTime _lastClean;
        public long CleanedTotal { get => _cleanedTotal; set => Set(ref _cleanedTotal, value); } long _cleanedTotal;
        public bool AutoClean { get => _autoClean; set => Set(ref _autoClean, value); } bool _autoClean;
        public int AutoCleanDays { get => _autoCleanDays; set => Set(ref _autoCleanDays, value); } int _autoCleanDays = 7;
        public bool BatteryAlertHigh { get => _batHi; set => Set(ref _batHi, value); } bool _batHi;
        public int BatteryHigh { get => _batHiV; set => Set(ref _batHiV, value); } int _batHiV = 80;
        public bool AutoPlan { get => _autoPlan; set => Set(ref _autoPlan, value); } bool _autoPlan;
        public string PlanOnAc { get => _planAc; set => Set(ref _planAc, value); } string _planAc = "";
        public string PlanOnDc { get => _planDc; set => Set(ref _planDc, value); } string _planDc = "";
        public bool HungryWatch { get => _hungry; set => Set(ref _hungry, value); } bool _hungry = true;
        public int HungryCpu { get => _hungryCpu; set => Set(ref _hungryCpu, value); } int _hungryCpu = 30;
        public bool RebootRemind { get => _reboot; set => Set(ref _reboot, value); } bool _reboot = true;
        public int RebootDays { get => _rebootDays; set => Set(ref _rebootDays, value); } int _rebootDays = 7;
        public bool DiskLowAlert { get => _diskLow; set => Set(ref _diskLow, value); } bool _diskLow = true;
        public bool CamMicAlert { get => _camMic; set => Set(ref _camMic, value); } bool _camMic = true;
        public bool ActivityJournal { get => _actJ; set => Set(ref _actJ, value); } bool _actJ = true;
        public string Scenario { get => _scenario; set => Set(ref _scenario, value); } string _scenario = "";

        // Уведомления — кастомизация
        public string NotifyBlock { get => _nBlock; set => Set(ref _nBlock, value); } string _nBlock = "";
        public string NotifyKeywords { get => _nKeys; set => Set(ref _nKeys, value); } string _nKeys = "";
        public bool NotifyHideText { get => _nHide; set => Set(ref _nHide, value); } bool _nHide;
        public bool NotifyCodes { get => _nCodes; set => Set(ref _nCodes, value); } bool _nCodes = true;
        public bool NotifyBadge { get => _nBadge; set => Set(ref _nBadge, value); } bool _nBadge = true;
        public bool NotifySound { get => _nSound; set => Set(ref _nSound, value); } bool _nSound;

        // Прозрачность
        public int IslandOpacity { get => _islOp; set => Set(ref _islOp, value); } int _islOp = 100;
        public int IdleOpacity { get => _idleOp; set => Set(ref _idleOp, value); } int _idleOp = 100;
        public int BgOpacity { get => _bgOp; set => Set(ref _bgOp, value); } int _bgOp = 100;
        public int SettingsOpacity { get => _setOp; set => Set(ref _setOp, value); } int _setOp = 100;

        // Интеграции и VPN
        public bool PgHub { get => _pgHub; set => Set(ref _pgHub, value); } bool _pgHub = true;
        public bool PgVpn { get => _pgVpn; set => Set(ref _pgVpn, value); } bool _pgVpn = true;
        public bool PgWifi { get => _pgWifi; set => Set(ref _pgWifi, value); } bool _pgWifi = true;
        /// <summary>Всплывашка, когда VPN включается или выключается (в любой программе).</summary>
        public bool VpnNotify { get => _vpnNotify; set => Set(ref _vpnNotify, value); } bool _vpnNotify = true;
        /// <summary>Замочек в свёрнутом острове, пока включён VPN.</summary>
        public bool VpnInNotch { get => _vpnNotch; set => Set(ref _vpnNotch, value); } bool _vpnNotch;
        /// <summary>Настройки сервисов: ключ интеграции → включена, поля (секреты зашифрованы DPAPI), какие события пересылать.</summary>
        public Dictionary<string, NIsland.Services.IntegrationConfig> Integrations { get; set; } = new();

        // «Без дублей»: выключить то, что остров уже показывает сам (Services/Dedupe.cs)
        public bool DedupeBanners { get => _ddBan; set => Set(ref _ddBan, value); } bool _ddBan;
        public bool DedupeAutoplay { get => _ddAuto; set => Set(ref _ddAuto, value); } bool _ddAuto;
        public bool DedupeOsd { get => _ddOsd; set => Set(ref _ddOsd, value); } bool _ddOsd;

        // Быстрый поиск (отдельное окно, Ctrl+Alt+Space)
        public bool Spotlight { get => _spot; set => Set(ref _spot, value); } bool _spot = true;

        // 1.7: визуализаторы и эквалайзер
        /// <summary>Справа от плеера — визуализатор вместо текста песни.</summary>
        public bool MediaVisual { get => _mVis; set => Set(ref _mVis, value); } bool _mVis;
        public string VizStyle { get => _vizStyle; set => Set(ref _vizStyle, value); } string _vizStyle = "radial";
        public string VizPalette { get => _vizPal; set => Set(ref _vizPal, value); } string _vizPal = "art";
        public string VizWinStyle { get => _vizWinStyle; set => Set(ref _vizWinStyle, value); } string _vizWinStyle = "ambience";
        public string VizWinPalette { get => _vizWinPal; set => Set(ref _vizWinPal, value); } string _vizWinPal = "art";
        /// <summary>Окно визуализатора само меняет стиль каждые 30 секунд.</summary>
        public bool VizAuto { get => _vizAuto; set => Set(ref _vizAuto, value); } bool _vizAuto;
        public bool VizLyrics { get => _vizLyr; set => Set(ref _vizLyr, value); } bool _vizLyr = true;
        public bool VizTrack { get => _vizTrack; set => Set(ref _vizTrack, value); } bool _vizTrack = true;
        public int VizQuality { get => _vizQ; set => Set(ref _vizQ, value); } int _vizQ = 2;
        public string VizBounds { get => _vizBounds; set => Set(ref _vizBounds, value); } string _vizBounds = "";
        // 1.8: тонкая настройка визуализаторов (общая для плеера и окна)
        public int VizSens { get => _vizSens; set => Set(ref _vizSens, value); } int _vizSens = 100;          // чувствительность, %
        public int VizSmooth { get => _vizSmooth; set => Set(ref _vizSmooth, value); } int _vizSmooth = 25;   // плавность, 0–90
        public int VizTrail { get => _vizTrail; set => Set(ref _vizTrail, value); } int _vizTrail = 50;       // след, 50 — как задумано стилем
        public int VizSpeed { get => _vizSpeed; set => Set(ref _vizSpeed, value); } int _vizSpeed = 100;      // скорость движения, %
        public int VizDensity { get => _vizDens; set => Set(ref _vizDens, value); } int _vizDens = 100;       // количество полос/частиц, %
        public int VizGlow { get => _vizGlow; set => Set(ref _vizGlow, value); } int _vizGlow = 30;           // свечение, 0–100
        public int VizBeatFx { get => _vizBeat; set => Set(ref _vizBeat, value); } int _vizBeat = 100;       // реакция на удары, 0–200
        public int VizHue { get => _vizHue; set => Set(ref _vizHue, value); } int _vizHue;                   // перелив цветов, 0–100
        public int VizSat { get => _vizSat; set => Set(ref _vizSat, value); } int _vizSat = 100;             // насыщенность, 0–150
        public int VizBright { get => _vizBr; set => Set(ref _vizBr, value); } int _vizBr = 100;             // яркость, 50–150
        public bool VizMirror { get => _vizMirror; set => Set(ref _vizMirror, value); } bool _vizMirror;
        public string VizBg { get => _vizBg; set => Set(ref _vizBg, value); } string _vizBg = "none";        // none | dark | art | vignette
        public string VizColorA { get => _vizCa; set => Set(ref _vizCa, value); } string _vizCa = "#FF2D95";
        public string VizColorB { get => _vizCb; set => Set(ref _vizCb, value); } string _vizCb = "#00E5FF";
        public int VizFps { get => _vizFps; set => Set(ref _vizFps, value); } int _vizFps = 45;
        public int VizAutoSec { get => _vizAutoSec; set => Set(ref _vizAutoSec, value); } int _vizAutoSec = 30;
        /// <summary>Избранные стили (через запятую): автосмена и стрелки в окне ходят только по ним.</summary>
        public string VizFavorites { get => _vizFav; set => Set(ref _vizFav, value); } string _vizFav = "";
        public bool VizBeatFlash { get => _vizFlash; set => Set(ref _vizFlash, value); } bool _vizFlash;
        /// <summary>Полоски в свёрнутой чёлке слушают настоящий звук.</summary>
        public bool LiveBars { get => _liveBars; set => Set(ref _liveBars, value); } bool _liveBars = true;
        public bool PgEq { get => _pgEq; set => Set(ref _pgEq, value); } bool _pgEq = true;
        public bool EqOn { get => _eqOn; set => Set(ref _eqOn, value); } bool _eqOn;
        public string EqGains { get => _eqGains; set => Set(ref _eqGains, value); } string _eqGains = "";
        public string EqPreset { get => _eqPreset; set => Set(ref _eqPreset, value); } string _eqPreset = "flat";
        /// <summary>Свой профиль эквалайзера для каждого устройства вывода (наушники, колонки).</summary>
        public bool EqPerDevice { get => _eqDev; set => Set(ref _eqDev, value); } bool _eqDev = true;

        // 1.7: синхронизация текста
        /// <summary>Запоминать сдвиг текста для приложения (браузеры часто сообщают время с опозданием).</summary>
        public bool LyricsAppOffset { get => _lyrApp; set => Set(ref _lyrApp, value); } bool _lyrApp = true;
        /// <summary>Приложение не сообщает время — ловить начало трека по звуку.</summary>
        public bool LyricsAutoAlign { get => _lyrAlign; set => Set(ref _lyrAlign, value); } bool _lyrAlign = true;

        // 1.7: запоминание состояния
        public bool RememberPage { get => _remPage; set => Set(ref _remPage, value); } bool _remPage = true;
        public string LastPage { get => _lastPage; set => Set(ref _lastPage, value); } string _lastPage = "";
        public bool RememberPin { get => _remPin; set => Set(ref _remPin, value); } bool _remPin = true;
        public bool IslandPinned { get => _islPin; set => Set(ref _islPin, value); } bool _islPin;
        public string LastSettingsSection { get => _lastSet; set => Set(ref _lastSet, value); } string _lastSet = "";
        public string LastCenterSection { get => _lastCen; set => Set(ref _lastCen, value); } string _lastCen = "";
        public string WifiTab { get => _wifiTab; set => Set(ref _wifiTab, value); } string _wifiTab = "";
        public string WeatherView { get => _wView; set => Set(ref _wView, value); } string _wView = "hours";

        // 1.7: погода
        public bool WeatherRainAlert { get => _wRain; set => Set(ref _wRain, value); } bool _wRain = true;
        public bool WeatherMorning { get => _wMorn; set => Set(ref _wMorn, value); } bool _wMorn;
        /// <summary>Предупреждать об опасной погоде: гроза, шторм, заморозок, жара, УФ.</summary>
        public bool WeatherAlerts { get => _wAlerts; set => Set(ref _wAlerts, value); } bool _wAlerts = true;
        /// <summary>Другие города через запятую — коротко на странице погоды, клик — сделать основным.</summary>
        public string WeatherCities { get => _wCities; set => Set(ref _wCities, value); } string _wCities = "";
        public string WeatherMorningDay { get => _wMornDay; set => Set(ref _wMornDay, value); } string _wMornDay = "";

        // 1.7: интеграция с Windows
        public bool ShellMenu { get => _shellMenu; set => Set(ref _shellMenu, value); } bool _shellMenu;
        public bool UrlProtocol { get => _urlProto; set => Set(ref _urlProto, value); } bool _urlProto;
        public bool FollowFocusAssist { get => _focusAssist; set => Set(ref _focusAssist, value); } bool _focusAssist;
        public bool WindowsAccent { get => _winAccent; set => Set(ref _winAccent, value); } bool _winAccent;
        public bool MediaKeysHud { get => _mkHud; set => Set(ref _mkHud, value); } bool _mkHud = true;

        // HUD
        public bool HudVolume { get => _hudVol; set => Set(ref _hudVol, value); } bool _hudVol = true;
        public bool HudBrightness { get => _hudBri; set => Set(ref _hudBri, value); } bool _hudBri = true;
        public bool HudBattery { get => _hudBat; set => Set(ref _hudBat, value); } bool _hudBat = true;
        public bool HudLowBattery { get => _hudLow; set => Set(ref _hudLow, value); } bool _hudLow = true;
        public int HudSeconds { get => _hudSec; set => Set(ref _hudSec, value); } int _hudSec = 2;
    }

    public class TodoItem : Bindable
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Text { get => _text; set => Set(ref _text, value); } string _text;
        public bool Done { get => _done; set => Set(ref _done, value); } bool _done;
        public DateTime Created { get; set; } = DateTime.Now;
    }

    public class WaterEntry
    {
        public DateTime Time { get; set; }
        public int Ml { get; set; }
    }

    public class CalEvent : Bindable
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public DateTime Date { get; set; }
        public string Time { get; set; }
        public string Title { get; set; }
        public bool Notified { get; set; }

        [JsonIgnore] public string TimeLabel => string.IsNullOrEmpty(Time) ? "весь день" : Time;
        [JsonIgnore]
        public DateTime? At
        {
            get
            {
                if (string.IsNullOrEmpty(Time) || !TimeSpan.TryParse(Time, out var t)) return null;
                return Date.Date + t;
            }
        }
    }

    /// <summary>Свой ИИ-провайдер, добавленный пользователем.</summary>
    public class AiCustomProvider
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
        public string Name { get; set; } = "Мой провайдер";
        public string Kind { get; set; } = "openai";          // openai | anthropic
        public string BaseUrl { get; set; } = "http://localhost:8000/v1";
        public string Model { get; set; } = "model";
        public string Headers { get; set; } = "";              // «Имя: значение» по строкам
        public bool NeedsKey { get; set; } = true;
    }

    public class ClipItem : Bindable
    {
        public string Text { get; set; }
        public DateTime Time { get; set; } = DateTime.Now;
        public bool Pinned { get => _pinned; set => Set(ref _pinned, value); } bool _pinned;
    }

    public class LaunchItem
    {
        public string Name { get; set; }
        public string Target { get; set; }
    }

    /// <summary>Запись журнала: какая программа пользовалась камерой, микрофоном или местоположением и сколько.</summary>
    public class ActivityRecord
    {
        public string App { get; set; }
        public string Kind { get; set; }
        public DateTime Start { get; set; }
        public DateTime? End { get; set; }
    }

    public class PlayedItem
    {
        public string Title { get; set; }
        public string Artist { get; set; }
        public string App { get; set; }
        public DateTime Time { get; set; } = DateTime.Now;
        [JsonIgnore] public string Line => string.IsNullOrEmpty(Artist) ? Title : $"{Artist} — {Title}";
    }

    public class AppData
    {
        public string Notes { get; set; } = "";
        public List<PlayedItem> Played { get; set; } = new();
        public List<PlayedItem> Favorites { get; set; } = new();
        public ObservableCollection<ClipItem> Clips { get; set; } = new();
        public List<LaunchItem> Launch { get; set; }
        public ObservableCollection<TodoItem> Todos { get; set; } = new();
        public List<WaterEntry> Water { get; set; } = new();
        public ObservableCollection<CalEvent> Events { get; set; } = new();
        public ObservableCollection<string> Shelf { get; set; } = new();
        public Dictionary<string, int> PomodoroDone { get; set; } = new();
        /// <summary>Подстройка текста под конкретную песню, секунды («Название|Исполнитель» → сдвиг).</summary>
        public Dictionary<string, double> LyricOffsets { get; set; } = new();
        /// <summary>Программы, добавленные в «Тонкую настройку программ».</summary>
        public List<string> TunedApps { get; set; } = new();
        /// <summary>Журнал активности: камера, микрофон, место — кто, когда и сколько (последние записи).</summary>
        public List<ActivityRecord> ActivityLog { get; set; } = new();
        /// <summary>Быстрый поиск: что и сколько раз запускали — частое поднимается выше.</summary>
        public Dictionary<string, int> SpotlightUsage { get; set; } = new();
        /// <summary>Эквалайзер для каждого устройства вывода: имя → «пресет|полосы|вкл».</summary>
        public Dictionary<string, string> EqProfiles { get; set; } = new();
        /// <summary>Выученный сдвиг текста для приложения (Chrome, Spotify…), секунды.</summary>
        public Dictionary<string, double> AppLyricOffsets { get; set; } = new();
        /// <summary>Списки моделей ИИ-провайдеров (обновляются сами раз в несколько дней).</summary>
        public Dictionary<string, List<string>> AiModelCache { get; set; } = new();
        public Dictionary<string, DateTime> AiModelCacheAt { get; set; } = new();
        /// <summary>Карта сигнала Wi-Fi: комната → RSSI.</summary>
        public Dictionary<string, int> SignalMap { get; set; } = new();
        public List<NIsland.Services.AgentEvent> AgentEvents { get; set; } = new();
        /// <summary>Общий диалог с ИИ: чат острова и Telegram-бот.</summary>
        public List<NIsland.Services.AiMessage> AiChat { get; set; } = new();
    }

    public static class Store
    {
        /// <summary>Папка данных. NISLAND_DATA — своя папка (для проверки сборки, не трогая настоящие настройки).</summary>
        /// <summary>Имя системного объекта (мьютекс, событие) для этой папки данных: тестовая копия с NISLAND_DATA
        /// не должна перехватывать сигналы основной. Хэш стабильный — string.GetHashCode в .NET случайный в каждом процессе.</summary>
        public static string Inst(string name)
        {
            var d = Environment.GetEnvironmentVariable("NISLAND_DATA");
            if (string.IsNullOrEmpty(d)) return name;
            uint h = 2166136261;
            foreach (char c in d.ToLowerInvariant()) h = (h ^ c) * 16777619;
            return name + "." + h.ToString("x8");
        }

        public static readonly string Dir = Environment.GetEnvironmentVariable("NISLAND_DATA") is { Length: > 0 } custom ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NIsland");

        public static Settings Settings { get; private set; }
        public static AppData Data { get; private set; }

        static DispatcherTimer _timer;
        static bool _dirtySettings, _dirtyData;
        static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

        public static void Load()
        {
            Directory.CreateDirectory(Dir);
            bool existing = File.Exists(System.IO.Path.Combine(Dir, "settings.json"));
            Settings = Read<Settings>("settings.json") ?? new Settings();
            // 1.5: язык и лёгкий режим. У тех, кто уже пользовался, остаётся русский; новым — язык Windows.
            Settings.Language ??= existing ? "ru" : "auto";
            Settings.LiteModeSetting ??= WeakPc();
            Data = Read<AppData>("data.json") ?? new AppData();
            Data.Todos ??= new();
            Data.Water ??= new();
            Data.Events ??= new();
            Data.Shelf ??= new();
            Data.PomodoroDone ??= new();
            Data.LyricOffsets ??= new();
            Data.TunedApps ??= new();
            Data.ActivityLog ??= new();
            Data.Clips ??= new();
            Data.Notes ??= "";
            Data.Launch ??= DefaultLaunch();
            Data.Played ??= new();
            Data.Favorites ??= new();
            Data.EqProfiles ??= new();
            Data.AppLyricOffsets ??= new();
            Data.SpotlightUsage ??= new();
            if (!Settings.ClipSave) Data.Clips.Clear();
            // обновление 1.1: делаем остров стеклянным
            if (!Settings.PresetMigrated)
            {
                Settings.PresetMigrated = true;
                Presets.Apply(Presets.Get("liquid"));
            }

            var cutoff = DateTime.Today.AddDays(-14);
            Data.Water.RemoveAll(w => w.Time < cutoff);
            foreach (var old in Data.Events.Where(e => e.Date < DateTime.Today.AddDays(-60)).ToList())
                Data.Events.Remove(old);

            Settings.PropertyChanged += (_, __) => SaveSettings();
            // записываем файл сразу: так в нём появятся новые поля и результат миграции
            SaveSettings();
        }

        /// <summary>Самопроверка: настройки по умолчанию, без чтения и записи файлов.</summary>
        public static void UseDefaults()
        {
            ReadOnly = true;
            Settings = new Settings { LyricsCache = false, Language = "ru" };
            Data = new AppData();
        }

        /// <summary>Самопроверка: ничего не записываем на диск (иначе настройки по умолчанию затёрли бы настоящие).</summary>
        public static bool ReadOnly { get; private set; }

        /// <summary>Слабый компьютер: до 4 ГБ памяти, 2 потока или видеокарта без аппаратного ускорения WPF.</summary>
        static bool WeakPc()
        {
            try
            {
                var m = new Native.MEMORYSTATUSEX();
                bool lowRam = Native.GlobalMemoryStatusEx(m) && m.ullTotalPhys < 5UL << 30;
                bool fewCores = Environment.ProcessorCount <= 2;
                bool noGpu = (System.Windows.Media.RenderCapability.Tier >> 16) < 2;
                return lowRam || fewCores || noGpu;
            }
            catch { return false; }
        }

        public static List<LaunchItem> DefaultLaunch() => new()
            {
                new() { Name = "Проводник", Target = "explorer.exe" },
                new() { Name = "Загрузки", Target = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads") },
                new() { Name = "Параметры", Target = "ms-settings:" },
                new() { Name = "Диспетчер", Target = "taskmgr.exe" },
                new() { Name = "Калькулятор", Target = "calc.exe" },
                new() { Name = "Блокнот", Target = "notepad.exe" },
                new() { Name = "YouTube", Target = "https://www.youtube.com" },
                new() { Name = "Корзина", Target = "shell:RecycleBinFolder" },
            };

        public static void SaveSettings() { if (ReadOnly) return; _dirtySettings = true; Kick(); }
        public static void SaveData() { if (ReadOnly) return; _dirtyData = true; Kick(); }

        /// <summary>
        /// Отложенное сохранение. Таймер живёт ТОЛЬКО в потоке интерфейса: раньше его создавал тот, кто первым попросил
        /// сохранить, — и если это был фоновый поток (обновление списка моделей ИИ, Telegram, агенты), таймер привязывался
        /// к потоку без цикла сообщений и не срабатывал никогда: в 1.8–1.9 настройки и данные не сохранялись до выхода.
        /// </summary>
        static void Kick()
        {
            var ui = System.Windows.Application.Current?.Dispatcher;
            if (ui == null) { Flush(); return; }                                    // без окон (самопроверка) — сразу
            if (!ui.CheckAccess()) { ui.BeginInvoke(new Action(Kick)); return; }
            if (_timer == null)
            {
                _timer = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromMilliseconds(700) };
                _timer.Tick += (_, __) => { _timer.Stop(); Flush(); };
            }
            _timer.Stop();
            _timer.Start();
        }

        static readonly object FlushGate = new();

        public static void Flush()
        {
            if (ReadOnly) return;
            lock (FlushGate)
            {
                // флаги сбрасываем до записи: изменение во время записи снова пометит файл и запишется следующим разом
                bool s = _dirtySettings, d = _dirtyData;
                _dirtySettings = _dirtyData = false;
                try { if (s) Write("settings.json", Settings); }
                catch (Exception e) { _dirtySettings = true; Log.Error(e); }
                try { if (d) Write("data.json", Data); }
                catch (Exception e) { _dirtyData = true; Log.Error(e); }
            }
        }

        /// <summary>Файл повреждён (выключили питание во время записи, диск) — берём резервную копию, а не начинаем с нуля.</summary>
        static T Read<T>(string name) where T : class
        {
            var p = Path.Combine(Dir, name);
            foreach (var file in new[] { p, p + ".bak" })
            {
                try
                {
                    if (!File.Exists(file)) continue;
                    var text = File.ReadAllText(file);
                    if (string.IsNullOrWhiteSpace(text)) continue;
                    var v = JsonSerializer.Deserialize<T>(text, Json);
                    if (v == null) continue;
                    if (file != p)
                    {
                        Log.Info($"{name}: основной файл повреждён, загружена резервная копия");
                        try { File.Copy(p, p + ".broken", true); } catch { }
                    }
                    return v;
                }
                catch (Exception e) { Log.Info($"{name}: {e.Message}"); }
            }
            return null;
        }

        static void Write<T>(string name, T value)
        {
            var p = Path.Combine(Dir, name);
            var tmp = p + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(value, Json));
            // атомарная замена с резервной копией прошлой версии
            if (File.Exists(p))
            {
                try { File.Replace(tmp, p, p + ".bak", true); return; }
                catch (Exception e) { Log.Info($"{name}: replace — {e.Message}"); }
            }
            File.Move(tmp, p, true);
        }
    }

    public static class Log
    {
        static readonly object Gate = new();

        public static void Error(Exception e) => Write("ERROR " + e);
        public static void Info(string s) => Write(s);

        static void Write(string s)
        {
            try
            {
                lock (Gate)
                {
                    var p = Path.Combine(Store.Dir, "log.txt");
                    if (File.Exists(p) && new FileInfo(p).Length > 512 * 1024) File.Delete(p);
                    File.AppendAllText(p, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {s}{Environment.NewLine}");
                }
            }
            catch { }
        }
    }
}
