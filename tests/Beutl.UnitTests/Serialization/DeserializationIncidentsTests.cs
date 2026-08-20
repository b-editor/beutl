using System.Text.Json.Nodes;
using Beutl.Graphics.Transformation;
using Beutl.Serialization;

namespace Beutl.UnitTests.Serialization;

public sealed class DeserializationIncidentsTests
{
    [Test]
    public void TryCreateFallback_RecordsAnIncident()
    {
        int before = DeserializationIncidents.FallbackCount;

        ICoreSerializable? fallback = FallbackDeserializationHelper.TryCreateFallback(
            typeof(Transform), null, new JsonObject());

        Assert.Multiple(() =>
        {
            Assert.That(fallback, Is.InstanceOf<IFallback>());
            Assert.That(DeserializationIncidents.FallbackCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void TryCreateFallback_WithoutFallbackType_RecordsNothing()
    {
        int before = DeserializationIncidents.FallbackCount;

        ICoreSerializable? fallback = FallbackDeserializationHelper.TryCreateFallback(
            typeof(CoreObject), null, new JsonObject());

        Assert.Multiple(() =>
        {
            Assert.That(fallback, Is.Null);
            Assert.That(DeserializationIncidents.FallbackCount, Is.EqualTo(before));
        });
    }
}
