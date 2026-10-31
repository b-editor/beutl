using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics.Transitions;

/// <summary>Alternating tiles expand from their centres, followed by the remaining checkerboard tiles.</summary>
[Display(Name = nameof(GraphicsStrings.CheckerboardTransition), ResourceType = typeof(GraphicsStrings))]
public sealed partial class CheckerboardTransition : ClipTransition
{
    public CheckerboardTransition()
    {
        ScanProperties<CheckerboardTransition>();
    }

    [Range(1, 64)]
    [Display(Name = nameof(GraphicsStrings.CheckerboardTransition_Columns), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<int> Columns { get; } = Property.CreateExpression(8);

    [Range(1, 64)]
    [Display(Name = nameof(GraphicsStrings.CheckerboardTransition_Rows), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<int> Rows { get; } = Property.CreateExpression(6);

    public new partial class Resource
    {
        private readonly GeometryMask _mask = new();

        public override void Draw(ClipTransitionContext context)
        {
            if (context.DrawSettledSide()) return;

            using var path = new GeometryContext();
            int columns = Math.Clamp(Columns, 1, 64);
            int rows = Math.Clamp(Rows, 1, 64);
            float width = context.Graphics.Size.Width / columns;
            float height = context.Graphics.Size.Height / rows;

            for (int row = 0; row < rows; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    float amount = Math.Clamp(context.Progress * 2 - ((row + column) % 2), 0, 1);
                    GeometryMask.AddRectangle(path, new Rect(
                        (column + (1 - amount) / 2) * width,
                        (row + (1 - amount) / 2) * height,
                        width * amount, height * amount));
                }
            }

            _mask.Draw(context, path);
        }

        partial void PostDispose(bool disposing) => _mask.Dispose();
    }
}
