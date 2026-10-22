using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Tests.Helpers;
using Beutl.AgentToolkit.Tools;
using Beutl.Graphics;
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

    // On a port whose value the current document writes, on a new object, and on the port of a new node.
    [TestCase("port")]
    [TestCase("new object")]
    [TestCase("port of a new node")]
    public void Apply_edit_still_rejects_a_misspelled_member(string place)
    {
        (Scene scene, NodeGraphDrawable drawable) = CreateScene(typeof(TransformNode));
        using var session = new AgentToolkitTestSession(scene);
        JsonObject document = session.Documents.Read(scene);
        JsonObject target = FindObject(document, drawable.Id).AsObject();
        if (place == "new object")
        {
            target = new JsonObject { ["$type"] = IdentityHelper.WriteDiscriminator(typeof(RectShape)) };
            document["Elements"]![0]![nameof(Element.Objects)]!.AsArray().Add(target);
        }
        else if (place == "port of a new node")
        {
            target = FindObjectWith(AddCopyOfTheNode(document, drawable), "Setter")!;
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

    // The copy's ports have new Ids, so they take their members from the ports that a new node creates.
    [Test]
    public void Duplicate_object_copies_a_node_graph_whose_port_has_a_value()
    {
        (Scene scene, NodeGraphDrawable drawable) = CreateScene(typeof(TransformNode));
        Matrix matrix = SetMatrix(drawable);
        using var session = new AgentToolkitTestSession(scene);

        ToolResult<DuplicateObjectResponse> result = session.CreateEditTools().DuplicateObject(drawable.Id.ToString());

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        var copy = (NodeGraphDrawable)scene.Children.Single().Objects.Single(item => item.Id.ToString() == result.Value!.ObjectId);
        Assert.That(MatrixOf(copy), Is.EqualTo(matrix));
    }

    [Test]
    public void Apply_edit_adds_a_node_whose_port_has_a_value()
    {
        (Scene scene, NodeGraphDrawable drawable) = CreateScene(typeof(TransformNode));
        Matrix matrix = SetMatrix(drawable);
        using var session = new AgentToolkitTestSession(scene);
        JsonObject document = session.Documents.Read(scene);
        AddCopyOfTheNode(document, drawable);

        ToolResult<ApplyEditResponse> result = session.CreateEditTools()
            .ApplyEdit(desired: document, schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
            Assert.That(drawable.Model.Value!.Nodes.OfType<TransformNode>().Select(MatrixOf), Is.EqualTo(new[] { matrix, matrix }));
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

    // Gives the transform node's port a value that is not its default.
    private static Matrix SetMatrix(NodeGraphDrawable drawable)
    {
        Matrix matrix = Matrix.CreateTranslation(10, 20);
        ((DefaultInputPort<Matrix>)((TransformNode)drawable.Model.Value!.Nodes.Single()).Matrix).GetProperty()!.SetValue(matrix);
        return matrix;
    }

    private static Matrix MatrixOf(NodeGraphDrawable drawable) => MatrixOf((TransformNode)drawable.Model.Value!.Nodes.Single());

    private static Matrix MatrixOf(TransformNode node) => ((DefaultInputPort<Matrix>)node.Matrix).GetProperty()!.GetValue();

    // Adds a copy of the graph's node to the document, as a new node without Ids.
    private static JsonObject AddCopyOfTheNode(JsonObject document, NodeGraphDrawable drawable)
    {
        JsonArray nodes = FindObject(document, drawable.Model.Value!.Id)[nameof(GraphModel.Nodes)]!.AsArray();
        JsonNode copy = nodes[0]!.DeepClone();
        RemoveIds(copy);
        nodes.Add(copy);
        return copy.AsObject();

        static void RemoveIds(JsonNode? node)
        {
            if (node is JsonObject obj)
            {
                obj.Remove(nameof(CoreObject.Id));
                foreach (KeyValuePair<string, JsonNode?> pair in obj)
                    RemoveIds(pair.Value);
            }
            else if (node is JsonArray array)
            {
                foreach (JsonNode? item in array)
                    RemoveIds(item);
            }
        }
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
