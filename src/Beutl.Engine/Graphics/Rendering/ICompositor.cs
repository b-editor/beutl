using Beutl.Media;

namespace Beutl.Graphics.Rendering;

public interface ICompositor : IDisposable
{
    CompositionFrame EvaluateGraphics(TimeSpan time);

    CompositionFrame EvaluateAudio(TimeRange timeRange);
}
