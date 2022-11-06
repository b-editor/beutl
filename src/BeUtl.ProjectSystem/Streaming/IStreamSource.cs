using Beutl.Animation;
using Beutl.Rendering;

namespace Beutl.Streaming;

public interface IStreamSource : IStreamOperator
{
    IRenderable? Publish(IClock clock);
}
