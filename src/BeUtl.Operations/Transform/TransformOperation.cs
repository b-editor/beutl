using BeUtl.Graphics;
using BeUtl.Graphics.Transformation;
using BeUtl.ProjectSystem;

namespace BeUtl.Operations.Transform;

public abstract class TransformOperation : LayerOperation
{
    public override void BeginningRender(IScopedRenderable scope)
    {
        for (int i = 0; i < scope.Count; i++)
        {
            if (scope[i] is IDrawable obj)
            {
                obj.Transform.Add(Transform);
            }
        }
    }

    public override void EndingRender(IScopedRenderable scope)
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
