using Beutl.Animation;
using Beutl.Rendering;

namespace Beutl.Operation;

public interface ISourcePublisher : ISourceOperator
{
    Renderable? Publish(IClock clock);
}
