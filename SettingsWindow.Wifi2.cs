using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using NIsland.Core;
using NIsland.Pages;
using NIsland.Services;

namespace NIsland
{
    // 1.8: ещё 11 инструментов на странице Wi-Fi
    public partial class SettingsWindow
    {
        void AddWifiExtras(StackPanel page)
        {
            // 1. точка доступа
            var hsState = Info("");
            var hsName = Field("", 200, null, "имя сети");
            var hsPass = Field("", 200, null, "пароль (от 8 символов)");
            void HsUpd()
            {
                var s = WifiTools2.HotspotState();
                hsState.Text = s == null ? I18n.T("Точка доступа недоступна: нет подключения или адаптер её не поддерживает")
                    : (s.Value.On ? I18n.T("Включена · подключено ") + s.Value.Clients + " / " + s.Value.Max : I18n.T("Выключена")) + $" · «{s.Value.Name}»";
                if (s != null && hsName.Text == "") { hsName.Text = s.Value.Name; hsPass.Text = s.Value.Pass; }
            }
            HsUpd();
            page.Children.Add(Group("Раздать интернет с ноутбука",
                Item("Мобильная точка доступа", "Ноутбук становится роутером: телефон, планшет или телевизор подключаются к нему по Wi-Fi и пользуются его интернетом.", Safety.Safe,
                    UI.Row(Act("Включить", async () => { var e = await WifiTools2.SetHotspot(true, hsName.Text, hsPass.Text); HsUpd(); return e; }, null, true),
                           Act("Выключить", async () => { var e = await WifiTools2.SetHotspot(false); HsUpd(); return e; }).M(6, 0, 0, 0))),
                Row("Имя и пароль", null, UI.Row(hsName, hsPass.M(6, 0, 0, 0), UI.Pill("Придумать", () => hsPass.Text = WifiTools2.StrongPassword(12)).M(6, 0, 0, 0))),
                hsState));

            // 2. стабильность
            var stab = new StackPanel();
            page.Children.Add(Group("Стабильность связи",
                Item("Тест на минуту", "120 замеров до Cloudflare раз в полсекунды: задержка, «скачки» (джиттер), потери и всплески. Покажет, почему лагают игры и рвутся звонки — даже если скорость хорошая.", Safety.Safe,
                    Act("Начать", async () =>
                    {
                        stab.Children.Clear();
                        var canvas = new Canvas { Height = 70, Margin = new Thickness(14, 6, 14, 6), ClipToBounds = true, Background = UI.Hex("#10FFFFFF") };
                        var line = new Polyline { Stroke = UI.Res("AccentBrush"), StrokeThickness = 1.5 };
                        canvas.Children.Add(line);
                        var cap = Loading("Замер…");
                        stab.Children.Add(canvas);
                        stab.Children.Add(cap);
                        var r = await WifiTools2.Stability(60, (i, ms) => Dispatcher.BeginInvoke(new Action(() =>
                        {
                            double w = Math.Max(100, canvas.ActualWidth);
                            double y = ms < 0 ? 0 : 70 - Math.Min(70, ms / 3);
                            line.Points.Add(new Point(i * w / 120, y));
                            if (ms < 0) canvas.Children.Add(new Rectangle { Width = 2, Height = 70, Fill = UI.Hex("#80FF453A"), Margin = new Thickness(i * w / 120, 0, 0, 0) });
                            cap.Text = $"{i + 1} / 120 · {(ms < 0 ? I18n.T("потеря") : ms + " мс")}";
                        })));
                        stab.Children.Remove(cap);
                        stab.Children.Add(Info(r.Verdict, r.Verdict.EndsWith("✓") ? "#30D158" : "#FF9F0A"));
                        stab.Children.Add(Table(new[]
                        {
                            ("Средняя задержка", r.Avg < 0 ? "—" : $"{r.Avg:0} мс"), ("Джиттер", $"{r.Jitter:0} мс"), ("Максимум", r.Max < 0 ? "—" : $"{r.Max:0} мс"),
                            ("Потеряно", $"{r.Lost} из {r.Ms.Count}"), ("Всплесков", r.Spikes.ToString()),
                        }));
                        return null;
                    }, null, true)),
                stab));

            // 3. DNS
            var dnsRes = new StackPanel();
            page.Children.Add(Group("Самый быстрый DNS",
                Item("Сравнить DNS-серверы", "DNS превращает адрес сайта в IP. Медленный DNS — это пауза перед открытием каждого сайта. Сравню 8 публичных серверов с твоим текущим.", Safety.Safe,
                    Act("Сравнить", async () =>
                    {
                        dnsRes.Children.Clear();
                        dnsRes.Children.Add(Loading("Опрашиваю DNS…"));
                        var list = await WifiTools2.DnsBenchmark();
                        dnsRes.Children.Clear();
                        var best = list.FirstOrDefault(x => x.Ms > 0);
                        dnsRes.Children.Add(Info(best.Name != null ? I18n.T("Быстрее всех: ") + best.Name + $" ({best.Ms:0} мс)" : I18n.T("Ни один DNS не ответил"), "#9FD0FF"));
                        foreach (var d in list)
                        {
                            var preset = Laptop.DnsPresets.FirstOrDefault(p => p.Primary == d.Ip);
                            dnsRes.Children.Add(UI.Grid("*,a,a").M(14, 3, 14, 3).Add(
                                UI.Text(d.Name + " · " + d.Ip, 12.5),
                                UI.Text(d.Ms < 0 ? I18n.T("не отвечает") : $"{d.Ms:0} мс", 12.5, true, d.Ms < 0 ? UI.Hex("#FF453A") : UI.Dim).At(0, 1).M(8, 0, 8, 0),
                                preset != null ? UI.Pill("Поставить", async () => { int code = await Laptop.SetDns(preset); App.Island.Toast("", "DNS", code == 0 ? I18n.T("Поставлен ") + preset.Name : I18n.T("Не получилось")); }).At(0, 2) : null));
                        }
                        return null;
                    }, null, true)),
                dnsRes));

            // 4. трассировка
            var trHost = Field("google.com", 220);
            var trRes = new StackPanel();
            page.Children.Add(Group("Трассировка маршрута",
                Item("Где тормозит", "Показывает каждый узел по пути до сайта и задержку до него. Если задержка резко растёт на втором узле — проблема у провайдера, на первом — у роутера.", Safety.Safe,
                    UI.Row(trHost, Act("Проследить", async () =>
                    {
                        trRes.Children.Clear();
                        var p = Loading("Ищу маршрут…");
                        trRes.Children.Add(p);
                        var hops = await WifiTools2.Trace(trHost.Text.Trim(), s => Dispatcher.BeginInvoke(new Action(() => p.Text = I18n.T(s))));
                        trRes.Children.Clear();
                        double prev = 0;
                        foreach (var h in hops)
                        {
                            bool jump = h.Ms > 0 && prev > 0 && h.Ms - prev > 40;
                            trRes.Children.Add(UI.Grid("a,*,a").M(14, 2, 14, 2).Add(
                                UI.Text(h.Hop + ".", 12, true, UI.Dim).M(0, 0, 8, 0),
                                new TextBlock { Text = h.Ip + (h.Host != "" && h.Host != h.Ip ? "  " + h.Host : ""), FontSize = 12, Tag = "no-translate", TextTrimming = TextTrimming.CharacterEllipsis }.At(0, 1),
                                UI.Text(h.Ms < 0 ? "*" : $"{h.Ms:0} мс" + (jump ? " ↑" : ""), 12, true, jump ? UI.Hex("#FF9F0A") : UI.Dim).At(0, 2)));
                            if (h.Ms > 0) prev = h.Ms;
                        }
                        return null;
                    }).M(6, 0, 0, 0))),
                trRes));

            // 5. ремонт
            var fixRes = new StackPanel();
            page.Children.Add(Group("Починить интернет",
                Item("Быстрый ремонт", "Сброс кэша DNS, новый адрес от роутера и переподключение Wi-Fi — помогает, когда «подключено, без доступа к интернету». Настройки не меняются.", Safety.Safe,
                    Act("Починить", async () =>
                    {
                        fixRes.Children.Clear();
                        var p = Loading("Чиню…");
                        fixRes.Children.Add(p);
                        var log = await WifiTools2.QuickRepair(s => Dispatcher.BeginInvoke(new Action(() => p.Text = I18n.T(s))));
                        fixRes.Children.Clear();
                        foreach (var l in log) fixRes.Children.Add(Info(l, l.StartsWith("✓") ? "#30D158" : l.StartsWith("✕") ? "#FF453A" : null));
                        return null;
                    }, null, true)),
                fixRes));

            // 6. адаптер
            var adRes = new StackPanel();
            page.Children.Add(Group("Сетевой адаптер",
                Item("Всё о подключении", "MAC и IP-адреса, роутер, DNS, кто выдаёт адрес, MTU, трафик и ошибки — то, что спрашивает техподдержка провайдера.", Safety.Safe,
                    Act("Показать", () => { adRes.Children.Clear(); adRes.Children.Add(Table(WifiTools2.AdapterInfo())); return Task.FromResult<string>(null); })),
                adRes));

            // 7. Wake-on-LAN
            var wolMac = Field(S.WolMac ?? "", 200, v => S.WolMac = v.Trim(), "00:1A:2B:3C:4D:5E");
            page.Children.Add(Group("Включить компьютер по сети",
                Item("Wake-on-LAN", "Будит другой компьютер в этой сети, если в его BIOS включена функция Wake-on-LAN. MAC-адрес можно взять в «Кто подключён к моей сети».", Safety.Safe,
                    UI.Row(wolMac, Act("Разбудить", async () => { S.WolMac = wolMac.Text.Trim(); return await WifiTools2.WakeOnLan(wolMac.Text) ?? I18n.T("Сигнал отправлен ✓"); }).M(6, 0, 0, 0)))));

            // 8. карта сигнала
            var room = Field("", 180, null, "где ты сейчас: кухня…");
            var map = new StackPanel();
            void RenderMap()
            {
                map.Children.Clear();
                var data = Store.Data.SignalMap ?? new();
                if (data.Count == 0) { map.Children.Add(Info("Пока пусто. Обойди квартиру с ноутбуком и замерь сигнал в каждой комнате — станет видно, где поставить роутер или усилитель.")); return; }
                foreach (var (name, rssi) in data.OrderByDescending(x => x.Value))
                {
                    var n = name;
                    int pct = WifiTools.Percent(rssi);
                    var bar = new Border { Height = 8, CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Left, Width = pct * 1.6,
                        Background = UI.Hex(rssi >= -60 ? "#30D158" : rssi >= -70 ? "#FFD60A" : "#FF453A") };
                    map.Children.Add(UI.Grid("120,*,a,a").M(14, 3, 14, 3).Add(
                        new TextBlock { Text = n, FontSize = 12.5, Tag = "no-translate" }, bar.At(0, 1).VA(VerticalAlignment.Center),
                        UI.Text($"{pct}% · {rssi} dBm · {I18n.T(WifiTools.SignalWord(rssi))}", 11.5, false, UI.Dim).At(0, 2).M(8, 0, 8, 0),
                        UI.IconBtn("", () => { Store.Data.SignalMap.Remove(n); Store.SaveData(); RenderMap(); }, 9, 22, "Убрать").At(0, 3)));
                }
            }
            RenderMap();
            page.Children.Add(Group("Карта сигнала по квартире",
                Item("Замерить здесь", "Встань с ноутбуком в комнате, впиши её название и нажми — сигнал усреднится за 3 секунды.", Safety.Safe,
                    UI.Row(room, Act("Замерить", async () => { var e = await WifiTools2.MeasureRoom(room.Text); RenderMap(); room.Text = ""; return e; }).M(6, 0, 0, 0))),
                map));

            // 9. безопасность и двойники
            var secRes = new StackPanel();
            page.Children.Add(Group("Безопасность сетей вокруг",
                Item("Проверить", "Ищет «злых двойников» (сеть с тем же именем, но другой защитой), открытые сети, устаревшую защиту, WPS и заводское имя роутера.", Safety.Safe,
                    Act("Проверить", async () =>
                    {
                        secRes.Children.Clear();
                        secRes.Children.Add(Loading("Сканирую эфир…"));
                        var aps = await WifiTools.Nearby();
                        secRes.Children.Clear();
                        foreach (var (lvl, title, advice) in WifiTools2.Security(aps))
                        {
                            var (glyph, color) = lvl switch { 2 => ("", "#FF453A"), 1 => ("", "#FF9F0A"), _ => ("", "#30D158") };
                            secRes.Children.Add(UI.Grid("a,*").M(14, 5, 14, 5).Add(UI.Icon(glyph, 14, UI.Hex(color)).VA(VerticalAlignment.Top).M(0, 2, 10, 0),
                                UI.Col(new TextBlock { Text = I18n.T(title), FontSize = 13, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap },
                                       advice == null ? null : new TextBlock { Text = I18n.T(advice), FontSize = 12, Foreground = UI.Dim, TextWrapping = TextWrapping.Wrap }).At(0, 1)));
                        }
                        return null;
                    }, null, true)),
                secRes));

            // 10. регионы
            var regRes = new StackPanel();
            page.Children.Add(Group("Задержка до регионов",
                Item("Пинг до серверов мира", "Сколько идёт сигнал до дата-центров в Европе, США и Азии — подскажет, какой регион выбрать в игре или облачном сервисе.", Safety.Safe,
                    Act("Измерить", async () =>
                    {
                        regRes.Children.Clear();
                        regRes.Children.Add(Loading("Измеряю…"));
                        var res = await Task.WhenAll(WifiTools2.Regions.Select(async r => (r.Name, Ms: await WifiTools2.TcpMs(r.Host))));
                        regRes.Children.Clear();
                        foreach (var (name, ms) in res.OrderBy(x => x.Ms < 0 ? 99999 : x.Ms))
                            regRes.Children.Add(UI.Grid("*,a").M(14, 3, 14, 3).Add(UI.Text(name, 12.5),
                                UI.Text(ms < 0 ? I18n.T("нет ответа") : $"{ms:0} мс", 12.5, true, ms < 0 ? UI.Hex("#FF453A") : ms < 60 ? UI.Hex("#30D158") : ms < 120 ? UI.Hex("#FFD60A") : UI.Hex("#FF9F0A")).At(0, 1)));
                        return null;
                    })),
                regRes));

            // 11. пароль
            var pw = UI.Text("", 15, true);
            pw.Tag = "no-translate";
            var pwInfo = UI.Text("", 11.5, false, UI.Dim);
            void NewPw(bool readable) { pw.Text = WifiTools2.StrongPassword(readable ? 19 : 16, readable); pwInfo.Text = I18n.T("Стойкость: ") + I18n.T(WifiTools2.Strength(pw.Text)); }
            NewPw(true);
            page.Children.Add(Group("Надёжный пароль для Wi-Fi",
                Item("Придумать пароль", "Без похожих символов (l/1, O/0) — его легко ввести на телефоне и телевизоре. Поставить его можно в настройках роутера.", Safety.Safe,
                    UI.Row(UI.Pill("Удобный", () => NewPw(true)), UI.Pill("Максимальный", () => NewPw(false)).M(6, 0, 0, 0),
                           UI.Pill("Копировать", () => { ClipboardService.I.CopyBack(pw.Text); App.Island.Toast("", "Пароль скопирован", null); }, true).M(6, 0, 0, 0))),
                UI.Col(pw, pwInfo).M(14, 4, 14, 10)));
        }
    }
}
