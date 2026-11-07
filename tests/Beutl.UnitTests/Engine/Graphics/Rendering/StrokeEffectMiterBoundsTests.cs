using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

// A mitered pen reaches past the outline at a corner (#4104). StrokeEffect's output allows for corners down to about
// 39 degrees, three times what the pen reaches past a straight side, or as far as the pen's own miter limit allows.
[TestFixture]
public class StrokeEffectMiterBoundsTests
{
    private static readonly Rect Source = new(0, 0, 100, 60);

    private static Rect Bounds(StrokeJoin join, float miterLimit = 10, float penOffset = 0,
        StrokeAlignment alignment = StrokeAlignment.Center)
    {
        var pen = new Pen();
        pen.Thickness.Value = 14;
        pen.Brush.Value = Brushes.Red;
        pen.StrokeJoin.Value = join;
        pen.MiterLimit.Value = miterLimit;
        pen.Offset.Value = penOffset;
        pen.StrokeAlignment.Value = alignment;
        var effect = new StrokeEffect();
        effect.Pen.Value = pen;

        var context = new FilterEffectContext(Source);
        context.ApplyTransactional(effect.ToResource(CompositionContext.Default));
        return context.Bounds;
    }

    // The thickness is 14, so the pen reaches 7 past a straight side when centred on the outline.
    [TestCase(StrokeJoin.Miter, 10f, 21f, TestName = "A_mitered_corner_reaches_three_times_as_far_as_a_side")]
    [TestCase(StrokeJoin.Miter, 2f, 14f, TestName = "A_low_miter_limit_reaches_no_further_than_it_allows")]
    [TestCase(StrokeJoin.Miter, 0.5f, 7f, TestName = "A_miter_limit_under_one_reaches_as_far_as_a_side")]
    [TestCase(StrokeJoin.Round, 10f, 7f, TestName = "A_round_join_reaches_as_far_as_a_side")]
    [TestCase(StrokeJoin.Bevel, 10f, 7f, TestName = "A_bevel_join_reaches_as_far_as_a_side")]
    public void Output_allows_for_the_corner_reach(StrokeJoin join, float miterLimit, float expected)
    {
        Assert.That(Bounds(join, miterLimit), Is.EqualTo(Source.Inflate(expected)));
    }

    [Test]
    public void A_positive_pen_offset_is_mitered_too()
    {
        // The offset outline's corners are mitered like the stroke: 3 x (7 + 10).
        Assert.That(Bounds(StrokeJoin.Miter, penOffset: 10), Is.EqualTo(Source.Inflate(51)));
    }

    [Test]
    public void An_outside_stroke_reaches_its_whole_thickness_from_a_side()
    {
        Assert.That(Bounds(StrokeJoin.Miter, alignment: StrokeAlignment.Outside), Is.EqualTo(Source.Inflate(42)));
    }

    [Test]
    public void An_inside_stroke_stays_within_the_content()
    {
        Assert.That(Bounds(StrokeJoin.Miter, alignment: StrokeAlignment.Inside), Is.EqualTo(Source));
    }
}
