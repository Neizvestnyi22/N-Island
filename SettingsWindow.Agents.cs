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
    // Настройки → ИИ-агенты: Claude Code, Codex, Gemini CLI и другие на этом ПК
    public partial class SettingsWindow
    {
        UIElement AgentsSec()
        {
            var page = new StackPanel();
            var found = new StackPanel();
            found.Children.Add(Loading("Ищу установленных агентов…"));
            List<AgentFound> agents = new();

            // ---------------- запуск ----------------
            var folder = Field(Agents.Folders.FirstOrDefault() ?? "", 380, null, @"C:\Projects\my-app");
            var recent = new WrapPanel();
            foreach (var f in Agents.Folders.Take(6))
            {
                var ff = f;
                var chip = UI.Pill(System.IO.Path.GetFileName(f.TrimEnd('\\')), () => folder.Text = ff, false, f);
                chip.Padding = new Thickness(8, 2, 8, 2); chip.FontSize = 11; chip.Margin = new Thickness(0, 4, 4, 0);
                recent.Children.Add(chip);
            }
            var browse = UI.Pill("Выбрать…", () =>
            {
                using var dlg = new System.Windows.Forms.FolderBrowserDialog { Description = I18n.T("Папка проекта"), UseDescriptionForTitle = true, SelectedPath = folder.Text };
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK) folder.Text = dlg.SelectedPath;
            });
            var prompt = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 64, Template = Ago.PlainBox(), Foreground = Brushes.White, CaretBrush = Brushes.White, FontSize = 12.5, Tag = I18n.T("Задание (необязательно): например, «найди и исправь падающие тесты»") };
            var agentSeg = new WrapPanel();
            agentSeg.Children.Add(Info(I18n.T("Ищу…")));
            var launchRes = Info("");
            void FillSeg()
            {
                agentSeg.Children.Clear();
                if (agents.Count == 0) agentSeg.Children.Add(Info(I18n.T("Сначала установи агента — список ниже")));
                foreach (var a in agents)
                {
                    var key = a.Kind.Key;
                    var rb = new RadioButton { Style = UI.St("Segment"), Content = a.Kind.Name, GroupName = "agentpick", IsChecked = S.AgentDefault == key || (agents.All(x => x.Kind.Key != S.AgentDefault) && a == agents[0]) };
                    rb.Checked += (_, __) => S.AgentDefault = key;
                    agentSeg.Children.Add(rb);
                }
            }
            AgentFound Picked() => agents.FirstOrDefault(a => a.Kind.Key == S.AgentDefault) ?? agents.FirstOrDefault();
            var launch = UI.Pill("Запустить в терминале", () =>
            {
                var a = Picked();
                if (a == null) { launchRes.Text = I18n.T("Сначала установи агента — список ниже"); return; }
                var err = Agents.Launch(a, folder.Text.Trim(), prompt.Text);
                launchRes.Text = err ?? I18n.T("Открыт ") + a.Kind.Name + " · " + folder.Text.Trim();
            }, true);
            var oneShot = UI.Pill("Выполнить и показать ответ", async () =>
            {
                var a = Picked();
                if (a == null || string.IsNullOrWhiteSpace(prompt.Text)) { launchRes.Text = I18n.T("Нужны агент и задание"); return; }
                launchRes.Text = I18n.T("Агент работает… (до 10 минут)");
                var ans = await Agents.Headless(a, folder.Text.Trim(), prompt.Text.Trim(), TimeSpan.FromMinutes(10));
                launchRes.Text = ans.Length > 3000 ? ans[..3000] + "…" : ans;
            });

            // ---------------- уведомления ----------------
            var hookState = Info("");
            void HookUpd() => hookState.Text = $"Claude Code: {(Agents.ClaudeHooked() ? "подключено ✓" : "не подключено")} · Codex: {(Agents.CodexHooked() ? "подключено ✓" : "не подключено")}";
            HookUpd();

            // ---------------- запущенные ----------------
            var running = new StackPanel();
            async Task<string> Refresh()
            {
                running.Children.Clear();
                running.Children.Add(Loading("Смотрю процессы…"));
                var list = await Agents.Running();
                running.Children.Clear();
                if (list.Count == 0) running.Children.Add(Info("Сейчас ни один агент не запущен"));
                foreach (var p in list)
                {
                    var pid = p.Pid;
                    running.Children.Add(UI.Grid("*,a").M(14, 4, 14, 4).Add(
                        UI.Col(UI.Text(p.Agent + (p.Started > DateTime.MinValue ? " · " + I18n.T("с ") + p.Started.ToString("HH:mm") : ""), 13, true),
                               new TextBlock { Text = p.CommandLine, FontSize = 10.5, Foreground = UI.Dim, TextTrimming = TextTrimming.CharacterEllipsis, Tag = "no-translate" }),
                        UI.Pill("Остановить", () => { var e = Agents.Stop(pid); _ = Refresh(); if (e != null) App.Island.Toast("\uE7BA", "Не остановился", e); }, false, "Завершить процесс агента — несохранённая работа в его окне пропадёт").At(0, 1)));
                }
                var u = await Agents.ClaudeUsageToday();
                if (u.Sessions > 0)
                    running.Children.Add(Info($"Claude Code сегодня: сессий {u.Sessions} · отправлено {Agents.Tokens(u.In)} токенов, получено {Agents.Tokens(u.Out)}, из кэша {Agents.Tokens(u.Cache)}"));
                return null;
            }

            // ---------------- журнал ----------------
            var log = new StackPanel();
            foreach (var ev in (Store.Data.AgentEvents ?? new()).Take(12))
                log.Children.Add(UI.Grid("a,*").M(14, 3, 14, 3).Add(
                    UI.Text(ev.At.ToString("dd.MM HH:mm"), 11, false, UI.Dim).M(0, 0, 10, 0),
                    new TextBlock { Text = $"{ev.Agent}: {ev.Message}" + (string.IsNullOrEmpty(ev.Folder) ? "" : " · " + System.IO.Path.GetFileName(ev.Folder.TrimEnd('\\', '/'))), FontSize = 12, TextWrapping = TextWrapping.Wrap, Tag = "no-translate" }.At(0, 1)));
            if (log.Children.Count == 0) log.Children.Add(Info("Здесь появятся события: агент ждёт подтверждения, закончил задачу."));

            _ = Agents.Detect().ContinueWith(t => Dispatcher.Invoke(() =>
            {
                agents = t.Result;
                found.Children.Clear();
                foreach (var k in Agents.Known)
                {
                    var a = agents.FirstOrDefault(x => x.Kind == k);
                    var kind = k;
                    UIElement actions = a != null
                        ? UI.Row(UI.Pill("Открыть", () => Agents.Launch(a, string.IsNullOrWhiteSpace(folder.Text) ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) : folder.Text.Trim(), null)),
                                 kind.Key == "claude" && !a.Path.Contains("Claude-Data", StringComparison.OrdinalIgnoreCase) ? UI.Pill("Обновить", () => Agents.OpenTerminal($"\"{a.Path}\" update")).M(6, 0, 0, 0) : null)
                        : kind.Install != null
                            ? UI.Row(UI.Pill("Установить", () => Agents.OpenTerminal(kind.Install), false, kind.Install), UI.Pill("Сайт", () => SystemService.Run(kind.Site)).M(6, 0, 0, 0))
                            : UI.Pill("Сайт", () => SystemService.Run(kind.Site));
                    found.Children.Add(Row(kind.Name + (a != null ? "  ✓" : ""), a != null ? $"{a.Version} · {a.Path}" : I18n.T("не установлен") + (kind.Install != null ? " · " + kind.Install : ""), actions));
                }
                FillSeg();
                _ = Refresh();
            }));

            page.Children.Add(Group("Запустить агента",
                Wide("Папка проекта", null, UI.Col(UI.Row(folder, browse.M(6, 0, 0, 0)), recent)),
                Wide("Агент", null, agentSeg),
                Wide("Задание", "Пусто — просто открыть агента в этой папке. «Выполнить и показать ответ» — задание без окна (claude -p, codex exec): агент работает с теми разрешениями, что у него настроены", prompt),
                Wide("", null, UI.Col(UI.Row(launch, oneShot.M(6, 0, 0, 0)), launchRes.M(0, 4, 0, 0)))));
            page.Children.Add(Group("Уведомления от агентов",
                Item("Claude Code", "Добавляет в настройки Claude Code хуки: когда агент ждёт подтверждения или закончил задачу — всплывашка в острове (клик — к окну агента) и сообщение в Telegram. Твои хуки остаются, копия настроек — settings.json.nisland-backup.", Safety.Safe,
                    UI.Row(Act("Подключить", () => { var e = Agents.HookClaude(true); HookUpd(); return Task.FromResult(e ?? I18n.T("Готово ✓ Действует с новой сессии Claude Code")); }, null, true),
                           Act("Отключить", () => { var e = Agents.HookClaude(false); HookUpd(); return Task.FromResult(e); }).M(6, 0, 0, 0))),
                Item("Codex CLI", "Добавляет в ~/.codex/config.toml строку notify — когда Codex закончил ход, приходит всплывашка с его ответом.", Safety.Safe,
                    UI.Row(Act("Подключить", () => { var e = Agents.HookCodex(true); HookUpd(); return Task.FromResult(e ?? I18n.T("Готово ✓")); }, null, true),
                           Act("Отключить", () => { var e = Agents.HookCodex(false); HookUpd(); return Task.FromResult(e); }).M(6, 0, 0, 0))),
                Row("Звук", "Сигнал, когда агент ждёт подтверждения", Toggle(nameof(Settings.AgentSound))),
                hookState));
            page.Children.Add(Group("Работают сейчас", Item("Обновить", "Сессии агентов на этом ПК и расход токенов Claude Code за сегодня", Safety.Safe, Act("Обновить", Refresh)), running));
            page.Children.Add(Group("Установленные агенты", found));
            page.Children.Add(Group("Последние события", log));
            page.Children.Add(Note("С телефона: в Telegram-боте /agents — кто работает и последние события, /agent задание — выполнить задание в последней папке (нужно разрешение «Команды Windows»)."));
            return page;
        }
    }
}
