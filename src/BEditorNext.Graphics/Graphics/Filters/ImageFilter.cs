using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;

using SkiaSharp;

namespace BEditorNext.Graphics.Filters;

public abstract class ImageFilter
{
    internal Drawable? _drawable;

    public virtual Rect TransformBounds(Rect rect)
    {
        return rect;
    }

    protected bool SetProperty<T>(ref T field, T value)
    {
        if (!EqualityComparer<T>.Default.Equals(field, value))
        {
            field = value;
            _drawable?.InvalidateVisual();

            return true;
        }
        else
        {
            return false;
        }
    }

    protected internal abstract SKImageFilter ToSKImageFilter();
}

public sealed class ImageFilters : Collection<ImageFilter>
{
    private readonly Drawable _drawable;

    public ImageFilters(Drawable drawable)
    {
        _drawable = drawable;
    }

    protected override void ClearItems()
    {
        base.ClearItems();
        _drawable.InvalidateVisual();
    }

    protected override void InsertItem(int index, ImageFilter item)
    {
        base.InsertItem(index, item);
        item._drawable = _drawable;
        _drawable.InvalidateVisual();
    }

    protected override void RemoveItem(int index)
    {
        this[index]._drawable = null;
        base.RemoveItem(index);
        _drawable.InvalidateVisual();
    }

    protected override void SetItem(int index, ImageFilter item)
    {
        base.SetItem(index, item);
        item._drawable = _drawable;
        _drawable.InvalidateVisual();
    }

    public Rect TransformBounds(Rect rect)
    {
        Rect original = rect;

        for (int i = 0; i < Count; i++)
        {
            rect = this[i].TransformBounds(original).Union(rect);
        }

        return rect;
    }

    internal SKImageFilter ToSKImageFilter()
    {
        var array = new SKImageFilter[Count];
        for (int i = 0; i < Count; i++)
        {
            array[i] = this[i].ToSKImageFilter();
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
            array[i] = filters[i].ToSKImageFilter();
        }

        return SKImageFilter.CreateMerge(array);
    }
}
