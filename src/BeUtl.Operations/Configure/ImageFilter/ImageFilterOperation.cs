using BeUtl.Graphics;
using BeUtl.Graphics.Filters;
using BeUtl.ProjectSystem;

namespace BeUtl.Operations.Configure.ImageFilter;

public abstract class ImageFilterOperation<T> : LayerOperation
    where T : Graphics.Filters.ImageFilter
{
    private Graphics.Drawable? _drawable;

    public abstract T Filter { get; }

    protected override void OnDetachedFromLogicalTree(in LogicalTreeAttachmentEventArgs args)
    {
        base.OnDetachedFromLogicalTree(args);
        if (_drawable != null && _drawable.Filter is ImageFilterGroup group)
        {
            group.Children.Remove(Filter);
        }
    }

    protected override void RenderCore(ref OperationRenderArgs args)
    {
        if (args.Result is Graphics.Drawable drawable)
        {
            Filter.IsEnabled = IsEnabled;
            if (_drawable != drawable)
            {
                if (drawable.Filter is not ImageFilterGroup group)
                {
                    drawable.Filter = group = new ImageFilterGroup();
                }

                if (_drawable?.Filter is ImageFilterGroup group1)
                {
                    group1.Children.Remove(Filter);
                }

                group.Children.Add(Filter);
                _drawable = drawable;
            }
        }
        base.RenderCore(ref args);
    }
}
