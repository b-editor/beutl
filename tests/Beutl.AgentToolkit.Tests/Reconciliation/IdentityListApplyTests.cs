using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Schema;
using Beutl.AgentToolkit.Tests.Helpers;
using Beutl.AgentToolkit.Tools;
using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.Tests.Reconciliation;

// The applier looks for an item from its index in the document onward. Whatever the edit, the items end
// up in document order, and the ones that stay keep their instances.
public sealed class IdentityListApplyTests
{
    [TestCase("EABCD", TestName = "Moving the last element to the front")]
    [TestCase("BCDEA", TestName = "Moving the first element to the back")]
    [TestCase("EDCBA", TestName = "Reversing the elements")]
    [TestCase("ACE", TestName = "Deleting elements in between")]
    [TestCase("BCDE", TestName = "Deleting the first element")]
    [TestCase("XABCDE", TestName = "Inserting an element at the front")]
    [TestCase("ABXCDE", TestName = "Inserting an element in between")]
    [TestCase("ABCDEX", TestName = "Inserting an element at the back")]
    [TestCase("DXAY", TestName = "Moving, deleting and inserting at once")]
    public void Elements_end_up_in_document_order(string order)
    {
        Scene scene = TestWorkspace.CreateScene();
        Dictionary<string, Element> elements = "ABCDE".ToDictionary(name => name.ToString(), name => AddElement(scene, name.ToString()));
        using var session = new AgentToolkitTestSession(scene);
        JsonObject desired = session.Documents.Read(scene);
        Dictionary<string, JsonNode> items = desired["Elements"]!.AsArray()
            .ToDictionary(item => (string)item![nameof(CoreObject.Name)]!, item => item!.DeepClone());
        desired["Elements"] = new JsonArray([.. order.Select(name => items.TryGetValue(name.ToString(), out JsonNode? item) ? item : NewElement(name.ToString()))]);

        ToolResult<ApplyEditResponse> result = session.CreateEditTools().ApplyEdit(desired: desired, schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
            Assert.That(string.Concat(scene.Children.Select(element => element.Name.Value)), Is.EqualTo(order));
            foreach (Element element in scene.Children.Where(element => elements.ContainsKey(element.Name.Value)))
            {
                Assert.That(element, Is.SameAs(elements[element.Name.Value]));
            }
        });
    }

    [Test]
    public void Each_entry_with_a_repeated_id_edits_the_first_copy()
    {
        Scene scene = TestWorkspace.CreateScene();
        Element first = AddElement(scene, "First");
        Element second = AddElement(scene, "Second");
        second.Id.Value = first.Id.Value;
        using var session = new AgentToolkitTestSession(scene);
        JsonObject desired = session.Documents.Read(scene);
        JsonArray items = desired["Elements"]!.AsArray();
        items[0]![nameof(CoreObject.Name)] = "Edited by the first entry";
        items[1]![nameof(CoreObject.Name)] = "Edited by the second entry";

        ToolResult<ApplyEditResponse> result = session.CreateEditTools().ApplyEdit(desired: desired, schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
            Assert.That(scene.Children, Is.EqualTo(new[] { second, first }));
            Assert.That(first.Name.Value, Is.EqualTo("Edited by the second entry"));
            Assert.That(second.Name.Value, Is.EqualTo("Second"));
        });
    }

    // A corrupted project can repeat an Id. Each entry with it edits the first copy and moves it to the entry's index,
    // which pushes an element still to be placed in front of its own index.
    [Test]
    public void An_element_after_a_repeated_id_is_still_found()
    {
        Scene scene = TestWorkspace.CreateScene();
        Element first = AddElement(scene, "First");
        Element other = AddElement(scene, "Other");
        Element second = AddElement(scene, "Second");
        second.Id.Value = first.Id.Value;
        using var session = new AgentToolkitTestSession(scene);
        JsonObject desired = session.Documents.Read(scene);
        JsonArray items = desired["Elements"]!.AsArray();
        desired["Elements"] = new JsonArray(items[0]!.DeepClone(), items[2]!.DeepClone(), items[1]!.DeepClone());

        ToolResult<ApplyEditResponse> result = session.CreateEditTools().ApplyEdit(desired: desired, schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
            Assert.That(scene.Children, Has.Count.EqualTo(3));
            Assert.That(scene.Children, Does.Contain(other));
        });
    }

    // Groups are read after the elements, so a member deleted by the same document counts as gone.
    [Test]
    public void Groups_keep_only_the_members_that_are_elements_after_the_edit()
    {
        Scene scene = TestWorkspace.CreateScene();
        Element[] elements = [.. "ABCD".Select(name => AddElement(scene, name.ToString()))];
        using var session = new AgentToolkitTestSession(scene);
        JsonObject desired = session.Documents.Read(scene);
        desired["Elements"]!.AsArray().RemoveAt(2);
        desired["Groups"] = new JsonArray(
            $"{elements[0].Id.Value}:{elements[1].Id.Value}:{Guid.NewGuid()}",
            $"{elements[2].Id.Value}:{elements[3].Id.Value}");

        ToolResult<ApplyEditResponse> result = session.CreateEditTools().ApplyEdit(desired: desired, schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
            Assert.That(scene.Groups.Single(), Is.EquivalentTo(new[] { elements[0].Id.Value, elements[1].Id.Value }));
        });
    }

    private static JsonObject NewElement(string name) => new()
    {
        ["$type"] = "[Beutl.ProjectSystem]:Element",
        [nameof(CoreObject.Name)] = name,
        [nameof(Element.Start)] = TimeSpan.FromSeconds(100 + name[0]).ToString("c"),
        [nameof(Element.Length)] = TimeSpan.FromSeconds(2).ToString("c"),
        [nameof(Element.ZIndex)] = 9,
    };

    private static Element AddElement(Scene scene, string name)
    {
        var element = new Element
        {
            Name = { Value = name },
            Start = { Value = MediaTime.FromSeconds(scene.Children.Count * 3) },
            Length = { Value = MediaTime.FromSeconds(2) },
            Uri = new Uri(Path.Combine(Path.GetDirectoryName(scene.Uri!.LocalPath)!, $"{Guid.NewGuid():N}.belm")),
        };
        scene.Children.Add(element);
        return element;
    }
}
