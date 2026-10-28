using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics.Transitions;

/// <summary>Reveals the incoming clip in parallel strips that open in the chosen direction.</summary>
[Display(Name = nameof(GraphicsStrings.BlindsTransition), ResourceType = typeof(GraphicsStrings))]
public sealed partial class BlindsTransition : ClipTransition
{
    public BlindsTransition()
    {
        ScanProperties<BlindsTransition>();
    }

    [Display(Name = nameof(GraphicsStrings.Direction), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<ClipTransitionDirection> Direction { get; } = Property.CreateExpression(ClipTransitionDirection.LeftToRight);

    [Range(1, 64)]
    [Display(Name = nameof(GraphicsStrings.BlindsTransition_Count), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<int> Count { get; } = Property.CreateExpression(8);

    public new partial class Resource
    {
        private readonly GeometryMask _mask = new();

        public override void Draw(ClipTransitionContext context)
        {
            using var path = new GeometryContext();
            Size size = context.Graphics.Size;
            Vector direction = ClipTransitionContext.GetDirection(Direction);
            bool horizontal = direction.X != 0;
            bool reverse = direction.X < 0 || direction.Y < 0;
            int count = Math.Clamp(Count, 1, 64);
            float extent = (horizontal ? size.Width : size.Height) / count;
            float revealed = extent * context.Progress;

            for (int i = 0; i < count; i++)
            {
                float start = extent * i + (reverse ? extent - revealed : 0);
                GeometryMask.AddRectangle(path, horizontal
                    ? new Rect(start, 0, revealed, size.Height)
                    : new Rect(0, start, size.Width, revealed));
            }

            _mask.Draw(context, path);
        }

        partial void PostDispose(bool disposing) => _mask.Dispose();
    }
}
