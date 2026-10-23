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
            ZIndex = { Value = 3 },
            IsLocked = { Value = true },
            IsAudioMuted = { Value = true },
            IsVideoMuted = { Value = true },
            IsSolo = { Value = true },
        };

        var json = CoreSerializer.SerializeToJsonObject(layer);
        var restored = (TimelineLayer)CoreSerializer.DeserializeFromJsonObject(json, typeof(TimelineLayer));

        Assert.Multiple(() =>
        {
            Assert.That(restored.ZIndex.Value, Is.EqualTo(3));
            Assert.That(restored.IsLocked.Value, Is.True);
            Assert.That(restored.IsAudioMuted.Value, Is.True);
            Assert.That(restored.IsVideoMuted.Value, Is.True);
            Assert.That(restored.IsSolo.Value, Is.True);
        });
    }

    [Test]
    public void TimelineLayer_DefaultFlags_StayFalseAfterRoundTrip()
    {
        var layer = new TimelineLayer { ZIndex = { Value = 1 } };

        var json = CoreSerializer.SerializeToJsonObject(layer);
        var restored = (TimelineLayer)CoreSerializer.DeserializeFromJsonObject(json, typeof(TimelineLayer));

        Assert.Multiple(() =>
        {
            Assert.That(restored.IsLocked.Value, Is.False);
            Assert.That(restored.IsAudioMuted.Value, Is.False);
            Assert.That(restored.IsVideoMuted.Value, Is.False);
            Assert.That(restored.IsSolo.Value, Is.False);
        });
    }

    [Test]
    public void Element_IsLocked_RoundTrips()
    {
        var element = new Element { IsLocked = { Value = true } };

        var json = CoreSerializer.SerializeToJsonObject(element);
        var restored = (Element)CoreSerializer.DeserializeFromJsonObject(json, typeof(Element));

        Assert.That(restored.IsLocked.Value, Is.True);
    }

    [Test]
    public void Element_IsLocked_DefaultsToFalseAfterRoundTrip()
    {
        var element = new Element();

        var json = CoreSerializer.SerializeToJsonObject(element);
        var restored = (Element)CoreSerializer.DeserializeFromJsonObject(json, typeof(Element));

        Assert.That(restored.IsLocked.Value, Is.False);
    }
}
