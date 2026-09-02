using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace Beutl.Graphics.Rendering.Requests;

/// <summary>
/// A comparable description of everything one <see cref="RenderNode.Process(RenderNodeContext)"/> call
/// recorded.
/// </summary>
/// <remarks>
/// <para>
/// This compares recorded fragment metadata, the graph the fragments form, each payload's type, and each
/// payload's <see cref="StructuralFragmentIdentity"/> - the same parameter-independent identity the plan cache
/// rebinds a compiled plan on, which reaches into the payload as far as content is defined there: a shader's
/// or geometry's description fingerprint, a bounds, hit-test, scale or input-demand contract, an opaque
/// description's replay contract, the resource types a description declares.
/// </para>
/// <para>
/// It also compares the per-call values a payload states in engine types - an opacity, a blend mode, a mask's
/// bounds and inversion, a layer's domain, a target region. Those are deliberately outside structural identity,
/// because they are what the plan cache varies while holding one plan, so nothing else compares them; a node
/// that advances one inside its own <see cref="RenderNode.Process(RenderNodeContext)"/> passes BESG005 too,
/// which excludes that method because memoizing while recording is legitimate and reads the same. Recording
/// twice is what separates the two: a memo answers the same value the second time and a drift does not.
/// </para>
/// <para>
/// What is still not compared is a payload's author-supplied callback state. A closure's captured state has no
/// defined equality, and a state object a node rebuilds per recording would compare unequal while nothing
/// drifted, so reading it would trade a silent stale frame for a spurious failed one. A drift confined to such
/// a value therefore still passes here; <see cref="RenderNode.HasChanges"/> remains the only signal for that
/// half, as it is for output reuse.
/// </para>
/// </remarks>
internal readonly struct RecordedNodeShape
{
    private readonly int _inputCount;
    private readonly ImmutableArray<string> _fragments;
    private readonly ImmutableArray<StructuralFragmentIdentity> _identities;
    private readonly ImmutableArray<string> _callValues;
    private readonly ImmutableArray<string> _publications;

    private RecordedNodeShape(
        int inputCount,
        ImmutableArray<string> fragments,
        ImmutableArray<StructuralFragmentIdentity> identities,
        ImmutableArray<string> callValues,
        ImmutableArray<string> publications)
    {
        _inputCount = inputCount;
        _fragments = fragments;
        _identities = identities;
        _callValues = callValues;
        _publications = publications;
    }

    /// <summary>Describes the recording a live transaction holds, before it commits.</summary>
    public static RecordedNodeShape Capture(
        IReadOnlyList<RenderFragmentReference> inputs,
        NodeRecordingTransaction transaction)
        => Capture(inputs, transaction.RecordedFragments, transaction.RecordedPublications);

    /// <summary>Describes a recording from its fragments and publications.</summary>
    /// <remarks>
    /// This is the seam a recording cache uses: it holds the fragments it would replay, so it can describe
    /// them here and hand the result to <see cref="RenderRecordingCrossCheck.Verify"/> as the baseline.
    /// </remarks>
    public static RecordedNodeShape Capture(
        IReadOnlyList<RenderFragmentReference> inputs,
        IReadOnlyList<RecordedRenderFragmentEntry> fragments,
        IReadOnlyList<RenderFragmentReference> publications)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(fragments);
        ArgumentNullException.ThrowIfNull(publications);

        var labels = new Dictionary<RenderFragmentReference, string>(ReferenceEqualityComparer.Instance);
        var slots = new Dictionary<RenderFragmentReference, int>(ReferenceEqualityComparer.Instance);
        for (int index = 0; index < inputs.Count; index++)
        {
            labels[inputs[index]] = "in" + index.ToString(CultureInfo.InvariantCulture);
            slots[inputs[index]] = -index - 1;
        }

        for (int index = 0; index < fragments.Count; index++)
        {
            labels[fragments[index].Reference] = "#" + index.ToString(CultureInfo.InvariantCulture);
            slots[fragments[index].Reference] = index;
        }

        // A fragment reached from outside this recording is a different instance in every request, so it is
        // numbered by where it first appears rather than by identity - two recordings agree only when they
        // reach the same count of them in the same order.
        int outer = inputs.Count;
        foreach (RecordedRenderFragmentEntry entry in fragments)
        {
            foreach (RenderFragmentReference input in entry.Reference.Inputs)
            {
                if (!slots.ContainsKey(input))
                    slots[input] = -(++outer);
            }
        }

        var described = ImmutableArray.CreateBuilder<string>(fragments.Count);
        var identities = ImmutableArray.CreateBuilder<StructuralFragmentIdentity>(fragments.Count);
        var callValues = ImmutableArray.CreateBuilder<string>(fragments.Count);
        foreach (RecordedRenderFragmentEntry entry in fragments)
        {
            described.Add(Describe(entry, labels));
            identities.Add(StructuralFragmentIdentity.Create(entry.Reference, slots));
            callValues.Add(DescribeCallValues(entry.Reference));
        }

        var publicationLabels = ImmutableArray.CreateBuilder<string>(publications.Count);
        foreach (RenderFragmentReference publication in publications)
            publicationLabels.Add(Label(publication, labels));

        return new RecordedNodeShape(
            inputs.Count,
            described.ToImmutable(),
            identities.ToImmutable(),
            callValues.ToImmutable(),
            publicationLabels.ToImmutable());
    }

    /// <summary>Reports the first way <paramref name="other"/> differs from this recording.</summary>
    public bool TryDescribeDifference(in RecordedNodeShape other, out string? difference)
    {
        if (_inputCount != other._inputCount)
        {
            difference = $"the first recording was made over {_inputCount} input(s) and the second over "
                + $"{other._inputCount}.";
            return true;
        }

        if (_fragments.Length != other._fragments.Length)
        {
            difference = $"the first recording produced {_fragments.Length} fragment(s) and the second "
                + $"produced {other._fragments.Length}.";
            return true;
        }

        for (int index = 0; index < _fragments.Length; index++)
        {
            if (!string.Equals(_fragments[index], other._fragments[index], StringComparison.Ordinal))
            {
                difference = $"fragment {index} was recorded as{Environment.NewLine}"
                    + $"  first:  {_fragments[index]}{Environment.NewLine}"
                    + $"  second: {other._fragments[index]}";
                return true;
            }
        }

        for (int index = 0; index < _identities.Length; index++)
        {
            if (!_identities[index].Equals(other._identities[index]))
            {
                difference = $"fragment {index} kept its recorded metadata but its payload's structural "
                    + $"identity changed. It was recorded as {_fragments[index]}.";
                return true;
            }
        }

        for (int index = 0; index < _callValues.Length; index++)
        {
            if (!string.Equals(_callValues[index], other._callValues[index], StringComparison.Ordinal))
            {
                difference = $"fragment {index} kept its recorded metadata and its payload's structural "
                    + $"identity, and the per-call values it carries changed from [{_callValues[index]}] to "
                    + $"[{other._callValues[index]}]. It was recorded as {_fragments[index]}.";
                return true;
            }
        }

        if (!_publications.SequenceEqual(other._publications, StringComparer.Ordinal))
        {
            difference = $"the published fragments changed from [{string.Join(", ", _publications)}] to "
                + $"[{string.Join(", ", other._publications)}].";
            return true;
        }

        difference = null;
        return false;
    }

    private static string Describe(
        RecordedRenderFragmentEntry entry,
        IReadOnlyDictionary<RenderFragmentReference, string> labels)
    {
        RenderFragmentReference reference = entry.Reference;
        var builder = new StringBuilder();
        builder.Append(reference.Kind)
            .Append(" role=").Append(entry.Role)
            .Append(" origin=").Append(entry.Origin.GetType().Name)
            .Append(" bounds=").Append(reference.RecordedBounds.ToString())
            .Append(" scale=").Append(
                reference.RecordedEffectiveScale.IsUnbounded
                    ? "unbounded"
                    : reference.RecordedEffectiveScale.Value.ToString("R", CultureInfo.InvariantCulture))
            .Append(" boundsRequirement=").Append(reference.BoundsRequirement)
            .Append(" cardinality=")
            .Append(reference.ValueCardinality.Minimum.ToString(CultureInfo.InvariantCulture))
            .Append("..")
            .Append(reference.ValueCardinality.Maximum?.ToString(CultureInfo.InvariantCulture) ?? "*")
            .Append(" contributes=").Append(reference.ContributesValuesToTarget)
            .Append(" valueInput=").Append(reference.CanBeUsedAsValueInput)
            .Append(" targetEffects=").Append(reference.HasTargetEffects)
            .Append(" opaqueExternalWork=").Append(reference.HasOpaqueExternalWork)
            .Append(" writesTarget=").Append(reference.PotentiallyWritesTarget)
            .Append(" symbolicWrite=").Append(reference.HasSymbolicTargetWrite)
            .Append(" payload=").Append(reference.Payload?.GetType().FullName ?? "none")
            .Append(" inputs=[");

        for (int index = 0; index < reference.Inputs.Length; index++)
        {
            if (index > 0)
                builder.Append(", ");
            builder.Append(Label(reference.Inputs[index], labels));
        }

        return builder.Append(']').ToString();
    }

    /// <summary>The per-call values a payload states in a type the engine defines and can compare.</summary>
    /// <remarks>
    /// Only engine-defined values are read. A payload's author-supplied callback state is left alone - see the
    /// remarks on this type for why - so this net is narrow by design rather than by omission.
    /// </remarks>
    private static string DescribeCallValues(RenderFragmentReference reference) => reference.Payload switch
    {
        OpacityRenderFragmentPayload opacity
            => "opacity=" + opacity.Opacity.ToString("R", CultureInfo.InvariantCulture),
        BlendRenderFragmentPayload blend => "blend=" + blend.BlendMode,
        OpacityMaskRenderFragmentPayload mask
            => $"maskBounds={mask.BrushBounds} invert={mask.Invert}",
        LayerRenderFragmentPayload layer
            => $"domain={layer.Domain?.ToString() ?? "none"} queryFootprint={layer.DomainIsQueryFootprint}",
        TargetLayerScopeRenderFragmentPayload layerScope => "region=" + DescribeRegion(layerScope.Region),
        TargetCommandRenderFragmentPayload command
            => "affected=" + DescribeRegion(command.Description.AffectedRegion),
        _ => string.Empty,
    };

    private static string DescribeRegion(TargetRegion region)
        => region.Kind == TargetRegionKind.Region
            ? region.Kind + ":" + region.Value
            : region.Kind.ToString();

    private static string Label(
        RenderFragmentReference reference,
        IReadOnlyDictionary<RenderFragmentReference, string> labels)
        => labels.TryGetValue(reference, out string? label) ? label : "outer:" + reference.Kind;
}
