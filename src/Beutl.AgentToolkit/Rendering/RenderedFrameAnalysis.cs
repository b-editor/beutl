using Beutl.Media;

namespace Beutl.AgentToolkit.Rendering;

public sealed class RenderedFrameAnalysis : IDisposable
{
    public RenderedFrameAnalysis(MediaTime time, Bitmap bitmap, IReadOnlyList<RenderedTextBounds> textBounds)
    {
        Time = time;
        Bitmap = bitmap;
        TextBounds = textBounds;
    }

    public MediaTime Time { get; }

    public Bitmap Bitmap { get; }

    public IReadOnlyList<RenderedTextBounds> TextBounds { get; }

    public void Dispose()
    {
        Bitmap.Dispose();
    }
}
