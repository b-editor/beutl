using System.ComponentModel.DataAnnotations;
using Beutl.Composition;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics.Transitions;

/// <summary>
/// The outgoing clip fades to white over the first half and the incoming clip fades in from it over the
/// second half.
/// </summary>
[Display(Name = nameof(GraphicsStrings.DipToWhiteTransition), ResourceType = typeof(GraphicsStrings))]
public sealed partial class DipToWhiteTransition : ClipTransition
{
    public DipToWhiteTransition()
    {
        ScanProperties<DipToWhiteTransition>();
    }

    public new partial class Resource
    {
        private readonly DipFill _dipFill = new();

        public override void Draw(ClipTransitionContext context) => _dipFill.Draw(context);

        partial void PostReconcile(DipToWhiteTransition obj, CompositionContext context)
            => _dipFill.Update(this, Colors.White, context);

        partial void PostDispose(bool disposing) => _dipFill.Dispose();
    }
}
