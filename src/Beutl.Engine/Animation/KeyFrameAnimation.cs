using System.ComponentModel;

using Beutl.Media;
using Beutl.Serialization;

namespace Beutl.Animation;

public abstract class KeyFrameAnimation : CoreObject, IKeyFrameAnimation
{
    public static readonly CoreProperty<bool> UseGlobalClockProperty;
    private CoreProperty? _property;
    private bool _useGlobalClock;

    static KeyFrameAnimation()
    {
        UseGlobalClockProperty = ConfigureProperty<bool, KeyFrameAnimation>(nameof(UseGlobalClock))
            .Accessor(o => o.UseGlobalClock, (o, v) => o.UseGlobalClock = v)
            .Register();
    }

    public KeyFrameAnimation(CoreProperty property)
    {
        _property = property;
        KeyFrames.Attached += OnKeyFrameAttached;
        KeyFrames.Detached += OnKeyFrameDetached;
    }

    public KeyFrameAnimation()
    {
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
            keyFrame.Property = Property;
        }

        obj.SetParent(this);
        obj.KeyTimeChanged += OnKeyTimeChanged;
        obj.Invalidated += OnKeyFrameInvalidated;
    }

    private void OnKeyFrameDetached(IKeyFrame obj)
    {
        if (obj is KeyFrame keyFrame)
        {
            keyFrame.Property = null;
        }

        obj.SetParent(null);
        obj.KeyTimeChanged -= OnKeyTimeChanged;
        obj.Invalidated -= OnKeyFrameInvalidated;
    }

    public bool UseGlobalClock
    {
        get => _useGlobalClock;
        set => SetAndRaise(UseGlobalClockProperty, ref _useGlobalClock, value);
    }

    public KeyFrames KeyFrames { get; } = [];

    public CoreProperty Property
    {
        get => _property!;
        set
        {
            _property = value;
            foreach (IKeyFrame item in KeyFrames)
            {
                if (item is KeyFrame keyFrame)
                {
                    keyFrame.Property = _property;
                }
            }
        }
    }

    public TimeSpan Duration
        => KeyFrames.Count > 0
            ? KeyFrames[^1].KeyTime
            : TimeSpan.Zero;

    public abstract void ApplyAnimation(Animatable target, IClock clock);

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

    public override void Serialize(ICoreSerializationContext context)
    {
        base.Serialize(context);
        context.SetValue(nameof(Property), Property);
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);
        if (context.GetValue<CoreProperty>(nameof(Property)) is { } prop)
        {
            Property = prop;
        }
    }
}
