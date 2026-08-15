using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Media;

[TestFixture]
public class TypefaceCollectionTests
{
    // A material package can ship a font the system already has, so the same
    // family/style/weight arrives twice. FontManager builds its map during static
    // initialization, so throwing here takes the whole process down at startup.
    [Test]
    public void Create_KeepsTheFirstEntry_WhenTwoTypefacesShareAKey()
    {
        SKTypeface first = SKTypeface.Default;
        SKTypeface second = SKTypeface.Default;

        var collection = TypefaceCollection.Create([first, second]);

        Assert.Multiple(() =>
        {
            Assert.That(collection, Has.Count.EqualTo(1));
            Assert.That(collection.Values[0], Is.SameAs(first));
        });
    }
}
