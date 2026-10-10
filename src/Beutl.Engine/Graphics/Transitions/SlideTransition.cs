using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;

namespace Beutl.Graphics.Transitions;

/// <summary>
/// The incoming clip slides into the frame in <see cref="Direction"/> over the outgoing clip, which stays
/// where it is.
/// </summary>
[Display(Name = nameof(GraphicsStrings.SlideTransition), ResourceType = typeof(GraphicsStrings))]
public sealed partial class SlideTransition : ClipTransition
{
    public SlideTransition()
    {
        ScanProperties<SlideTransition>();
    }

    [Display(Name = nameof(GraphicsStrings.Direction), ResourceType = typeof(GraphicsStrings))]
    public IProperty<ClipTransitionDirection> Direction { get; } = Property.Create(ClipTransitionDirection.LeftToRight);

    public new partial class Resource
    {
        public override void Draw(ClipTransitionContext context)
        {
            context.DrawSlide(Direction);
        }
    }
}
