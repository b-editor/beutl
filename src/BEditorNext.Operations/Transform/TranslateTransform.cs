using System.Numerics;

using BEditorNext.ProjectSystem;

namespace BEditorNext.Operations.Transform;

public sealed class TranslateTransform : RenderOperation
{
    public static readonly PropertyDefine<float> XProperty;
    public static readonly PropertyDefine<float> YProperty;

    static TranslateTransform()
    {
        XProperty = RegisterProperty<float, TranslateTransform>(nameof(X), (owner, obj) => owner.X = obj, owner => owner.X)
            .EnableEditor()
            .EnableAnimation()
            .JsonName("x");

        YProperty = RegisterProperty<float, TranslateTransform>(nameof(Y), (owner, obj) => owner.Y = obj, owner => owner.Y)
            .EnableEditor()
            .EnableAnimation()
            .JsonName("y");
    }

    public float X { get; set; }

    public float Y { get; set; }

    public override void Render(in OperationRenderArgs args)
    {
        args.Renderer.Graphics.Skew(new Vector2(X, Y) / 100);
    }
}
