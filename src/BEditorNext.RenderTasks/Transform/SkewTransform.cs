using System.Numerics;
using BEditorNext.ProjectItems;

namespace BEditorNext.RenderTasks.Transform;

public sealed class SkewTransform : RenderTask
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

    public override void Execute(in RenderTaskExecuteArgs args)
    {
        args.Renderer.Graphics.Skew(new Vector2(SkewX, SkewY) / 100);
    }
}
