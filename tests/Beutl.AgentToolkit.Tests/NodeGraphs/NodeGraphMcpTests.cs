using System.Text.Json;
using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.NodeGraphs;
using Beutl.AgentToolkit.Rendering;
using Beutl.AgentToolkit.Tests.Helpers;
using Beutl.AgentToolkit.Tools;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes;
using Beutl.NodeGraph.Nodes.Shaders;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using SkiaSharp;

namespace Beutl.AgentToolkit.Tests.NodeGraphs;

// What an agent does with nothing but the MCP server: add an effect, build its graph, render, save and reopen.
public sealed class NodeGraphMcpTests
{
    [Test]
    public async Task The_operations_schema_lists_the_fields_and_a_misspelled_one_is_refused()
    {
        string workspace = TestWorkspace.CreateDirectory();
        await using InProcessAgentToolkitServer server = await InProcessAgentToolkitServer.StartAsync(TestWorkspace.CreateDirectory());
        McpClient client = server.Client;
        string graphId = await CreateProjectWithEffectAsync(client, Path.Combine(workspace, "schema.bep"));

        IList<McpClientTool> tools = await client.ListToolsAsync();
        JsonElement operation = tools.Single(tool => tool.Name == "edit_node_graph").JsonSchema
            .GetProperty("properties").GetProperty("operations").GetProperty("items").GetProperty("properties");
        CallToolResult misspelled = await client.CallToolAsync("edit_node_graph", new Dictionary<string, object?>
        {
            ["graphId"] = graphId,
            ["operations"] = new JsonArray(new JsonObject { ["op"] = "add", ["ref"] = "time", ["typ"] = "TimeNode" })
        });
        ToolResult<EditNodeGraphResponse> result = ToolResultReader.Read<EditNodeGraphResponse>(misspelled);

        Assert.Multiple(() =>
        {
            Assert.That(operation.EnumerateObject().Select(property => property.Name),
                Is.SupersetOf(new[] { "op", "ref", "type", "node", "graph", "values", "from", "to", "port", "replace" }));
            Assert.That(operation.TryGetProperty("unknownFields", out _), Is.False);
            Assert.That(result.Error?.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(result.Error?.Message, Does.Contain("'typ'"));
        });
    }

    [Test]
    public async Task A_multi_pass_glow_renders_the_same_after_seeks_and_a_save_and_reload()
    {
        TestGpu.RequireAnyDevice();
        string workspace = TestWorkspace.CreateDirectory();
        string project = Path.Combine(workspace, "glow.bep");
        await using InProcessAgentToolkitServer server = await InProcessAgentToolkitServer.StartAsync(TestWorkspace.CreateDirectory());
        McpClient client = server.Client;
        string graphId = await CreateProjectWithEffectAsync(client, project);
        string plain = await RenderAsync(client, Path.Combine(workspace, "plain.png"), 0.5);

        JsonArray operations = NodeGraphGuide.LoadExamples().Single(example => example.Name == "multi-pass-glow").Operations;
        ToolResult<EditNodeGraphResponse> edit = ToolResultReader.Read<EditNodeGraphResponse>(
            await client.CallToolAsync("edit_node_graph", new Dictionary<string, object?>
            {
                ["graphId"] = graphId,
                ["operations"] = operations.DeepClone()
            }));
        Assert.That(edit.IsSuccess, Is.True, edit.Error?.Message);
        Assert.That(edit.Value!.Applied, Is.True, string.Join("; ", edit.Value.Diagnostics.Select(d => d.Message)));

        string first = await RenderAsync(client, Path.Combine(workspace, "first.png"), 0.5);
        string later = await RenderAsync(client, Path.Combine(workspace, "later.png"), 1.5);
        string back = await RenderAsync(client, Path.Combine(workspace, "back.png"), 0.5);
        await RenderAsync(client, Path.Combine(workspace, "half.png"), 0.5, renderScale: 0.5f);
        await RenderAsync(client, Path.Combine(workspace, "double.png"), 0.5, renderScale: 2);

        AssertSuccess(await client.CallToolAsync("save_project", new Dictionary<string, object?>()));
        AssertSuccess(await client.CallToolAsync("open_project", new Dictionary<string, object?> { ["path"] = project }));
        string reopened = await RenderAsync(client, Path.Combine(workspace, "reopened.png"), 0.5);

        using SKBitmap plainBitmap = SKBitmap.Decode(plain);
        using SKBitmap firstBitmap = SKBitmap.Decode(first);
        // The rectangle spans x 36..60; 12 pixels to its left only the glow can reach.
        Assert.Multiple(() =>
        {
            Assert.That(plainBitmap.GetPixel(24, 32).Alpha, Is.Zero);
            Assert.That(firstBitmap.GetPixel(24, 32).Alpha, Is.GreaterThan(0), "The glow spreads past the layer.");
            AssertSamePixels(first, later, "The glow does not depend on time.");
            AssertSamePixels(first, back, "A seek back renders what the first render did.");
            AssertSamePixels(first, reopened, "Saving and reopening keeps the graph.");
        });
    }

    // A missing buffer fails the pass itself; an integer image fails only the picture, which takes that back when its
    // renderer goes, so the warnings are read while it is there.
    [TestCase(NodeGraphTestScene.NeedsBufferCode)]
    [TestCase(NodeGraphTestScene.IntegerOutputCode)]
    public async Task Render_still_reports_what_a_graph_failed_on(string code)
    {
        using var scene = new NodeGraphTestScene();
        GraphModel graph = scene.Graph;
        var input = new FilterEffectInputNode();
        var pass = new ShaderFullscreenPassNode { Code = { Value = code } };
        var output = new OutputNode();
        graph.Nodes.Add(input);
        graph.Nodes.Add(pass);
        graph.Nodes.Add(output);
        graph.Connect((IInputPort)pass.Items.Single(item => item.Name.Value == "In.source"), input.Output);
        graph.Connect(output.InputPort, (IOutputPort)pass.Items.Single(item => item.Name.Value == "Out.color"));

        RenderStillResponse response = await new StillRenderer().RenderAsync(
            RenderTools.CreateSceneSnapshot(scene.Session),
            MediaTime.Zero,
            Path.Combine(TestWorkspace.CreateDirectory(), "failed.png"),
            1,
            CancellationToken.None);

        Assert.That(response.Warnings, Has.Some.Contain($"node {pass.Id.Value}"));
    }

    // A project with one element holding a white rectangle whose effect is an empty node graph; returns the effect's Id.
    private static async Task<string> CreateProjectWithEffectAsync(McpClient client, string path)
    {
        AssertSuccess(await client.CallToolAsync("create_project", new Dictionary<string, object?>
        {
            ["path"] = path,
            ["width"] = 96,
            ["height"] = 64,
            ["frameRate"] = 30,
            ["duration"] = "00:00:02"
        }));

        var patch = new JsonObject
        {
            ["Elements"] = new JsonArray(new JsonObject
            {
                ["$type"] = "[Beutl.ProjectSystem]:Element",
                ["Name"] = "Glow",
                ["Start"] = "00:00:00",
                ["Length"] = "00:00:02",
                ["Objects"] = new JsonArray(new JsonObject
                {
                    ["$type"] = IdentityHelper.WriteDiscriminator(typeof(RectShape)),
                    ["Width"] = 24,
                    ["Height"] = 16,
                    ["Fill"] = new JsonObject
                    {
                        ["$type"] = IdentityHelper.WriteDiscriminator(typeof(SolidColorBrush)),
                        ["Color"] = CoreSerializer.SerializeToJsonNode(Colors.White)
                    },
                    ["FilterEffect"] = new JsonObject { ["$type"] = IdentityHelper.WriteDiscriminator(typeof(NodeGraphFilterEffect)) }
                })
            })
        };
        ToolResult<ApplyEditResponse> apply = ToolResultReader.Read<ApplyEditResponse>(
            await client.CallToolAsync("apply_edit", new Dictionary<string, object?>
            {
                ["patch"] = patch,
                ["schemaVersion"] = SchemaVersion.Current
            }));
        Assert.That(apply.IsSuccess, Is.True, apply.Error?.Message);
        return apply.Value!.CreatedIds
            .Single(created => created.Type == IdentityHelper.WriteDiscriminator(typeof(NodeGraphFilterEffect)))
            .Id;
    }

    private static async Task<string> RenderAsync(McpClient client, string path, double time, float renderScale = 1)
    {
        ToolResult<RenderStillResponse> result = ToolResultReader.Read<RenderStillResponse>(
            await client.CallToolAsync("render_still", new Dictionary<string, object?>
            {
                ["outputPath"] = path,
                ["timeSeconds"] = time,
                ["renderScale"] = renderScale
            }));
        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        Assert.That(result.Value!.Warnings.Where(warning => warning.StartsWith("Node graph", StringComparison.Ordinal)), Is.Empty);
        return path;
    }

    private static void AssertSamePixels(string expected, string actual, string message)
    {
        using SKBitmap a = SKBitmap.Decode(expected);
        using SKBitmap b = SKBitmap.Decode(actual);
        Assert.That((b.Width, b.Height), Is.EqualTo((a.Width, a.Height)), message);
        int different = 0;
        for (int y = 0; y < a.Height; y++)
        {
            for (int x = 0; x < a.Width; x++)
            {
                SKColor p = a.GetPixel(x, y);
                SKColor q = b.GetPixel(x, y);
                if (Math.Abs(p.Red - q.Red) > 1 || Math.Abs(p.Green - q.Green) > 1
                    || Math.Abs(p.Blue - q.Blue) > 1 || Math.Abs(p.Alpha - q.Alpha) > 1)
                {
                    different++;
                }
            }
        }

        Assert.That(different, Is.Zero, message);
    }

    private static void AssertSuccess(CallToolResult result)
    {
        Assert.That(result.IsError, Is.Not.True, result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text);
        using JsonDocument document = JsonDocument.Parse(result.Content.OfType<TextContentBlock>().Single().Text);
        Assert.That(document.RootElement.TryGetProperty("error", out JsonElement error) && error.ValueKind != JsonValueKind.Null,
            Is.False, document.RootElement.ToString());
    }
}
