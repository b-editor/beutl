using Beutl.Media;

namespace Beutl.Composition;

public interface ICompositor : IDisposable
{
    CompositionFrame EvaluateGraphics(TimeSpan time);

    CompositionFrame EvaluateAudio(TimeRange timeRange);
}
