using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Beutl.Graphics.Rendering;

public class LayerNode : ContainerNode
{
    public LayerNode(Rect limit)
    {
        Limit = limit;
    }

    public Rect Limit { get; }

    public override void Render(ImmediateCanvas canvas)
    {
        using (canvas.PushLayer(Limit))
        {
            base.Render(canvas);
        }
    }
}
