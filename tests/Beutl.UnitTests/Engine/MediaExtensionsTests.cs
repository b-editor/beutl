using Beutl.Graphics;
using Beutl.Media;

namespace Beutl.UnitTests.Engine;

[TestFixture]
public class MediaExtensionsTests
{
    [Test]
    public void CalculateScaling_None_IsIdentity()
    {
        var scale = Stretch.None.CalculateScaling(new Size(100, 100), new Size(50, 50));
        Assert.That(scale.X, Is.EqualTo(1f));
        Assert.That(scale.Y, Is.EqualTo(1f));
    }

    [Test]
    public void CalculateScaling_Fill_DistinctScales()
    {
        var scale = Stretch.Fill.CalculateScaling(new Size(200, 100), new Size(50, 50));
        Assert.That(scale.X, Is.EqualTo(4f));
        Assert.That(scale.Y, Is.EqualTo(2f));
    }

    [Test]
    public void CalculateScaling_Uniform_PicksMin()
    {
        var scale = Stretch.Uniform.CalculateScaling(new Size(200, 100), new Size(50, 50));
        Assert.That(scale.X, Is.EqualTo(2f));
        Assert.That(scale.Y, Is.EqualTo(2f));
    }

    [Test]
    public void CalculateScaling_UniformToFill_PicksMax()
    {
        var scale = Stretch.UniformToFill.CalculateScaling(new Size(200, 100), new Size(50, 50));
        Assert.That(scale.X, Is.EqualTo(4f));
        Assert.That(scale.Y, Is.EqualTo(4f));
    }

    [Test]
    public void CalculateScaling_UpOnly_PreventsShrink()
    {
        var scale = Stretch.Fill.CalculateScaling(new Size(50, 25), new Size(100, 100), StretchDirection.UpOnly);
        Assert.That(scale.X, Is.EqualTo(1f));
        Assert.That(scale.Y, Is.EqualTo(1f));
    }

    [Test]
    public void CalculateScaling_DownOnly_PreventsGrow()
    {
        var scale = Stretch.Fill.CalculateScaling(new Size(200, 200), new Size(100, 100), StretchDirection.DownOnly);
        Assert.That(scale.X, Is.EqualTo(1f));
        Assert.That(scale.Y, Is.EqualTo(1f));
    }

    [Test]
    public void CalculateScaling_ZeroSourceSize_ProducesZeroScale()
    {
        var scale = Stretch.Fill.CalculateScaling(new Size(100, 100), new Size(0, 0));
        Assert.That(scale.X, Is.EqualTo(0f));
        Assert.That(scale.Y, Is.EqualTo(0f));
    }

    [Test]
    public void CalculateScaling_UnconstrainedWidth_PropagatesY()
    {
        var scale = Stretch.Uniform.CalculateScaling(new Size(float.PositiveInfinity, 100), new Size(50, 50));
        Assert.That(scale.X, Is.EqualTo(scale.Y));
        Assert.That(scale.Y, Is.EqualTo(2f));
    }

    [Test]
    public void CalculateScaling_UnconstrainedHeight_PropagatesX()
    {
        var scale = Stretch.Uniform.CalculateScaling(new Size(200, float.PositiveInfinity), new Size(50, 50));
        Assert.That(scale.X, Is.EqualTo(scale.Y));
        Assert.That(scale.X, Is.EqualTo(4f));
    }

    [Test]
    public void CalculateSize_StretchedSourceMatchesScaling()
    {
        var size = Stretch.Uniform.CalculateSize(new Size(200, 100), new Size(50, 50));
        Assert.That(size.Width, Is.EqualTo(100f));
        Assert.That(size.Height, Is.EqualTo(100f));
    }
}
