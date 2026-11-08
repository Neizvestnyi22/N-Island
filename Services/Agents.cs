using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using NIsland.Core;

namespace NIsland.Services
{
    public record AgentKind(string Key, string Name, string Command, string Install, string Site, string OneShot);
    public record AgentFound(AgentKind Kind, string Path, string Version);
    public record AgentProcess(int Pid, string Agent, string CommandLine, DateTime Started);
    public record AgentEvent(DateTime At, string Agent, string Kind, string Message, string Folder);

    /// <summary>
    /// ИИ-агенты для программирования на этом ПК (Claude Code, Codex, Gemini CLI…): найти, запустить в папке проекта,
    /// узнать, что агент ждёт подтверждения или закончил (хуки Claude Code и notify в Codex → остров и Telegram), расход токенов.
    /// </summary>
    public static class Agents
    {
        public static readonly AgentKind[] Known =
        {
            new("claude", "Claude Code", "claude", "irm https://claude.ai/install.ps1 | iex", "https://claude.com/claude-code", "-p"),
            new("codex", "Codex CLI", "codex", "npm i -g @openai/codex", "https://github.com/openai/codex", "exec"),
            new("gemini", "Gemini CLI", "gemini", "npm i -g @google/gemini-cli", "https://github.com/google-gemini/gemini-cli", "-p"),
            new("qwen", "Qwen Code", "qwen", "npm i -g @qwen-code/qwen-code", "https://github.com/QwenLM/qwen-code", "-p"),
            new("opencode", "OpenCode", "opencode", "npm i -g opencode-ai", "https://opencode.ai", "run"),
            new("copilot", "GitHub Copilot CLI", "copilot", "npm i -g @github/copilot", "https://github.com/github/copilot-cli", "-p"),
            new("aider", "Aider", "aider", "python -m pip install aider-install && aider-install", "https://aider.chat", "--message"),
            new("cursor", "Cursor Agent", "cursor-agent", null, "https://cursor.com/cli", "-p"),
        };

        /// <summary>Самопроверка: подменить домашнюю папку, чтобы не трогать настоящие настройки агентов.</summary>
        internal static string HomeOverride;
        static string Home => HomeOverride ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        internal static AgentEvent ParseForTest(string agent, string json) => Parse(agent, json);

        // ------------------------------------------------------------------
        //  Поиск установленных
        // ------------------------------------------------------------------

        static string Which(string cmd)
        {
            var exts = new[] { ".exe", ".cmd", ".bat", ".ps1", "" };
            var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Concat(new[] { Path.Combine(Home, ".local", "bin"), Path.Combine(Home, "AppData", "Roaming", "npm"), Path.Combine(Home, ".claude", "local") });
            foreach (var d in dirs.Distinct(StringComparer.OrdinalIgnoreCase))
                foreach (var e in exts)
                {
                    try { var p = Path.Combine(d.Trim('"'), cmd + e); if (File.Exists(p)) return p; } catch { }
                }
            return cmd == "claude" ? ClaudeFromDesktopApp() : null;
        }

        /// <summary>Claude Code, который приносит с собой приложение Claude: %LOCALAPPDATA%\Claude-Data\*\claude-code\<версия>\…\claude.exe — берём самую новую версию.</summary>
        static string ClaudeFromDesktopApp()
        {
            try
            {
                var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Claude-Data");
                if (!Directory.Exists(root)) return null;
                return Directory.EnumerateDirectories(root)
                    .Select(d => Path.Combine(d, "claude-code")).Where(Directory.Exists)
                    .SelectMany(Directory.EnumerateDirectories)
                    .OrderByDescending(d => Version.TryParse(Path.GetFileName(d), out var v) ? v : new Version(0, 0))
                    .Select(d => Directory.EnumerateFiles(d, "claude.exe", SearchOption.AllDirectories).FirstOrDefault())
                    .FirstOrDefault(f => f != null);
            }
            catch { return null; }
        }

        static string Run(string file, string args, int ms = 6000, string dir = null)
        {
            try
            {
                var psi = new ProcessStartInfo(file.EndsWith(".cmd") || file.EndsWith(".bat") ? "cmd.exe" : file,
                    file.EndsWith(".cmd") || file.EndsWith(".bat") ? $"/d /c \"\"{file}\" {args}\"" : args)
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8 };
                if (dir != null) psi.WorkingDirectory = dir;
                using var p = Process.Start(psi);
                var o = p.StandardOutput.ReadToEndAsync();
                _ = p.StandardError.ReadToEndAsync();   // иначе полный буфер stderr остановит процесс
                if (!p.WaitForExit(ms)) { try { p.Kill(true); } catch { } return null; }
                // дочерний процесс (GUI-программа, node из .cmd-обёртки) может держать вывод открытым и после выхода родителя
                return o.Wait(1500) ? o.Result.Trim() : null;
            }
            catch { return null; }
        }

        public static Task<List<AgentFound>> Detect() => Task.Run(() =>
        {
            var list = new List<AgentFound>();
            foreach (var k in Known)
            {
                var path = Which(k.Command);
                if (path == null) continue;
                var ver = Run(path, "--version")?.Split('\n').FirstOrDefault()?.Trim();
                list.Add(new AgentFound(k, path, string.IsNullOrEmpty(ver) ? "?" : ver.Length > 60 ? ver[..60] : ver));
            }
            return list;
        });

        // ------------------------------------------------------------------
        //  Запущенные сессии
        // ------------------------------------------------------------------

        public static Task<List<AgentProcess>> Running() => Task.Run(() =>
        {
            var res = new List<AgentProcess>();
            try
            {
                using var mos = new System.Management.ManagementObjectSearcher(
                    "SELECT ProcessId, Name, CommandLine, CreationDate FROM Win32_Process WHERE Name='claude.exe' OR Name='codex.exe' OR Name='cursor-agent.exe' OR Name='opencode.exe' OR Name='node.exe' OR Name='python.exe' OR Name='bun.exe'");
                foreach (System.Management.ManagementObject o in mos.Get())
                {
                    var name = (o["Name"] as string ?? "").ToLowerInvariant();
                    var cmd = o["CommandLine"] as string ?? "";
                    var low = cmd.ToLowerInvariant();
                    string agent = name switch
                    {
                        "claude.exe" => "Claude Code", "codex.exe" => "Codex CLI", "cursor-agent.exe" => "Cursor Agent", "opencode.exe" => "OpenCode",
                        _ => low.Contains("@anthropic-ai/claude-code") || low.Contains("claude-code") ? "Claude Code"
                           : low.Contains("@openai/codex") ? "Codex CLI"
                           : low.Contains("gemini-cli") || low.Contains("@google/gemini") ? "Gemini CLI"
                           : low.Contains("qwen-code") ? "Qwen Code"
                           : low.Contains("@github/copilot") ? "GitHub Copilot CLI"
                           : low.Contains("aider") ? "Aider" : null,
                    };
                    if (agent == null) continue;
                    // служебные копии (MCP-серверы, хуки) и процессы приложения Claude для ПК (Electron) — не сессии агента
                    if (low.Contains(" mcp ") || low.Contains("--agent ") || low.Contains("--type=") || (low.Contains("\\windowsapps\\claude_") && !low.Contains("claude-code"))) continue;
                    if (low.Contains("\\claude-code\\") && name == "claude.exe") agent = "Claude Code (в приложении Claude)";
                    DateTime started = DateTime.MinValue;
                    try { started = System.Management.ManagementDateTimeConverter.ToDateTime(o["CreationDate"] as string); } catch { }
                    res.Add(new AgentProcess(Convert.ToInt32(o["ProcessId"]), agent, cmd.Length > 160 ? cmd[..160] + "…" : cmd, started));
                }
            }
            catch (Exception e) { Log.Info("agents running: " + e.Message); }
            // у Codex и Claude по два процесса (обёртка + сам агент) — оставляем самый ранний на агента и время
            return res.GroupBy(r => r.Agent + r.Started.ToString("HHmmss")).Select(g => g.First()).OrderByDescending(r => r.Started).ToList();
        });

        public static string Stop(int pid)
        {
            try { using var p = Process.GetProcessById(pid); p.Kill(true); return null; }
            catch (Exception e) { return e.Message; }
        }

        // ------------------------------------------------------------------
        //  Запуск
        // ------------------------------------------------------------------

        static string Quote(string s) => "\"" + (s ?? "").Replace("\"", "\\\"") + "\"";

        /// <summary>Открыть агента в терминале в папке проекта (с заданием или без).</summary>
        public static string Launch(AgentFound a, string folder, string prompt)
        {
            if (!Directory.Exists(folder)) return "Папка не найдена: " + folder;
            Remember(folder);
            // полный путь: агента может не быть в PATH (например, Claude Code внутри приложения Claude)
            var cmd = Quote(a.Path) + (string.IsNullOrWhiteSpace(prompt) ? "" : " " + Quote(prompt.Trim()));
            try
            {
                var wt = Which("wt");
                var psi = wt != null
                    ? new ProcessStartInfo(wt, $"-d {Quote(folder)} --title {Quote(a.Kind.Name + " · " + Path.GetFileName(folder.TrimEnd('\\')))} cmd /s /k \"{cmd}\"") { UseShellExecute = true }
                    : new ProcessStartInfo("cmd.exe", $"/s /k \"{cmd}\"") { UseShellExecute = true, WorkingDirectory = folder };
                Process.Start(psi)?.Dispose();
                return null;
            }
            catch (Exception e) { return e.Message; }
        }

        /// <summary>Открыть терминал с командой установки или обновления (пользователь видит, что выполняется).</summary>
        public static void OpenTerminal(string command, string folder = null)
        {
            var wt = Which("wt");
            var psi = command.StartsWith("irm ")
                ? new ProcessStartInfo("powershell.exe", $"-NoExit -Command {command}") { UseShellExecute = true }
                // /s + внешние кавычки: cmd снимет только их, а кавычки внутри команды (путь, заголовок) останутся целыми
                : wt != null ? new ProcessStartInfo(wt, (folder != null ? $"-d {Quote(folder)} " : "") + "cmd /s /k \"" + command + "\"") { UseShellExecute = true }
                             : new ProcessStartInfo("cmd.exe", "/s /k \"" + command + "\"") { UseShellExecute = true, WorkingDirectory = folder ?? Home };
            Process.Start(psi)?.Dispose();
        }

        /// <summary>Как вызвать Claude Code в терминале: «claude», если он в PATH, иначе полный путь в кавычках.</summary>
        public static string ClaudeCommand()
        {
            var p = Which("claude");
            if (p == null) return "claude";
            var dir = Path.GetDirectoryName(p);
            bool inPath = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';').Any(d => string.Equals(d.Trim('"').TrimEnd('\\'), dir, StringComparison.OrdinalIgnoreCase));
            return inPath ? "claude" : "\"" + p + "\"";
        }

        /// <summary>
        /// Задание без интерфейса (claude -p, codex exec…): ответ агента текстом. Для Telegram.
        /// Агент работает в папке проекта с теми разрешениями, что настроены у него самого.
        /// </summary>
        public static async Task<string> Headless(AgentFound a, string folder, string prompt, TimeSpan timeout, CancellationToken ct = default)
        {
            if (!Directory.Exists(folder)) return "Папка не найдена: " + folder;
            if (a.Kind.OneShot == null) return a.Kind.Name + " не умеет выполнять задание без окна";
            Remember(folder);
            var file = a.Path;
            bool script = file.EndsWith(".cmd") || file.EndsWith(".bat");
            var args = a.Kind.OneShot + " " + Quote(prompt);
            var psi = new ProcessStartInfo(script ? "cmd.exe" : file, script ? $"/d /c \"\"{file}\" {args}\"" : args)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
                WorkingDirectory = folder, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            };
            using var p = Process.Start(psi);
            p.StandardInput.Close();
            var o = p.StandardOutput.ReadToEndAsync();
            var e = p.StandardError.ReadToEndAsync();
            var done = p.WaitForExitAsync(ct);
            if (await Task.WhenAny(done, Task.Delay(timeout, ct)) != done) { try { p.Kill(true); } catch { } return "⏱ Агент не уложился в " + (int)timeout.TotalMinutes + " мин и остановлен.\n" + await o; }
            var text = (await o).Trim();
            var err = (await e).Trim();
            return text != "" ? text : err != "" ? "⚠ " + err : "(агент ничего не ответил)";
        }

        public static void Remember(string folder)
        {
            var list = (Store.Settings.AgentFolders ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries).ToList();
            list.RemoveAll(f => string.Equals(f, folder, StringComparison.OrdinalIgnoreCase));
            list.Insert(0, folder);
            Store.Settings.AgentFolders = string.Join("|", list.Take(10));
        }

        public static List<string> Folders => (Store.Settings.AgentFolders ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries).Where(Directory.Exists).ToList();

        // ------------------------------------------------------------------
        //  Уведомления: хуки Claude Code и notify в Codex
        // ------------------------------------------------------------------

        static string ExeForHooks => (Environment.ProcessPath ?? "NIsland.exe").Replace('\\', '/');
        static string ClaudeSettings => Path.Combine(Home, ".claude", "settings.json");
        static string CodexConfig => Path.Combine(Home, ".codex", "config.toml");

        static string HookCommand(string agent) => $"\"{ExeForHooks}\" --agent {agent}";

        public static bool ClaudeHooked()
        {
            try { return File.Exists(ClaudeSettings) && File.ReadAllText(ClaudeSettings).Contains("--agent claude"); } catch { return false; }
        }

        public static bool CodexHooked()
        {
            try { return File.Exists(CodexConfig) && File.ReadAllText(CodexConfig).Contains("\"--agent\", \"codex\""); } catch { return false; }
        }

        /// <summary>Добавить в настройки Claude Code хуки Notification (нужно подтверждение) и Stop (закончил). Остальные хуки не трогаем.</summary>
        public static string HookClaude(bool on)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ClaudeSettings));
                JsonObject root = File.Exists(ClaudeSettings) ? JsonNode.Parse(File.ReadAllText(ClaudeSettings), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject ?? new() : new();
                if (File.Exists(ClaudeSettings) && !File.Exists(ClaudeSettings + ".nisland-backup")) File.Copy(ClaudeSettings, ClaudeSettings + ".nisland-backup");
                var hooks = root["hooks"] as JsonObject;
                if (hooks == null) { hooks = new JsonObject(); root["hooks"] = hooks; }
                foreach (var ev in new[] { "Notification", "Stop" })
                {
                    var arr = hooks[ev] as JsonArray ?? new JsonArray();
                    // убираем наши прежние записи (путь к программе мог поменяться)
                    for (int i = arr.Count - 1; i >= 0; i--)
                        if (arr[i]?.ToJsonString().Contains("--agent claude") == true) arr.RemoveAt(i);
                    if (on)
                        arr.Add(new JsonObject
                        {
                            ["hooks"] = new JsonArray(new JsonObject { ["type"] = "command", ["command"] = HookCommand("claude"), ["timeout"] = 10 }),
                        });
                    if (arr.Count > 0) hooks[ev] = arr; else hooks.Remove(ev);
                }
                if (hooks.Count == 0) root.Remove("hooks");
                File.WriteAllText(ClaudeSettings, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
                return null;
            }
            catch (Exception e) { return "Не удалось изменить настройки Claude Code: " + e.Message; }
        }

        /// <summary>Codex вызывает программу из notify с JSON последним аргументом. Если у пользователя уже есть свой notify — не перезаписываем.</summary>
        public static string HookCodex(bool on)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(CodexConfig));
                var lines = File.Exists(CodexConfig) ? File.ReadAllLines(CodexConfig).ToList() : new List<string>();
                if (File.Exists(CodexConfig) && !File.Exists(CodexConfig + ".nisland-backup")) File.Copy(CodexConfig, CodexConfig + ".nisland-backup");
                int ours = lines.FindIndex(l => l.TrimStart().StartsWith("notify") && l.Contains("\"--agent\", \"codex\""));
                if (!on) { if (ours >= 0) lines.RemoveAt(ours); }
                else
                {
                    int other = lines.FindIndex(l => l.TrimStart().StartsWith("notify") && l.Contains('='));
                    if (other >= 0 && other != ours) return "В настройках Codex уже есть свой notify — его не трогаю. Убери его, если хочешь уведомления от N-Island";
                    var line = $"notify = [\"{ExeForHooks}\", \"--agent\", \"codex\"]";
                    // notify — ключ верхнего уровня: до первой таблицы [..]
                    int firstTable = lines.FindIndex(l => l.TrimStart().StartsWith("["));
                    if (ours >= 0) lines[ours] = line;
                    else if (firstTable >= 0) lines.Insert(firstTable, line);
                    else lines.Add(line);
                }
                File.WriteAllLines(CodexConfig, lines);
                return null;
            }
            catch (Exception e) { return "Не удалось изменить настройки Codex: " + e.Message; }
        }

        // ------------------------------------------------------------------
        //  События от агентов: вторая копия программы кладёт файл и будит основную
        // ------------------------------------------------------------------

        public static string QueueDir => Path.Combine(Store.Dir, "agent-events");
        public static string WakeName => Store.Inst("NIsland.AgentEvent.v1");

        /// <summary>Вызывается из хука (NIsland.exe --agent claude|codex): быстро сохранить событие и выйти.</summary>
        public static void Enqueue(string[] args)
        {
            try
            {
                string agent = args.Length > 1 ? args[1] : "agent";
                string payload = args.Length > 2 ? args[^1] : null;   // Codex передаёт JSON последним аргументом
                if (payload == null && Console.IsInputRedirected)
                {
                    // Claude Code передаёт JSON во входной поток
                    var read = Task.Run(() => Console.In.ReadToEnd());
                    payload = read.Wait(1500) ? read.Result : null;
                }
                Directory.CreateDirectory(QueueDir);
                File.WriteAllText(Path.Combine(QueueDir, $"{DateTime.Now.Ticks}-{agent}.json"), payload ?? "{}");
                try { EventWaitHandle.OpenExisting(WakeName).Set(); } catch { }
            }
            catch { }
        }

        public static event Action<AgentEvent> EventArrived;
        static EventWaitHandle _wake;

        /// <summary>Основная копия: ждём сигнал и разбираем очередь.</summary>
        public static void Listen()
        {
            if (_wake != null) return;
            _wake = new EventWaitHandle(false, EventResetMode.AutoReset, WakeName);
            new Thread(() =>
            {
                Drain(onlyFresh: true);
                while (_wake.WaitOne()) Drain(false);
            }) { IsBackground = true, Name = "agent-events" }.Start();
        }

        static void Drain(bool onlyFresh)
        {
            try
            {
                if (!Directory.Exists(QueueDir)) return;
                foreach (var f in Directory.GetFiles(QueueDir, "*.json").OrderBy(x => x))
                {
                    try
                    {
                        var fresh = (DateTime.Now - File.GetLastWriteTime(f)).TotalMinutes < 3;
                        var text = File.ReadAllText(f);
                        File.Delete(f);
                        if (onlyFresh && !fresh) continue;
                        var agent = Path.GetFileNameWithoutExtension(f).Split('-').Last();
                        var ev = Parse(agent, text);
                        if (ev != null) EventArrived?.Invoke(ev);
                    }
                    catch (Exception e) { Log.Info("agent event: " + e.Message); }
                }
            }
            catch { }
        }

        static AgentEvent Parse(string agent, string json)
        {
            JsonDocument doc;
            try { doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json); } catch { return null; }
            using (doc)
            {
                var r = doc.RootElement;
                string S(string k) => r.ValueKind == JsonValueKind.Object && r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                var cwd = S("cwd") ?? "";
                if (agent == "claude")
                {
                    var hook = S("hook_event_name") ?? "";
                    var msg = S("message") ?? "";
                    if (hook == "Stop") return new AgentEvent(DateTime.Now, "Claude Code", "done", "Задача выполнена — проверь результат", cwd);
                    if (msg.Contains("waiting for your input", StringComparison.OrdinalIgnoreCase) || msg.Contains("idle", StringComparison.OrdinalIgnoreCase))
                        return new AgentEvent(DateTime.Now, "Claude Code", "idle", "Ждёт твоего ответа", cwd);
                    return new AgentEvent(DateTime.Now, "Claude Code", "permission", msg != "" ? msg : "Нужно подтверждение", cwd);
                }
                if (agent == "codex")
                {
                    var type = S("type") ?? "";
                    var last = S("last-assistant-message") ?? "";
                    if (type.Contains("complete")) return new AgentEvent(DateTime.Now, "Codex CLI", "done", last.Length > 200 ? last[..200] + "…" : last == "" ? "Задача выполнена" : last, cwd);
                    return new AgentEvent(DateTime.Now, "Codex CLI", "info", type, cwd);
                }
                return new AgentEvent(DateTime.Now, agent, "info", S("message") ?? "Событие", cwd);
            }
        }

        // ------------------------------------------------------------------
        //  Токены Claude Code за сегодня (по журналам сессий)
        // ------------------------------------------------------------------

        public static Task<(long In, long Out, long Cache, int Sessions)> ClaudeUsageToday() => Task.Run(() =>
        {
            long tin = 0, tout = 0, tcache = 0;
            int sessions = 0;
            var root = Path.Combine(Home, ".claude", "projects");
            if (!Directory.Exists(root)) return (0L, 0L, 0L, 0);
            var today = DateTime.Today;
            foreach (var f in Directory.EnumerateFiles(root, "*.jsonl", SearchOption.AllDirectories))
            {
                try
                {
                    if (File.GetLastWriteTime(f) < today) continue;
                    sessions++;
                    using var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var sr = new StreamReader(fs);
                    string line;
                    while ((line = sr.ReadLine()) != null)
                    {
                        if (!line.Contains("\"usage\"")) continue;
                        using var d = JsonDocument.Parse(line);
                        var r = d.RootElement;
                        if (r.TryGetProperty("timestamp", out var ts) && DateTime.TryParse(ts.GetString(), out var t) && t.ToLocalTime() < today) continue;
                        if (!r.TryGetProperty("message", out var m) || m.ValueKind != JsonValueKind.Object || !m.TryGetProperty("usage", out var u)) continue;
                        long L(string k) => u.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0;
                        tin += L("input_tokens") + L("cache_creation_input_tokens");
                        tout += L("output_tokens");
                        tcache += L("cache_read_input_tokens");
                    }
                }
                catch { }
            }
            return (tin, tout, tcache, sessions);
        });

        public static string Tokens(long n) => n >= 1_000_000 ? (n / 1_000_000.0).ToString("0.0") + " млн" : n >= 1000 ? (n / 1000.0).ToString("0") + " тыс." : n.ToString();
    }
}
