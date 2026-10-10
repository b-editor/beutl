using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using Avalonia.Headless.NUnit;
using Beutl.AgentHost;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Workspace;
using Beutl.Api.Services;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Music;
using Beutl.Media.Source;
using Beutl.NodeGraph.Generative;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.Services.AI;
using Beutl.Testing.Headless;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Beutl.HeadlessUITests;

[TestFixture]
[NonParallelizable]
public sealed class AgentHostAiToolsTests
{
    private string _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "beutl-agent-ai-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [AvaloniaTest]
    public async Task TheInAppHostListsTheAiToolsAndRefusesThemWithoutAnAccount()
    {
        await TestReset.ResetShellAsync();
        var endpoint = new AgentHostEndpoint(
            new ProjectService(),
            new EditorService(new ExtensionProvider()),
            GetAvailableLoopbackPort(),
            "test-token",
            registryDirectory: _directory);
        try
        {
            await endpoint.StartAsync();
            var transport = new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = endpoint.EndpointUri!,
                TransportMode = HttpTransportMode.StreamableHttp,
                AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer " + endpoint.Token },
            });
            await using McpClient client = await McpClient.CreateAsync(transport);

            string[] names = [.. (await client.ListToolsAsync()).Select(tool => tool.Name)];
            CallToolResult result = await client.CallToolAsync(
                "generate_image",
                new Dictionary<string, object?> { ["prompt"] = "a lighthouse", ["waitSeconds"] = 0 });
            string text = string.Join("\n", result.Content.OfType<TextContentBlock>().Select(block => block.Text));

            Assert.Multiple(() =>
            {
                Assert.That(names, Is.SupersetOf(new[]
                {
                    "list_ai_models", "generate_image", "edit_image", "generate_video", "edit_video",
                    "transcribe_audio", "read_ai_job", "cancel_ai_job",
                }));
                Assert.That(text, Does.Contain(ErrorCode.AiUnavailable), "the tools are constructed and report the missing account");
            });
        }
        finally
        {
            await endpoint.StopAsync();
        }
    }

    [AvaloniaTest]
    public async Task AGeneratedPictureIsSavedAndItsPathReturned()
    {
        await TestReset.ResetShellAsync();
        Scene scene = await OpenSceneAsync("agent-ai-image");
        var backend = new FakeBackend { Result = WritePng("result.png") };
        using var jobs = new AgentAiJobManager();
        var tools = CreateTools(TestShell.Editor, jobs, backend);

        ToolResult<AgentAiJobSnapshot> result = await tools.GenerateImage("a lighthouse at dusk", waitSeconds: 10);

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        AgentAiJobSnapshot job = result.Value!;
        var request = (AiImageGenerationNodeRequest)backend.Requests.Single();
        Assert.Multiple(() =>
        {
            Assert.That(job.Status, Is.EqualTo(AgentAiJobStatus.Succeeded));
            Assert.That(job.Output?.OutputPath, Is.EqualTo(backend.Result));
            Assert.That(job.Output?.MediaKind, Is.EqualTo("image"));
            Assert.That(job.NextStep, Does.Contain("apply_edit"));
            Assert.That(request.Prompt, Is.EqualTo("a lighthouse at dusk"));
            Assert.That(request.AspectRatio, Is.EqualTo("4:3"), "the ratio nearest the 640x480 scene");
            Assert.That(backend.Scenes.Single(), Is.SameAs(scene));
        });
    }

    [AvaloniaTest]
    public async Task ALongJobIsReadBackByIdAndCanBeCancelled()
    {
        await TestReset.ResetShellAsync();
        await OpenSceneAsync("agent-ai-video");
        var backend = new FakeBackend { Result = WriteFile("clip.mp4"), Gate = new TaskCompletionSource() };
        using var jobs = new AgentAiJobManager();
        var tools = CreateTools(TestShell.Editor, jobs, backend);

        AgentAiJobSnapshot running = (await tools.GenerateVideo("waves", waitSeconds: 0)).Value!;
        backend.Gate.SetResult();
        AgentAiJobSnapshot finished = (await tools.ReadAiJob(running.JobId, waitSeconds: 10)).Value!;

        backend.Gate = new TaskCompletionSource();
        AgentAiJobSnapshot second = (await tools.GenerateVideo("rain", waitSeconds: 0)).Value!;
        tools.CancelAiJob(second.JobId);
        AgentAiJobSnapshot cancelled = (await tools.ReadAiJob(second.JobId, waitSeconds: 10)).Value!;

        Assert.Multiple(() =>
        {
            Assert.That(running.Status, Is.EqualTo(AgentAiJobStatus.Running));
            Assert.That(running.NextStep, Does.Contain("read_ai_job"));
            Assert.That(finished.Status, Is.EqualTo(AgentAiJobStatus.Succeeded));
            Assert.That(finished.Output?.MediaKind, Is.EqualTo("video"));
            Assert.That(cancelled.Status, Is.EqualTo(AgentAiJobStatus.Cancelled));
        });
    }

    [AvaloniaTest]
    public async Task ATranscriptIsReturnedWithTheJob()
    {
        await TestReset.ResetShellAsync();
        await OpenSceneAsync("agent-ai-transcribe");
        var backend = new FakeBackend();
        using var jobs = new AgentAiJobManager();
        var tools = CreateTools(TestShell.Editor, jobs, backend);

        ToolResult<AgentAiJobSnapshot> result = await tools.TranscribeAudio(WriteFile("voice.wav"), language: "ja", waitSeconds: 10);

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        Assert.That(result.Value!.Output?.Transcript?.Segments.Single().Text, Is.EqualTo("こんにちは"));
    }

    [AvaloniaTest]
    public async Task BadArgumentsAreTheCallsErrorsAndCostNothing()
    {
        await TestReset.ResetShellAsync();
        await OpenSceneAsync("agent-ai-errors");
        var backend = new FakeBackend();
        using var jobs = new AgentAiJobManager();
        var tools = CreateTools(TestShell.Editor, jobs, backend);

        ToolResult<AgentAiJobSnapshot> unknownTask = await tools.EditImage(WritePng("a.png"), "sharpen");
        ToolResult<AgentAiJobSnapshot> missingFile = await tools.EditImage(Path.Combine(_directory, "missing.png"), "upscale");
        ToolResult<AgentAiJobSnapshot> noPrompt = await tools.EditImage(WritePng("b.png"), "restyle");
        ToolResult<AgentAiJobSnapshot> unknownJob = await tools.ReadAiJob("nope", waitSeconds: 0);
        ToolResult<AgentAiJobSnapshot> negativeWait = await tools.GenerateImage("a cat", waitSeconds: -1);
        ToolResult<AgentAiJobSnapshot> longWait = await tools.ReadAiJob("nope", waitSeconds: 111);
        ToolResult<AgentAiJobSnapshot> oddOutpaint = await tools.EditImage(WritePng("c.png"), "outpaint", "more sky", outpaintExpansionPercent: 30);
        string[] fiveReferences = [.. Enumerable.Range(0, 5).Select(index => WritePng($"reference-{index}.png"))];
        ToolResult<AgentAiJobSnapshot> tooManyReferences = await tools.GenerateImage("a cat", referenceImagePaths: fiveReferences);
        string huge = Path.Combine(_directory, "huge.png");
        using (var bitmap = new Bitmap(8_193, 1))
            Assert.That(bitmap.Save(huge, EncodedImageFormat.Png), Is.True);
        ToolResult<AgentAiJobSnapshot> hugePicture = await tools.EditImage(huge, "upscale");
        ToolResult<ListAiModelsResponse> typo = await tools.ListAiModels("image.generat");
        ToolResult<ListAiModelsResponse> blank = await tools.ListAiModels(" ");
        backend.Models["image.generate"] = [SquareModel];
        ToolResult<AgentAiJobSnapshot> unknownModel = await tools.GenerateImage("a cat", model: "nope");
        backend.Models.Clear();
        ToolResult<AgentAiJobSnapshot> negativeSeed = await tools.GenerateImage("a cat", seed: -1);
        backend.ReferenceBudget = 10;
        ToolResult<AgentAiJobSnapshot> overBudget = await tools.GenerateImage("a cat", referenceImagePaths: [WritePng("budget.png")]);

        Assert.Multiple(() =>
        {
            Assert.That(unknownTask.Error?.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(missingFile.Error?.Code, Is.EqualTo(ErrorCode.MediaNotFound));
            Assert.That(noPrompt.Error?.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(unknownJob.Error?.Code, Is.EqualTo(ErrorCode.AiJobNotFound));
            Assert.That(negativeWait.Error?.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(longWait.Error?.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(oddOutpaint.Error?.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(tooManyReferences.Error?.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(hugePicture.Error?.Code, Is.EqualTo(ErrorCode.MediaUnsupported), "checked before it is decoded");
            Assert.That(typo.Error?.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(blank.Error?.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(unknownModel.Error?.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(negativeSeed.Error?.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(overBudget.Error?.Code, Is.EqualTo(ErrorCode.ValidationRejected), "the references come to more than the catalog's budget");
            Assert.That(backend.Requests, Is.Empty);
        });
    }

    [AvaloniaTest]
    public async Task OmittedSettingsAreChosenFromTheModelsOwnChoices()
    {
        await TestReset.ResetShellAsync();
        await OpenSceneAsync("agent-ai-model-choices");
        var backend = new FakeBackend { Result = WritePng("result.png") };
        backend.Models["image.generate"] = [SquareModel];
        backend.Models["video.generate"] = [PortraitModel];
        using var jobs = new AgentAiJobManager();
        var tools = CreateTools(TestShell.Editor, jobs, backend);

        ToolResult<AgentAiJobSnapshot> image = await tools.GenerateImage("a cat", waitSeconds: 10);
        ToolResult<AgentAiJobSnapshot> video = await tools.GenerateVideo("a cat", waitSeconds: 10);
        ToolResult<AgentAiJobSnapshot> sound = await tools.GenerateVideo("a cat", generateAudio: true, waitSeconds: 0);

        Assert.That(image.IsSuccess, Is.True, image.Error?.Message);
        Assert.That(video.IsSuccess, Is.True, video.Error?.Message);
        var imageRequest = backend.Requests.OfType<AiImageGenerationNodeRequest>().Single();
        var videoRequest = backend.Requests.OfType<AiVideoGenerationNodeRequest>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(imageRequest.AspectRatio, Is.EqualTo("1:1"), "the only ratio the model offers, not the 4:3 nearest the scene");
            Assert.That(imageRequest.Background, Is.EqualTo("opaque"));
            Assert.That(videoRequest.DurationSeconds, Is.EqualTo(5));
            Assert.That(videoRequest.Resolution, Is.EqualTo("480p"), "the smallest that covers the 480-line scene");
            Assert.That(videoRequest.AspectRatio, Is.EqualTo("9:16"));
            Assert.That(sound.Error?.Code, Is.EqualTo(ErrorCode.ValidationRejected), "a silent clip is not paid for");
            Assert.That(backend.Requests, Has.Count.EqualTo(2));
        });
    }

    [AvaloniaTest]
    public async Task ExplicitSettingsTheModelDoesNotTakeAreRefusedBeforeAJobStarts()
    {
        await TestReset.ResetShellAsync();
        await OpenSceneAsync("agent-ai-explicit");
        var backend = new FakeBackend();
        backend.Models["image.generate"] = [SquareModel];
        backend.Models["video.generate"] = [PortraitModel];
        backend.Models["video.extend"] = [PortraitModel];
        backend.Models["audio.transcribe"] = [new GenerativeModelInfo("whisper", "Whisper", IsDefault: true, IsAvailable: true, Image: null)];
        using var jobs = new AgentAiJobManager();
        var tools = CreateTools(TestShell.Editor, jobs, backend);

        ToolResult<AgentAiJobSnapshot>[] refused =
        [
            await tools.GenerateImage("a cat", aspectRatio: "16:9"),
            await tools.GenerateImage("a cat", background: "transparent"),
            await tools.GenerateVideo("a cat", resolution: "1080p"),
            await tools.GenerateVideo("a cat", durationSeconds: 6),
            await tools.GenerateVideo("a cat", aspectRatio: "16:9"),
            await tools.EditVideo(WriteFile("clip.mp4"), "more", mode: "extend", durationSeconds: 6),
            await tools.TranscribeAudio(WriteFile("voice.wav"), model: "nope"),
            await tools.GenerateImage("a cat", seed: 7),
            await tools.GenerateVideo("a cat", seed: 7),
            await tools.GenerateImage(new string('a', 4_001)),
        ];

        Assert.Multiple(() =>
        {
            foreach (ToolResult<AgentAiJobSnapshot> result in refused)
                Assert.That(result.Error?.Code, Is.EqualTo(ErrorCode.ValidationRejected), result.Error?.Message);
            Assert.That(backend.Requests, Is.Empty);
            Assert.That(backend.TranscribedPaths, Is.Empty);
        });
    }

    [AvaloniaTest]
    public async Task AModelsListShowsTheCapabilitiesOfTheOperationAsked()
    {
        await TestReset.ResetShellAsync();
        var backend = new FakeBackend();
        // The catalog gives video models image capabilities too, with the unrestricted seed.
        backend.Models["video.generate"] =
        [
            PortraitModel with { Image = new GenerativeImageCapabilities(null, null, SupportsSeed: true, MaxReferenceImages: 4) },
        ];
        using var jobs = new AgentAiJobManager();
        var tools = CreateTools(TestShell.Editor, jobs, backend);

        AiModelSummary summary = (await tools.ListAiModels("video.generate")).Value!.Models.Single();

        Assert.Multiple(() =>
        {
            Assert.That(summary.SupportsSeed, Is.False);
            Assert.That(summary.AspectRatios, Is.EqualTo(new[] { "9:16" }));
            Assert.That(summary.MaxReferenceImages, Is.Null);
            Assert.That(summary.DurationsSeconds, Is.EqualTo(new[] { 5, 10 }));
        });
    }

    [AvaloniaTest]
    public async Task AClipIsOpenedBeforeItsEditIsPaidFor()
    {
        await TestReset.ResetShellAsync();
        await OpenSceneAsync("agent-ai-edit-video");
        var backend = new FakeBackend { Result = WriteFile("result.mp4") };
        using var jobs = new AgentAiJobManager();
        var tools = CreateTools(TestShell.Editor, jobs, backend);
        var decoder = new StubVideoDecoder();
        DecoderRegistry.Register(decoder);
        try
        {
            ToolResult<AgentAiJobSnapshot> broken = await tools.EditVideo(WriteFile("broken.webm"), "rain");
            ToolResult<AgentAiJobSnapshot> edited = await tools.EditVideo(WriteFile("clip.mp4"), "rain", waitSeconds: 10);
            // The stub clip lasts six seconds.
            backend.Models["video.edit"] = [PortraitModel with { Video = PortraitModel.Video! with { MaxSourceVideoSeconds = 5 } }];
            ToolResult<AgentAiJobSnapshot> tooLong = await tools.EditVideo(WriteFile("long.mp4"), "rain");

            Assert.Multiple(() =>
            {
                Assert.That(broken.Error?.Code, Is.EqualTo(ErrorCode.MediaUnsupported));
                Assert.That(tooLong.Error?.Code, Is.EqualTo(ErrorCode.ValidationRejected));
                Assert.That(edited.IsSuccess, Is.True, edited.Error?.Message);
                Assert.That(((AiVideoEditNodeRequest)backend.Requests.Single()).SourceVideo.Name, Is.EqualTo("source.mp4"));
            });
        }
        finally
        {
            DecoderRegistry.Unregister(decoder);
        }
    }

    [Test]
    public async Task AFileThatCannotBeDecodedIsUnsupportedMediaNotAFailedGeneration()
    {
        var backend = new AgentHostAiBackend(() => null, null!, null!, null!, () => null!, _ => null!, null!);
        string path = WriteFile("noise.wav");

        AgentAiException? error = await Assert.ThrowsAsync<AgentAiException>(
            () => backend.TranscribeAsync(path, null, null, new Progress<string>(), CancellationToken.None));

        Assert.That(error?.Code, Is.EqualTo(ErrorCode.MediaUnsupported));
    }

    [AvaloniaTest]
    public async Task AnInputOutsideTheWorkspaceIsRefusedBeforeAnythingIsUploaded()
    {
        await TestReset.ResetShellAsync();
        Scene scene = await OpenSceneAsync("agent-ai-boundary");
        string outside = Path.Combine(Path.GetTempPath(), "beutl-agent-ai-outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        try
        {
            string secret = Path.Combine(outside, "secret.png");
            using (var bitmap = new Bitmap(4, 4))
                Assert.That(bitmap.Save(secret, EncodedImageFormat.Png), Is.True);
            string? linked = null;
            if (!OperatingSystem.IsWindows())
            {
                linked = Path.Combine(_directory, "linked.png");
                File.CreateSymbolicLink(linked, secret);
            }

            // Results of earlier calls sit beside the scene, which here is outside the workspace.
            string resultDirectory = AiResultImporter.GetResourceDirectory(scene);
            Directory.CreateDirectory(resultDirectory);
            string earlierResult = Path.Combine(resultDirectory, "earlier.png");
            File.Copy(secret, earlierResult);
            WritePng("relative.png");

            var backend = new FakeBackend { Result = WritePng("result.png") };
            using var jobs = new AgentAiJobManager();
            var tools = CreateTools(TestShell.Editor, jobs, backend);

            ToolResult<AgentAiJobSnapshot> edit = await tools.EditImage(secret, "upscale", waitSeconds: 0);
            ToolResult<AgentAiJobSnapshot> reference = await tools.GenerateImage("a cat", referenceImagePaths: [secret], waitSeconds: 0);
            ToolResult<AgentAiJobSnapshot> firstFrame = await tools.GenerateVideo("a cat", firstFramePath: secret, waitSeconds: 0);
            ToolResult<AgentAiJobSnapshot> video = await tools.EditVideo(secret, "a cat", waitSeconds: 0);
            ToolResult<AgentAiJobSnapshot> transcript = await tools.TranscribeAudio(secret, waitSeconds: 0);
            ToolResult<AgentAiJobSnapshot> probe = await tools.EditImage(Path.Combine(outside, "missing.png"), "upscale", waitSeconds: 0);
            ToolResult<AgentAiJobSnapshot>? link = linked is null ? null : await tools.EditImage(linked, "upscale", waitSeconds: 0);

            Assert.Multiple(() =>
            {
                foreach (ToolResult<AgentAiJobSnapshot> refused in new[] { edit, reference, firstFrame, video, transcript, probe })
                    Assert.That(refused.Error?.Code, Is.EqualTo(ErrorCode.WorkspaceBoundary));
                if (link is not null)
                    Assert.That(link.Error?.Code, Is.EqualTo(ErrorCode.WorkspaceBoundary), "a link inside the workspace is followed");
                Assert.That(backend.Requests, Is.Empty);
                Assert.That(backend.TranscribedPaths, Is.Empty);
            });

            ToolResult<AgentAiJobSnapshot> chained = await tools.EditImage(earlierResult, "upscale", waitSeconds: 10);
            ToolResult<AgentAiJobSnapshot> relative = await tools.EditImage("relative.png", "upscale", waitSeconds: 10);

            Assert.Multiple(() =>
            {
                Assert.That(chained.IsSuccess, Is.True, chained.Error?.Message);
                Assert.That(relative.IsSuccess, Is.True, relative.Error?.Message);
                Assert.That(backend.Requests, Has.Count.EqualTo(2));
            });
        }
        finally
        {
            Directory.Delete(outside, recursive: true);
        }
    }

    [AvaloniaTest]
    public async Task WithoutAnOpenSceneThereIsNowhereToSaveTheResult()
    {
        await TestReset.ResetShellAsync();
        using var jobs = new AgentAiJobManager();
        var tools = CreateTools(new EditorService(new ExtensionProvider()), jobs, new FakeBackend());

        ToolResult<AgentAiJobSnapshot> result = await tools.GenerateImage("a lighthouse", waitSeconds: 0);

        Assert.That(result.Error?.Code, Is.EqualTo(ErrorCode.NoActiveEditorSession));
    }

    private static async Task<Scene> OpenSceneAsync(string name)
    {
        string workspace = Path.Combine(BeutlHomeIsolation.CurrentHome!, name);
        Directory.CreateDirectory(workspace);
        Project project = (await TestShell.Project.CreateProject(640, 480, 30, 44100, name, workspace))!;
        Scene scene = project.Items.OfType<Scene>().First();
        TestShell.Editor.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();
        return scene;
    }

    private static GenerativeModelInfo PortraitModel { get; } =
        new("portrait", "Portrait", IsDefault: true, IsAvailable: true, Image: null, Video: new GenerativeVideoCapabilities(
            DurationsSeconds: [5, 10],
            Resolutions: ["480p", "720p"],
            AspectRatios: ["9:16"],
            SupportsAudio: false,
            SupportsSeed: false,
            SupportsFirstFrame: true,
            SupportsLastFrame: true,
            SupportsPromptToVideo: true,
            SupportsInputReferences: false,
            MaxImageReferences: 0,
            MaxImageReferenceBytes: 0,
            MaxVideoReferences: 0,
            MaxVideoReferenceBytes: 0,
            MaxPromptLength: 4_000));

    private static GenerativeModelInfo SquareModel { get; } =
        new("square", "Square", IsDefault: true, IsAvailable: true, new GenerativeImageCapabilities(["1:1"], ["opaque"], SupportsSeed: false, MaxReferenceImages: 4));

    private AgentHostAiTools CreateTools(EditorService editor, AgentAiJobManager jobs, FakeBackend backend)
        => new(editor, jobs, backend, new WorkspaceGuard(_directory));

    private string WritePng(string name)
    {
        string path = Path.Combine(_directory, name);
        using var bitmap = new Bitmap(4, 4);
        Assert.That(bitmap.Save(path, EncodedImageFormat.Png), Is.True);
        return path;
    }

    private string WriteFile(string name)
    {
        string path = Path.Combine(_directory, name);
        File.WriteAllBytes(path, [0, 1, 2, 3]);
        return path;
    }

    private static int GetAvailableLoopbackPort()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private sealed class StubVideoDecoder : IDecoderInfo
    {
        public string Name => "Agent edit test decoder";

        public IEnumerable<string> VideoExtensions() => [".mp4"];

        public IEnumerable<string> AudioExtensions() => [];

        public MediaReader Open(string file, MediaOptions options) => new StubVideoReader();
    }

    private sealed class StubVideoReader : MediaReader
    {
        public override VideoStreamInfo VideoInfo { get; } =
            new("test", new Rational(180, 1), new PixelSize(2, 2), new Rational(30, 1)) { Duration = new Rational(6, 1) };

        public override AudioStreamInfo AudioInfo => throw new InvalidOperationException();

        public override bool HasVideo => true;

        public override bool HasAudio => false;

        public override bool ReadVideo(int frame, [NotNullWhen(true)] out Ref<Bitmap>? image)
        {
            image = null;
            return false;
        }

        public override bool ReadAudio(int start, int length, [NotNullWhen(true)] out Ref<IPcm>? sound)
        {
            sound = null;
            return false;
        }
    }

    private sealed class FakeBackend : IAgentAiBackend, IGenerativeNodeExecutor
    {
        public string Result { get; set; } = string.Empty;

        public TaskCompletionSource? Gate { get; set; }

        public List<GenerativeRequest> Requests { get; } = [];

        public List<Scene> Scenes { get; } = [];

        public List<string> TranscribedPaths { get; } = [];

        public Dictionary<string, IReadOnlyList<GenerativeModelInfo>> Models { get; } = [];

        public long ReferenceBudget { get; set; } = AiRequestLimits.MaxImageReferencesTotalBytes;

        public Task<long> GetImageReferenceBudgetAsync(CancellationToken cancellationToken)
            => Task.FromResult(ReferenceBudget);

        public Task<string?> GetUnavailableReasonAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null);

        public Task<IReadOnlyList<GenerativeModelInfo>> GetModelsAsync(string operationId, CancellationToken cancellationToken)
            => Task.FromResult(Models.GetValueOrDefault(operationId) ?? []);

        public IGenerativeNodeExecutor CreateExecutor(Scene scene)
        {
            Scenes.Add(scene);
            return this;
        }

        public async Task<GenerativeExecutionResult> ExecuteAsync(
            GenerativeRequest request,
            IProgress<GenerativeProgress> progress,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (Gate is { } gate)
                await gate.Task.WaitAsync(cancellationToken);
            return new GenerativeExecutionResult(new Uri(Result), "model", 1, request.Operation == GenerativeOperation.VideoGeneration);
        }

        public Task<AgentTranscript> TranscribeAsync(
            string path,
            string? language,
            string? modelId,
            IProgress<string> progress,
            CancellationToken cancellationToken)
        {
            TranscribedPaths.Add(path);
            return Task.FromResult(new AgentTranscript(language, [new AgentTranscriptSegment(0, 1.5, "こんにちは")], null));
        }
    }
}
