using BEditorNext.Graphics;
using BEditorNext.Graphics.Transformation;
using BEditorNext.ProjectSystem;

namespace BEditorNext.Operations.Transform;

public abstract class TransformOperation : LayerOperation
{
    public override void BeginningRender(IScopedRenderable scope)
    {
        for (int i = 0; i < scope.Count; i++)
        {
            if (scope[i].Item is IDrawable obj)
            {
                obj.Transform.Add(Transform);
            }
        }
    }

    public override void EndingRender(IScopedRenderable scope)
    {
        for (int i = 0; i < scope.Count; i++)
        {
            if (scope[i].Item is IDrawable obj)
            {
                obj.Transform.Remove(Transform);
            }
        }
    }

    public abstract Graphics.Transformation.Transform Transform { get; }
}
