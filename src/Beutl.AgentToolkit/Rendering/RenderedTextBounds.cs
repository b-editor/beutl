using Beutl.Graphics;
using Beutl.Graphics.Shapes;
using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.Rendering;

public sealed record RenderedTextBounds(
    Element Element,
    TextBlock TextBlock,
    Rect Bounds);
