namespace BeUtl.Graphics.Effects;

public sealed class BitmapEffectGroup : BitmapEffect
{
    public static readonly CoreProperty<BitmapEffects> ChildrenProperty;
    private readonly BitmapEffects _children;
    private readonly BitmapProcessorGroup _processor = new();

    static BitmapEffectGroup()
    {
        ChildrenProperty = ConfigureProperty<BitmapEffects, BitmapEffectGroup>(nameof(Children))
            .Accessor(o => o.Children, (o, v) => o.Children = v)
            .PropertyFlags(PropertyFlags.Styleable | PropertyFlags.Designable)
            .Register();
        AffectsRender<BitmapEffectGroup>(ChildrenProperty);
    }

    public BitmapEffectGroup()
    {
        _children = new BitmapEffects()
        {
            Attached = item => (item as ILogicalElement).NotifyAttachedToLogicalTree(new(this)),
            Detached = item => (item as ILogicalElement).NotifyDetachedFromLogicalTree(new(this)),
        };
        _children.Invalidated += (_, _) =>
        {
            IBitmapProcessor[] array = new IBitmapProcessor[ValidEffectCount()];
            int index = 0;
            foreach (BitmapEffect item in _children.AsSpan())
            {
                if (item.IsEnabled)
                {
                    array[index] = item.Processor;
                    index++;
                }
            }
            _processor.Processors = array;
            RaiseInvalidated();
        };
    }

    public BitmapEffects Children
    {
        get => _children;
        set => _children.Replace(value);
    }

    public override IBitmapProcessor Processor => _processor;

    public override Rect TransformBounds(Rect rect)
    {
        foreach (BitmapEffect item in _children.AsSpan())
        {
            if (item.IsEnabled)
                rect = item.TransformBounds(rect);
        }
        return rect;
    }

    private int ValidEffectCount()
    {
        int count = 0;
        foreach (BitmapEffect item in _children.AsSpan())
        {
            if (item.IsEnabled)
            {
                count++;
            }
        }
        return count;
    }
}
