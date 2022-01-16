using BeUtl.Graphics;
using BeUtl.Graphics.Transformation;
using BeUtl.ProjectSystem;
using BeUtl.Rendering;

namespace BeUtl.Operations.Transform;

public abstract class TransformOperation : LayerOperation
{
    public override void BeginningRender(ILayerScope scope)
    {
        for (int i = 0; i < scope.Count; i++)
        {
            if (scope[i] is IDrawable obj)
            {
                obj.Transform.Add(Transform);
            }
        }
    }

    public override void EndingRender(ILayerScope scope)
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
