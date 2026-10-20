using System.Text.Json;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Tests.Helpers;
using Beutl.Extensions.FFmpeg;
using Beutl.Graphics.Transitions;
using Beutl.Services;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Beutl.AgentToolkit.Tests.Tools;

public sealed class McpToolBindingTests
{
    [Test]
    public async Task Frame_measurement_returns_raw_data_for_a_static_empty_scene()
    {
        string workspace = TestWorkspace.CreateDirectory();
        await using InProcessAgentToolkitServer server =
            await InProcessAgentToolkitServer.StartAsync(TestWorkspace.CreateDirectory());
        McpClient client = server.Client;
        AssertToolResultSuccess(await client.CallToolAsync("create_project", new Dictionary<string, object?>
        {
            ["path"] = Path.Combine(workspace, "static.bep"),
            ["width"] = 32,
            ["height"] = 18,
            ["frameRate"] = 30,
            ["duration"] = "00:00:03"
        }));

        CallToolResult result = await client.CallToolAsync("measure_frame_differences", new Dictionary<string, object?>
        {
            ["timeSeconds"] = new[] { 0.5, 1.5 },
            ["pixelDeltaThreshold"] = 32,
            ["foregroundLumaThreshold"] = 16
        });
        AssertToolResultSuccess(result);
        using JsonDocument document = JsonDocument.Parse(ReadText(result));
        JsonElement value = document.RootElement.GetProperty("value");
        Assert.Multiple(() =>
        {
            Assert.That(value.GetProperty("minimumChangedPixelRatio").GetDouble(), Is.Zero);
            Assert.That(value.GetProperty("averageChangedPixelRatio").GetDouble(), Is.Zero);
            Assert.That(value.GetProperty("pixelDeltaThreshold").GetInt32(), Is.EqualTo(32));
            Assert.That(value.GetProperty("foregroundLumaThreshold").GetInt32(), Is.EqualTo(16));
            Assert.That(value.EnumerateObject().Select(property => property.Name), Is.EquivalentTo(new[]
            {
                "pixelDeltaThreshold", "foregroundLumaThreshold", "minimumChangedPixelRatio",
                "averageChangedPixelRatio", "samples", "pairVariations", "frameCoverage"
            }));
        });
    }

    [Test]
    public async Task Export_video_returns_clear_error_when_ffmpeg_worker_absent()
    {
        if (FFmpegWorkerProcess.IsWorkerAvailable(AppContext.BaseDirectory))
        {
            Assert.Ignore("Test BaseDirectory already has an FFmpeg worker; cannot verify the absent-worker path.");
        }

        string workspace = TestWorkspace.CreateDirectory();
        await using InProcessAgentToolkitServer server =
            await InProcessAgentToolkitServer.StartAsync(TestWorkspace.CreateDirectory());
        McpClient client = server.Client;

        CallToolResult create = await client.CallToolAsync(
            "create_project",
            new Dictionary<string, object?>
            {
                ["path"] = Path.Combine(workspace, "export-probe.bep"),
                ["width"] = 320,
                ["height"] = 180,
                ["frameRate"] = 30,
                ["duration"] = "00:00:02"
            });
        AssertToolResultSuccess(create);

        CallToolResult result = await client.CallToolAsync(
            "export_video",
            new Dictionary<string, object?>
            {
                // .webm is FFmpeg-only on every platform, so the absent-worker guard fires even where
                // AVFoundation would otherwise handle .mp4/.mov without the worker.
                ["outputPath"] = Path.Combine(workspace, "out.webm")
            });

        Assert.Multiple(() =>
        {
            AssertToolResultError(result, ErrorCode.CodecUnavailable);
            Assert.That(ReadText(result), Does.Contain("FFmpegWorker"));
        });
    }

    // The installed server runs the editor's LibraryRegistrar, so get_schema lists what the app's library lists.
    [Test]
    public async Task Get_schema_lists_every_library_type()
    {
        await using InProcessAgentToolkitServer server =
            await InProcessAgentToolkitServer.StartAsync(TestWorkspace.CreateDirectory());

        CallToolResult result = await server.Client.CallToolAsync(
            "get_schema", new Dictionary<string, object?> { ["includeProperties"] = false });

        AssertToolResultSuccess(result);
        using JsonDocument document = JsonDocument.Parse(ReadText(result));
        string[] listed = document.RootElement.GetProperty("value").GetProperty("types").EnumerateArray()
            .Select(type => type.GetProperty("type").GetString()!)
            .ToArray();
        string[] library = typeof(KnownLibraryItemFormats).GetFields()
            .SelectMany(field => LibraryService.Current.GetTypesFromFormat((string)field.GetValue(null)!))
            .Select(type => type.FullName!)
            .ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(library, Does.Contain(typeof(CrossDissolveTransition).FullName));
            Assert.That(library.Except(listed), Is.Empty);
        });
    }

    [Test]
    public async Task Missing_required_tool_argument_returns_typed_validation_error_through_mcp()
    {
        await using InProcessAgentToolkitServer server =
            await InProcessAgentToolkitServer.StartAsync(TestWorkspace.CreateDirectory());
        McpClient client = server.Client;

        CallToolResult result = await client.CallToolAsync("render_still", new Dictionary<string, object?>());

        Assert.Multiple(() =>
        {
            Assert.That(ReadText(result), Does.Not.Contain("An error occurred invoking 'render_still'"));
            AssertToolResultError(result, ErrorCode.ValidationRejected);
        });
    }

    [Test]
    public async Task Unknown_tool_argument_returns_typed_validation_error_with_accepted_parameters()
    {
        await using InProcessAgentToolkitServer server =
            await InProcessAgentToolkitServer.StartAsync(TestWorkspace.CreateDirectory());
        McpClient client = server.Client;

        CallToolResult result = await client.CallToolAsync(
            "render_still",
            new Dictionary<string, object?>
            {
                ["outputPath"] = "still.png",
                ["time"] = 1.25
            });
        string text = ReadText(result);

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Not.Contain("An error occurred invoking 'render_still'"));
            AssertToolResultError(result, ErrorCode.ValidationRejected);
            Assert.That(text, Does.Contain("Unknown argument"));
            Assert.That(text, Does.Contain("time"));
            Assert.That(text, Does.Contain("timeSeconds"));
            Assert.That(text, Does.Contain("outputPath"));
        });
    }

    [Test]
    public async Task Save_project_uses_current_session_when_session_is_omitted()
    {
        string workspace = TestWorkspace.CreateDirectory();
        await using InProcessAgentToolkitServer server =
            await InProcessAgentToolkitServer.StartAsync(TestWorkspace.CreateDirectory());
        McpClient client = server.Client;

        CallToolResult create = await client.CallToolAsync(
            "create_project",
            new Dictionary<string, object?>
            {
                ["path"] = Path.Combine(workspace, "sessionless-save.bep"),
                ["width"] = 320,
                ["height"] = 180,
                ["frameRate"] = 30,
                ["duration"] = "00:00:03"
            });
        AssertToolResultSuccess(create);

        CallToolResult result = await client.CallToolAsync("save_project", new Dictionary<string, object?>());

        Assert.Multiple(() =>
        {
            Assert.That(ReadText(result), Does.Not.Contain("An error occurred invoking 'save_project'"));
            AssertToolResultSuccess(result);
            Assert.That(File.Exists(Path.Combine(workspace, "sessionless-save.bep")), Is.True);
        });
    }

    private static void AssertToolResultSuccess(CallToolResult result)
    {
        string text = ReadText(result);
        using JsonDocument document = JsonDocument.Parse(text);
        Assert.That(document.RootElement.GetProperty("isSuccess").GetBoolean(), Is.True, text);
    }

    private static void AssertToolResultError(CallToolResult result, string code)
    {
        string text = ReadText(result);
        using JsonDocument document = JsonDocument.Parse(text);
        JsonElement root = document.RootElement;
        JsonElement error = root.GetProperty("error");

        Assert.Multiple(() =>
        {
            Assert.That(error.ValueKind, Is.EqualTo(JsonValueKind.Object), text);
            Assert.That(error.GetProperty("code").GetString(), Is.EqualTo(code), text);
        });
    }

    private static string ReadText(CallToolResult result)
        => string.Join("\n", result.Content.OfType<TextContentBlock>().Select(block => block.Text));
}
