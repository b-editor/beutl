using BeUtl.Graphics;
using BeUtl.Graphics.Transformation;
using BeUtl.ProjectSystem;

namespace BeUtl.Operations.Transform;

public abstract class TransformOperation : ConfigureOperation<IDrawable>
{
    public override void Configure(in OperationRenderArgs args, ref IDrawable obj)
    {
        obj.Transform.Add(Transform);
    }

    public abstract ITransform Transform { get; }
}
