namespace Beutl.Models;

internal sealed class RetainedRenderTargetCheckpoint
{
    internal const int DefaultReleaseInterval = 30;
    private readonly int _releaseInterval;
    private int _renderedFrameCount;

    public RetainedRenderTargetCheckpoint(int releaseInterval = DefaultReleaseInterval)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(releaseInterval);
        _releaseInterval = releaseInterval;
    }

    public bool Advance()
        => ++_renderedFrameCount % _releaseInterval == 0;
}
