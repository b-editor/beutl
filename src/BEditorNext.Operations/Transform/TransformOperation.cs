using System.Numerics;

using BEditorNext.Graphics;
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
                bmp.Transform = GetMatrix() * bmp.Transform;
            }
        }
    }

    public abstract Matrix3x2 GetMatrix();
}
