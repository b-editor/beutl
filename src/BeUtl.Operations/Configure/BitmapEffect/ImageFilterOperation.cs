using BeUtl.Graphics;
using BeUtl.Graphics.Effects;
using BeUtl.ProjectSystem;

namespace BeUtl.Operations.Configure.BitmapEffect;

public abstract class BitmapEffectOperation<T> : LayerOperation
    where T : Graphics.Effects.BitmapEffect
{
    private Graphics.Drawable? _drawable;

    public abstract T Effect { get; }

    protected override void OnDetachedFromLogicalTree(in LogicalTreeAttachmentEventArgs args)
    {
        base.OnDetachedFromLogicalTree(args);
        if (_drawable != null && _drawable.Effect is BitmapEffectGroup group)
        {
            group.Children.Remove(Effect);
        }
    }

    protected override void RenderCore(ref OperationRenderArgs args)
    {
        if (args.Result is Graphics.Drawable drawable)
        {
            Effect.IsEnabled = IsEnabled;
            if (_drawable != drawable)
            {
                if (drawable.Effect is not BitmapEffectGroup group)
                {
                    drawable.Effect = group = new BitmapEffectGroup();
                }

                if (_drawable?.Effect is BitmapEffectGroup group1)
                {
                    group1.Children.Remove(Effect);
                }

                group.Children.Add(Effect);
                _drawable = drawable;
            }
        }
        base.RenderCore(ref args);
    }
}
