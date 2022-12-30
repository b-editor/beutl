using Beutl.Animation;
using Beutl.Rendering;

namespace Beutl.Operation;

public interface ISourceHandler : ISourceOperator
{
    void Handle(IList<Renderable> renderables, IClock clock);
}
