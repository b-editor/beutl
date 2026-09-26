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
        EnumEditor editor = await ShowAsync(vm, CreateCatalog());

        Assert.That(editor.Items.Select(item => item.DisplayName), Is.EqualTo(new[]
        {
            NodeGraphStrings.Generative_ModelDefault,
            $"Wide — {Strings.AiModelCostLow}",
            $"Square — {Strings.AiModelCostHigh}",
        }));
        Assert.That(editor.SelectedIndex, Is.Zero, "An empty model is the default model.");
    }

    [AvaloniaTest]
    public async Task ChoosingANarrowerModelNarrowsAndResetsTheAspectRatio()
    {
        var node = new AiImageGenerationNode();
        node.AspectRatio.Property!.SetValue("16:9");
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

        // Awaited rather than Assert.ThrowsAsync: that blocks the UI thread, and a run that
        // wrongly went on to save a result would deadlock instead of failing.
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

    private AiGenerativeNodeExecutor CreateExecutor(CapturingImages images)
    {
        var scene = new Scene(640, 480, "nodes") { Uri = new Uri(Path.Combine(_directory, "scene.scene")) };
        return new AiGenerativeNodeExecutor(
            scene,
            CreateCatalog(),
            images,
            new AlwaysAvailable(),
            new PngContent());
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
