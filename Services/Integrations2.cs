using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NIsland.Core;

namespace NIsland.Services
{
    // =====================================================================
    //  Telegram: события на телефон + управление ПК командами и кнопками
    // =====================================================================

    public class TelegramHub : Integration
    {
        public override string Key => "telegram";
        public override string Name => "Telegram";
        public override string Glyph => "";
        public override string Color => "#2AABEE";
        public override string Desc => "Бот на телефоне: события острова приходят сообщениями, а командами и кнопками управляешь ПК — музыка, громкость, статус, " +
                                       "заметки, задачи, напоминания, таймер, буфер обмена, «найти ноутбук». Сообщение боту — всплывашка в острове";
        public override string Help => "Создай бота у @BotFather, вставь токен, напиши боту любое сообщение и нажми «Проверить» — чат найдётся сам. " +
                                       "Бот слушается только твоего чата. Команды — /help в чате с ботом";
        public override string HelpUrl => "https://t.me/BotFather";
        public override HubField[] Fields => new[]
        {
            new HubField("token", "Токен бота", "123456:ABC-DEF…", "secret"),
            new HubField("chat", "ID чата", "Найдётся сам после «Проверить»", Required: false),
            new HubField("control", "Управление с телефона", "музыка, громкость, заметки, таймер, буфер", "bool", Required: false, Default: "1"),
            new HubField("power", "Блокировка, сон и выключение ПК", "команды /lock, /sleep, /shutdown", "bool", Required: false, Default: "0"),
            new HubField("screen", "Снимок экрана по команде", "/screenshot — отправит то, что сейчас на экране", "bool", Required: false, Default: "0"),
            new HubField("apps", "Программы и процессы", "/apps, /kill, /open — список окон, закрыть, запустить", "bool", Required: false, Default: "0"),
            new HubField("files", "Файлы", "/ls, /get — папки и файлы с ПК; присланный боту файл сохранится в «Загрузки\\N-Island»", "bool", Required: false, Default: "0"),
            new HubField("shell", "Команды Windows (опасно)", "/cmd и /ps — любая команда, как в консоли. Включай, только если токен бота никому не известен", "bool", Required: false, Default: "0"),
        };
        public override string[] CanSend => Integrations.EventKinds.Select(e => e.Key).ToArray();
        public override int PollSeconds => 15;

        static readonly HttpClient Long = new(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) }) { Timeout = TimeSpan.FromSeconds(45) };
        string Api(string method) => $"https://api.telegram.org/bot{V("token")}/{method}";
        CancellationTokenSource _loop;
        string _loopToken;
        bool _commandsSet;

        public override async Task<string> Test()
        {
            using var me = await Integrations.GetJson(Api("getMe"));
            var bot = Integrations.S(me.RootElement.GetProperty("result"), "username");
            if (string.IsNullOrEmpty(V("chat")))
            {
                StopLoop();
                using var up = await Integrations.GetJson(Api("getUpdates?limit=20"));
                var last = up.RootElement.GetProperty("result").EnumerateArray().LastOrDefault();
                if (last.ValueKind == JsonValueKind.Undefined || !last.TryGetProperty("message", out var msg))
                    return $"Бот @{bot} работает. Напиши ему любое сообщение в Telegram и нажми «Проверить» ещё раз";
                var chat = msg.GetProperty("chat").GetProperty("id").ToString();
                Integrations.SetValue(this, "chat", chat);
            }
            await SetCommands();
            await Send(new HubEvent("test", "N-Island подключён ✓", "Сюда будут приходить события острова. Команды: /help"));
            return $"Работает ✓ Бот @{bot}, сообщение отправлено в чат {V("chat")}";
        }

        static string Emoji(string kind) => kind switch
        {
            "timer" => "⏰", "reminder" => "📅", "call" => "📞", "notify" => "💬", "track" => "🎵", "vpn" => "🔒", "battery" => "🔋",
            "alert" => "⚠️", "net" => "🌐", "parcel" => "📦", "test" => "✅", _ => "🔔",
        };

        static string H(string s) => System.Net.WebUtility.HtmlEncode(s ?? "");

        public override async Task Send(HubEvent e)
        {
            if (string.IsNullOrEmpty(V("chat"))) return;
            var text = $"{Emoji(e.Kind)} <b>{H(e.Title)}</b>" + (e.Text != "" ? "\n" + H(e.Text) : "");
            await Message(text, e.Kind == "track" ? MusicKeys() : null, silent: e.Kind is "track" or "notify");
            Status = "Отправлено " + DateTime.Now.ToString("HH:mm");
        }

        async Task<long> Message(string html, object keyboard = null, bool silent = false)
        {
            using var doc = await Integrations.PostJson(Api("sendMessage"), new
            {
                chat_id = V("chat"), text = html, parse_mode = "HTML", disable_web_page_preview = true, disable_notification = silent,
                reply_markup = keyboard,
            });
            return doc.RootElement.TryGetProperty("result", out var r) && r.TryGetProperty("message_id", out var id) ? id.GetInt64() : 0;
        }

        async Task SetCommands()
        {
            if (_commandsSet) return;
            var cmds = new List<object>
            {
                new { command = "panel", description = "Пульт кнопками" }, new { command = "status", description = "Состояние ПК" },
                new { command = "top", description = "Что грузит ПК" }, new { command = "apps", description = "Открытые программы" },
                new { command = "music", description = "Что играет + кнопки" },
                new { command = "vol", description = "Громкость: /vol 40" }, new { command = "note", description = "В заметки: /note текст" },
                new { command = "todo", description = "Задача: /todo текст" }, new { command = "remind", description = "Напомнить: /remind 30 текст" },
                new { command = "timer", description = "Таймер: /timer 10" }, new { command = "clip", description = "Буфер обмена ПК" },
                new { command = "copy", description = "Скопировать на ПК: /copy текст" }, new { command = "say", description = "Сообщение в чёлку" },
                new { command = "find", description = "Найти ноутбук (громкий звук)" }, new { command = "weather", description = "Погода" },
                new { command = "alert", description = "Воздушная тревога" }, new { command = "ttn", description = "Посылки Новой почты" },
                new { command = "ip", description = "Внешний IP и сеть" }, new { command = "lock", description = "Заблокировать ПК" },
                new { command = "screenshot", description = "Снимок экрана" }, new { command = "help", description = "Все команды" },
            };
            try { using var _ = await Integrations.PostJson(Api("setMyCommands"), new { commands = cmds }); _commandsSet = true; } catch { }
        }

        static object MusicKeys() => new
        {
            inline_keyboard = new[]
            {
                new[] { Btn("⏮", "prev"), Btn("⏯", "play"), Btn("⏭", "next") },
                new[] { Btn("🔉 −10", "vol-"), Btn("🔇", "mute"), Btn("🔊 +10", "vol+") },
            },
        };

        static object Btn(string text, string data) => new { text, callback_data = data };

        static object PanelKeys() => new
        {
            inline_keyboard = new[]
            {
                new[] { Btn("⏮", "prev"), Btn("⏯", "play"), Btn("⏭", "next") },
                new[] { Btn("🔉 −10", "vol-"), Btn("🔇", "mute"), Btn("🔊 +10", "vol+") },
                new[] { Btn("🔅 −20", "bri-"), Btn("🌙 Не беспокоить", "dnd"), Btn("🔆 +20", "bri+") },
                new[] { Btn("📊 Статус", "status"), Btn("📸 Экран", "shot"), Btn("🔒 Блок", "lock"), Btn("😴 Сон", "sleep") },
            },
        };

        // ------------------------------------------------------------------
        //  Глубокое управление: процессы, файлы, консоль
        // ------------------------------------------------------------------

        static readonly string[] Protected = { "system", "idle", "csrss", "wininit", "winlogon", "lsass", "services", "smss", "svchost", "dwm", "fontdrvhost", "nisland", "registry", "memory compression", "audiodg" };

        static string TopText()
        {
            var sys = SystemService.I;
            sys.CpuPercent();
            var before = System.Diagnostics.Process.GetProcesses().Select(p => { try { return (p.Id, p.ProcessName, Cpu: p.TotalProcessorTime.TotalMilliseconds); } catch { return (p.Id, p.ProcessName, Cpu: -1.0); } }).ToList();
            Thread.Sleep(1000);
            int cpuAll = sys.CpuPercent();
            var rows = new List<(string Name, double Cpu, long Ram)>();
            foreach (var b in before.Where(b => b.Cpu >= 0))
            {
                try
                {
                    using var p = System.Diagnostics.Process.GetProcessById(b.Id);
                    double cpu = (p.TotalProcessorTime.TotalMilliseconds - b.Cpu) / 10.0 / Environment.ProcessorCount;
                    rows.Add((b.ProcessName, cpu, p.WorkingSet64));
                }
                catch { }
            }
            var byName = rows.GroupBy(r => r.Name).Select(g => (Name: g.Key, Cpu: g.Sum(x => x.Cpu), Ram: g.Sum(x => x.Ram))).ToList();
            var sb = new StringBuilder($"⚙️ <b>Процессор {cpuAll}% · память {sys.RamPercent()}%</b>\n\n<b>Процессор:</b>\n");
            foreach (var r in byName.Where(x => x.Name != "Idle").OrderByDescending(x => x.Cpu).Take(6)) sb.Append($"{r.Cpu:0}% — {H(r.Name)}\n");
            sb.Append("\n<b>Память:</b>\n");
            foreach (var r in byName.OrderByDescending(x => x.Ram).Take(6)) sb.Append($"{Maint.Size(r.Ram)} — {H(r.Name)}\n");
            return sb.ToString();
        }

        static (string, object) AppsList()
        {
            var apps = System.Diagnostics.Process.GetProcesses()
                .Where(p => { try { return p.MainWindowHandle != IntPtr.Zero && p.MainWindowTitle != "" && !Protected.Contains(p.ProcessName.ToLowerInvariant()); } catch { return false; } })
                .Select(p => { try { return (p.Id, p.ProcessName, p.MainWindowTitle, Ram: p.WorkingSet64); } catch { return (p.Id, p.ProcessName, MainWindowTitle: "", Ram: 0L); } })
                .OrderByDescending(a => a.Ram).Take(12).ToList();
            var text = "🪟 <b>Открытые программы</b>\n" + string.Join("\n", apps.Select(a => $"• {H(a.ProcessName)} — {H(a.MainWindowTitle.Length > 50 ? a.MainWindowTitle[..50] + "…" : a.MainWindowTitle)} ({Maint.Size(a.Ram)})")) +
                       "\n\nНажми, чтобы закрыть:";
            var keys = apps.Select(a => new[] { Btn("✕ " + (a.ProcessName.Length > 24 ? a.ProcessName[..24] : a.ProcessName), "kill:" + a.Id) }).ToArray();
            return (text, new { inline_keyboard = keys });
        }

        /// <summary>Закрыть по номеру процесса или имени: сначала вежливо (как крестик окна), через 3 с — принудительно.</summary>
        static string Kill(string what)
        {
            what = what.Trim();
            if (what == "") return "Напиши: /kill chrome  или нажми кнопку в /apps";
            var list = int.TryParse(what, out var pid)
                ? new[] { System.Diagnostics.Process.GetProcessById(pid) }
                : System.Diagnostics.Process.GetProcessesByName(what.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? what[..^4] : what);
            if (list.Length == 0) return "Нет такой программы: " + H(what);
            var name = list[0].ProcessName;
            if (Protected.Contains(name.ToLowerInvariant())) return "⛔ " + H(name) + " — системный процесс, его закрывать нельзя";
            int n = 0;
            foreach (var p in list)
            {
                try
                {
                    if (p.MainWindowHandle != IntPtr.Zero) p.CloseMainWindow();
                    if (!p.WaitForExit(3000)) p.Kill(true);
                    n++;
                }
                catch { }
                finally { p.Dispose(); }
            }
            OnUi(() => App.Island?.Toast("", "Закрыто из Telegram", name));
            return n > 0 ? $"✕ Закрыто: {H(name)}" + (n > 1 ? $" ({n} шт.)" : "") : "Не получилось закрыть " + H(name);
        }

        static string Expand(string path)
        {
            path = (path ?? "").Trim().Trim('"');
            if (path == "" || path == "~") return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var known = path.ToLowerInvariant() switch
            {
                "загрузки" or "downloads" => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
                "рабочий стол" or "desktop" => Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                "документы" or "documents" => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                _ => null,
            };
            return known ?? Environment.ExpandEnvironmentVariables(path.StartsWith("~") ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + path[1..] : path);
        }

        static string ListDir(string arg)
        {
            var dir = Expand(arg);
            if (!System.IO.Directory.Exists(dir)) return "Нет такой папки: " + H(dir);
            var di = new System.IO.DirectoryInfo(dir);
            var sb = new StringBuilder($"📁 <code>{H(di.FullName)}</code>\n");
            foreach (var d in di.EnumerateDirectories().Where(x => !x.Attributes.HasFlag(System.IO.FileAttributes.Hidden)).Take(25)) sb.Append($"📁 {H(d.Name)}\n");
            foreach (var f in di.EnumerateFiles().Where(x => !x.Attributes.HasFlag(System.IO.FileAttributes.Hidden)).OrderByDescending(x => x.LastWriteTime).Take(25))
                sb.Append($"📄 {H(f.Name)} · {Maint.Size(f.Length)}\n");
            sb.Append("\nСкачать: /get " + H(System.IO.Path.Combine(di.FullName, "имя файла")));
            var s = sb.ToString();
            return s.Length > 3900 ? s[..3900] + "…" : s;
        }

        async Task<string> SendFile(string arg)
        {
            var path = Expand(arg);
            if (!System.IO.File.Exists(path)) return "Нет такого файла: " + H(path) + "\nПосмотреть папку: /ls путь";
            var fi = new System.IO.FileInfo(path);
            if (fi.Length > 49L * 1024 * 1024) return $"Файл {Maint.Size(fi.Length)} — больше 50 МБ, Telegram не примет его от бота";
            using var form = new MultipartFormDataContent { { new StringContent(V("chat")), "chat_id" } };
            var content = new StreamContent(System.IO.File.OpenRead(path));
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(content, "document", fi.Name);
            using var resp = await Long.PostAsync(Api("sendDocument"), form);
            if (!resp.IsSuccessStatusCode) return "Не отправилось: " + H(Integrations.Snip(await resp.Content.ReadAsStringAsync()));
            OnUi(() => App.Island?.Toast("", "Файл отправлен в Telegram", fi.Name));
            return null;
        }

        /// <summary>Файл или фото, присланные боту, — в «Загрузки\N-Island» (Telegram отдаёт ботам файлы до 20 МБ).</summary>
        async Task ReceiveFile(JsonElement m)
        {
            string fileId = null, name = null;
            if (m.TryGetProperty("document", out var doc)) { fileId = Integrations.S(doc, "file_id"); name = Integrations.S(doc, "file_name"); }
            else if (m.TryGetProperty("photo", out var ph) && ph.GetArrayLength() > 0) { fileId = Integrations.S(ph[ph.GetArrayLength() - 1], "file_id"); name = $"photo-{DateTime.Now:yyyyMMdd-HHmmss}.jpg"; }
            else if (m.TryGetProperty("video", out var vd)) { fileId = Integrations.S(vd, "file_id"); name = Integrations.S(vd, "file_name") is { Length: > 0 } vn ? vn : $"video-{DateTime.Now:yyyyMMdd-HHmmss}.mp4"; }
            else if (m.TryGetProperty("audio", out var au)) { fileId = Integrations.S(au, "file_id"); name = Integrations.S(au, "file_name"); }
            if (fileId == null) return;
            if (!Flag("files")) { await Message("🔐 Приём файлов не разрешён (Интеграции → Telegram → Файлы)"); return; }
            using var info = await Integrations.GetJson(Api("getFile?file_id=" + Uri.EscapeDataString(fileId)));
            var fp = Integrations.S(info.RootElement.GetProperty("result"), "file_path");
            if (fp == "") { await Message("Файл больше 20 МБ — Telegram не отдаёт такие ботам"); return; }
            var bytes = await Long.GetByteArrayAsync($"https://api.telegram.org/file/bot{V("token")}/{fp}");
            var dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "N-Island");
            System.IO.Directory.CreateDirectory(dir);
            name = string.Concat((string.IsNullOrEmpty(name) ? System.IO.Path.GetFileName(fp) : name).Select(c => System.IO.Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            var target = System.IO.Path.Combine(dir, name);
            for (int i = 2; System.IO.File.Exists(target); i++) target = System.IO.Path.Combine(dir, System.IO.Path.GetFileNameWithoutExtension(name) + $" ({i})" + System.IO.Path.GetExtension(name));
            await System.IO.File.WriteAllBytesAsync(target, bytes);
            OnUi(() =>
            {
                NIsland.Pages.ShelfPage.Add(new[] { target });   // сразу на полку острова — удобно перетащить куда нужно
                App.Island?.ShowActivity(new Activity { Kind = "hub", Glyph = "", Tint = System.Windows.Media.Color.FromRgb(0x2A, 0xAB, 0xEE), Title = "Файл с телефона", Sub = System.IO.Path.GetFileName(target), Seconds = 6, Wide = true,
                    OnClick = () => System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{target}\"")?.Dispose() });
            });
            await Message($"💾 Сохранено на ПК: <code>{H(target)}</code>");
        }

        static async Task<string> Shell(bool powershell, string command)
        {
            var psi = powershell
                ? new System.Diagnostics.ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -Command -") { RedirectStandardInput = true }
                : new System.Diagnostics.ProcessStartInfo("cmd.exe", "/d /c " + command);
            psi.UseShellExecute = false; psi.CreateNoWindow = true; psi.RedirectStandardOutput = true; psi.RedirectStandardError = true;
            var enc = Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
            psi.StandardOutputEncoding = psi.StandardErrorEncoding = powershell ? Encoding.UTF8 : enc;
            using var p = System.Diagnostics.Process.Start(psi);
            if (powershell)
            {
                await p.StandardInput.WriteLineAsync("[Console]::OutputEncoding=[Text.Encoding]::UTF8; " + command);
                p.StandardInput.Close();
            }
            var outTask = p.StandardOutput.ReadToEndAsync();
            var errTask = p.StandardError.ReadToEndAsync();
            bool done = await Task.Run(() => p.WaitForExit(30000));
            if (!done) { try { p.Kill(true); } catch { } }
            var text = (await outTask + (await errTask is { Length: > 0 } err ? "\n" + err : "")).Trim();
            OnUi(() => App.Island?.Toast("", "Выполнена команда из Telegram", command.Length > 60 ? command[..60] + "…" : command));
            if (text.Length > 3500) text = text[..3500] + "\n…";
            return (done ? "" : "⏱ Остановлено через 30 с\n") + (text == "" ? "✓ Выполнено, вывода нет" : "<pre>" + H(text) + "</pre>");
        }

        // ------------------------------------------------------------------
        //  Приём: длинный опрос (ответ на команду — за секунду, а не через 20 с)
        // ------------------------------------------------------------------

        public override Task Poll()
        {
            if (string.IsNullOrEmpty(V("chat"))) return Task.CompletedTask;
            if (_loop == null || _loop.IsCancellationRequested || _loopToken != V("token")) StartLoop();
            return Task.CompletedTask;
        }

        void StartLoop()
        {
            StopLoop();
            _loop = new CancellationTokenSource();
            _loopToken = V("token");
            var ct = _loop.Token;
            _ = Task.Run(async () =>
            {
                int fails = 0;
                await SetCommands();
                while (!ct.IsCancellationRequested && Enabled && Ready && V("chat") != "" && V("token") == _loopToken)
                {
                    try
                    {
                        long.TryParse(St("offset"), out var offset);
                        using var resp = await Long.GetAsync(Api($"getUpdates?timeout=25&limit=20&offset={offset}&allowed_updates=" + Uri.EscapeDataString("[\"message\",\"callback_query\"]")), ct);
                        var body = await resp.Content.ReadAsStringAsync(ct);
                        if (!resp.IsSuccessStatusCode) throw new HttpRequestException(Integrations.Snip(body), null, resp.StatusCode);
                        using var doc = JsonDocument.Parse(body);
                        foreach (var u in doc.RootElement.GetProperty("result").EnumerateArray())
                        {
                            offset = Math.Max(offset, u.GetProperty("update_id").GetInt64() + 1);
                            try { await Handle(u.Clone()); } catch (Exception e) { Log.Info("telegram cmd: " + e.Message); }
                        }
                        if (offset.ToString() != St("offset")) OnUi(() => SetSt("offset", offset.ToString()));
                        fails = 0;
                        Ok = true;
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
                    catch (Exception e)
                    {
                        Ok = false;
                        Status = "Ошибка: " + Integrations.Reason(e);
                        // 409 — этот же бот опрашивается где-то ещё; 401 — токен отозван: ждём дольше
                        await Task.Delay(TimeSpan.FromSeconds(Math.Min(300, 5 * Math.Pow(2, Math.Min(++fails, 6)))), ct).ContinueWith(_ => { });
                    }
                }
            });
        }

        void StopLoop() { try { _loop?.Cancel(); } catch { } _loop = null; }

        static void OnUi(Action a) => System.Windows.Application.Current?.Dispatcher.BeginInvoke(a);
        static Task<T> Ui<T>(Func<T> f) => System.Windows.Application.Current.Dispatcher.InvokeAsync(f).Task;

        async Task Handle(JsonElement u)
        {
            if (u.TryGetProperty("callback_query", out var cq))
            {
                if (cq.GetProperty("message").GetProperty("chat").GetProperty("id").ToString() != V("chat")) return;
                var data = Integrations.S(cq, "data");
                var answer = await Callback(data, cq);
                try { using var _ = await Integrations.PostJson(Api("answerCallbackQuery"), new { callback_query_id = Integrations.S(cq, "id"), text = answer ?? "" }); } catch { }
                return;
            }
            if (!u.TryGetProperty("message", out var m) || m.GetProperty("chat").GetProperty("id").ToString() != V("chat")) return;   // чужие чаты не слушаем
            var text = Integrations.S(m, "text").Trim();
            if (text == "") { await ReceiveFile(m); return; }
            if (text.StartsWith("/"))
            {
                var sp = text.IndexOf(' ');
                var cmd = (sp > 0 ? text[1..sp] : text[1..]).ToLowerInvariant();
                if (cmd.Contains('@')) cmd = cmd[..cmd.IndexOf('@')];
                var arg = sp > 0 ? text[(sp + 1)..].Trim() : "";
                var reply = await Command(cmd, arg);
                if (reply?.Html != null) await Message(reply.Value.Html, reply.Value.Keys);
                return;
            }
            // обычный текст с телефона → в остров (ссылку можно открыть, текст — скопировать)
            bool link = text.StartsWith("http://") || text.StartsWith("https://");
            OnUi(() =>
            {
                Rows.Insert(0, new HubRow
                {
                    Title = text.Length > 120 ? text[..120] + "…" : text, Sub = "из Telegram · " + DateTime.Now.ToString("HH:mm"), Url = link ? text : null,
                    ActionText = "Копировать", Action = () => { ClipboardService.I.CopyBack(text); return Task.FromResult<string>("Скопировано"); },
                });
                if (Rows.Count > 8) Rows.RemoveAt(Rows.Count - 1);
                Notify("Telegram", text, link ? text : null);
                Integrations.RaiseChanged();
            });
        }

        bool Control => Flag("control");

        async Task<(string Html, object Keys)?> Command(string cmd, string arg)
        {
            var m = MediaService.I;
            string NoControl() => "🔐 Управление с телефона выключено в настройках N-Island (Интеграции → Telegram).";
            switch (cmd)
            {
                case "start":
                case "help":
                    return ("<b>N-Island — пульт ПК</b>\n" +
                            "/status — состояние ПК\n/music — что играет и кнопки\n/play /pause /next /prev\n/vol 40 · /mute — громкость\n" +
                            "/note текст — в заметки\n/todo текст — в задачи\n/remind 30 текст — напомнить через 30 мин\n/timer 10 — таймер\n" +
                            "/clip — что в буфере ПК · /copy текст — скопировать на ПК\n/say текст — сообщение в чёлку\n/find — громкий сигнал, чтобы найти ноутбук\n" +
                            "/weather · /alert · /ttn · /ip\n" +
                            "/panel — пульт кнопками\n/top — что грузит ПК · /disk · /battery · /wifi\n/brightness 70 · /dnd · /mode game · /eq bass · /awake on\n" +
                            "/apps · /kill имя · /open ссылка или программа\n/ls папка · /get путь — файлы (пришли файл боту — сохранится на ПК)\n" +
                            "/lock · /sleep · /hibernate · /restart · /shutdown · /screenshot\n/cmd команда · /ps команда — консоль Windows\n" +
                            "Часть команд работает, только если разрешена в N-Island (Интеграции → Telegram).\n\nПросто текст или ссылка — появится в чёлке.", null);
                case "panel":
                    return ("🎛 <b>Пульт ПК</b>", PanelKeys());
                case "top":
                    return (await Task.Run(TopText), null);
                case "disk":
                case "disks":
                    return ("💾 " + string.Join("\n", System.IO.DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType is System.IO.DriveType.Fixed or System.IO.DriveType.Removable)
                        .Select(d => $"{d.Name.TrimEnd('\\')} свободно {Maint.Size(d.AvailableFreeSpace)} из {Maint.Size(d.TotalSize)} ({100 - d.AvailableFreeSpace * 100 / Math.Max(1, d.TotalSize)}% занято)")), null);
                case "battery":
                {
                    var sys = SystemService.I;
                    if (!sys.HasBattery) return ("🔌 У этого ПК нет батареи", null);
                    var hb = await Maint.Battery();
                    return ($"🔋 {sys.BatteryPercent}%{(sys.Charging ? " ⚡ заряжается" : sys.BatteryLeft is TimeSpan l ? $" · ещё {(int)l.TotalHours} ч {l.Minutes} мин" : "")}" +
                            (hb != null && hb.Design > 0 ? $"\nИзнос: ёмкость {hb.Full * 100 / hb.Design}% от новой" + (hb.Cycles > 0 ? $" · циклов {hb.Cycles}" : "") : ""), null);
                }
                case "wifi":
                {
                    var l = await WifiTools.Current();
                    return (l == null ? "📶 Wi-Fi не подключён" : $"📶 <b>{H(l.Ssid)}</b>\nСигнал {l.Signal}% ({l.Rssi} dBm) · канал {l.Channel} · {H(l.Band)}\n{H(l.Standard)} · {Math.Max(l.RxMbps, l.TxMbps):0} Мбит/с", null);
                }
                case "brightness":
                case "bri":
                    if (!Control) return (NoControl(), null);
                    if (!int.TryParse(arg.TrimEnd('%'), out var bri)) return ($"🔆 Яркость {SystemService.I.GetBrightness()?.ToString() ?? "—"}%. Напиши: /brightness 70", null);
                    SystemService.I.SetBrightness(Math.Clamp(bri, 0, 100));
                    return ($"🔆 Яркость {Math.Clamp(bri, 0, 100)}%", null);
                case "dnd":
                    if (!Control) return (NoControl(), null);
                    var dnd = await Ui(() => { App.Island?.SetMode(Store.Settings.Mode == "dnd" ? "normal" : "dnd"); return Store.Settings.Mode == "dnd"; });
                    return (dnd ? "🌙 «Не беспокоить» включён" : "🔔 «Не беспокоить» выключен", null);
                case "mode":
                    if (!Control) return (NoControl(), null);
                    if (arg == "") return ("Режимы: " + string.Join(", ", Modes.All.Select(x => x.Key)) + ". Напиши: /mode game", null);
                    if (!Modes.All.Any(x => x.Key == arg)) return ("Нет такого режима. Есть: " + string.Join(", ", Modes.All.Select(x => x.Key)), null);
                    await Ui(() => { App.Island?.SetMode(arg); return 0; });
                    return ("Режим: " + H(arg), null);
                case "eq":
                    if (!Control) return (NoControl(), null);
                    if (arg == "") return ("🎚 Эквалайзер: " + (Store.Settings.EqOn ? H(Equalizer.PresetName(Store.Settings.EqPreset)) : "выключен") + "\nПресеты: " + string.Join(", ", Equalizer.Presets.Select(p => p.Key)) + ", off", null);
                    await Ui(() => { if (arg == "off") Equalizer.SetOn(false); else Equalizer.SetPreset(arg); return 0; });
                    return (Equalizer.Installed ? "🎚 Готово" : "🎚 Сохранено, но на ПК не установлен движок Equalizer APO", null);
                case "awake":
                    if (!Control) return (NoControl(), null);
                    bool on = arg is "on" or "1" or "да" or "вкл" || (arg == "" && !SystemService.I.KeepAwake);
                    await Ui(() => { SystemService.I.KeepAwake = on; return 0; });
                    return (on ? "☕ ПК не будет засыпать" : "💤 ПК снова засыпает как обычно", null);
                case "apps":
                case "kill":
                case "open":
                    if (!Flag("apps")) return ("🔐 Управление программами не разрешено (Интеграции → Telegram)", null);
                    if (cmd == "apps") return await Task.Run(AppsList);
                    if (cmd == "open")
                    {
                        if (arg == "") return ("Напиши: /open https://… или /open notepad", null);
                        await Ui(() => { SystemService.Run(arg); App.Island?.Toast("", "Открыто из Telegram", arg); return 0; });
                        return ("🚀 Открываю: " + H(arg), null);
                    }
                    return (await Task.Run(() => Kill(arg)), null);
                case "ls":
                case "get":
                    if (!Flag("files")) return ("🔐 Доступ к файлам не разрешён (Интеграции → Telegram)", null);
                    if (cmd == "ls") return (ListDir(arg), null);
                    return (await SendFile(arg), null);
                case "cmd":
                case "ps":
                    if (!Flag("shell")) return ("🔐 Команды Windows не разрешены (Интеграции → Telegram)", null);
                    if (arg == "") return ("Напиши: /cmd ipconfig  или  /ps Get-Date", null);
                    return (await Shell(cmd == "ps", arg), null);
                case "hibernate":
                    if (!Flag("power")) return ("🔐 Блокировка, сон и выключение не разрешены (Интеграции → Telegram)", null);
                    _ = Task.Run(async () => { await Task.Delay(1500); System.Windows.Forms.Application.SetSuspendState(System.Windows.Forms.PowerState.Hibernate, false, false); });
                    return ("🛌 Гибернация", null);
                case "restart":
                case "reboot":
                    if (!Flag("power")) return ("🔐 Блокировка, сон и выключение не разрешены (Интеграции → Telegram)", null);
                    return ("⚠️ Перезагрузить ПК? Несохранённое пропадёт.", new { inline_keyboard = new[] { new[] { Btn("Да, перезагрузить через минуту", "restart"), Btn("Отмена", "cancel") } } });
                case "status": return (await Ui(StatusText), MusicKeys());
                case "music":
                case "now":
                    return (await Ui(() => m.HasSession ? $"🎵 <b>{H(m.Title)}</b>\n{H(m.Artist)}\n{H(m.AppName)} · {(m.IsPlaying ? "играет" : "пауза")}" +
                                                         (m.HasTime ? $" · {Fmt.Time(m.Position)} / {Fmt.Time(m.Duration)}" : "") : "Сейчас ничего не играет"), MusicKeys());
                case "play": case "pause": case "next": case "prev":
                    if (!Control) return (NoControl(), null);
                    await Ui(() => { if (cmd == "next") m.Next(); else if (cmd == "prev") m.Prev(); else if (cmd == "play") m.Play(); else m.Pause(); return 0; });
                    return null;
                case "vol":
                    if (!Control) return (NoControl(), null);
                    if (int.TryParse(arg.TrimEnd('%'), out var v)) { await Ui(() => { AudioService.I.SetVolume(Math.Clamp(v, 0, 100) / 100.0); return 0; }); return ($"🔊 Громкость {Math.Clamp(v, 0, 100)}%", null); }
                    return ($"🔊 Громкость {Math.Round(AudioService.I.Volume * 100)}%{(AudioService.I.Muted ? " (без звука)" : "")}", MusicKeys());
                case "mute":
                    if (!Control) return (NoControl(), null);
                    await Ui(() => { AudioService.I.ToggleMute(); return 0; });
                    return (AudioService.I.Muted ? "🔊 Звук включён" : "🔇 Звук выключен", null);
                case "note":
                    if (!Control) return (NoControl(), null);
                    if (arg == "") return ("Напиши так: /note купить хлеб", null);
                    await Ui(() => { Store.Data.Notes = (Store.Data.Notes ?? "").TrimEnd() + (string.IsNullOrWhiteSpace(Store.Data.Notes) ? "" : "\n") + arg; Store.SaveData(); return 0; });
                    return ("📝 Добавлено в заметки", null);
                case "todo":
                    if (!Control) return (NoControl(), null);
                    if (arg == "") return ("Напиши так: /todo позвонить маме", null);
                    await Ui(() => { Store.Data.Todos.Insert(0, new TodoItem { Text = arg }); Store.SaveData(); return 0; });
                    return ("✅ Задача добавлена", null);
                case "remind":
                {
                    if (!Control) return (NoControl(), null);
                    var mm = Regex.Match(arg, @"^(\d{1,4})\s*(м|мин|m|min|ч|h)?\s+(.+)$", RegexOptions.IgnoreCase);
                    if (!mm.Success) return ("Напиши так: /remind 30 вынуть пиццу  (минуты) или /remind 2ч созвон", null);
                    int n = int.Parse(mm.Groups[1].Value);
                    var at = DateTime.Now.AddMinutes(mm.Groups[2].Value.ToLowerInvariant() is "ч" or "h" ? n * 60 : n);
                    var what = mm.Groups[3].Value;
                    await Ui(() => { Store.Data.Events.Add(new CalEvent { Date = at.Date, Time = at.ToString("HH:mm"), Title = what }); Store.SaveData(); return 0; });
                    return ($"⏰ Напомню в {at:HH:mm}: {H(what)}", null);
                }
                case "timer":
                    if (!Control) return (NoControl(), null);
                    if (!double.TryParse(arg.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var min) || min <= 0) return ("Напиши так: /timer 10  (минуты)", null);
                    await Ui(() => { var t = TimerService.I; t.Reset(); t.SetMinutes(min); t.Start(); return 0; });
                    return ($"⏱ Таймер на {min:0.#} мин запущен", null);
                case "clip":
                    if (!Control) return (NoControl(), null);
                    var clip = await Ui(() => { try { return System.Windows.Clipboard.ContainsText() ? System.Windows.Clipboard.GetText() : null; } catch { return null; } });
                    return (clip == null ? "📋 В буфере обмена ПК нет текста" : "📋 <code>" + H(clip.Length > 3500 ? clip[..3500] + "…" : clip) + "</code>", null);
                case "copy":
                    if (!Control) return (NoControl(), null);
                    if (arg == "") return ("Напиши так: /copy текст", null);
                    await Ui(() => { ClipboardService.I.CopyBack(arg); App.Island?.Toast("", "Скопировано с телефона", arg.Length > 60 ? arg[..60] + "…" : arg); return 0; });
                    return ("📋 Скопировано на ПК", null);
                case "say":
                    if (arg == "") return ("Напиши так: /say я уже еду", null);
                    await Ui(() =>
                    {
                        App.Island?.ShowActivity(new Activity { Kind = "hub", Glyph = Glyph, Tint = System.Windows.Media.Color.FromRgb(0x2A, 0xAB, 0xEE), Title = "Сообщение с телефона", Sub = arg, Seconds = 15, Wide = true });
                        Sound.Play(SoundEvent.Notification);
                        return 0;
                    });
                    return ("💬 Показано в чёлке", null);
                case "find":
                    if (!Control) return (NoControl(), null);
                    await Ui(() =>
                    {
                        AudioService.I.SetVolume(Math.Max(AudioService.I.Volume, 0.8));
                        App.Island?.ShowActivity(new Activity { Kind = "alert", Glyph = "", Tint = System.Windows.Media.Color.FromRgb(0xFF, 0x9F, 0x0A), Title = "Ищут этот ноутбук", Sub = "Сигнал из Telegram", Seconds = 10, Wide = true });
                        return 0;
                    });
                    for (int i = 0; i < 6; i++) { await Ui(() => { Sound.Play(SoundEvent.Alert); return 0; }); await Task.Delay(1300); }
                    return ("🔔 Сигнал проигран", null);
                case "weather":
                {
                    var w = await WeatherService.Get();
                    if (w == null) return ("🌦 Погода недоступна: " + H(WeatherService.LastError ?? "нет связи"), null);
                    var (icon, desc) = WeatherService.Describe(w.Code);
                    var sb = new StringBuilder($"{icon} <b>{H(w.City)}: {Math.Round(w.Temp)}°</b>, {H(desc)}\nощущается {Math.Round(w.Feels)}° · ветер {Math.Round(w.Wind)} м/с · влажность {Math.Round(w.Humidity)}%");
                    if (WeatherService.Nowcast(w) is { } nc) sb.Append("\n☔ " + H(nc));
                    foreach (var d in w.Days.Take(3)) sb.Append($"\n{Fmt.Cap(d.Day.ToString("ddd d", Fmt.Ru))}: {WeatherService.Describe(d.Code).Icon} {Math.Round(d.Max)}° / {Math.Round(d.Min)}°");
                    return (sb.ToString(), null);
                }
                case "alert":
                case "alerts":
                {
                    var a = Integrations.Get("alerts");
                    if (a == null || !a.Enabled) return ("Тревоги не подключены в N-Island (Интеграции → Повітряна тривога)", null);
                    await Integrations.PollOne(a);
                    return (string.Join("\n", a.Rows.Select(r => "• " + H(r.Title) + (string.IsNullOrEmpty(r.Sub) ? "" : " — " + H(r.Sub)))), null);
                }
                case "ttn":
                case "np":
                {
                    var np = Integrations.Get("novaposhta");
                    if (np == null || !np.Enabled || !np.Ready) return ("Посылки не подключены (Интеграции → Нова пошта)", null);
                    await Integrations.PollOne(np);
                    return (np.Rows.Count == 0 ? "📦 Посылок нет" : "📦 " + string.Join("\n\n", np.Rows.Select(r => "<b>" + H(r.Title) + "</b>\n" + H(r.Sub))), null);
                }
                case "ip":
                {
                    var r = await Osint.Ip("");
                    var link = await WifiTools.Current();
                    return ("🌐 " + string.Join("\n", r.Take(6).Select(x => H(x.K) + ": " + H(x.V))) + (link != null ? $"\nWi-Fi: {H(link.Ssid)} · {link.Signal}%" : "") +
                            (Vpn.Active ? "\n🔒 VPN включён" : ""), null);
                }
                case "lock":
                    if (!Flag("power")) return ("🔐 Блокировка, сон и выключение не разрешены (Интеграции → Telegram)", null);
                    LockWorkStation();
                    return ("🔒 ПК заблокирован", null);
                case "sleep":
                    if (!Flag("power")) return ("🔐 Блокировка, сон и выключение не разрешены (Интеграции → Telegram)", null);
                    _ = Task.Run(async () => { await Task.Delay(1500); System.Windows.Forms.Application.SetSuspendState(System.Windows.Forms.PowerState.Suspend, false, false); });
                    return ("😴 ПК засыпает", null);
                case "shutdown":
                    if (!Flag("power")) return ("🔐 Блокировка, сон и выключение не разрешены (Интеграции → Telegram)", null);
                    return ("⚠️ Выключить ПК? Несохранённое пропадёт.", new { inline_keyboard = new[] { new[] { Btn("Да, выключить через минуту", "shutdown"), Btn("Отмена", "cancel") } } });
                case "cancel":
                    if (!Flag("power")) return null;
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("shutdown.exe", "/a") { CreateNoWindow = true, UseShellExecute = false })?.Dispose();
                    return ("Выключение отменено", null);
                case "screenshot":
                case "screen":
                    if (!Flag("screen")) return ("🔐 Снимок экрана не разрешён (Интеграции → Telegram)", null);
                    await SendScreenshot();
                    return null;
                default:
                    return ("Не знаю такой команды. /help — список", null);
            }
        }

        async Task<string> Callback(string data, JsonElement cq)
        {
            var m = MediaService.I;
            if (data == "shutdown")
            {
                if (!Flag("power")) return "Не разрешено";
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("shutdown.exe", "/s /t 60") { CreateNoWindow = true, UseShellExecute = false })?.Dispose();
                await Ui(() => { App.Island?.Toast("", "ПК выключится через минуту", "Команда из Telegram · /cancel — отменить"); return 0; });
                await Message("⏻ ПК выключится через минуту. Отменить: /cancel");
                return "Выключаю";
            }
            if (data == "restart")
            {
                if (!Flag("power")) return "Не разрешено";
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("shutdown.exe", "/r /t 60") { CreateNoWindow = true, UseShellExecute = false })?.Dispose();
                await Ui(() => { App.Island?.Toast("", "ПК перезагрузится через минуту", "Команда из Telegram · /cancel — отменить"); return 0; });
                await Message("🔄 ПК перезагрузится через минуту. Отменить: /cancel");
                return "Перезагружаю";
            }
            if (data == "cancel") return "Отменено";
            if (data.StartsWith("kill:"))
            {
                if (!Flag("apps")) return "Не разрешено";
                return await Task.Run(() => Kill(data[5..]));
            }
            switch (data)
            {
                case "status": await Message(await Ui(StatusText), MusicKeys()); return "";
                case "shot":
                    if (!Flag("screen")) return "Снимок экрана не разрешён";
                    await SendScreenshot(); return "📸";
                case "lock":
                    if (!Flag("power")) return "Не разрешено";
                    LockWorkStation(); return "🔒 Заблокирован";
                case "sleep":
                    if (!Flag("power")) return "Не разрешено";
                    _ = Task.Run(async () => { await Task.Delay(1500); System.Windows.Forms.Application.SetSuspendState(System.Windows.Forms.PowerState.Suspend, false, false); });
                    return "😴 Засыпает";
            }
            if (!Control) return "Управление выключено";
            if (data is "bri-" or "bri+")
            {
                int cur = SystemService.I.GetBrightness() ?? 50;
                int to = Math.Clamp(cur + (data == "bri+" ? 20 : -20), 0, 100);
                SystemService.I.SetBrightness(to);
                return $"🔆 {to}%";
            }
            if (data == "dnd") return await Ui(() => { App.Island?.SetMode(Store.Settings.Mode == "dnd" ? "normal" : "dnd"); return Store.Settings.Mode == "dnd" ? "🌙 Не беспокоить" : "🔔 Обычный режим"; });
            string res = await Ui(() =>
            {
                switch (data)
                {
                    case "prev": m.Prev(); return "⏮";
                    case "next": m.Next(); return "⏭";
                    case "play": m.TogglePlay(); return m.IsPlaying ? "⏸" : "▶";
                    case "mute": AudioService.I.ToggleMute(); return "🔇";
                    case "vol-": AudioService.I.SetVolume(AudioService.I.Volume - 0.1); return $"🔉 {Math.Round(Math.Max(0, AudioService.I.Volume - 0.1) * 100)}%";
                    case "vol+": AudioService.I.SetVolume(AudioService.I.Volume + 0.1); return $"🔊 {Math.Round(Math.Min(1, AudioService.I.Volume + 0.1) * 100)}%";
                }
                return null;
            });
            return res;
        }

        static string StatusText()
        {
            var sys = SystemService.I;
            var m = MediaService.I;
            sys.CpuPercent();
            System.Threading.Thread.Sleep(250);
            var sb = new StringBuilder("💻 <b>" + H(Environment.MachineName) + "</b>\n");
            if (sys.HasBattery) sb.Append($"🔋 {sys.BatteryPercent}%{(sys.Charging ? " ⚡ заряжается" : sys.BatteryLeft is TimeSpan left ? $" · ещё {(int)left.TotalHours} ч {left.Minutes} мин" : "")}\n");
            sb.Append($"⚙️ Процессор {sys.CpuPercent()}% · память {sys.RamPercent()}%\n");
            var up = Maint.Uptime;
            sb.Append($"⏱ Работает {(up.TotalDays >= 1 ? (int)up.TotalDays + " дн " : "")}{up.Hours} ч {up.Minutes} мин\n");
            sb.Append($"🔊 Громкость {Math.Round(AudioService.I.Volume * 100)}%{(AudioService.I.Muted ? " (без звука)" : "")} · {H(AudioService.I.OutputName)}\n");
            if (Vpn.Active) sb.Append("🔒 VPN включён\n");
            sb.Append($"🌙 Режим: {H(I18n.T(Modes.Current.Name))}\n");
            if (m.HasSession) sb.Append($"🎵 {(m.IsPlaying ? "Играет" : "Пауза")}: {H(m.TrackLine)}");
            return sb.ToString();
        }

        [DllImport("user32.dll")] static extern bool LockWorkStation();

        async Task SendScreenshot()
        {
            var png = await Ui(() =>
            {
                var b = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
                using var bmp = new System.Drawing.Bitmap(b.Width, b.Height);
                using (var g = System.Drawing.Graphics.FromImage(bmp)) g.CopyFromScreen(b.Left, b.Top, 0, 0, b.Size);
                using var ms = new MemoryStream();
                bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                return ms.ToArray();
            });
            using var form = new MultipartFormDataContent
            {
                { new StringContent(V("chat")), "chat_id" },
                { new StringContent("🖥 " + DateTime.Now.ToString("HH:mm:ss")), "caption" },
            };
            var file = new ByteArrayContent(png);
            file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(file, "document", "screen.png");   // документом — без сжатия, текст на снимке читается
            using var resp = await Http.PostAsync(Api("sendDocument"), form);
            resp.EnsureSuccessStatusCode();
            await Ui(() => { App.Island?.Toast("", "Снимок экрана отправлен в Telegram", null); return 0; });
        }
    }

    // =====================================================================
    //  Повітряна тривога: siren.pp.ua без ключа или alerts.in.ua с токеном
    // =====================================================================

    public class AlertsHub : Integration
    {
        public override string Key => "alerts";
        public override string Name => "Повітряна тривога";
        public override string Glyph => "";
        public override string Color => "#FF453A";
        public override string Desc => "Начало и отбой тревоги в твоей области, районе или громаде — и там, где живут родные. Сколько длится, сколько было за сутки, " +
                                       "артобстрел и другие угрозы. Дополнение к официальному приложению «Повітряна тривога», не замена ему";
        public override string Help => "Без ключа данные берутся с siren.pp.ua (Ukraine Alarm). С токеном alerts.in.ua — оттуда. Места — по одному в строке, как в списке регионов: " +
                                       "«Миколаївська область», «Миколаївський район», «м. Київ». Если связи нет — остров честно пишет «нет данных», а не «тревоги нет»";
        public override string HelpUrl => "https://map.ukrainealarm.com/";
        public override HubField[] Fields => new[]
        {
            new HubField("oblast", "Моё место", "Миколаївська область", Default: "м. Київ"),
            new HubField("places", "Ещё места (родные, работа)", "Одеський район\nм. Київ", "multi", Required: false),
            new HubField("token", "Токен alerts.in.ua", "необязательно", "secret", Required: false),
            new HubField("threats", "Другие угрозы", "артобстрел, уличные бои, химическая и радиационная", "bool", Required: false, Default: "1"),
            new HubField("clear", "Сообщать об отбое", "", "bool", Required: false, Default: "1"),
            new HubField("pause", "Пауза музыки при тревоге", "чтобы не пропустить сирену", "bool", Required: false, Default: "0"),
            new HubField("notch", "Значок тревоги в чёлке", "пока тревога не закончится", "bool", Required: false, Default: "1"),
        };
        public override string[] DefaultSend => new[] { "alert" };
        public override int PollSeconds => 15;

        /// <summary>Сейчас тревога в «моём месте» — для значка в свёрнутой чёлке.</summary>
        public static bool AlertNow { get; private set; }
        public static bool ShowInNotch => AlertNow && Integrations.Get("alerts") is { Enabled: true } a && a.Flag("notch");

        static readonly Dictionary<string, string> TypeNames = new()
        {
            ["AIR"] = "Повітряна тривога", ["air_raid"] = "Повітряна тривога",
            ["ARTILLERY"] = "Загроза артобстрілу", ["artillery_shelling"] = "Загроза артобстрілу",
            ["URBAN_FIGHTS"] = "Вуличні бої", ["urban_fights"] = "Вуличні бої",
            ["CHEMICAL"] = "Хімічна загроза", ["chemical"] = "Хімічна загроза",
            ["NUCLEAR"] = "Радіаційна загроза", ["nuclear"] = "Радіаційна загроза",
        };

        record Node(string Id, string Name, string Type, string Parent);
        static Dictionary<string, Node> _regions;
        static DateTime _regionsAt;
        record Active(string RegionId, string Name, string Type, DateTime Since);

        static string Norm(string s) => Regex.Replace((s ?? "").ToLowerInvariant().Replace("ʼ", "'").Replace("’", "'"), @"\s+", " ").Trim();

        async Task<Dictionary<string, Node>> Regions()
        {
            if (_regions != null && (DateTime.Now - _regionsAt).TotalDays < 7) return _regions;
            var file = Path.Combine(Store.Dir, "alert-regions.json");
            string json = null;
            if (File.Exists(file) && (DateTime.Now - File.GetLastWriteTime(file)).TotalDays < 14) json = File.ReadAllText(file);
            if (json == null)
            {
                json = await Http.GetStringAsync("https://siren.pp.ua/api/v3/regions");
                try { File.WriteAllText(file, json); } catch { }
            }
            var map = new Dictionary<string, Node>();
            using var doc = JsonDocument.Parse(json);
            void Walk(JsonElement el, string parent)
            {
                var id = Integrations.S(el, "regionId");
                map[id] = new Node(id, Integrations.S(el, "regionName"), Integrations.S(el, "regionType"), parent);
                if (el.TryGetProperty("regionChildIds", out var ch) && ch.ValueKind == JsonValueKind.Array)
                    foreach (var c in ch.EnumerateArray()) Walk(c, id);
            }
            foreach (var st in doc.RootElement.GetProperty("states").EnumerateArray()) Walk(st, null);
            _regions = map;
            _regionsAt = DateTime.Now;
            return map;
        }

        /// <summary>Место из настроек → регион (точное совпадение важнее частичного, область важнее громады с похожим названием).</summary>
        static Node Find(Dictionary<string, Node> map, string place)
        {
            var p = Norm(place);
            if (p == "") return null;
            var exact = map.Values.FirstOrDefault(n => Norm(n.Name) == p);
            if (exact != null) return exact;
            int Rank(Node n) => n.Type switch { "State" => 0, "District" => 1, _ => 2 };
            return map.Values.Where(n => Norm(n.Name).Contains(p) || p.Contains(Norm(n.Name).Replace(" область", "")))
                             .OrderBy(Rank).ThenBy(n => n.Name.Length).FirstOrDefault();
        }

        static bool Ancestor(Dictionary<string, Node> map, string ancestor, string id)
        {
            for (var cur = id; cur != null && map.TryGetValue(cur, out var n); cur = n.Parent)
                if (cur == ancestor) return true;
            return false;
        }

        IEnumerable<string> Places() => new[] { V("oblast") }.Concat(Lines("places")).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).Distinct();

        public override async Task<string> Test()
        {
            await Poll();
            return "Работает ✓ " + Status + (V("token") == "" ? " · данные siren.pp.ua" : " · данные alerts.in.ua");
        }

        public override async Task Poll()
        {
            List<Active> active;
            Dictionary<string, Node> map = null;
            try
            {
                if (V("token") != "") active = await FromAlertsInUa();
                else { map = await Regions(); active = await FromSiren(); }
            }
            catch
            {
                Rows = new List<HubRow> { new() { Title = "Нет данных о тревогах", Sub = "нет связи с сервисом — статус неизвестен, ориентируйся на сирену и официальное приложение" } };
                throw;
            }
            bool threats = Flag("threats");
            var rows = new List<HubRow>();
            bool mineOn = false;
            int idx = 0;
            foreach (var place in Places())
            {
                bool mainPlace = idx++ == 0;
                List<Active> here, part = new();
                string title = place;
                if (map != null)
                {
                    var node = Find(map, place);
                    if (node == null) { rows.Add(new HubRow { Title = "Не нашёл «" + place + "»", Sub = "напиши как в списке регионов: «Харківська область», «Бучанський район», «м. Київ»" }); continue; }
                    title = node.Name;
                    here = active.Where(a => Ancestor(map, a.RegionId, node.Id)).ToList();                       // тревога на всё место или шире
                    part = active.Where(a => a.RegionId != node.Id && Ancestor(map, node.Id, a.RegionId)).ToList();  // тревога в части места
                }
                else
                {
                    var p = Norm(place).Replace(" область", "");
                    here = active.Where(a => Norm(a.Name).Contains(p)).ToList();
                }
                if (!threats) { here = here.Where(a => a.Type is "AIR" or "air_raid").ToList(); part = part.Where(a => a.Type is "AIR" or "air_raid").ToList(); }
                bool on = here.Count > 0;
                if (mainPlace) mineOn = on;

                var key = "on:" + Norm(title);
                var types = here.Select(a => TypeNames.TryGetValue(a.Type, out var tn) ? tn : a.Type).Distinct().ToList();
                var prev = St(key);
                var sig = on ? "1:" + string.Join(",", types) : "0";
                if (prev != null && prev != sig)
                {
                    if (on)
                    {
                        CountToday(title);
                        Notify((types.FirstOrDefault() ?? "Тривога") + (mainPlace ? "" : " · " + title), title + (types.Count > 1 ? " · " + string.Join(", ", types.Skip(1)) : ""), "https://map.ukrainealarm.com/", true);
                        if (mainPlace && Flag("pause")) System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() => MediaService.I.Pause()));
                    }
                    else if (prev.StartsWith("1") && Flag("clear"))
                    {
                        var since = St("since:" + Norm(title));
                        string dur = DateTime.TryParse(since, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var s0) ? " · тривала " + Dur(DateTime.Now - s0) : "";
                        Notify("Відбій тривоги" + (mainPlace ? "" : " · " + title), title + dur, null, true);
                    }
                }
                if (prev != sig) SetSt(key, sig);
                if (on && here.Min(a => a.Since) is var started && started > DateTime.MinValue) SetStQuiet("since:" + Norm(title), started.ToString("o"));

                int today = Today(title);
                if (on)
                {
                    var since = here.Min(a => a.Since);
                    rows.Add(new HubRow
                    {
                        Title = "🔴 " + string.Join(", ", types) + " · " + title,
                        Sub = (since > DateTime.MinValue ? "триває " + Dur(DateTime.Now - since) + " · з " + since.ToString("HH:mm") : "зараз") + (today > 0 ? $" · за добу: {today}" : ""),
                        Url = "https://map.ukrainealarm.com/",
                    });
                }
                else if (part.Count > 0)
                    rows.Add(new HubRow { Title = "🟠 Частково · " + title, Sub = string.Join(", ", part.Select(a => a.Name).Distinct().Take(3)) + (part.Count > 3 ? "…" : ""), Url = "https://map.ukrainealarm.com/" });
                else rows.Add(new HubRow { Title = "🟢 Тривоги немає · " + title, Sub = (today > 0 ? $"за добу тривог: {today} · " : "") + "оновлено " + DateTime.Now.ToString("HH:mm") });
            }
            Rows = rows;
            if (AlertNow != mineOn)
            {
                AlertNow = mineOn;
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() => App.Island?.RefreshCollapsed()));
            }
            Status = mineOn ? "Тривога!" : "Спокійно · " + DateTime.Now.ToString("HH:mm");
        }

        void SetStQuiet(string k, string v) { if (St(k) != v) Cfg.State[k] = v; }

        void CountToday(string place)
        {
            var k = "day:" + Norm(place);
            var today = DateTime.Today.ToString("yyyyMMdd");
            var cur = St(k)?.Split('|');
            int n = cur != null && cur.Length == 2 && cur[0] == today && int.TryParse(cur[1], out var c) ? c : 0;
            SetSt(k, today + "|" + (n + 1));
        }

        int Today(string place)
        {
            var cur = St("day:" + Norm(place))?.Split('|');
            return cur != null && cur.Length == 2 && cur[0] == DateTime.Today.ToString("yyyyMMdd") && int.TryParse(cur[1], out var c) ? c : 0;
        }

        static string Dur(TimeSpan t) => t.TotalMinutes < 60 ? $"{Math.Max(1, (int)t.TotalMinutes)} хв" : $"{(int)t.TotalHours} год {t.Minutes} хв";

        async Task<List<Active>> FromSiren()
        {
            var json = await Http.GetStringAsync("https://siren.pp.ua/api/v3/alerts");
            using var doc = JsonDocument.Parse(json);
            var res = new List<Active>();
            foreach (var r in doc.RootElement.EnumerateArray())
            {
                var id = Integrations.S(r, "regionId");
                var name = Integrations.S(r, "regionName");
                if (!r.TryGetProperty("activeAlerts", out var aa)) continue;
                foreach (var a in aa.EnumerateArray())
                {
                    DateTime.TryParse(Integrations.S(a, "lastUpdate"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t);
                    res.Add(new Active(id, name, Integrations.S(a, "type"), t == default ? DateTime.MinValue : t.ToLocalTime()));
                }
            }
            return res;
        }

        async Task<List<Active>> FromAlertsInUa()
        {
            using var doc = await Integrations.GetJson("https://api.alerts.in.ua/v1/alerts/active.json", r => r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", V("token")));
            var res = new List<Active>();
            foreach (var a in doc.RootElement.GetProperty("alerts").EnumerateArray())
            {
                DateTime.TryParse(Integrations.S(a, "started_at"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t);
                var name = Integrations.S(a, "location_title") + " / " + Integrations.S(a, "location_oblast");
                res.Add(new Active("", name, Integrations.S(a, "alert_type"), t == default ? DateTime.MinValue : t.ToLocalTime()));
            }
            return res;
        }
    }

    // =====================================================================
    //  Нова пошта: статус, срок хранения, сумма к оплате, ТТН из буфера обмена
    // =====================================================================

    public class NovaPoshtaHub : Integration
    {
        public override string Key => "novaposhta";
        public override string Name => "Нова пошта";
        public override string Glyph => "";
        public override string Color => "#DA291C";
        public override string Desc => "Следит за посылками: сообщает, когда посылка прибыла в отделение или почтомат, сколько платить при получении и с какого дня хранение станет платным. " +
                                       "Скопировал номер ТТН — остров предложит его отслеживать";
        public override string Help => "Номера ТТН по одному в строке. Через пробел — телефон получателя или отправителя (тогда видно отделение, сумму и сроки) и своё название: " +
                                       "«20450000000000 0671234567 Кроссовки». API-ключ не обязателен";
        public override string HelpUrl => "https://novaposhta.ua/tracking/";
        public override HubField[] Fields => new[]
        {
            new HubField("ttn", "Номера ТТН", "20400048799000 0671234567 Кроссовки", "multi"),
            new HubField("phone", "Мой телефон", "0671234567 — подставится ко всем ТТН", Required: false),
            new HubField("key", "API-ключ", "необязательно", "secret", Required: false),
            new HubField("clipboard", "Предлагать ТТН из буфера обмена", "", "bool", Required: false, Default: "1"),
            new HubField("hide", "Прятать полученные через 3 дня", "", "bool", Required: false, Default: "1"),
        };
        public override string[] DefaultSend => new[] { "parcel" };
        public override int PollSeconds => 900;

        record Ttn(string Number, string Phone, string Label);

        IEnumerable<Ttn> Items() => (V("ttn") ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Where(p => p.Length > 0 && Regex.IsMatch(p[0], @"^\d{11,20}$"))
            .Select(p =>
            {
                string phone = p.Length > 1 && Regex.IsMatch(p[1], @"^\+?\d{9,13}$") ? p[1] : V("phone");
                int labelFrom = p.Length > 1 && Regex.IsMatch(p[1], @"^\+?\d{9,13}$") ? 2 : 1;
                return new Ttn(p[0], Regex.Replace(phone ?? "", @"\D", ""), string.Join(" ", p.Skip(labelFrom)));
            }).Take(30);

        static string Money(string s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v > 0 ? v.ToString("0.##", CultureInfo.InvariantCulture) + " ₴" : null;

        public override async Task Poll()
        {
            var list = Items().ToList();
            if (list.Count == 0) { Rows = new(); Status = "Нет посылок"; return; }
            using var doc = await Integrations.PostJson("https://api.novaposhta.ua/v2.0/json/", new
            {
                apiKey = V("key"), modelName = "TrackingDocument", calledMethod = "getStatusDocuments",
                methodProperties = new { Documents = list.Select(t => new { DocumentNumber = t.Number, Phone = t.Phone }).ToArray() },
            });
            if (doc.RootElement.TryGetProperty("success", out var ok) && ok.ValueKind == JsonValueKind.False && doc.RootElement.TryGetProperty("errors", out var errs) && errs.GetArrayLength() > 0)
                throw new InvalidOperationException(string.Join("; ", errs.EnumerateArray().Select(e => e.ToString())));
            var rows = new List<HubRow>();
            int waiting = 0;
            foreach (var d in doc.RootElement.GetProperty("data").EnumerateArray())
            {
                var num = Integrations.S(d, "Number");
                var t = list.FirstOrDefault(x => x.Number == num);
                var status = Integrations.S(d, "Status");
                int.TryParse(Integrations.S(d, "StatusCode"), out var code);
                string S(string k) => Integrations.S(d, k);
                bool arrived = code is 7 or 8, received = code is 9 or 10 or 11, refused = code is 102 or 103 or 108 or 105;
                if (arrived) waiting++;

                var parts = new List<string>();
                if (S("WarehouseRecipient") is { Length: > 0 } wh) parts.Add(wh);
                else if (S("CityRecipient") is { Length: > 0 } city) parts.Add(city);
                if (!received && S("ScheduledDeliveryDate") is { Length: > 0 } eta) parts.Add("очікується " + ShortDate(eta));
                var pay = new[] { S("PayerType") == "Recipient" ? Money(S("DocumentCost")) : null, Money(S("AfterpaymentOnGoodsCost")) }.Where(x => x != null).ToList();
                if (pay.Count > 0 && !received) parts.Add("до сплати " + string.Join(" + ", pay));
                if (arrived && S("DatePayedKeeping") is { Length: > 0 } keep) parts.Add("безкоштовно до " + ShortDate(keep));
                if (received && S("RecipientDateTime") is { Length: > 0 } got) parts.Add("отримано " + ShortDate(got));
                if (S("CargoDescriptionString") is { Length: > 0 } cargo && t?.Label == "") parts.Add(cargo);

                // полученные несколько дней назад прячем, чтобы список не разрастался
                if (received && Flag("hide") && DateTime.TryParse(S("RecipientDateTime"), CultureInfo.GetCultureInfo("uk-UA"), DateTimeStyles.None, out var gotAt) && (DateTime.Now - gotAt).TotalDays > 3) continue;

                var number = num;
                rows.Add(new HubRow
                {
                    Title = (arrived ? "📦 " : received ? "✓ " : refused ? "↩ " : "🚚 ") + (string.IsNullOrEmpty(t?.Label) ? num : t.Label + " · " + num[^4..]) + " — " + status,
                    Sub = string.Join(" · ", parts), Url = "https://novaposhta.ua/tracking/?cargo_number=" + num,
                    ActionText = "Убрать", Action = () => { Remove(number); return Task.FromResult("Убрано из списка"); },
                });

                var key = "st:" + num;
                var prev = St(key);
                if (prev != null && prev != code + "|" + status)
                {
                    var title = (string.IsNullOrEmpty(t?.Label) ? "Нова пошта · " + num : t.Label) + (arrived ? " — прибула!" : "");
                    Notify(title, status + (parts.Count > 0 ? " · " + string.Join(" · ", parts.Take(3)) : ""), "https://novaposhta.ua/tracking/?cargo_number=" + num);
                    if (arrived) System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() => Sound.Play(SoundEvent.Reminder)));
                    Integrations.Publish("parcel", title, status + (parts.Count > 0 ? "\n" + string.Join("\n", parts) : ""));
                }
                if (prev != code + "|" + status) SetSt(key, code + "|" + status);
            }
            Rows = rows;
            Status = waiting > 0 ? $"Чекає в відділенні: {waiting}" : "Оновлено " + DateTime.Now.ToString("HH:mm");
        }

        static string ShortDate(string s) =>
            DateTime.TryParse(s, CultureInfo.GetCultureInfo("uk-UA"), DateTimeStyles.None, out var d) || DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out d)
                ? d.ToString(d.TimeOfDay == TimeSpan.Zero ? "d MMM" : "d MMM HH:mm", Fmt.Ru) : s;

        void Remove(string number)
        {
            var keep = (V("ttn") ?? "").Split('\n').Where(l => !l.TrimStart().StartsWith(number)).ToList();
            Integrations.SetValue(this, "ttn", string.Join("\n", keep).Trim());
            Rows.RemoveAll(r => r.Url?.EndsWith(number) == true);
            Integrations.RaiseChanged();
        }

        public void Add(string number)
        {
            if (Items().Any(t => t.Number == number)) return;
            Integrations.SetValue(this, "ttn", ((V("ttn") ?? "").Trim() + "\n" + number).Trim());
            if (!Enabled) Integrations.SetEnabled(this, true);
            _ = Integrations.PollOne(this);
        }

        static readonly Regex TtnRx = new(@"^\s*(20|59|1)\d{12,13}\s*$");

        /// <summary>Скопировали номер ТТН — предлагаем отслеживать (один раз на номер).</summary>
        public static void OnCopied(string text)
        {
            if (text == null || !TtnRx.IsMatch(text)) return;
            if (Integrations.Get("novaposhta") is not NovaPoshtaHub np || (np.Enabled && !np.Flag("clipboard"))) return;
            var num = text.Trim();
            if (np.Items().Any(t => t.Number == num)) return;
            App.Island?.ShowActivity(new Activity
            {
                Kind = "hub", Glyph = np.Glyph, Tint = ColorUtil.Parse(np.Color, System.Windows.Media.Colors.Red), Title = "Відстежувати посилку?", Sub = "ТТН " + num,
                Seconds = 8, Wide = true, ActionText = "Відстежувати", Action = () => { np.Add(num); App.Island?.Toast(np.Glyph, "Посилка додана", "ТТН " + num); },
            });
        }
    }
}
