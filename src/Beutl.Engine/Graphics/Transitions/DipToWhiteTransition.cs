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
        private SolidColorBrush? _brush;
        private SolidColorBrush.Resource? _fill;

        public override void Draw(ClipTransitionContext context)
        {
            context.DrawThrough(_fill);
        }

        partial void PostUpdate(DipToWhiteTransition obj, CompositionContext context)
        {
            UpdateDipFill(ref _brush, ref _fill, Colors.White, context);
        }

        partial void PostDispose(bool disposing)
        {
            _fill?.Dispose();
            _fill = null;
            _brush = null;
        }
    }
}
