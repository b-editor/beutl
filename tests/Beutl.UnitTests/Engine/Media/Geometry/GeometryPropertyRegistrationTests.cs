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
        geometry.FillType.CurrentValue = PathFillType.EvenOdd;
        geometry.Transform.CurrentValue = new TranslateTransform(5, 6);

        JsonObject json = CoreSerializer.SerializeToJsonObject(geometry);
        var restored = (RectGeometry)CoreSerializer.DeserializeFromJsonObject(json, typeof(RectGeometry));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(geometry.Properties, Does.Contain(geometry.FillType).And.Contain(geometry.Transform));
            Assert.That(restored.FillType.CurrentValue, Is.EqualTo(PathFillType.EvenOdd));
            Assert.That(restored.Transform.CurrentValue, Is.TypeOf<TranslateTransform>());
        }
    }
}
