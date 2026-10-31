using System.ComponentModel.DataAnnotations;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics.Transitions;

/// <summary>
/// Reveals the incoming clip with a straight edge, an expanding box or diamond, or a clock sweep.
/// </summary>
[Display(Name = nameof(GraphicsStrings.WipeTransition), ResourceType = typeof(GraphicsStrings))]
public sealed partial class WipeTransition : ClipTransition
{
    public WipeTransition()
    {
        ScanProperties<WipeTransition>();
    }

    [Display(Name = nameof(GraphicsStrings.WipeTransition_Shape), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<WipeTransitionShape> Shape { get; } = Property.CreateExpression(WipeTransitionShape.Linear);

    [Display(Name = nameof(GraphicsStrings.WipeTransition_LinearDirection), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<ClipTransitionDirection> Direction { get; } = Property.CreateExpression(ClipTransitionDirection.LeftToRight);

    [Display(Name = nameof(GraphicsStrings.WipeTransition_Clockwise), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<bool> Clockwise { get; } = Property.CreateExpression(true);

    public new partial class Resource
    {
        private LinearGradientBrush? _brush;
        private LinearGradientBrush.Resource? _mask;
        private readonly GeometryMask _geometry = new();

        public override void Draw(ClipTransitionContext context)
        {
            // A shape this version does not know wipes with a straight edge too.
            if (Shape is not (WipeTransitionShape.Box or WipeTransitionShape.Diamond or WipeTransitionShape.Clock))
            {
                context.DrawWipe(_mask!, Direction);
                return;
            }

            if (context.DrawSettledSide()) return;

            using var path = new GeometryContext();
            Size size = context.Graphics.Size;
            Point center = new Rect(size).Center;
            float amount = context.Progress;
            switch (Shape)
            {
                case WipeTransitionShape.Box:
                    GeometryMask.AddRectangle(path, new Rect(
                        size.Width * (1 - amount) / 2, size.Height * (1 - amount) / 2,
                        size.Width * amount, size.Height * amount));
                    break;

                case WipeTransitionShape.Diamond:
                    float radius = (size.Width + size.Height) * amount / 2;
                    path.MoveTo(new Point(center.X, center.Y - radius));
                    path.LineTo(new Point(center.X + radius, center.Y));
                    path.LineTo(new Point(center.X, center.Y + radius));
                    path.LineTo(new Point(center.X - radius, center.Y));
                    path.Close();
                    break;

                case WipeTransitionShape.Clock:
                    float clockRadius = MathF.Sqrt(size.Width * size.Width + size.Height * size.Height) / 2 + 1;
                    float angle = amount * MathF.Tau;
                    var end = new Point(
                        center.X + MathF.Sin(angle) * clockRadius * (Clockwise ? 1 : -1),
                        center.Y - MathF.Cos(angle) * clockRadius);
                    path.MoveTo(center);
                    path.LineTo(new Point(center.X, center.Y - clockRadius));
                    path.ArcTo(new Size(clockRadius, clockRadius), 0, amount > 0.5f, Clockwise, end);
                    path.Close();
                    break;
            }

            _geometry.Draw(context, path);
        }

        // An opaque-to-clear ramp along Direction, laid across whatever bounds it is drawn into.
        partial void PostReconcile(WipeTransition obj, CompositionContext context)
        {
            Vector direction = ClipTransitionContext.GetDirection(Direction);
            if (_brush == null)
            {
                _brush = new LinearGradientBrush();
                _brush.GradientStops.Add(new GradientStop(Colors.White, 0));
                _brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1));
            }

            _brush.StartPoint.Value = new RelativePoint(
                0.5f - direction.X / 2, 0.5f - direction.Y / 2, RelativeUnit.Relative);
            _brush.EndPoint.Value = new RelativePoint(
                0.5f + direction.X / 2, 0.5f + direction.Y / 2, RelativeUnit.Relative);
            Reconcile(_brush, ref _mask, context);
        }

        partial void PostDispose(bool disposing)
        {
            _geometry.Dispose();
            _mask?.Dispose();
            _mask = null;
            _brush = null;
        }
    }
}
