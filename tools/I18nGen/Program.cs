// I18nGen — собирает все строки интерфейса N-Island (русский текст из .cs и .xaml) и делает словари переводов
// Assets/i18n/<язык>.json. Уже переведённые строки не трогает — можно запускать после каждого изменения интерфейса.
//
//   dotnet run --project tools/I18nGen -- <папка проекта> en uk
//
// Перевод — бесплатный ИИ Pollinations с контекстом приложения, запасной — Google Translate (оба без ключа). Ручные правки в json сохраняются при следующих запусках.

using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

var root = Path.GetFullPath(args.Length > 0 ? args[0] : ".");
var langs = args.Skip(1).ToArray();
if (langs.Length == 0) langs = new[] { "en", "uk" };

var cyr = new Regex("[А-Яа-яЁё]");
var keys = new SortedSet<string>(StringComparer.Ordinal);
foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
{
    var rel = Path.GetRelativePath(root, file);
    if (Regex.IsMatch(rel, @"^(bin|obj|publish|tools|docs|\.git)[\\/]") || rel.Contains(@"\bin\") || rel.Contains(@"\obj\")) continue;
    // строки журнала (Log.Info) и регулярные выражения пользователю не показываются — не переводим
    if (file.EndsWith(".cs"))
        foreach (var s in CSharpStrings(string.Join("\n", File.ReadAllLines(file).Select(l => Regex.IsMatch(l, @"Log\.(Info|Error)\(|new Regex\(|Regex\.(IsMatch|Match|Matches|Replace)\(") ? "" : l))))
            Add(s);
    else if (file.EndsWith(".xaml"))
        foreach (Match m in Regex.Matches(File.ReadAllText(file), @"\b(?:Text|Content|ToolTip|Title|Header)=""([^""]*)"""))
            Add(WebUtility.HtmlDecode(m.Groups[1].Value));
}
void Add(string s)
{
    if (s.Trim().Length == 0 || !cyr.IsMatch(s)) return;
    keys.Add(s);
}
Console.WriteLine($"строк интерфейса: {keys.Count}");

var outDir = Path.Combine(root, "Assets", "i18n");
Directory.CreateDirectory(outDir);
var jsonOut = new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

// Перевод файлом (например, DeepL):  -- <проект> --export strings.json  →  перевести файл  →  -- <проект> --import en strings.en.json
if (langs.Length >= 2 && langs[0] == "--export")
{
    var list = keys.ToList();
    var doc = new SortedDictionary<string, string>(StringComparer.Ordinal);
    for (int i = 0; i < list.Count; i++) doc[$"s{i:D4}"] = Glossary.Prepare(list[i].Trim());
    File.WriteAllText(langs[1], JsonSerializer.Serialize(doc, jsonOut));
    File.WriteAllText(langs[1] + ".keys", JsonSerializer.Serialize(list, jsonOut));
    Console.WriteLine($"экспорт: {list.Count} строк → {langs[1]}");
    return;
}
if (langs.Length >= 3 && langs[0] == "--import")
{
    string lang = langs[1], src = langs[2];
    var exportKeys = Directory.GetFiles(Path.GetDirectoryName(Path.GetFullPath(src)) ?? ".", "*.keys").First();
    var list = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(exportKeys));
    var tr = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(src));
    var file = Path.Combine(outDir, lang + ".json");
    var dict = File.Exists(file) ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file)) ?? new() : new();
    for (int i = 0; i < list.Count; i++)
        if (tr.TryGetValue($"s{i:D4}", out var v) && !string.IsNullOrWhiteSpace(v)) dict[list[i]] = Keep(list[i], v);
    foreach (var (ru, terms) in Glossary.Terms)
        if (terms.TryGetValue(lang, out var t)) foreach (var k in list.Where(k => k.Trim() == ru)) dict[k] = Keep(k, t);
    File.WriteAllText(file, JsonSerializer.Serialize(new SortedDictionary<string, string>(dict, StringComparer.Ordinal), jsonOut));
    Console.WriteLine($"[{lang}] импортировано: {dict.Count} из {list.Count}");
    return;
}
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (N-Island i18n generator)");
var json = new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
var names = new Dictionary<string, string> { ["en"] = "English", ["uk"] = "Ukrainian", ["de"] = "German", ["es"] = "Spanish", ["fr"] = "French",
                                             ["pt"] = "Portuguese (Brazil)", ["pl"] = "Polish", ["tr"] = "Turkish" };
var gate = new SemaphoreSlim(1);   // бесплатные сервисы — по одному запросу за раз, иначе 429/402
var lastLlm = DateTime.MinValue;

// Проход 1: пачки строк сразу на все языки одним запросом к ИИ — при лимите «1 ответ в 45 с» это в разы быстрее.
// Если тем временем заработал Google — переходим к проходу 2 (по языкам, через Google).
var dicts = langs.ToDictionary(l => l, l =>
{
    var f = Path.Combine(outDir, l + ".json");
    return File.Exists(f) ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(f)) ?? new() : new Dictionary<string, string>();
});
var todo = keys.Where(k => langs.Any(l => !dicts[l].ContainsKey(k))).ToList();
Console.WriteLine($"без перевода хотя бы на один язык: {todo.Count}");
var nextProbe = DateTime.Now;
for (int i = 0; i < todo.Count;)
{
    if (DateTime.Now >= nextProbe)
    {
        nextProbe = DateTime.Now.AddMinutes(10);
        try { await Google("проверка", "en"); Console.WriteLine("Google снова доступен — перевожу через него"); break; } catch { }
    }
    var items = new List<string>();
    int len = 0;
    // чем меньше языков в запросе, тем больше строк влезает в один ответ
    int maxItems = langs.Length <= 2 ? 32 : 18, maxLen = langs.Length <= 2 ? 1900 : 1000;
    while (i < todo.Count && items.Count < maxItems && (items.Count == 0 || len + todo[i].Length < maxLen)) { items.Add(todo[i]); len += todo[i].Length; i++; }
    await Multi(items);
    Console.WriteLine($"  {i}/{todo.Count}");
}

async Task Multi(List<string> items)
{
    var need = langs.Where(l => items.Any(k => !dicts[l].ContainsKey(k))).ToList();
    if (need.Count == 0) return;
    Dictionary<string, List<string>> res = null;
    for (int attempt = 0; attempt < 6 && res == null; attempt++)
    {
        var pause = lastLlm.AddSeconds(46) - DateTime.Now;
        if (pause > TimeSpan.Zero) await Task.Delay(pause);
        lastLlm = DateTime.Now;
        try { res = await LlmMulti(items.Select(x => x.Trim()).ToList(), need); if (res == null) break; }
        catch (Exception e) { Console.WriteLine($"  ИИ: {e.Message}"); await Task.Delay(30000); }
    }
    if (res == null)
    {
        // не поместилось или не разобралось — делим пачку пополам
        if (items.Count > 1) { await Multi(items.Take(items.Count / 2).ToList()); await Multi(items.Skip(items.Count / 2).ToList()); }
        return;
    }
    foreach (var l in need)
    {
        if (!res.TryGetValue(l, out var arr) || arr == null || arr.Count != items.Count) continue;
        for (int j = 0; j < items.Count; j++) if (!string.IsNullOrWhiteSpace(arr[j])) dicts[l][items[j]] = Keep(items[j], arr[j]);
        File.WriteAllText(Path.Combine(outDir, l + ".json"), JsonSerializer.Serialize(new SortedDictionary<string, string>(dicts[l], StringComparer.Ordinal), json));
    }
}

async Task<Dictionary<string, List<string>>> LlmMulti(List<string> items, List<string> need)
{
    var system =
        "You translate the user interface of N-Island, a Windows app that imitates the iPhone Dynamic Island, from Russian. " +
        "Context: «чёлка» always means the island/notch at the top of the screen (never hair); «Корзина» is the Windows Recycle Bin; " +
        "«Сон» is sleep mode. Keep emoji, symbols, numbers, units, program names, paths, shortcuts and punctuation like «·», «—», «✓». " +
        "Keep it short like real UI text, friendly informal tone. " +
        "Reply with ONLY a JSON object whose keys are these language codes: " + string.Join(", ", need.Select(l => $"\"{l}\" ({names[l]})")) +
        "; each value is an array with exactly one translation per input string, in the same order.";
    var payload = JsonSerializer.Serialize(new
    {
        model = "openai-fast", reasoning_effort = "low", max_tokens = 12000,
        messages = new object[] { new { role = "system", content = system }, new { role = "user", content = JsonSerializer.Serialize(items, json) } },
    });
    using var resp = await http.PostAsync("https://text.pollinations.ai/openai", new StringContent(payload, Encoding.UTF8, "application/json"));
    var body = await resp.Content.ReadAsStringAsync();
    if (!resp.IsSuccessStatusCode)
    {
        if (body.Contains("output_token_limit")) return null;
        throw new HttpRequestException($"{(int)resp.StatusCode} {body[..Math.Min(160, body.Length)]}");
    }
    using var doc = JsonDocument.Parse(body);
    var text = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
    int a = text.IndexOf('{'), b = text.LastIndexOf('}');
    if (a < 0 || b <= a) return null;
    try { return JsonSerializer.Deserialize<Dictionary<string, List<string>>>(text[a..(b + 1)]); }
    catch { return null; }
}

// Проход 2: по языкам — добивает пропуски (Google, если доступен, иначе ИИ) и применяет глоссарий
foreach (var lang in langs) await Run(lang);

async Task Run(string lang)
{
    var file = Path.Combine(outDir, lang + ".json");
    var dict = File.Exists(file) ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file)) ?? new() : new();
    var missing = keys.Where(k => !dict.ContainsKey(k)).ToList();
    Console.WriteLine($"[{lang}] уже есть {dict.Count}, перевести {missing.Count}");
    var batch = new List<string>();
    int len = 0;
    foreach (var k in missing)
    {
        if ((len + k.Length > 1800 || batch.Count >= 30) && batch.Count > 0) await Flush();
        batch.Add(k); len += k.Length + 4;
    }
    if (batch.Count > 0) await Flush();
    // убираем строки, которых больше нет в коде; ключевые термины — из глоссария
    lock (dict)
    {
        foreach (var gone in dict.Keys.Where(k => !keys.Contains(k)).ToList()) dict.Remove(gone);
        foreach (var (ru, tr) in Glossary.Terms)
            if (tr.TryGetValue(lang, out var v)) foreach (var k in keys.Where(k => k.Trim() == ru)) dict[k] = Keep(k, v);
        File.WriteAllText(file, JsonSerializer.Serialize(new SortedDictionary<string, string>(dict, StringComparer.Ordinal), json));
    }
    Console.WriteLine($"[{lang}] готово: {dict.Count}");

    async Task Flush()
    {
        var items = batch.ToList();
        batch.Clear(); len = 0;
        await TranslateBatch(items, lang, dict);
        lock (dict) File.WriteAllText(file, JsonSerializer.Serialize(new SortedDictionary<string, string>(dict, StringComparer.Ordinal), json));
    }
}

async Task TranslateBatch(List<string> items, string lang, Dictionary<string, string> dict)
{
    var res = await TranslateMany(items.Select(i => i.Trim()).ToList(), lang);
    if (res != null && res.Count == items.Count)
    {
        lock (dict) for (int i = 0; i < items.Count; i++) dict[items[i]] = Keep(items[i], res[i]);
        return;
    }
    // ответ не совпал по количеству строк — делим пачку пополам
    if (items.Count == 1) { if (res?.Count > 0) lock (dict) dict[items[0]] = Keep(items[0], string.Join(" ", res)); return; }
    int half = items.Count / 2;
    await TranslateBatch(items.Take(half).ToList(), lang, dict);
    await TranslateBatch(items.Skip(half).ToList(), lang, dict);
}

// Google Translate (с глоссарием «чёлка» → «остров»); если он ограничил запросы — ждём и пробуем снова.
// Запасной вариант — бесплатный ИИ Pollinations. Между запросами пауза, чтобы не превышать лимиты бесплатных сервисов.
async Task<List<string>> TranslateMany(List<string> items, string lang)
{
    for (int attempt = 0; attempt < 14; attempt++)
    {
        await gate.WaitAsync();
        try
        {
            var lines = (await Google(string.Join("\n", items), lang)).Split('\n').ToList();
            await Task.Delay(5000);
            return lines.Count == items.Count ? lines : null;
        }
        catch (Exception e) { Console.WriteLine($"  [{lang}] Google: {e.Message}"); }
        finally { gate.Release(); }
        try
        {
            // бесплатный лимит Pollinations — один ответ в 45 секунд с одного IP
            var pause = lastLlm.AddSeconds(46) - DateTime.Now;
            if (pause > TimeSpan.Zero) await Task.Delay(pause);
            lastLlm = DateTime.Now;
            // null — ответ не поместился или не разобрался: пусть вызывающий поделит пачку пополам, ждать нечего
            return await Llm(items, lang);
        }
        catch (Exception e) { Console.WriteLine($"  [{lang}] ИИ: {e.Message}"); }
        int wait = Math.Min(300, 30 * (attempt + 1));
        Console.WriteLine($"  [{lang}] жду {wait} с");
        await Task.Delay(wait * 1000);
    }
    return null;
}

async Task<List<string>> Llm(List<string> items, string lang)
{
    var system =
        $"You translate the user interface of N-Island, a Windows app that imitates the iPhone Dynamic Island, from Russian to {names[lang]}. " +
        "Context: «чёлка» always means the island/notch at the top of the screen (never hair); «Корзина» is the Windows Recycle Bin; " +
        "«Сон» is sleep mode; «Центр управления ноутбуком» is the Laptop Control Center. " +
        "Rules: translate every array element separately and keep the same order and count; keep emoji, symbols, numbers, units, " +
        "brand and program names, file paths, keyboard shortcuts and punctuation like «·», «—», «✓»; keep it short like real UI text; " +
        "use the informal friendly tone. Reply with ONLY a JSON array of strings, nothing else.";
    var payload = JsonSerializer.Serialize(new
    {
        model = "openai-fast",   // «openai» у Pollinations стала платной
        reasoning_effort = "low",
        max_tokens = 8000,
        messages = new object[] { new { role = "system", content = system }, new { role = "user", content = JsonSerializer.Serialize(items, json) } },
    });
    using var resp = await http.PostAsync("https://text.pollinations.ai/openai", new StringContent(payload, Encoding.UTF8, "application/json"));
    if (!resp.IsSuccessStatusCode)
    {
        var err = await resp.Content.ReadAsStringAsync();
        if (err.Contains("output_token_limit")) return null;   // слишком большая пачка
        throw new HttpRequestException($"{(int)resp.StatusCode} {err[..Math.Min(200, err.Length)]}");
    }
    using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
    var text = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
    int a = text.IndexOf('['), b = text.LastIndexOf(']');
    if (a < 0 || b <= a) return null;
    try { return JsonSerializer.Deserialize<List<string>>(text[a..(b + 1)]); }
    catch { return null; }
}

async Task<string> Google(string text, string lang)
{
    using var body = new FormUrlEncodedContent(new Dictionary<string, string> { ["q"] = Glossary.Prepare(text) });
    using var resp = await http.PostAsync($"https://translate.googleapis.com/translate_a/single?client=gtx&sl=ru&tl={lang}&dt=t", body);
    resp.EnsureSuccessStatusCode();
    using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
    var sb = new StringBuilder();
    foreach (var seg in doc.RootElement[0].EnumerateArray())
        if (seg.ValueKind == JsonValueKind.Array && seg[0].ValueKind == JsonValueKind.String) sb.Append(seg[0].GetString());
    return sb.ToString();
}

// пробелы по краям важны: строки склеиваются в коде («Освобождено » + размер)
static string Keep(string src, string tr)
{
    tr = tr.Trim();
    int a = src.Length - src.TrimStart().Length, b = src.Length - src.TrimEnd().Length;
    return src[..a] + tr + src[(src.Length - b)..];
}

// ---------------------------------------------------------------------------
//  Мини-лексер C#: обычные, @-, $- и $@-строки; комментарии и символьные литералы пропускаются.
//  Для интерполированных строк берутся куски текста между {…}.
// ---------------------------------------------------------------------------
static IEnumerable<string> CSharpStrings(string t)
{
    var res = new List<string>();
    int i = 0;
    while (i < t.Length)
    {
        char c = t[i];
        if (c == '/' && i + 1 < t.Length && t[i + 1] == '/') { while (i < t.Length && t[i] != '\n') i++; continue; }
        if (c == '/' && i + 1 < t.Length && t[i + 1] == '*') { int e = t.IndexOf("*/", i + 2); i = e < 0 ? t.Length : e + 2; continue; }
        if (c == '\'') { i++; while (i < t.Length && t[i] != '\'') { if (t[i] == '\\') i++; i++; } i++; continue; }
        if (c == '"' || ((c == '@' || c == '$') && i + 1 < t.Length && (t[i + 1] == '"' || ((t[i + 1] == '@' || t[i + 1] == '$') && i + 2 < t.Length && t[i + 2] == '"'))))
        {
            bool verbatim = false, interp = false;
            while (t[i] != '"') { if (t[i] == '@') verbatim = true; if (t[i] == '$') interp = true; i++; }
            i = ReadString(t, i, verbatim, interp, res);
            continue;
        }
        i++;
    }
    return res;
}

// t[i] == '"' — читает строку, кладёт куски текста в res, возвращает позицию после закрывающей кавычки
static int ReadString(string t, int i, bool verbatim, bool interp, List<string> res)
{
    i++;
    var sb = new StringBuilder();
    while (i < t.Length)
    {
        char c = t[i];
        if (verbatim && c == '"') { if (i + 1 < t.Length && t[i + 1] == '"') { sb.Append('"'); i += 2; continue; } i++; break; }
        if (!verbatim && c == '"') { i++; break; }
        if (!verbatim && c == '\\' && i + 1 < t.Length)
        {
            char n = t[i + 1];
            i += 2;
            switch (n)
            {
                case 'n': sb.Append('\n'); break;
                case 't': sb.Append('\t'); break;
                case 'r': break;
                case '0': sb.Append('\0'); break;
                case 'u' when i + 4 <= t.Length: sb.Append((char)Convert.ToInt32(t.Substring(i, 4), 16)); i += 4; break;
                default: sb.Append(n); break;
            }
            continue;
        }
        if (interp && c == '{')
        {
            if (i + 1 < t.Length && t[i + 1] == '{') { sb.Append('{'); i += 2; continue; }
            res.Add(sb.ToString()); sb.Clear();
            i = SkipHole(t, i + 1, res);
            continue;
        }
        if (interp && c == '}' && i + 1 < t.Length && t[i + 1] == '}') { sb.Append('}'); i += 2; continue; }
        if (!verbatim && c == '\n') break;   // незакрытая строка — защита от ошибок разбора
        sb.Append(c);
        i++;
    }
    res.Add(sb.ToString());
    return i;
}

// пропускает выражение внутри {…} интерполяции, учитывая вложенные скобки и строки
static int SkipHole(string t, int i, List<string> nested)
{
    int depth = 0;
    while (i < t.Length)
    {
        char c = t[i];
        if (c == '"' || ((c == '$' || c == '@') && i + 1 < t.Length && t[i + 1] == '"'))
        {
            bool v = false, s = false;
            while (t[i] != '"') { if (t[i] == '@') v = true; if (t[i] == '$') s = true; i++; }
            i = ReadString(t, i, v, s, nested);
            continue;
        }
        if (c == '\'') { i++; while (i < t.Length && t[i] != '\'') { if (t[i] == '\\') i++; i++; } i++; continue; }
        if (c == '{' || c == '(' || c == '[') depth++;
        else if (c == ')' || c == ']') depth--;
        else if (c == '}') { if (depth == 0) return i + 1; depth--; }
        i++;
    }
    return i;
}