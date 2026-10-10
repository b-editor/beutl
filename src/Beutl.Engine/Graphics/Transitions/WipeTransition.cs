using System.ComponentModel.DataAnnotations;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics.Transitions;

/// <summary>
/// A straight edge sweeps across the frame in <see cref="Direction"/>, revealing the incoming clip behind
/// it.
/// </summary>
[Display(Name = nameof(GraphicsStrings.WipeTransition), ResourceType = typeof(GraphicsStrings))]
public sealed partial class WipeTransition : ClipTransition
{
    public WipeTransition()
    {
        ScanProperties<WipeTransition>();
    }

    [Display(Name = nameof(GraphicsStrings.Direction), ResourceType = typeof(GraphicsStrings))]
    public IProperty<ClipTransitionDirection> Direction { get; } = Property.Create(ClipTransitionDirection.LeftToRight);

    public new partial class Resource
    {
        private LinearGradientBrush? _brush;
        private LinearGradientBrush.Resource? _mask;

        internal override void Draw(TransitionDrawing drawing)
        {
            if (_mask is { } mask)
            {
                drawing.DrawWipe(mask, Direction);
            }
            else
            {
                base.Draw(drawing);
            }
        }

        // An opaque-to-clear ramp along Direction, laid across whatever bounds it is drawn into.
        partial void PostUpdate(WipeTransition obj, CompositionContext context)
        {
            Vector direction = TransitionDrawing.GetDirection(Direction);
            if (_brush == null)
            {
                _brush = new LinearGradientBrush();
                _brush.GradientStops.Add(new GradientStop(Colors.White, 0));
                _brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1));
            }

            _brush.StartPoint.CurrentValue = new RelativePoint(
                0.5f - direction.X / 2, 0.5f - direction.Y / 2, RelativeUnit.Relative);
            _brush.EndPoint.CurrentValue = new RelativePoint(
                0.5f + direction.X / 2, 0.5f + direction.Y / 2, RelativeUnit.Relative);
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
