using System.Text.Json.Nodes;
using Beutl.Animation;
using Beutl.Engine;
using Beutl.Graphics.Shapes;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.UnitTests.TestInfrastructure;

/// <summary>
/// Content that makes recovery keep an element's file, for the tests of protected elements.
/// </summary>
/// <remarks>
/// An object whose type is missing no longer does: its fallback saves the JSON it was read from, so the rest of
/// its element stays editable. A keyframe whose easing type is missing still does, since that load incident leaves
/// no fallback to save it again, and so does content the Beutl 1.x migration could not convert.
/// </remarks>
public static class RecoveryBlockers
{
    public const string MissingEasingType = "[Missing.Assembly]Missing.Namespace:MissingEasing";

    /// <summary>
    /// Creates the object that stands in for a Beutl 1.x node tree the migration could not convert. Removing or
    /// replacing it repairs a protected element.
    /// </summary>
    public static FallbackEngineObject CreateUnmigratedObject()
    {
        return new FallbackEngineObject
        {
            Json = new JsonObject { ["$type"] = ElementMigration.LegacyNodeTreeTypeName },
            Reason = FallbackReason.TypeNotFound,
        };
    }

    /// <summary>Gives the shape that <paramref name="shapeJson"/> describes a width keyframe whose easing type is missing.</summary>
    public static void AddUnreadableEasing(JsonObject shapeJson)
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

    /// <summary>Creates the JSON of a shape with a width keyframe whose easing type is missing.</summary>
    public static JsonObject CreateShapeWithUnreadableEasing()
    {
        JsonObject shapeJson = CoreSerializer.SerializeToJsonObject(new RectShape());
        AddUnreadableEasing(shapeJson);
        return shapeJson;
    }
}
