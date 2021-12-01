using BEditorNext.ProjectItems;

namespace BEditorNext.RenderTasks.Transform;

public sealed class RotateTransform : RenderTask
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

    public override void Execute(in RenderTaskExecuteArgs args)
    {
        args.Renderer.Graphics.RotateDegrees(Rotation);
    }
}
