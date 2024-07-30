using Beutl.Media;
using Beutl.Media.Pixel;

namespace Beutl.Extensibility;

public interface IFrameProvider
{
    public long FrameCount { get; }

    public Rational FrameRate { get; }

    public ValueTask<Bitmap<Bgra8888>> RenderFrame(long frame);
}
