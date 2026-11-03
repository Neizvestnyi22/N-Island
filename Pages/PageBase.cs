using System.Windows.Controls;
using System.Windows.Media;
using NIsland.Core;

namespace NIsland.Pages
{
    /// <summary>Страница раскрытой чёлки. Создаётся лениво — при первом открытии.</summary>
    public class PageBase : UserControl
    {
        /// <summary>Дополнительная ширина острова (например, колонка с текстом песни).</summary>
        public virtual double ExtraWidth => 0;
        /// <summary>Дополнительная высота (до 40 px), если странице тесно.</summary>
        public virtual double ExtraHeight => 0;

        public virtual void OnShown() { }
        public virtual void OnHidden() { }

        protected static Settings S => Store.Settings;
        protected static void Relayout() => App.Island?.Relayout();
        protected bool Visible => App.Island?.IsShowing(this) == true;

        public PageBase()
        {
            Foreground = Brushes.White;
            Focusable = false;
        }
    }
}
