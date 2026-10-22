using System.Text.Json.Nodes;
using Beutl.Animation;
using Beutl.Graphics.Shapes;
using Beutl.Serialization;

namespace Beutl.AgentToolkit.Tests.Helpers;

// A keyframe easing whose type is missing makes recovery keep its element's file, since that load incident leaves
// no fallback to save it again. An object whose type is missing no longer does: its fallback saves the JSON it was
// read from, so the tests of protected elements use this instead.
internal static class UnreadableEasing
{
    public const string MissingEasingType = "[Missing.Assembly]Missing.Namespace:MissingEasing";

    // Gives the shape that shapeJson describes a width keyframe whose easing type is missing.
    public static void AddTo(JsonObject shapeJson)
    {
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = MediaTime.Zero, Value = 32 });
        var shape = new RectShape();
        shape.Width.SetKeyFrameAnimation(animation);
        JsonObject animationJson = CoreSerializer.SerializeToJsonObject(shape)["Animations"]![nameof(RectShape.Width)]!
            .DeepClone()
            .AsObject();
        animationJson[nameof(IKeyFrameAnimation.KeyFrames)]![0]![nameof(KeyFrame.Easing)] = MissingEasingType;
        shapeJson["Animations"]![nameof(RectShape.Width)] = animationJson;
    }
}
