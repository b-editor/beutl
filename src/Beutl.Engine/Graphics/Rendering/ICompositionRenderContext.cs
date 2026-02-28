using Beutl.Engine;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// Extends RenderContext for composition pipeline.
/// Flow operators use this to consume and manipulate the flow during PostUpdate.
/// </summary>
public interface ICompositionRenderContext
{
    /// <summary>
    /// The flow list that PreUpdate/PostUpdate can manipulate.
    /// </summary>
    IList<EngineObject.Resource> Flow { get; set; }
}
