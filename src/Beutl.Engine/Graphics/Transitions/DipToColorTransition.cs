using System.ComponentModel.DataAnnotations;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics.Transitions;

/// <summary>
/// The outgoing clip fades to <see cref="Color"/> over the first half and the incoming clip fades in from
/// it over the second half.
/// </summary>
[Display(Name = nameof(GraphicsStrings.DipToColorTransition), ResourceType = typeof(GraphicsStrings))]
public sealed partial class DipToColorTransition : ClipTransition
{
    public DipToColorTransition()
    {
        ScanProperties<DipToColorTransition>();
    }

    [Display(Name = nameof(GraphicsStrings.Color), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<Color> Color { get; } = Property.CreateExpression(Colors.Black);

    public new partial class Resource
    {
        private readonly DipFill _dipFill = new();

        public override void Draw(ClipTransitionContext context) => _dipFill.Draw(context);

        partial void PostReconcile(DipToColorTransition obj, CompositionContext context)
            => _dipFill.Update(this, Color, context);

        partial void PostDispose(bool disposing) => _dipFill.Dispose();
    }
}
