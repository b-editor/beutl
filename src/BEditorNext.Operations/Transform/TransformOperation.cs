using BEditorNext.Graphics;
using BEditorNext.Graphics.Transformation;
using BEditorNext.ProjectSystem;

namespace BEditorNext.Operations.Transform;

public abstract class TransformOperation : ConfigureOperation<IDrawable>
{
    public override void Configure(in OperationRenderArgs args, ref IDrawable obj)
    {
        obj.Transform.Add(Transform);
    }

    public abstract ITransform Transform { get; }
}
