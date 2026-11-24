using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;

namespace Beutl.Controls;

// Short UI motion driven by the frames themselves: each frame writes the value for its own timestamp, so an animation
// never starts part-way through after a slow layout, and what it leaves behind is an ordinary local value.
public static partial class UiMotion
{
    public static readonly TimeSpan Fast = TimeSpan.FromMilliseconds(83);
    public static readonly TimeSpan Normal = TimeSpan.FromMilliseconds(167);

    // Fluent's decelerate curve: quick at first, settling at the end.
    public static readonly Easing Decelerate = new SplineEasing(0, 0, 0, 1);

    // How far, in device-independent pixels, something that appears moves into place.
    public const double RevealOffset = 4;

    private static readonly ConditionalWeakTable<Visual, object> s_runs = new();
    private static readonly ConditionalWeakTable<Visual, TranslateTransform> s_slides = new();

    // Calls apply with eased progress from 0 to 1 on each frame, then completed. A newer run on the same target
    // supersedes this one, which then stops without calling completed. A target not in a window finishes at once.
    public static void Run(Visual target, TimeSpan duration, Action<double> apply, Action? completed = null)
    {
        Cancel(target);
        var run = new object();
        s_runs.AddOrUpdate(target, run);

        if (TopLevel.GetTopLevel(target) is not { } topLevel || duration <= TimeSpan.Zero)
        {
            Finish();
            return;
        }

        TimeSpan? start = null;
        topLevel.RequestAnimationFrame(Frame);

        void Frame(TimeSpan now)
        {
            if (!IsCurrent(target, run)) return;
            start ??= now;
            double progress = Math.Clamp((now - start.Value) / duration, 0, 1);
            if (progress >= 1)
            {
                Finish();
                return;
            }

            apply(Decelerate.Ease(progress));
            topLevel.RequestAnimationFrame(Frame);
        }

        void Finish()
        {
            s_runs.Remove(target);
            apply(1);
            completed?.Invoke();
        }
    }

    // Stops a run on the target without calling its completed callback, and takes away the offset of a reveal.
    public static void Cancel(Visual target)
    {
        s_runs.Remove(target);
        if (s_slides.TryGetValue(target, out TranslateTransform? slide))
        {
            s_slides.Remove(target);
            if (ReferenceEquals(target.RenderTransform, slide))
                target.RenderTransform = null;
        }
    }

    // Fades the target in while it moves down into place from just above. A target with a transform of its own, or
    // where Slides is off, only fades.
    public static void Reveal(Visual target, TimeSpan duration, Action? completed = null)
    {
        Cancel(target);
        TranslateTransform? slide = null;
        if (target.RenderTransform is null && GetSlides(target))
        {
            slide = new TranslateTransform(0, -RevealOffset);
            target.RenderTransform = slide;
        }

        target.Opacity = 0;
        Run(target, duration, progress =>
        {
            target.Opacity = progress;
            // Something else (a drag, say) may have taken over the transform meanwhile.
            if (slide != null && ReferenceEquals(target.RenderTransform, slide))
                slide.Y = -RevealOffset * (1 - progress);
        }, () =>
        {
            if (slide != null && ReferenceEquals(target.RenderTransform, slide))
                target.RenderTransform = null;
            completed?.Invoke();
        });
        // Run cancels what came before it, so the slide is recorded after it starts.
        if (slide != null && ReferenceEquals(target.RenderTransform, slide))
            s_slides.AddOrUpdate(target, slide);
    }

    // Whether what appears inside moves into place or only fades. Off where something laid out beside it stays put
    // while it moves, such as the ports on a node's edge beside the node's rows.
    [GeneratedAttachedProperty(DefaultValue = true, Inherits = true)]
    public static partial bool GetSlides(Visual visual);

    private static bool IsCurrent(Visual target, object run)
    {
        return s_runs.TryGetValue(target, out object? current) && ReferenceEquals(current, run);
    }
}
