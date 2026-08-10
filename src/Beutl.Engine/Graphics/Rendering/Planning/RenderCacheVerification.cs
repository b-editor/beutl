using Beutl.Media;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// Reports a render-cache runtime identity that does not capture every value its producer draws with:
/// replaying the producer under the identity that selected the cached output produced different pixels.
/// </summary>
internal sealed class RenderCacheOutputMismatchException(string message) : Exception(message);

/// <summary>
/// Opt-in verification of render-cache runtime identities. While enabled, every selected cache hit also
/// executes its producer normally and the two outputs are compared, so an under-specified identity fails
/// loudly instead of silently serving stale pixels.
/// </summary>
/// <remarks>
/// Verification pays a full re-execution for every cache hit and is never enabled in a shipping configuration.
/// </remarks>
internal static class RenderCacheVerification
{
    // The surfaces compared here are always RGBA16F.
    private const int BytesPerPixel = 8;

    private static int s_scopeCount;

    public static bool IsEnabled => Volatile.Read(ref s_scopeCount) != 0;

    /// <summary>Verifies every render request created while the returned scope is alive.</summary>
    /// <remarks>
    /// The switch is process-wide, not thread-scoped, because <c>Renderer</c> and <c>SceneRenderer</c> build
    /// their requests on the render thread rather than the thread that opens the scope. A test that opens one
    /// must therefore be <c>[NonParallelizable]</c>.
    /// </remarks>
    public static IDisposable EnableForAllRequests()
    {
        Interlocked.Increment(ref s_scopeCount);
        return new Scope();
    }

    /// <summary>
    /// Describes the first difference between two materialized outputs, or returns <see langword="null"/> when
    /// they are byte-identical.
    /// </summary>
    public static string? DescribeDifference(RenderTarget cached, RenderTarget executed)
    {
        ArgumentNullException.ThrowIfNull(cached);
        ArgumentNullException.ThrowIfNull(executed);
        if (cached.Width != executed.Width || cached.Height != executed.Height)
        {
            return $"cached device size {cached.Width}x{cached.Height} differs from "
                   + $"executed device size {executed.Width}x{executed.Height}";
        }

        if (cached.Width == 0 || cached.Height == 0)
            return null;

        using Bitmap cachedPixels = cached.Snapshot();
        using Bitmap executedPixels = executed.Snapshot();
        ReadOnlySpan<byte> expected = cachedPixels.GetPixelSpan();
        ReadOnlySpan<byte> actual = executedPixels.GetPixelSpan();
        int prefix = expected.CommonPrefixLength(actual);
        if (prefix == expected.Length && expected.Length == actual.Length)
            return null;

        if (prefix >= expected.Length || prefix >= actual.Length)
            return $"cached readback is {expected.Length} bytes but the executed readback is {actual.Length}";

        int rowBytes = cachedPixels.RowBytes;
        int y = prefix / rowBytes;
        int x = prefix % rowBytes / BytesPerPixel;
        return $"device pixel ({x}, {y}) differs at byte {prefix}: "
               + $"cached 0x{expected[prefix]:X2}, executed 0x{actual[prefix]:X2}";
    }

    private sealed class Scope : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            Interlocked.Decrement(ref s_scopeCount);
        }
    }
}
