using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace NIsland.Pages
{
    /// <summary>
    /// Анимации появления строки текста песни — общие для плеера и текста на рабочем столе.
    /// Анимируется сам TextBlock (его прозрачность, сдвиг, масштаб, маска, эффект), а не строка-контейнер:
    /// у контейнера свои анимации (увеличение текущей строки, затухание дальних по стилю).
    /// </summary>
    public static class LyricsAnim
    {
        public static readonly (string Key, string Name)[] Kinds =
        {
            ("none", "Без анимации"), ("fade", "Проявление"), ("rise", "Всплытие"), ("drop", "Падение"), ("slide", "Выезд"),
            ("zoom", "Приближение"), ("flip", "Переворот"), ("blur", "Из размытия"), ("glow", "Вспышка"), ("wipe", "Шторка"),
            ("typewriter", "Печатная машинка"), ("random", "Случайно"),
        };

        static readonly Random Rnd = new();
        // эффект, который был у строки до анимации (тень на рабочем столе, неон) — вернуть после «размытия» и «вспышки»
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<FrameworkElement, Effect> Prev = new();

        /// <summary>Сыграть анимацию появления на элементе (обычно TextBlock строки).</summary>
        public static void Play(FrameworkElement el, string kind, int ms, Color glow, bool centered = false)
        {
            if (el == null) return;
            if (kind == "random") kind = Kinds[1 + Rnd.Next(Kinds.Length - 2)].Key;
            Reset(el);
            if (kind is null or "none" || ms <= 0) return;
            var dur = TimeSpan.FromMilliseconds(Math.Clamp(ms, 80, 3000));
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var (sc, tr) = Transforms(el, centered);
            void Fade(double from) => el.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(from, 1, dur) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
            switch (kind)
            {
                case "fade":
                    Fade(0.05);
                    break;
                case "rise":
                    Fade(0.1);
                    tr.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(16, 0, dur) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
                    break;
                case "drop":
                    Fade(0.2);
                    tr.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-18, 0, dur) { EasingFunction = new BounceEase { Bounces = 2, Bounciness = 3, EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
                    break;
                case "slide":
                    Fade(0.1);
                    tr.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-28, 0, dur) { EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
                    break;
                case "zoom":
                {
                    Fade(0.15);
                    var a = new DoubleAnimation(0.78, 1, dur) { EasingFunction = new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop };
                    sc.BeginAnimation(ScaleTransform.ScaleXProperty, a);
                    sc.BeginAnimation(ScaleTransform.ScaleYProperty, a);
                    break;
                }
                case "flip":
                    sc.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.05, 1, dur) { EasingFunction = new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
                    break;
                case "blur":
                {
                    Fade(0.3);
                    var blur = new BlurEffect { Radius = 12, RenderingBias = RenderingBias.Performance };
                    Swap(el, blur);
                    var a = new DoubleAnimation(12, 0, dur) { EasingFunction = ease };
                    a.Completed += (_, __) => { if (el.Effect == blur) Restore(el); };
                    blur.BeginAnimation(BlurEffect.RadiusProperty, a);
                    break;
                }
                case "glow":
                {
                    var sh = new DropShadowEffect { Color = glow, ShadowDepth = 0, BlurRadius = 28, Opacity = 1, RenderingBias = RenderingBias.Performance };
                    Swap(el, sh);
                    var a = new DoubleAnimation(1, 0, dur.Add(dur)) { EasingFunction = ease };
                    a.Completed += (_, __) => { if (el.Effect == sh) Restore(el); };
                    sh.BeginAnimation(DropShadowEffect.OpacityProperty, a);
                    break;
                }
                case "wipe":
                case "typewriter":
                {
                    // маска: слева видно, справа прозрачно; граница едет слева направо (у машинки — рывками)
                    var mask = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
                    var s1 = new GradientStop(Colors.Black, 0);
                    var s2 = new GradientStop(Colors.Transparent, kind == "wipe" ? 0.08 : 0.001);
                    mask.GradientStops.Add(s1);
                    mask.GradientStops.Add(s2);
                    el.OpacityMask = mask;
                    Timeline a1, a2;
                    if (kind == "wipe")
                    {
                        a1 = new DoubleAnimation(-0.1, 1.0, dur) { EasingFunction = ease };
                        a2 = new DoubleAnimation(0, 1.1, dur) { EasingFunction = ease };
                    }
                    else
                    {
                        var k1 = new DoubleAnimationUsingKeyFrames { Duration = dur };
                        var k2 = new DoubleAnimationUsingKeyFrames { Duration = dur };
                        const int Steps = 14;
                        for (int i = 0; i <= Steps; i++)
                        {
                            var at = KeyTime.FromPercent(i / (double)Steps);
                            k1.KeyFrames.Add(new DiscreteDoubleKeyFrame(i / (double)Steps, at));
                            k2.KeyFrames.Add(new DiscreteDoubleKeyFrame(i / (double)Steps + 0.001, at));
                        }
                        a1 = k1; a2 = k2;
                    }
                    a2.Completed += (_, __) => { if (el.OpacityMask == mask) el.OpacityMask = null; };
                    s1.BeginAnimation(GradientStop.OffsetProperty, (AnimationTimeline)a1);
                    s2.BeginAnimation(GradientStop.OffsetProperty, (AnimationTimeline)a2);
                    break;
                }
            }
        }

        /// <summary>Лесенка: строки въезжают по очереди (при смене песни).</summary>
        public static void Cascade(FrameworkElement el, int index, int ms)
        {
            Reset(el);
            var (_, tr) = Transforms(el, false);
            var begin = TimeSpan.FromMilliseconds(Math.Min(index, 14) * 45);
            var dur = TimeSpan.FromMilliseconds(Math.Clamp(ms, 150, 1500));
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            el.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, dur) { BeginTime = begin, EasingFunction = ease, FillBehavior = FillBehavior.Stop });
            el.Opacity = 1;
            tr.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(22, 0, dur) { BeginTime = begin, EasingFunction = ease, FillBehavior = FillBehavior.Stop });
        }

        /// <summary>Снять прошлую анимацию (строка снова стала текущей раньше, чем доиграла).</summary>
        public static void Reset(FrameworkElement el)
        {
            el.BeginAnimation(UIElement.OpacityProperty, null);
            if (el.RenderTransform is TransformGroup g)
                foreach (var t in g.Children)
                {
                    if (t is ScaleTransform s) { s.BeginAnimation(ScaleTransform.ScaleXProperty, null); s.BeginAnimation(ScaleTransform.ScaleYProperty, null); }
                    if (t is TranslateTransform tt) { tt.BeginAnimation(TranslateTransform.XProperty, null); tt.BeginAnimation(TranslateTransform.YProperty, null); }
                }
            Restore(el);
            el.OpacityMask = null;
        }

        static void Swap(FrameworkElement el, Effect temp)
        {
            Prev.AddOrUpdate(el, el.Effect);
            el.Effect = temp;
        }

        static void Restore(FrameworkElement el)
        {
            if (!Prev.TryGetValue(el, out var e)) return;
            Prev.Remove(el);
            el.Effect = e;
        }

        static (ScaleTransform, TranslateTransform) Transforms(FrameworkElement el, bool centered)
        {
            if (el.RenderTransform is TransformGroup g && g.Children.Count == 2 && g.Children[0] is ScaleTransform s0 && g.Children[1] is TranslateTransform t0)
                return (s0, t0);
            var s = new ScaleTransform(1, 1);
            var t = new TranslateTransform();
            el.RenderTransform = new TransformGroup { Children = { s, t } };
            el.RenderTransformOrigin = centered ? new Point(0.5, 0.5) : new Point(0, 0.5);
            return (s, t);
        }
    }
}
