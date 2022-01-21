using SkiaSharp;

namespace BeUtl.Graphics.Filters;

public sealed class ImageFilterGroup : ImageFilter
{
    public static readonly CoreProperty<ImageFilters> ChildrenProperty;
    private readonly ImageFilters _children;

    static ImageFilterGroup()
    {
        ChildrenProperty = ConfigureProperty<ImageFilters, ImageFilterGroup>(nameof(Children))
            .Accessor(o => o.Children, (o, v) => o.Children = v)
            .Register();
    }

    public ImageFilterGroup()
    {
        _children = new ImageFilters()
        {
            Attached = item => (item as ILogicalElement).NotifyAttachedToLogicalTree(new(this)),
            Detached = item => (item as ILogicalElement).NotifyDetachedFromLogicalTree(new(this)),
        };
        _children.Invalidated += (_, _) => RaiseInvalidated();
    }

    public ImageFilters Children
    {
        get => _children;
        set => _children.Replace(value);
    }

    public override Rect TransformBounds(Rect rect)
    {
        Rect original = rect;

        foreach (ImageFilter item in _children.AsSpan())
        {
            rect = item.TransformBounds(original).Union(rect);
        }

        return rect;
    }

    protected internal override SKImageFilter ToSKImageFilter()
    {
        var array = new SKImageFilter[_children.Count];
        int index = 0;
        foreach (ImageFilter item in _children.AsSpan())
        {
            array[index] = item.ToSKImageFilter();

            index++;
        }

        return SKImageFilter.CreateMerge(array);
    }
}
