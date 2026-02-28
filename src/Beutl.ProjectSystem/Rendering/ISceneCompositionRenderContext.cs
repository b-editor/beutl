using Beutl.Engine;
using Beutl.ProjectSystem;

namespace Beutl.Graphics.Rendering;

public interface ISceneCompositionRenderContext : ICompositionRenderContext
{
    List<Element> CurrentElements { get; }

    void EvaluateElementIntoFlow(Element element, EvaluationTarget target);
}
