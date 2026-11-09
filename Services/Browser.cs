using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using NIsland.Core;

namespace NIsland.Services
{
    /// <summary>
    /// Браузер на ПК с телефона: открыть ссылку (обычно, в новом окне, инкогнито), поиск, окна браузеров, вкладки, назад/вперёд,
    /// прокрутка, масштаб, видео, текущий адрес. Клавиши уходят только в окно браузера: сначала оно выходит на передний план,
    /// и если Windows не отдала ему фокус — ничего не нажимается.
    /// </summary>
    static class Browser
    {
        /// <summary>Процессы браузеров → имя для показа.</summary>
        static readonly Dictionary<string, string> Known = new(StringComparer.OrdinalIgnoreCase)
        {
            ["chrome"] = "Chrome", ["msedge"] = "Edge", ["firefox"] = "Firefox", ["opera"] = "Opera", ["brave"] = "Brave", ["browser"] = "Яндекс Браузер",
            ["vivaldi"] = "Vivaldi", ["chromium"] = "Chromium", ["arc"] = "Arc", ["zen"] = "Zen", ["librewolf"] = "LibreWolf", ["waterfox"] = "Waterfox",
            ["floorp"] = "Floorp", ["thorium"] = "Thorium", ["iexplore"] = "Internet Explorer",
        };

        public static bool IsBrowser(string proc) => proc != null && Known.ContainsKey(proc);
        public static string NameOf(string proc) => proc != null && Known.TryGetValue(proc, out var n) ? n : proc;

        /// <summary>Окна браузеров, верхнее первым. Заголовок окна = заголовок активной вкладки (+ « - Google Chrome»).</summary>
        public static List<PcControl.Win> Windows() => PcControl.ListWindows().Where(w => IsBrowser(w.Process)).ToList();

        /// <summary>Заголовок вкладки без хвоста « - Google Chrome», « — Mozilla Firefox».</summary>
        public static string TabTitle(PcControl.Win w)
        {
            var t = w.Title ?? "";
            var m = Regex.Match(t, @"^(.*?)\s+[-—–]\s+(Google Chrome|Microsoft\u200B? ?Edge|Mozilla Firefox|Opera|Brave|Яндекс Браузер|Yandex Browser|Vivaldi|Chromium|Firefox|Zen Browser|LibreWolf|Waterfox|Floorp|Thorium)$");
            if (m.Success) t = m.Groups[1].Value;
            t = Regex.Replace(t, @"\s+и ещё \d+ (страниц[аы]?|вкладк[аи]?)$|\s+and \d+ more pages?$", "");
            return t.Trim();
        }

        /// <summary>Окно, куда пойдут клавиши: активное, если это браузер, иначе самое верхнее окно браузера.</summary>
        public static PcControl.Win Target()
        {
            var ws = Windows();
            return ws.FirstOrDefault(w => w.Active) ?? ws.FirstOrDefault(w => !w.Minimized) ?? ws.FirstOrDefault();
        }

        // ------------------------------------------------------------------
        //  Клавиши
        // ------------------------------------------------------------------

        /// <summary>Действие → сочетание (Chromium и Firefox одинаково, кроме инкогнито).</summary>
        public static readonly Dictionary<string, (string Combo, string Label)> Keys = new()
        {
            ["back"] = ("alt+left", "◀ Назад"), ["fwd"] = ("alt+right", "Вперёд ▶"), ["reload"] = ("f5", "↻ Обновить страницу"),
            ["newtab"] = ("ctrl+t", "➕ Новая вкладка"), ["close"] = ("ctrl+w", "✖ Закрыть вкладку"), ["reopen"] = ("ctrl+shift+t", "↩ Вернуть вкладку"),
            ["next"] = ("ctrl+tab", "⇥ Следующая вкладка"), ["prev"] = ("ctrl+shift+tab", "⇤ Предыдущая вкладка"), ["last"] = ("ctrl+9", "Последняя вкладка"),
            ["up"] = ("pgup", "⬆ Выше"), ["down"] = ("pgdn", "⬇ Ниже"), ["top"] = ("home", "⏫ В начало"), ["bottom"] = ("end", "⏬ В конец"),
            ["zin"] = ("ctrl+plus", "🔍 Крупнее"), ["zout"] = ("ctrl+minus", "🔍 Мельче"), ["z0"] = ("ctrl+0", "100 %"), ["full"] = ("f11", "⛶ Во весь экран"),
            ["play"] = ("k", "⏯ Видео"), ["vmute"] = ("m", "🔇 Звук видео"), ["vfull"] = ("f", "⛶ Видео на весь экран"), ["home"] = ("alt+home", "🏠 Домашняя"),
            ["closewin"] = ("ctrl+shift+w", "Закрыть окно браузера"), ["dl"] = ("ctrl+j", "📥 Загрузки браузера"), ["hist"] = ("ctrl+h", "🕘 История браузера"),
        };

        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();

        /// <summary>Нажать действие в браузере. null — нажато, иначе причина.</summary>
        public static string Press(string action)
        {
            if (!Keys.TryGetValue(action, out var k)) return "Нет такого действия";
            var w = Target();
            if (w == null) return "Браузер не открыт — пришли ссылку, и он откроется";
            string combo = k.Combo;
            // видео: на YouTube — k/m/f, в остальных плеерах — пробел и т. п.
            bool yt = Regex.IsMatch(w.Title ?? "", "YouTube", RegexOptions.IgnoreCase);
            if (action == "play" && !yt) combo = "space";
            if (action == "vmute" && !yt) combo = "m";
            PcControl.Focus(w.Handle);
            for (int i = 0; i < 10 && GetForegroundWindow() != w.Handle; i++) Thread.Sleep(30);
            if (GetForegroundWindow() != w.Handle) return "Windows не дала переключиться на браузер — нажми на него один раз на ПК";
            Thread.Sleep(60);
            return PcControl.Hotkey(combo);
        }

        /// <summary>Вкладка по номеру 1–8 (Ctrl+1…8).</summary>
        public static string Tab(int n) => n is >= 1 and <= 8 ? PressCombo("ctrl+" + n) : "Номер вкладки — от 1 до 8";

        static string PressCombo(string combo)
        {
            var w = Target();
            if (w == null) return "Браузер не открыт";
            PcControl.Focus(w.Handle);
            for (int i = 0; i < 10 && GetForegroundWindow() != w.Handle; i++) Thread.Sleep(30);
            if (GetForegroundWindow() != w.Handle) return "Windows не дала переключиться на браузер";
            Thread.Sleep(60);
            return PcControl.Hotkey(combo);
        }

        /// <summary>Окно браузера на передний план.</summary>
        public static string FocusWindow(IntPtr h)
        {
            var w = Windows().FirstOrDefault(x => x.Handle == h);
            if (w == null) return "Окно уже закрыто";
            PcControl.Focus(h);
            return null;
        }

        // ------------------------------------------------------------------
        //  Текущий адрес (UI Automation: адресная строка — первое поле ввода окна)
        // ------------------------------------------------------------------

        public static string CurrentUrl(PcControl.Win w = null, int timeoutMs = 2500)
        {
            w ??= Target();
            if (w == null) return null;
            var t = Task.Run(() =>
            {
                try
                {
                    var root = System.Windows.Automation.AutomationElement.FromHandle(w.Handle);
                    var cond = new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.ControlTypeProperty, System.Windows.Automation.ControlType.Edit);
                    var edit = root.FindFirst(System.Windows.Automation.TreeScope.Descendants, cond);
                    if (edit != null && edit.TryGetCurrentPattern(System.Windows.Automation.ValuePattern.Pattern, out var p))
                    {
                        var v = ((System.Windows.Automation.ValuePattern)p).Current.Value?.Trim();
                        if (!string.IsNullOrEmpty(v) && !v.Contains(' ') && v.Contains('.')) return v;
                    }
                }
                catch { }
                return null;
            });
            return t.Wait(timeoutMs) ? t.Result : null;
        }

        // ------------------------------------------------------------------
        //  Открыть ссылку
        // ------------------------------------------------------------------

        static readonly string[] Tlds = { "com", "net", "org", "ru", "ua", "by", "kz", "io", "dev", "me", "app", "tv", "info", "ai", "gg", "xyz", "co", "us", "eu",
                                          "uk", "de", "pl", "fr", "site", "online", "tech", "su", "рф", "pro", "biz", "cc", "to", "fm", "ly", "link", "store", "shop" };

        /// <summary>Ссылки в тексте: http(s)://…, www.…, голый домен с известной зоной (youtube.com/…). Не больше 5.</summary>
        public static List<string> FindLinks(string text)
        {
            var res = new List<string>();
            if (string.IsNullOrWhiteSpace(text)) return res;
            foreach (Match m in Regex.Matches(text, @"(?:https?://|www\.)[^\s<>""'«»]+|(?<![@\w.-])(?:[a-zа-я0-9-]+\.)+([a-zа-я]{2,12})(?:/[^\s<>""'«»]*)?(?![\w@])", RegexOptions.IgnoreCase))
            {
                var s = m.Value.TrimEnd('.', ',', ')', ']', '!', '?', ';', ':');
                if (!s.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    if (!s.StartsWith("www.", StringComparison.OrdinalIgnoreCase) && !Tlds.Contains(m.Groups[1].Value.ToLowerInvariant())) continue;
                    s = "https://" + s;
                }
                if (Uri.TryCreate(s, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps) && u.Host.Contains('.') && !res.Contains(u.AbsoluteUri))
                    res.Add(u.AbsoluteUri);
                if (res.Count >= 5) break;
            }
            return res;
        }

        /// <summary>Текст целиком — это ссылка(и), без других слов (тогда она открывается сразу).</summary>
        public static bool OnlyLinks(string text, List<string> links)
        {
            if (links.Count == 0) return false;
            var rest = Regex.Replace(text, @"(?:https?://|www\.)\S+|(?:[a-zа-я0-9-]+\.)+[a-zа-я]{2,12}(?:/\S*)?", "", RegexOptions.IgnoreCase);
            return rest.Trim().Length == 0;
        }

        public enum Mode { Normal, NewWindow, Private }

        /// <summary>Открыть ссылку в браузере по умолчанию (или в выбранном в настройках). null — открыто.</summary>
        public static string Open(string url, Mode mode = Mode.Normal)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps)) return "Это не адрес сайта";
            Remember(u.AbsoluteUri);
            if (mode == Mode.Normal) { SystemService.OpenWeb(u.AbsoluteUri); return null; }
            var exe = DefaultExe();
            if (exe == null) { SystemService.OpenWeb(u.AbsoluteUri); return mode == Mode.Private ? "Браузер по умолчанию не найден — открыл обычным окном" : null; }
            var name = Path.GetFileNameWithoutExtension(exe).ToLowerInvariant();
            bool ff = name is "firefox" or "librewolf" or "waterfox" or "floorp" or "zen";
            string flag = mode == Mode.NewWindow ? (ff ? "-new-window" : "--new-window")
                        : ff ? "-private-window" : name == "msedge" ? "-inprivate" : name == "opera" ? "--private" : "--incognito";
            try { Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, ArgumentList = { flag, u.AbsoluteUri } })?.Dispose(); return null; }
            catch (Exception e) { SystemService.OpenWeb(u.AbsoluteUri); return "Не получилось (" + e.Message + ") — открыл обычным окном"; }
        }

        /// <summary>Поиск в Google (или ссылка, если это адрес).</summary>
        public static string OpenOrSearch(string text, Mode mode = Mode.Normal)
        {
            var links = FindLinks(text);
            if (links.Count > 0 && OnlyLinks(text.Trim(), links)) return Open(links[0], mode);
            return Open("https://www.google.com/search?q=" + Uri.EscapeDataString(text.Trim()), mode);
        }

        /// <summary>exe браузера по умолчанию (выбор пользователя для https).</summary>
        public static string DefaultExe()
        {
            try
            {
                using var uc = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\https\UserChoice");
                var progId = uc?.GetValue("ProgId") as string;
                if (string.IsNullOrEmpty(progId)) return null;
                using var cmd = Registry.ClassesRoot.OpenSubKey(progId + @"\shell\open\command");
                var line = cmd?.GetValue("") as string ?? "";
                var m = Regex.Match(line, "^\"([^\"]+\\.exe)\"|^(\\S+\\.exe)", RegexOptions.IgnoreCase);
                var exe = m.Success ? (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value) : null;
                return exe != null && File.Exists(exe) ? exe : null;
            }
            catch { return null; }
        }

        public static string DefaultName()
        {
            var exe = DefaultExe();
            return exe == null ? null : NameOf(Path.GetFileNameWithoutExtension(exe));
        }

        // ------------------------------------------------------------------
        //  Недавние ссылки и избранное
        // ------------------------------------------------------------------

        static List<string> _recent;
        static string RecentFile => Path.Combine(Store.Dir, "links.json");

        public static List<string> Recent()
        {
            if (_recent == null)
            {
                try { _recent = File.Exists(RecentFile) ? JsonSerializer.Deserialize<List<string>>(File.ReadAllText(RecentFile)) ?? new() : new(); }
                catch { _recent = new(); }
            }
            lock (_recent) return _recent.ToList();
        }

        static void Remember(string url)
        {
            Recent();
            lock (_recent)
            {
                _recent.Remove(url);
                _recent.Insert(0, url);
                if (_recent.Count > 20) _recent.RemoveRange(20, _recent.Count - 20);
                if (Store.ReadOnly) return;   // самопроверки ничего не пишут
                try { File.WriteAllText(RecentFile, JsonSerializer.Serialize(_recent)); } catch { }
            }
        }

        /// <summary>Избранное: «Имя|ссылка;…» (настройка BrowserFavs).</summary>
        public static List<(string Name, string Url)> Favs() =>
            (Store.Settings.BrowserFavs ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Split('|', 2))
                .Where(p => p.Length == 2 && Uri.TryCreate(p[1].Trim(), UriKind.Absolute, out _)).Select(p => (p[0].Trim(), p[1].Trim())).ToList();

        public static void SetFavs(IEnumerable<(string Name, string Url)> list) =>
            Store.Settings.BrowserFavs = string.Join(";", list.Select(f => f.Name.Replace(";", ",").Replace("|", "/") + "|" + f.Url));

        /// <summary>Короткое имя сайта для кнопки: youtube.com/watch… → youtube.com.</summary>
        public static string Short(string url) =>
            Uri.TryCreate(url, UriKind.Absolute, out var u) ? Regex.Replace(u.Host, @"^www\.", "") + (u.AbsolutePath.Length > 1 ? u.AbsolutePath : "") : url;
    }
}
