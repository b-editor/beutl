using System.Collections;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Beutl.Animation;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Serialization;

namespace Beutl.UnitTests.Core;

// A collection value whose type names a JSON converter must be written and read by that converter, not item
// by item (b-editor/beutl#2728).
public class CollectionJsonConverterSerializationTests
{
    private static readonly Point[] s_points = [new Point(1, 2), new Point(3, 4)];

    // Item by item, the points would be written as ["1,2","3,4"].
    private const string ConverterJson = "[1,2,3,4]";

    [Test]
    public void EngineProperty_WritesTheValueWithItsConverterAndReadsItBack()
    {
        var holder = new PointListHolder();
        holder.Points.CurrentValue = new PointList(s_points);

        JsonObject json = CoreSerializer.SerializeToJsonObject(holder);
        var restored = (PointListHolder)CoreSerializer.DeserializeFromJsonObject(json, typeof(PointListHolder));

        Assert.That(json[nameof(PointListHolder.Points)]!.ToJsonString(), Is.EqualTo(ConverterJson));
        Assert.That(restored.Points.CurrentValue, Is.EqualTo(s_points));
    }

    // A core property, such as a key frame's value, is read as its own type rather than as an Optional<T>.
    [Test]
    public void KeyFrameValue_WritesTheValueWithItsConverterAndReadsItBack()
    {
        var keyFrame = new KeyFrame<PointList?> { Value = new PointList(s_points) };

        JsonObject json = CoreSerializer.SerializeToJsonObject(keyFrame);
        var restored = (KeyFrame<PointList?>)CoreSerializer.DeserializeFromJsonObject(json, typeof(KeyFrame<PointList?>));

        Assert.That(json[nameof(KeyFrame<PointList?>.Value)]!.ToJsonString(), Is.EqualTo(ConverterJson));
        Assert.That(restored.Value, Is.EqualTo(s_points));
    }

    // Node graph properties are written as object and read back as their property type.
    [Test]
    public void ValueWrittenAsObject_UsesTheConverterOfItsRuntimeType()
    {
        JsonNode node = CoreSerializer.SerializeToJsonNode(new PointList(s_points));
        var restored = (PointList?)CoreSerializer.DeserializeFromJsonNode(node, typeof(PointList));

        Assert.That(node.ToJsonString(), Is.EqualTo(ConverterJson));
        Assert.That(restored, Is.EqualTo(s_points));
    }

    // Has no parameterless constructor, so it cannot be rebuilt by adding the items one at a time.
    [JsonConverter(typeof(PointListJsonConverter))]
    public sealed class PointList(IEnumerable<Point> points) : IReadOnlyList<Point>
    {
        private readonly Point[] _points = [.. points];

        public int Count => _points.Length;

        public Point this[int index] => _points[index];

        public IEnumerator<Point> GetEnumerator() => ((IEnumerable<Point>)_points).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    // Writes the coordinates as one flat array: [x0, y0, x1, y1, ...].
    public sealed class PointListJsonConverter : JsonConverter<PointList>
    {
        public override PointList Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            float[] coordinates = JsonSerializer.Deserialize<float[]>(ref reader, options) ?? throw new JsonException();
            return new PointList(coordinates.Chunk(2).Select(pair => new Point(pair[0], pair[1])));
        }

        public override void Write(Utf8JsonWriter writer, PointList value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            foreach (Point point in value)
            {
                writer.WriteNumberValue(point.X);
                writer.WriteNumberValue(point.Y);
            }

            writer.WriteEndArray();
        }
    }

    [SuppressResourceClassGeneration]
    public sealed class PointListHolder : EngineObject
    {
        public PointListHolder() => ScanProperties<PointListHolder>();

        public IProperty<PointList?> Points { get; } = Property.Create<PointList?>();
    }
}
