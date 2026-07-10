using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.UnitTests.ProjectSystem;

[TestFixture]
public class ClipLockSerializationTests
{
    [Test]
    public void TimelineLayer_LockMuteSoloFlags_RoundTrip()
    {
        var layer = new TimelineLayer
        {
            ZIndex = 3,
            IsLocked = true,
            IsAudioMuted = true,
            IsVideoMuted = true,
            IsSolo = true,
        };

        var json = CoreSerializer.SerializeToJsonObject(layer);
        var restored = (TimelineLayer)CoreSerializer.DeserializeFromJsonObject(json, typeof(TimelineLayer));

        Assert.Multiple(() =>
        {
            Assert.That(restored.ZIndex, Is.EqualTo(3));
            Assert.That(restored.IsLocked, Is.True);
            Assert.That(restored.IsAudioMuted, Is.True);
            Assert.That(restored.IsVideoMuted, Is.True);
            Assert.That(restored.IsSolo, Is.True);
        });
    }

    [Test]
    public void TimelineLayer_DefaultFlags_StayFalseAfterRoundTrip()
    {
        var layer = new TimelineLayer { ZIndex = 1 };

        var json = CoreSerializer.SerializeToJsonObject(layer);
        var restored = (TimelineLayer)CoreSerializer.DeserializeFromJsonObject(json, typeof(TimelineLayer));

        Assert.Multiple(() =>
        {
            Assert.That(restored.IsLocked, Is.False);
            Assert.That(restored.IsAudioMuted, Is.False);
            Assert.That(restored.IsVideoMuted, Is.False);
            Assert.That(restored.IsSolo, Is.False);
        });
    }

    [Test]
    public void Element_IsLocked_RoundTrips()
    {
        var element = new Element { IsLocked = true };

        var json = CoreSerializer.SerializeToJsonObject(element);
        var restored = (Element)CoreSerializer.DeserializeFromJsonObject(json, typeof(Element));

        Assert.That(restored.IsLocked, Is.True);
    }

    [Test]
    public void Element_IsLocked_DefaultsToFalseAfterRoundTrip()
    {
        var element = new Element();

        var json = CoreSerializer.SerializeToJsonObject(element);
        var restored = (Element)CoreSerializer.DeserializeFromJsonObject(json, typeof(Element));

        Assert.That(restored.IsLocked, Is.False);
    }
}
