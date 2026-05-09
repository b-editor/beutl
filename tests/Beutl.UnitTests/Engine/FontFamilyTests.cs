using Beutl.Media;

namespace Beutl.UnitTests.Engine;

[TestFixture]
public class FontFamilyTests
{
    [Test]
    public void Default_HasNonEmptyName()
    {
        Assert.That(FontFamily.Default.Name, Is.Not.Empty);
    }

    [Test]
    public void Constructor_StoresName()
    {
        var family = new FontFamily("Arial");
        Assert.That(family.Name, Is.EqualTo("Arial"));
    }

    [Test]
    public void Equals_SameName()
    {
        var a = new FontFamily("Arial");
        var b = new FontFamily("Arial");
        Assert.That(a.Equals(b), Is.True);
        Assert.That(a.Equals((object)b), Is.True);
        Assert.That(a == b, Is.True);
        Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
    }

    [Test]
    public void Equals_DifferentName()
    {
        var a = new FontFamily("Arial");
        var b = new FontFamily("Times");
        Assert.That(a.Equals(b), Is.False);
        Assert.That(a != b, Is.True);
    }

    [Test]
    public void Equals_Null()
    {
        var a = new FontFamily("Arial");
        Assert.That(a.Equals((FontFamily?)null), Is.False);
    }

    [Test]
    public void Operator_NullHandling()
    {
        FontFamily? a = null;
        FontFamily? b = null;
#pragma warning disable CS8602
        Assert.That(a == b, Is.True);
        Assert.That(a != new FontFamily("Arial"), Is.True);
#pragma warning restore CS8602
        Assert.That(new FontFamily("Arial") != null, Is.True);
    }

    [Test]
    public void Equals_OtherTypeReturnsFalse()
    {
        var a = new FontFamily("Arial");
        Assert.That(a.Equals((object)"Arial"), Is.False);
    }
}

[TestFixture]
public class TypefaceTests
{
    [Test]
    public void Constructor_DefaultStyleAndWeight()
    {
        var family = new FontFamily("Arial");
        var typeface = new Typeface(family);

        Assert.That(typeface.FontFamily, Is.EqualTo(family));
        Assert.That(typeface.Style, Is.EqualTo(FontStyle.Normal));
        Assert.That(typeface.Weight, Is.EqualTo(FontWeight.Regular));
    }

    [Test]
    public void Constructor_StoresAllFields()
    {
        var family = new FontFamily("Arial");
        var typeface = new Typeface(family, FontStyle.Italic, FontWeight.Bold);

        Assert.That(typeface.Style, Is.EqualTo(FontStyle.Italic));
        Assert.That(typeface.Weight, Is.EqualTo(FontWeight.Bold));
    }

    [Test]
    public void Equality_AllFieldsMatch()
    {
        var a = new Typeface(new FontFamily("Arial"), FontStyle.Italic, FontWeight.Bold);
        var b = new Typeface(new FontFamily("Arial"), FontStyle.Italic, FontWeight.Bold);

        Assert.That(a.Equals(b), Is.True);
        Assert.That(a.Equals((object)b), Is.True);
        Assert.That(a == b, Is.True);
        Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
    }

    [Test]
    public void Equality_DifferentStyle()
    {
        var a = new Typeface(new FontFamily("Arial"));
        var b = new Typeface(new FontFamily("Arial"), FontStyle.Italic);

        Assert.That(a.Equals(b), Is.False);
        Assert.That(a != b, Is.True);
    }

    [Test]
    public void Equality_DifferentWeight()
    {
        var a = new Typeface(new FontFamily("Arial"));
        var b = new Typeface(new FontFamily("Arial"), FontStyle.Normal, FontWeight.Bold);

        Assert.That(a.Equals(b), Is.False);
    }

    [Test]
    public void Equality_OtherType()
    {
        var a = new Typeface(new FontFamily("Arial"));
        Assert.That(a.Equals((object)"not a typeface"), Is.False);
    }
}
