using Beutl.ProjectSystem;

namespace Beutl.Graphics.Rendering;

public interface ISceneCompositionRenderContext
{
    IList<Element> CurrentElements { get; }

    void EvaluateElementIntoFlow(Element element);
}
