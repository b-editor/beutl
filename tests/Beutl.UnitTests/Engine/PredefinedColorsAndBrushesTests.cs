using Beutl.Media;

namespace Beutl.UnitTests.Engine;

public class PredefinedColorsAndBrushesTests
{
    [Test]
    public void Colors_BasicColors_HaveExpectedArgb()
    {
        Assert.That(Colors.Lime, Is.EqualTo(Color.FromArgb(0xff, 0x00, 0xff, 0x00)));
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
        Assert.That(Brushes.Black.Color.Evaluate(Composition.CompositionContext.Default), Is.EqualTo(Colors.Black));
        Assert.That(Brushes.White.Color.Evaluate(Composition.CompositionContext.Default), Is.EqualTo(Colors.White));
        Assert.That(Brushes.Red.Color.Evaluate(Composition.CompositionContext.Default), Is.EqualTo(Colors.Red));
        Assert.That(Brushes.Transparent.Color.Evaluate(Composition.CompositionContext.Default), Is.EqualTo(Colors.Transparent));
    }
}
