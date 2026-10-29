using Beutl;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace PackageSample;

public sealed partial class SampleDrawable : Drawable
{
    public SampleDrawable()
    {
        ScanProperties<SampleDrawable>();
    }

    public IExpressionProperty<float> Size { get; } = Property.CreateExpression(100f);

    public new partial class Resource
    {
        protected override Size MeasureCore(Size availableSize)
        {
            return new(Size, Size);
        }

        protected override void OnDraw(GraphicsContext2D context)
        {
            context.DrawRectangle(new(0, 0, Size, Size), Brushes.Resource.White, null);
        }
    }
}
