using System.Numerics;

using BEditorNext.ProjectSystem;

namespace BEditorNext.Operations.Transform;

public sealed class SkewTransform : RenderOperation
{
    public static readonly PropertyDefine<float> SkewXProperty;
    public static readonly PropertyDefine<float> SkewYProperty;

    static SkewTransform()
    {
        SkewXProperty = RegisterProperty<float, SkewTransform>(nameof(SkewX), (owner, obj) => owner.SkewX = obj, owner => owner.SkewX)
            .EnableEditor()
            .EnableAnimation()
            .DefaultValue(100f)
            .JsonName("skewX");

        SkewYProperty = RegisterProperty<float, SkewTransform>(nameof(SkewY), (owner, obj) => owner.SkewY = obj, owner => owner.SkewY)
            .EnableEditor()
            .EnableAnimation()
            .DefaultValue(100f)
            .JsonName("skewY");
    }

    public float SkewX { get; set; }

    public float SkewY { get; set; }

    public override void Render(in OperationRenderArgs args)
    {
        args.Renderer.Graphics.Skew(new Vector2(SkewX, SkewY) / 100);
    }
}
