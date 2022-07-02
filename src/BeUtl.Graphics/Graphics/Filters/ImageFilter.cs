using BeUtl.Media;
using BeUtl.Styling;

using SkiaSharp;

namespace BeUtl.Graphics.Filters;

public interface IImageFilter : IAffectsRender
{
    Rect TransformBounds(Rect rect);

    SKImageFilter ToSKImageFilter();
}

public interface IMutableImageFilter : IStyleable, IImageFilter
{
}

public abstract class ImageFilter : Styleable, IMutableImageFilter
{
    public static readonly CoreProperty<bool> IsEnabledProperty;
    private bool _isEnabled;

    static ImageFilter()
    {
        IsEnabledProperty = ConfigureProperty<bool, ImageFilter>(nameof(IsEnabled))
            .Accessor(o => o.IsEnabled, (o, v) => o.IsEnabled = v)
            .DefaultValue(true)
            .SerializeName("is-enabled")
            .Register();

        AffectsRender<ImageFilter>(IsEnabledProperty);
    }

    public event EventHandler? Invalidated;

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetAndRaise(IsEnabledProperty, ref _isEnabled, value);
    }

    public virtual Rect TransformBounds(Rect rect)
    {
        return rect;
    }

    protected internal abstract SKImageFilter ToSKImageFilter();

    protected static void AffectsRender<T>(params CoreProperty[] properties)
        where T : ImageFilter
    {
        foreach (CoreProperty? item in properties)
        {
            item.Changed.Subscribe(e =>
            {
                if (e.Sender is T s)
                {
                    s.RaiseInvalidated();
                }
            });
        }
    }

    protected void RaiseInvalidated()
    {
        Invalidated?.Invoke(this, EventArgs.Empty);
    }

    SKImageFilter IImageFilter.ToSKImageFilter()
    {
        return ToSKImageFilter();
    }
}
