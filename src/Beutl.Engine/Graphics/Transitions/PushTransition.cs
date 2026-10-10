using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;

namespace Beutl.Graphics.Transitions;

/// <summary>
/// The incoming clip moves into the frame in <see cref="Direction"/>, pushing the outgoing clip out ahead
/// of it.
/// </summary>
[Display(Name = nameof(GraphicsStrings.PushTransition), ResourceType = typeof(GraphicsStrings))]
public sealed partial class PushTransition : ClipTransition
{
    public PushTransition()
    {
        ScanProperties<PushTransition>();
    }

    [Display(Name = nameof(GraphicsStrings.Direction), ResourceType = typeof(GraphicsStrings))]
    public IProperty<ClipTransitionDirection> Direction { get; } = Property.Create(ClipTransitionDirection.LeftToRight);

    public new partial class Resource
    {
        public override void Draw(ClipTransitionContext context)
        {
            context.DrawPush(Direction);
        }
    }
}
