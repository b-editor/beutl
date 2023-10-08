using System.Text.Json.Nodes;

using Beutl.Graphics.Effects;

namespace Beutl.Serialization.Migration;

// https://github.com/b-editor/beutl/issues/680
internal static class Migration_ChangeSigmaType
{
    public static void Update(JsonNode animationJson)
    {
        if (animationJson != null)
        {
            animationJson["$type"] = "[Beutl.Engine]Beutl.Animation:KeyFrameAnimation<[Beutl.Engine]Beutl.Graphics:Size>";

            if (animationJson["KeyFrames"] is JsonArray keyFrames)
            {
                foreach (JsonNode? kf in keyFrames)
                {
                    if (kf != null)
                    {
                        kf["$type"] = "[Beutl.Engine]Beutl.Animation:KeyFrame<[Beutl.Engine]Beutl.Graphics:Size>";
                    }
                }
            }
        }
    }

    public static bool ShouldMigrate(CoreProperty property)
    {
        return property == Blur.SigmaProperty
            || property == DropShadow.SigmaProperty
            || property == InnerShadow.SigmaProperty
            || property == Graphics.Effects.OpenCv.GaussianBlur.SigmaProperty;
    }
}
