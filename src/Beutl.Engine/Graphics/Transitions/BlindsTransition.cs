using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics.Transitions;

/// <summary>Reveals the incoming clip in parallel strips that open along the chosen angle.</summary>
[Display(Name = nameof(GraphicsStrings.BlindsTransition), ResourceType = typeof(GraphicsStrings))]
public sealed partial class BlindsTransition : ClipTransition
{
    public BlindsTransition()
    {
        ScanProperties<BlindsTransition>();
    }

    [Display(Name = nameof(GraphicsStrings.Angle), ResourceType = typeof(GraphicsStrings))]
    [Unit(PropertyUnit.Degrees)]
    public IExpressionProperty<float> Angle { get; } = Property.CreateExpression(0f);

    [Range(1, 64)]
    [Display(Name = nameof(GraphicsStrings.BlindsTransition_Count), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<int> Count { get; } = Property.CreateExpression(8);

    public new partial class Resource
    {
        private readonly GeometryMask _mask = new();

        public override void Draw(ClipTransitionContext context)
        {
            if (context.DrawSettledSide()) return;

            using var path = new GeometryContext();
            Size size = context.Graphics.Size;
            float angle = (float.IsFinite(Angle) ? Angle % 360 : 0) * MathF.PI / 180;
            var normal = new Vector(MathF.Cos(angle), MathF.Sin(angle));
            var tangent = new Vector(-normal.Y, normal.X);
            Point center = new Rect(size).Center;
            float halfSpan = (MathF.Abs(normal.X) * size.Width + MathF.Abs(normal.Y) * size.Height) / 2;
            float halfLength = (MathF.Abs(tangent.X) * size.Width + MathF.Abs(tangent.Y) * size.Height) / 2;
            int count = Math.Clamp(Count, 1, 64);
            float extent = 2 * halfSpan / count;
            float revealed = extent * context.Progress;

            for (int i = 0; i < count; i++)
            {
                float start = -halfSpan + extent * i;
                Point first = center + normal * start;
                Point last = center + normal * (start + revealed);
                path.MoveTo(first - tangent * halfLength);
                path.LineTo(last - tangent * halfLength);
                path.LineTo(last + tangent * halfLength);
                path.LineTo(first + tangent * halfLength);
                path.Close();
            }

            _mask.Draw(context, path);
        }

        partial void PostDispose(bool disposing) => _mask.Dispose();
    }
}
