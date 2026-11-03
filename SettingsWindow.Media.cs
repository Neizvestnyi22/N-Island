using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
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
            var preview = new Visualizer { Height = 150, VisualStyle = S.VizStyle, Palette = S.VizPalette, MaxPixels = 480 * 150, Fps = 40,
                BaseColor = MediaService.I.Art != null ? MediaService.I.ArtColor : (UI.Res("AccentBrush") as SolidColorBrush)?.Color ?? Colors.DodgerBlue };
            var styles = new WrapPanel { Margin = new Thickness(14, 6, 14, 6) };
            var desc = Info(Visualizer.Styles.First(x => x.Key == S.VizStyle || x == Visualizer.Styles[0]).Desc);
            void Mark()
            {
                foreach (Button b in styles.Children) b.Style = UI.St((string)b.Tag == preview.VisualStyle ? "AccentPill" : "PillButton");
                desc.Text = I18n.T(Visualizer.Styles.FirstOrDefault(x => x.Key == preview.VisualStyle)?.Desc ?? "");
            }
            foreach (var st in Visualizer.Styles)
            {
                var key = st.Key;
                var b = UI.Pill(st.Name, () => { preview.VisualStyle = key; S.VizStyle = key; Mark(); }, false, st.Desc);
                b.Tag = key;
                b.Margin = new Thickness(0, 0, 6, 6);
                styles.Children.Add(b);
            }
            var pal = new WrapPanel { Margin = new Thickness(14, 6, 14, 10) };
            foreach (var (key, name) in Visualizer.Palettes)
            {
                var k = key;
                var b = UI.Pill(name, () => { preview.Palette = k; S.VizPalette = k; foreach (Button x in pal.Children) x.Style = UI.St((string)x.Tag == k ? "AccentPill" : "PillButton"); });
                b.Tag = key;
                b.Style = UI.St(key == S.VizPalette ? "AccentPill" : "PillButton");
                b.Margin = new Thickness(0, 0, 6, 6);
                pal.Children.Add(b);
            }
            Mark();
            var hint = AudioCapture.I.Error;
            return Panel(
                Group(null,
                    new Border { CornerRadius = new CornerRadius(10), ClipToBounds = true, Background = Brushes.Black, Margin = new Thickness(14, 12, 14, 4), Child = preview },
                    desc,
                    Wide("Стиль в плеере чёлки", "Классика старых плееров и новые. Включается кнопкой с пульсом рядом с «Текстом песни» в плеере", styles),
                    Wide("Цвета", "«Цвет обложки» подстраивается под каждый трек", pal),
                    Row("Визуализатор в плеере", "Колонка справа от плеера вместо текста песни (строка песни идёт поверх)", Toggle(nameof(Settings.MediaVisual))),
                    Row("Полоски в чёлке слушают звук", "Пляшут под настоящую музыку: бас, середина, верха. Выключи — будет просто анимация", Toggle(nameof(Settings.LiveBars)))),
                Group("Окно на весь экран",
                    Row("Открыть визуализатор", "Отдельное окно: F11 — весь экран, ← → — стиль, C — цвета, A — автосмена, L — текст песни", UI.Pill("Открыть", () => VisualizerWindow.Open(), true)),
                    Row("Автосмена стилей", "Каждые 30 секунд — следующий стиль", Toggle(nameof(Settings.VizAuto))),
                    Row("Текст песни поверх", "Текущая строка крупно внизу", Toggle(nameof(Settings.VizLyrics))),
                    Row("Название трека", "Обложка и название в углу, прячутся через несколько секунд", Toggle(nameof(Settings.VizTrack))),
                    Row("Качество картинки", "Выше — чётче, но больше нагрузка на процессор", Seg(nameof(Settings.VizQuality), (1, "Экономно"), (2, "Обычно"), (3, "Высокое")))),
                Note("Визуализатор слушает то, что звучит в колонках или наушниках (как запись «что слышно»), только пока он на экране. Звук никуда не записывается и не отправляется." +
                     (hint != null ? "\n" + I18n.T("Сейчас: ") + I18n.T(hint) : "")));
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
