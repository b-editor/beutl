using System.Text.Json.Nodes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.Serialization;

namespace Beutl.UnitTests.Engine.Media.Geometry;

[TestFixture]
public sealed class GeometryPropertyRegistrationTests
{
    [Test]
    public void FillTypeAndTransform_AreRegisteredAndSaved()
    {
        var geometry = new RectGeometry();
        geometry.FillType.Value = PathFillType.EvenOdd;
        geometry.Transform.Value = new TranslateTransform(5, 6);

        JsonObject json = CoreSerializer.SerializeToJsonObject(geometry);
        var restored = (RectGeometry)CoreSerializer.DeserializeFromJsonObject(json, typeof(RectGeometry));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(geometry.Properties, Does.Contain(geometry.FillType).And.Contain(geometry.Transform));
            Assert.That(restored.FillType.Value, Is.EqualTo(PathFillType.EvenOdd));
            Assert.That(restored.Transform.Value, Is.TypeOf<TranslateTransform>());
        }
    }
}
