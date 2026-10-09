using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Beutl.Editor;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.UnitTests.Editor.VersionControl;

[TestFixture]
public class VersionControlSerializationGraphTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void Converted_value_arrays_and_read_only_views_can_be_inspected(bool empty)
    {
        Comment[] comments = empty ? [] : [new(TimeSpan.FromSeconds(1), "Hello")];

        Assert.DoesNotThrow(() => Discover(new ConvertedValue<Comment>(comments)));
    }

    [Test]
    public void Resources_inside_converted_value_collections_are_discovered()
    {
        var source = new ImageSource();
        var uri = new Uri(Path.Combine(Path.GetTempPath(), "beutl-converted-collection.png"));
        source.ReadFrom(uri);
        ImageSource[] sources = [source];

        VersionControlSerializationGraph.SerializationGraph graph = Discover(new ConvertedValue<ImageSource>(sources));

        Assert.That(graph.UnaddressableFileSources, Does.Contain(uri));
    }

    [Test]
    public void Read_only_collections_with_custom_backing_storage_are_still_rejected()
    {
        InvalidDataException? exception = Assert.Throws<InvalidDataException>(() =>
            Discover(new CustomBackingValue()));

        Assert.That(exception!.Message,
            Does.Contain("opaque collection contract").And.Contain(nameof(CustomCommentStorage)));
    }

    private static VersionControlSerializationGraph.SerializationGraph Discover<T>(T value)
        => VersionControlSerializationGraph.DiscoverSerializationGraph(new ValueProjectItem<T>
        {
            Value = value,
        });

    public sealed class ValueProjectItem<T> : ProjectItem
    {
        public T? Value { get; set; }

        public override void Serialize(ICoreSerializationContext context)
        {
            base.Serialize(context);
            context.SetValue(nameof(Value), Value);
        }
    }

    public sealed record Comment(TimeSpan Time, string Text)
    {
        public string[] Lines => Text.Split('\n');
    }

    [JsonConverter(typeof(ConvertedValueConverterFactory))]
    public sealed class ConvertedValue<T>
    {
        private readonly T[] _items;

        public ConvertedValue(IEnumerable<T> items)
        {
            _items = items.ToArray();
            Items = Array.AsReadOnly(_items);
        }

        public IReadOnlyList<T> Items { get; }
    }

    public sealed class ConvertedValueConverterFactory : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert)
            => typeToConvert.IsGenericType
               && typeToConvert.GetGenericTypeDefinition() == typeof(ConvertedValue<>);

        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
            => (JsonConverter)Activator.CreateInstance(
                typeof(ConvertedValueConverter<>).MakeGenericType(typeToConvert.GetGenericArguments()))!;
    }

    public sealed class ConvertedValueConverter<T> : JsonConverter<ConvertedValue<T>>
    {
        public override ConvertedValue<T> Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
            => new(JsonSerializer.Deserialize<T[]>(ref reader, options)!);

        public override void Write(Utf8JsonWriter writer, ConvertedValue<T> value, JsonSerializerOptions options)
            => JsonSerializer.Serialize(writer, value.Items, options);
    }

    [JsonConverter(typeof(CustomBackingValueConverter))]
    public sealed class CustomBackingValue
    {
        public ReadOnlyCollection<Comment> Comments { get; } = new(new CustomCommentStorage
        {
            new(TimeSpan.FromSeconds(1), "Hello"),
        });
    }

    public sealed class CustomBackingValueConverter : JsonConverter<CustomBackingValue>
    {
        public override CustomBackingValue Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            _ = reader.GetString();
            return new CustomBackingValue();
        }

        public override void Write(Utf8JsonWriter writer, CustomBackingValue value, JsonSerializerOptions options)
            => writer.WriteStringValue("Comments");
    }

    public sealed class CustomCommentStorage : List<Comment>
    {
        // A custom collection can carry resources that its enumerator does not expose.
        public ImageSource Source { get; } = CreateSource();

        private static ImageSource CreateSource()
        {
            var source = new ImageSource();
            source.ReadFrom(new Uri(Path.Combine(Path.GetTempPath(), "beutl-hidden-collection.png")));
            return source;
        }
    }
}
