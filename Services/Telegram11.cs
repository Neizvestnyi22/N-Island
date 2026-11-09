using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NIsland.Core;

namespace NIsland.Services
{
    // =====================================================================
    //  Защитник ПК в Telegram: тревога с кнопками «В карантин» / «Доверять», результаты проверки с карантином,
    //  защита в реальном времени, обновление базы, список карантина и проверка встроенным Защитником Windows.
    // =====================================================================
    public partial class TelegramHub
    {
        /// <summary>Тревога из защиты в реальном времени. true — ушла в Telegram с кнопками (обычное событие «тревога» сюда не дублируем).</summary>
        public bool ThreatAlert(Threat t)
        {
            if (Remote != null || !Enabled || !Ready || string.IsNullOrEmpty(V("chat"))) return false;
            _ = Task.Run(async () =>
            {
                try
                {
                    var name = Path.GetFileName(t.Path);
                    await Message((t.Score >= 70 ? "🔴 <b>Опасный файл</b>: " : "🟡 <b>Подозрительный файл</b>: ") + H(name) + $" · {t.Score}/100\n<code>{H(Cut(t.Path, 200))}</code>\n" +
                                  string.Join("\n", t.Reasons.Take(4).Select(r => "• " + H(r))),
                        K(Row(B("🧊 В карантин", "av2:q:" + Put(t.Path)), B("✓ Доверять", "av2:ok:" + Put(t.Sha256 + "|" + t.Path)))));
                }
                catch (Exception e) { Log.Info("telegram threat: " + e.Message); }
            });
            return true;
        }

        /// <summary>Итог проверки: что найдено, с кнопкой «В карантин» у каждой находки.</summary>
        async Task ReportScan(List<Threat> found, string title)
        {
            var s = Store.Settings;
            if (found.Count == 0) { await Message($"🛡 {T(title)}: {T("угроз не найдено")} ✓ · {T("проверено файлов")}: {s.AvLastCount}"); return; }
            var rows = found.Take(6).Select(t => Row(B("🧊 " + Cut(Path.GetFileName(t.Path), 22), "av2:q:" + Put(t.Path)), B("✓", "av2:ok:" + Put(t.Sha256 + "|" + t.Path)))).ToList();
            rows.Add(Row(B("🗂 Карантин", "av2:ql")));
            await Message($"🛡 <b>{T(title)}: {T("найдено подозрительных")} {found.Count}</b>\n" +
                          string.Join("\n", found.Take(10).Select(t => $"{(t.Score >= 70 ? "🔴" : "🟡")} {t.Score} · <code>{H(Cut(t.Path, 90))}</code>\n   <i>{H(t.Verdict)}</i>")) +
                          "\n\n" + T("🧊 — убрать файл в карантин (его можно вернуть), ✓ — доверять и больше не трогать."), K(rows.ToArray()));
        }

        static string _avConfirm;
        static DateTime _avConfirmAt;

        /// <summary>Кнопки защитника в боте (av2:…). Проверка и аудит остались в Do2.</summary>
        async Task<string> Av2(string rest, long msgId)
        {
            string no;
            switch (rest)
            {
                case var q when q.StartsWith("q:"):
                {
                    if ((no = Need("control")) != null) return no;
                    var path = Get(q[2..]);
                    if (path == null) return Stale;
                    if (!File.Exists(path)) return T("Файла уже нет");
                    var e = await Task.Run(() => Antivirus.Quarantine(path, T("из Telegram")));
                    return e ?? "🧊 " + T("В карантине") + ": " + Path.GetFileName(path);
                }
                case var o when o.StartsWith("ok:"):
                {
                    if ((no = Need("control")) != null) return no;
                    var v = Get(o[3..]);
                    if (v == null || !v.Contains('|')) return Stale;
                    Antivirus.Allow(v[..v.IndexOf('|')]);
                    return "✓ " + T("Доверяю, больше не трогаю") + ": " + Path.GetFileName(v[(v.IndexOf('|') + 1)..]);
                }
                case "rt":
                {
                    if ((no = Need("control")) != null) return no;
                    bool on = !Store.Settings.AvRealtime;
                    await Ui(() => { Store.Settings.AvRealtime = on; Antivirus.ApplyRealtime(); });
                    if (msgId != 0 && await Menu2("av") is { } m) await Edit(msgId, m.Item1, m.Item2);
                    return T(on ? "🟢 Защита в реальном времени включена" : "⚪ Защита в реальном времени выключена");
                }
                case "db":
                {
                    var e = await Antivirus.UpdateDb();
                    if (msgId != 0 && await Menu2("av") is { } m) await Edit(msgId, m.Item1, m.Item2);
                    return e ?? T("🔄 База обновлена: ") + Antivirus.DbCount.ToString("N0");
                }
                case "ql":
                {
                    var l = Antivirus.Quarantined();
                    if (l.Count == 0) { await Message(T("🗂 Карантин пуст")); return "🗂"; }
                    var rows = l.Take(8).Select(x => Row(B("↩ " + Cut(Path.GetFileName(x.Original), 24), "av2:qr:" + x.Id))).ToList();
                    await Message("🗂 <b>" + T("Карантин") + $": {l.Count}</b>\n" +
                                  string.Join("\n", l.Take(12).Select(x => $"{x.At:dd.MM HH:mm} · <code>{H(Cut(Path.GetFileName(x.Original), 60))}</code> · {H(Cut(x.Reason ?? "", 60))}")) +
                                  "\n\n" + T("↩ — вернуть файл на место (дважды: это может быть вирус). Удалить навсегда — только на ПК."), K(rows.ToArray()));
                    return "🗂";
                }
                case var r when r.StartsWith("qr:"):
                {
                    if ((no = Need("control")) != null) return no;
                    var id = r[3..];
                    if (_avConfirm != id || (DateTime.Now - _avConfirmAt).TotalSeconds > 15) { _avConfirm = id; _avConfirmAt = DateTime.Now; return T("⚠️ Это может быть вирус. Нажми ещё раз в течение 15 секунд, чтобы вернуть"); }
                    _avConfirm = null;
                    var e = await Task.Run(() => Antivirus.Restore(id));
                    return e ?? T("↩ Файл возвращён и добавлен в доверенные");
                }
                case "wd":
                    if ((no = Need("control")) != null) return no;
                    if (_wdScanning) return T("Проверка Защитником Windows уже идёт");
                    _wdScanning = true;
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await Message(T("🛡 Защитник Windows: быстрая проверка… (обычно 2–10 минут)"));
                            var (res, _) = await WindowsDefenderScan();
                            await Message(res);
                        }
                        catch (Exception e) { await Message("⚠️ " + H(e.Message)); }
                        finally { _wdScanning = false; }
                    });
                    return T("🛡 Проверка запущена");
            }
            return null;
        }

        static bool _wdScanning;

        static string MpCmdRun()
        {
            var a = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Windows Defender", "MpCmdRun.exe");
            if (File.Exists(a)) return a;
            try
            {
                var plat = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "Windows Defender", "Platform");
                return Directory.GetDirectories(plat).OrderByDescending(d => d).Select(d => Path.Combine(d, "MpCmdRun.exe")).FirstOrDefault(File.Exists);
            }
            catch { return null; }
        }

        /// <summary>Быстрая проверка встроенным Защитником Windows (MpCmdRun). Код 0 — чисто, 2 — найдены угрозы.</summary>
        static async Task<(string Text, bool Threats)> WindowsDefenderScan()
        {
            var exe = MpCmdRun();
            if (exe == null) return (T("Защитник Windows не найден (может, стоит другой антивирус)"), false);
            return await Task.Run(() =>
            {
                var psi = new System.Diagnostics.ProcessStartInfo(exe, "-Scan -ScanType 1") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                using var p = System.Diagnostics.Process.Start(psi);
                var outp = p.StandardOutput.ReadToEndAsync();
                if (!p.WaitForExit(40 * 60 * 1000)) { try { p.Kill(true); } catch { } return (T("Проверка Защитником не уложилась в 40 минут"), false); }
                var text = outp.Result ?? "";
                return p.ExitCode switch
                {
                    0 => (T("🛡 Защитник Windows: угроз не найдено ✓"), false),
                    2 => (T("🔴 Защитник Windows нашёл угрозы и обработал их сам. Подробности — «Безопасность Windows → Журнал защиты» на ПК."), true),
                    _ => ("⚠️ " + T("Защитник Windows не смог проверить") + $" (код {p.ExitCode}): " + H(Cut(text.Trim(), 300)), false),
                };
            });
        }
    }
}
