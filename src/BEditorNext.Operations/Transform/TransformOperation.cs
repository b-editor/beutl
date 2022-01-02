using BEditorNext.Graphics;
using BEditorNext.Graphics.Transformation;
using BEditorNext.ProjectSystem;

namespace BEditorNext.Operations.Transform;

public abstract class TransformOperation : RenderOperation
{
    public override void Render(in OperationRenderArgs args)
    {
        for (int i = 0; i < args.List.Count; i++)
        {
            if (args.List[i] is IDrawable bmp)
            {
                bmp.Transform.Add(Transform);
            }
        }
    }

    public abstract ITransform Transform { get; }
}
