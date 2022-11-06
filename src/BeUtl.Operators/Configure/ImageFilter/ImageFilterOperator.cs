using Beutl.Graphics;
using Beutl.Graphics.Filters;

namespace Beutl.Operators.Configure.ImageFilter;

#pragma warning disable IDE0065
using ImageFilter = Graphics.Filters.ImageFilter;

public abstract class ImageFilterOperator<T> : ConfigureOperator<Drawable, T>
    where T : ImageFilter, new()
{
    protected override void PreSelect(Drawable target, T value)
    {
        value.IsEnabled = IsEnabled;
    }

    protected override void OnAttached(Drawable target, T value)
    {
        if (target.Filter is not ImageFilterGroup group)
        {
            target.Filter = group = new ImageFilterGroup();
        }

        group.Children.Add(value);
    }

    protected override void OnDetached(Drawable target, T value)
    {
        if (target.Filter is ImageFilterGroup group)
        {
            group.Children.Remove(value);
        }
    }
}
