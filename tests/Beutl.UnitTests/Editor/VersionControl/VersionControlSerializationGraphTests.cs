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

    [TestCase(false)]
    [TestCase(true)]
    public void Converted_dictionary_values_with_standard_backing_can_be_inspected(bool empty)
    {
        Assert.DoesNotThrow(() => Discover(new DictionaryValuesValue(customBacking: false, empty)));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Converted_dictionary_values_with_custom_backing_storage_are_rejected(bool empty)
    {
        InvalidDataException? exception = Assert.Throws<InvalidDataException>(() =>
            Discover(new DictionaryValuesValue(customBacking: true, empty)));

        Assert.That(exception!.Message,
            Does.Contain("opaque dictionary contract").And.Contain(nameof(CustomCommentDictionary)));
    }

    [Test]
    public void Resources_inside_converted_dictionaries_are_discovered()
    {
        var source = new ImageSource();
        var uri = new Uri(Path.Combine(Path.GetTempPath(), "beutl-converted-dictionary.png"));
        source.ReadFrom(uri);
        var dictionary = new Dictionary<string, ImageSource> { ["image"] = source };

        VersionControlSerializationGraph.SerializationGraph graph =
            Discover(new ConvertedValue<Dictionary<string, ImageSource>>([dictionary]));

        Assert.That(graph.UnaddressableFileSources, Does.Contain(uri));
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

    [JsonConverter(typeof(DictionaryValuesValueConverter))]
    public sealed class DictionaryValuesValue
    {
        public DictionaryValuesValue(bool customBacking, bool empty)
        {
            CustomBacking = customBacking;
            Empty = empty;
            Dictionary<string, Comment> dictionary = customBacking
                ? new CustomCommentDictionary()
                : new Dictionary<string, Comment>();
            if (!empty)
            {
                dictionary.Add("comment", new Comment(TimeSpan.FromSeconds(1), "Hello"));
            }

            Values = dictionary.Values;
        }

        public bool CustomBacking { get; }

        public bool Empty { get; }

        public Dictionary<string, Comment>.ValueCollection Values { get; }
    }

    public sealed class DictionaryValuesValueConverter : JsonConverter<DictionaryValuesValue>
    {
        public override DictionaryValuesValue Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            using JsonDocument document = JsonDocument.ParseValue(ref reader);
            return new DictionaryValuesValue(
                document.RootElement[0].GetBoolean(),
                document.RootElement[1].GetBoolean());
        }

        public override void Write(Utf8JsonWriter writer, DictionaryValuesValue value, JsonSerializerOptions options)
        {
            // Read rebuilds the backing dictionary, including its extra resource. The JSON exposes
            // no file URI, so only storage inspection can detect the unsupported backing object.
            writer.WriteStartArray();
            writer.WriteBooleanValue(value.CustomBacking);
            writer.WriteBooleanValue(value.Empty);
            writer.WriteEndArray();
        }
    }

    public sealed class CustomCommentDictionary : Dictionary<string, Comment>
    {
        public ImageSource Source { get; } = CreateSource();

        private static ImageSource CreateSource()
        {
            var source = new ImageSource();
            source.ReadFrom(new Uri(Path.Combine(Path.GetTempPath(), "beutl-hidden-dictionary.png")));
            return source;
        }
    }
}
