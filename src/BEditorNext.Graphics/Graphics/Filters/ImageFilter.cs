using SkiaSharp;

namespace BEditorNext.Graphics.Filters;

public abstract class ImageFilter
{
    protected internal abstract SKImageFilter ToSKImageFilter();
}

public sealed class ImageFilters : List<ImageFilter>
{
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
