using System.ComponentModel.DataAnnotations;
using Beutl.Language;

namespace Beutl.Graphics.Transitions;

/// <summary>
/// The outgoing clip zooms in towards the centre of the frame as it dissolves into the incoming clip,
/// which settles back from the same zoom.
/// </summary>
[Display(Name = nameof(GraphicsStrings.ZoomTransition), ResourceType = typeof(GraphicsStrings))]
public sealed partial class ZoomTransition : ClipTransition
{
    public ZoomTransition()
    {
        ScanProperties<ZoomTransition>();
    }

    public new partial class Resource
    {
        public override void Draw(ClipTransitionContext context)
        {
            context.DrawZoom();
        }
    }
}
