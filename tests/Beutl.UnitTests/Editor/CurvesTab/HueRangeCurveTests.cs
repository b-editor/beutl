using Beutl.Editor.Components.CurvesTab.Services;
using Beutl.Graphics;

namespace Beutl.UnitTests.Editor.CurvesTab;

[TestFixture]
public sealed class HueRangeCurveTests
{
    private static readonly CurveMap s_flat = new([new CurveControlPoint(0, 0.5f), new CurveControlPoint(1, 0.5f)]);

    // A curve already edited by hand, with a straight piece and a bent one on either side of the range.
    private static readonly CurveMap s_edited = new(
    [
        new CurveControlPoint(0, 0.5f),
        new CurveControlPoint(0.2f, 0.3f),
        new CurveControlPoint(new Point(0.45f, 0.7f), new Point(-0.05f, 0.02f), new Point(0.05f, -0.02f)),
        new CurveControlPoint(new Point(0.8f, 0.6f), new Point(-0.1f, 0.1f), new Point(0.1f, -0.05f)),
        new CurveControlPoint(1, 0.5f),
    ]);

    [TestCase(0.3f, 0.1f)]
    [TestCase(0.55f, 0.2f)]
    [TestCase(0.47f, 0.05f)]
    public void Outside_the_range_the_curve_keeps_its_shape(float center, float width)
    {
        CurveMap shaped = Shape(s_edited, center, width, 0.95f);

        Assert.Multiple(() =>
        {
            foreach (float x in Samples())
            {
                if (MathF.Abs(x - center) >= width / 2)
                    Assert.That(shaped.Evaluate(x), Is.EqualTo(s_edited.Evaluate(x)).Within(1e-4f), $"x = {x}");
            }

            Assert.That(shaped.Evaluate(center), Is.EqualTo(0.95f).Within(1e-4f));
        });
    }

    [Test]
    public void Points_away_from_the_range_stay_as_they_were()
    {
        CurveMap shaped = Shape(s_edited, 0.3f, 0.1f, 0.1f);

        Assert.That(shaped.Points, Does.Contain(s_edited.Points[3]));
        Assert.That(shaped.Points, Does.Contain(s_edited.Points[4]));
        Assert.That(shaped.Points.Count(p => p.Point.X > 0.25f && p.Point.X < 0.35f), Is.EqualTo(1), "Only the center lies inside.");
    }

    // On a level curve the range is a bump that falls off alike on both sides and stays within the values it joins.
    [Test]
    public void The_bump_falls_off_alike_on_both_sides()
    {
        CurveMap shaped = Shape(s_flat, 0.5f, 0.2f, 0.9f);

        Assert.Multiple(() =>
        {
            for (float d = 0; d <= 0.1f; d += 0.005f)
            {
                Assert.That(shaped.Evaluate(0.5f - d), Is.EqualTo(shaped.Evaluate(0.5f + d)).Within(1e-4f), $"d = {d}");
                Assert.That(shaped.Evaluate(0.5f + d), Is.InRange(0.5f - 1e-4f, 0.9f + 1e-4f));
            }

            Assert.That(shaped.Evaluate(0.42f), Is.LessThan(shaped.Evaluate(0.46f)));
        });
    }

    // Hue 0 and hue 1 are the same red, so a range across them bends both ends of the curve alike and falls off as it
    // does elsewhere, on the far side of the ends as on the near one.
    [TestCase(0.02f)]
    [TestCase(0.97f)]
    [TestCase(0f)]
    [TestCase(0.99995f)]
    public void A_range_across_the_ends_goes_on_at_the_other_end(float center)
    {
        const float width = 60f / 360;
        CurveMap shaped = Shape(s_flat, center, width, 0.8f);

        Assert.Multiple(() =>
        {
            Assert.That(shaped.Points[0].Point.X, Is.Zero);
            Assert.That(shaped.Points[^1].Point.X, Is.EqualTo(1));
            Assert.That(shaped.Evaluate(0), Is.EqualTo(shaped.Evaluate(1)).Within(1e-4f), "The ends are one red.");
            Assert.That(shaped.Evaluate(Wrap(center)), Is.EqualTo(0.8f).Within(1e-3f));
            for (float d = 0.005f; d < width / 2; d += 0.005f)
            {
                Assert.That(shaped.Evaluate(Wrap(center - d)), Is.EqualTo(shaped.Evaluate(Wrap(center + d))).Within(1e-3f), $"d = {d}");
            }

            Assert.That(shaped.Evaluate(Wrap(center + 0.5f)), Is.EqualTo(0.5f).Within(1e-6f), "The opposite hue does not change.");
        });
    }

    // The tab shapes each edit from the curve before the first, so narrowing the range gives back what it left.
    [Test]
    public void Shaping_the_original_again_with_a_narrower_range_gives_back_what_it_left()
    {
        CurveMap wide = Shape(s_edited, 0.3f, 0.3f, 0.9f);
        CurveMap narrow = Shape(s_edited, 0.3f, 0.1f, 0.9f);

        Assert.Multiple(() =>
        {
            Assert.That(wide.Evaluate(0.2f), Is.Not.EqualTo(s_edited.Evaluate(0.2f)).Within(1e-3f));
            Assert.That(narrow.Evaluate(0.2f), Is.EqualTo(s_edited.Evaluate(0.2f)).Within(1e-4f));
            Assert.That(narrow.Evaluate(0.42f), Is.EqualTo(s_edited.Evaluate(0.42f)).Within(1e-4f));
        });
    }

    [Test]
    public void Shaping_the_result_again_changes_nothing()
    {
        CurveMap once = Shape(s_edited, 0.62f, 0.25f, 0.2f);
        CurveMap twice = Shape(once, 0.62f, 0.25f, 0.2f);

        Assert.Multiple(() =>
        {
            foreach (float x in Samples())
                Assert.That(twice.Evaluate(x), Is.EqualTo(once.Evaluate(x)).Within(1e-4f), $"x = {x}");
        });
    }

    // A handle that reaches past the next point makes x go back and forth along the piece. CurveMap.Evaluate reads one of
    // the piece's crossings of each x and the halves of a cut would read others, so an edge there leaves the curve alone.
    [TestCase(0.3f, 0.2f)]
    [TestCase(0.95f, 0.2f)]
    public void An_edge_where_x_goes_back_and_forth_leaves_the_curve_as_it_is(float center, float width)
    {
        var crossing = new CurveMap(
        [
            new CurveControlPoint(new Point(0, 0.4f), default, new Point(2, 0)),
            new CurveControlPoint(new Point(1, 0.6f), new Point(-2, 0), default),
        ]);

        Assert.That(HueRangeCurve.Shape(crossing, center, width, 0.9f), Is.Null);
    }

    [Test]
    public void A_piece_where_x_goes_back_and_forth_away_from_the_range_stays_as_it_was()
    {
        var curve = new CurveMap(
        [
            new CurveControlPoint(0, 0.5f),
            new CurveControlPoint(new Point(0.6f, 0.4f), default, new Point(0.4f, 0)),
            new CurveControlPoint(new Point(0.8f, 0.6f), new Point(-0.4f, 0), default),
            new CurveControlPoint(1, 0.5f),
        ]);

        CurveMap shaped = Shape(curve, 0.3f, 0.2f, 0.9f);

        Assert.Multiple(() =>
        {
            Assert.That(shaped.Evaluate(0.3f), Is.EqualTo(0.9f).Within(1e-4f));
            for (int i = 1; i < 20; i++)
            {
                float x = 0.6f + i * 0.01f;
                Assert.That(shaped.Evaluate(x), Is.EqualTo(curve.Evaluate(x)), $"x = {x}");
            }
        });
    }

    [Test]
    public void A_range_wider_than_the_widest_is_held_to_it()
    {
        CurveMap shaped = Shape(s_flat, 0.5f, 1.5f, 1f);
        float edge = 0.5f + HueRangeCurve.MaxWidth / 2;

        Assert.Multiple(() =>
        {
            Assert.That(shaped.Evaluate(edge + 0.01f), Is.EqualTo(0.5f).Within(1e-6f));
            Assert.That(shaped.Evaluate(0.5f), Is.EqualTo(1f).Within(1e-4f));
        });
    }

    // The other curves here have no piece whose x goes back and forth, so each range shapes them.
    private static CurveMap Shape(CurveMap curve, float center, float width, float value)
        => HueRangeCurve.Shape(curve, center, width, value) ?? throw new AssertionException("The range left the curve as it was.");

    private static float Wrap(float x) => x - MathF.Floor(x);

    private static IEnumerable<float> Samples()
    {
        for (int i = 0; i <= 400; i++)
            yield return i / 400f;
    }
}
