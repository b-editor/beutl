using Beutl.Graphics;

namespace Beutl.UnitTests.Engine;

[TestFixture]
public class CurveMapTests
{
    [Test]
    public void Default_IsIdentityCurve()
    {
        var curve = CurveMap.Default;
        Assert.That(curve.Evaluate(0f), Is.EqualTo(0f).Within(1e-6));
        Assert.That(curve.Evaluate(0.5f), Is.EqualTo(0.5f).Within(1e-6));
        Assert.That(curve.Evaluate(1f), Is.EqualTo(1f).Within(1e-6));
    }

    [Test]
    public void Evaluate_ClampsBelowZero()
    {
        var curve = new CurveMap([new(0.2f, 0.3f), new(1f, 1f)]);
        Assert.That(curve.Evaluate(-0.5f), Is.EqualTo(0.3f).Within(1e-6));
    }

    [Test]
    public void Evaluate_ClampsAboveOne()
    {
        var curve = new CurveMap([new(0f, 0f), new(0.8f, 0.6f)]);
        Assert.That(curve.Evaluate(2f), Is.EqualTo(0.6f).Within(1e-6));
    }

    [Test]
    public void Evaluate_LinearInterpolation()
    {
        var curve = new CurveMap([new(0f, 0f), new(1f, 1f)]);
        Assert.That(curve.Evaluate(0.25f), Is.EqualTo(0.25f).Within(1e-6));
    }

    [Test]
    public void Evaluate_StepBetweenPoints()
    {
        var curve = new CurveMap([new(0f, 0f), new(0.5f, 1f), new(1f, 0f)]);
        Assert.That(curve.Evaluate(0.25f), Is.EqualTo(0.5f).Within(1e-6));
        Assert.That(curve.Evaluate(0.75f), Is.EqualTo(0.5f).Within(1e-6));
    }

    [Test]
    public void Constructor_NormalizesByX()
    {
        var curve = new CurveMap([new(1f, 1f), new(0f, 0f), new(0.5f, 0.5f)]);

        Assert.That(curve.Points[0].Point.X, Is.EqualTo(0f));
        Assert.That(curve.Points[1].Point.X, Is.EqualTo(0.5f));
        Assert.That(curve.Points[2].Point.X, Is.EqualTo(1f));
    }

    [Test]
    public void WithPoints_ReturnsNewInstance()
    {
        var curve = CurveMap.Default;
        var updated = curve.WithPoints([new(0f, 0.5f), new(1f, 0.5f)]);

        Assert.That(updated, Is.Not.SameAs(curve));
        Assert.That(updated.Evaluate(0.5f), Is.EqualTo(0.5f).Within(1e-6));
    }

    [Test]
    public void EvaluateBezier_PassesThroughEndpoints()
    {
        var p0 = new CurveControlPoint(new Point(0f, 0f), default, new Point(0.3f, 0f));
        var p1 = new CurveControlPoint(new Point(1f, 1f), new Point(-0.3f, 0f), default);
        var curve = new CurveMap([p0, p1]);

        Assert.That(curve.Evaluate(0f), Is.EqualTo(0f).Within(1e-3));
        Assert.That(curve.Evaluate(1f), Is.EqualTo(1f).Within(1e-3));
    }

    [Test]
    public void Equals_PointsMatch()
    {
        var a = new CurveMap([new(0f, 0f), new(1f, 1f)]);
        var b = new CurveMap([new(0f, 0f), new(1f, 1f)]);
        var c = new CurveMap([new(0f, 0f), new(0.5f, 0.5f), new(1f, 1f)]);

        Assert.That(a.Equals(b), Is.True);
        Assert.That(a.Equals((object)b), Is.True);
        Assert.That(a.Equals((object)"not a curve"), Is.False);
        Assert.That(a.Equals(c), Is.False);
        Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
    }

    [Test]
    public void Equals_NullReturnsFalse()
    {
        var curve = CurveMap.Default;
        Assert.That(curve.Equals((CurveMap?)null), Is.False);
    }

    [Test]
    public void Equals_SameInstance()
    {
        var curve = CurveMap.Default;
        Assert.That(curve.Equals(curve), Is.True);
    }

    [Test]
    public void EmptyPoints_EvaluateReturnsT()
    {
        var curve = new CurveMap([]);
        Assert.That(curve.Evaluate(0.7f), Is.EqualTo(0.7f).Within(1e-6));
    }
}

[TestFixture]
public class CurveControlPointTests
{
    [Test]
    public void DefaultConstructor_NoHandles()
    {
        var p = new CurveControlPoint(0.5f, 0.5f);
        Assert.That(p.Point.X, Is.EqualTo(0.5f));
        Assert.That(p.Point.Y, Is.EqualTo(0.5f));
        Assert.That(p.HasHandles, Is.False);
        Assert.That(p.LeftHandle, Is.EqualTo(default(Point)));
        Assert.That(p.RightHandle, Is.EqualTo(default(Point)));
    }

    [Test]
    public void Constructor_FromPoint()
    {
        var p = new CurveControlPoint(new Point(0.3f, 0.7f));
        Assert.That(p.Point.X, Is.EqualTo(0.3f));
        Assert.That(p.HasHandles, Is.False);
    }

    [Test]
    public void Constructor_WithHandles()
    {
        var p = new CurveControlPoint(
            new Point(0.5f, 0.5f),
            new Point(-0.1f, 0f),
            new Point(0.1f, 0f)
        );
        Assert.That(p.HasHandles, Is.True);
        Assert.That(p.LeftHandle.X, Is.EqualTo(-0.1f));
        Assert.That(p.RightHandle.X, Is.EqualTo(0.1f));
    }

    [Test]
    public void AbsoluteHandles_AreOffsetFromPoint()
    {
        var p = new CurveControlPoint(
            new Point(0.5f, 0.5f),
            new Point(-0.1f, 0.05f),
            new Point(0.1f, -0.05f)
        );

        Assert.That(p.AbsoluteLeftHandle.X, Is.EqualTo(0.4f).Within(1e-6));
        Assert.That(p.AbsoluteLeftHandle.Y, Is.EqualTo(0.55f).Within(1e-6));
        Assert.That(p.AbsoluteRightHandle.X, Is.EqualTo(0.6f).Within(1e-6));
        Assert.That(p.AbsoluteRightHandle.Y, Is.EqualTo(0.45f).Within(1e-6));
    }

    [Test]
    public void WithPoint_ReplacesPoint()
    {
        var original = new CurveControlPoint(
            new Point(0.5f, 0.5f),
            new Point(-0.1f, 0f),
            new Point(0.1f, 0f)
        );
        var updated = original.WithPoint(new Point(0.7f, 0.7f));

        Assert.That(updated.Point.X, Is.EqualTo(0.7f));
        Assert.That(updated.LeftHandle, Is.EqualTo(original.LeftHandle));
        Assert.That(updated.RightHandle, Is.EqualTo(original.RightHandle));
    }

    [Test]
    public void WithLeftHandle_ReplacesLeftHandle()
    {
        var original = new CurveControlPoint(0.5f, 0.5f);
        var updated = original.WithLeftHandle(new Point(-0.2f, 0f));

        Assert.That(updated.LeftHandle.X, Is.EqualTo(-0.2f));
        Assert.That(updated.Point, Is.EqualTo(original.Point));
    }

    [Test]
    public void WithRightHandle_ReplacesRightHandle()
    {
        var original = new CurveControlPoint(0.5f, 0.5f);
        var updated = original.WithRightHandle(new Point(0.2f, 0f));

        Assert.That(updated.RightHandle.X, Is.EqualTo(0.2f));
    }

    [Test]
    public void WithSymmetricHandles_LeftIsNegated()
    {
        var p = new CurveControlPoint(0.5f, 0.5f).WithSymmetricHandles(new Point(0.1f, 0.05f));

        Assert.That(p.RightHandle.X, Is.EqualTo(0.1f));
        Assert.That(p.RightHandle.Y, Is.EqualTo(0.05f));
        Assert.That(p.LeftHandle.X, Is.EqualTo(-0.1f));
        Assert.That(p.LeftHandle.Y, Is.EqualTo(-0.05f));
    }

    [Test]
    public void Equality_AndHashCode()
    {
        var a = new CurveControlPoint(0.5f, 0.5f);
        var b = new CurveControlPoint(0.5f, 0.5f);
        var c = new CurveControlPoint(0.6f, 0.6f);

        Assert.That(a.Equals(b), Is.True);
        Assert.That(a.Equals((object)b), Is.True);
        Assert.That(a.Equals((object)"not a point"), Is.False);
        Assert.That(a == b, Is.True);
        Assert.That(a != c, Is.True);
        Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
    }

    [Test]
    public void ToString_NoHandles()
    {
        var p = new CurveControlPoint(0.5f, 0.6f);
        Assert.That(p.ToString(), Is.EqualTo("(0.5,0.6)"));
    }

    [Test]
    public void ToString_WithHandles()
    {
        var p = new CurveControlPoint(
            new Point(0.5f, 0.6f),
            new Point(-0.1f, 0f),
            new Point(0.1f, 0f)
        );
        Assert.That(p.ToString(), Does.Contain("L"));
        Assert.That(p.ToString(), Does.Contain("R"));
    }
}
