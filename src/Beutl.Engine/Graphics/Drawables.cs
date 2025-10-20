using Beutl.Collections;
using Beutl.Media;

namespace Beutl.Graphics;

public sealed class Drawables : AffectsRenders<Drawable>
{
    public Drawables(IModifiableHierarchical parent)
    {
        ResetBehavior = ResetBehavior.Remove;
        Parent = parent;
        Attached += item => parent.AddChild(item);
        Detached += item => parent.RemoveChild(item);
    }

    public Drawables()
    {
        ResetBehavior = ResetBehavior.Remove;
    }

    public IModifiableHierarchical? Parent { get; }
}
