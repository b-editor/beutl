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
internal readonly struct StructuralKey : IEquatable<StructuralKey>
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

            case GeometryNodeDescriptor geometry:
                sb.Append("geometry:")
                    .Append(geometry.StructuralToken)
                    .Append(',').Append(geometry.RequiresReadback ? '1' : '0')
                    .Append(',').Append(geometry.Bounds.StructuralIdentity.ToString(CultureInfo.InvariantCulture));
                break;

            case ComputeNodeDescriptor compute:
                // PassCount is topology (C3.6): it is in the key, so animating it recompiles exactly once.
                // Scratch maxima are structural: they determine the pass-scoped resource declarations (C3.3).
                sb.Append("compute:")
                    .Append(compute.StructuralToken)
                    .Append(',').Append(compute.PassCount)
                    .Append(',').Append(compute.ColorScratchCount)
                    .Append(',').Append(compute.DepthScratchCount)
                    .Append(',').Append((int)compute.Fallback)
                    .Append(',').Append(compute.CpuFallbackRequiresReadback ? '1' : '0')
                    .Append(',').Append((int)compute.DispatchFailureBehavior);
                break;

            case SplitNodeDescriptor split:
                // Branch count is topology (C3.6); a dynamic split keys on its dynamic flag, not a runtime count.
                sb.Append("split:")
                    .Append(split.StructuralToken)
                    .Append(',').Append(split.IsDynamicOutputs ? "dyn" : split.BranchCount.ToString(CultureInfo.InvariantCulture))
                    .Append(',').Append(split.RequiresReadback ? '1' : '0');
                break;

            case CompositeNodeDescriptor composite:
                sb.Append("composite:")
                    .Append(composite.StructuralToken)
                    .Append(',').Append((int)composite.BlendMode)
                    .Append(',').Append(composite.InputOffsets.Length);
                break;

            case NestedGraphNodeDescriptor nested:
                // The child graph's structure is resolved per branch at execution; only the nested kind is
                // structural here (a child topology change re-describes inside the branch, never stale-hits this key).
                sb.Append("nested:").Append(nested.StructuralToken);
                break;

            case CustomRenderNodeDescriptor custom:
                // The child's render-node type plus the child resource's stable identity token: a swapped child
                // instance or a changed node type must recompile the plan (the render node it drives differs). The
                // child's Version is a per-frame parameter (rebind), so it stays OUT of the key — an animated child
                // param re-renders through the same plan. Assembly-qualified so same-full-name types from different
                // assemblies can never alias.
                sb.Append("custom:")
                    .Append(custom.NodeType.AssemblyQualifiedName ?? custom.NodeType.FullName)
                    .Append(',').Append(custom.Resource.StructuralId.ToString(CultureInfo.InvariantCulture));
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
