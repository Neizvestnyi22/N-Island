using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace NIsland.Services
{
    /// <summary>Тексты песен: латиница для чужих алфавитов, экспорт в .lrc, простой текст.</summary>
    public static class LyricsTools
    {
        // ------------------------------------------------------------------
        //  Транслитерация
        // ------------------------------------------------------------------

        static readonly Dictionary<char, string> Cyr = new()
        {
            ['а'] = "a", ['б'] = "b", ['в'] = "v", ['г'] = "g", ['д'] = "d", ['е'] = "e", ['ё'] = "yo", ['ж'] = "zh", ['з'] = "z", ['и'] = "i", ['й'] = "y",
            ['к'] = "k", ['л'] = "l", ['м'] = "m", ['н'] = "n", ['о'] = "o", ['п'] = "p", ['р'] = "r", ['с'] = "s", ['т'] = "t", ['у'] = "u", ['ф'] = "f",
            ['х'] = "kh", ['ц'] = "ts", ['ч'] = "ch", ['ш'] = "sh", ['щ'] = "shch", ['ъ'] = "", ['ы'] = "y", ['ь'] = "", ['э'] = "e", ['ю'] = "yu", ['я'] = "ya",
            ['і'] = "i", ['ї'] = "yi", ['є'] = "ye", ['ґ'] = "g", ['ў'] = "w",
        };

        static readonly Dictionary<char, string> Greek = new()
        {
            ['α'] = "a", ['β'] = "v", ['γ'] = "g", ['δ'] = "d", ['ε'] = "e", ['ζ'] = "z", ['η'] = "i", ['θ'] = "th", ['ι'] = "i", ['κ'] = "k", ['λ'] = "l",
            ['μ'] = "m", ['ν'] = "n", ['ξ'] = "x", ['ο'] = "o", ['π'] = "p", ['ρ'] = "r", ['σ'] = "s", ['ς'] = "s", ['τ'] = "t", ['υ'] = "y", ['φ'] = "f",
            ['χ'] = "ch", ['ψ'] = "ps", ['ω'] = "o", ['ά'] = "a", ['έ'] = "e", ['ή'] = "i", ['ί'] = "i", ['ό'] = "o", ['ύ'] = "y", ['ώ'] = "o",
        };

        // корейский: слог = начальная + гласная + конечная (Unicode-разложение)
        static readonly string[] Lead = { "g", "kk", "n", "d", "tt", "r", "m", "b", "pp", "s", "ss", "", "j", "jj", "ch", "k", "t", "p", "h" };
        static readonly string[] Vowel = { "a", "ae", "ya", "yae", "eo", "e", "yeo", "ye", "o", "wa", "wae", "oe", "yo", "u", "wo", "we", "wi", "yu", "eu", "ui", "i" };
        static readonly string[] Tail = { "", "k", "k", "k", "n", "n", "n", "t", "l", "k", "m", "l", "l", "l", "p", "l", "m", "p", "p", "t", "t", "ng", "t", "t", "k", "t", "p", "t" };

        /// <summary>Есть ли в строке что переводить в латиницу.</summary>
        public static bool NeedsRoman(string s) =>
            !string.IsNullOrEmpty(s) && s.Any(c => c is >= 'Ѐ' and <= 'ӿ' or >= 'Ͱ' and <= 'Ͽ' or >= '가' and <= '힣');

        public static string Romanize(string s)
        {
            if (!NeedsRoman(s)) return null;
            var sb = new StringBuilder(s.Length + 8);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                char lo = char.ToLowerInvariant(c);
                bool up = c != lo;
                string r = null;
                if (Cyr.TryGetValue(lo, out var cr)) r = cr;
                else if (Greek.TryGetValue(lo, out var gr)) r = gr;
                else if (c is >= '가' and <= '힣')
                {
                    int code = c - 0xAC00, l = code / 588, v = code % 588 / 28, t = code % 28;
                    r = Lead[l] + Vowel[v] + Tail[t];
                    if (i + 1 < s.Length && s[i + 1] is >= '가' and <= '힣') r += "";
                }
                if (r == null) { sb.Append(c); continue; }
                if (up && r.Length > 0) r = char.ToUpperInvariant(r[0]) + r[1..];
                sb.Append(r);
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------
        //  Экспорт
        // ------------------------------------------------------------------

        public static string ToLrc(IReadOnlyList<LyricLine> lines, string title, string artist, bool synced)
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(artist)) sb.Append("[ar:").Append(artist).AppendLine("]");
            if (!string.IsNullOrEmpty(title)) sb.Append("[ti:").Append(title).AppendLine("]");
            sb.AppendLine("[re:N-Island]");
            foreach (var l in lines)
            {
                var text = l.Text == "♪" ? "" : l.Text;
                if (synced) sb.Append($"[{(int)l.Time.TotalMinutes:00}:{l.Time.Seconds:00}.{l.Time.Milliseconds / 10:00}]");
                sb.AppendLine(text);
            }
            return sb.ToString();
        }

        public static string PlainText(IReadOnlyList<LyricLine> lines) =>
            string.Join(Environment.NewLine, lines.Select(l => l.Text == "♪" ? "" : l.Text)).Trim();

        /// <summary>Сохранить в «Музыка\Тексты песен\Исполнитель - Название.lrc». Возвращает путь.</summary>
        public static string SaveLrc(IReadOnlyList<LyricLine> lines, string title, string artist, bool synced)
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "Lyrics");
            Directory.CreateDirectory(dir);
            var name = string.IsNullOrEmpty(artist) ? title : artist + " - " + title;
            name = string.Concat((name ?? "lyrics").Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
            if (name.Length > 120) name = name[..120];
            var path = Path.Combine(dir, name + (synced ? ".lrc" : ".txt"));
            File.WriteAllText(path, synced ? ToLrc(lines, title, artist, true) : PlainText(lines), new UTF8Encoding(false));
            return path;
        }
    }
}
