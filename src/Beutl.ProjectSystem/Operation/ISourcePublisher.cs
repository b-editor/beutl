using Beutl.Animation;
using Beutl.Graphics.Rendering;

namespace Beutl.Operation;

public interface ISourcePublisher : ISourceOperator
{
    Renderable? Publish(IClock clock);
}
