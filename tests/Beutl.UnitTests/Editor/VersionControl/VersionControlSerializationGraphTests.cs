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

    [TestCase(false)]
    [TestCase(true)]
    public void Converted_sorted_dictionaries_can_be_inspected(bool empty)
    {
        var dictionary = new SortedDictionary<string, Comment>();
        if (!empty)
        {
            dictionary.Add("comment", new Comment(TimeSpan.FromSeconds(1), "Hello"));
        }

        Assert.DoesNotThrow(() => Discover(new ConvertedValue<SortedDictionary<string, Comment>>([dictionary])));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Converted_sorted_sets_can_be_inspected(bool empty)
    {
        SortedSet<int> values = empty ? [] : [1, 2, 3];

        Assert.DoesNotThrow(() => Discover(new ConvertedValue<SortedSet<int>>([values])));
    }

    [Test]
    public void Resources_inside_converted_sorted_dictionaries_are_discovered()
    {
        var source = new ImageSource();
        var uri = new Uri(Path.Combine(Path.GetTempPath(), "beutl-converted-sorted-dictionary.png"));
        source.ReadFrom(uri);
        var dictionary = new SortedDictionary<string, ImageSource> { ["image"] = source };

        VersionControlSerializationGraph.SerializationGraph graph =
            Discover(new ConvertedValue<SortedDictionary<string, ImageSource>>([dictionary]));

        Assert.That(graph.UnaddressableFileSources, Does.Contain(uri));
    }

    [Test]
    public void Converted_custom_sorted_dictionaries_are_still_rejected()
    {
        var dictionary = new CustomSortedCommentDictionary
        {
            ["comment"] = new Comment(TimeSpan.FromSeconds(1), "Hello"),
        };

        InvalidDataException? exception = Assert.Throws<InvalidDataException>(() =>
            Discover(new ConvertedValue<CustomSortedCommentDictionary>([dictionary])));

        Assert.That(exception!.Message,
            Does.Contain("opaque dictionary contract").And.Contain(nameof(CustomSortedCommentDictionary)));
    }

    [Test]
    public void Converted_custom_sorted_sets_are_still_rejected()
    {
        var values = new CustomIntSortedSet { 1, 2, 3 };

        InvalidDataException? exception = Assert.Throws<InvalidDataException>(() =>
            Discover(new ConvertedValue<CustomIntSortedSet>([values])));

        Assert.That(exception!.Message,
            Does.Contain("opaque collection contract").And.Contain(nameof(CustomIntSortedSet)));
    }

    [Test]
    public void Converted_read_only_dictionaries_with_custom_backing_are_rejected()
    {
        InvalidDataException? exception = Assert.Throws<InvalidDataException>(() =>
            Discover(new DictionaryStorageValue(DictionaryStorageKind.ReadOnlyCustom)));

        Assert.That(exception!.Message,
            Does.Contain("opaque dictionary contract").And.Contain(nameof(CustomCommentDictionary)));
    }

    [Test]
    public void Resources_inside_restored_sorted_dictionary_keys_are_discovered()
    {
        VersionControlSerializationGraph.SerializationGraph graph =
            Discover(new DictionaryStorageValue(DictionaryStorageKind.SortedKey));

        Assert.That(graph.UnaddressableFileSources,
            Does.Contain(ResourceUri("beutl-dictionary-key.png")));
    }

    [Test]
    public void Resources_inside_restored_sorted_dictionary_comparers_are_discovered()
    {
        VersionControlSerializationGraph.SerializationGraph graph =
            Discover(new DictionaryStorageValue(DictionaryStorageKind.RestoredComparer));

        Assert.That(graph.UnaddressableFileSources,
            Does.Contain(ResourceUri("beutl-restored-dictionary-comparer.png")));
    }

    [Test]
    public void Comparers_discarded_by_a_dictionary_converter_do_not_introduce_resources()
    {
        VersionControlSerializationGraph.SerializationGraph graph =
            Discover(new DictionaryStorageValue(DictionaryStorageKind.DiscardedComparer));

        Assert.That(graph.UnaddressableFileSources,
            Does.Not.Contain(ResourceUri("beutl-discarded-dictionary-comparer.png")));
    }

    private static Uri ResourceUri(string fileName)
        => new(Path.Combine(Path.GetTempPath(), fileName));

    private static ImageSource ResourceSource(string fileName)
    {
        var source = new ImageSource();
        source.ReadFrom(ResourceUri(fileName));
        return source;
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

    public sealed class CustomSortedCommentDictionary : SortedDictionary<string, Comment>
    {
        public ImageSource Source { get; } = CreateSource();

        private static ImageSource CreateSource()
        {
            var source = new ImageSource();
            source.ReadFrom(new Uri(Path.Combine(Path.GetTempPath(), "beutl-hidden-sorted-dictionary.png")));
            return source;
        }
    }

    public sealed class CustomIntSortedSet : SortedSet<int>
    {
        public ImageSource Source { get; } = CreateSource();

        private static ImageSource CreateSource()
        {
            var source = new ImageSource();
            source.ReadFrom(new Uri(Path.Combine(Path.GetTempPath(), "beutl-hidden-sorted-set.png")));
            return source;
        }
    }

    public enum DictionaryStorageKind
    {
        ReadOnlyCustom,
        SortedKey,
        RestoredComparer,
        DiscardedComparer,
    }

    [JsonConverter(typeof(DictionaryStorageValueConverter))]
    public sealed class DictionaryStorageValue
    {
        public DictionaryStorageValue(DictionaryStorageKind kind, bool restored = false)
        {
            Kind = kind;
            var comment = new Comment(TimeSpan.FromSeconds(1), "Hello");
            switch (kind)
            {
                case DictionaryStorageKind.ReadOnlyCustom:
                    ReadOnly = new ReadOnlyDictionary<string, Comment>(new CustomCommentDictionary
                    {
                        ["comment"] = comment,
                    });
                    break;
                case DictionaryStorageKind.SortedKey:
                    SortedKeys = new SortedDictionary<ResourceKey, Comment>
                    {
                        [new ResourceKey()] = comment,
                    };
                    break;
                case DictionaryStorageKind.RestoredComparer:
                    SortedComparer = new SortedDictionary<string, Comment>(restored
                        ? new ResourceComparer("beutl-restored-dictionary-comparer.png")
                        : StringComparer.Ordinal)
                    { ["comment"] = comment };
                    break;
                case DictionaryStorageKind.DiscardedComparer:
                    SortedComparer = new SortedDictionary<string, Comment>(restored
                        ? StringComparer.Ordinal
                        : new ResourceComparer("beutl-discarded-dictionary-comparer.png"))
                    { ["comment"] = comment };
                    break;
            }
        }

        public DictionaryStorageKind Kind { get; }

        public ReadOnlyDictionary<string, Comment>? ReadOnly { get; }

        public SortedDictionary<ResourceKey, Comment>? SortedKeys { get; }

        public SortedDictionary<string, Comment>? SortedComparer { get; }
    }

    public sealed class DictionaryStorageValueConverter : JsonConverter<DictionaryStorageValue>
    {
        public override DictionaryStorageValue Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
            => new((DictionaryStorageKind)reader.GetInt32(), restored: true);

        // The token contains no file URI; Read reconstructs the dictionary's actual storage.
        public override void Write(Utf8JsonWriter writer, DictionaryStorageValue value, JsonSerializerOptions options)
            => writer.WriteNumberValue((int)value.Kind);
    }

    public sealed class ResourceKey : IComparable<ResourceKey>
    {
        public ImageSource Source { get; } = ResourceSource("beutl-dictionary-key.png");

        public int CompareTo(ResourceKey? other) => other is null ? 1 : 0;
    }

    public sealed class ResourceComparer(string fileName) : IComparer<string>
    {
        public ImageSource Source { get; } = ResourceSource(fileName);

        public int Compare(string? x, string? y) => StringComparer.Ordinal.Compare(x, y);
    }
}
