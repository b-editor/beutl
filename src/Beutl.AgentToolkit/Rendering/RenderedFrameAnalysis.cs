using Beutl.Media;

namespace Beutl.AgentToolkit.Rendering;

public sealed class RenderedFrameAnalysis : IDisposable
{
    public RenderedFrameAnalysis(
        MediaTime time, Bitmap bitmap, IReadOnlyList<RenderedTextBounds> textBounds, IReadOnlyList<string>? warnings = null)
    {
        Time = time;
        Bitmap = bitmap;
        TextBounds = textBounds;
        Warnings = warnings ?? [];
    }

    public MediaTime Time { get; }

    public Bitmap Bitmap { get; }

    public IReadOnlyList<RenderedTextBounds> TextBounds { get; }

    public IReadOnlyList<string> Warnings { get; }

    public void Dispose()
    {
        Bitmap.Dispose();
    }
}
