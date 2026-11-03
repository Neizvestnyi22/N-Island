using System.Linq;

namespace NIsland.Core
{
    /// <summary>Стилевой пресет острова.</summary>
    /// <param name="Bg1">Цвет фона сверху-слева.</param>
    /// <param name="Bg2">Цвет фона снизу-справа.</param>
    /// <param name="Tint">Затемнение стекла, %.</param>
    /// <param name="Glass">Матовое стекло в раскрытой чёлке.</param>
    /// <param name="GlassCollapsed">Стекло и в свёрнутой чёлке.</param>
    /// <param name="Rim">Яркость светлого ободка 0..1.</param>
    /// <param name="Sheen">Блик сверху 0..1.</param>
    public record StylePreset(string Key, string Name, string Bg1, string Bg2, int Tint, bool Glass, bool GlassCollapsed,
                              double Rim, string RimColor, double Sheen, string Accent, string Style);

    public static class Presets
    {
        public static readonly StylePreset[] All =
        {
            new("liquid",   "Жидкое стекло",  "#0B0B12", "#1C1C2A", 20, true,  true,  0.60, "#FFFFFF", 0.18, "#64D2FF", "floating"),
            new("frosted",  "Матовое стекло", "#000000", "#101014", 38, true,  false, 0.24, "#FFFFFF", 0.08, "#0A84FF", "classic"),
            new("smoke",    "Дымка",          "#2A2A30", "#141418", 48, true,  true,  0.32, "#FFFFFF", 0.10, "#BF5AF2", "floating"),
            new("ice",      "Лёд",            "#22313F", "#0E1820", 24, true,  true,  0.70, "#DDF4FF", 0.24, "#64D2FF", "floating"),
            new("iphone",   "iPhone",         "#000000", "#000000", 30, false, false, 0.00, "#FFFFFF", 0.00, "#0A84FF", "classic"),
            new("aurora",   "Аврора",         "#3B1C8C", "#0A4AA8", 45, true,  false, 0.38, "#C9B8FF", 0.12, "#BF5AF2", "floating"),
            new("sunset",   "Закат",          "#9C1F4A", "#B8560A", 45, true,  false, 0.38, "#FFD3B0", 0.12, "#FFD60A", "floating"),
            new("ocean",    "Океан",          "#003B5C", "#00616E", 45, true,  false, 0.34, "#9BE7FF", 0.12, "#64D2FF", "floating"),
            new("mint",     "Мята",           "#00594F", "#0E5A2A", 45, true,  false, 0.34, "#B6FFE4", 0.12, "#30D158", "floating"),
            new("rose",     "Розовое золото", "#6E3640", "#8A5560", 45, true,  false, 0.42, "#FFD9DE", 0.14, "#FFB3C1", "floating"),
            new("neon",     "Неон",           "#000000", "#08000A", 30, false, false, 0.95, "#FF2D95", 0.00, "#FF2D95", "floating"),
            new("matrix",   "Матрица",        "#000000", "#001A06", 30, false, false, 0.75, "#30D158", 0.00, "#30D158", "classic"),
            new("graphite", "Графит",         "#1C1C1E", "#2C2C2E", 30, false, false, 0.16, "#FFFFFF", 0.06, "#8E8E93", "classic"),
            new("midnight", "Полночь",        "#0B1026", "#1B2A6B", 30, false, false, 0.26, "#8FA8FF", 0.08, "#5E5CE6", "floating"),
            // чистый чёрный: на OLED-экранах пиксели выключены — экономит заряд и сливается с вырезом камеры
            new("amoled",   "AMOLED",         "#000000", "#000000", 0,  false, false, 0.10, "#3A3A3C", 0.00, "#FFFFFF", "classic"),
        };

        /// <summary>«Свой стиль» — собирается из настроек кастомизации.</summary>
        public static StylePreset Custom
        {
            get
            {
                var s = Store.Settings;
                return new("custom", "Свой стиль", s.CustomBg1, s.CustomBg2, s.GlassTint, s.Glass, s.CustomGlassCollapsed,
                    s.CustomRim / 100.0, s.CustomRimColor, s.CustomSheen / 100.0, s.Accent, s.Style);
            }
        }

        public static StylePreset Get(string key) => key == "custom" ? Custom : All.FirstOrDefault(p => p.Key == key) ?? All[0];

        public static StylePreset Current => Get(Store.Settings.Preset);

        public static void Apply(StylePreset p)
        {
            var s = Store.Settings;
            s.Glass = p.Glass;
            s.GlassTint = p.Tint;
            s.Accent = p.Accent;
            s.Style = p.Style;
            if (p.Key == "amoled") s.WindowTheme = "amoled";   // окна настроек — тоже чисто чёрные
            s.Preset = p.Key; // последним — остров перерисуется один раз со всеми полями
        }
    }

    /// <summary>Режимы работы острова.</summary>
    public record IslandMode(string Key, string Name, string Glyph, string Desc);

    public static class Modes
    {
        public static readonly IslandMode[] All =
        {
            new("normal",  "Обычный",        "", "Всё включено"),
            new("dnd",     "Не беспокоить",  "", "Без уведомлений и всплывашек, звонки — показываются"),
            new("focus",   "Фокус",          "", "Не беспокоить + таймер помодоро в чёлке"),
            new("minimal", "Минимум",        "", "Крошечная чёлка без часов"),
            new("night",   "Ночь",           "", "Приглушённая чёлка, без звуков"),
            new("game",    "Игра",           "", "Чёлка спрятана, только важное"),
            new("present", "Презентация",    "", "Полностью невидима, ничего не всплывает"),
        };

        static Settings S => Store.Settings;

        /// <summary>Не показывать обычные уведомления и мелкие всплывашки.</summary>
        public static bool Dnd => S.Mode is "dnd" or "focus" or "game" or "present";
        public static bool Silent => Dnd || S.Mode == "night" || !S.Sounds;
        /// <summary>Чёлка свёрнута до тонкой полоски.</summary>
        public static bool Hidden => S.Hidden || S.Mode is "game" or "present";
        public static IslandMode Current => All.FirstOrDefault(m => m.Key == S.Mode) ?? All[0];
    }
}
