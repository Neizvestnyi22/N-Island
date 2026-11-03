using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace NIsland.Core
{
    /// <summary>
    /// Языки интерфейса. Интерфейс написан по-русски; для других языков встроены словари Assets/i18n/*.json
    /// (их собирает tools/I18nGen). Перевод применяется к элементам при загрузке и при смене их текста —
    /// поэтому работает и для старых окон, и для всплывашек. Контент пользователя (песни, заметки, уведомления)
    /// не переводится: он не совпадает со строками интерфейса.
    /// </summary>
    public static class I18n
    {
        public static readonly (string Code, string Name)[] Languages =
        {
            ("ru", "Русский"), ("en", "English"), ("uk", "Українська"),
        };

        public static string Current { get; private set; } = "ru";

        /// <summary>Языки, словари которых встроены в эту сборку (русский — всегда).</summary>
        public static IEnumerable<(string Code, string Name)> Available
        {
            get
            {
                var res = typeof(I18n).Assembly.GetManifestResourceNames();
                return Languages.Where(l => l.Code == "ru" || res.Contains($"i18n.{l.Code}.json"));
            }
        }
        public static bool Active => Current != "ru" && _exact != null;

        static Dictionary<string, string> _exact;
        static Dictionary<int, List<(string Key, string Value)>> _fragments;   // ключ — первые два символа (без выделения строк)
        static int Pair(string s, int i) => (s[i] << 16) | s[i + 1];
        static readonly ConditionalWeakTable<DependencyObject, object> Hooked = new();
        static readonly DependencyPropertyDescriptor TextDesc = DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock));
        [ThreadStatic] static bool _busy;

        /// <summary>Язык из настроек; «auto» — по языку Windows (русский для СНГ, украинский, иначе английский или язык из списка).</summary>
        public static string Resolve(string setting)
        {
            var avail = Available.Select(l => l.Code).ToList();
            if (!string.IsNullOrEmpty(setting) && setting != "auto" && avail.Contains(setting)) return setting;
            var sys = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            if (sys is "ru" or "be" or "kk" or "ky" or "uz" or "tg" or "hy" or "az" or "ka") return "ru";
            return avail.Contains(sys) ? sys : avail.Contains("en") ? "en" : "ru";
        }

        public static void Init()
        {
            Current = Resolve(Store.Settings.Language);
            if (Current == "ru") return;
            try
            {
                using var s = typeof(I18n).Assembly.GetManifestResourceStream($"i18n.{Current}.json");
                if (s == null) { Current = "ru"; return; }
                _exact = JsonSerializer.Deserialize<Dictionary<string, string>>(s) ?? new();
            }
            catch (Exception e) { Log.Info("i18n: " + e.Message); Current = "ru"; return; }

            // куски для склеенных строк («Освобождено » + размер) — по первым двум символам, длинные первыми
            _fragments = new();
            foreach (var (k, v) in _exact)
            {
                if (k.Trim().Length < 3) continue;
                var key = Pair(k, 0);
                if (!_fragments.TryGetValue(key, out var list)) _fragments[key] = list = new();
                list.Add((k, v));
            }
            foreach (var l in _fragments.Values) l.Sort((a, b) => b.Key.Length.CompareTo(a.Key.Length));

            // Loaded WPF рассылает только элементам со своими обработчиками (обычные TextBlock его не получают),
            // а SizeChanged приходит каждому элементу при первой раскладке — до отрисовки, так что русский текст даже не мелькнёт
            EventManager.RegisterClassHandler(typeof(FrameworkElement), FrameworkElement.SizeChangedEvent, new SizeChangedEventHandler(OnSized), true);
            EventManager.RegisterClassHandler(typeof(FrameworkElement), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnLoaded), true);
        }

        static bool IsCyr(char c) => c is >= 'А' and <= 'я' or 'Ё' or 'ё';

        /// <summary>Перевести строку интерфейса (если язык не русский и строка известна).</summary>
        public static string T(string s)
        {
            if (!Active || string.IsNullOrEmpty(s) || !s.Any(IsCyr)) return s;
            if (_exact.TryGetValue(s, out var v)) return v;
            var trimmed = s.Trim();
            if (trimmed.Length != s.Length && _exact.TryGetValue(trimmed, out v))
                return s[..(s.Length - s.TrimStart().Length)] + v + s[s.TrimEnd().Length..];
            if (s.Length > 600) return s;   // длинный текст — это ответ ИИ, заметка или песня, а не интерфейс
            if (_fragments == null) return s;
            // склеенная строка: заменяем известные куски, но только если они покрывают большую часть текста —
            // иначе это пользовательский текст (песня, заметка), и его не трогаем
            var sb = new StringBuilder(s.Length + 16);
            int i = 0, covered = 0;
            while (i < s.Length)
            {
                if (i + 2 <= s.Length && _fragments.TryGetValue(Pair(s, i), out var list))
                {
                    bool hit = false;
                    foreach (var (k, val) in list)
                    {
                        if (k.Length > s.Length - i || string.CompareOrdinal(s, i, k, 0, k.Length) != 0) continue;
                        bool startOk = i == 0 || !char.IsLetter(s[i - 1]) || !char.IsLetter(k[0]);
                        bool endOk = i + k.Length == s.Length || !char.IsLetter(s[i + k.Length]) || !char.IsLetter(k[^1]);
                        if (!startOk || !endOk) continue;
                        sb.Append(val);
                        covered += k.Count(IsCyr);
                        i += k.Length;
                        hit = true;
                        break;
                    }
                    if (hit) continue;
                }
                sb.Append(s[i]);
                i++;
            }
            // короткие служебные слова и единицы («2 из 25», «70 ГБ») — в словаре их нет, они слишком короткие для кусков
            var res = sb.ToString();
            if (ShortWords.TryGetValue(Current, out var words) && res.Any(IsCyr))
            {
                res = System.Text.RegularExpressions.Regex.Replace(res, @"(?<![А-Яа-яЁё])[А-Яа-яЁё]{1,4}\.?(?![А-Яа-яЁё])", m =>
                {
                    if (!words.TryGetValue(m.Value, out var w)) return m.Value;
                    covered += m.Value.Count(IsCyr);
                    return w;
                });
            }
            int total = s.Count(IsCyr);
            return covered > 0 && covered >= total * 0.6 ? res : s;
        }

        static readonly Dictionary<string, Dictionary<string, string>> ShortWords = new()
        {
            ["en"] = new() { ["ЦП"] = "CPU", ["ОЗУ"] = "RAM", ["из"] = "of",["ГБ"] = "GB", ["МБ"] = "MB", ["КБ"] = "KB", ["ТБ"] = "TB", ["мин"] = "min", ["ч"] = "h", ["с"] = "s", ["дн."] = "d", ["шт."] = "pcs", ["и"] = "and" },
            ["uk"] = new() { ["из"] = "з", ["мин"] = "хв", ["дн."] = "дн.", ["шт."] = "шт.", ["и"] = "і" },
        };

        // ------------------------------------------------------------------
        //  Применение к элементам WPF
        // ------------------------------------------------------------------

        static void OnSized(object sender, SizeChangedEventArgs e) => OnLoaded(sender, e);

        static void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.Tag is "no-translate") return;   // названия языков и т. п. — как есть
            Apply(fe);
            if (fe is TextBlock tb && !Hooked.TryGetValue(tb, out _) && !BindingOperations.IsDataBound(tb, TextBlock.TextProperty))
            {
                // текст может смениться позже (статусы, всплывашки) — переводим и его
                Hooked.Add(tb, null);
                TextDesc.AddValueChanged(tb, OnTextChanged);
                tb.Unloaded += Unhook;
            }
        }

        static void Unhook(object sender, RoutedEventArgs e)
        {
            if (sender is not TextBlock tb) return;
            TextDesc.RemoveValueChanged(tb, OnTextChanged);
            tb.Unloaded -= Unhook;
            Hooked.Remove(tb);
        }

        static void OnTextChanged(object sender, EventArgs e)
        {
            if (_busy || sender is not TextBlock tb) return;
            var t = T(tb.Text);
            if (ReferenceEquals(t, tb.Text) || t == tb.Text) return;
            _busy = true;
            try { tb.Text = t; } finally { _busy = false; }
        }

        static void Apply(FrameworkElement fe)
        {
            switch (fe)
            {
                case TextBlock tb when !BindingOperations.IsDataBound(tb, TextBlock.TextProperty):
                    var t = T(tb.Text);
                    if (t != tb.Text) { _busy = true; try { tb.Text = t; } finally { _busy = false; } }
                    break;
                case HeaderedContentControl h when h.Header is string hs:
                    h.Header = T(hs);
                    break;
                case ContentControl cc when cc.Content is string cs && fe is not Window:
                    cc.Content = T(cs);
                    break;
                case TextBox box when box.Tag is string hint:
                    box.Tag = T(hint);   // подсказка в пустом поле; сам текст — пользовательский, не трогаем
                    break;
            }
            if (fe is Window w) w.Title = T(w.Title);
            if (fe.ToolTip is string tip) fe.ToolTip = T(tip);
        }

        /// <summary>Меню трея (WinForms) — переводится при каждом открытии.</summary>
        public static void Apply(System.Windows.Forms.ToolStripItemCollection items)
        {
            if (!Active) return;
            foreach (System.Windows.Forms.ToolStripItem i in items)
            {
                i.Text = T(i.Text);
                if (i is System.Windows.Forms.ToolStripMenuItem m && m.HasDropDownItems) Apply(m.DropDownItems);
            }
        }
    }
}
