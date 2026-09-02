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
/// The baseline - what a skip path would have reused - is the shape the recording cache retained for the node,
/// so what is verified is the artifact that would actually have been replayed rather than a second opinion
/// about it. While this is on, a node the cache would have skipped is recorded anyway: there has to be a fresh
/// recording for the retained one to be compared against. A node with no retained shape - its first request,
/// or one the cache refuses - falls back to a probe re-record, which needs no history.
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
        IReadOnlyList<RenderFragmentReference> inputs,
        RenderNodeRecordingSnapshot? reusable)
    {
        if (!IsEnabled || node.HasChanges || recorder.IsCapturingCrossCheckBaseline)
            return null;

        return reusable?.Shape ?? recorder.CaptureCrossCheckBaseline(node, inputs);
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
