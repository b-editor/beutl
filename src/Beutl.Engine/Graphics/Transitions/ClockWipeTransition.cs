using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics.Transitions;

/// <summary>Reveals the incoming clip with a sweep starting at twelve o'clock.</summary>
[Display(Name = nameof(GraphicsStrings.ClockWipeTransition), ResourceType = typeof(GraphicsStrings))]
public sealed partial class ClockWipeTransition : ClipTransition
{
    public ClockWipeTransition()
    {
        ScanProperties<ClockWipeTransition>();
    }

    [Display(Name = nameof(GraphicsStrings.ClockWipeTransition_Clockwise), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<bool> Clockwise { get; } = Property.CreateExpression(true);

    public new partial class Resource
    {
        private readonly GeometryMask _mask = new();

        public override void Draw(ClipTransitionContext context)
        {
            using var path = new GeometryContext();
            Size size = context.Graphics.Size;
            Point center = new Rect(size).Center;
            float radius = MathF.Sqrt(size.Width * size.Width + size.Height * size.Height) / 2 + 1;
            float angle = context.Progress * MathF.Tau;
            var end = new Point(
                center.X + MathF.Sin(angle) * radius * (Clockwise ? 1 : -1),
                center.Y - MathF.Cos(angle) * radius);
            path.MoveTo(center);
            path.LineTo(new Point(center.X, center.Y - radius));
            path.ArcTo(new Size(radius, radius), 0, context.Progress > 0.5f, Clockwise, end);
            path.Close();
            _mask.Draw(context, path);
        }

        partial void PostDispose(bool disposing) => _mask.Dispose();
    }
}
