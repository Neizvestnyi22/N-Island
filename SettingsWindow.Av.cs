using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NIsland.Core;
using NIsland.Pages;
using NIsland.Services;

namespace NIsland
{
    // Центр управления → Защитник N-Island
    public partial class SettingsWindow
    {
        CancellationTokenSource _avCts;

        UIElement AvSec()
        {
            var page = new StackPanel();
            var status = Info("");
            void Upd()
            {
                var q = Antivirus.Quarantined().Count;
                var last = DateTime.TryParse(S.AvLastScan, null, System.Globalization.DateTimeStyles.RoundtripKind, out var ls) ? ls.ToString("d MMM, HH:mm", Fmt.Ru) : I18n.T("ещё не было");
                status.Text = $"{(S.AvRealtime ? I18n.T("Защита в реальном времени включена ✓") : I18n.T("Защита в реальном времени выключена"))} · " +
                              $"{I18n.T("база угроз")}: {Antivirus.DbCount:#,0} {I18n.T("записей")}" + (Antivirus.DbUpdated is DateTime d ? $" ({d:dd.MM})" : "") +
                              $" · {I18n.T("последняя проверка")}: {last} · {I18n.T("в карантине")}: {q}";
            }
            Upd();

            var results = new StackPanel();
            var progress = Loading("");
            progress.Visibility = Visibility.Collapsed;
            void ShowResults(List<Threat> list)
            {
                results.Children.Clear();
                results.Children.Add(Info(list.Count == 0 ? "Угроз не найдено ✓" : $"Найдено: {list.Count} (опасных {list.Count(t => t.Score >= 70)})", list.Count == 0 ? "#30D158" : "#FF9F0A"));
                foreach (var t in list) results.Children.Add(ThreatRow(t, results, Upd));
                Upd();
            }
            async Task<string> Run(Func<CancellationToken, Task<List<Threat>>> scan)
            {
                _avCts?.Cancel();
                _avCts = new CancellationTokenSource();
                results.Children.Clear();
                progress.Visibility = Visibility.Visible;
                var list = await scan(_avCts.Token);
                progress.Visibility = Visibility.Collapsed;
                ShowResults(list);
                return null;
            }
            void Prog(string path, int i, int n) => Dispatcher.BeginInvoke(new Action(() => progress.Text = n == 0 ? I18n.T(path) : $"{i} / {n} · {System.IO.Path.GetFileName(path)}"));

            page.Children.Add(Group("Защитник N-Island",
                status,
                Item("Быстрая проверка", "Автозагрузка, задачи планировщика, запущенные программы и свежие файлы в «Загрузках», на рабочем столе, во временных папках и AppData. 1–3 минуты.", Safety.Safe,
                    UI.Row(Act("Проверить", () => Run(ct => Antivirus.QuickScan(Prog, ct)), null, true),
                           UI.Pill("Стоп", () => _avCts?.Cancel()).M(6, 0, 0, 0))),
                Item("Проверить папку", "Все программы, скрипты, ярлыки и архивы в выбранной папке (например, флешка или папка со скачанными играми).", Safety.Safe,
                    Act("Выбрать папку…", async () =>
                    {
                        using var dlg = new System.Windows.Forms.FolderBrowserDialog { UseDescriptionForTitle = true, Description = I18n.T("Что проверить") };
                        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return null;
                        return await Run(ct => Antivirus.ScanFolder(dlg.SelectedPath, Prog, ct));
                    })),
                Item("Проверить файл", "Один файл — подробно, с причинами. Можно и из Проводника: правый клик → «Проверить в N-Island».", Safety.Safe,
                    Act("Выбрать файл…", async () =>
                    {
                        var dlg = new Microsoft.Win32.OpenFileDialog { Title = I18n.T("Что проверить") };
                        if (dlg.ShowDialog() != true) return null;
                        var t = await Task.Run(() => Antivirus.ScanFile(dlg.FileName));
                        results.Children.Clear();
                        results.Children.Add(ThreatRow(t, results, Upd, expanded: true));
                        return null;
                    })),
                progress, results));

            page.Children.Add(Group("Защита",
                Row("В реальном времени", "Каждый новый файл в «Загрузках», на рабочем столе и в автозагрузке проверяется сразу; если опасно — всплывашка с кнопкой «В карантин» и сообщение на телефон",
                    ToggleAct(nameof(Settings.AvRealtime), v => { S.AvRealtime = v; Antivirus.ApplyRealtime(); Upd(); })),
                Row("Следить за автозагрузкой", "Сообщу, когда какая-то программа пропишет себя в автозапуск Windows — так закрепляется большинство вирусов",
                    ToggleAct(nameof(Settings.AvWatchStartup), v => { S.AvWatchStartup = v; Antivirus.ApplyRealtime(); })),
                Row("База известных угроз", "Хеши вредоносных файлов из открытой базы signature-base (Florian Roth, лицензия DRL). Обновляется сама раз в неделю",
                    UI.Pill("Обновить", async () => { var e = await Antivirus.UpdateDb(); Upd(); App.Island.Toast("", "База угроз", e ?? I18n.T("Обновлена: ") + Antivirus.DbCount.ToString("#,0")); }))));

            // аудит
            var audit = new StackPanel();
            page.Children.Add(Group("Настройки безопасности Windows",
                Item("Проверить", "Защитник и его базы, брандмауэр, UAC, SmartScreen, подмена сайтов в hosts, прокси, удалённый рабочий стол, неподписанные программы в автозапуске.", Safety.Safe,
                    Act("Проверить", async () =>
                    {
                        audit.Children.Clear();
                        audit.Children.Add(Loading("Проверяю…"));
                        var list = await Antivirus.Audit();
                        audit.Children.Clear();
                        foreach (var (lvl, title, advice) in list)
                        {
                            var (glyph, color) = lvl switch { 2 => ("", "#FF453A"), 1 => ("", "#FF9F0A"), _ => ("", "#30D158") };
                            audit.Children.Add(UI.Grid("a,*").M(14, 5, 14, 5).Add(UI.Icon(glyph, 14, UI.Hex(color)).VA(VerticalAlignment.Top).M(0, 2, 10, 0),
                                UI.Col(new TextBlock { Text = I18n.T(title), FontSize = 13, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap },
                                       advice == null ? null : new TextBlock { Text = I18n.T(advice), FontSize = 12, Foreground = UI.Dim, TextWrapping = TextWrapping.Wrap }).At(0, 1)));
                        }
                        return null;
                    }, null, true),
                    extra: UI.Pill("Безопасность Windows", () => SystemService.Run("windowsdefender:"))),
                audit));

            // карантин
            var qBox = new StackPanel();
            void RenderQ()
            {
                qBox.Children.Clear();
                var q = Antivirus.Quarantined();
                if (q.Count == 0) qBox.Children.Add(Info("Карантин пуст"));
                foreach (var it in q)
                {
                    var id = it.Id;
                    qBox.Children.Add(UI.Grid("*,a,a").M(14, 4, 14, 4).Add(
                        UI.Col(new TextBlock { Text = System.IO.Path.GetFileName(it.Original), FontSize = 13, FontWeight = FontWeights.SemiBold, Tag = "no-translate" },
                               new TextBlock { Text = $"{it.At:dd.MM HH:mm} · {Maint.Size(it.Size)} · {it.Original}", FontSize = 11, Foreground = UI.Dim, Tag = "no-translate", TextTrimming = TextTrimming.CharacterEllipsis },
                               new TextBlock { Text = I18n.T(it.Reason ?? ""), FontSize = 11, Foreground = UI.Dim, TextWrapping = TextWrapping.Wrap }),
                        UI.Pill("Вернуть", () => { var e = Antivirus.Restore(id); if (e != null) App.Island.Toast("", "Не удалось", e); RenderQ(); Upd(); }, false, "Файл вернётся на место и попадёт в список доверенных").At(0, 1).M(8, 0, 0, 0),
                        UI.Pill("Удалить навсегда", () => { Antivirus.DeleteForever(id); RenderQ(); Upd(); }, false, "Удалить зашифрованную копию без возможности восстановления").At(0, 2).M(6, 0, 0, 0)));
                }
            }
            RenderQ();
            Antivirus.Changed += () => Dispatcher.BeginInvoke(new Action(() => { RenderQ(); Upd(); }));
            page.Children.Add(Group("Карантин", Info("Файлы в карантине зашифрованы и не могут запуститься. Вернуть можно в любой момент."), qBox));

            var hist = new StackPanel();
            lock (Antivirus.History) foreach (var (at, text) in Antivirus.History.Take(12))
                hist.Children.Add(UI.Grid("a,*").M(14, 2, 14, 2).Add(UI.Text(at.ToString("HH:mm"), 11, false, UI.Dim).M(0, 0, 8, 0), new TextBlock { Text = text, FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis, Tag = "no-translate" }.At(0, 1)));
            if (hist.Children.Count > 0) page.Children.Add(Group("Журнал", hist));
            page.Children.Add(Note("Защитник N-Island дополняет Защитник Windows, а не заменяет его: использует его движок (AMSI), базу известных угроз и свои проверки поведения программ и скриптов. " +
                                   "Оценка «Подозрительно» — не приговор: проверь файл Защитником Windows или онлайн, прежде чем удалять."));
            return page;
        }

        UIElement ThreatRow(Threat t, StackPanel host, Action upd, bool expanded = false)
        {
            var color = t.Score >= 70 ? "#FF453A" : t.Score >= 40 ? "#FF9F0A" : "#30D158";
            var extra = new StackPanel { Visibility = expanded ? Visibility.Visible : Visibility.Collapsed };
            foreach (var r in t.Reasons) extra.Children.Add(new TextBlock { Text = "• " + I18n.T(r), FontSize = 12, Foreground = UI.Dim, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 0) });
            var check = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
            extra.Children.Add(check);
            UIElement row = null;
            var buttons = UI.Row(
                t.Score >= 40 ? UI.Pill("В карантин", () =>
                {
                    var e = Antivirus.Quarantine(t.Path, string.Join("; ", t.Reasons.Take(3)));
                    if (e == null) host.Children.Remove(row); else check.Text = e;
                    upd();
                }, true) : null,
                UI.Pill("Защитник Windows", async () => { check.Text = I18n.T("Проверяю…"); extra.Visibility = Visibility.Visible; var (_, txt) = await Antivirus.DefenderScan(t.Path); check.Text = I18n.T(txt); }).M(6, 0, 0, 0),
                t.Sha256 == null ? null : UI.Pill("Онлайн", async () => { check.Text = I18n.T("Проверяю…"); extra.Visibility = Visibility.Visible; check.Text = I18n.T(await Antivirus.OnlineLookup(t.Sha256)); }, false, "Только отпечаток файла (SHA-256) уходит в открытую базу CIRCL").M(6, 0, 0, 0),
                UI.Pill("Показать", () => System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{t.Path}\"")?.Dispose()).M(6, 0, 0, 0),
                t.Score >= 40 && t.Sha256 != null ? UI.Pill("Доверяю", () => { Antivirus.Allow(t.Sha256); host.Children.Remove(row); }, false, "Больше не предупреждать об этом файле").M(6, 0, 0, 0) : null);
            var head = UI.Grid("a,*").Add(
                new Border { Width = 44, CornerRadius = new CornerRadius(8), Background = UI.Hex(color), Padding = new Thickness(0, 3, 0, 3), VerticalAlignment = VerticalAlignment.Top,
                    Child = UI.Text(t.Score.ToString(), 14, true, Brushes.Black).HA(HorizontalAlignment.Center) },
                UI.Col(new TextBlock { Text = System.IO.Path.GetFileName(t.Path) + " — " + I18n.T(t.Verdict), FontSize = 13, FontWeight = FontWeights.SemiBold, Tag = "no-translate", TextWrapping = TextWrapping.Wrap },
                       new TextBlock { Text = t.Path, FontSize = 11, Foreground = UI.Dim, Tag = "no-translate", TextTrimming = TextTrimming.CharacterEllipsis },
                       extra, buttons.M(0, 6, 0, 0)).At(0, 1).M(10, 0, 0, 0));
            head.Cursor = System.Windows.Input.Cursors.Hand;
            head.MouseLeftButtonUp += (_, e) => { if (e.OriginalSource is TextBlock) extra.Visibility = extra.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible; };
            row = new Border { Margin = new Thickness(14, 4, 14, 6), Child = head };
            return row;
        }
    }
}
