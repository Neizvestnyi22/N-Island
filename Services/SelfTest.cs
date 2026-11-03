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
            try
            {
                return args[1] switch
                {
                    "lyrics" => Task.Run(() => Lyrics(args[2], args[3])).GetAwaiter().GetResult(),
                    "hub" => Task.Run(() => Hub(args[2])).GetAwaiter().GetResult(),
                    "dedupe" => DedupeTest(args[2]),
                    "net" => Task.Run(() => NetTest(args[2])).GetAwaiter().GetResult(),
                    _ => 2,
                };
            }
            catch (Exception e) { File.WriteAllText(args[^1], e.ToString()); return 1; }
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
            Cfg("alerts", ("token", "wrong"), ("oblast", "Миколаївська область"));
            Cfg("discord", ("url", "https://discord.com/api/webhooks/0/wrong"));
            {
                var n = Integrations.Get("ntfy");
                var sw0 = Stopwatch.StartNew();
                try { await n.Send(new HubEvent("test", "N-Island подключён ✓", "Проверка")); sb.AppendLine($"ntfy send {sw0.ElapsedMilliseconds} ms"); }
                catch (Exception e) { sb.AppendLine($"ntfy send FAIL {sw0.ElapsedMilliseconds} ms {e.GetType().Name}: {e.Message} {e.InnerException?.Message}"); }
                sw0.Restart();
                try { await n.Poll(); sb.AppendLine($"ntfy poll {sw0.ElapsedMilliseconds} ms"); }
                catch (Exception e) { sb.AppendLine($"ntfy poll FAIL {sw0.ElapsedMilliseconds} ms {e.GetType().Name}: {e.Message} {e.InnerException?.Message}"); }
            }
            foreach (var key in new[] { "ntfy", "rates", "novaposhta", "uptime", "rss", "ics", "telegram", "github", "todoist", "listenbrainz", "ha", "alerts", "discord" })
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
