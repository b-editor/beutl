using System.Text.Json.Nodes;
using Beutl.Graphics.Effects;
using Beutl.Media;
using Beutl.Serialization;

namespace Beutl.UnitTests.Serialization;

// StrokeEffect chose whether its stroke went over or under the content with a Style member, which the pen's stroke
// order replaced. A file saved with it still loads with the same drawing order.
public class StrokeEffectStyleMigrationTests
{
    [TestCase(0, StrokeOrder.AboveFill)]
    [TestCase(1, StrokeOrder.BelowFill)]
    public void Style_LoadsAsThePenStrokeOrder(int style, StrokeOrder expected)
    {
        JsonObject json = CreateSavedWithStyle(style);

        var restored = (StrokeEffect)CoreSerializer.DeserializeFromJsonObject(json, typeof(StrokeEffect));

        Assert.That(restored.Pen.Value!.StrokeOrder.Value, Is.EqualTo(expected));
    }

    [Test]
    public void Style_IsNotWrittenBack()
    {
        JsonObject json = CreateSavedWithStyle(1);

        var restored = (StrokeEffect)CoreSerializer.DeserializeFromJsonObject(json, typeof(StrokeEffect));
        JsonObject written = CoreSerializer.SerializeToJsonObject(restored);

        Assert.Multiple(() =>
        {
            Assert.That(written.ContainsKey("Style"), Is.False);
            Assert.That(
                written["Pen"]![nameof(Pen.StrokeOrder)]!.GetValue<int>(), Is.EqualTo((int)StrokeOrder.BelowFill));
        });
    }

    // The JSON a build before the stroke order wrote: a Style member, and a pen without StrokeOrder.
    private static JsonObject CreateSavedWithStyle(int style)
    {
        var effect = new StrokeEffect();
        effect.Pen.Value!.Thickness.Value = 8;
        JsonObject json = CoreSerializer.SerializeToJsonObject(effect);
        json["Pen"]!.AsObject().Remove(nameof(Pen.StrokeOrder));
        json["Style"] = style;
        return json;
    }
}
