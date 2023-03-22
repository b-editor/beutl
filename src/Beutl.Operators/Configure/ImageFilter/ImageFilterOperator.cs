using Beutl.Graphics;
using Beutl.Graphics.Filters;

namespace Beutl.Operators.Configure.ImageFilter;

#pragma warning disable IDE0065
using ImageFilter = Graphics.Filters.ImageFilter;

public abstract class ImageFilterOperator<T> : ConfigureOperator<Drawable, T>
    where T : ImageFilter, new()
{
    protected override void PreProcess(Drawable target, T value)
    {
        value.IsEnabled = IsEnabled;
    }

    protected override void Process(Drawable target, T value)
    {
        (target.Filter as ImageFilterGroup)?.Children.Add(value);
    }
}
