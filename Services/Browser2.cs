using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;

namespace NIsland.Services
{
    /// <summary>
    /// Браузер, вторая часть: настоящий список вкладок (UI Automation — полоса вкладок Chrome, Edge, Firefox…), переключение и закрытие
    /// любой вкладки, поиск по странице, ввод текста, видео (перемотка, скорость), текст страницы для пересказа, закрыть браузер.
    /// </summary>
    static partial class Browser
    {
        public record TabInfo(string Name, bool Selected);

        static T WithTimeout<T>(Func<T> f, int ms, T fallback)
        {
            var t = Task.Run(f);
            return t.Wait(ms) ? t.Result : fallback;
        }

        static List<AutomationElement> TabElements(IntPtr h)
        {
            var root = AutomationElement.FromHandle(h);
            var items = root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem));
            return items.Cast<AutomationElement>().Where(e => !string.IsNullOrWhiteSpace(e.Current.Name)).ToList();
        }

        static bool IsSelected(AutomationElement e)
        {
            try { return e.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var p) && ((SelectionItemPattern)p).Current.IsSelected; }
            catch { return false; }
        }

        /// <summary>Вкладки окна браузера (по порядку), отмечена текущая. Пусто — браузер не отдаёт полосу вкладок.</summary>
        public static List<TabInfo> Tabs(PcControl.Win w = null)
        {
            w ??= Target();
            if (w == null) return new();
            return WithTimeout(() =>
            {
                try { return TabElements(w.Handle).Select(e => new TabInfo(e.Current.Name.Trim(), IsSelected(e))).Take(40).ToList(); }
                catch { return new List<TabInfo>(); }
            }, 3000, new List<TabInfo>());
        }

        /// <summary>Переключиться на вкладку по номеру в списке Tabs() (и вывести окно вперёд).</summary>
        public static string SelectTab(int index, PcControl.Win w = null)
        {
            w ??= Target();
            if (w == null) return "Браузер не открыт";
            return WithTimeout(() =>
            {
                try
                {
                    var tabs = TabElements(w.Handle);
                    if (index < 0 || index >= tabs.Count) return "Вкладки уже нет";
                    var e = tabs[index];
                    PcControl.Focus(w.Handle);
                    if (e.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var sp)) ((SelectionItemPattern)sp).Select();
                    else if (e.TryGetCurrentPattern(InvokePattern.Pattern, out var ip)) ((InvokePattern)ip).Invoke();
                    else if (index < 8) return PcControl.Hotkey("ctrl+" + (index + 1));
                    else return "Браузер не даёт переключить эту вкладку";
                    return null;
                }
                catch (Exception ex) { return "Не получилось: " + ex.Message; }
            }, 3000, "Браузер не ответил");
        }

        /// <summary>Закрыть вкладку по номеру: переключиться на неё и Ctrl+W.</summary>
        public static string CloseTab(int index)
        {
            var r = SelectTab(index);
            if (r != null) return r;
            Thread.Sleep(250);
            return Press("close");
        }

        // ------------------------------------------------------------------
        //  Страница
        // ------------------------------------------------------------------

        /// <summary>Найти на странице: Ctrl+F, текст, Enter (подсветится первое совпадение).</summary>
        public static string Find(string text)
        {
            var r = PressCombo("ctrl+f");
            if (r != null) return r;
            Thread.Sleep(250);
            PcControl.Hotkey("ctrl+a");
            PcControl.Type(text);
            Thread.Sleep(80);
            return PcControl.Hotkey("enter");
        }

        /// <summary>Напечатать текст в поле на странице (там, где сейчас курсор), Enter — по желанию.</summary>
        public static string TypeText(string text, bool enter)
        {
            var w = Target();
            if (w == null) return "Браузер не открыт";
            PcControl.Focus(w.Handle);
            for (int i = 0; i < 10 && GetForegroundWindow() != w.Handle; i++) Thread.Sleep(30);
            if (GetForegroundWindow() != w.Handle) return "Windows не дала переключиться на браузер";
            Thread.Sleep(60);
            PcControl.Type(text);
            return enter ? PcControl.Hotkey("enter") : null;
        }

        /// <summary>Видео: перемотка ±10 с и скорость (на YouTube — J/L и Shift+&gt;/&lt;, в других плеерах — стрелки).</summary>
        public static string Video(string op)
        {
            var w = Target();
            if (w == null) return "Браузер не открыт";
            bool yt = Regex.IsMatch(w.Title ?? "", "YouTube", RegexOptions.IgnoreCase);
            string combo = op switch
            {
                "rw" => yt ? "j" : "left", "ff" => yt ? "l" : "right",
                "faster" => "shift+period", "slower" => "shift+comma", _ => null,
            };
            return combo == null ? "Нет такого действия" : PressCombo(combo);
        }

        /// <summary>Закрыть все окна браузера (как крестиком — браузер сам спросит, если что-то не сохранено).</summary>
        public static string CloseBrowser()
        {
            var w = Target();
            if (w == null) return "Браузер не открыт";
            int n = 0;
            foreach (var p in Process.GetProcessesByName(w.Process))
            {
                try { if (p.MainWindowHandle != IntPtr.Zero && p.CloseMainWindow()) n++; } catch { }
                finally { p.Dispose(); }
            }
            return n > 0 ? null : "Не получилось закрыть — браузер не ответил";
        }

        // ------------------------------------------------------------------
        //  Текст страницы (для пересказа ИИ)
        // ------------------------------------------------------------------

        static readonly HttpClient Http = new(new SocketsHttpHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 5 }) { Timeout = TimeSpan.FromSeconds(12) };

        /// <summary>Заголовок и текст страницы по адресу (без скриптов и разметки), до maxChars. null — не удалось.</summary>
        public static async Task<(string Title, string Text)?> PageText(string url, int maxChars = 6000)
        {
            if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) url = "https://" + url;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps)) return null;
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, u);
                req.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36");
                req.Headers.AcceptLanguage.ParseAdd("ru,en;q=0.8");
                using var res = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
                if (!res.IsSuccessStatusCode) return null;
                var type = res.Content.Headers.ContentType?.MediaType ?? "";
                if (!type.Contains("html") && !type.Contains("text")) return null;
                var html = await res.Content.ReadAsStringAsync();
                if (html.Length > 3_000_000) html = html[..3_000_000];
                var title = System.Net.WebUtility.HtmlDecode(Regex.Match(html, @"<title[^>]*>(.*?)</title>", RegexOptions.Singleline | RegexOptions.IgnoreCase).Groups[1].Value).Trim();
                var body = Regex.Replace(html, @"<(script|style|noscript|svg|nav|footer|header)[^>]*>.*?</\1>", " ", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                body = Regex.Replace(body, @"<br\s*/?>|</p>|</h\d>|</li>|</div>", "\n", RegexOptions.IgnoreCase);
                body = Regex.Replace(body, "<[^>]+>", " ");
                body = System.Net.WebUtility.HtmlDecode(body);
                body = Regex.Replace(body, @"[ \t ]+", " ");
                body = Regex.Replace(body, @"\s*\n\s*(\n\s*)+", "\n").Trim();
                if (body.Length > maxChars) body = body[..maxChars];
                return (title, body);
            }
            catch { return null; }
        }
    }
}
