using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NIsland.Core;

namespace NIsland.Services
{
    // =====================================================================
    //  Telegram 2.0: живая панель, живой экран, обложка трека, сценарии, команды по расписанию,
    //  история буфера и треков, здоровье ПК, Защитник, перевод, калькулятор, QR, пробуждение по сети,
    //  скорость интернета, Wi-Fi/Bluetooth, голосовое с телефона звучит на ПК.
    // =====================================================================
    public partial class TelegramHub
    {
        // ------------------------------------------------------------------
        //  Красивые полоски и карточки
        // ------------------------------------------------------------------

        /// <summary>Полоска «▰▰▰▱▱▱» на 10 делений.</summary>
        static string Bar(double frac, int n = 10)
        {
            int k = (int)Math.Round(Math.Clamp(frac, 0, 1) * n);
            return new string('▰', k) + new string('▱', n - k);
        }

        /// <summary>Строка трека: 1:23 ━━━━●──── 3:45.</summary>
        static string TrackBar(TimeSpan pos, TimeSpan dur, int n = 14)
        {
            if (dur.TotalSeconds < 1) return "";
            int k = (int)Math.Round(Math.Clamp(pos.TotalSeconds / dur.TotalSeconds, 0, 1) * (n - 1));
            return $"{Fmt.Time(pos)} " + new string('━', k) + "●" + new string('─', n - 1 - k) + $" {Fmt.Time(dur)}";
        }

        /// <summary>Панель состояния ПК с полосками — для пульта и «живой панели».</summary>
        static string Dashboard(bool live = false)
        {
            var sys = SystemService.I;
            var m = MediaService.I;
            int cpu = sys.CpuPercent(), ram = sys.RamPercent();
            var sb = new StringBuilder();
            sb.Append($"🎛 <b>{H(Environment.MachineName)}</b> · {DateTime.Now:HH:mm}{(live ? ":" + DateTime.Now.ToString("ss") : "")}\n");
            sb.Append("<code>");
            sb.Append($"CPU {Bar(cpu / 100.0)} {cpu,3}%\n");
            sb.Append($"RAM {Bar(ram / 100.0)} {ram,3}%\n");
            if (sys.HasBattery) sb.Append($"BAT {Bar(sys.BatteryPercent / 100.0)} {sys.BatteryPercent,3}%{(sys.Charging ? " ⚡" : "")}\n");
            var a = AudioService.I;
            sb.Append($"VOL {Bar(a.Muted ? 0 : a.Volume)} {(a.Muted ? "off" : Math.Round(a.Volume * 100) + "%"),4}");
            sb.Append("</code>");
            if (m.HasSession)
            {
                sb.Append($"\n🎵 {(m.IsPlaying ? "▶" : "⏸")} {H(Cut(m.TrackLine, 60))}");
                if (live && m.HasTime) sb.Append($"\n<code>{TrackBar(m.Position, m.Duration)}</code>");
            }
            if (WeatherService.Cached is { } w && (DateTime.Now - w.At).TotalHours < 3) sb.Append($"\n🌦 {Math.Round(w.Temp):0}° · {H(w.City)}");
            if (Vpn.Active) sb.Append(" · 🔒 VPN");
            if (Store.Settings.Mode != "normal") sb.Append($"\n🌙 {H(T(Modes.Current.Name))}");
            return sb.ToString();
        }

        // ------------------------------------------------------------------
        //  Команды 2.0 (вызываются из Command, если там команды нет)
        // ------------------------------------------------------------------

        static readonly (string Cmd, string Desc)[] Commands2 =
        {
            ("now", "Что играет — с обложкой"), ("live", "Живая панель ПК"), ("watch", "Живой экран"), ("scene", "Сценарии: работа, кино, игры"),
            ("in", "Через время: /in 10m /lock"), ("at", "В момент: /at 18:30 /shutdown"), ("jobs", "Запланированные команды"),
            ("cliphist", "История буфера обмена"), ("history", "Что играло"), ("health", "Здоровье ПК"), ("av", "Защитник: проверка"),
            ("tr", "Перевести: /tr en текст"), ("calc", "Посчитать: /calc 2*21"), ("qr", "QR-код: /qr текст"), ("wake", "Разбудить ПК по сети"),
            ("speed", "Скорость интернета"), ("radio", "Wi-Fi и Bluetooth"), ("startup", "Автозагрузка"), ("pin", "Закрепить пульт в чате"),
            ("seek", "Перемотать: /seek 1:30"), ("shuffle", "Перемешать"), ("repeat", "Повтор"),
            ("do", "Умная команда: /do громкость 30 и выключи экран"),
        };

        // что ИИ может выполнить сам по просьбе из /do: только безопасное и обратимое (консоль, агенты, закрытие программ,
        // выключение, файлы и расписание — никогда)
        static readonly Dictionary<string, string> SmartAllowed = new()
        {
            ["vol"] = "громкость 0–100: /vol 30", ["mute"] = "выключить/включить звук", ["mic"] = "микрофон вкл/выкл", ["play"] = "продолжить музыку", ["pause"] = "пауза",
            ["next"] = "следующий трек", ["prev"] = "предыдущий трек", ["seek"] = "перемотка: /seek 1:30", ["shuffle"] = "перемешать", ["repeat"] = "повтор",
            ["brightness"] = "яркость 0–100: /brightness 60", ["monitor"] = "экран: /monitor off или /monitor on", ["theme"] = "переключить светлую/тёмную тему",
            ["dnd"] = "«Не беспокоить» вкл/выкл", ["mode"] = "режим острова: normal, dnd, focus, minimal, night, game, present", ["awake"] = "не засыпать: /awake on или off",
            ["scene"] = "сценарий: work, movie, game, saver, present, normal", ["eq"] = "эквалайзер: пресет или off", ["timer"] = "таймер в минутах: /timer 10",
            ["remind"] = "напоминание: /remind 30 текст или /remind 18:30 текст", ["note"] = "заметка: /note текст", ["todo"] = "задача: /todo текст",
            ["say"] = "показать текст в острове: /say текст", ["speak"] = "сказать вслух на ПК: /speak текст", ["screenshot"] = "снимок экрана", ["lock"] = "заблокировать ПК",
            ["now"] = "что играет с обложкой", ["status"] = "состояние ПК", ["weather"] = "погода", ["battery"] = "батарея", ["top"] = "что грузит ПК",
            ["url"] = "открыть сайт: /url youtube.com", ["tr"] = "перевод: /tr en текст", ["calc"] = "посчитать: /calc 2*21", ["live"] = "живая панель",
        };

        async Task<(string, object)?> Smart(string arg)
        {
            if (arg == "") { Await("do"); return ("🤖 Напиши обычными словами, что сделать на ПК: «громкость 30 и выключи экран», «через 10 минут напомни про чай», «включи режим кино»", null); }
            await Action("typing");
            var prompt = new StringBuilder();
            prompt.AppendLine("Ты переводишь просьбу пользователя в команды Telegram-бота, который управляет его ПК (N-Island).");
            prompt.AppendLine("Доступные команды (и только они):");
            foreach (var (k, d) in SmartAllowed) prompt.AppendLine($"/{k} — {d}");
            prompt.AppendLine("Ответь ТОЛЬКО командами, по одной в строке, максимум 5, без пояснений и без кавычек. Аргументы пиши на языке пользователя.");
            prompt.AppendLine("Если просьбу нельзя выполнить этими командами — ответь одним словом NONE.");
            prompt.AppendLine("Просьба: " + arg);
            string ans;
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
                ans = await Ai.Ask(new[] { new AiMessage("user", prompt.ToString()) }, null, cts.Token, channel: "telegram-smart");
            }
            catch (Exception e) { return ("🤖 ИИ не ответил: " + H(Cut(e.Message, 150)), null); }
            var cmds = Regex.Matches(Ai.StripThink(ans ?? ""), @"^\s*/([a-z]+)(?:@\w+)?[ \t]*(.*)$", RegexOptions.Multiline)
                .Select(x => (Cmd: x.Groups[1].Value.ToLowerInvariant(), Arg: x.Groups[2].Value.Trim())).Take(5).ToList();
            if (cmds.Count == 0) return ("🤖 Не понял, что сделать. Попробуй иначе или посмотри /help", null);
            var sb = new StringBuilder($"🤖 <b>Понял так</b> <i>({H(Ai.LastAnswered ?? Ai.Current.Name)})</i>\n");
            foreach (var (c, a) in cmds)
            {
                if (!SmartAllowed.ContainsKey(c)) { sb.Append($"\n⛔ <code>/{H(c)} {H(Cut(a, 40))}</code> — не выполняю (опасно или неизвестно)"); continue; }
                string res;
                try { var r = await Command(c, a); res = r?.Html == null ? "✓" : System.Net.WebUtility.HtmlDecode(Regex.Replace(r.Value.Html, "<[^>]+>", "")); }
                catch (Exception e) { res = "⚠️ " + e.Message; }
                sb.Append($"\n▸ <code>/{H(c)} {H(Cut(a, 40))}</code>\n   {H(Cut(res.Replace('\n', ' '), 160))}");
            }
            return (sb.ToString(), null);
        }

        async Task<(string Html, object Keys)?> Command2(string cmd, string arg)
        {
            var m = MediaService.I;
            string no;
            switch (cmd)
            {
                case "do": case "smart":
                    return await Smart(arg);
                case "now": case "cover":
                    await NowCard();
                    return null;
                case "live": case "dash":
                {
                    int min = int.TryParse(arg, out var lm) ? Math.Clamp(lm, 1, 15) : 3;
                    await StartLive(min);
                    return null;
                }
                case "watch": case "stream":
                {
                    if ((no = Need("screen")) != null) return (no, null);
                    int sec = int.TryParse(arg.TrimEnd('s', 'с'), out var ws) ? Math.Clamp(ws, 10, 180) : 60;
                    await StartWatch(sec);
                    return null;
                }
                case "scene": case "scenario":
                    if (arg == "") return await Menu2("scenes");
                    if ((no = Need("control")) != null) return (no, null);
                    var sc = Laptop.Scenarios.FirstOrDefault(s => s.Key == arg.ToLowerInvariant() || s.Title.Equals(arg, StringComparison.OrdinalIgnoreCase));
                    if (sc.Key == null) return ("Нет такого сценария. Есть: " + string.Join(", ", Laptop.Scenarios.Select(s => s.Key)), null);
                    await Ui(() => Laptop.ApplyScenario(sc.Key));
                    return ($"🎬 Сценарий «{H(T(sc.Title))}» включён\n<i>{H(T(sc.Explain))}</i>", null);
                case "in": case "at":
                    return (Schedule(cmd == "at", arg), K(Row(B("🗓 Все запланированные", "nav:jobs"))));
                case "jobs": return await Menu2("jobs");
                case "cliphist": case "clips":
                    if ((no = Need("control")) != null) return (no, null);
                    return await Menu2("clips");
                case "history": case "played": return await Menu2("history");
                case "health":
                    await Message("🩺 Проверяю здоровье ПК… (до минуты)");
                    await Action("typing");
                    return (await HealthText(), K(Row(B("🔄 Ещё раз", "do2:health"), B("🛡 Защитник", "nav:av"))));
                case "av": case "defender": return await Menu2("av");
                case "tr": case "translate":
                {
                    if (arg == "") return ("Напиши так: /tr en Привет — или /tr текст (на язык интерфейса)", null);
                    var mm = Regex.Match(arg, @"^([a-z]{2}(?:-[A-Z]{2})?)\s+(.+)$", RegexOptions.Singleline);
                    string to = mm.Success ? mm.Groups[1].Value : (I18n.Current is { Length: 2 } l && l != "ru" ? l : "ru"), text = mm.Success ? mm.Groups[2].Value : arg;
                    // та же цепочка, что у текста песен: Google → второй адрес Google → MyMemory → ИИ острова (Google часто отвечает 429)
                    await Action("typing");
                    try
                    {
                        var lines = text.Replace("\r", "").Split('\n');
                        var (res, via) = await LyricsTranslate.Run("tr:" + text.GetHashCode(), lines, to);
                        var joined = string.Join("\n", res);
                        return ($"🌐 <b>{H(LyricsTranslate.Guess(lines) ?? "?")} → {H(to)}</b> <i>· {H(via)}</i>\n{H(joined)}", K(Row(Copy("📱 Копировать", joined))));
                    }
                    catch (LyricsTranslate.SameLanguageException) { return ($"🌐 Текст уже на этом языке ({H(to)}). Укажи другой: /tr en текст", null); }
                    catch (Exception e) { return ("🌐 " + H(Cut(e.Message, 200)), null); }
                }
                case "calc": case "c":
                {
                    if (arg == "") return ("Напиши так: /calc (12+8)*3", null);
                    var r = Calc.TryEval(arg);
                    return (r == null ? "🧮 Не понял выражение" : $"🧮 <code>{H(arg)}</code> = <b>{H(r)}</b>", r == null ? null : K(Row(Copy("📱 Копировать", r))));
                }
                case "qr":
                {
                    if (arg == "") return ("Напиши так: /qr https://… или любой текст", null);
                    await Action("upload_photo");
                    try
                    {
                        var png = await Big.GetByteArrayAsync("https://api.qrserver.com/v1/create-qr-code/?size=480x480&margin=12&data=" + Uri.EscapeDataString(Cut(arg, 900)));
                        await Upload("sendPhoto", "photo", Bytes(png, "image/png"), "qr.png", "🔳 " + H(Cut(arg, 200)));
                        return null;
                    }
                    catch (Exception e) { return ("🔳 Не удалось сделать QR: " + H(Cut(e.Message, 120)), null); }
                }
                case "wake": case "wol":
                    if ((no = Need("control")) != null) return (no, null);
                    var mac = arg != "" ? arg : Store.Settings.WolMac;
                    if (string.IsNullOrWhiteSpace(mac)) return ("Напиши так: /wake 00:1A:2B:3C:4D:5E (MAC-адрес можно запомнить на ПК: Wi-Fi → Включить компьютер по сети)", null);
                    var werr = await WifiTools2.WakeOnLan(mac);
                    return (werr ?? $"⏰ Сигнал пробуждения отправлен на {H(mac)}", null);
                case "speed": case "speedtest":
                    await Message("🚀 Меряю скорость интернета… (до 30 с)");
                    await Action("typing");
                    try { return ("🚀 " + H(await ApiTools.SpeedTest()), K(Row(B("🔄 Ещё раз", "do2:speed")))); }
                    catch (Exception e) { return ("🚀 Не удалось: " + H(Cut(e.Message, 150)), null); }
                case "radio": case "bt": case "bluetooth": case "airplane":
                    return await Menu2("radio");
                case "startup": case "autorun":
                {
                    var list = await Task.Run(Maint.Startup);
                    var sb = new StringBuilder($"🚀 <b>Автозагрузка</b> ({list.Count(x => x.Enabled)} из {list.Count} включено)\n");
                    foreach (var s in list.OrderByDescending(x => x.Enabled).Take(30))
                        sb.Append($"\n{(s.Enabled ? "🟢" : "⚪")} {H(Cut(s.Name, 50))} <i>· {H(s.Source)}</i>");
                    sb.Append("\n\nВключать и выключать — на ПК: Центр управления → Программы.");
                    return (Fold(sb.ToString()), null);
                }
                case "pin":
                {
                    var (h, k) = await Menu("main");
                    var id = await Message(h, k);
                    try { await Call("pinChatMessage", new { chat_id = V("chat"), message_id = id, disable_notification = true }); return ("📌 Пульт закреплён вверху чата", null); }
                    catch (Exception e) { return ("📌 Не удалось закрепить: " + H(Cut(e.Message, 120)), null); }
                }
                case "seek":
                {
                    if ((no = Need("control")) != null) return (no, null);
                    if (!m.HasSession) return ("🎵 Сейчас ничего не играет", null);
                    TimeSpan to;
                    var pm = Regex.Match(arg, @"^(\d{1,3})\s*%$");
                    if (pm.Success && m.HasTime) to = TimeSpan.FromSeconds(m.Duration.TotalSeconds * int.Parse(pm.Groups[1].Value) / 100.0);
                    else if (TimeSpan.TryParseExact(arg, new[] { @"m\:ss", @"mm\:ss", @"h\:mm\:ss" }, CultureInfo.InvariantCulture, out var ts)) to = ts;
                    else if (int.TryParse(arg, out var secs)) to = TimeSpan.FromSeconds(secs);
                    else return ("Напиши так: /seek 1:30 · /seek 50% · /seek 90", null);
                    await Ui(() => m.Seek(to));
                    return ($"⏩ {Fmt.Time(to)}", null);
                }
                case "shuffle":
                    if ((no = Need("control")) != null) return (no, null);
                    if (!m.CanShuffle) return ("🔀 Этот плеер не умеет перемешивать", null);
                    await Ui(m.ToggleShuffle);
                    return ("🔀 Перемешивание переключено", null);
                case "repeat":
                    if ((no = Need("control")) != null) return (no, null);
                    if (!m.CanRepeat) return ("🔁 Этот плеер не умеет повторять", null);
                    await Ui(m.CycleRepeat);
                    return ("🔁 Режим повтора переключён", null);
            }
            return null;
        }

        // ------------------------------------------------------------------
        //  Меню 2.0
        // ------------------------------------------------------------------

        async Task<(string, object)?> Menu2(string name)
        {
            switch (name)
            {
                case "more":
                    return ("✨ <b>Ещё возможности</b>", K(
                        Row(B("📡 Живая панель", "do2:live"), B("🎥 Живой экран", "do2:watch")),
                        Row(B("🎬 Сценарии", "nav:scenes"), B("🗓 По расписанию", "nav:jobs")),
                        Row(B("📋 История буфера", "nav:clips"), B("🎧 Что играло", "nav:history")),
                        Row(B("🩺 Здоровье ПК", "do2:health"), B("🛡 Защитник", "nav:av")),
                        Row(B("🚀 Скорость сети", "do2:speed"), B("📶 Wi-Fi и Bluetooth", "nav:radio")),
                        Row(B("🚀 Автозагрузка", "do2:startup"), B("📌 Закрепить пульт", "do2:pin")),
                        Back()));

                case "scenes":
                {
                    var rows = Laptop.Scenarios.Select(s => Row(B(T(s.Title), "sc:" + s.Key))).ToList();
                    rows.Add(Back("more"));
                    return ("🎬 <b>Сценарии</b> — одним нажатием меняют питание, яркость, режим острова и уведомления\n\n" +
                            string.Join("\n", Laptop.Scenarios.Select(s => $"<b>{H(T(s.Title))}</b> — {H(T(s.Explain))}")), K(Grid(rows.SelectMany(r => r), 2).ToArray()));
                }

                case "jobs":
                {
                    List<Job> jobs;
                    lock (Jobs) jobs = Jobs.OrderBy(j => j.At).ToList();
                    var sb = new StringBuilder("🗓 <b>Команды по расписанию</b>\n");
                    if (jobs.Count == 0) sb.Append("Пусто. Примеры:\n/in 10m /lock — заблокировать через 10 минут\n/in 1h /music — показать музыку через час\n/at 23:30 /shutdown 1 — выключить в 23:31\n/at 7:00 /vol 60");
                    foreach (var j in jobs) sb.Append($"\n⏰ {j.At:HH:mm}{(j.At.Date != DateTime.Today ? " (" + j.At.ToString("d MMM", Fmt.Ru) + ")" : "")} — <code>/{H(j.Cmd)} {H(Cut(j.Arg, 40))}</code>");
                    sb.Append("\n\nЖивут, пока работает N-Island.");
                    var rows = jobs.Take(8).Select(j => Row(B($"✖ {j.At:HH:mm} /{Cut(j.Cmd, 14)}", "jb:" + j.Id))).ToList();
                    rows.Add(Row(B("🔄", "nav:jobs"), B("⬅️ Назад", "nav:more")));
                    return (sb.ToString(), K(rows.ToArray()));
                }

                case "clips":
                {
                    var items = await Ui(() => ClipboardService.I.Items.Take(10).Select(c => (c.Text, c.Time)).ToList());
                    if (items.Count == 0) return ("📋 История буфера пуста (включается на ПК: Настройки → Буфер обмена)", K(Back("more")));
                    var sb = new StringBuilder("📋 <b>История буфера обмена ПК</b>\n");
                    var rows = new List<object[]>();
                    for (int i = 0; i < items.Count; i++)
                    {
                        sb.Append($"\n<b>{i + 1}.</b> {H(Cut(items[i].Text?.Replace('\n', ' '), 80))} <i>· {items[i].Time:HH:mm}</i>");
                        rows.Add(Row(Copy($"📱 {i + 1}", items[i].Text), B($"💻 {i + 1} в буфер ПК", "ch:" + Put(items[i].Text ?? ""))));
                    }
                    rows.Add(Row(B("🔄", "nav:clips"), B("⬅️ Назад", "nav:more")));
                    return (sb.ToString(), K(rows.ToArray()));
                }

                case "history":
                {
                    var list = (Store.Data.Played ?? new()).Take(12).ToList();
                    if (list.Count == 0) return ("🎧 История пуста", K(Back("more")));
                    var sb = new StringBuilder("🎧 <b>Что играло</b>\n");
                    foreach (var p in list) sb.Append($"\n• {H(Cut(string.IsNullOrEmpty(p.Artist) ? p.Title : p.Artist + " — " + p.Title, 70))}");
                    var rows = list.Take(6).Select(p => Row(Url("▶ " + Cut(p.Title, 28), "https://www.youtube.com/results?search_query=" + Uri.EscapeDataString((p.Artist + " " + p.Title).Trim())))).ToList();
                    rows.Add(Back("more"));
                    return (sb.ToString(), K(rows.ToArray()));
                }

                case "av":
                {
                    var s = Store.Settings;
                    var last = DateTime.TryParse(s.AvLastScan, null, DateTimeStyles.RoundtripKind, out var ls) ? ls.ToString("d MMM, HH:mm", Fmt.Ru) : T("ещё не было");
                    var text = $"🛡 <b>Защитник N-Island</b>\nЗащита в реальном времени: {(s.AvRealtime ? "🟢 включена" : "⚪ выключена")}\n" +
                               $"База угроз: {Antivirus.DbCount:N0} записей\nПоследняя проверка: {last}" + (s.AvLastCount > 0 ? $" · файлов {s.AvLastCount}" : "");
                    int qn = Antivirus.Quarantined().Count;
                    text += $"\n{T("В карантине")}: {qn}";
                    return (text, K(Row(B("🔎 Быстрая проверка", "av2:scan"), B("🧾 Проверка Windows", "av2:audit")),
                        Row(B(s.AvRealtime ? "⏸ Выключить защиту" : "▶ Включить защиту", "av2:rt"), B("🔄 Обновить базу", "av2:db")),
                        Row(B($"🗂 Карантин ({qn})", "av2:ql"), B("🛡 Защитник Windows", "av2:wd")), Back("more")));
                }

                case "radio":
                {
                    var radios = await RadioList();
                    if (radios.Count == 0) return ("📶 Windows не дала доступ к радиомодулям (Wi-Fi, Bluetooth)", K(Back("more")));
                    var text = "📶 <b>Wi-Fi и Bluetooth</b>\n" + string.Join("\n", radios.Select(r => $"{(r.On ? "🟢" : "⚪")} {H(r.Name)} ({H(r.Kind)})")) +
                               "\n\n⚠️ Если выключить Wi-Fi, а бот работает через него — связь пропадёт до включения на ПК.";
                    var rows = radios.Select(r => Row(B((r.On ? "⏻ Выключить " : "⏻ Включить ") + Cut(r.Name, 20), "rd:" + Put(r.Id)))).ToList();
                    rows.Add(Row(B("🔄", "nav:radio"), B("⬅️ Назад", "nav:more")));
                    return (text, K(rows.ToArray()));
                }
            }
            return null;
        }

        // ------------------------------------------------------------------
        //  Кнопки 2.0 (вызываются из Do, если там кнопки нет)
        // ------------------------------------------------------------------

        async Task<string> Do2(string head, string rest, long msgId)
        {
            string no;
            switch (head)
            {
                case "do2":
                    if (rest is "shuffle" or "repeat")
                    {
                        // быстрые переключатели — ответ всплывашкой, без нового сообщения
                        var q = await Command(rest, "");
                        return q?.Html == null ? "" : System.Net.WebUtility.HtmlDecode(Regex.Replace(q.Value.Html, "<[^>]+>", ""));
                    }
                    _ = Task.Run(async () => { try { await Reply(await Command(rest, "")); } catch (Exception e) { Log.Info("tg do2: " + e.Message); } });
                    return rest switch { "health" => "🩺 Проверяю…", "speed" => "🚀 Меряю…", _ => "" };
                case "sc":
                {
                    if ((no = Need("control")) != null) return no;
                    var sc = Laptop.Scenarios.FirstOrDefault(s => s.Key == rest);
                    if (sc.Key == null) return Stale;
                    await Ui(() => Laptop.ApplyScenario(sc.Key));
                    Toast("Сценарий из Telegram", T(sc.Title));
                    return "🎬 " + T(sc.Title);
                }
                case "jb":
                {
                    Job j;
                    lock (Jobs) { j = Jobs.FirstOrDefault(x => x.Id == rest); if (j != null) Jobs.Remove(j); }
                    j?.Cts.Cancel();
                    var (h, k) = (await Menu2("jobs")).Value;
                    await Edit(msgId, h, k);
                    return j == null ? "Уже выполнено" : "✖ Отменено";
                }
                case "ch":
                {
                    if ((no = Need("control")) != null) return no;
                    var text = Get(rest);
                    if (text == null) return Stale;
                    await Ui(() => ClipboardService.I.CopyBack(text));
                    return "💻 В буфере обмена ПК";
                }
                case "av2":
                    if (rest is "rt" or "db" or "ql" or "wd" || rest.StartsWith("q:") || rest.StartsWith("ok:") || rest.StartsWith("qr:")) return await Av2(rest, msgId) ?? Stale;
                    if (rest != "scan" && rest != "audit") return Stale;
                    if (rest == "audit")
                    {
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                var list = await Antivirus.Audit();
                                await Message("🧾 <b>Безопасность Windows</b>\n" + string.Join("\n", list.Select(x => (x.Level == 2 ? "🔴 " : x.Level == 1 ? "🟡 " : "🟢 ") + H(x.Title) + (x.Level > 0 && x.Advice != "" ? $"\n   <i>{H(x.Advice)}</i>" : ""))));
                            }
                            catch (Exception e) { await Message("⚠️ " + H(e.Message)); }
                        });
                        return "🧾 Проверяю…";
                    }
                    if (_avScanning) return "Проверка уже идёт";
                    _avScanning = true;
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await Message("🔎 Быстрая проверка: автозагрузка, запущенные программы, «Загрузки», рабочий стол… (1–3 мин)");
                            var found = await Antivirus.QuickScan(null, default);
                            await ReportScan(found, "Быстрая проверка");
                        }
                        catch (Exception e) { await Message("⚠️ " + H(e.Message)); }
                        finally { _avScanning = false; }
                    });
                    return "🔎 Проверка запущена";
                case "rd":
                {
                    if ((no = Need("control")) != null) return no;
                    var id = Get(rest);
                    if (id == null) return Stale;
                    var r = await RadioToggle(id);
                    var (h, k) = (await Menu2("radio")).Value;
                    await Edit(msgId, h, k);
                    return r;
                }
                case "lv":
                    lock (Lives) if (Lives.TryGetValue(rest, out var c)) c.Cancel();
                    return "⏹ Остановлено";
            }
            return null;
        }

        static volatile bool _avScanning;

        async Task<string> HealthText()
        {
            var (score, issues) = await PcHealth.Score(_ => { });
            var face = score >= 85 ? "💚" : score >= 65 ? "💛" : "❤️";
            var sb = new StringBuilder($"🩺 <b>Здоровье ПК</b>: {face} <b>{score}</b>/100\n<code>{Bar(score / 100.0, 20)}</code>\n");
            foreach (var i in issues.OrderByDescending(x => x.Penalty).Take(8))
                sb.Append($"\n{(i.Penalty >= 10 ? "🔴" : "🟡")} {H(i.Title)}" + (string.IsNullOrEmpty(i.Advice) ? "" : $"\n   <i>{H(Cut(i.Advice, 140))}</i>"));
            if (issues.Count == 0) sb.Append("\nВсё в порядке ✓");
            return sb.ToString();
        }

        // ------------------------------------------------------------------
        //  Что играет — карточка с обложкой
        // ------------------------------------------------------------------

        async Task NowCard()
        {
            var m = MediaService.I;
            if (!m.HasSession) { await Message("🎵 Сейчас ничего не играет", MusicKeys2()); return; }
            var caption = new StringBuilder($"🎵 <b>{H(m.Title)}</b>\n{H(m.Artist)}" + (string.IsNullOrEmpty(m.Album) ? "" : $" · <i>{H(m.Album)}</i>"));
            caption.Append($"\n{(m.IsPlaying ? "▶ играет" : "⏸ пауза")} · {H(m.AppName)}");
            if (m.HasTime) caption.Append($"\n<code>{TrackBar(m.Position, m.Duration)}</code>");
            if (m.CurrentLyric is { } line) caption.Append($"\n🎤 <i>{H(line)}</i>");
            // обложка: кодируем в PNG в потоке интерфейса (картинка из Windows живёт там)
            var png = await Ui(() =>
            {
                if (m.Art == null) return null;
                try
                {
                    var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(m.Art));
                    using var ms = new MemoryStream();
                    enc.Save(ms);
                    return ms.ToArray();
                }
                catch { return null; }
            });
            if (png == null) { await Message(caption.ToString(), MusicKeys2(), translate: false); return; }
            await Action("upload_photo");
            await Upload("sendPhoto", "photo", Bytes(png, "image/png"), "cover.png", caption.ToString(), MusicKeys2());
        }

        static object MusicKeys2() => K(
            Row(B("⏮", "mu:prev"), B("⏯", "mu:play"), B("⏭", "mu:next")),
            Row(B("⏪ 10 с", "mu:back"), B("💖", "mu:fav"), B("⏩ 10 с", "mu:fwd")),
            Row(B("🔉 −10", "au:vol-"), B("🔇", "au:mute"), B("🔊 +10", "au:vol+")),
            Row(B("📝 Текст", "do:lyrics"), B("🎛 Пульт музыки", "nav:music")));

        // ------------------------------------------------------------------
        //  Живая панель и живой экран: одно сообщение обновляется само
        // ------------------------------------------------------------------

        static readonly Dictionary<string, CancellationTokenSource> Lives = new();

        CancellationToken NewLive(string key)
        {
            var cts = new CancellationTokenSource();
            lock (Lives)
            {
                if (Lives.TryGetValue(key, out var old)) old.Cancel();   // одна живая панель и один живой экран за раз
                Lives[key] = cts;
            }
            return cts.Token;
        }

        async Task StartLive(int minutes)
        {
            var ct = NewLive("dash");
            var stop = K(Row(B("⏹ Остановить", "lv:dash"), B("🎛 Пульт", "nav:main")));
            long id = await Message(await Task.Run(() => Dashboard(true)) + $"\n\n📡 <i>Живая панель · обновляется {minutes} мин</i>", stop, silent: true);
            if (id <= 0 || TestSink != null) return;
            _ = Task.Run(async () =>
            {
                var until = DateTime.Now.AddMinutes(minutes);
                try
                {
                    while (DateTime.Now < until && !ct.IsCancellationRequested)
                    {
                        await Task.Delay(4000, ct);
                        var left = until - DateTime.Now;
                        await Edit(id, Dashboard(true) + $"\n\n📡 <i>Живая панель · ещё {Math.Max(0, (int)left.TotalMinutes)}:{Math.Max(0, left.Seconds):00}</i>", stop);
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception e) { Log.Info("tg live: " + e.Message); }
                try { await Edit(id, Dashboard() + "\n\n⏹ <i>Живая панель остановлена</i>", K(Row(B("📡 Ещё раз", "do2:live"), B("🎛 Пульт", "nav:main")))); } catch { }
            });
        }

        async Task StartWatch(int seconds)
        {
            var ct = NewLive("watch");
            await Action("upload_photo");
            var stop = K(Row(B("⏹ Остановить", "lv:watch"), B("📸 Снимок", "sh:0")));
            var first = await Task.Run(() => PcControl.Screenshot(0, false));
            long id = await Upload("sendPhoto", "photo", Bytes(first, "image/jpeg"), "live.jpg", $"🎥 Живой экран · {seconds} с", stop);
            Toast("Живой экран в Telegram", $"{seconds} с · снимки каждые 3 с");
            if (id <= 0 || TestSink != null) return;
            _ = Task.Run(async () =>
            {
                var until = DateTime.Now.AddSeconds(seconds);
                try
                {
                    while (DateTime.Now < until && !ct.IsCancellationRequested)
                    {
                        await Task.Delay(3000, ct);
                        var jpg = PcControl.Screenshot(0, false);
                        var left = (int)Math.Max(0, (until - DateTime.Now).TotalSeconds);
                        await EditPhoto(id, jpg, $"🎥 Живой экран · {DateTime.Now:HH:mm:ss} · ещё {left} с", stop);
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception e) { Log.Info("tg watch: " + e.Message); }
                try { await Call("editMessageCaption", new { chat_id = V("chat"), message_id = id, caption = T("⏹ Живой экран остановлен") + " · " + DateTime.Now.ToString("HH:mm:ss"), reply_markup = K(Row(B("🎥 Ещё раз", "do2:watch"))) }); } catch { }
            });
        }

        /// <summary>Заменить картинку в уже отправленном сообщении (editMessageMedia с файлом).</summary>
        async Task EditPhoto(long msgId, byte[] jpg, string caption, object keys)
        {
            using var form = new MultipartFormDataContent
            {
                { new StringContent(V("chat")), "chat_id" },
                { new StringContent(msgId.ToString()), "message_id" },
                { new StringContent(JsonSerializer.Serialize(new { type = "photo", media = "attach://shot", caption = Tr(caption) }, JsonOpts)), "media" },
                { new StringContent(JsonSerializer.Serialize(keys, JsonOpts)), "reply_markup" },
            };
            form.Add(Bytes(jpg, "image/jpeg"), "shot", "shot.jpg");
            using var resp = await Big.PostAsync(Api("editMessageMedia"), form);
            if (!resp.IsSuccessStatusCode) throw new TgError(Integrations.Snip(await resp.Content.ReadAsStringAsync()), (int)resp.StatusCode);
        }

        // ------------------------------------------------------------------
        //  Команды по расписанию: /in 10m /lock · /at 18:30 /shutdown
        // ------------------------------------------------------------------

        sealed class Job { public string Id, Cmd, Arg; public DateTime At; public CancellationTokenSource Cts; }
        static readonly List<Job> Jobs = new();
        static int _jobN;

        string Schedule(bool at, string arg)
        {
            var mm = at ? Regex.Match(arg, @"^(\d{1,2})[:.](\d{2})\s+(.+)$", RegexOptions.Singleline)
                        : Regex.Match(arg, @"^(\d+(?:[.,]\d+)?)\s*(с|s|сек|м|m|мин|min|ч|h|час)?\s+(.+)$", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (!mm.Success) return at ? "Напиши так: /at 18:30 /lock" : "Напиши так: /in 10m /lock · /in 2h /shutdown 1 · /in 30s /screenshot";
            DateTime when;
            if (at)
            {
                int h = int.Parse(mm.Groups[1].Value), mi = int.Parse(mm.Groups[2].Value);
                if (h > 23 || mi > 59) return "Нет такого времени";
                when = DateTime.Today.AddHours(h).AddMinutes(mi);
                if (when <= DateTime.Now) when = when.AddDays(1);
            }
            else
            {
                double n = double.Parse(mm.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
                var u = mm.Groups[2].Value.ToLowerInvariant();
                var span = u is "с" or "s" or "сек" ? TimeSpan.FromSeconds(n) : u is "ч" or "h" or "час" ? TimeSpan.FromHours(n) : TimeSpan.FromMinutes(n);
                if (span.TotalSeconds < 5 || span.TotalDays > 7) return "От 5 секунд до недели";
                when = DateTime.Now + span;
            }
            var line = mm.Groups[3].Value.Trim().TrimStart('/');
            var sp = line.IndexOfAny(new[] { ' ', '\n' });
            var cmd = (sp > 0 ? line[..sp] : line).ToLowerInvariant();
            var carg = sp > 0 ? line[(sp + 1)..].Trim() : "";
            if (cmd is "in" or "at" or "jobs") return "Расписание внутри расписания не поддерживается";
            Job job;
            lock (Jobs)
            {
                if (Jobs.Count >= 20) return "Запланировано уже 20 команд — отмени лишние: /jobs";
                job = new Job { Id = (++_jobN).ToString(), Cmd = cmd, Arg = carg, At = when, Cts = new CancellationTokenSource() };
                Jobs.Add(job);
            }
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(when - DateTime.Now, job.Cts.Token);
                    lock (Jobs) Jobs.Remove(job);
                    Toast("Команда из Telegram по расписанию", "/" + job.Cmd + " " + Cut(job.Arg, 40));
                    var r = await Command(job.Cmd, job.Arg);
                    await Message($"⏰ <i>По расписанию:</i> <code>/{H(job.Cmd)} {H(Cut(job.Arg, 60))}</code>\n" + (r?.Html ?? "✓"), r?.Keys);
                }
                catch (OperationCanceledException) { }
                catch (Exception e) { try { await Message("⚠️ По расписанию: " + H(e.Message)); } catch { } }
            });
            return $"🗓 <code>/{H(cmd)} {H(Cut(carg, 60))}</code> выполнится в {when:HH:mm}" + (when.Date != DateTime.Today ? " " + when.ToString("d MMM", Fmt.Ru) : "") +
                   $" (через {Fmt.Time(when - DateTime.Now)})";
        }

        // ------------------------------------------------------------------
        //  Wi-Fi и Bluetooth (Windows.Devices.Radios)
        // ------------------------------------------------------------------

        static async Task<List<(string Id, string Name, string Kind, bool On)>> RadioList()
        {
            var res = new List<(string, string, string, bool)>();
            try
            {
                var access = await global::Windows.Devices.Radios.Radio.RequestAccessAsync();
                if (access != global::Windows.Devices.Radios.RadioAccessStatus.Allowed) return res;
                foreach (var r in await global::Windows.Devices.Radios.Radio.GetRadiosAsync())
                    res.Add((r.Name + "|" + r.Kind, r.Name, r.Kind.ToString(), r.State == global::Windows.Devices.Radios.RadioState.On));
            }
            catch (Exception e) { Log.Info("radios: " + e.Message); }
            return res;
        }

        static async Task<string> RadioToggle(string id)
        {
            try
            {
                foreach (var r in await global::Windows.Devices.Radios.Radio.GetRadiosAsync())
                {
                    if (r.Name + "|" + r.Kind != id) continue;
                    bool on = r.State != global::Windows.Devices.Radios.RadioState.On;
                    var st = await r.SetStateAsync(on ? global::Windows.Devices.Radios.RadioState.On : global::Windows.Devices.Radios.RadioState.Off);
                    if (st != global::Windows.Devices.Radios.RadioAccessStatus.Allowed) return "Windows не разрешила: " + st;
                    Toast((on ? "Включён " : "Выключен ") + r.Name, "Команда из Telegram");
                    return (on ? "🟢 " : "⚪ ") + r.Name;
                }
                return "Модуль не найден";
            }
            catch (Exception e) { return "⚠️ " + Cut(e.Message, 150); }
        }

        // ------------------------------------------------------------------
        //  Голосовое с телефона звучит на ПК
        // ------------------------------------------------------------------

        static global::Windows.Media.Playback.MediaPlayer _voice;

        async Task<bool> PlayVoice(JsonElement m)
        {
            if (!m.TryGetProperty("voice", out var vo)) return false;
            if (Need("control") is { } no) { await Message(no); return true; }
            var (bytes, _) = await Download(Integrations.S(vo, "file_id"));
            if (bytes == null) { await Message("Голосовое слишком большое"); return true; }
            var path = Path.Combine(Path.GetTempPath(), $"nisland-voice-{DateTime.Now:HHmmss}.ogg");
            await File.WriteAllBytesAsync(path, bytes);
            await Ui(() =>
            {
                try
                {
                    _voice?.Dispose();
                    _voice = new global::Windows.Media.Playback.MediaPlayer { Volume = 1 };
                    _voice.Source = global::Windows.Media.Core.MediaSource.CreateFromUri(new Uri(path));
                    _voice.MediaEnded += (_, __) => { try { File.Delete(path); } catch { } };
                    _voice.Play();
                    App.Island?.ShowActivity(new Activity { Kind = "hub", Glyph = "", Tint = System.Windows.Media.Color.FromRgb(0x2A, 0xAB, 0xEE), Title = "Голосовое с телефона", Sub = "звучит на ПК", Seconds = 8, Wide = true });
                }
                catch (Exception e) { Log.Info("tg voice: " + e.Message); }
            });
            await React(_lastMsgId, "👀");
            return true;
        }
    }
}
