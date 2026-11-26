using System.ComponentModel.DataAnnotations;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media;
using Beutl.Serialization;

namespace Beutl.Graphics.Transitions;

/// <summary>
/// The outgoing clip fades to <see cref="Color"/> over the first half and the incoming clip fades in from
/// it over the second half.
/// </summary>
[Display(Name = nameof(GraphicsStrings.DipToColorTransition), ResourceType = typeof(GraphicsStrings))]
[FormerPersistedName(DipToColorTransition.FormerDipToWhiteName)]
public sealed partial class DipToColorTransition : ClipTransition
{
    private const string FormerDipToWhiteName = "[Beutl.Engine]Beutl.Graphics.Transitions:DipToWhiteTransition";

    public DipToColorTransition()
    {
        ScanProperties<DipToColorTransition>();
    }

    [Display(Name = nameof(GraphicsStrings.Color), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<Color> Color { get; } = Property.CreateExpression(Colors.Black);

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);

        // DipToWhite used white without saving a Color member.
        if (!context.Contains(nameof(Color)) && context.GetValue<string>("$type") == FormerDipToWhiteName)
        {
            Color.Value = Colors.White;
        }
    }

    public new partial class Resource
    {
        private readonly DipFill _dipFill = new();

        public override void Draw(ClipTransitionContext context) => _dipFill.Draw(context);

        partial void PostReconcile(DipToColorTransition obj, CompositionContext context)
            => _dipFill.Update(this, Color, context);

        partial void PostDispose(bool disposing) => _dipFill.Dispose();
    }
}
