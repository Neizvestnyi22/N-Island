using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NIsland.Core;
using NIsland.Pages;
using NIsland.Services;

namespace NIsland
{
    // Центр управления → Здоровье ПК
    public partial class SettingsWindow
    {
        UIElement HealthSec()
        {
            var page = new StackPanel();

            // 1. оценка
            var scoreBox = new StackPanel();
            page.Children.Add(Group("Оценка здоровья",
                Item("Проверить ПК", "Место на дисках, синие экраны и сбои, устройства с ошибками, обновления, перезагрузки, память, автозапуск, батарея и мусор — одна оценка из 100 и что исправить в первую очередь.", Safety.Safe,
                    Act("Проверить", async () =>
                    {
                        scoreBox.Children.Clear();
                        var p = Loading("Проверяю…");
                        scoreBox.Children.Add(p);
                        var (score, issues) = await PcHealth.Score(s => Dispatcher.BeginInvoke(new Action(() => p.Text = I18n.T(s))));
                        scoreBox.Children.Clear();
                        var color = score >= 85 ? "#30D158" : score >= 65 ? "#FFD60A" : "#FF453A";
                        scoreBox.Children.Add(UI.Row(UI.Text(score.ToString(), 34, true, UI.Hex(color)), UI.Text(" / 100", 14, false, UI.Dim).VA(VerticalAlignment.Bottom).M(0, 0, 0, 7),
                            UI.Text(score >= 85 ? "Компьютер в отличной форме" : score >= 65 ? "Есть что улучшить" : "Нужно внимание", 14, true).VA(VerticalAlignment.Center).M(14, 0, 0, 0)).M(14, 8, 14, 4));
                        foreach (var i in issues)
                        {
                            var sec = i.Section;
                            scoreBox.Children.Add(UI.Grid("*,a").M(14, 5, 14, 5).Add(
                                UI.Col(new TextBlock { Text = I18n.T(i.Title), FontSize = 13, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap },
                                       new TextBlock { Text = I18n.T(i.Advice), FontSize = 12, Foreground = UI.Dim, TextWrapping = TextWrapping.Wrap }),
                                UI.Text("−" + i.Penalty, 12, true, UI.Hex("#FF9F0A")).At(0, 1).M(10, 0, 0, 0)));
                        }
                        if (issues.Count == 0) scoreBox.Children.Add(Info("Ничего не нашлось — так держать ✓", "#30D158"));
                        return null;
                    }, null, true)),
                scoreBox));

            // 2. турбо
            var turboState = Info(PcHealth.TurboOn ? "Турбо-режим включён" : "");
            page.Children.Add(Group("Турбо-режим",
                Item("Максимум скорости", "На время игры, монтажа или сборки: схема питания «Высокая производительность», освобождение памяти, ПК не засыпает, остров не отвлекает. Кнопка «Выключить» возвращает всё как было.", Safety.Safe,
                    UI.Row(Act("Включить", async () => { var r = await PcHealth.Turbo(true); turboState.Text = I18n.T(r); return null; }, null, true),
                           Act("Выключить", async () => { var r = await PcHealth.Turbo(false); turboState.Text = I18n.T(r); return null; }).M(6, 0, 0, 0))),
                turboState));

            // 3. процессор
            var cpuBox = new StackPanel();
            page.Children.Add(Group("Процессор: частота и перегрев",
                Item("Проверить троттлинг", "Сравнивает текущую частоту процессора с базовой. Если под нагрузкой частота сильно ниже — процессор перегревается или зажат режимом экономии.", Safety.Safe,
                    Act("Проверить", async () =>
                    {
                        cpuBox.Children.Clear();
                        cpuBox.Children.Add(Loading("Меряю…"));
                        var c = await PcHealth.Cpu();
                        cpuBox.Children.Clear();
                        cpuBox.Children.Add(Table(new[]
                        {
                            ("Сейчас", c.PerfPct < 0 ? "—" : $"{c.MhzNow} МГц ({c.PerfPct}% от базовой)"), ("Базовая частота", c.MhzBase > 0 ? c.MhzBase + " МГц" : "—"),
                            ("Нагрузка", SystemService.I.CpuPercent() + "%"),
                        }));
                        cpuBox.Children.Add(Info(c.Why, c.Throttled ? "#FF9F0A" : "#30D158"));
                        return null;
                    })),
                cpuBox));

            // 4. сбои
            var crashBox = new StackPanel();
            page.Children.Add(Group("Сбои и синие экраны",
                Item("Журнал за месяц", "Читает журналы Windows: синие экраны, внезапные выключения, какие программы падали и зависали — и сколько раз.", Safety.Safe,
                    Act("Показать", async () =>
                    {
                        crashBox.Children.Clear();
                        crashBox.Children.Add(Loading("Читаю журналы…"));
                        var list = await PcHealth.Crashes(30);
                        crashBox.Children.Clear();
                        int bsod = list.Count(c => c.Kind == "bsod"), power = list.Count(c => c.Kind == "power");
                        crashBox.Children.Add(Info($"Синих экранов: {bsod} · внезапных выключений: {power} · сбоев программ: {list.Count(c => c.Kind is "crash" or "hang")}", bsod > 0 ? "#FF453A" : "#9FD0FF"));
                        foreach (var g in list.Where(c => c.Kind is "crash" or "hang").GroupBy(c => c.App).OrderByDescending(g => g.Count()).Take(6))
                            crashBox.Children.Add(UI.Grid("*,a").M(14, 3, 14, 3).Add(new TextBlock { Text = g.Key, FontSize = 12.5, Tag = "no-translate" },
                                UI.Text($"{g.Count()} раз · последний {g.First().At:dd.MM HH:mm}", 11.5, false, UI.Dim).At(0, 1)));
                        foreach (var c in list.Where(c => c.Kind is "bsod" or "power").Take(8))
                            crashBox.Children.Add(UI.Grid("a,*").M(14, 3, 14, 3).Add(UI.Text(c.At.ToString("dd.MM HH:mm"), 11.5, false, UI.Dim).M(0, 0, 10, 0), UI.Text(c.Detail, 12).At(0, 1)));
                        if (list.Count == 0) crashBox.Children.Add(Info("За месяц сбоев не было ✓", "#30D158"));
                        return null;
                    })),
                crashBox));

            // 5. устройства
            var devBox = new StackPanel();
            page.Children.Add(Group("Устройства с ошибками",
                Item("Проверить драйверы", "То, что в диспетчере устройств отмечено жёлтым треугольником: устройство без драйвера, с ошибкой или неподписанным драйвером — и что с этим делать.", Safety.Safe,
                    UI.Row(Act("Проверить", async () =>
                    {
                        devBox.Children.Clear();
                        var list = await PcHealth.ProblemDevices();
                        if (list.Count == 0) devBox.Children.Add(Info("Все устройства работают ✓", "#30D158"));
                        foreach (var d in list)
                            devBox.Children.Add(UI.Col(new TextBlock { Text = d.Name, FontSize = 13, FontWeight = FontWeights.SemiBold, Tag = "no-translate" }, UI.Text(d.Meaning, 12, false, UI.Dim)).M(14, 4, 14, 4));
                        return null;
                    }), UI.Pill("Диспетчер устройств", () => SystemService.Run("devmgmt.msc")).M(6, 0, 0, 0))),
                devBox));

            // 6. winget
            var wgBox = new StackPanel();
            page.Children.Add(Group("Обновления программ",
                Item("Найти устаревшие программы", "Через официальный winget Windows: какие программы можно обновить. Старые версии — частая причина сбоев и уязвимостей.", Safety.Safe,
                    UI.Row(Act("Проверить", async () =>
                    {
                        wgBox.Children.Clear();
                        wgBox.Children.Add(Loading("Спрашиваю winget (до минуты)…"));
                        var (list, err) = await PcHealth.WingetUpgrades();
                        wgBox.Children.Clear();
                        if (err != null) { wgBox.Children.Add(Info(err, "#FF9F0A")); return null; }
                        wgBox.Children.Add(Info(list.Count == 0 ? "Всё обновлено ✓" : $"Можно обновить: {list.Count}", list.Count == 0 ? "#30D158" : "#9FD0FF"));
                        foreach (var u in list.Take(30))
                        {
                            var id = u.Id;
                            wgBox.Children.Add(UI.Grid("*,a,a").M(14, 3, 14, 3).Add(
                                UI.Col(new TextBlock { Text = u.Name, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Tag = "no-translate" }, UI.Text($"{u.Current} → {u.Available}", 11, false, UI.Dim)),
                                UI.Pill("Обновить", () => Agents.OpenTerminal($"winget upgrade --id {id} --exact")).At(0, 2)));
                        }
                        return null;
                    }), UI.Pill("Обновить всё", () => Agents.OpenTerminal("winget upgrade --all"), false, "Откроет терминал: видно, что обновляется").M(6, 0, 0, 0))),
                wgBox));

            // 7. диск
            var duBox = new StackPanel();
            page.Children.Add(Group("Что занимает диск",
                Item("Разобрать место", "Системные файлы (гибернация, подкачка), Windows.old, корзина, папки пользователя и данные программ — по размеру, с подсказкой, что можно убрать.", Safety.Safe,
                    Act("Посчитать", async () =>
                    {
                        duBox.Children.Clear();
                        duBox.Children.Add(Loading("Считаю (до минуты)…"));
                        var list = await PcHealth.DiskUsage();
                        duBox.Children.Clear();
                        long max = list.Count > 0 ? list.Max(x => x.Bytes) : 1;
                        foreach (var (name, bytes, path, hint) in list)
                        {
                            var pth = path;
                            var bar = new Border { Height = 6, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, Width = Math.Max(4, 180.0 * bytes / max), Background = UI.Res("AccentBrush") };
                            var row = UI.Grid("*,a,a").M(14, 4, 14, 4).Add(
                                UI.Col(new TextBlock { Text = name, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Tag = "no-translate", TextWrapping = TextWrapping.Wrap }, bar.M(0, 3, 0, 0),
                                       hint == null ? null : UI.Text(hint, 11, false, UI.Dim)),
                                UI.Text(Maint.Size(bytes), 12.5, true).At(0, 1).M(10, 0, 8, 0),
                                pth == null ? null : UI.Pill("Открыть", () => System.Diagnostics.Process.Start("explorer.exe", System.IO.File.Exists(pth) ? $"/select,\"{pth}\"" : $"\"{pth}\"")?.Dispose()).At(0, 2));
                            duBox.Children.Add(row);
                        }
                        return null;
                    })),
                duBox));

            // 8–9. большие файлы и дубликаты
            var bigBox = new StackPanel();
            page.Children.Add(Group("Большие файлы",
                Item("Найти файлы больше 100 МБ", "В Рабочем столе, Загрузках, Документах, Изображениях, Видео и Музыке — забытые установщики, архивы и видео.", Safety.Safe,
                    Act("Найти", async () =>
                    {
                        bigBox.Children.Clear();
                        bigBox.Children.Add(Loading("Ищу…"));
                        var list = await PcHealth.LargeFiles();
                        bigBox.Children.Clear();
                        if (list.Count == 0) bigBox.Children.Add(Info("Больших файлов нет ✓"));
                        foreach (var f in list) bigBox.Children.Add(FileRow(f, bigBox));
                        return null;
                    })),
                bigBox));
            var dupBox = new StackPanel();
            page.Children.Add(Group("Дубликаты файлов",
                Item("Найти одинаковые файлы", "Сравнивает содержимое (SHA-256), а не только имена. В каждой группе первым — самый старый файл (оригинал), копии можно отправить в корзину.", Safety.Safe,
                    Act("Найти", async () =>
                    {
                        dupBox.Children.Clear();
                        var p = Loading("Ищу…");
                        dupBox.Children.Add(p);
                        var groups = await PcHealth.Duplicates(s => Dispatcher.BeginInvoke(new Action(() => p.Text = I18n.T(s))));
                        dupBox.Children.Clear();
                        long waste = groups.Sum(g => g[0].Length * (g.Count - 1));
                        dupBox.Children.Add(Info(groups.Count == 0 ? "Дубликатов нет ✓" : $"Групп: {groups.Count} · можно освободить {Maint.Size(waste)}", groups.Count == 0 ? "#30D158" : "#9FD0FF"));
                        foreach (var g in groups.Take(20))
                        {
                            dupBox.Children.Add(UI.Text($"{g[0].Name} · {Maint.Size(g[0].Length)} × {g.Count}", 12, true).M(14, 8, 14, 2));
                            for (int i = 0; i < g.Count; i++) dupBox.Children.Add(FileRow(g[i], dupBox, i == 0 ? "оригинал" : null));
                        }
                        return null;
                    })),
                dupBox));

            // 10–12. обновления, TRIM, память
            var sysBox = new StackPanel();
            _ = PcHealth.WindowsUpdate().ContinueWith(t => Dispatcher.Invoke(() =>
            {
                var (last, reboot, days) = t.Result;
                sysBox.Children.Add(Table(new[]
                {
                    ("Последнее обновление Windows", last?.ToString("d MMMM yyyy", Fmt.Ru) ?? "неизвестно"), ("Ждёт перезагрузки", reboot ? "да — перезагрузи ПК" : "нет"),
                    ("Без перезагрузки", days + " дн."), ("TRIM для SSD", PcHealth.TrimState()),
                }));
            }));
            page.Children.Add(Group("Обновления, SSD и память",
                sysBox,
                Row("Центр обновления Windows", null, UI.Pill("Открыть", () => SystemService.Run("ms-settings:windowsupdate"))),
                Row("Оптимизация дисков", "TRIM для SSD и дефрагментация для HDD — официальная утилита Windows", UI.Pill("Открыть", () => SystemService.Run("dfrgui.exe"))),
                Item("Проверить оперативную память", "Встроенная проверка Windows ищет неисправную память — частую причину синих экранов. Попросит перезагрузку и займёт 10–20 минут.", Safety.Caution,
                    UI.Pill("Запустить", () => SystemService.Run("mdsched.exe")))));
            page.Children.Add(Note("Здесь всё только читает систему. Меняют что-то лишь кнопки: «Турбо-режим» (возвращается кнопкой «Выключить»), обновления (в терминале видно, что делается) и «В корзину» (файл можно восстановить)."));
            return page;
        }

        UIElement FileRow(System.IO.FileInfo f, StackPanel host, string mark = null)
        {
            UIElement row = null;
            var path = f.FullName;
            row = UI.Grid("*,a,a,a").M(14, 2, 14, 2).Add(
                UI.Col(new TextBlock { Text = f.Name + (mark != null ? "  · " + I18n.T(mark) : ""), FontSize = 12, Tag = "no-translate", TextTrimming = TextTrimming.CharacterEllipsis },
                       new TextBlock { Text = f.DirectoryName, FontSize = 10.5, Foreground = UI.Dim, Tag = "no-translate", TextTrimming = TextTrimming.CharacterEllipsis }),
                UI.Text(Maint.Size(f.Length), 12, true).At(0, 1).M(8, 0, 8, 0),
                UI.Pill("Показать", () => System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"")?.Dispose()).At(0, 2),
                UI.Pill("В корзину", () =>
                {
                    try { PcControl.Recycle(path); host.Children.Remove(row); App.Island.Toast("", "В корзине", System.IO.Path.GetFileName(path)); }
                    catch (Exception e) { App.Island.Toast("", "Не удалось", e.Message); }
                }, false, "Файл можно будет восстановить из корзины").M(6, 0, 0, 0).At(0, 3));
            return row;
        }
    }
}
