using BeUtl.Collections;

using SkiaSharp;

namespace BeUtl.Graphics.Filters;

public abstract class ImageFilter : ILogicalElement
{
    private bool _isEnabled;

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }

    public Drawable? Parent { get; internal set; }

    ILogicalElement? ILogicalElement.LogicalParent => Parent;

    IEnumerable<ILogicalElement> ILogicalElement.LogicalChildren => Array.Empty<ILogicalElement>();

    event EventHandler<LogicalTreeAttachmentEventArgs> ILogicalElement.AttachedToLogicalTree
    {
        add => throw new NotSupportedException();
        remove => throw new NotSupportedException();
    }

    event EventHandler<LogicalTreeAttachmentEventArgs> ILogicalElement.DetachedFromLogicalTree
    {
        add => throw new NotSupportedException();
        remove => throw new NotSupportedException();
    }

    public virtual Rect TransformBounds(Rect rect)
    {
        return rect;
    }

    protected bool SetProperty<T>(ref T field, T value)
    {
        if (!EqualityComparer<T>.Default.Equals(field, value))
        {
            field = value;
            Parent?.InvalidateVisual();

            return true;
        }
        else
        {
            return false;
        }
    }

    void ILogicalElement.NotifyAttachedToLogicalTree(in LogicalTreeAttachmentEventArgs e)
    {
        Parent = e.Parent as Drawable;
    }

    void ILogicalElement.NotifyDetachedFromLogicalTree(in LogicalTreeAttachmentEventArgs e)
    {
        Parent = null;
    }

    protected internal abstract SKImageFilter ToSKImageFilter();
}

public sealed class ImageFilters : CoreList<ImageFilter>
{
    private readonly Drawable _drawable;

    public ImageFilters(Drawable drawable)
    {
        _drawable = drawable;
        Attached = item =>
        {
            _drawable.InvalidateVisual();
            (item as ILogicalElement).NotifyAttachedToLogicalTree(new LogicalTreeAttachmentEventArgs(_drawable));
        };
        Detached = item =>
        {
            _drawable.InvalidateVisual();
            (item as ILogicalElement).NotifyDetachedFromLogicalTree(new LogicalTreeAttachmentEventArgs(null));
        };
    }

    public Rect TransformBounds(Rect rect)
    {
        Rect original = rect;

        foreach (ImageFilter item in AsSpan())
        {
            rect = item.TransformBounds(original).Union(rect);
        }

        return rect;
    }

    internal SKImageFilter ToSKImageFilter()
    {
        var array = new SKImageFilter[Count];
        int index = 0;
        foreach (ImageFilter item in AsSpan())
        {
            if (item.IsEnabled)
            {
                array[index] = item.ToSKImageFilter();
            }

            index++;
        }

        return SKImageFilter.CreateMerge(array);
    }
}

internal static class ImageFilterExtensions
{
    public static Rect TransformBounds(this IReadOnlyList<ImageFilter> filters, Rect rect)
    {
        Rect original = rect;

        for (int i = 0; i < filters.Count; i++)
        {
            rect = filters[i].TransformBounds(original).Union(rect);
        }

        return rect;
    }

    public static SKImageFilter ToSKImageFilter(this IReadOnlyList<ImageFilter> filters)
    {
        var array = new SKImageFilter[filters.Count];
        for (int i = 0; i < filters.Count; i++)
        {
            ImageFilter item = filters[i];
            if (item.IsEnabled)
            {
                array[i] = item.ToSKImageFilter();
            }
        }

        return SKImageFilter.CreateMerge(array);
    }
}
