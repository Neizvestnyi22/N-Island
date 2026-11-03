using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Anthropic;
using Anthropic.Models.Beta.Messages;

namespace NIsland.Services
{
    /// <summary>
    /// Claude через официальный SDK Anthropic. Вынесено в отдельный класс: сборка SDK
    /// загружается в память только когда пользователь выбрал Claude.
    /// </summary>
    static class ClaudeChat
    {
        static AnthropicClient _client;
        static string _clientKey, _clientUrl;

        static AnthropicClient Client(string apiKey, string baseUrl)
        {
            if (_client == null || _clientKey != apiKey || _clientUrl != baseUrl)
            {
                _client = string.IsNullOrWhiteSpace(baseUrl)
                    ? new AnthropicClient { ApiKey = apiKey }
                    : new AnthropicClient { ApiKey = apiKey, BaseUrl = baseUrl };
                _clientKey = apiKey;
                _clientUrl = baseUrl;
            }
            return _client;
        }

        public static async Task<string> Ask(string apiKey, string model, string system, IReadOnlyList<AiMessage> history,
                                             int maxTokens, string effort, bool stream, string baseUrl,
                                             Action<string> onDelta, CancellationToken ct)
        {
            var client = Client(apiKey, baseUrl);
            var messages = new List<BetaMessageParam>();
            foreach (var m in history)
                messages.Add(m.Role == "assistant"
                    ? new BetaMessageParam(new BetaAssistantMessageParam { Content = m.Text })
                    : new BetaMessageParam(new BetaUserMessageParam { Content = m.Text }));

            var p = new MessageCreateParams
            {
                Model = model,
                MaxTokens = maxTokens > 0 ? maxTokens : stream ? 64000 : 16000,
                System = system,
                Messages = messages,
            };
            if (effort is "low" or "medium" or "high" or "xhigh" or "max")
            {
                p = p with
                {
                    OutputConfig = new BetaOutputConfig
                    {
                        Effort = effort switch
                        {
                            "low" => Effort.Low,
                            "medium" => Effort.Medium,
                            "high" => Effort.High,
                            "xhigh" => Effort.Xhigh,
                            _ => Effort.Max,
                        },
                    },
                };
            }
            // Для Claude Opus 5.5 / Opus 5 / Fable включаем резервную модель: если запрос
            // отклонит фильтр безопасности, сервер сам продолжит на подходящей модели в том же вызове.
            if (model.StartsWith("claude-opus-5") || model.StartsWith("claude-fable"))
            {
                p = p with
                {
                    Betas = ["server-side-fallback-2026-07-01"],
                    Fallbacks = new Default(),
                };
            }

            var sb = new StringBuilder();
            if (stream)
            {
                await foreach (var ev in client.Beta.Messages.CreateStreaming(p, ct))
                {
                    if (ev.TryPickContentBlockDelta(out var delta) && delta.Delta.TryPickText(out var text))
                    {
                        sb.Append(text.Text);
                        onDelta?.Invoke(text.Text);
                    }
                }
            }
            else
            {
                var resp = await client.Beta.Messages.Create(p, ct);
                foreach (var block in resp.Content)
                    if (block.TryPickText(out var t)) sb.Append(t.Text);
                if (resp.StopReason == "refusal" && sb.Length == 0) sb.Append("Claude отказался отвечать на этот запрос.");
                onDelta?.Invoke(sb.ToString());
            }
            return sb.ToString();
        }

        public static async Task<List<string>> Models(string apiKey, string baseUrl)
        {
            var page = await Client(apiKey, baseUrl).Models.List();
            return page.Items.Select(m => m.ID).ToList();
        }
    }
}
