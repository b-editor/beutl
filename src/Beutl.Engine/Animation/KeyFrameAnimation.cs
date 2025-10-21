using System.ComponentModel;
using Beutl.Engine;
using Beutl.Media;
using Beutl.Serialization;
using Beutl.Validation;

namespace Beutl.Animation;

public abstract class KeyFrameAnimation : Hierarchical, IKeyFrameAnimation
{
    public static readonly CoreProperty<bool> UseGlobalClockProperty;
    private bool _useGlobalClock;
    private IValidator? _validator;

    static KeyFrameAnimation()
    {
        UseGlobalClockProperty = ConfigureProperty<bool, KeyFrameAnimation>(nameof(UseGlobalClock))
            .Accessor(o => o.UseGlobalClock, (o, v) => o.UseGlobalClock = v)
            .Register();
    }

    public KeyFrameAnimation()
    {
        KeyFrames = new KeyFrames(this);
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
                for (int i = 0; i < KeyFrames.Count; i++)
                {
                    IKeyFrame item = KeyFrames[i];
                    if (keyframe != item && keyframe.KeyTime < item.KeyTime)
                    {
                        if (index < i)
                        {
                            i--;
                        }
                        KeyFrames.Move(index, i);
                        return;
                    }
                }

                if (KeyFrames.Count > 1)
                {
                    IKeyFrame last = KeyFrames[^1];
                    if (last.KeyTime < keyframe.KeyTime)
                    {
                        KeyFrames.Move(index, KeyFrames.Count - 1);
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
        if (obj is KeyFrame keyFrame)
        {
            keyFrame.Validator = Validator;
        }

        obj.KeyTimeChanged += OnKeyTimeChanged;
        obj.Invalidated += OnKeyFrameInvalidated;
    }

    private void OnKeyFrameDetached(IKeyFrame obj)
    {
        if (obj is KeyFrame keyFrame)
        {
            keyFrame.Validator = null;
        }

        obj.KeyTimeChanged -= OnKeyTimeChanged;
        obj.Invalidated -= OnKeyFrameInvalidated;
    }

    public abstract Type ValueType { get; }

    public bool UseGlobalClock
    {
        get => _useGlobalClock;
        set => SetAndRaise(UseGlobalClockProperty, ref _useGlobalClock, value);
    }

    public KeyFrames KeyFrames { get; }

    public IValidator? Validator
    {
        get => _validator;
        set
        {
            _validator = value;
            foreach (IKeyFrame item in KeyFrames)
            {
                if (item is KeyFrame keyFrame)
                {
                    keyFrame.Validator = value;
                }
            }
        }
    }

    public TimeSpan Duration
        => KeyFrames.Count > 0
            ? KeyFrames[^1].KeyTime
            : TimeSpan.Zero;

    public (IKeyFrame? Previous, IKeyFrame? Next) GetPreviousAndNextKeyFrame(IKeyFrame keyframe)
    {
        return KeyFrames.GetPreviousAndNextKeyFrame(keyframe);
    }

    public (IKeyFrame? Previous, IKeyFrame? Next) GetPreviousAndNextKeyFrame(TimeSpan timeSpan)
    {
        return KeyFrames.GetPreviousAndNextKeyFrame(timeSpan);
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs args)
    {
        base.OnPropertyChanged(args);
        if (args.PropertyName is nameof(UseGlobalClock))
        {
            Invalidated?.Invoke(this, new(this));
        }
    }
}
