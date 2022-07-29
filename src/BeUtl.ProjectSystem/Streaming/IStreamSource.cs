using BeUtl.Animation;
using BeUtl.Rendering;

namespace BeUtl.Streaming;

public interface IStreamSource : IStreamOperator
{
    IRenderable? Publish(IClock clock);
}
