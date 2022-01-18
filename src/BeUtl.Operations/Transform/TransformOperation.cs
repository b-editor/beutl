using BeUtl.Graphics;
using BeUtl.Graphics.Transformation;
using BeUtl.ProjectSystem;
using BeUtl.Rendering;

namespace BeUtl.Operations.Transform;

public abstract class TransformOperation : LayerOperation
{
    public override void ApplySetters(in OperationRenderArgs args)
    {
        Transform.IsEnabled = IsEnabled;
        base.ApplySetters(args);
    }

    protected override void BeginningRenderCore(ILayerScope scope)
    {
        scope.First<IDrawable>()?.Transform.Add(Transform);
    }

    protected override void EndingRenderCore(ILayerScope scope)
    {
        Transform.Parent?.Transform.Remove(Transform);
    }

    public abstract Graphics.Transformation.Transform Transform { get; }
}
