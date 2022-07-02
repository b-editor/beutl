using System.Text.Json.Nodes;

using BeUtl.Media.Immutable;

namespace BeUtl.Media;

/// <summary>
/// A collection of <see cref="GradientStop"/>s.
/// </summary>
public sealed class GradientStops : AffectsRenders<GradientStop>, IJsonSerializable
{
    public IReadOnlyList<ImmutableGradientStop> ToImmutable()
    {
        return this.Select(x => new ImmutableGradientStop(x.Offset, x.Color)).ToArray();
    }

    public void ReadFromJson(JsonNode json)
    {
        if (json is JsonArray childrenArray)
        {
            Clear();
            if (Capacity < childrenArray.Count)
            {
                Capacity = childrenArray.Count;
            }

            foreach (JsonObject childJson in childrenArray.OfType<JsonObject>())
            {
                var item = new GradientStop();
                item.ReadFromJson(childJson);
                Add(item);
            }
        }
    }

    public void WriteToJson(ref JsonNode json)
    {
        var array = new JsonArray();

        foreach (GradientStop item in AsSpan())
        {
            JsonNode node = new JsonObject();
            item.WriteToJson(ref node);

            array.Add(node);
        }

        json = array;
    }
}
