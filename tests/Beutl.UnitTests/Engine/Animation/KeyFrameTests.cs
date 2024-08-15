using System.Text.Json.Nodes;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Serialization;
using Moq;

namespace Beutl.UnitTests.Engine.Animation;

public class KeyFrameTests
{
    [Test]
    public void Serialize_ShouldCorrectlySerializeLinearEasing()
    {
        var keyFrame = new KeyFrame<int> { Easing = new LinearEasing(), KeyTime = TimeSpan.FromSeconds(1) };
        var context = new Mock<ICoreSerializationContext>();
        var jsonObject = new JsonObject();

        context.Setup(c => c.SetValue(It.IsAny<string>(), It.IsAny<object>()))
            .Callback<string, object>((key, value) => jsonObject[key] = JsonValue.Create(value));

        keyFrame.Serialize(context.Object);

        Assert.That(jsonObject["Easing"]?.GetValue<string>(),
            Is.EqualTo("[Beutl.Engine]Beutl.Animation.Easings:LinearEasing"));
    }

    [Test]
    public void Serialize_ShouldCorrectlySerializeSplineEasing()
    {
        var keyFrame = new KeyFrame<int>
        {
            Easing = new SplineEasing(0.1f, 0.2f, 0.3f, 0.4f),
            KeyTime = TimeSpan.FromSeconds(1)
        };
        var context = new Mock<ICoreSerializationContext>();
        var jsonObject = new JsonObject();

        context.Setup(c => c.SetValue(It.IsAny<string>(), It.IsAny<object>()))
            .Callback<string, object>((key, value) => jsonObject[key] = value as JsonNode ?? JsonValue.Create(value));

        keyFrame.Serialize(context.Object);

        var easingObject = jsonObject["Easing"]!.AsObject();
        Assert.That(easingObject["X1"]!.GetValue<float>(), Is.EqualTo(0.1f));
        Assert.That(easingObject["Y1"]!.GetValue<float>(), Is.EqualTo(0.2f));
        Assert.That(easingObject["X2"]!.GetValue<float>(), Is.EqualTo(0.3f));
        Assert.That(easingObject["Y2"]!.GetValue<float>(), Is.EqualTo(0.4f));
    }

    [Test]
    public void Deserialize_ShouldCorrectlyDeserializeLinearEasing()
    {
        var keyFrame = new KeyFrame<int>();
        var context = new Mock<ICoreSerializationContext>();
        var jsonObject = new JsonObject { ["Easing"] = "[Beutl.Engine]Beutl.Animation.Easings:LinearEasing" };

        context.Setup(c => c.GetValue<JsonNode>(It.IsAny<string>())).Returns(jsonObject["Easing"]);
        context.Setup(c => c.Contains(It.IsAny<string>())).Returns(false);

        keyFrame.Deserialize(context.Object);

        Assert.That(keyFrame.Easing, Is.InstanceOf<LinearEasing>());
    }

    [Test]
    public void Deserialize_ShouldCorrectlyDeserializeSplineEasing()
    {
        var keyFrame = new KeyFrame<int>();
        var context = new Mock<ICoreSerializationContext>();
        var jsonObject = new JsonObject
        {
            ["Easing"] = new JsonObject { ["X1"] = 0.1f, ["Y1"] = 0.2f, ["X2"] = 0.3f, ["Y2"] = 0.4f }
        };

        context.Setup(c => c.GetValue<JsonNode>(It.IsAny<string>())).Returns(jsonObject["Easing"]);
        context.Setup(c => c.Contains(It.IsAny<string>())).Returns(false);

        keyFrame.Deserialize(context.Object);

        var easing = keyFrame.Easing as SplineEasing;
        Assert.That(easing, Is.Not.Null);
        Assert.That(easing!.X1, Is.EqualTo(0.1f));
        Assert.That(easing.Y1, Is.EqualTo(0.2f));
        Assert.That(easing.X2, Is.EqualTo(0.3f));
        Assert.That(easing.Y2, Is.EqualTo(0.4f));
    }
}
