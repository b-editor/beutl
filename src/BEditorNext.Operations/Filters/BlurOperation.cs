using BEditorNext.Graphics;
using BEditorNext.Graphics.Filters;

namespace BEditorNext.Operations.Filters;

public sealed class BlurOperation : ImageFilterOperation<Blur>
{
    public static readonly CoreProperty<Vector> SigmaProperty;

    static BlurOperation()
    {
        SigmaProperty = ConfigureProperty<Vector, BlurOperation>(nameof(Sigma))
            .JsonName("sigma")
            .Header("SigmaString")
            .Minimum(Vector.Zero)
            .DefaultValue(new Vector(25, 25))
            .EnableEditor()
            .Animatable(true)
            .Accessor(o => o.Sigma, (o, v) => o.Sigma = v)
            .Register();
    }

    public Vector Sigma
    {
        get => Filter.Sigma;
        set => Filter.Sigma = value;
    }

    public override Blur Filter { get; } = new();
}
