using BeUtl.Graphics;
using BeUtl.Graphics.Transformation;
using BeUtl.ProjectSystem;

namespace BeUtl.Operations.Configure.Transform;

public abstract class TransformOperation : LayerOperation
{
    private Graphics.Drawable? _drawable;

    public abstract Graphics.Transformation.Transform Transform { get; }

    protected override void OnDetachedFromLogicalTree(in LogicalTreeAttachmentEventArgs args)
    {
        base.OnDetachedFromLogicalTree(args);
        if (_drawable != null && _drawable.Transform is TransformGroup group)
        {
            group.Children.Remove(Transform);
        }
    }

    protected override void RenderCore(ref OperationRenderArgs args)
    {
        if (args.Result is Graphics.Drawable drawable)
        {
            Transform.IsEnabled = IsEnabled;
            if (_drawable != drawable)
            {
                if (drawable.Transform is not TransformGroup group)
                {
                    drawable.Transform = group = new TransformGroup();
                }

                if (_drawable?.Transform is TransformGroup group1)
                {
                    group1.Children.Remove(Transform);
                }

                group.Children.Add(Transform);
                _drawable = drawable;
            }
        }
        base.RenderCore(ref args);
    }
}
