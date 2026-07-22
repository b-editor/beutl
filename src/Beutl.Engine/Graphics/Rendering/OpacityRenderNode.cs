using Beutl.Graphics.Effects;

namespace Beutl.Graphics.Rendering;

public sealed class OpacityRenderNode(float opacity) : ContainerRenderNode
{
    private const string FusionSource =
        "uniform float opacity; half4 apply(half4 color) { return color * opacity; }";

    public float Opacity { get; private set; } = opacity;

    public bool Update(float opacity)
    {
        if (Opacity != opacity)
        {
            Opacity = opacity;
            HasChanges = true;
            return true;
        }

        return false;
    }

    public override void Process(RenderNodeContext context)
    {
        float opacity = Opacity;
        foreach (RenderFragmentHandle input in context.Inputs)
        {
            context.Publish(context.Opacity(input, opacity));
        }
    }

    internal static ShaderDescription CreateFusionDescription(float opacity)
    {
        if (!float.IsFinite(opacity))
            throw new ArgumentOutOfRangeException(nameof(opacity), opacity, "Opacity must be finite.");

        return ShaderDescription.CurrentPixel(
            FusionSource,
            bindings => bindings.Uniform("opacity", opacity));
    }
}
