using System.ComponentModel.DataAnnotations;
using Beutl.Language;

namespace Beutl.Graphics.Transitions;

/// <summary>
/// The outgoing clip fades out to transparent over the first half and the incoming clip fades in over the
/// second half, so whatever lies beneath the layer shows through at the midpoint.
/// </summary>
[Display(Name = nameof(GraphicsStrings.FadeTransition), ResourceType = typeof(GraphicsStrings))]
public sealed partial class FadeTransition : ClipTransition
{
    public FadeTransition()
    {
        ScanProperties<FadeTransition>();
    }

    public new partial class Resource
    {
        public override void Draw(ClipTransitionContext context)
        {
            context.DrawThrough(fill: null);
        }
    }
}
