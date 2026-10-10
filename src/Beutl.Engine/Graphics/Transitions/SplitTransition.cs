using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;

namespace Beutl.Graphics.Transitions;

/// <summary>
/// The outgoing clip splits apart from the centre of the frame like a pair of doors, revealing the
/// incoming clip in a band that widens as <see cref="Orientation"/> says.
/// </summary>
[Display(Name = nameof(GraphicsStrings.SplitTransition), ResourceType = typeof(GraphicsStrings))]
public sealed partial class SplitTransition : ClipTransition
{
    public SplitTransition()
    {
        ScanProperties<SplitTransition>();
    }

    [Display(Name = nameof(GraphicsStrings.ClipTransition_Orientation), ResourceType = typeof(GraphicsStrings))]
    public IProperty<ClipTransitionOrientation> Orientation { get; } = Property.Create(ClipTransitionOrientation.Horizontal);

    public new partial class Resource
    {
        public override void Draw(ClipTransitionContext context)
        {
            context.DrawSplit(Orientation);
        }
    }
}
