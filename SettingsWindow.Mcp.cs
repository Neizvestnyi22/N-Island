using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NIsland.Core;
using NIsland.Pages;
using NIsland.Services;

namespace NIsland
{
    // Настройки → MCP: подключить остров к ИИ-агентам
    public partial class SettingsWindow
    {
        UIElement McpSec()
        {
            var state = Info("");
            void Upd() => state.Text = McpServer.Running ? I18n.T("Работает: ") + McpServer.Url + " · " + I18n.T("инструментов: ") + McpServer.Tools.Count(t => t.Level <= S.McpLevel)
                                     : McpServer.Error ?? I18n.T("Выключен");
            Upd();
            var token = UI.Text(S.McpOn ? McpServer.Token : "", 12, true);
            token.Tag = "no-translate";
            string Exe() => (Environment.ProcessPath ?? "NIsland.exe").Replace('\\', '/');
            string ClaudeCmd() => $"{Agents.ClaudeCommand()} mcp add --scope user --transport http n-island {McpServer.Url} --header \"Authorization: Bearer {McpServer.Token}\"";
            string JsonCfg() => "{\n  \"mcpServers\": {\n    \"n-island\": {\n      \"command\": \"" + Exe() + "\",\n      \"args\": [\"--mcp\"]\n    }\n  }\n}";
            string CodexCfg() => "[mcp_servers.n-island]\ncommand = \"" + Exe() + "\"\nargs = [\"--mcp\"]";
            void Copy(string text, string what) { ClipboardService.I.CopyBack(text); App.Island.Toast("", "Скопировано", what); }

            var log = new StackPanel();
            void RenderLog()
            {
                log.Children.Clear();
                var items = McpServer.Log.Reverse().Take(15).ToList();
                if (items.Count == 0) log.Children.Add(Info("Здесь появятся вызовы агентов: какой инструмент и когда."));
                foreach (var c in items)
                    log.Children.Add(UI.Grid("a,a,*").M(14, 2, 14, 2).Add(UI.Text(c.At.ToString("HH:mm:ss"), 11, false, UI.Dim).M(0, 0, 8, 0),
                        UI.Text((c.Ok ? "✓ " : "✕ ") + c.Tool, 12, true, c.Ok ? null : UI.Hex("#FF453A")).At(0, 1).M(0, 0, 8, 0),
                        new TextBlock { Text = c.Note, FontSize = 11, Foreground = UI.Dim, TextTrimming = TextTrimming.CharacterEllipsis, Tag = "no-translate" }.At(0, 2)));
            }
            RenderLog();
            Action onChange = () => Dispatcher.BeginInvoke(new Action(() => { Upd(); RenderLog(); }));
            // подписка — пока раздел на экране (не до закрытия окна: при переходах между разделами обработчики копились)
            log.Loaded += (_, __) => { McpServer.Changed -= onChange; McpServer.Changed += onChange; };
            log.Unloaded += (_, __) => McpServer.Changed -= onChange;

            var tools = new WrapPanel { Margin = new Thickness(14, 4, 14, 10) };
            foreach (var t in McpServer.Tools)
            {
                var chip = new Border { CornerRadius = new CornerRadius(7), Padding = new Thickness(7, 2, 7, 2), Margin = new Thickness(0, 0, 4, 4), Background = UI.Hex(t.Level == 0 ? "#2230D158" : t.Level == 1 ? "#220A84FF" : "#22FF9F0A"),
                    Child = UI.Text(t.Name, 10.5), ToolTip = I18n.T(t.Title) + " — " + I18n.T(t.Desc) + "\n" + I18n.T(McpServer.LevelName(t.Level)) };
                tools.Children.Add(chip);
            }

            return Panel(
                Group("MCP-сервер N-Island",
                    Row("Включить", "Агенты на этом ПК смогут показывать в острове ход и итог задач, писать тебе в Telegram, управлять музыкой, заметками, напоминаниями и видом острова. Доступ только с этого компьютера и только по токену",
                        ToggleAct(nameof(Settings.McpOn), v => { S.McpOn = v; McpServer.Apply(); token.Text = v ? McpServer.Token : ""; })),
                    Wide("Что разрешено агентам", "Чтение — только смотреть; управление — уведомления, музыка, заметки, буфер, снимок экрана; настройка — ещё и менять вид острова",
                        Seg(nameof(Settings.McpLevel), (0, "Чтение"), (1, "Управление"), (2, "Настройка острова"))),
                    Row("Порт", null, Stepper(nameof(Settings.McpPort), 1024, 65000, 1, v => v.ToString(), () => McpServer.Apply())),
                    Wide("Токен", "Пароль для агентов. Сменишь — переподключи агентов", UI.Col(token, UI.Row(UI.Pill("Копировать", () => Copy(McpServer.Token, "токен")),
                        UI.Pill("Новый токен", () => { S.McpToken = McpServer.NewToken(); McpServer.Apply(); token.Text = McpServer.Token; }).M(6, 0, 0, 0)).M(0, 4, 0, 0))),
                    state),
                Group("Подключить агента",
                    Item("Claude Code", "Одной командой: откроется терминал с «claude mcp add …» — после неё остров доступен во всех проектах. Проверить: /mcp в Claude Code.", Safety.Safe,
                        UI.Row(UI.Pill("Подключить", () => { if (!S.McpOn) { S.McpOn = true; McpServer.Apply(); token.Text = McpServer.Token; } Agents.OpenTerminal(ClaudeCmd()); }, true),
                               UI.Pill("Копировать команду", () => Copy(ClaudeCmd(), "команда для Claude Code")).M(6, 0, 0, 0))),
                    Item("Claude Desktop, Cursor, Windsurf и др.", "Через stdio: N-Island сам станет мостом. Вставь в файл настроек MCP приложения (для Claude Desktop — claude_desktop_config.json).", Safety.Safe,
                        UI.Pill("Копировать JSON", () => Copy(JsonCfg(), "настройки MCP (JSON)"))),
                    Item("Codex CLI", "Добавь в ~/.codex/config.toml.", Safety.Safe, UI.Pill("Копировать TOML", () => Copy(CodexCfg(), "настройки Codex (TOML)"))),
                    Item("Проверить", "Запрос к серверу, как от агента: список инструментов.", Safety.Safe, Act("Проверить", async () =>
                    {
                        if (!McpServer.Running) return I18n.T("Сервер выключен");
                        var r = await McpServer.Handle("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\"}");
                        return r != null && r.Contains("\"tools\"") ? I18n.T("Работает ✓ Инструментов: ") + McpServer.Tools.Count(t => t.Level <= S.McpLevel) : I18n.T("Ошибка: ") + r;
                    }))),
                Group($"Инструменты · {McpServer.Tools.Count}", Info("Зелёные — чтение, синие — управление, оранжевые — настройка острова. Наведи — описание."), tools),
                Group("Последние вызовы", log),
                Note("Идея: попроси агента «показывай прогресс в N-Island и напиши мне в Telegram, когда закончишь» — он сам вызовет island_progress и telegram_send."));
        }
    }
}
