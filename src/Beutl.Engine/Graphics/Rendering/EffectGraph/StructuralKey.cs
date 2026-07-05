using System.Globalization;
using System.Text;
using Beutl.Graphics.Effects;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// The cache identity of an <see cref="EffectGraph"/>'s <em>structure</em> (feature 004, data-model §3,
/// research D3). Accumulated over node kinds and topology, shader-source identity hashes, color/image-filter
/// factory identity, structural ints (pass/branch counts — none in step 3a's linear chains), coordinate-invariance
/// flags, and the bounds-contract identity of non-invariant nodes. It deliberately <b>excludes</b> uniform values,
/// colors, matrices and sampler texture contents: two graphs that differ only in parameters produce equal keys, so
/// an animated blur sigma re-resolves sizes without a recompile (the equality <see cref="PlanCache"/> keys on).
/// Equality compares a canonical signature string, so it is collision-free.
/// </summary>
public readonly struct StructuralKey : IEquatable<StructuralKey>
{
    private readonly string _signature;

    private StructuralKey(string signature) => _signature = signature;

    internal static StructuralKey Compute(EffectGraph graph)
    {
        var sb = new StringBuilder();
        foreach (EffectNode node in graph.Nodes)
        {
            Append(sb, node.Descriptor);
            sb.Append('|');
        }

        return new StructuralKey(sb.ToString());
    }

    private static void Append(StringBuilder sb, EffectNodeDescriptor descriptor)
    {
        // Each case must write a distinct type tag: payloads alone (e.g. two StructuralTokens) may collide across kinds.
        sb.Append(descriptor.IsCoordinateInvariant ? '1' : '0').Append(':');

        switch (descriptor)
        {
            case ShaderNodeDescriptor shader:
                sb.Append("shader:")
                    .Append(shader.Source.IdentityHash)
                    .Append(',').Append((int)shader.Source.Kind)
                    .Append(',').Append(shader.Samplers.Length)
                    .Append(',').Append(shader.Children.Length);
                if (!shader.IsCoordinateInvariant)
                    sb.Append(',').Append(shader.Bounds.StructuralIdentity.ToString(CultureInfo.InvariantCulture));
                break;

            case ColorFilterNodeDescriptor colorFilter:
                sb.Append("colorFilter:").Append(colorFilter.StructuralToken);
                break;

            case SkiaFilterNodeDescriptor skiaFilter:
                sb.Append("skiaFilter:")
                    .Append(skiaFilter.StructuralToken)
                    .Append(',').Append(skiaFilter.Bounds.StructuralIdentity.ToString(CultureInfo.InvariantCulture));
                break;

            case OpaqueLegacyNodeDescriptor opaque:
                // The token is the effect TYPE, not the recorded item count: a count varies with an animated
                // parameter (a bridged chain would then never cache-hit) and collides across distinct effect kinds
                // of equal count (a false hit on a real topology change). Type is stable per animation, distinct
                // per kind.
                sb.Append("legacy:").Append(opaque.StructuralToken);
                break;

            default:
                sb.Append(descriptor.GetType().FullName);
                break;
        }
    }

    /// <inheritdoc/>
    public bool Equals(StructuralKey other) => string.Equals(_signature, other._signature, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is StructuralKey other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => _signature?.GetHashCode(StringComparison.Ordinal) ?? 0;

    /// <summary>Structural-key equality.</summary>
    public static bool operator ==(StructuralKey left, StructuralKey right) => left.Equals(right);

    /// <summary>Structural-key inequality.</summary>
    public static bool operator !=(StructuralKey left, StructuralKey right) => !left.Equals(right);
}
