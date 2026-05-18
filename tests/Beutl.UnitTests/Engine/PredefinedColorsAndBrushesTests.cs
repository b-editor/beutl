using Beutl.Media;

namespace Beutl.UnitTests.Engine;

public class PredefinedColorsAndBrushesTests
{
    [Test]
    public void Colors_BasicColors_HaveExpectedArgb()
    {
        Assert.That(Colors.Black, Is.EqualTo(Color.FromArgb(0xff, 0x00, 0x00, 0x00)));
        Assert.That(Colors.White, Is.EqualTo(Color.FromArgb(0xff, 0xff, 0xff, 0xff)));
        Assert.That(Colors.Red, Is.EqualTo(Color.FromArgb(0xff, 0xff, 0x00, 0x00)));
        Assert.That(Colors.Lime, Is.EqualTo(Color.FromArgb(0xff, 0x00, 0xff, 0x00)));
        Assert.That(Colors.Blue, Is.EqualTo(Color.FromArgb(0xff, 0x00, 0x00, 0xff)));
        Assert.That(Colors.Transparent, Is.EqualTo(Color.FromArgb(0x00, 0xff, 0xff, 0xff)));
    }

    [Test]
    public void Colors_AliceBlue_HasExpectedArgb()
    {
        Assert.That(Colors.AliceBlue, Is.EqualTo(Color.FromArgb(0xff, 0xf0, 0xf8, 0xff)));
    }

    [Test]
    public void Colors_Yellow_HasExpectedArgb()
    {
        Assert.That(Colors.Yellow, Is.EqualTo(Color.FromArgb(0xff, 0xff, 0xff, 0x00)));
    }

    [Test]
    public void Colors_Aqua_HasExpectedArgb()
    {
        Assert.That(Colors.Aqua, Is.EqualTo(Color.FromArgb(0xff, 0x00, 0xff, 0xff)));
    }

    [Test]
    public void Brushes_BasicBrushes_HaveMatchingColors()
    {
        Assert.That(
            Brushes.Black.Color.GetValue(Composition.CompositionContext.Default),
            Is.EqualTo(Colors.Black)
        );
        Assert.That(
            Brushes.White.Color.GetValue(Composition.CompositionContext.Default),
            Is.EqualTo(Colors.White)
        );
        Assert.That(
            Brushes.Red.Color.GetValue(Composition.CompositionContext.Default),
            Is.EqualTo(Colors.Red)
        );
        Assert.That(
            Brushes.Transparent.Color.GetValue(Composition.CompositionContext.Default),
            Is.EqualTo(Colors.Transparent)
        );
    }

    [Test]
    public void Brushes_GreatVariety_DoNotThrow()
    {
        Assert.That(Brushes.AliceBlue, Is.Not.Null);
        Assert.That(Brushes.AntiqueWhite, Is.Not.Null);
        Assert.That(Brushes.Aqua, Is.Not.Null);
        Assert.That(Brushes.Aquamarine, Is.Not.Null);
        Assert.That(Brushes.Azure, Is.Not.Null);
        Assert.That(Brushes.Beige, Is.Not.Null);
        Assert.That(Brushes.Bisque, Is.Not.Null);
        Assert.That(Brushes.BlanchedAlmond, Is.Not.Null);
        Assert.That(Brushes.Yellow, Is.Not.Null);
        Assert.That(Brushes.YellowGreen, Is.Not.Null);
    }

    [Test]
    public void Colors_NewBrushPerInvocation_DistinctButColorEqual()
    {
        var b1 = Brushes.Black;
        var b2 = Brushes.Black;
        Assert.That(
            b1.Color.GetValue(Composition.CompositionContext.Default),
            Is.EqualTo(b2.Color.GetValue(Composition.CompositionContext.Default))
        );
    }
}
