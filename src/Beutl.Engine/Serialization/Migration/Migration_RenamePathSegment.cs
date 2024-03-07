using System.Text.Json.Nodes;

namespace Beutl.Serialization.Migration;

internal static class Migration_RenamePathSegment
{
    private static string? RewriteTypeName(string? s)
    {
        return s switch
        {
            "[Beutl.Engine]Beutl.Media:ArcOperation" => "[Beutl.Engine]Beutl.Media:ArcSegment",
            "[Beutl.Engine]Beutl.Media:ConicOperation" => "[Beutl.Engine]Beutl.Media:ConicSegment",
            "[Beutl.Engine]Beutl.Media:CubicBezierOperation" => "[Beutl.Engine]Beutl.Media:CubicBezierSegment",
            "[Beutl.Engine]Beutl.Media:LineOperation" => "[Beutl.Engine]Beutl.Media:LineSegment",
            "[Beutl.Engine]Beutl.Media:QuadraticBezierOperation" => "[Beutl.Engine]Beutl.Media:QuadraticBezierSegment",
            _ => s
        };
    }

    public static void Update(JsonObject pathGeometry, JsonArray segments)
    {
        if (segments != null && pathGeometry != null)
        {
            for (int i = 0; i < segments.Count; i++)
            {
                JsonNode? item = segments[i];
                if (item is JsonObject itemObj)
                {
                    if (itemObj.TryGetPropertyValueAsJsonValue("$type", out string? oldTypeName))
                    {
                        if (oldTypeName == "[Beutl.Engine]Beutl.Media:MoveOperation")
                        {
                            try
                            {
                                pathGeometry["StartPoint"] = itemObj["Point"]?.DeepClone();

                                if (pathGeometry["Animations"] is { } anm
                                    && itemObj["Animations"]?["Point"] is { } ptAnm)
                                {
                                    ptAnm["Property"] = new JsonObject
                                    {
                                        ["Name"] = "StartPoint",
                                        ["Owner"] = "[Beutl.Engine]Beutl.Media:PathGeometry",
                                    };

                                    anm["StartPoint"] = ptAnm.DeepClone();
                                }

                                segments.RemoveAt(i);
                                i--;
                                continue;
                            }
                            catch
                            {
                            }
                        }
                        else if (oldTypeName == "[Beutl.Engine]Beutl.Media:CloseOperation")
                        {
                            try
                            {
                                pathGeometry["IsClosed"] = JsonValue.Create(true);
                                segments.RemoveAt(i);
                                i--;
                                continue;
                            }
                            catch
                            {
                            }
                        }

                        var newTypeName = RewriteTypeName(oldTypeName);
                        itemObj["$type"] = newTypeName;

                        if (itemObj["Animations"] is JsonObject anms)
                        {
                            foreach (KeyValuePair<string, JsonNode?> anm in anms)
                            {
                                try
                                {
                                    anm.Value!["Property"]!["Owner"] = newTypeName;
                                }
                                catch
                                {
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
