using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.NodeGraphs;
using Beutl.AgentToolkit.Tests.Helpers;
using Beutl.AgentToolkit.Tools;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.NodeGraph;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.AgentToolkit.Tests.NodeGraphs;

// A scene of one element holding a white rectangle whose effect is an empty node graph, attached to a hierarchy root
// as file sessions and the editor are: a group mirrors its inner ports only once attached.
internal sealed class NodeGraphTestScene : IDisposable
{
    public NodeGraphTestScene()
    {
        NodeTypeCatalog.EnsureBuiltInNodesRegistered();
        string directory = TestWorkspace.CreateDirectory();
        Scene = new Scene(64, 48, "scene")
        {
            Uri = new Uri(Path.Combine(directory, "scene.scene")),
            Duration = { Value = MediaTime.FromSeconds(2) }
        };
        var project = new Project();
        project.Items.Add(Scene);
        _ = new BeutlApplication { Projects = { project } };

        Effect = new NodeGraphFilterEffect();
        Shape = new RectShape
        {
            Width = { Value = 24 },
            Height = { Value = 16 },
            Fill = { Value = new SolidColorBrush(Colors.White) },
            FilterEffect = { Value = Effect }
        };
        Element = new Element
        {
            Start = { Value = MediaTime.Zero },
            Length = { Value = MediaTime.FromSeconds(2) },
            Uri = new Uri(Path.Combine(directory, "element.belm"))
        };
        Element.AddObject(Shape);
        Scene.Children.Add(Element);
        Session = new AgentToolkitTestSession(Scene);
        Tools = new NodeGraphTools(Session.CreateManager());
    }

    public Scene Scene { get; }

    public Element Element { get; }

    public RectShape Shape { get; }

    public NodeGraphFilterEffect Effect { get; }

    public AgentToolkitTestSession Session { get; }

    public NodeGraphTools Tools { get; }

    public string GraphId => Effect.Id.Value.ToString();

    public GraphModel Graph => Effect.Model.Value!;

    public ToolResult<EditNodeGraphResponse> Edit(params NodeGraphOperation[] operations)
        => Tools.EditNodeGraph(GraphId, operations);

    public ToolResult<EditNodeGraphResponse> Validate(params NodeGraphOperation[] operations)
        => Tools.EditNodeGraph(GraphId, operations, validateOnly: true);

    // The graph as saved, with the monitors' text, which a save leaves out.
    public string Capture()
    {
        string monitors = string.Join("|", Graph.EnumerateGraphs()
            .SelectMany(graph => graph.Nodes)
            .Select(node => $"{node.Id.Value}:{NodeGraphMembers.ReadError(node)}"));
        return CoreSerializer.SerializeToJsonObject(Graph).ToJsonString() + monitors;
    }

    public GraphNode Node(string id) => Graph.EnumerateGraphs().SelectMany(graph => graph.Nodes).Single(node => node.Id.Value.ToString() == id);

    public void Dispose() => Session.Dispose();

    public static NodeGraphOperation Add(string reference, string type, JsonObject? values = null, string? graph = null)
        => new() { Op = "add", Ref = reference, Type = type, Values = values, Graph = graph };

    public static NodeGraphOperation Connect(string from, string to, string? graph = null, bool replace = false)
        => new() { Op = "connect", From = from, To = to, Graph = graph, Replace = replace };

    // An effect input, a pass running code with values, and an output, wired in a line.
    public static NodeGraphOperation[] OnePass(string code, JsonObject? values = null)
    {
        var passValues = new JsonObject { ["Code"] = code };
        foreach (KeyValuePair<string, JsonNode?> pair in values ?? new JsonObject())
        {
            passValues[pair.Key] = pair.Value?.DeepClone();
        }

        return
        [
            Add("input", "FilterEffectInputNode"),
            Add("pass", "ShaderFullscreenPassNode", passValues),
            Add("output", "OutputNode"),
            Connect("input.Output", "pass.In.source"),
            Connect("pass.Out.color", "output.Output")
        ];
    }

    public const string TintCode = """
        #version 450
        layout(location = 0) in vec2 uv;
        layout(location = 0) out vec4 color;
        layout(set = 0, binding = 0) uniform sampler2D source;
        layout(set = 0, binding = 1) uniform Params { vec4 tint; float amount; } params;

        void main() {
            vec4 c = texture(source, uv);
            color = vec4(mix(c.rgb, c.rgb * params.tint.rgb, params.amount), c.a);
        }
        """;

    // TintCode with an integer amount, in quarters.
    public const string IntegerAmountCode = """
        #version 450
        layout(location = 0) in vec2 uv;
        layout(location = 0) out vec4 color;
        layout(set = 0, binding = 0) uniform sampler2D source;
        layout(set = 0, binding = 1) uniform Params { vec4 tint; int amount; } params;

        void main() {
            vec4 c = texture(source, uv);
            color = vec4(mix(c.rgb, c.rgb * params.tint.rgb, float(params.amount) / 4.0), c.a);
        }
        """;

    // Line 4 of the fragment stage uses a name nothing declares.
    public const string BrokenCode = """
        #version 450
        layout(location = 0) out vec4 color;
        void main() {
            color = undeclaredThing;
        }
        """;

    // An unsigned integer output, which a picture cannot show.
    public const string IntegerOutputCode = """
        #version 450
        layout(location = 0) in vec2 uv;
        layout(location = 0) out uvec4 color;
        layout(set = 0, binding = 0) uniform sampler2D source;

        void main() {
            color = uvec4(texture(source, uv) * 255.0);
        }
        """;

    // A read-only buffer of no fixed size, which a pass cannot make itself.
    public const string NeedsBufferCode = """
        #version 450
        layout(location = 0) in vec2 uv;
        layout(location = 0) out vec4 color;
        layout(set = 0, binding = 0) uniform sampler2D source;
        layout(set = 0, binding = 1) readonly buffer Data { vec4 values[]; } data;

        void main() {
            color = texture(source, uv) * data.values[0];
        }
        """;
}
