using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// Records a node a second way for one request and fails the request when the two recordings disagree.
/// </summary>
/// <remarks>
/// <para>
/// The contract this enforces is the one a recording cache needs: a node whose <see cref="RenderNode.HasChanges"/>
/// is <see langword="false"/> must record the same fragments it recorded before. When a skip path exists, a
/// node that breaks that contract is never re-recorded and renders stale, and because
/// <see cref="RenderNode.HasChanges"/> is public an out-of-tree node can break it with no compile error.
/// <see cref="Beutl.Engine.SourceGenerators"/>'s BESG005 catches the assignment authors usually write; this
/// catches what static analysis cannot follow, at the cost of running the node twice.
/// </para>
/// <para>
/// The baseline - what a skip path would have reused - is supplied by the caller, so a later change that
/// caches recorded fragments plugs in by passing <see cref="RecordedNodeShape.Capture(IReadOnlyList{RenderFragmentReference}, IReadOnlyList{RecordedRenderFragmentEntry}, IReadOnlyList{RenderFragmentReference})"/>
/// over its cached fragments instead of the re-record this uses today. Nothing else about the mechanism moves.
/// </para>
/// <para>
/// This costs a second <see cref="RenderNode.Process(RenderNodeContext)"/> call per node per request, so it is
/// off by default and reachable from the render path only in a Debug build - the call sites in
/// <c>RenderRequestRecorder</c> are compiled out of Release entirely. The type stays compiled either way so
/// that tests bind against it in both configurations; <see cref="IsAvailable"/> says whether the render path
/// can actually reach it.
/// </para>
/// </remarks>
internal static class RenderRecordingCrossCheck
{
    private static int s_enabled;

    /// <summary>Gets whether the recorder is built with the cross-check call sites in it.</summary>
    public static bool IsAvailable =>
#if DEBUG
        true;
#else
        false;
#endif

    public static bool IsEnabled => Volatile.Read(ref s_enabled) != 0;

    /// <summary>Turns the cross-check on until the returned scope is disposed.</summary>
    public static IDisposable Enable()
    {
        Interlocked.Increment(ref s_enabled);
        return new Scope();
    }

    /// <summary>
    /// Records <paramref name="node"/> a second time to stand in for what a skip path would have reused.
    /// </summary>
    /// <returns>
    /// The shape to verify the coming recording against, or <see langword="null"/> when this node is not
    /// subject to the contract - it already reports changes, so a skip path would re-record it anyway.
    /// </returns>
    public static RecordedNodeShape? CaptureBaseline(
        RenderRequestRecorder recorder,
        RenderNode node,
        IReadOnlyList<RenderFragmentReference> inputs)
    {
        if (!IsEnabled || node.HasChanges || recorder.IsCapturingCrossCheckBaseline)
            return null;

        return recorder.CaptureCrossCheckBaseline(node, inputs);
    }

    /// <summary>Fails the request when the node's fresh recording differs from <paramref name="baseline"/>.</summary>
    public static void Verify(
        RenderNode node,
        RecordedNodeShape? baseline,
        IReadOnlyList<RenderFragmentReference> inputs,
        NodeRecordingTransaction fresh)
    {
        if (baseline is not { } expected)
            return;

        RecordedNodeShape actual = RecordedNodeShape.Capture(inputs, fresh);
        if (expected.TryDescribeDifference(actual, out string? difference))
            throw new RenderRecordingCrossCheckException(node.GetType(), difference!);
    }

    private sealed class Scope : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                Interlocked.Decrement(ref s_enabled);
        }
    }
}

/// <summary>
/// A comparable description of everything one <see cref="RenderNode.Process(RenderNodeContext)"/> call
/// recorded.
/// </summary>
/// <remarks>
/// This compares recorded fragment metadata, the graph the fragments form, and each payload's type. It does
/// not compare payload contents: a payload carries author callbacks and state whose equality is undefined, so
/// reading it would trade a silent stale frame for a spurious failed one. A node whose drift changes only a
/// payload value - a color, a matrix inside a command - therefore passes here; BESG005 is the net for that
/// half, since such a value reaches the payload from a field the node assigns.
/// </remarks>
internal readonly struct RecordedNodeShape
{
    private readonly ImmutableArray<string> _fragments;
    private readonly ImmutableArray<string> _publications;

    private RecordedNodeShape(ImmutableArray<string> fragments, ImmutableArray<string> publications)
    {
        _fragments = fragments;
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
        for (int index = 0; index < inputs.Count; index++)
            labels[inputs[index]] = "in" + index.ToString(CultureInfo.InvariantCulture);
        for (int index = 0; index < fragments.Count; index++)
            labels[fragments[index].Reference] = "#" + index.ToString(CultureInfo.InvariantCulture);

        var described = ImmutableArray.CreateBuilder<string>(fragments.Count);
        foreach (RecordedRenderFragmentEntry entry in fragments)
            described.Add(Describe(entry, labels));

        var publicationLabels = ImmutableArray.CreateBuilder<string>(publications.Count);
        foreach (RenderFragmentReference publication in publications)
            publicationLabels.Add(Label(publication, labels));

        return new RecordedNodeShape(described.ToImmutable(), publicationLabels.ToImmutable());
    }

    /// <summary>Reports the first way <paramref name="other"/> differs from this recording.</summary>
    public bool TryDescribeDifference(in RecordedNodeShape other, out string? difference)
    {
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

    private static string Label(
        RenderFragmentReference reference,
        IReadOnlyDictionary<RenderFragmentReference, string> labels)
        => labels.TryGetValue(reference, out string? label) ? label : "outer:" + reference.Kind;
}

/// <summary>Reports that one node recorded two different graphs for the same request.</summary>
internal sealed class RenderRecordingCrossCheckException(Type nodeType, string difference)
    : InvalidOperationException(
        $"Render node '{nodeType.FullName ?? nodeType.Name}' recorded a different graph the second time it "
        + "was recorded for one request while reporting no changes. A recorded graph may be reused for a "
        + "node whose HasChanges is false, so this node would render stale: set HasChanges = true wherever "
        + $"it changes state its Process reads. Difference: {difference}")
{
    public Type NodeType { get; } = nodeType;
}
