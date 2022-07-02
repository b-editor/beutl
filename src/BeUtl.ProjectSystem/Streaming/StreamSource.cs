using BeUtl.Animation;
using BeUtl.Rendering;

namespace BeUtl.Streaming;

public abstract class StreamSource : StreamOperator
{
    public abstract IRenderable? Publish(IClock clock);
}
