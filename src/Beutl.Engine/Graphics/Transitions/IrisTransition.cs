using System.ComponentModel.DataAnnotations;
using Beutl.Composition;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics.Transitions;

/// <summary>
/// The incoming clip opens from the centre of the frame inside a circle that grows until it covers the
/// frame.
/// </summary>
[Display(Name = nameof(GraphicsStrings.IrisTransition), ResourceType = typeof(GraphicsStrings))]
public sealed partial class IrisTransition : ClipTransition
{
    public IrisTransition()
    {
        ScanProperties<IrisTransition>();
    }

    public new partial class Resource
    {
        private RadialGradientBrush? _brush;
        private RadialGradientBrush.Resource? _mask;

        internal override void Draw(TransitionDrawing drawing)
        {
            if (_mask is { } mask)
            {
                drawing.DrawIris(mask);
            }
            else
            {
                base.Draw(drawing);
            }
        }

        // A disc filling whatever square it is drawn into, opaque inside and clear outside.
        partial void PostUpdate(IrisTransition obj, CompositionContext context)
        {
            if (_brush == null)
            {
                _brush = new RadialGradientBrush();
                _brush.GradientStops.Add(new GradientStop(Colors.White, 0));
                _brush.GradientStops.Add(new GradientStop(Colors.White, TransitionDrawing.IrisSolidFraction));
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
