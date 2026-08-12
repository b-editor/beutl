using System.Collections.Concurrent;
using Beutl.Graphics.Effects;

namespace Beutl.Graphics.Rendering;

public sealed class OpacityRenderNode(float opacity) : ContainerRenderNode
{
    private const string FusionSource =
        "uniform float opacity; half4 apply(half4 color) { return color * opacity; }";

    private const int MaximumCachedDescriptions = 256;

    private static readonly SkslSource s_fusionSource = new(FusionSource, ShaderDescriptionKind.CurrentPixel);

    private static readonly ConcurrentDictionary<int, ShaderDescription> s_fusionDescriptions = new();

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
        context.PublishMappedInputs(
            opacity,
            static (context, input, value) => context.Opacity(input, value));
    }

    /// <summary>Returns the shared immutable fusion description for one normalized opacity.</summary>
    /// <remarks>
    /// Recording allocates one opacity fragment per drawable per pass while the SkSL text is a compile-time
    /// constant, so the source is parsed and validated once and every distinct normalized opacity keeps its
    /// description. Sharing an instance only avoids repeated construction; retained-output reuse is controlled by
    /// the owning node's <see cref="RenderNode.HasChanges"/> lifecycle.
    /// </remarks>
    internal static ShaderDescription CreateFusionDescription(float opacity)
    {
        opacity = Normalize(opacity);
        int key = BitConverter.SingleToInt32Bits(opacity);
        if (s_fusionDescriptions.TryGetValue(key, out ShaderDescription? cached))
            return cached;

        ShaderDescription created = ShaderDescription.CurrentPixel(
            s_fusionSource,
            bindings => bindings.Uniform("opacity", opacity));

        // An animated opacity mints a new key every frame, so the memo is bounded rather than evicted per entry.
        if (s_fusionDescriptions.Count >= MaximumCachedDescriptions)
            s_fusionDescriptions.Clear();

        return s_fusionDescriptions.GetOrAdd(key, created);
    }

    internal static float Normalize(float opacity)
    {
        if (!float.IsFinite(opacity))
            throw new ArgumentOutOfRangeException(nameof(opacity), opacity, "Opacity must be finite.");

        return Math.Clamp(opacity, 0, 1);
    }
}
