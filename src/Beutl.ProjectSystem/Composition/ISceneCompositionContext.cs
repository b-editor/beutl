using Beutl.ProjectSystem;

namespace Beutl.Composition;

public interface ISceneCompositionContext
{
    IList<Element> CurrentElements { get; }

    void EvaluateElementIntoFlow(Element element);
}
