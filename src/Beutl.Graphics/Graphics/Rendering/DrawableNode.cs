using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Beutl.Graphics.Rendering;

public class DrawableNode : ContainerNode
{
    public DrawableNode(Drawable drawable)
    {
        Drawable = drawable;
    }

    public Drawable Drawable { get; }
}
