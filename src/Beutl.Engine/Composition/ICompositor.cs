using Beutl.Media;

namespace Beutl.Composition;

public interface ICompositor : IDisposable
{
    CompositionFrame EvaluateGraphics(MediaTime time);

    CompositionFrame EvaluateAudio(TimeRange timeRange);
}
