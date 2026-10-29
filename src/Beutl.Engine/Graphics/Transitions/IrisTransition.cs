using System.ComponentModel.DataAnnotations;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics.Transitions;

/// <summary>
/// Opens the incoming clip from the centre or closes the outgoing clip towards the centre.
/// </summary>
[Display(Name = nameof(GraphicsStrings.IrisTransition), ResourceType = typeof(GraphicsStrings))]
public sealed partial class IrisTransition : ClipTransition
{
    public IrisTransition()
    {
        ScanProperties<IrisTransition>();
    }

    [Display(Name = nameof(GraphicsStrings.Direction), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<IrisTransitionDirection> Direction { get; } = Property.CreateExpression(IrisTransitionDirection.In);

    public new partial class Resource
    {
        private RadialGradientBrush? _brush;
        private RadialGradientBrush.Resource? _mask;

        public override void Draw(ClipTransitionContext context)
        {
            context.DrawIris(_mask!, Direction == IrisTransitionDirection.Out);
        }

        // A disc filling whatever square it is drawn into, opaque inside and clear outside.
        partial void PostReconcile(IrisTransition obj, CompositionContext context)
        {
            if (_brush == null)
            {
                _brush = new RadialGradientBrush();
                _brush.GradientStops.Add(new GradientStop(Colors.White, 0));
                _brush.GradientStops.Add(new GradientStop(Colors.White, ClipTransitionContext.IrisSolidFraction));
                _brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1));
            }

            Reconcile(_brush, ref _mask, context);
        }

        partial void PostDispose(bool disposing)
        {
            _mask?.Dispose();
            _mask = null;
            _brush = null;
        }
    }
}
