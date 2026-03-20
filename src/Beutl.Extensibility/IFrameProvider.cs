using Beutl.Media;

namespace Beutl.Extensibility;

public interface IFrameProvider
{
    public long FrameCount { get; }

    public Rational FrameRate { get; }

    public ValueTask<Bitmap> RenderFrame(long frame);
}
