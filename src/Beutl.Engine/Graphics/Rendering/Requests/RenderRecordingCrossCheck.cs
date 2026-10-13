namespace Beutl.Graphics.Rendering.Requests;

/// <summary>
/// Records a node a second time and fails the request when the recordings differ, enforcing that a node reporting
/// no changes records the same graph. Off unless <see cref="Enable"/> is called (today only by tests); the call
/// sites exist in Debug builds only.
/// </summary>
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
