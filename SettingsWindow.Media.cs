using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using NIsland.Controls;
using NIsland.Core;
using NIsland.Pages;
using NIsland.Services;

namespace NIsland
{
    /// <summary>Разделы «Эквалайзер», «Визуализатор» и «Windows».</summary>
    public partial class SettingsWindow
    {
        // ------------------------------------------------------------------
        //  Эквалайзер
        // ------------------------------------------------------------------

        UIElement EqSec()
        {
            var page = new StackPanel();
            bool inst = Equalizer.Installed;
            var status = Info("");
            void Upd()
            {
                if (!Equalizer.Installed) { status.Text = I18n.T("Движок Equalizer APO не установлен — эквалайзер пока только сохраняет настройки."); status.Foreground = UI.Hex("#FF9F0A"); return; }
                var dev = Equalizer.OnCurrentDevice();
                status.Text = Equalizer.LastError != null ? I18n.T(Equalizer.LastError)
                    : dev == false ? I18n.T("APO установлен, но не подключён к «") + AudioService.I.OutputName + I18n.T("». Нажми «Выбрать устройства» и отметь его.")
                    : I18n.T(S.EqOn ? "Работает: звук всех приложений идёт через эквалайзер ✓" : "Установлен. Включи эквалайзер в чёлке или ниже.");
                status.Foreground = Equalizer.LastError != null || dev == false ? UI.Hex("#FF9F0A") : UI.Hex("#30D158");
            }
            Upd();

            var curve = new EqCurve { Height = 170, Gains = Equalizer.Gains, Enabled = S.EqOn, CurveColor = (UI.Res("AccentBrush") as SolidColorBrush)?.Color ?? Colors.DodgerBlue };
            curve.Committed += g => { Equalizer.SetGains(g, "custom"); Upd(); };
            var presets = new WrapPanel { Margin = new Thickness(14, 0, 14, 10) };
            foreach (var p in Equalizer.Presets)
            {
                var key = p.Key;
                var b = UI.Pill(p.Name, () => { Equalizer.SetPreset(key); curve.Gains = Equalizer.Gains; curve.Enabled = S.EqOn; Upd(); });
                b.Margin = new Thickness(0, 0, 6, 6);
                presets.Children.Add(b);
            }
            Follow(curve, nameof(Settings.EqOn), () => { curve.Enabled = S.EqOn; curve.InvalidateVisual(); Upd(); });
            Follow(curve, nameof(Settings.EqGains), () => curve.Gains = Equalizer.Gains);

            page.Children.Add(Group(null,
                Row("Эквалайзер", "Меняет звук всех приложений: музыки, видео, игр и звонков", ToggleAct(nameof(Settings.EqOn), on => { Equalizer.SetOn(on); Upd(); })),
                status,
                curve.M(8, 4, 8, 4),
                presets,
                Row("Свой профиль для каждого устройства", "Наушники, колонки и Bluetooth звучат по-разному: настройка запоминается для каждого и включается сама при переключении",
                    Toggle(nameof(Settings.EqPerDevice))),
                Row("Страница «Эквалайзер» в чёлке", "Кривая с живым спектром и пресеты прямо в чёлке", Toggle(nameof(Settings.PgEq)))));

            var install = new StackPanel();
            page.Children.Add(Group("Движок Equalizer APO",
                Row(inst ? "Установлен" : "Установить Equalizer APO", inst ? Equalizer.ConfigDir : "Бесплатная открытая программа (sourceforge.net/projects/equalizerapo), её используют Peace и другие эквалайзеры. " +
                    "В установщике отметь свои наушники и колонки, потом перезагрузи ПК — дальше всё управляется из N-Island",
                    inst ? UI.Pill("Выбрать устройства", Equalizer.OpenConfigurator) : UI.Pill("Скачать и установить", async () =>
                    {
                        install.Children.Clear();
                        var t = Info("Скачиваю…");
                        install.Children.Add(t);
                        var err = await Equalizer.Install(s => t.Text = I18n.T(s));
                        t.Text = I18n.T(err ?? "Установщик запущен. Отметь устройства, нажми OK и перезагрузи компьютер.");
                    }, true)),
                install,
                Equalizer.NeedsPermission
                    ? Row("Нет права записи", "Папка настроек APO закрыта для обычных программ — дай доступ один раз", UI.Pill("Разрешить запись", () => { Equalizer.FixPermissions(); Upd(); Open("eq"); }, true))
                    : null,
                inst ? Row("Убрать N-Island из APO", "Удалит строку подключения и файл N-Island.txt — свои настройки APO останутся как были", UI.Pill("Отключить", () => { Equalizer.Detach(); Open("eq"); })) : null));
            page.Children.Add(Note("Windows сама не даёт программам менять звук других приложений — для этого и нужен движок APO. " +
                                   "N-Island пишет только свой файл N-Island.txt в папку настроек APO; громкие места не хрипят — общий уровень автоматически снижается на величину самого большого подъёма."));
            return page;
        }

        /// <summary>Переключатель с действием (а не просто привязкой) — состояние всё равно следует за настройкой.</summary>
        static CheckBox ToggleAct(string prop, Action<bool> act)
        {
            var p = P(prop);
            var c = new CheckBox { Style = UI.St("Switch"), IsChecked = (bool)p.GetValue(S) };
            c.Click += (_, __) => act(c.IsChecked == true);
            Follow(c, prop, () => c.IsChecked = (bool)p.GetValue(S));
            return c;
        }

        // ------------------------------------------------------------------
        //  Визуализатор
        // ------------------------------------------------------------------

        UIElement VizSec()
        {
            var preview = new Visualizer { Height = 170, VisualStyle = S.VizStyle, Palette = S.VizPalette, MaxPixels = 480 * 170,
                BaseColor = MediaService.I.Art != null ? MediaService.I.ArtColor : (UI.Res("AccentBrush") as SolidColorBrush)?.Color ?? Colors.DodgerBlue };
            var desc = Info(Visualizer.Styles.FirstOrDefault(x => x.Key == S.VizStyle)?.Desc ?? "");
            var groups = new StackPanel { Margin = new Thickness(14, 6, 14, 6) };
            var favs = new HashSet<string>((S.VizFavorites ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            var buttons = new List<Button>();
            void Mark()
            {
                foreach (var b in buttons)
                {
                    var key = (string)b.Tag;
                    b.Style = UI.St(key == preview.VisualStyle ? "AccentPill" : "PillButton");
                    b.Content = (favs.Contains(key) ? "★ " : "") + I18n.T(Visualizer.NameOf(key));
                }
                desc.Text = I18n.T(Visualizer.Styles.FirstOrDefault(x => x.Key == preview.VisualStyle)?.Desc ?? "");
            }
            foreach (var g in Visualizer.Styles.GroupBy(s => s.Group))
            {
                groups.Children.Add(UI.Text(g.Key + $" · {g.Count()}", 11, true, UI.Dim).M(0, 6, 0, 4));
                var wrap = new WrapPanel();
                foreach (var st in g)
                {
                    var key = st.Key;
                    var b = UI.Pill(st.Name, () => { preview.VisualStyle = key; S.VizStyle = key; Mark(); }, false, I18n.T(st.Desc) + "\n" + I18n.T("Правый клик — в избранное"));
                    b.Tag = key;
                    b.Margin = new Thickness(0, 0, 6, 6);
                    b.MouseRightButtonUp += (_, e) =>
                    {
                        if (!favs.Remove(key)) favs.Add(key);
                        S.VizFavorites = string.Join(",", favs);
                        Mark();
                        e.Handled = true;
                    };
                    buttons.Add(b);
                    wrap.Children.Add(b);
                }
                groups.Children.Add(wrap);
            }

            // палитры с образцом цвета
            var pal = new WrapPanel { Margin = new Thickness(14, 6, 14, 10) };
            foreach (var (key, name) in Visualizer.Palettes)
            {
                var k = key;
                var swatch = new Border { Width = 26, Height = 10, CornerRadius = new CornerRadius(5), Margin = new Thickness(0, 0, 6, 0), Background = PaletteBrush(k) };
                var b = new Button { Content = UI.Row(swatch, UI.Text(name, 12)), Tag = key, Margin = new Thickness(0, 0, 6, 6), Cursor = Cursors.Hand };
                b.Style = UI.St(key == S.VizPalette ? "AccentPill" : "PillButton");
                b.Click += (_, __) => { preview.Palette = k; S.VizPalette = k; foreach (Button x in pal.Children) x.Style = UI.St((string)x.Tag == k ? "AccentPill" : "PillButton"); };
                pal.Children.Add(b);
            }
            var colA = Field(S.VizColorA, 110, v => { S.VizColorA = v.Trim(); }, "#FF2D95");
            var colB = Field(S.VizColorB, 110, v => { S.VizColorB = v.Trim(); }, "#00E5FF");
            Mark();
            var hint = AudioCapture.I.Error;
            string Pct(int v) => v + "%";
            return Panel(
                Group(null,
                    new Border { CornerRadius = new CornerRadius(10), ClipToBounds = true, Background = Brushes.Black, Margin = new Thickness(14, 12, 14, 4), Child = preview },
                    desc,
                    Wide($"Стили · {Visualizer.Styles.Length}", "Классика старых плееров, ностальгия и новые. Правый клик — в избранное ★: автосмена и стрелки в окне ходят только по избранным", groups),
                    Wide("Цвета", "«Цвет обложки» подстраивается под каждый трек", pal),
                    Row("Свои цвета", "Для палитры «Свои цвета» — два цвета в формате #RRGGBB", UI.Row(colA, colB.M(6, 0, 0, 0))),
                    Row("Визуализатор в плеере", "Колонка справа от плеера вместо текста песни (строка песни идёт поверх)", Toggle(nameof(Settings.MediaVisual))),
                    Row("Полоски в чёлке слушают звук", "Пляшут под настоящую музыку: бас, середина, верха. Выключи — будет просто анимация", Toggle(nameof(Settings.LiveBars)))),
                Group("Тонкая настройка",
                    Row("Чувствительность", "Тихая музыка тоже раскачивает картинку", Stepper(nameof(Settings.VizSens), 20, 400, 10, Pct)),
                    Row("Плавность", "Больше — движения мягче, меньше — резче и точнее", Stepper(nameof(Settings.VizSmooth), 0, 90, 5, v => v.ToString())),
                    Row("След", "50 — как задумано стилем; больше — длинные шлейфы, меньше — чётко", Stepper(nameof(Settings.VizTrail), 0, 100, 5, v => v.ToString())),
                    Row("Скорость движения", null, Stepper(nameof(Settings.VizSpeed), 10, 400, 10, Pct)),
                    Row("Плотность", "Сколько полос, частиц и звёзд", Stepper(nameof(Settings.VizDensity), 30, 300, 10, Pct)),
                    Row("Реакция на удары", "0 — без вспышек и рывков на бас", Stepper(nameof(Settings.VizBeatFx), 0, 200, 10, Pct)),
                    Row("Свечение", "Мягкий ореол вокруг ярких мест", Stepper(nameof(Settings.VizGlow), 0, 100, 10, v => v == 0 ? "нет" : v.ToString())),
                    Row("Перелив цветов", "Палитра медленно плывёт по кругу оттенков", Stepper(nameof(Settings.VizHue), 0, 100, 5, v => v == 0 ? "нет" : v.ToString())),
                    Row("Насыщенность", null, Stepper(nameof(Settings.VizSat), 0, 150, 10, Pct)),
                    Row("Яркость", null, Stepper(nameof(Settings.VizBright), 40, 160, 10, Pct)),
                    Row("Зеркало", "Левая половина отражается вправо — симметрично", Toggle(nameof(Settings.VizMirror))),
                    Row("Вспышка на ударе", "Весь кадр мигает на сильный бас", Toggle(nameof(Settings.VizBeatFlash))),
                    Row("Фон", null, Seg(nameof(Settings.VizBg), ("none", "Прозрачный"), ("dark", "Тёмный"), ("art", "Цвет обложки"), ("vignette", "Виньетка"))),
                    Row("Кадров в секунду", "Больше — плавнее, но выше нагрузка", Seg(nameof(Settings.VizFps), (30, "30"), (45, "45"), (60, "60"))),
                    Row("Сбросить настройку", "Вернуть все ползунки как было", UI.Pill("Сбросить", () =>
                    {
                        S.VizSens = 100; S.VizSmooth = 25; S.VizTrail = 50; S.VizSpeed = 100; S.VizDensity = 100; S.VizBeatFx = 100; S.VizGlow = 30;
                        S.VizHue = 0; S.VizSat = 100; S.VizBright = 100; S.VizMirror = false; S.VizBeatFlash = false; S.VizBg = "none"; S.VizFps = 45;
                        Open("visual");
                    }))),
                Group("Окно на весь экран",
                    Row("Открыть визуализатор", "Отдельное окно: F11 — весь экран, ← → — стиль, C — цвета, A — автосмена, L — текст песни", UI.Pill("Открыть", () => VisualizerWindow.Open(), true)),
                    Row("Автосмена стилей", "Следующий стиль через заданное время (только избранные, если они есть)", Toggle(nameof(Settings.VizAuto))),
                    Row("Интервал автосмены", null, Stepper(nameof(Settings.VizAutoSec), 10, 300, 10, v => v + " с")),
                    Row("Текст песни поверх", "Текущая строка крупно внизу", Toggle(nameof(Settings.VizLyrics))),
                    Row("Название трека", "Обложка и название в углу, прячутся через несколько секунд", Toggle(nameof(Settings.VizTrack))),
                    Row("Качество картинки", "Выше — чётче, но больше нагрузка на процессор", Seg(nameof(Settings.VizQuality), (1, "Экономно"), (2, "Обычно"), (3, "Высокое")))),
                Note("Визуализатор слушает то, что звучит в колонках или наушниках (как запись «что слышно»), только пока он на экране. Звук никуда не записывается и не отправляется." +
                     (hint != null ? "\n" + I18n.T("Сейчас: ") + I18n.T(hint) : "")));
        }

        /// <summary>Образец палитры: полоска-градиент.</summary>
        static Brush PaletteBrush(string key)
        {
            Color C(string hex) => ColorUtil.Parse(hex, Colors.Gray);
            var stops = key switch
            {
                "art" => new[] { C("#3A6EA5"), C("#9DC6FF") },
                "accent" => new[] { ((UI.Res("AccentBrush") as SolidColorBrush)?.Color ?? Colors.DodgerBlue), Colors.White },
                "rainbow" => new[] { C("#FF3B30"), C("#FFCC00"), C("#34C759"), C("#0A84FF"), C("#BF5AF2") },
                "fire" => new[] { C("#600000"), C("#FF8C00"), C("#FFFFC0") },
                "ocean" => new[] { C("#001E46"), C("#005FD2"), C("#D7FFFF") },
                "neon" => new[] { C("#7D00FF"), C("#FF00B9"), C("#00F0FF") },
                "mono" => new[] { C("#6E6E7D"), Colors.White },
                "sunset" => new[] { C("#2D0A5A"), C("#C81E6E"), C("#FF6E3C"), C("#FFDC78") },
                "aurora" => new[] { C("#052838"), C("#00C88C"), C("#C878FF") },
                "vapor" => new[] { C("#3C0078"), C("#FF28B4"), C("#00F0FF") },
                "matrix" => new[] { C("#001E05"), C("#00B428"), C("#DCFFDC") },
                "ice" => new[] { C("#14285A"), C("#5AAAFF"), C("#F0FAFF") },
                "lava" => new[] { C("#960014"), C("#FF3C00"), C("#FFAA00") },
                "candy" => new[] { C("#FF5AA0"), C("#FFC850"), C("#78DCFF"), C("#C88CFF") },
                "gold" => new[] { C("#3C2300"), C("#D29620"), C("#FFFAD2") },
                "amber" => new[] { C("#280F00"), C("#FF9600"), C("#FFE196") },
                "cga" => new[] { C("#55FFFF"), C("#FF55FF"), Colors.White },
                "pastel" => new[] { C("#A6D8FF"), C("#D6B3FF"), C("#FFB3D9") },
                "forest" => new[] { C("#0A280A"), C("#3CA03C"), C("#F0FFC8") },
                "custom" => new[] { C(S.VizColorA), C(S.VizColorB) },
                _ => new[] { Colors.Gray, Colors.White },
            };
            var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            for (int i = 0; i < stops.Length; i++) b.GradientStops.Add(new GradientStop(stops[i], i / (double)Math.Max(1, stops.Length - 1)));
            return b;
        }

        // ------------------------------------------------------------------
        //  Windows
        // ------------------------------------------------------------------

        UIElement WindowsSec()
        {
            var urls = new StackPanel { Margin = new Thickness(14, 0, 14, 10) };
            foreach (var (url, what) in WinShell.UrlExamples)
                urls.Children.Add(UI.Grid("260,*").Add(new TextBox { Text = url, IsReadOnly = true, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = UI.Hex("#9FD0FF"), FontFamily = new FontFamily("Consolas"), FontSize = 12, Tag = "no-translate" },
                    UI.Text(what, 12, false, UI.Dim).At(0, 1)).M(0, 1, 0, 1));
            var focus = WinShell.FocusAssist();
            return Panel(
                Group("Проводник",
                    Row("Пункты в меню файлов", "Правый клик по файлу → «Проверить файл в N-Island» (подпись, настоящий тип, откуда скачан, VirusTotal); по фото → «Копия без метаданных и GPS». " +
                        "В Windows 11 — в «Показать дополнительные параметры»", Toggle(nameof(Settings.ShellMenu)))),
                Group("Ссылки nisland://",
                    Row("Команды по ссылке", "Ярлыки, скрипты, Stream Deck, AutoHotkey и умный дом могут управлять островом: музыка, страницы, всплывашки, таймер, режимы, эквалайзер", Toggle(nameof(Settings.UrlProtocol))),
                    urls),
                Group("Система",
                    Row("«Не беспокоить» Windows", "Включил «Не беспокоить» в центре уведомлений Windows — остров тоже затихает, выключил — возвращается прежний режим" +
                        (focus == null ? I18n.T(" (на этом ПК состояние не читается)") : focus > 0 ? I18n.T(" · сейчас включено") : ""), Toggle(nameof(Settings.FollowFocusAssist))),
                    Row("Цвет как у Windows", "Акцентный цвет острова и окон берётся из Параметры → Персонализация → Цвета" + (WinShell.WindowsAccentHex() is { } hx ? " (" + hx + ")" : ""), Toggle(nameof(Settings.WindowsAccent))),
                    Row("Запуск вместе с Windows", "Остров появляется сразу после входа в систему", Toggle(nameof(Settings.StartWithWindows))),
                    Row("Без дублей Windows", "Баннеры уведомлений, окно «Автозапуск» и системная громкость — когда остров их уже показывает", UI.Pill("Настроить", () => Open("dedupe")))),
                Note("Всё здесь записывается только для твоей учётной записи (HKCU), без прав администратора, и полностью убирается выключением переключателя."));
        }
    }
}
