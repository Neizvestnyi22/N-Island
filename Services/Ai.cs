using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NIsland.Core;

namespace NIsland.Services
{
    /// <param name="Kind">openai — OpenAI-совместимый протокол; anthropic — Claude через официальный SDK.</param>
    public record AiProvider(string Key, string Name, string Kind, string BaseUrl, string DefaultModel, bool NeedsKey, string KeyUrl, string Note,
                             string Headers = null, bool Custom = false);

    public record AiMessage(string Role, string Text);

    public record AiPersona(string Key, string Name, string Prompt);

    public record AiAction(string Name, string Prompt);

    /// <summary>Подключение нейросетей к острову: готовые провайдеры + свои.</summary>
    public static class Ai
    {
        public static readonly AiProvider[] BuiltIn =
        {
            new("free", "Бесплатный (Pollinations)", "openai", "https://text.pollinations.ai/openai", "openai-fast", false, null,
                "Работает сразу, без ключа и регистрации. Скорость и качество — как повезёт."),
            new("claude", "Claude (Anthropic)", "anthropic", "", "claude-opus-5-5", true, "https://console.anthropic.com/settings/keys",
                "Модели: claude-opus-5-5 (по умолчанию), claude-sonnet-5-5, claude-haiku-5-5. Нужен API-ключ."),
            new("openai", "OpenAI (ChatGPT)", "openai", "https://api.openai.com/v1", "gpt-4o-mini", true, "https://platform.openai.com/api-keys", null),
            new("gemini", "Google Gemini", "openai", "https://generativelanguage.googleapis.com/v1beta/openai", "gemini-2.0-flash", true, "https://aistudio.google.com/apikey",
                "У Gemini есть бесплатный лимит."),
            new("groq", "Groq", "openai", "https://api.groq.com/openai/v1", "llama-3.3-70b-versatile", true, "https://console.groq.com/keys",
                "Очень быстрые ответы, есть бесплатный лимит."),
            new("deepseek", "DeepSeek", "openai", "https://api.deepseek.com/v1", "deepseek-chat", true, "https://platform.deepseek.com/api_keys", null),
            new("mistral", "Mistral", "openai", "https://api.mistral.ai/v1", "mistral-small-latest", true, "https://console.mistral.ai/api-keys", null),
            new("xai", "xAI (Grok)", "openai", "https://api.x.ai/v1", "grok-3-mini", true, "https://console.x.ai", null),
            new("openrouter", "OpenRouter", "openai", "https://openrouter.ai/api/v1", "openrouter/auto", true, "https://openrouter.ai/keys",
                "Один ключ — сотни моделей."),
            new("together", "Together AI", "openai", "https://api.together.xyz/v1", "meta-llama/Llama-3.3-70B-Instruct-Turbo", true, "https://api.together.xyz/settings/api-keys", null),
            new("ollama", "Ollama (на этом ПК)", "openai", "http://localhost:11434/v1", "llama3.2", false, "https://ollama.com",
                "Нейросеть на твоём компьютере, без интернета. Нужен установленный Ollama."),
            new("lmstudio", "LM Studio (на этом ПК)", "openai", "http://localhost:1234/v1", "local-model", false, "https://lmstudio.ai",
                "Включи «Local Server» в LM Studio."),
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
        };

        public static readonly string[] Efforts = { "default", "low", "medium", "high", "xhigh", "max" };

        /// <summary>Бесплатные модели Pollinations по порядку: если одна недоступна без оплаты, берём следующую.</summary>
        static readonly string[] FreeModels = { "openai-fast", "openai" };

        static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };
        static Settings S => Store.Settings;

        public static IEnumerable<AiProvider> All =>
            BuiltIn.Concat((S.AiCustom ?? new()).Select(c => new AiProvider("c:" + c.Id, c.Name, c.Kind, c.BaseUrl?.TrimEnd('/'), c.Model,
                c.NeedsKey, null, "Свой провайдер", c.Headers, true)));

        public static AiProvider Current => All.FirstOrDefault(p => p.Key == S.AiProvider) ?? BuiltIn[0];

        public static string Model(AiProvider p) =>
            S.AiModels.TryGetValue(p.Key, out var m) && !string.IsNullOrWhiteSpace(m) ? m.Trim() : p.DefaultModel;

        public static string Url(AiProvider p) =>
            S.AiUrls.TryGetValue(p.Key, out var u) && !string.IsNullOrWhiteSpace(u) ? u.Trim().TrimEnd('/') : p.BaseUrl;

        public static string Key(AiProvider p) => S.AiKeys.TryGetValue(p.Key, out var k) ? Secret.Unprotect(k) : null;
        public static bool HasKey(AiProvider p) => S.AiKeys.ContainsKey(p.Key);

        public static void SetKey(AiProvider p, string key)
        {
            if (string.IsNullOrWhiteSpace(key)) S.AiKeys.Remove(p.Key);
            else S.AiKeys[p.Key] = Secret.Protect(key.Trim());
            Store.SaveSettings();
        }

        public static void SetModel(AiProvider p, string model) { S.AiModels[p.Key] = model?.Trim() ?? ""; Store.SaveSettings(); }
        public static void SetUrl(AiProvider p, string url) { S.AiUrls[p.Key] = url?.Trim() ?? ""; Store.SaveSettings(); }

        public static AiCustomProvider AddCustom(AiCustomProvider c)
        {
            S.AiCustom ??= new();
            S.AiCustom.Add(c);
            Store.SaveSettings();
            return c;
        }

        public static void RemoveCustom(string key)
        {
            S.AiCustom?.RemoveAll(c => "c:" + c.Id == key);
            S.AiKeys.Remove(key); S.AiModels.Remove(key); S.AiUrls.Remove(key);
            if (S.AiProvider == key) S.AiProvider = "free";
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
            return true;
        }

        public static string SystemPrompt()
        {
            var sb = new StringBuilder("Ты — помощник в приложении N-Island (Dynamic Island для Windows). Отвечай на языке пользователя, " +
                                       "по делу: ответ показывается в небольшой панели. Без длинных вступлений. " +
                                       $"Сегодня {DateTime.Now:dd.MM.yyyy}, {DateTime.Now:HH:mm}.");
            var persona = Personas.FirstOrDefault(x => x.Key == S.AiPersona);
            if (!string.IsNullOrEmpty(persona?.Prompt)) sb.Append("\n\n").Append(persona.Prompt);
            if (!string.IsNullOrWhiteSpace(S.AiSystem)) sb.Append("\n\nИнструкции пользователя: ").Append(S.AiSystem.Trim());
            return sb.ToString();
        }

        /// <summary>Запрос (с потоком или без). onDelta вызывается в фоновом потоке.</summary>
        public static async Task<string> Ask(IReadOnlyList<AiMessage> history, Action<string> onDelta, CancellationToken ct, AiProvider p = null)
        {
            p ??= Current;
            if (!Ready(p, out var why)) throw new InvalidOperationException(why);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(S.AiTimeout, 10, 900)));
            int keep = Math.Clamp(S.AiHistory, 2, 100);
            var trimmed = history.Count > keep ? history.Skip(history.Count - keep).ToList() : history;
            try
            {
                return p.Kind == "anthropic"
                    ? await ClaudeChat.Ask(Key(p), Model(p), SystemPrompt(), trimmed, S.AiMaxTokens, S.AiEffort, S.AiStream, Url(p), onDelta, timeout.Token)
                    : await OpenAiAsk(p, trimmed, onDelta, timeout.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new InvalidOperationException($"Нет ответа за {S.AiTimeout} с — увеличь тайм-аут в настройках ИИ");
            }
        }

        static HttpRequestMessage Request(AiProvider p, HttpMethod method, string url, string body = null)
        {
            var req = new HttpRequestMessage(method, url);
            if (body != null) req.Content = new StringContent(body, Encoding.UTF8, "application/json");
            var key = Key(p);
            if (!string.IsNullOrEmpty(key)) req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + key);
            if (p.Key == "openrouter") { req.Headers.TryAddWithoutValidation("HTTP-Referer", "https://github.com/n-island"); req.Headers.TryAddWithoutValidation("X-Title", "N-Island"); }
            foreach (var line in (p.Headers ?? "").Split('\n', ';'))
            {
                int c = line.IndexOf(':');
                if (c > 0) req.Headers.TryAddWithoutValidation(line[..c].Trim(), line[(c + 1)..].Trim());
            }
            return req;
        }

        static async Task<string> OpenAiAsk(AiProvider p, IReadOnlyList<AiMessage> history, Action<string> onDelta, CancellationToken ct)
        {
            var msgs = new List<object> { new { role = "system", content = SystemPrompt() } };
            msgs.AddRange(history.Select(m => (object)new { role = m.Role, content = m.Text }));
            var body = new Dictionary<string, object> { ["model"] = Model(p), ["messages"] = msgs, ["stream"] = S.AiStream };
            if (S.AiTemperature >= 0) body["temperature"] = S.AiTemperature / 100.0;
            if (S.AiMaxTokens > 0) body["max_tokens"] = S.AiMaxTokens;
            var url = Url(p) + (p.Key == "free" ? "" : "/chat/completions");
            var req = Request(p, HttpMethod.Post, url, JsonSerializer.Serialize(body));

            var res = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            // бесплатный провайдер иногда делает модель платной (402) или убирает её — пробуем запасные бесплатные модели
            foreach (var spare in FreeModels)
            {
                if (p.Key != "free" || res.IsSuccessStatusCode || (int)res.StatusCode is not (402 or 403 or 404 or 429) || Equals(body["model"], spare)) continue;
                res.Dispose(); req.Dispose();
                body["model"] = spare;
                req = Request(p, HttpMethod.Post, url, JsonSerializer.Serialize(body));
                res = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            }
            using var reqHold = req;
            using var resHold = res;
            if (!res.IsSuccessStatusCode)
            {
                var err = await res.Content.ReadAsStringAsync(ct);
                throw new InvalidOperationException($"{(int)res.StatusCode}: {Short(err)}");
            }
            var sb = new StringBuilder();
            bool sse = res.Content.Headers.ContentType?.MediaType?.Contains("event-stream") == true;
            if (!sse)
            {
                var raw = await res.Content.ReadAsStringAsync(ct);
                string text;
                try
                {
                    using var doc = JsonDocument.Parse(raw);
                    text = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
                }
                catch { text = raw; }
                onDelta?.Invoke(text);
                return text;
            }
            using var stream = await res.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream);
            string line;
            while ((line = await reader.ReadLineAsync(ct)) != null)
            {
                if (!line.StartsWith("data:")) continue;
                var data = line[5..].Trim();
                if (data == "[DONE]") break;
                try
                {
                    using var doc = JsonDocument.Parse(data);
                    var ch = doc.RootElement.GetProperty("choices");
                    if (ch.GetArrayLength() == 0) continue;
                    if (ch[0].TryGetProperty("delta", out var d) && d.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String)
                    {
                        var piece = c.GetString();
                        sb.Append(piece);
                        onDelta?.Invoke(piece);
                    }
                }
                catch { }
            }
            return sb.ToString();
        }

        /// <summary>Список моделей прямо с сервера провайдера.</summary>
        public static async Task<List<string>> ListModels(AiProvider p)
        {
            if (p.NeedsKey && string.IsNullOrEmpty(Key(p))) throw new InvalidOperationException("Сначала добавь ключ");
            if (p.Kind == "anthropic") return await ClaudeChat.Models(Key(p), Url(p));
            if (p.Key == "free") return new() { "openai", "openai-large", "mistral", "deepseek", "llama" };
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var req = Request(p, HttpMethod.Get, Url(p) + "/models");
            using var res = await Http.SendAsync(req, cts.Token);
            var raw = await res.Content.ReadAsStringAsync(cts.Token);
            if (!res.IsSuccessStatusCode) throw new InvalidOperationException($"{(int)res.StatusCode}: {Short(raw)}");
            using var doc = JsonDocument.Parse(raw);
            var arr = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement : doc.RootElement.GetProperty("data");
            return arr.EnumerateArray().Select(m => m.TryGetProperty("id", out var id) ? id.GetString() : null)
                      .Where(x => !string.IsNullOrEmpty(x)).OrderBy(x => x).ToList();
        }

        static string Short(string s)
        {
            if (string.IsNullOrEmpty(s)) return "нет ответа";
            try
            {
                using var doc = JsonDocument.Parse(s);
                if (doc.RootElement.TryGetProperty("error", out var e))
                    return e.ValueKind == JsonValueKind.Object && e.TryGetProperty("message", out var m) ? m.GetString() : e.ToString();
            }
            catch { }
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
