using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NIsland.Core;

namespace NIsland.Services
{
    /// <param name="Kind">openai — OpenAI-совместимый протокол; anthropic — Claude через официальный SDK.</param>
    public record AiProvider(string Key, string Name, string Kind, string BaseUrl, string DefaultModel, bool NeedsKey, string KeyUrl, string Note,
                             string Headers = null, bool Custom = false, string Group = "Облако");

    /// <param name="Source">Откуда сообщение: null — остров, «tg» — Telegram.</param>
    public record AiMessage(string Role, string Text, string Source = null);

    public record AiPersona(string Key, string Name, string Prompt);

    public record AiAction(string Name, string Prompt);

    /// <summary>Подключение нейросетей к острову: готовые провайдеры + свои.</summary>
    public static class Ai
    {
        public static readonly AiProvider[] BuiltIn =
        {
            new("free", "Бесплатный (Pollinations)", "openai", "https://text.pollinations.ai/openai", "openai-fast", false, null,
                "Работает сразу, без ключа и регистрации. Если занят — ответит запасной бесплатный (LLM7).", Group: "Без ключа"),
            new("llm7", "Бесплатный №2 (LLM7)", "openai", "https://api.llm7.io/v1", "default", false, "https://token.llm7.io",
                "Без ключа: модель «default» выбирает сервис. С бесплатным токеном — больше лимиты и десятки моделей.", Group: "Без ключа"),
            new("claude", "Claude (Anthropic)", "anthropic", "", "claude-opus-5-5", true, "https://console.anthropic.com/settings/keys",
                "Модели: claude-opus-5-5 (по умолчанию), claude-sonnet-5-5, claude-haiku-5-5. Нужен API-ключ."),
            new("openai", "OpenAI (ChatGPT)", "openai", "https://api.openai.com/v1", "gpt-4.1-mini", true, "https://platform.openai.com/api-keys", null),
            new("gemini", "Google Gemini", "openai", "https://generativelanguage.googleapis.com/v1beta/openai", "gemini-2.5-flash", true, "https://aistudio.google.com/apikey",
                "У Gemini есть бесплатный лимит."),
            new("groq", "Groq", "openai", "https://api.groq.com/openai/v1", "llama-3.3-70b-versatile", true, "https://console.groq.com/keys",
                "Очень быстрые ответы, есть бесплатный лимит."),
            new("cerebras", "Cerebras", "openai", "https://api.cerebras.ai/v1", "llama-3.3-70b", true, "https://cloud.cerebras.ai", "Самые быстрые ответы, бесплатный лимит."),
            new("deepseek", "DeepSeek", "openai", "https://api.deepseek.com/v1", "deepseek-chat", true, "https://platform.deepseek.com/api_keys", null),
            new("mistral", "Mistral", "openai", "https://api.mistral.ai/v1", "mistral-small-latest", true, "https://console.mistral.ai/api-keys", "Есть бесплатный план."),
            new("xai", "xAI (Grok)", "openai", "https://api.x.ai/v1", "grok-3-mini", true, "https://console.x.ai", null),
            new("perplexity", "Perplexity (с поиском)", "openai", "https://api.perplexity.ai", "sonar", true, "https://www.perplexity.ai/settings/api", "Отвечает с поиском в интернете."),
            new("openrouter", "OpenRouter", "openai", "https://openrouter.ai/api/v1", "openrouter/auto", true, "https://openrouter.ai/keys",
                "Один ключ — сотни моделей, есть бесплатные (с «:free» в имени).", Group: "Агрегаторы"),
            new("github", "GitHub Models", "openai", "https://models.github.ai/inference", "openai/gpt-4.1-mini", true, "https://github.com/settings/tokens",
                "Бесплатно с аккаунтом GitHub: токен с правом models:read.", Group: "Агрегаторы"),
            new("hf", "Hugging Face", "openai", "https://router.huggingface.co/v1", "meta-llama/Llama-3.3-70B-Instruct", true, "https://huggingface.co/settings/tokens",
                "Открытые модели, бесплатный лимит.", Group: "Агрегаторы"),
            new("together", "Together AI", "openai", "https://api.together.xyz/v1", "meta-llama/Llama-3.3-70B-Instruct-Turbo", true, "https://api.together.xyz/settings/api-keys", null, Group: "Агрегаторы"),
            new("zro", "Zro", "openai", "https://zro.moonmath.ai/v1", "deepseek-v4.1-flash", true, "https://zro.moonmath.ai", "DeepSeek и GLM по одному ключу.", Group: "Агрегаторы"),
            new("glm", "Zhipu GLM", "openai", "https://open.bigmodel.cn/api/paas/v4", "glm-4-flash", true, "https://open.bigmodel.cn/usercenter/apikeys", "glm-4-flash — бесплатная."),
            new("qwen", "Qwen (Alibaba)", "openai", "https://dashscope-intl.aliyuncs.com/compatible-mode/v1", "qwen-plus", true, "https://bailian.console.alibabacloud.com/?apiKey=1", null),
            new("ollama", "Ollama (на этом ПК)", "openai", "http://localhost:11434/v1", "llama3.2", false, "https://ollama.com",
                "Нейросеть на твоём компьютере, без интернета. Нужен установленный Ollama.", Group: "На этом ПК"),
            new("lmstudio", "LM Studio (на этом ПК)", "openai", "http://localhost:1234/v1", "local-model", false, "https://lmstudio.ai",
                "Включи «Local Server» в LM Studio.", Group: "На этом ПК"),
        };

        public static readonly AiPersona[] Personas =
        {
            new("default", "Обычный", ""),
            new("short", "Кратко", "Отвечай максимально коротко: 1–3 предложения или короткий список."),
            new("coder", "Программист", "Ты опытный программист. Давай рабочий код и краткие пояснения, без воды."),
            new("editor", "Редактор", "Ты редактор текстов: исправляй ошибки, улучшай стиль, сохраняй смысл и тон автора."),
            new("translator", "Переводчик", "Ты переводчик. Переводи точно и естественно, сохраняя стиль. Без комментариев, только перевод."),
            new("teacher", "Учитель", "Объясняй как хороший учитель: просто, с примерами, шаг за шагом."),
            new("friend", "Друг", "Общайся тепло и неформально, как друг, с лёгким юмором."),
            new("pc", "Помощник по ПК", "Ты помогаешь с Windows и компьютером: объясняй по шагам, где нажать, какие команды ввести; предупреждай о рисках."),
        };

        public static readonly string[] Efforts = { "default", "low", "medium", "high", "xhigh", "max" };

        static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };
        static Settings S => Store.Settings;

        public static IEnumerable<AiProvider> All =>
            BuiltIn.Concat((S.AiCustom ?? new()).Select(c => new AiProvider("c:" + c.Id, c.Name, c.Kind, Normalize(c.BaseUrl, c.Kind), c.Model,
                c.NeedsKey, null, "Свой провайдер", c.Headers, true, "Свои")));

        public static AiProvider Current => All.FirstOrDefault(p => p.Key == S.AiProvider) ?? BuiltIn[0];
        public static AiProvider Find(string key) => All.FirstOrDefault(p => p.Key == key);

        public static string Model(AiProvider p) =>
            S.AiModels.TryGetValue(p.Key, out var m) && !string.IsNullOrWhiteSpace(m) ? m.Trim() : p.DefaultModel;

        public static string Url(AiProvider p) =>
            S.AiUrls.TryGetValue(p.Key, out var u) && !string.IsNullOrWhiteSpace(u) ? Normalize(u, p.Kind) : p.BaseUrl;

        /// <summary>
        /// Адрес, как его обычно вставляют, → базовый адрес API. Люди копируют из документации и «…/v1/chat/completions»,
        /// и «…/v1/», и адрес без https — раньше из-за этого получалось «…/chat/completions/chat/completions» и 404.
        /// </summary>
        public static string Normalize(string url, string kind)
        {
            url = (url ?? "").Trim().Trim('"', '\'');
            if (url == "") return url;
            if (!url.Contains("://")) url = (url.StartsWith("localhost") || url.StartsWith("127.") || url.StartsWith("192.168.") ? "http://" : "https://") + url;
            url = url.TrimEnd('/');
            foreach (var tail in new[] { "/chat/completions", "/completions", "/messages", "/models" })
                if (url.EndsWith(tail, StringComparison.OrdinalIgnoreCase)) { url = url[..^tail.Length]; break; }
            // SDK Anthropic сам добавляет /v1
            if (kind == "anthropic" && url.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)) url = url[..^3];
            return url.TrimEnd('/');
        }

        public static string Key(AiProvider p) => S.AiKeys.TryGetValue(p.Key, out var k) ? Secret.Unprotect(k) : null;
        public static bool HasKey(AiProvider p) => S.AiKeys.ContainsKey(p.Key);

        public static void SetKey(AiProvider p, string key)
        {
            key = key?.Trim();
            if (key != null && key.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) key = key[7..].Trim();   // вставили вместе с «Bearer »
            if (string.IsNullOrWhiteSpace(key)) S.AiKeys.Remove(p.Key);
            else S.AiKeys[p.Key] = Secret.Protect(key);
            Store.SaveSettings();
        }

        public static void SetModel(AiProvider p, string model) { S.AiModels[p.Key] = model?.Trim() ?? ""; Store.SaveSettings(); }
        public static void SetUrl(AiProvider p, string url) { S.AiUrls[p.Key] = url?.Trim() ?? ""; Store.SaveSettings(); }

        public static AiCustomProvider AddCustom(AiCustomProvider c)
        {
            S.AiCustom ??= new();
            c.BaseUrl = Normalize(c.BaseUrl, c.Kind);
            S.AiCustom.Add(c);
            Store.SaveSettings();
            return c;
        }

        public static void UpdateCustom(string key, Action<AiCustomProvider> change)
        {
            var c = S.AiCustom?.FirstOrDefault(x => "c:" + x.Id == key);
            if (c == null) return;
            change(c);
            c.BaseUrl = Normalize(c.BaseUrl, c.Kind);
            S.AiUrls.Remove(key);   // адрес теперь в самом провайдере
            Store.SaveSettings();
        }

        public static void RemoveCustom(string key)
        {
            S.AiCustom?.RemoveAll(c => "c:" + c.Id == key);
            S.AiKeys.Remove(key); S.AiModels.Remove(key); S.AiUrls.Remove(key);
            Store.Data.AiModelCache.Remove(key);
            if (S.AiProvider == key) S.AiProvider = "free";
            if (S.AiFallback == key) S.AiFallback = "auto";
            Store.SaveSettings();
        }

        public static List<AiAction> Actions()
        {
            var list = new List<AiAction>();
            foreach (var line in (S.AiActions ?? "").Split('\n'))
            {
                int bar = line.IndexOf('|');
                if (bar <= 0) { if (list.Count > 0 && line.Trim() != "") list[^1] = list[^1] with { Prompt = list[^1].Prompt + "\n" + line }; continue; }
                list.Add(new AiAction(line[..bar].Trim(), line[(bar + 1)..].Trim()));
            }
            return list;
        }

        public static bool Ready(AiProvider p, out string why)
        {
            why = null;
            if (p.NeedsKey && string.IsNullOrEmpty(Key(p))) { why = $"Нужен API-ключ для «{p.Name}» — добавь его в настройках → ИИ."; return false; }
            if (p.Kind == "openai" && string.IsNullOrWhiteSpace(Url(p))) { why = "Не указан адрес сервера"; return false; }
            if (string.IsNullOrWhiteSpace(Model(p))) { why = "Не указана модель — выбери её в настройках → ИИ"; return false; }
            return true;
        }

        public static string SystemPrompt(string channel = null)
        {
            var sb = new StringBuilder("Ты — помощник в приложении N-Island (Dynamic Island для Windows). Отвечай на языке пользователя, по делу, без длинных вступлений. " +
                                       (channel == "tg" ? "Сейчас пользователь пишет из Telegram с телефона. Ответ показывается с полной разметкой Markdown: заголовки, **жирный**, списки, таблицы, `код` и блоки кода, цитаты, формулы $...$ — используй их, когда это помогает, но не перегружай короткие ответы. "
                                                        : "Ответ показывается в небольшой панели. ") +
                                       $"Сегодня {DateTime.Now:dd.MM.yyyy}, {DateTime.Now:HH:mm}, {DateTime.Now.ToString("dddd", System.Globalization.CultureInfo.GetCultureInfo("ru-RU"))}.");
            var persona = Personas.FirstOrDefault(x => x.Key == S.AiPersona);
            if (!string.IsNullOrEmpty(persona?.Prompt)) sb.Append("\n\n").Append(persona.Prompt);
            if (!string.IsNullOrWhiteSpace(S.AiSystem)) sb.Append("\n\nИнструкции пользователя: ").Append(S.AiSystem.Trim());
            return sb.ToString();
        }

        // ------------------------------------------------------------------
        //  Общий диалог (остров + Telegram)
        // ------------------------------------------------------------------

        public static List<AiMessage> Chat => Store.Data.AiChat ??= new();
        /// <summary>Диалог изменился не из острова (например, сообщение из Telegram).</summary>
        public static event Action ChatChanged;

        public static void AddToChat(AiMessage m, bool notify = true)
        {
            lock (Chat)
            {
                Chat.Add(m);
                while (Chat.Count > 200) Chat.RemoveAt(0);
            }
            if (S.AiKeepChat) Store.SaveData();
            if (notify) ChatChanged?.Invoke();
        }

        public static void ClearChat()
        {
            lock (Chat) Chat.Clear();
            Store.SaveData();
            ChatChanged?.Invoke();
        }

        /// <summary>Какой провайдер ответил последним (мог сработать запасной).</summary>
        public static string LastAnswered { get; private set; }

        static int _busy;
        /// <summary>Сколько запросов сейчас в работе (персонаж острова «думает»).</summary>
        public static int Busy => _busy;
        public static event Action BusyChanged;

        // ------------------------------------------------------------------
        //  Запрос
        // ------------------------------------------------------------------

        /// <summary>Запрос (с потоком или без). onDelta вызывается в фоновом потоке. Если основной провайдер не ответил — пробует запасной.</summary>
        public static async Task<string> Ask(IReadOnlyList<AiMessage> history, Action<string> onDelta, CancellationToken ct, AiProvider p = null, string channel = null)
        {
            System.Threading.Interlocked.Increment(ref _busy);
            BusyChanged?.Invoke();
            try { return await AskInner(history, onDelta, ct, p, channel); }
            catch (Exception e) when (!ct.IsCancellationRequested) { Failed?.Invoke(e.Message); throw; }
            finally { System.Threading.Interlocked.Decrement(ref _busy); BusyChanged?.Invoke(); }
        }

        /// <summary>Запрос не удался (не отмена) — персонаж острова озадачен.</summary>
        public static event Action<string> Failed;

        static async Task<string> AskInner(IReadOnlyList<AiMessage> history, Action<string> onDelta, CancellationToken ct, AiProvider p, string channel)
        {
            bool explicitProvider = p != null;
            p ??= Current;
            if (!Ready(p, out var why))
            {
                var fb0 = explicitProvider ? null : Fallback(p);
                if (fb0 == null) throw new InvalidOperationException(why);
                p = fb0;
            }
            bool gotText = false;
            Action<string> delta = d => { if (!string.IsNullOrEmpty(d)) gotText = true; onDelta?.Invoke(d); };
            try
            {
                var ans = await AskOne(p, history, delta, ct, channel);
                LastAnswered = p.Name;
                return ans;
            }
            catch (Exception e) when (!ct.IsCancellationRequested && !gotText && !explicitProvider && Fallback(p) is { } fb)
            {
                Log.Info($"ai {p.Key} failed, fallback {fb.Key}: {e.Message}");
                onDelta?.Invoke("");
                var ans = await AskOne(fb, history, onDelta, ct, channel);
                LastAnswered = fb.Name + " (запасной)";
                return ans;
            }
        }

        /// <summary>Запасной провайдер для p: выбранный в настройках, а в режиме «авто» — бесплатный.</summary>
        static AiProvider Fallback(AiProvider p)
        {
            var key = S.AiFallback;
            if (string.IsNullOrEmpty(key) || key == "none") return null;
            if (key == "auto") key = p.Key == "free" ? "llm7" : "free";
            var fb = Find(key);
            return fb != null && fb.Key != p.Key && Ready(fb, out _) ? fb : null;
        }

        static async Task<string> AskOne(AiProvider p, IReadOnlyList<AiMessage> history, Action<string> onDelta, CancellationToken ct, string channel)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(S.AiTimeout, 10, 900)));
            int keep = Math.Clamp(S.AiHistory, 2, 100);
            var trimmed = history.Count > keep ? history.Skip(history.Count - keep).ToList() : history;
            // модель не может начать с ответа ассистента и не любит два сообщения одной роли подряд — склеиваем
            var clean = new List<AiMessage>();
            foreach (var m in trimmed.Where(m => !string.IsNullOrWhiteSpace(m.Text) && m.Role is "user" or "assistant"))
            {
                if (clean.Count == 0 && m.Role == "assistant") continue;
                if (clean.Count > 0 && clean[^1].Role == m.Role) clean[^1] = clean[^1] with { Text = clean[^1].Text + "\n\n" + m.Text };
                else clean.Add(m);
            }
            if (clean.Count == 0) throw new InvalidOperationException("Пустой запрос");
            try
            {
                if (p.Kind == "anthropic")
                    return await ClaudeChat.Ask(Key(p), Model(p), SystemPrompt(channel), clean, S.AiMaxTokens, S.AiEffort, S.AiStream, Url(p), onDelta, timeout.Token);
                return await OpenAiAsk(p, clean, onDelta, timeout.Token, channel, allowModelFix: S.AiAutoModel);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new InvalidOperationException($"«{p.Name}» не ответил за {S.AiTimeout} с — увеличь тайм-аут в настройках ИИ");
            }
            catch (HttpRequestException e) when (e.StatusCode == null)
            {
                throw new InvalidOperationException(p.Group == "На этом ПК"
                    ? $"Нет связи с «{p.Name}» — программа запущена? Адрес: {Url(p)}"
                    : $"Нет связи с сервером «{p.Name}» ({Url(p)}): {e.InnerException?.Message ?? e.Message}");
            }
        }

        static HttpRequestMessage Request(AiProvider p, HttpMethod method, string url, string body = null)
        {
            var req = new HttpRequestMessage(method, url);
            if (body != null) req.Content = new StringContent(body, Encoding.UTF8, "application/json");
            var key = Key(p);
            var custom = ParseHeaders(p.Headers, key);
            // свой заголовок авторизации (Azure: «api-key: {key}») заменяет стандартный Bearer
            bool ownAuth = custom.Any(h => h.Name.Equals("Authorization", StringComparison.OrdinalIgnoreCase) || h.Name.Equals("api-key", StringComparison.OrdinalIgnoreCase) || h.Name.Equals("x-api-key", StringComparison.OrdinalIgnoreCase))
                           || (p.Headers ?? "").Contains("{key}");
            if (!string.IsNullOrEmpty(key) && !ownAuth) req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + key);
            if (p.Key == "openrouter") { req.Headers.TryAddWithoutValidation("HTTP-Referer", "https://github.com/Neizvestnyi22/N-Island"); req.Headers.TryAddWithoutValidation("X-Title", "N-Island"); }
            if (p.Key == "github") req.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
            foreach (var (name, value) in custom)
                if (!req.Headers.TryAddWithoutValidation(name, value)) req.Content?.Headers.TryAddWithoutValidation(name, value);
            return req;
        }

        /// <summary>«Имя: значение» по строкам; {key} — подставить ключ.</summary>
        static List<(string Name, string Value)> ParseHeaders(string text, string key)
        {
            var res = new List<(string, string)>();
            foreach (var line in (text ?? "").Split('\n'))
            {
                int c = line.IndexOf(':');
                if (c <= 0) continue;
                var name = line[..c].Trim();
                var value = line[(c + 1)..].Trim().Replace("{key}", key ?? "");
                if (name.Length > 0 && !name.Contains(' ')) res.Add((name, value));
            }
            return res;
        }

        static async Task<string> OpenAiAsk(AiProvider p, IReadOnlyList<AiMessage> history, Action<string> onDelta, CancellationToken ct, string channel, bool allowModelFix)
        {
            var msgs = new List<object> { new { role = "system", content = SystemPrompt(channel) } };
            msgs.AddRange(history.Select(m => (object)new { role = m.Role, content = m.Text }));
            var body = new Dictionary<string, object> { ["model"] = Model(p), ["messages"] = msgs, ["stream"] = S.AiStream };
            if (S.AiTemperature >= 0) body["temperature"] = S.AiTemperature / 100.0;
            if (S.AiMaxTokens > 0) body[p.Key == "openai" ? "max_completion_tokens" : "max_tokens"] = S.AiMaxTokens;
            var url = Url(p) + (p.Key == "free" ? "" : "/chat/completions");

            HttpResponseMessage res = null;
            string fixNote = null;
            // до 4 попыток: сервер может не принять параметр (у «думающих» моделей нет temperature и max_tokens),
            // поток, или модель могли убрать — тогда подбираем доступную
            for (int attempt = 0; attempt < 4; attempt++)
            {
                using var req = Request(p, HttpMethod.Post, url, JsonSerializer.Serialize(body));
                res = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                if (res.IsSuccessStatusCode) break;
                var err = await res.Content.ReadAsStringAsync(ct);
                int code = (int)res.StatusCode;
                var low = err.ToLowerInvariant();
                res.Dispose();
                res = null;
                if (code == 400 && low.Contains("max_tokens") && body.Remove("max_tokens")) { body["max_completion_tokens"] = S.AiMaxTokens; continue; }
                if (code == 400 && low.Contains("max_completion_tokens") && body.Remove("max_completion_tokens")) { body["max_tokens"] = S.AiMaxTokens; continue; }
                if (code == 400 && low.Contains("temperature") && body.Remove("temperature")) continue;
                if (code == 400 && low.Contains("stream") && (bool)body["stream"]) { body["stream"] = false; continue; }
                if (allowModelFix && ModelMissing(code, low) && attempt < 3)
                {
                    var better = await PickModel(p, (string)body["model"]);
                    if (better != null)
                    {
                        body["model"] = better;
                        fixNote = better;
                        continue;
                    }
                }
                throw new InvalidOperationException(Explain(p, code, err));
            }
            if (fixNote != null)
            {
                // запомним рабочую модель, чтобы не искать каждый раз
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() => SetModel(p, fixNote)));
                Log.Info($"ai {p.Key}: model switched to {fixNote}");
            }
            using var resHold = res;
            var sb = new StringBuilder();
            var reasoning = new StringBuilder();
            var media = res.Content.Headers.ContentType?.MediaType ?? "";
            if (!media.Contains("event-stream"))
            {
                var raw = await res.Content.ReadAsStringAsync(ct);
                string text;
                try
                {
                    using var doc = JsonDocument.Parse(raw);
                    if (doc.RootElement.TryGetProperty("error", out var e)) throw new InvalidOperationException(Short(raw));
                    var msg = doc.RootElement.GetProperty("choices")[0].GetProperty("message");
                    text = msg.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : "";
                    if (string.IsNullOrWhiteSpace(text))
                        foreach (var rk in new[] { "reasoning_content", "reasoning" })
                            if (msg.TryGetProperty(rk, out var r) && r.ValueKind == JsonValueKind.String) { text = r.GetString(); break; }
                }
                catch (JsonException)
                {
                    if (raw.TrimStart().StartsWith("<")) throw new InvalidOperationException($"Сервер «{p.Name}» вернул веб-страницу, а не ответ API — проверь адрес (обычно он заканчивается на /v1)");
                    text = raw;
                }
                onDelta?.Invoke(text);
                return StripThink(text);
            }
            using var stream = await res.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream);
            string line;
            while ((line = await reader.ReadLineAsync(ct)) != null)
            {
                if (!line.StartsWith("data:")) continue;
                var data = line[5..].Trim();
                if (data == "[DONE]") break;
                JsonDocument doc;
                try { doc = JsonDocument.Parse(data); } catch { continue; }
                using (doc)
                {
                    if (doc.RootElement.TryGetProperty("error", out _)) throw new InvalidOperationException(Short(data));
                    if (!doc.RootElement.TryGetProperty("choices", out var ch) || ch.ValueKind != JsonValueKind.Array || ch.GetArrayLength() == 0) continue;
                    if (!ch[0].TryGetProperty("delta", out var d)) continue;
                    if (d.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String)
                    {
                        var piece = c.GetString();
                        sb.Append(piece);
                        onDelta?.Invoke(piece);
                    }
                    foreach (var rk in new[] { "reasoning_content", "reasoning" })
                        if (d.TryGetProperty(rk, out var r) && r.ValueKind == JsonValueKind.String) reasoning.Append(r.GetString());
                }
            }
            if (sb.Length == 0 && reasoning.Length > 0) { onDelta?.Invoke(reasoning.ToString()); return reasoning.ToString(); }
            return StripThink(sb.ToString());
        }

        /// <summary>Для недописанного ответа: открытый &lt;think&gt; без закрытия — модель ещё рассуждает, показывать нечего.</summary>
        public static string StripThinkPartial(string s)
        {
            if (string.IsNullOrEmpty(s) || S.AiShowReasoning) return s;
            var t = s.TrimStart();
            if (t.StartsWith("<think>", StringComparison.Ordinal) && !t.Contains("</think>", StringComparison.Ordinal)) return "";
            return StripThink(s);
        }

        /// <summary>Некоторые модели (DeepSeek R1, Qwen) пишут рассуждения в &lt;think&gt; прямо в ответе.</summary>
        public static string StripThink(string s)
        {
            if (string.IsNullOrEmpty(s) || S.AiShowReasoning) return s;
            var res = System.Text.RegularExpressions.Regex.Replace(s, @"^\s*<think>[\s\S]*?</think>\s*", "");
            return res.Length > 0 ? res : s;
        }

        static bool ModelMissing(int code, string low) =>
            (code is 400 or 404 or 422) && low.Contains("model") &&
            (low.Contains("not found") || low.Contains("does not exist") || low.Contains("not exist") || low.Contains("unavailable") || low.Contains("invalid model")
             || low.Contains("decommission") || low.Contains("deprecated") || low.Contains("unknown model") || low.Contains("no such model") || low.Contains("not supported"));

        /// <summary>Подобрать похожую доступную модель: та же «семья» имени, иначе быстрая/мини.</summary>
        static async Task<string> PickModel(AiProvider p, string broken)
        {
            List<string> list;
            try { list = await Models(p, force: true); } catch { return null; }
            list = list.Where(m => m != broken && !Regex.IsMatch(m, "embed|whisper|tts|dall|image|audio|vision-only|moderation|rerank|guard", RegexOptions.IgnoreCase)).ToList();
            if (list.Count == 0) return null;
            var family = Regex.Match(broken ?? "", @"^[a-z\-/]*?[a-z]+").Value;
            return list.FirstOrDefault(m => family.Length > 2 && m.StartsWith(family, StringComparison.OrdinalIgnoreCase) && Regex.IsMatch(m, "mini|flash|small|turbo|instant|fast", RegexOptions.IgnoreCase))
                   ?? list.FirstOrDefault(m => family.Length > 2 && m.StartsWith(family, StringComparison.OrdinalIgnoreCase))
                   ?? list.FirstOrDefault(m => Regex.IsMatch(m, "mini|flash|small|turbo|instant|chat", RegexOptions.IgnoreCase))
                   ?? list[0];
        }

        static string Explain(AiProvider p, int code, string err)
        {
            var msg = Short(err);
            return code switch
            {
                401 or 403 => $"«{p.Name}»: ключ не подошёл ({code}). {msg}",
                402 => $"«{p.Name}»: закончились деньги или лимит на счёте (402). {msg}",
                404 => $"«{p.Name}»: адрес или модель не найдены (404) — проверь адрес ({Url(p)}) и модель «{Model(p)}». {msg}",
                429 => $"«{p.Name}»: слишком много запросов или кончился бесплатный лимит (429). Подожди минуту. {msg}",
                >= 500 => $"«{p.Name}» временно не работает ({code}). {msg}",
                _ => $"{code}: {msg}",
            };
        }

        // ------------------------------------------------------------------
        //  Модели: с сервера провайдера, кеш на 3 дня
        // ------------------------------------------------------------------

        public static List<string> CachedModels(AiProvider p) =>
            Store.Data.AiModelCache.TryGetValue(p.Key, out var l) && l != null ? l : new List<string>();

        public static bool ModelsStale(AiProvider p) =>
            !Store.Data.AiModelCacheAt.TryGetValue(p.Key, out var at) || (DateTime.Now - at).TotalDays > 3;

        /// <summary>Список моделей провайдера (из кеша, если он свежий).</summary>
        public static async Task<List<string>> Models(AiProvider p, bool force = false)
        {
            if (!force && !ModelsStale(p) && CachedModels(p).Count > 0) return CachedModels(p);
            var list = await ListModels(p);
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                Store.Data.AiModelCache[p.Key] = list;
                Store.Data.AiModelCacheAt[p.Key] = DateTime.Now;
                Store.SaveData();
            });
            return list;
        }

        /// <summary>Список моделей прямо с сервера провайдера.</summary>
        public static async Task<List<string>> ListModels(AiProvider p)
        {
            if (p.NeedsKey && string.IsNullOrEmpty(Key(p))) throw new InvalidOperationException("Сначала добавь ключ");
            if (p.Kind == "anthropic") return (await ClaudeChat.Models(Key(p), Url(p))).OrderByDescending(x => x).ToList();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var url = p.Key == "free" ? "https://text.pollinations.ai/models" : Url(p) + "/models";
            if (p.Key == "github") url = "https://models.github.ai/catalog/models";
            using var req = Request(p, HttpMethod.Get, url);
            using var res = await Http.SendAsync(req, cts.Token);
            var raw = await res.Content.ReadAsStringAsync(cts.Token);
            if (!res.IsSuccessStatusCode) throw new InvalidOperationException(Explain(p, (int)res.StatusCode, raw));
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            var arr = root.ValueKind == JsonValueKind.Array ? root
                    : root.TryGetProperty("data", out var d) ? d
                    : root.TryGetProperty("models", out var ms) ? ms : default;
            if (arr.ValueKind != JsonValueKind.Array) return new();
            var ids = new List<string>();
            foreach (var m in arr.EnumerateArray())
            {
                string id = m.ValueKind == JsonValueKind.String ? m.GetString()
                          : m.TryGetProperty("id", out var i) ? i.GetString()
                          : m.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (string.IsNullOrEmpty(id)) continue;
                if (id.StartsWith("models/")) id = id[7..];   // Gemini отдаёт «models/gemini-…», а принимает без префикса
                // LLM7 и OpenRouter помечают платные; без ключа показываем только доступные
                if (p.Key == "llm7" && string.IsNullOrEmpty(Key(p)) && m.ValueKind == JsonValueKind.Object && m.TryGetProperty("tier", out var tier) && tier.GetString() == "pro") continue;
                if (m.ValueKind == JsonValueKind.Object && m.TryGetProperty("model_type", out var mt) && mt.GetString() is { } t && t != "chat") continue;
                ids.Add(id);
            }
            if (p.Key == "llm7" && !ids.Contains("default")) ids.Insert(0, "default");
            return ids.Distinct().OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>Обновить устаревшие списки моделей у настроенных провайдеров (фоном, раз в несколько дней).</summary>
        public static void RefreshModelsInBackground()
        {
            foreach (var p in All.Where(x => (x.Key == S.AiProvider || HasKey(x)) && ModelsStale(x) && Ready(x, out _) && x.Group != "На этом ПК").ToList())
                _ = Task.Run(async () => { try { await Models(p, true); } catch (Exception e) { Log.Info($"ai models {p.Key}: {e.Message}"); } });
        }

        static string Short(string s)
        {
            if (string.IsNullOrEmpty(s)) return "нет ответа";
            try
            {
                using var doc = JsonDocument.Parse(s);
                if (doc.RootElement.TryGetProperty("error", out var e))
                    return e.ValueKind == JsonValueKind.Object && e.TryGetProperty("message", out var m) ? m.GetString() : e.ToString();
                if (doc.RootElement.TryGetProperty("message", out var m2) && m2.ValueKind == JsonValueKind.String) return m2.GetString();
                if (doc.RootElement.TryGetProperty("detail", out var d2)) return d2.ToString();
            }
            catch { }
            if (s.TrimStart().StartsWith("<")) return "сервер вернул веб-страницу — проверь адрес";
            return s.Length > 200 ? s[..200] : s;
        }
    }

    /// <summary>Ключи API на диске — только в зашифрованном виде (DPAPI, привязка к учётной записи Windows).</summary>
    public static class Secret
    {
        [StructLayout(LayoutKind.Sequential)]
        struct BLOB { public int Size; public IntPtr Data; }
        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool CryptProtectData(ref BLOB data, string desc, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out BLOB output);
        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool CryptUnprotectData(ref BLOB data, IntPtr desc, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out BLOB output);
        [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr p);

        public static string Protect(string plain) =>
            Convert.ToBase64String(Run(Encoding.UTF8.GetBytes(plain), true) ?? Array.Empty<byte>());

        public static string Unprotect(string b64)
        {
            try
            {
                var res = Run(Convert.FromBase64String(b64), false);
                return res == null ? null : Encoding.UTF8.GetString(res);
            }
            catch { return null; }
        }

        static byte[] Run(byte[] input, bool protect)
        {
            var inBlob = new BLOB { Size = input.Length, Data = Marshal.AllocHGlobal(Math.Max(1, input.Length)) };
            try
            {
                Marshal.Copy(input, 0, inBlob.Data, input.Length);
                bool ok = protect
                    ? CryptProtectData(ref inBlob, "N-Island", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out var outBlob)
                    : CryptUnprotectData(ref inBlob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out outBlob);
                if (!ok) return null;
                var result = new byte[outBlob.Size];
                Marshal.Copy(outBlob.Data, result, 0, outBlob.Size);
                LocalFree(outBlob.Data);
                return result;
            }
            finally { Marshal.FreeHGlobal(inBlob.Data); }
        }
    }
}
