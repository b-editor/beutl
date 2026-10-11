using System.Text.Json.Nodes;
using Beutl.Serialization;

namespace Beutl.UnitTests.Core;

[TestFixture]
public class PopulateUnchangedValuesTests
{
    public sealed class ValueObject : CoreObject;

    // These members are not registered CoreProperties: preservation must use the values actually
    // written by Serialize, including extension-defined keys, rather than reflect over properties.
    private sealed class CustomOwner : CoreObject
    {
        public CoreObject? Child { get; set; } = new ValueObject { Name = "original" };

        public override void Serialize(ICoreSerializationContext context)
        {
            base.Serialize(context);
            context.SetValue("custom-child", Child);
        }

        public override void Deserialize(ICoreSerializationContext context)
        {
            base.Deserialize(context);
            Child = context.GetValue<CoreObject>("custom-child");
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Populate_reuses_unchanged_custom_values_only_when_requested(bool preserve)
    {
        var owner = new CustomOwner();
        CoreObject original = owner.Child!;
        JsonObject document = JsonNode.Parse(CoreSerializer.SerializeToJsonString(owner))!.AsObject();

        CoreSerializer.PopulateFromJsonObject(owner, document, new CoreSerializerOptions
        {
            PreserveUnchangedValues = preserve
        });

        Assert.Multiple(() =>
        {
            Assert.That(ReferenceEquals(owner.Child, original), Is.EqualTo(preserve));
            Assert.That(owner.Child?.Name, Is.EqualTo("original"));
            Assert.That(owner.Child?.Id, Is.EqualTo(original.Id));
        });
    }

    [TestCase("change")]
    [TestCase("null")]
    [TestCase("remove")]
    public void Populate_still_applies_changes_and_clears_custom_values(string edit)
    {
        var owner = new CustomOwner();
        CoreObject original = owner.Child!;
        JsonObject document = CoreSerializer.SerializeToJsonObject(owner);
        switch (edit)
        {
            case "change":
                document["custom-child"]![nameof(CoreObject.Name)] = "changed";
                break;
            case "null":
                document["custom-child"] = null;
                break;
            case "remove":
                document.Remove("custom-child");
                break;
        }

        CoreSerializer.PopulateFromJsonObject(owner, document, new CoreSerializerOptions
        {
            PreserveUnchangedValues = true
        });

        Assert.Multiple(() =>
        {
            Assert.That(owner.Child, Is.Not.SameAs(original));
            Assert.That(owner.Child?.Name, Is.EqualTo(edit == "change" ? "changed" : null));
            Assert.That(original.Name, Is.EqualTo("original"));
        });
    }

    private sealed class CollectionOwner(bool readAsArray) : CoreObject
    {
        public List<CoreObject> Children { get; } = [new ValueObject { Name = "child" }];

        public override void Serialize(ICoreSerializationContext context)
        {
            base.Serialize(context);
            context.SetValue(nameof(Children), Children);
        }

        public override void Deserialize(ICoreSerializationContext context)
        {
            base.Deserialize(context);
            IEnumerable<CoreObject> children = readAsArray
                ? context.GetValue<CoreObject[]>(nameof(Children))!
                : context.GetValue<List<CoreObject>>(nameof(Children))!;
            Children.Clear();
            Children.AddRange(children);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Populate_preserves_collection_contents_when_the_reader_rebuilds_the_collection(bool readAsArray)
    {
        var owner = new CollectionOwner(readAsArray);
        Guid childId = owner.Children[0].Id;
        JsonObject document = CoreSerializer.SerializeToJsonObject(owner);

        CoreSerializer.PopulateFromJsonObject(owner, document, new CoreSerializerOptions
        {
            PreserveUnchangedValues = true
        });

        Assert.Multiple(() =>
        {
            Assert.That(owner.Children.Select(child => child.Name), Is.EqualTo(new[] { "child" }));
            Assert.That(owner.Children.Select(child => child.Id), Is.EqualTo(new[] { childId }));
        });
    }

    [Test]
    public void Populate_preserves_project_variables_when_the_document_is_unchanged()
    {
        var project = new Project
        {
            Variables =
            {
                [ProjectVariableKeys.FrameRate] = "30",
                [ProjectVariableKeys.SampleRate] = "48000"
            }
        };
        KeyValuePair<string, string>[] variables = project.Variables.ToArray();
        JsonObject document = CoreSerializer.SerializeToJsonObject(project);

        CoreSerializer.PopulateFromJsonObject(project, document, new CoreSerializerOptions
        {
            PreserveUnchangedValues = true
        });

        Assert.That(project.Variables, Is.EquivalentTo(variables));
    }
}
