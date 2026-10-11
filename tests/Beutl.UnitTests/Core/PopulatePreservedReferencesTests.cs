using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Beutl.Serialization;

namespace Beutl.UnitTests.Core;

[TestFixture]
public class PopulatePreservedReferencesTests
{
    public sealed class Node : CoreObject
    {
        public static readonly CoreProperty<Node?> ChildProperty =
            ConfigureProperty<Node?, Node>(nameof(Child)).Register();

        public Node? Child
        {
            get => GetValue(ChildProperty);
            set => SetValue(ChildProperty, value);
        }
    }

    public sealed record NodeContainer(Node Node);

    public sealed class NodeContainerConverter : JsonConverter<NodeContainer>
    {
        public override NodeContainer Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            JsonObject json = JsonNode.Parse(ref reader)!.AsObject();
            return new NodeContainer((Node)CoreSerializer.DeserializeFromJsonObject(json["Node"]!.AsObject(), typeof(Node)));
        }

        public override void Write(Utf8JsonWriter writer, NodeContainer value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("Node");
            CoreSerializer.SerializeToJsonObject(value.Node).WriteTo(writer);
            writer.WriteEndObject();
        }
    }

    // No hierarchy is available as a fallback: references must resolve from the population context.
    private sealed class Owner : CoreObject
    {
        public static readonly CoreProperty<Reference<Node>> TargetProperty =
            ConfigureProperty<Reference<Node>, Owner>(nameof(Target)).Register();

        public static readonly CoreProperty<Node?> RegisteredProperty =
            ConfigureProperty<Node?, Owner>(nameof(Registered)).Register();

        public static readonly CoreProperty<List<Node>> CollectionProperty =
            ConfigureProperty<List<Node>, Owner>(nameof(Collection)).Register();

        public static readonly CoreProperty<NodeContainer> ConvertedProperty =
            ConfigureProperty<NodeContainer, Owner>(nameof(Converted)).Register();

        public Owner()
        {
            Registered = CreateNode();
            Collection = [CreateNode()];
            Converted = new NodeContainer(CreateNode());
        }

        public Reference<Node> Target
        {
            get => GetValue(TargetProperty);
            set => SetValue(TargetProperty, value);
        }

        public Node? Registered
        {
            get => GetValue(RegisteredProperty);
            set => SetValue(RegisteredProperty, value);
        }

        public List<Node> Collection
        {
            get => GetValue(CollectionProperty)!;
            set => SetValue(CollectionProperty, value);
        }

        [JsonConverter(typeof(NodeContainerConverter))]
        public NodeContainer Converted
        {
            get => GetValue(ConvertedProperty)!;
            set => SetValue(ConvertedProperty, value);
        }

        public Node? Custom { get; set; } = CreateNode();

        public List<Node> Rebuilt { get; } = [CreateNode()];

        public override void Serialize(ICoreSerializationContext context)
        {
            base.Serialize(context);
            context.SetValue(nameof(Custom), Custom);
            context.SetValue(nameof(Rebuilt), Rebuilt);
        }

        public override void Deserialize(ICoreSerializationContext context)
        {
            base.Deserialize(context);
            Custom = context.GetValue<Node>(nameof(Custom));
            List<Node> rebuilt = context.GetValue<List<Node>>(nameof(Rebuilt))!;
            Rebuilt.Clear();
            Rebuilt.AddRange(rebuilt);
        }

        public Node GetNode(string member) => member switch
        {
            nameof(Registered) => Registered!,
            nameof(Collection) => Collection[0],
            nameof(Converted) => Converted.Node,
            nameof(Custom) => Custom!,
            nameof(Rebuilt) => Rebuilt[0],
            _ => throw new ArgumentOutOfRangeException(nameof(member))
        };

        private static Node CreateNode() => new() { Name = "parent", Child = new Node { Name = "child" } };
    }

    [TestCase(nameof(Owner.Custom), false)]
    [TestCase(nameof(Owner.Custom), true)]
    [TestCase(nameof(Owner.Registered), false)]
    [TestCase(nameof(Owner.Registered), true)]
    [TestCase(nameof(Owner.Collection), false)]
    [TestCase(nameof(Owner.Collection), true)]
    [TestCase(nameof(Owner.Converted), false)]
    [TestCase(nameof(Owner.Converted), true)]
    public void Populate_resolves_changed_references_into_preserved_graphs(string member, bool nested)
    {
        var owner = new Owner();
        Node original = owner.GetNode(member);
        Node target = nested ? original.Child! : original;
        JsonObject document = CoreSerializer.SerializeToJsonObject(owner);
        document[nameof(Owner.Target)] = target.Id;

        CoreSerializer.PopulateFromJsonObject(owner, document, new CoreSerializerOptions
        {
            PreserveUnchangedValues = true
        });

        Assert.Multiple(() =>
        {
            Assert.That(owner.GetNode(member), Is.SameAs(original));
            Assert.That(owner.Target.Value, Is.SameAs(target));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Populate_resolves_rebuilt_collections_to_the_new_objects(bool preserve)
    {
        var owner = new Owner();
        Node original = owner.Rebuilt[0].Child!;
        JsonObject document = CoreSerializer.SerializeToJsonObject(owner);
        document[nameof(Owner.Target)] = original.Id;

        CoreSerializer.PopulateFromJsonObject(owner, document, new CoreSerializerOptions
        {
            PreserveUnchangedValues = preserve
        });

        Assert.Multiple(() =>
        {
            Assert.That(owner.Rebuilt[0].Child, Is.Not.SameAs(original));
            Assert.That(owner.Target.Value, Is.SameAs(owner.Rebuilt[0].Child));
        });
    }

    [TestCase("change")]
    [TestCase("remove")]
    [TestCase("replace")]
    public void Populate_does_not_register_discarded_objects(string edit)
    {
        var owner = new Owner();
        Node original = owner.Custom!;
        JsonObject document = CoreSerializer.SerializeToJsonObject(owner);
        document[nameof(Owner.Target)] = original.Id;
        switch (edit)
        {
            case "change":
                document[nameof(Owner.Custom)]![nameof(CoreObject.Name)] = "changed";
                break;
            case "remove":
                document.Remove(nameof(Owner.Custom));
                break;
            case "replace":
                document[nameof(Owner.Custom)] = CoreSerializer.SerializeToJsonObject(new Node());
                break;
        }

        CoreSerializer.PopulateFromJsonObject(owner, document, new CoreSerializerOptions
        {
            PreserveUnchangedValues = true
        });

        Assert.Multiple(() =>
        {
            Assert.That(owner.Custom, Is.Not.SameAs(original));
            Assert.That(owner.Target.Value, Is.SameAs(edit == "change" ? owner.Custom : null));
            Assert.That(original.Name, Is.EqualTo("parent"));
        });
    }
}
