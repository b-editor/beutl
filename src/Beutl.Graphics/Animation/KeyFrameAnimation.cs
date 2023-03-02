using Beutl.Media;

namespace Beutl.Animation;

public abstract class KeyFrameAnimation : CoreObject, IKeyFrameAnimation
{
    public KeyFrameAnimation(CoreProperty property)
    {
        Property = property;
        KeyFrames.Attached += OnKeyFrameAttached;
        KeyFrames.Detached += OnKeyFrameDetached;
    }

    public event EventHandler<RenderInvalidatedEventArgs>? Invalidated;

    private void OnKeyTimeChanged(object? sender, EventArgs e)
    {
        if (sender is IKeyFrame keyframe)
        {
            int index = KeyFrames.IndexOf(keyframe);
            (IKeyFrame? prev, IKeyFrame? next) = GetPreviousAndNextKeyFrame(keyframe);

            bool invalid = false;
            if (prev != null && prev.KeyTime > keyframe.KeyTime)
            {
                invalid = true;
            }
            else if (next != null && keyframe.KeyTime > next.KeyTime)
            {
                invalid = true;
            }

            if (invalid)
            {
                if (KeyFrames.Count > 1)
                {
                    IKeyFrame first = KeyFrames[0];
                    if (keyframe.KeyTime < first.KeyTime)
                    {
                        KeyFrames.Move(index, 0);
                        return;
                    }
                }

                for (int i = 0; i < KeyFrames.Count; i++)
                {
                    IKeyFrame item = KeyFrames[i];
                    if (item.KeyTime < keyframe.KeyTime)
                    {
                        KeyFrames.Move(index, i);
                        return;
                    }
                }
            }
        }
    }

    private void OnKeyFrameInvalidated(object? sender, RenderInvalidatedEventArgs e)
    {
        Invalidated?.Invoke(this, e);
    }

    private void OnKeyFrameAttached(IKeyFrame obj)
    {
        obj.KeyTimeChanged += OnKeyTimeChanged;
        obj.Invalidated += OnKeyFrameInvalidated;
    }

    private void OnKeyFrameDetached(IKeyFrame obj)
    {
        obj.KeyTimeChanged -= OnKeyTimeChanged;
        obj.Invalidated -= OnKeyFrameInvalidated;
    }

    public KeyFrames KeyFrames { get; } = new();

    public CoreProperty Property { get; set; }

    public TimeSpan Duration
        => KeyFrames.Count > 0
            ? KeyFrames[^1].KeyTime
            : TimeSpan.Zero;

    public abstract void ApplyAnimation(Animatable target, IClock clock);

    public (IKeyFrame? Previous, IKeyFrame? Next) GetPreviousAndNextKeyFrame(IKeyFrame keyframe)
    {
        int index = KeyFrames.IndexOf(keyframe);
        IKeyFrame? prev = null;
        IKeyFrame? next = null;

        if (index >= 0)
        {
            int prevIndex = index - 1;
            int nextIndex = index + 1;
            if (0 <= prevIndex && prevIndex < KeyFrames.Count)
            {
                prev = KeyFrames[prevIndex];
            }
            if (0 <= nextIndex && nextIndex < KeyFrames.Count)
            {
                next = KeyFrames[nextIndex];
            }
        }

        return (prev, next);
    }

    public (IKeyFrame? Previous, IKeyFrame? Next) GetPreviousAndNextKeyFrame(TimeSpan timeSpan)
    {
        if (KeyFrames.Count >= 1)
        {
            IKeyFrame first = KeyFrames[0];
            if (timeSpan <= first.KeyTime)
            {
                return (null, first);
            }
        }

        for (int i = 1; i < KeyFrames.Count; i++)
        {
            IKeyFrame prev = KeyFrames[i - 1];
            IKeyFrame next = KeyFrames[i];
            if (prev.KeyTime <= timeSpan
                && timeSpan <= next.KeyTime)
            {
                return (prev, next);
            }
        }

        if (KeyFrames.Count >= 1)
        {
            IKeyFrame last = KeyFrames[^1];
            if (last.KeyTime <= timeSpan)
            {
                return (null, last);
            }
        }

        return (null, null);
    }
}
