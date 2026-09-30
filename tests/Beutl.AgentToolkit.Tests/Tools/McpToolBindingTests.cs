using System.IO.Pipelines;
using System.Text.Json;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Rendering;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tools;
using Beutl.AgentToolkit.Workspace;
using Beutl.Extensibility;
using Beutl.Extensions.FFmpeg;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Beutl.AgentToolkit.Tests.Tools;

public sealed class McpToolBindingTests
{
    [Test]
    public async Task Frame_measurement_returns_raw_data_for_a_static_empty_scene()
    {
        string workspace = CreateWorkspace();
        await using InProcessMcpServer server = await InProcessMcpServer.StartAsync(workspace);
        await using McpClient client = await McpClient.CreateAsync(server.ClientTransport);
        AssertMcpCallSucceeded(await client.CallToolAsync("create_project", new Dictionary<string, object?>
        {
            ["path"] = "static.bep",
            ["width"] = 32,
            ["height"] = 18,
            ["frameRate"] = 30,
            ["duration"] = "00:00:03"
        }), "create_project");

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

        string workspace = CreateWorkspace();
        await using InProcessMcpServer server = await InProcessMcpServer.StartAsync(workspace);
        await using McpClient client = await McpClient.CreateAsync(server.ClientTransport);

        CallToolResult create = await client.CallToolAsync(
            "create_project",
            new Dictionary<string, object?>
            {
                ["path"] = "export-probe.bep",
                ["width"] = 320,
                ["height"] = 180,
                ["frameRate"] = 30,
                ["duration"] = "00:00:02"
            });
        AssertMcpCallSucceeded(create, "create_project");

        CallToolResult result = await client.CallToolAsync(
            "export_video",
            new Dictionary<string, object?>
            {
                // .webm is FFmpeg-only on every platform, so the absent-worker guard fires even where
                // AVFoundation would otherwise handle .mp4/.mov without the worker.
                ["outputPath"] = "out.webm"
            });

        Assert.Multiple(() =>
        {
            AssertToolResultError(result, ErrorCode.CodecUnavailable);
            Assert.That(ReadText(result), Does.Contain("FFmpegWorker"));
        });
    }

    [Test]
    public async Task Missing_required_tool_argument_returns_typed_validation_error_through_mcp()
    {
        string workspace = CreateWorkspace();
        await using InProcessMcpServer server = await InProcessMcpServer.StartAsync(workspace);
        await using McpClient client = await McpClient.CreateAsync(server.ClientTransport);

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
        string workspace = CreateWorkspace();
        await using InProcessMcpServer server = await InProcessMcpServer.StartAsync(workspace);
        await using McpClient client = await McpClient.CreateAsync(server.ClientTransport);

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
        string workspace = CreateWorkspace();
        await using InProcessMcpServer server = await InProcessMcpServer.StartAsync(workspace);
        await using McpClient client = await McpClient.CreateAsync(server.ClientTransport);

        CallToolResult create = await client.CallToolAsync(
            "create_project",
            new Dictionary<string, object?>
            {
                ["path"] = "sessionless-save.bep",
                ["width"] = 320,
                ["height"] = 180,
                ["frameRate"] = 30,
                ["duration"] = "00:00:03"
            });
        AssertMcpCallSucceeded(create, "create_project");

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
        JsonElement root = document.RootElement;

        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("isSuccess").GetBoolean(), Is.True, text);
            if (root.TryGetProperty("error", out JsonElement error))
            {
                Assert.That(error.ValueKind, Is.EqualTo(JsonValueKind.Null), text);
            }
        });
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

    private static void AssertMcpCallSucceeded(CallToolResult result, string toolName)
    {
        string text = ReadText(result);
        Assert.Multiple(() =>
        {
            Assert.That(result.IsError, Is.Not.True, text);
            Assert.That(text, Does.Not.Contain($"An error occurred invoking '{toolName}'"));
        });
    }

    private static string ReadText(CallToolResult result)
        => string.Join("\n", result.Content.OfType<TextContentBlock>().Select(block => block.Text));

    private static string CreateWorkspace()
    {
        string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class InProcessMcpServer : IAsyncDisposable
    {
        private readonly IHost _host;
        private readonly Stream _clientOutput;
        private readonly Stream _serverInput;
        private readonly Stream _serverOutput;
        private readonly Stream _clientInput;

        private InProcessMcpServer(
            IHost host,
            Stream clientOutput,
            Stream serverInput,
            Stream serverOutput,
            Stream clientInput)
        {
            _host = host;
            _clientOutput = clientOutput;
            _serverInput = serverInput;
            _serverOutput = serverOutput;
            _clientInput = clientInput;
            ClientTransport = new StreamClientTransport(_clientOutput, _clientInput);
        }

        public StreamClientTransport ClientTransport { get; }

        public static async Task<InProcessMcpServer> StartAsync(string workspace)
        {
            var clientToServer = new Pipe();
            var serverToClient = new Pipe();
            Stream clientOutput = clientToServer.Writer.AsStream();
            Stream serverInput = clientToServer.Reader.AsStream();
            Stream serverOutput = serverToClient.Writer.AsStream();
            Stream clientInput = serverToClient.Reader.AsStream();

            HostApplicationBuilder builder = Host.CreateApplicationBuilder();
            builder.Services
                .AddSingleton<IWorkspaceGuard>(_ => new WorkspaceGuard(workspace))
                .AddSingleton<DestructiveGuard>()
                .AddSingleton<StillRenderer>()
                .AddSingleton<StoryboardRenderer>()
                .AddSingleton<FrameDifferenceAnalyzer>()
                .AddSingleton<AudioRhythmAnalyzer>()
                .AddSingleton<EncoderRegistration>()
                .AddSingleton<VideoExporter>()
                .AddSingleton<IOutputOperationLeaseProvider>(StandaloneOutputOperationLeaseProvider.Instance)
                .AddSingleton<RenderJobManager>()
                .AddSingleton<FileSessionSource>()
                .AddSingleton<IProjectSessionGateway, FileProjectSessionGateway>()
                .AddSingleton<AgentSessionManager>();

            builder.Services
                .AddMcpServer()
                .WithStreamServerTransport(serverInput, serverOutput)
                .WithRequestFilters(filters => filters.AddToolkitCallToolErrorFilter())
                .WithTools<SessionTools>()
                .WithTools<QueryTools>()
                .WithTools<EditTools>()
                .WithTools<RenderTools>();

            IHost host = builder.Build();
            await host.StartAsync().ConfigureAwait(false);

            return new InProcessMcpServer(
                host,
                clientOutput,
                serverInput,
                serverOutput,
                clientInput);
        }

        public async ValueTask DisposeAsync()
        {
            await _host.StopAsync().ConfigureAwait(false);
            _host.Dispose();
            _clientInput.Dispose();
            _serverOutput.Dispose();
            _serverInput.Dispose();
            _clientOutput.Dispose();
        }
    }
}
