using Beutl.Engine;

namespace Beutl.Graphics;

/// <summary>A drawable that renders the drawables in its list at the composition time it is rendered at.</summary>
public interface IDrawableContainer
{
    IListProperty<Drawable> Children { get; }
}
