using Beutl.Media;

namespace Beutl.Rendering;

public interface IRenderable
{
    bool IsVisible { get; set; }

    //int ZIndex { get; set; }

    //TimeRange TimeRange { get; set; }

    void Invalidate();

    void Render(IRenderer renderer);
}
