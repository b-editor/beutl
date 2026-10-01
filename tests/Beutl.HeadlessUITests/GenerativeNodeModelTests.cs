using System.Collections.Immutable;
using Avalonia.Headless.NUnit;
using Beutl.Api.Services;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.NodeGraphTab.PropertyEditors;
using Beutl.Extensibility;
using Beutl.Graphics;
using Beutl.Language;
using Beutl.Media;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Generative;
using Beutl.NodeGraph.Nodes.Generative;
using Beutl.ProjectSystem;
using Beutl.Services.AI;
using Beutl.Testing.Headless;
using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public sealed class GenerativeNodeModelTests
{
    private static readonly AiModelOption Wide = new(
        new AiModelId("wide"),
        "Wide",
        AiModelCostTier.Low,
        IsDefault: true,
        Image: new AiImageModelCapabilities(
            AiCapabilityDimension<string>.Supported(["16:9", "1:1"]),
            AiCapabilityDimension<string>.Unspecified,
            SupportsSeed: true,
            MaxReferenceImages: 4));

    private static readonly AiModelOption Square = new(
        new AiModelId("square"),
        "Square",
        AiModelCostTier.High,
        IsDefault: false,
        Image: new AiImageModelCapabilities(
            AiCapabilityDimension<string>.Supported(["1:1"]),
            AiCapabilityDimension<string>.Supported(["auto", "opaque"]),
            SupportsSeed: false,
            MaxReferenceImages: 0));

    private string _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"beutl-generative-model-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_directory, recursive: true); }
        catch (IOException) { }
    }

    [AvaloniaTest]
    public async Task ModelChoiceListsTheCatalogWithTheDialogLabels()
    {
        var node = new AiImageGenerationNode();
        using var vm = CreateEditor(node.Model.Property!);
        EnumEditor editor = await ShowAsync(vm, CreateCatalog(), e => e.Items.Count == 2);

        Assert.That(editor.Items.Select(item => item.DisplayName), Is.EqualTo(new[]
        {
            $"Wide — {Strings.AiModelCostLow}",
            $"Square — {Strings.AiModelCostHigh}",
        }), "No separate default entry, as in the AI tab.");
        Assert.That(editor.SelectedIndex, Is.Zero, "An empty model shows the model the picker starts on.");
        Assert.That(node.Model.Property!.GetValue(), Is.Empty, "Showing the default does not edit the project.");
    }

    [AvaloniaTest]
    public async Task ChoosingANarrowerModelNarrowsAndResetsTheAspectRatio()
    {
        var node = new AiImageGenerationNode();
        Assert.That(node.AspectRatio.Property!.GetValue(), Is.EqualTo("16:9"), "The AI tab's default shape.");
        using var vm = CreateEditor(node.AspectRatio.Property!);
        EnumEditor editor = await ShowAsync(vm, CreateCatalog(), e => e.Items.Count == 2);
        Assert.That(editor.Items.Select(item => item.Value), Is.EqualTo(new[] { "16:9", "1:1" }),
            "Left on the default model, the default model's shapes are offered.");

        node.Model.Property!.SetValue("square");
        HeadlessTestHelpers.Render(3);

        Assert.That(node.AspectRatio.Property!.GetValue(), Is.EqualTo("1:1"));
        Assert.That(editor.Items.Select(item => item.Value), Is.EqualTo(new[] { "1:1" }));
    }

    [AvaloniaTest]
    public async Task UnsupportedShapeIsRefusedBeforeAnythingIsSent()
    {
        var images = new CapturingImages();
        var executor = CreateExecutor(images);
        var node = new AiImageGenerationNode();

        // Await the rejection without blocking the UI thread, so an unexpected save can complete.
        GenerativeExecutionException? refused = null;
        try
        {
            await executor.ExecuteAsync(
                Request(node, model: "square", aspectRatio: "16:9"),
                new Progress<GenerativeProgress>(),
                CancellationToken.None);
        }
        catch (GenerativeExecutionException ex)
        {
            refused = ex;
        }

        Assert.That(refused?.Message, Is.EqualTo(Strings.AiModelDoesNotSupportRequest));
        Assert.That(images.Requests, Is.Empty);
    }

    [AvaloniaTest]
    public async Task SeedIsLeftOutForAModelThatTakesNone()
    {
        var images = new CapturingImages();
        var executor = CreateExecutor(images);
        var node = new AiImageGenerationNode();

        GenerativeExecutionResult result = await executor.ExecuteAsync(
            Request(node, model: "square", aspectRatio: "1:1", seed: 42),
            new Progress<GenerativeProgress>(),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(images.Requests.Single().Seed, Is.Null);
            Assert.That(images.Requests.Single().Model, Is.EqualTo(new AiModelId("square")));
            Assert.That(result.Seed, Is.Null, "The record says what was actually sent.");
            Assert.That(File.Exists(result.ResultFile.LocalPath), Is.True);
        });
    }

    [AvaloniaTest]
    public async Task DefaultModelIsNamedAsThePickerWouldPickIt()
    {
        var images = new CapturingImages();
        var executor = CreateExecutor(images);
        var node = new AiImageGenerationNode();

        GenerativeExecutionResult result = await executor.ExecuteAsync(
            Request(node, model: null, aspectRatio: "16:9", seed: 7),
            new Progress<GenerativeProgress>(),
            CancellationToken.None);

        Assert.That(images.Requests.Single().Model, Is.EqualTo(new AiModelId("wide")));
        Assert.That(images.Requests.Single().Seed, Is.EqualTo(7));
        Assert.That(result.ModelId, Is.EqualTo("wide"));
    }

    [AvaloniaTest]
    public async Task SuccessfulGenerationIsRecordedInThePromptLibraryAndRefusalsAreNot()
    {
        var library = new RecordingLibrary();
        var executor = CreateExecutor(new CapturingImages(), library);
        var node = new AiImageGenerationNode();

        await executor.ExecuteAsync(Request(node, "wide", "1:1"), new Progress<GenerativeProgress>(), CancellationToken.None);
        try
        {
            await executor.ExecuteAsync(Request(node, "square", "16:9"), new Progress<GenerativeProgress>(), CancellationToken.None);
        }
        catch (GenerativeExecutionException)
        {
        }

        Assert.That(library.Recorded, Is.EqualTo(new[] { "a cat" }));
    }

    [Test]
    public void LibraryAdapterListsTheDialogsImagePromptsInTheDialogsOrder()
    {
        var now = DateTimeOffset.UtcNow;
        var store = new FixedPromptStore(
            [
                new PromptTemplate(Guid.NewGuid(), "old", PromptTaskKind.Image, "o", now.AddDays(-2), now.AddDays(-2), false),
                new PromptTemplate(Guid.NewGuid(), "pinned", PromptTaskKind.Image, "p", now.AddDays(-3), now.AddDays(-3), true),
                new PromptTemplate(Guid.NewGuid(), "video", PromptTaskKind.Video, "v", now, now, true),
            ],
            [
                new PromptHistoryEntry(Guid.NewGuid(), PromptTaskKind.Image, "first line\nStyle: x", now, 1, false),
            ]);

        var entries = new AiGenerativePromptLibrary(store).GetEntries(GenerativeOperation.ImageGeneration);

        Assert.That(entries.Select(e => (e.Name, e.IsTemplate)), Is.EqualTo(new[]
        {
            ("pinned", true), ("old", true), ("first line", false),
        }));
        Assert.That(entries[2].Prompt, Is.EqualTo("first line\nStyle: x"));
    }

    [AvaloniaTest]
    public void PromptInputsUseAMultilineEditor()
    {
        var prompt = new AiPromptNode();
        var image = new AiImageGenerationNode();

        foreach (IPropertyAdapter property in new IPropertyAdapter[]
                 {
                     prompt.Prompt.Property!, prompt.Style.Property!, prompt.Composition.Property!,
                     prompt.Exclusions.Property!, image.Prompt.Property!,
                 })
        {
            using var vm = new Beutl.ViewModels.Editors.StringEditorViewModel((IPropertyAdapter<string?>)property);
            var editor = new StringEditor();
            vm.Accept(editor);
            Assert.That(editor.Classes.Contains("multiline"), Is.True, property.DisplayName);
        }

        using var aspect = new Beutl.ViewModels.Editors.StringEditorViewModel((IPropertyAdapter<string?>)image.Model.Property!);
        var single = new StringEditor();
        aspect.Accept(single);
        Assert.That(single.Classes.Contains("multiline"), Is.False, "Only prompts are multi-line.");
    }

    [Test]
    public void InputsFollowTheAiTabsOrder()
    {
        var node = new AiImageGenerationNode();
        string[] inputs = node.Items.OfType<IInputPort>().Select(port => port.Name).ToArray();
        Assert.That(inputs, Is.EqualTo(new[]
        {
            "Prompt", "AspectRatio", "Model", "Background", "Seed", "SeedControl", "References",
        }));
    }

    [AvaloniaTest]
    public async Task OutpaintSendsTheWidenedCanvasAsTheTabDoes()
    {
        var editing = new CapturingEditing();
        var executor = CreateExecutor(new CapturingImages(), editing: editing);
        byte[] png;
        using (var bitmap = new Bitmap(40, 20))
        using (var stream = new MemoryStream())
        {
            bitmap.Save(stream, EncodedImageFormat.Png);
            png = stream.ToArray();
        }

        await executor.ExecuteAsync(
            new AiImageEditNodeRequest(new AiImageEditNode())
            {
                Task = AiImageEditTask.Outpaint,
                Prompt = "a beach",
                OutpaintExpansionPercent = 25,
                Image = new GenerativeImageInput("image.png", png),
                RequestKeySeed = Guid.NewGuid().ToString("N"),
                ParameterFingerprint = "p",
                CatalogOperationId = "image.edit.outpaint",
            },
            new Progress<GenerativeProgress>(),
            CancellationToken.None);

        AiImageEditRequest sent = editing.Requests.Single();
        using var uploaded = new MemoryStream();
        await using (Stream opened = await sent.Image.OpenReadAsync(CancellationToken.None))
            await opened.CopyToAsync(uploaded);
        uploaded.Position = 0;
        using Bitmap widened = Bitmap.FromStream(uploaded);
        Assert.Multiple(() =>
        {
            Assert.That(sent.Image.FileName, Is.EqualTo("image-outpaint.png"));
            Assert.That((widened.Width, widened.Height), Is.EqualTo((60, 30)));
            Assert.That(sent.Prompt, Does.StartWith("Extend the image naturally").And.EndWith("a beach"));
            Assert.That(sent.Task.Value, Is.EqualTo("outpaint"));
        });
    }

    [AvaloniaTest]
    public async Task EditModelsFollowTheTaskAndAForeignModelFallsBack()
    {
        var clear = new AiModelOption(new AiModelId("clear"), "Clear", null, true,
            Image: new AiImageModelCapabilities(
                AiCapabilityDimension<string>.Unspecified,
                AiCapabilityDimension<string>.Supported(["transparent"]), false, 1));
        var paint = new AiModelOption(new AiModelId("paint"), "Paint", null, true,
            Image: new AiImageModelCapabilities(
                AiCapabilityDimension<string>.Unspecified,
                AiCapabilityDimension<string>.Supported(["opaque"]), false, 1));
        var catalog = new AiGenerativeModelCatalog(
            new FixedCatalog(new AiModelCatalog(
            [
                KeyValuePair.Create(new AiOperationId("image.edit.remove_background"), ImmutableArray.Create(clear, paint)),
                KeyValuePair.Create(new AiOperationId("image.edit.restyle"), ImmutableArray.Create(paint)),
            ])),
            new StubEntitlements());
        var node = new AiImageEditNode();
        using var vm = CreateEditor(node.Model.Property!);
        EnumEditor editor = await ShowAsync(vm, catalog, e => e.Items.Count == 1);
        Assert.That(editor.Items.Select(i => i.Value), Is.EqualTo(new[] { "clear" }),
            "Removing a background needs a model that can make one transparent.");
        node.Model.Property!.SetValue("clear");

        node.Task.Property!.SetValue(AiImageEditTask.Restyle);
        for (int i = 0; i < 50 && !Equals(editor.Items.FirstOrDefault()?.Value, "paint"); i++)
        {
            await Task.Delay(10);
            HeadlessTestHelpers.Render(1);
        }

        Assert.That(editor.Items.Select(i => i.Value), Is.EqualTo(new[] { "paint" }));
        Assert.That(node.Model.Property!.GetValue(), Is.Empty, "Another task's model would be refused.");
    }

    [AvaloniaTest]
    public async Task VideoIsWaitedForAndSavedLeavingOutWhatTheModelDoesNotTake()
    {
        var videos = new FakeVideos(AiJobStatuses.Succeeded);
        AiGenerativeNodeExecutor executor = CreateVideoExecutor(videos);

        GenerativeExecutionResult result = await executor.ExecuteAsync(
            VideoRequest(duration: 4, audio: true, seed: 9),
            new Progress<GenerativeProgress>(),
            CancellationToken.None);

        AiVideoGenerationRequest sent = videos.Requests.Single();
        Assert.Multiple(() =>
        {
            Assert.That(sent.GenerateAudio, Is.False, "The model takes no audio.");
            Assert.That(sent.Seed, Is.Null, "The model takes no seed.");
            Assert.That(sent.DurationSeconds, Is.EqualTo(4));
            Assert.That(videos.Polls, Is.EqualTo(2), "Polled until the job succeeded.");
            Assert.That(result.IsVideo, Is.True);
            Assert.That(Path.GetDirectoryName(result.ResultFile.LocalPath),
                Is.EqualTo(Path.Combine(_directory, "resources", "ai")));
            Assert.That(File.ReadAllBytes(result.ResultFile.LocalPath), Is.EqualTo(FakeVideos.Clip));
        });
    }

    [AvaloniaTest]
    public async Task VideoLengthTheModelDoesNotOfferIsRefusedBeforeAnythingIsSent()
    {
        var videos = new FakeVideos(AiJobStatuses.Succeeded);
        AiGenerativeNodeExecutor executor = CreateVideoExecutor(videos);

        string? message = null;
        try
        {
            await executor.ExecuteAsync(VideoRequest(duration: 6, audio: false, seed: null),
                new Progress<GenerativeProgress>(), CancellationToken.None);
        }
        catch (GenerativeExecutionException ex)
        {
            message = ex.Message;
        }

        Assert.That(message, Is.EqualTo(Strings.AiModelDoesNotSupportRequest));
        Assert.That(videos.Requests, Is.Empty);
    }

    [AvaloniaTest]
    public async Task FailedVideoJobReportsTheServersReason()
    {
        var videos = new FakeVideos(AiJobStatuses.Failed);
        AiGenerativeNodeExecutor executor = CreateVideoExecutor(videos);

        string? message = null;
        bool settled = false;
        try
        {
            await executor.ExecuteAsync(VideoRequest(duration: 4, audio: false, seed: null),
                new Progress<GenerativeProgress>(), CancellationToken.None);
        }
        catch (GenerativeExecutionException ex)
        {
            message = ex.Message;
            settled = ex.SettledRequest;
        }

        Assert.That(message, Is.EqualTo(Strings.AiProviderError));
        Assert.That(settled, Is.True, "A failed, refunded job needs a new key to be tried again.");
    }

    [AvaloniaTest]
    public async Task VideoLengthMovesToTheNearestOneTheNewModelOffers()
    {
        AiModelOption Model(string id, bool isDefault, params int[] durations) => new(
            new AiModelId(id), id, null, isDefault,
            Video: new AiVideoModelCapabilities(
                AiCapabilityDimension<int>.Supported(durations),
                AiCapabilityDimension<string>.Unspecified,
                AiCapabilityDimension<string>.Unspecified,
                true,
                true));
        var catalog = new AiGenerativeModelCatalog(
            new FixedCatalog(new AiModelCatalog(
            [
                KeyValuePair.Create(AiOperations.VideoGeneration,
                    ImmutableArray.Create(Model("even", true, 4, 6, 8), Model("odd", false, 5, 10))),
            ])),
            new StubEntitlements());
        var node = new AiVideoGenerationNode();
        using var vm = CreateEditor(node.Duration.Property!);
        EnumEditor editor = await ShowAsync(vm, catalog, e => e.Items.Count == 3);
        Assert.That(editor.Items.Select(i => i.DisplayName).First(), Is.EqualTo($"4 {Strings.AiVideoSeconds}"));
        Assert.That(editor.SelectedIndex, Is.EqualTo(1), "The AI tab starts on 6 seconds.");

        node.Model.Property!.SetValue("odd");
        HeadlessTestHelpers.Render(3);

        Assert.That(node.Duration.Property!.GetValue(), Is.EqualTo(5));
        Assert.That(editor.Items.Select(i => i.Value), Is.EqualTo(new object[] { 5, 10 }));
    }

    [AvaloniaTest]
    public async Task EditKeepsTheClipsLengthAndExtendSendsTheChosenOne()
    {
        var videos = new FakeVideos(AiJobStatuses.Succeeded);
        AiGenerativeNodeExecutor executor = CreateVideoExecutor(videos, sourceSeconds: 4.2);

        await executor.ExecuteAsync(EditRequest(AiVideoEditMode.Edit, duration: 8), new Progress<GenerativeProgress>(), CancellationToken.None);
        await executor.ExecuteAsync(EditRequest(AiVideoEditMode.Extend, duration: 8), new Progress<GenerativeProgress>(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(videos.SourceRequests[0].Mode, Is.EqualTo(AiSourceVideoMode.Edit));
            Assert.That(videos.SourceRequests[0].DurationSeconds, Is.Null, "An edit keeps the clip's own length.");
            Assert.That(videos.SourceRequests[1].Mode, Is.EqualTo(AiSourceVideoMode.Extend));
            Assert.That(videos.SourceRequests[1].DurationSeconds, Is.EqualTo(8));
            Assert.That(videos.SourceRequests[1].CharacterImage, Is.Null);
        });
    }

    [AvaloniaTest]
    public async Task SourceClipLongerThanTheModelTakesIsRefusedBeforeAnythingIsSent()
    {
        var videos = new FakeVideos(AiJobStatuses.Succeeded);
        AiGenerativeNodeExecutor executor = CreateVideoExecutor(videos, sourceSeconds: 61);

        string? message = null;
        try
        {
            await executor.ExecuteAsync(EditRequest(AiVideoEditMode.Edit, duration: 8), new Progress<GenerativeProgress>(), CancellationToken.None);
        }
        catch (GenerativeExecutionException ex)
        {
            message = ex.Message;
        }

        Assert.That(message, Is.EqualTo(Strings.AiModelDoesNotSupportRequest));
        Assert.That(videos.SourceRequests, Is.Empty);
    }

    private static AiVideoEditNodeRequest EditRequest(AiVideoEditMode mode, int duration)
    {
        var node = new AiVideoEditNode();
        node.Task.Property!.SetValue(mode);
        return new AiVideoEditNodeRequest(node)
        {
            Mode = mode,
            Prompt = "make it rain",
            DurationSeconds = duration,
            SourceVideo = new GenerativeFileInput("source.mp4", "video/mp4", FakeVideos.Clip),
            RequestKeySeed = Guid.NewGuid().ToString("N"),
            ParameterFingerprint = "p",
        };
    }

    private AiGenerativeNodeExecutor CreateVideoExecutor(FakeVideos videos, double sourceSeconds = 4)
    {
        var quiet = new AiModelOption(new AiModelId("quiet"), "Quiet", null, true,
            Video: new AiVideoModelCapabilities(
                AiCapabilityDimension<int>.Supported([4, 8]),
                AiCapabilityDimension<string>.Unspecified,
                AiCapabilityDimension<string>.Unspecified,
                SupportsAudio: false,
                SupportsSeed: false));
        var catalog = new AiGenerativeModelCatalog(
            new FixedCatalog(new AiModelCatalog(
                new[]
                {
                    AiOperations.VideoGeneration, AiOperations.VideoEditing,
                    AiOperations.VideoExtension, AiOperations.VideoMotion,
                }.Select(op => KeyValuePair.Create(op, ImmutableArray.Create(quiet))))),
            new StubEntitlements());
        var kinds = new Moq.Mock<IAiJobKindRegistry>();
        kinds.Setup(x => x.GetStatus(Moq.It.IsAny<AiJobKindId>(), Moq.It.IsAny<AiJobStatusId>()))
            .Returns((AiJobKindId _, AiJobStatusId status) =>
                status == AiJobStatuses.Succeeded ? new AiJobStatusSemantics(true, false, AiJobOutcomes.Succeeded)
                : status == AiJobStatuses.Failed ? new AiJobStatusSemantics(true, false, AiJobOutcomes.Failed)
                : new AiJobStatusSemantics(false, true));
        var scene = new Scene(640, 480, "nodes") { Uri = new Uri(Path.Combine(_directory, "scene.scene")) };
        return new AiGenerativeNodeExecutor(
            scene, catalog, new CapturingImages(), new AlwaysAvailable(), new ClipContent(),
            videos: videos, jobKinds: kinds.Object)
        {
            PollInterval = TimeSpan.Zero,
            VideoDurationReader = _ => sourceSeconds,
        };
    }

    private static AiVideoGenerationNodeRequest VideoRequest(int duration, bool audio, int? seed)
        => new(new AiVideoGenerationNode())
        {
            Prompt = "the cat walks",
            DurationSeconds = duration,
            Resolution = "720p",
            AspectRatio = "16:9",
            GenerateAudio = audio,
            Seed = seed,
            RequestKeySeed = Guid.NewGuid().ToString("N"),
            ParameterFingerprint = "p",
        };

    private sealed class FakeVideos(AiJobStatusId outcome) : IAiVideoService
    {
        public static readonly byte[] Clip = [0, 0, 0, 24, 102, 116, 121, 112];

        public List<AiVideoGenerationRequest> Requests { get; } = [];

        public List<AiSourceVideoRequest> SourceRequests { get; } = [];

        public Task<AiVideoGenerationResult> CreateFromSourceAsync(AiSourceVideoRequest request, CancellationToken cancellationToken)
        {
            SourceRequests.Add(request);
            return Task.FromResult(new AiVideoGenerationResult(new AiJobId("job"), AiJobStatuses.Queued));
        }

        public int Polls { get; private set; }

        public Task<AiVideoGenerationResult> CreateAsync(AiVideoGenerationRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new AiVideoGenerationResult(new AiJobId("job"), AiJobStatuses.Queued));
        }

        public Task<AiVideoJob> GetAsync(AiJobId jobId, CancellationToken cancellationToken)
        {
            Polls++;
            AiJobStatusId status = Polls < 2 ? AiJobStatuses.Queued : outcome;
            return Task.FromResult(new AiVideoJob(
                jobId,
                status,
                null,
                status == AiJobStatuses.Succeeded ? new Uri("https://beutl.test/content/clip") : null,
                null,
                new AiContentMetadata("clip.mp4", "video/mp4")));
        }
    }

    private sealed class ClipContent : IAuthenticatedContentService
    {
        public async Task<AiContentDownload> CopyToAsync(Uri contentUri, Stream destination, CancellationToken cancellationToken)
        {
            await destination.WriteAsync(FakeVideos.Clip, cancellationToken);
            return new AiContentDownload(new AiContentMetadata("clip.mp4", "video/mp4"));
        }
    }

    private static AiImageGenerationNodeRequest Request(
        GenerativeNode node, string? model, string aspectRatio, int? seed = null)
        => new(node)
        {
            Prompt = "a cat",
            AspectRatio = aspectRatio,
            Seed = seed,
            ModelId = model,
            RequestKeySeed = Guid.NewGuid().ToString("N"),
            ParameterFingerprint = "p",
        };

    private AiGenerativeNodeExecutor CreateExecutor(
        CapturingImages images,
        IGenerativePromptLibrary? library = null,
        IAiImageEditingService? editing = null)
    {
        var scene = new Scene(640, 480, "nodes") { Uri = new Uri(Path.Combine(_directory, "scene.scene")) };
        return new AiGenerativeNodeExecutor(
            scene,
            CreateCatalog(),
            images,
            new AlwaysAvailable(),
            new PngContent(),
            library,
            editing);
    }

    private static AiGenerativeModelCatalog CreateCatalog()
    {
        var catalog = new AiModelCatalog(
        [
            KeyValuePair.Create(AiOperations.ImageGeneration, ImmutableArray.Create(Wide, Square)),
        ]);
        return new AiGenerativeModelCatalog(new FixedCatalog(catalog), new StubEntitlements());
    }

    private static GenerativeChoiceEditorViewModel CreateEditor(IPropertyAdapter property)
    {
        Assert.That(
            GenerativeChoicePropertyExtension.Instance.TryCreateContextForNode([property], out IPropertyEditorContext? context),
            Is.True);
        return (GenerativeChoiceEditorViewModel)context!;
    }

    private static async Task<EnumEditor> ShowAsync(
        GenerativeChoiceEditorViewModel vm,
        IGenerativeModelCatalog catalog,
        Func<EnumEditor, bool>? ready = null)
    {
        ready ??= editor => editor.Items.Count > 1;
        vm.Accept(new CatalogVisitor(catalog));
        Assert.That(GenerativeChoicePropertyExtension.Instance.TryCreateControlForNode(vm, out var control), Is.True);
        var editor = (EnumEditor)control!;
        for (int i = 0; i < 50 && !ready(editor); i++)
        {
            await Task.Delay(10);
            HeadlessTestHelpers.Render(1);
        }

        return editor;
    }

    private sealed class CatalogVisitor(IGenerativeModelCatalog catalog) : IPropertyEditorContextVisitor, IServiceProvider
    {
        public object? GetService(Type serviceType)
            => serviceType == typeof(IGenerativeModelCatalog) ? catalog : null;

        public void Visit(IPropertyEditorContext context)
        {
        }
    }

    private sealed class RecordingLibrary : IGenerativePromptLibrary
    {
        public List<string> Recorded { get; } = [];

        public IReadOnlyList<GenerativePromptEntry> GetEntries(GenerativeOperation operation) => [];

        public void Record(GenerativeOperation operation, string prompt) => Recorded.Add(prompt);

        public string? SaveTemplate(GenerativeOperation operation, string name, string prompt) => null;
    }

    private sealed class FixedPromptStore(
        IReadOnlyList<PromptTemplate> templates,
        IReadOnlyList<PromptHistoryEntry> history) : IPromptLibrary
    {
        public string StoragePath => string.Empty;
        public bool RetainRecentPromptText => true;
        public string? RecoveredCorruptFilePath => null;
        public IReadOnlyList<PromptHistoryEntry> History => history;
        public IReadOnlyList<PromptTemplate> Templates => templates;
        public PromptHistoryEntry Record(PromptTaskKind taskKind, string prompt) => throw new NotSupportedException();
        public PromptTemplate SaveTemplate(string name, PromptTaskKind taskKind, string prompt) => throw new NotSupportedException();
        public bool SetHistoryPinned(Guid id, bool isPinned) => false;
        public bool SetTemplatePinned(Guid id, bool isPinned) => false;
        public bool DeleteHistory(Guid id) => false;
        public bool DeleteTemplate(Guid id) => false;
        public void ClearHistory() { }
        public void ClearTemplates() { }
        public void ClearAll() { }
    }

    private sealed class FixedCatalog(AiModelCatalog catalog) : IAiModelCatalogService
    {
        public Task<AiModelCatalog> GetAsync(CancellationToken cancellationToken) => Task.FromResult(catalog);

        public void Invalidate()
        {
        }
    }

    private sealed class StubEntitlements : IAiEntitlementService
    {
        public IReadOnlyReactiveProperty<AiEntitlements?> Entitlements { get; } =
            new ReactivePropertySlim<AiEntitlements?>(new AiEntitlements(
                "pro", "active", null, null, false, true,
                new AiBalance(new AiMonthlyUsage(0, 100, false), 0, false),
                new AiOperationAvailability([])));

        public Task<AiEntitlements?> RefreshAsync(CancellationToken cancellationToken)
            => Task.FromResult(Entitlements.Value);
    }

    private sealed class AlwaysAvailable : IAiOperationAvailabilityService
    {
        public Task<bool> CheckAsync(AiOperationAvailabilityRequest request, CancellationToken cancellationToken)
            => Task.FromResult(true);
    }

    private sealed class CapturingImages : IAiImageGenerationService
    {
        public List<AiImageGenerationRequest> Requests { get; } = [];

        public Task<AiImageResult> GenerateAsync(AiImageGenerationRequest request, CancellationToken cancellationToken)
            => GenerateAsync(request, null, cancellationToken);

        public Task<AiImageResult> GenerateAsync(
            AiImageGenerationRequest request,
            IProgress<AiImagePreview>? progress,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new AiImageResult(null, new AiContentId("file"), new Uri("https://beutl.test/content/file")));
        }
    }

    private sealed class CapturingEditing : IAiImageEditingService
    {
        public List<AiImageEditRequest> Requests { get; } = [];

        public Task<AiImageResult> EditAsync(AiImageEditRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new AiImageResult(null, new AiContentId("file"), new Uri("https://beutl.test/content/file")));
        }
    }

    private sealed class PngContent : IAuthenticatedContentService
    {
        public Task<AiContentDownload> CopyToAsync(Uri contentUri, Stream destination, CancellationToken cancellationToken)
        {
            using var bitmap = new Bitmap(4, 4);
            bitmap.Save(destination, EncodedImageFormat.Png);
            return Task.FromResult(new AiContentDownload(null));
        }
    }
}
