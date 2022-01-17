using BeUtl.Graphics;
using BeUtl.Graphics.Transformation;
using BeUtl.ProjectSystem;
using BeUtl.Rendering;

namespace BeUtl.Operations.Transform;

public abstract class TransformOperation : LayerOperation
{
    protected override void BeginningRenderCore(ILayerScope scope)
    {
        for (int i = 0; i < scope.Count; i++)
        {
            if (scope[i] is IDrawable obj)
            {
                obj.Transform.Add(Transform);
            }
        }
    }

    protected override void EndingRenderCore(ILayerScope scope)
    {
        for (int i = 0; i < scope.Count; i++)
        {
            if (scope[i] is IDrawable obj)
            {
                obj.Transform.Remove(Transform);
            }
        }
    }

    public abstract Graphics.Transformation.Transform Transform { get; }
}
