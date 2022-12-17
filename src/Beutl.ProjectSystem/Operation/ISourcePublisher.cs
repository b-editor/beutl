using Beutl.Animation;
using Beutl.Rendering;

namespace Beutl.Operation;

public interface ISourcePublisher : ISourceOperator
{
    IRenderable? Publish(IClock clock);
}
