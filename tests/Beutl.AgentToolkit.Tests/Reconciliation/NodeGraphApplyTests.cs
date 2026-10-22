using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Tests.Helpers;
using Beutl.AgentToolkit.Tools;
using Beutl.Graphics.Shapes;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes;
using Beutl.NodeGraph.Nodes.Generative;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.AgentToolkit.Tests.Reconciliation;

// A node graph stays editable for an agent while its ports hold values of their own or a property of their node (#3450).
public sealed class NodeGraphApplyTests
{
    [TestCase(typeof(TransformNode))]
    [TestCase(typeof(TextNode))]
    [TestCase(typeof(AiImageGenerationNode))]
    public void Apply_edit_of_the_unchanged_document_succeeds(Type nodeType)
    {
        (Scene scene, _) = CreateScene(nodeType);
        using var session = new AgentToolkitTestSession(scene);

        ToolResult<ApplyEditResponse> result = session.CreateEditTools()
            .ApplyEdit(desired: session.Documents.Read(scene), schemaVersion: SchemaVersion.Current);

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
    }

    [TestCase(typeof(TransformNode))]
    [TestCase(typeof(TextNode))]
    [TestCase(typeof(AiImageGenerationNode))]
    public void Apply_edit_next_to_the_node_applies(Type nodeType)
    {
        (Scene scene, NodeGraphDrawable drawable) = CreateScene(nodeType);
        GraphNode node = drawable.Model.Value!.Nodes.Single();
        using var session = new AgentToolkitTestSession(scene);
        JsonObject document = session.Documents.Read(scene);
        FindObject(document, drawable.Id)[nameof(CoreObject.Name)] = "Renamed";

        ToolResult<ApplyEditResponse> result = session.CreateEditTools()
            .ApplyEdit(desired: document, schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
            Assert.That(drawable.Name, Is.EqualTo("Renamed"));
            Assert.That(drawable.Model.Value!.Nodes.Single(), Is.SameAs(node));
        });
    }

    // On a port whose value the current document writes, and on a new object.
    [TestCase(false)]
    [TestCase(true)]
    public void Apply_edit_still_rejects_a_misspelled_member(bool onNewObject)
    {
        (Scene scene, NodeGraphDrawable drawable) = CreateScene(typeof(TransformNode));
        using var session = new AgentToolkitTestSession(scene);
        JsonObject document = session.Documents.Read(scene);
        JsonObject target = FindObject(document, drawable.Id).AsObject();
        if (onNewObject)
        {
            target = new JsonObject { ["$type"] = IdentityHelper.WriteDiscriminator(typeof(RectShape)) };
            document["Elements"]![0]![nameof(Element.Objects)]!.AsArray().Add(target);
        }
        else
        {
            target = FindObjectWith(target, "Setter")!;
        }

        target["Settter"] = 1;

        ToolResult<ApplyEditResponse> result = session.CreateEditTools()
            .ApplyEdit(desired: document, schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error?.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(result.Error?.Message, Does.Contain("'Settter'"));
        });
    }

    // A scene whose element holds a node graph with one node of nodeType.
    private static (Scene Scene, NodeGraphDrawable Drawable) CreateScene(Type nodeType)
    {
        Scene scene = TestWorkspace.CreateScene();
        Element element = TestWorkspace.AddElement(scene, default, scene.Duration);
        var drawable = new NodeGraphDrawable();
        drawable.Model.Value!.Nodes.Add((GraphNode)Activator.CreateInstance(nodeType)!);
        element.AddObject(drawable);
        return (scene, drawable);
    }

    private static JsonObject FindObject(JsonObject document, Guid id)
        => FindObjectWith(document, nameof(CoreObject.Id), id.ToString())
           ?? throw new InvalidOperationException($"The document has no object {id}.");

    // The first object in node, depth first, that has the member name, with the given value if there is one.
    private static JsonObject? FindObjectWith(JsonNode? node, string name, string? value = null)
    {
        switch (node)
        {
            case JsonObject obj:
                if (obj.TryGetPropertyValue(name, out JsonNode? member)
                    && (value is null || member is JsonValue text && text.TryGetValue(out string? s) && s == value))
                {
                    return obj;
                }

                return obj.Select(pair => FindObjectWith(pair.Value, name, value)).FirstOrDefault(found => found is not null);
            case JsonArray array:
                return array.Select(item => FindObjectWith(item, name, value)).FirstOrDefault(found => found is not null);
            default:
                return null;
        }
    }
}
