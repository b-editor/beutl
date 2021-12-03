using BEditorNext.ProjectSystem;

namespace BEditorNext.Operations.Transform;

public sealed class RotateTransform : RenderOperation
{
    public static readonly PropertyDefine<float> RotationProperty;

    static RotateTransform()
    {
        RotationProperty = RegisterProperty<float, RotateTransform>(nameof(Rotation), (owner, obj) => owner.Rotation = obj, owner => owner.Rotation)
            .EnableEditor()
            .EnableAnimation()
            .DefaultValue(0f)
            .JsonName("rotation");
    }

    public float Rotation { get; set; }

    public override void Render(in OperationRenderArgs args)
    {
        args.Renderer.Graphics.RotateDegrees(Rotation);
    }
}
