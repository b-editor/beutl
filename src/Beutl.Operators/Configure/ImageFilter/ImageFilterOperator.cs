using System.Runtime.CompilerServices;

using Beutl.Graphics;
using Beutl.Graphics.Filters;

namespace Beutl.Operators.Configure.ImageFilter;

#pragma warning disable IDE0065
using ImageFilter = Graphics.Filters.ImageFilter;

public abstract class ImageFilterOperator<T> : ConfigureOperator<Drawable, T>
    where T : ImageFilter, new()
{
    private readonly ConditionalWeakTable<Drawable, ComposedImageFilter> _table = new();

    protected override void PreProcess(Drawable target, T value)
    {
        value.IsEnabled = IsEnabled;
    }

    protected override void Process(Drawable target, T value)
    {
        ComposedImageFilter composed = _table.GetValue(target, _ => new ComposedImageFilter());
        if (target.Filter != composed)
        {
            composed.Outer = value;
            composed.Inner = target.Filter;
            target.Filter = composed;
        }
    }

    protected override void OnDetachedFromHierarchy(in HierarchyAttachmentEventArgs args)
    {
        base.OnDetachedFromHierarchy(args);
        _table.Clear();
    }
}
