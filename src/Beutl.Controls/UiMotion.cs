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

    // Starting scale of a subtle pop-in reveal (badges, status indicators).
    public const double PopStartScale = 0.92;

    private static readonly ConditionalWeakTable<Visual, object> s_runs = new();
    private static readonly ConditionalWeakTable<Visual, Transform> s_transforms = new();

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

    // Stops a run on the target without calling its completed callback, and takes away any transform owned by a reveal.
    public static void Cancel(Visual target)
    {
        s_runs.Remove(target);
        if (s_transforms.TryGetValue(target, out Transform? transform))
        {
            s_transforms.Remove(target);
            if (ReferenceEquals(target.RenderTransform, transform))
                target.RenderTransform = null;
        }
    }

    // Fades the target in without moving it.
    public static void FadeIn(Visual target, TimeSpan duration, Action? completed = null)
    {
        Cancel(target);
        target.Opacity = 0;
        Run(target, duration, progress => target.Opacity = progress, completed);
    }

    // Fades the target out from its current opacity.
    public static void FadeOut(Visual target, TimeSpan duration, Action? completed = null)
    {
        double from = target.Opacity;
        Run(target, duration, progress => target.Opacity = from * (1 - progress), completed);
    }

    // Moves a drawn value from `from` to `to` over Fast; one already within tolerance of `to` settles at once.
    internal static void Tween(Visual target, double from, double to, double tolerance, Action<double> apply)
    {
        if (Math.Abs(from - to) < tolerance)
        {
            Cancel(target);
            apply(to);
            return;
        }

        Run(target, Fast, progress => apply(from + ((to - from) * progress)));
    }

    // Fades the target in while it moves down into place from just above. A target with a transform of its own, or
    // where Slides is off, only fades.
    public static void Reveal(Visual target, TimeSpan duration, Action? completed = null)
    {
        Cancel(target);
        TranslateTransform? slide = target.RenderTransform is null && GetSlides(target)
            ? new TranslateTransform(0, -RevealOffset)
            : null;
        RevealWith(target, duration, slide,
            static (transform, progress) => transform.Y = -RevealOffset * (1 - progress), completed);
    }

    // Fades the target in while growing gently into place. A target with a transform of its own only fades.
    public static void Pop(Visual target, TimeSpan duration, double startScale = PopStartScale, Action? completed = null)
    {
        Cancel(target);
        ScaleTransform? grow = target.RenderTransform is null ? new ScaleTransform(startScale, startScale) : null;
        RevealWith(target, duration, grow, (transform, progress) =>
        {
            double scale = startScale + ((1 - startScale) * progress);
            transform.ScaleX = scale;
            transform.ScaleY = scale;
        }, completed);
    }

    // Fades the target in while update moves the transform it is given, if any, into place.
    private static void RevealWith<T>(
        Visual target, TimeSpan duration, T? transform, Action<T, double> update, Action? completed)
        where T : Transform
    {
        if (transform != null)
            target.RenderTransform = transform;

        target.Opacity = 0;
        Run(target, duration, progress =>
        {
            target.Opacity = progress;
            // Something else (a drag, say) may have taken over the transform meanwhile.
            if (transform != null && ReferenceEquals(target.RenderTransform, transform))
                update(transform, progress);
        }, () =>
        {
            if (transform != null && ReferenceEquals(target.RenderTransform, transform))
                target.RenderTransform = null;
            completed?.Invoke();
        });
        // Run cancels what came before it, so the transform is recorded after it starts.
        if (transform != null && ReferenceEquals(target.RenderTransform, transform))
            s_transforms.AddOrUpdate(target, transform);
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
