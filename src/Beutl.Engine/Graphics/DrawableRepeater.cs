using System.ComponentModel.DataAnnotations;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics;

/// <summary>
/// Draws its children again in every cell of a grid around where they are. Every cell draws the same resources, so an
/// edit reaches all of them and no picture is copied. A child's own transform and effects come first, then where its
/// cell is and whether it mirrors, and last this object's transform, opacity and effects, on the grid as a whole.
/// </summary>
[Display(Name = nameof(GraphicsStrings.DrawableRepeater), ResourceType = typeof(GraphicsStrings))]
public sealed partial class DrawableRepeater : Drawable, IFlowOperator, IDrawableContainer
{
    private const int MaxCount = 100;

    public DrawableRepeater()
    {
        ScanProperties<DrawableRepeater>();
        HideProperties(AlignmentX, AlignmentY);
    }

    [SuppressResourceClassGeneration]
    [Display(Name = nameof(GraphicsStrings.Children), ResourceType = typeof(GraphicsStrings))]
    public IListProperty<Drawable> Children { get; } = Property.CreateList<Drawable>();

    [Display(Name = nameof(GraphicsStrings.DrawableRepeater_Columns), ResourceType = typeof(GraphicsStrings))]
    [Range(1, MaxCount)]
    public IExpressionProperty<int> Columns { get; } = Property.CreateAnimatable(3);

    [Display(Name = nameof(GraphicsStrings.DrawableRepeater_Rows), ResourceType = typeof(GraphicsStrings))]
    [Range(1, MaxCount)]
    public IExpressionProperty<int> Rows { get; } = Property.CreateAnimatable(3);

    /// <summary>The distance from a cell to the next one.</summary>
    [Display(Name = nameof(GraphicsStrings.DrawableRepeater_Spacing), ResourceType = typeof(GraphicsStrings))]
    [Range(typeof(Size), "0,0", "max,max")]
    public IExpressionProperty<Size> Spacing { get; } = Property.CreateAnimatable(new Size(200, 200));

    [Display(Name = nameof(GraphicsStrings.Offset), Description = nameof(GraphicsStrings.DrawableRepeater_Offset_Description),
        ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<Point> Offset { get; } = Property.CreateAnimatable(default(Point));

    [Display(Name = nameof(GraphicsStrings.DrawableRepeater_Mirror), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<DrawableRepeaterMirror> Mirror { get; } = Property.CreateAnimatable(DrawableRepeaterMirror.None);

    public new partial class Resource
    {
        private readonly FlowChildren _children = new(keepsFlowInputs: true);

        internal override IReadOnlyList<FlowNode> FlowInputs => _children.FlowInputs;

        internal override EngineObject? FlowOwner => _children.FlowOwner;

        public List<Drawable.Resource> Children
        {
            get => _children.Items;
            set => _children.Items = value;
        }

        protected override void RenderCore(GraphicsContext2D context)
        {
            Size availableSize = context.Size;
            var boundsMemory = context.UseMemory<Rect>();
            var transformParams = (Transform, TransformOrigin, availableSize, boundsMemory);
            // Cells that overlap count as one picture, as the children of a group do.
            bool isolatesContent = Opacity != 100f
                                   || EvaluatedBlendMode != Graphics.BlendMode.SrcOver
                                   || Children.Any(static child => child.BlendMode != Graphics.BlendMode.SrcOver);

            using (context.PushBlendMode(BlendMode))
            using (DrawableGroup.PushCustomTransform(context, transformParams))
            using (context.PushOpacity(Opacity / 100f))
            using (ContentIsolationRenderNode.Push(context, isolatesContent))
            using (FilterEffect == null ? new() : context.PushFilterEffect(FilterEffect))
            using (DrawableGroup.PushContentBounds(context, boundsMemory))
            {
                OnDraw(context);
            }
        }

        protected override void OnDraw(GraphicsContext2D context)
        {
            int columns = Math.Clamp(Columns, 1, MaxCount);
            int rows = Math.Clamp(Rows, 1, MaxCount);
            float middleX = context.Size.Width / 2;
            float middleY = context.Size.Height / 2;
            bool mirrorColumns = Mirror is DrawableRepeaterMirror.Columns or DrawableRepeaterMirror.Both;
            bool mirrorRows = Mirror is DrawableRepeaterMirror.Rows or DrawableRepeaterMirror.Both;
            for (int row = 0; row < rows; row++)
            {
                float y = CellPosition(row, rows, Spacing.Height, Offset.Y);
                for (int column = 0; column < columns; column++)
                {
                    float x = CellPosition(column, columns, Spacing.Width, Offset.X);
                    Matrix cell = Matrix.CreateTranslation(-middleX, -middleY)
                                  * Matrix.CreateScale(mirrorColumns && column % 2 == 1 ? -1 : 1, mirrorRows && row % 2 == 1 ? -1 : 1)
                                  * Matrix.CreateTranslation(middleX + x, middleY + y);
                    using (context.PushTransform(cell))
                    {
                        foreach (Drawable.Resource child in Children)
                        {
                            context.DrawDrawable(child);
                        }
                    }
                }
            }
        }

        // The grid is centered on where the children are. A cell that the offset takes past one end comes back at the
        // other, which is what lets an animated offset scroll without end.
        private static float CellPosition(int index, int count, float spacing, float offset)
        {
            float span = count * spacing;
            float position = index * spacing + offset;
            if (span > 0)
                position -= MathF.Floor(position / span) * span;
            return position - (count - 1) * spacing / 2;
        }

        protected override Size MeasureCore(Size availableSize)
        {
            return Size.Empty;
        }

        partial void PreReconcile(DrawableRepeater obj, CompositionContext context)
        {
            if (_children.Reconcile(context, obj.Children, obj))
                Version++;
        }

        partial void PostDispose(bool disposing)
        {
            _children.Dispose();
        }
    }
}
