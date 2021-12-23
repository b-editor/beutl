using System.Numerics;

using BEditorNext.ProjectSystem;
using BEditorNext.Rendering;

namespace BEditorNext.Operations.Transform;

public sealed class RotateTransform : RenderOperation
{
    public static readonly PropertyDefine<float> RotationProperty;

    static RotateTransform()
    {
        RotationProperty = RegisterProperty<float, RotateTransform>(nameof(Rotation), (owner, obj) => owner.Rotation = obj, owner => owner.Rotation)
            .EnableEditor()
            .Animatable()
            .DefaultValue(0f)
            .JsonName("rotation");
    }

    public float Rotation { get; set; }

    public override void Render(in OperationRenderArgs args)
    {
        for (int i = 0; i < args.List.Count; i++)
        {
            if (args.List[i] is IRenderableBitmap bmp)
            {
                bmp.Transform = Matrix3x2.CreateRotation(MathHelper.ToRadians(Rotation)) * bmp.Transform;
            }
        }
    }
}
