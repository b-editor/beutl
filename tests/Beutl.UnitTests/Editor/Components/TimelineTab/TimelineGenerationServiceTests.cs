using Beutl.Collections;
using Beutl.Editor;
using Beutl.Editor.Components.TimelineTab.Generative;
using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.Editor.Services.AI;
using Beutl.Graphics;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.NodeGraph.Generative;
using Beutl.ProjectSystem;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Editor.Components.TimelineTab;

[TestFixture]
public sealed class TimelineGenerationServiceTests
{
    private SceneHistoryHarness _harness = null!;
    private Scene _scene = null!;
    private string _resources = null!;
    private FakeExecutor _executor = null!;
    private FakeProbe _probe = null!;
    private TimelineGenerationService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _harness = new SceneHistoryHarness("beutl_ai_timeline", start: TimeSpan.Zero, duration: TimeSpan.FromSeconds(120));
        _scene = _harness.Scene;
        _resources = Path.Combine(_harness.BasePath, "resources", "ai");
        Directory.CreateDirectory(_resources);
        _executor = new FakeExecutor();
        _probe = new FakeProbe();
        _service = new TimelineGenerationService(
            _scene,
            _harness.History,
            new FakeAdder(_scene, _harness.History),
            () => _executor,
            () => _resources,
            _probe);
    }

    [TearDown]
    public void TearDown()
    {
        _service.Dispose();
        _harness.Dispose();
    }

    [Test]
    public async Task AnEditThatKeepsTheShapeReplacesThePictureAndKeepsTheOriginalAsATake()
    {
        string original = WritePng("original.png", 200, 100);
        Element element = AddImage(original, zIndex: 0);
        _executor.Result = WritePng("result.png", 400, 200);

        TimelineGenerationJob job = _service.Create(
            TimelineGenerationTarget.ForElement(TimelineGenerationPlacement.ReplaceImage, element),
            new TimelineGenerationSpec { Kind = TimelineGenerationKind.ImageEdit, ImageTask = AiImageEditTask.RemoveBackground },
            new TimelineGenerationInputs { ImagePath = original });
        await _service.RunAsync(job);

        SourceImage image = element.Objects.OfType<SourceImage>().Single();
        string shown = image.Source.CurrentValue!.Uri.LocalPath;
        Assert.Multiple(() =>
        {
            Assert.That(_service.Jobs, Is.Empty);
            Assert.That(shown, Is.Not.EqualTo(original));
            Assert.That(ReadSize(shown), Is.EqualTo(new PixelSize(200, 100)), "resampled to the size the picture showed at");
            Assert.That(element.Generation?.Takes.Count, Is.EqualTo(2));
            Assert.That(element.Generation?.Takes[0].IsOriginal, Is.True);
            Assert.That(element.Generation?.Takes[0].Image?.Uri.LocalPath, Is.EqualTo(original));
            Assert.That(element.Generation?.ActiveTake, Is.SameAs(element.Generation?.Takes[1]));
            Assert.That(_scene.Children, Has.Count.EqualTo(1));
        });

        _harness.History.Undo();
        Assert.That(image.Source.CurrentValue!.Uri.LocalPath, Is.EqualTo(original));
        Assert.That(element.Generation, Is.Null);
    }

    [Test]
    public async Task AResultOfAnotherShapeIsAddedAboveInsteadOfStretched()
    {
        string original = WritePng("original.png", 200, 100);
        Element element = AddImage(original, zIndex: 0);
        _executor.Result = WritePng("square.png", 100, 100);

        TimelineGenerationJob job = _service.Create(
            TimelineGenerationTarget.ForElement(TimelineGenerationPlacement.ReplaceImage, element),
            new TimelineGenerationSpec { Kind = TimelineGenerationKind.ImageEdit, ImageTask = AiImageEditTask.Restyle, Prompt = "ink" },
            new TimelineGenerationInputs { ImagePath = original });
        await _service.RunAsync(job);

        Element added = _scene.Children.Single(child => !ReferenceEquals(child, element));
        Assert.Multiple(() =>
        {
            Assert.That(element.Objects.OfType<SourceImage>().Single().Source.CurrentValue!.Uri.LocalPath, Is.EqualTo(original));
            Assert.That(added.ZIndex, Is.EqualTo(1));
            Assert.That(added.Range, Is.EqualTo(element.Range));
            Assert.That(added.Generation?.ActiveTake?.Image?.Uri.LocalPath, Is.EqualTo(_executor.Result));
        });
    }

    [Test]
    public async Task AnUpscaleIsAddedAboveAtTheSizeTheOriginalShowedAt()
    {
        string original = WritePng("original.png", 100, 50);
        Element element = AddImage(original, zIndex: 0);
        _executor.Result = WritePng("upscaled.png", 400, 200);

        TimelineGenerationJob job = _service.Create(
            TimelineGenerationTarget.ForElement(TimelineGenerationPlacement.Above, element),
            new TimelineGenerationSpec { Kind = TimelineGenerationKind.ImageEdit, ImageTask = AiImageEditTask.Upscale },
            new TimelineGenerationInputs { ImagePath = original });
        await _service.RunAsync(job);

        Element added = _scene.Children.Single(child => !ReferenceEquals(child, element));
        SourceImage image = added.Objects.OfType<SourceImage>().Single();
        // The correction is applied first, so it is the group's last child.
        Transform transform = image.Transform.CurrentValue!;
        var scale = (ScaleTransform)(transform is TransformGroup group ? group.Children[^1] : transform);
        Assert.Multiple(() =>
        {
            Assert.That(scale.ScaleX.CurrentValue, Is.EqualTo(25f));
            Assert.That(scale.ScaleY.CurrentValue, Is.EqualTo(25f));
        });
    }

    [Test]
    public async Task TheUpscaleCorrectionScalesThePictureBeforeTheElementMovesIt()
    {
        string original = WritePng("original.png", 100, 50);
        Element element = AddImage(original, zIndex: 0);
        var group = (TransformGroup)element.Objects.OfType<SourceImage>().Single().Transform.CurrentValue!;
        group.Children.Add(new TranslateTransform(100, 0));
        _executor.Result = WritePng("upscaled.png", 400, 200);

        await _service.RunAsync(_service.Create(
            TimelineGenerationTarget.ForElement(TimelineGenerationPlacement.Above, element),
            new TimelineGenerationSpec { Kind = TimelineGenerationKind.ImageEdit, ImageTask = AiImageEditTask.Upscale },
            new TimelineGenerationInputs { ImagePath = original }));

        Element added = _scene.Children.Single(child => !ReferenceEquals(child, element));
        Transform transform = added.Objects.OfType<SourceImage>().Single().Transform.CurrentValue!;
        Matrix matrix = transform.CreateMatrix(new Beutl.Composition.CompositionContext(TimeSpan.Zero));
        // A point 400 px into the upscaled picture lands 100 px in, then moves with the element.
        Assert.That(new Point(400, 0).Transform(matrix), Is.EqualTo(new Point(200, 0)));
    }

    [Test]
    public async Task AClipForAGapFillsItAndIsCutToItsLength()
    {
        _harness.AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(4), zIndex: 0);
        _harness.AddElement(TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(4), zIndex: 0);
        TimelineGap gap = TimelineGenerationSlots.FindGap(_scene, 0, TimeSpan.FromSeconds(5))!;
        _executor.Result = WriteFile("clip.mp4");
        _probe.Durations[_executor.Result] = TimeSpan.FromSeconds(4);

        TimelineGenerationJob job = _service.Create(
            TimelineGenerationTarget.ForGap(gap),
            new TimelineGenerationSpec { Kind = TimelineGenerationKind.Video, Prompt = "waves", DurationSeconds = 4 },
            new TimelineGenerationInputs());
        Assert.That(job.Slot.Value, Is.EqualTo(new TimelineGenerationSlot(0, new TimeRange(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(3)))));
        await _service.RunAsync(job);

        Element clip = _scene.Children.Single(child => child.Generation is not null);
        Assert.Multiple(() =>
        {
            Assert.That(clip.ZIndex, Is.EqualTo(0));
            Assert.That(clip.Start, Is.EqualTo(TimeSpan.FromSeconds(4)));
            Assert.That(clip.Length, Is.EqualTo(TimeSpan.FromSeconds(3)));
            Assert.That(clip.Generation!.Operation, Is.EqualTo("video.generate"));
            Assert.That(TimelineGenerationSpec.FromJson(clip.Generation.Parameters)?.Prompt, Is.EqualTo("waves"));
        });
    }

    [Test]
    public async Task AnExtensionShowsOnlyWhatWasAddedRightAfterTheClip()
    {
        string sourceClip = WriteFile("source.mp4");
        Element source = _harness.AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(5), zIndex: 2);
        _executor.Result = WriteFile("extended.mp4");
        _probe.Durations[sourceClip] = TimeSpan.FromSeconds(5);
        _probe.Durations[_executor.Result] = TimeSpan.FromSeconds(9);

        TimelineGenerationJob job = _service.Create(
            TimelineGenerationTarget.ForElement(TimelineGenerationPlacement.After, source),
            new TimelineGenerationSpec { Kind = TimelineGenerationKind.VideoExtend, Prompt = "more", DurationSeconds = 4 },
            new TimelineGenerationInputs { VideoPath = sourceClip });
        await _service.RunAsync(job);

        Element clip = _scene.Children.Single(child => child.Generation is not null);
        SourceVideo video = clip.Objects.OfType<SourceVideo>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(clip.ZIndex, Is.EqualTo(2));
            Assert.That(clip.Start, Is.EqualTo(TimeSpan.FromSeconds(5)));
            Assert.That(clip.Length, Is.EqualTo(TimeSpan.FromSeconds(4)));
            Assert.That(video.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(5)));
        });
    }

    [Test]
    public async Task AFailureStaysOnTheJobAndASettledOneGetsANewRequestName()
    {
        string original = WritePng("original.png", 10, 10);
        Element element = AddImage(original, zIndex: 0);
        TimelineGenerationJob job = _service.Create(
            TimelineGenerationTarget.ForElement(TimelineGenerationPlacement.ReplaceImage, element),
            new TimelineGenerationSpec { Kind = TimelineGenerationKind.ImageEdit, ImageTask = AiImageEditTask.RemoveBackground },
            new TimelineGenerationInputs { ImagePath = original });

        _executor.Failure = new GenerativeExecutionException("not now");
        await _service.RunAsync(job);
        string kept = _executor.Requests.Last().RequestKeySeed;
        _executor.Failure = new GenerativeExecutionException("refunded") { SettledRequest = true };
        await _service.RunAsync(job);
        string settled = _executor.Requests.Last().RequestKeySeed;
        _executor.Failure = null;
        _executor.Result = WritePng("result.png", 10, 10);
        await _service.RunAsync(job);

        Assert.Multiple(() =>
        {
            Assert.That(settled, Is.EqualTo(kept), "an unsettled failure is retried under the same name");
            Assert.That(_executor.Requests.Last().RequestKeySeed, Is.Not.EqualTo(settled));
            Assert.That(_service.Jobs, Is.Empty);
        });
    }

    [Test]
    public async Task RegeneratingAddsATakeAndSwitchingBackIsUndoable()
    {
        string original = WritePng("original.png", 20, 10);
        Element element = AddImage(original, zIndex: 0);
        _executor.Result = WritePng("first.png", 20, 10);
        var spec = new TimelineGenerationSpec { Kind = TimelineGenerationKind.ImageEdit, ImageTask = AiImageEditTask.RemoveBackground };
        await _service.RunAsync(_service.Create(
            TimelineGenerationTarget.ForElement(TimelineGenerationPlacement.ReplaceImage, element),
            spec,
            new TimelineGenerationInputs { ImagePath = original }));
        _executor.Result = WritePng("second.png", 20, 10);

        await _service.RunAsync(_service.Create(
            TimelineGenerationTarget.ForElement(TimelineGenerationPlacement.NewTake, element),
            spec,
            new TimelineGenerationInputs { ImagePath = original }));

        ElementGeneration generation = element.Generation!;
        SourceImage image = element.Objects.OfType<SourceImage>().Single();
        Assert.That(generation.Takes, Has.Count.EqualTo(3));
        Assert.That(image.Source.CurrentValue!.Uri.LocalPath, Is.EqualTo(_executor.Result));

        _service.SelectTake(element, generation.Takes[0]);
        Assert.That(image.Source.CurrentValue!.Uri.LocalPath, Is.EqualTo(original));
        _harness.History.Undo();
        Assert.That(image.Source.CurrentValue!.Uri.LocalPath, Is.EqualTo(_executor.Result));
    }

    private Element AddImage(string path, int zIndex)
    {
        Element element = _harness.AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), zIndex);
        var image = new SourceImage();
        image.Source.CurrentValue = ImageSource.Open(path);
        element.AddObject(image);
        _harness.History.Commit("image");
        return element;
    }

    private string WritePng(string name, int width, int height)
    {
        string path = Path.Combine(_resources, $"{Guid.NewGuid():N}-{name}");
        using var bitmap = new Bitmap(width, height);
        Assert.That(bitmap.Save(path, EncodedImageFormat.Png), Is.True);
        return path;
    }

    private string WriteFile(string name)
    {
        string path = Path.Combine(_resources, $"{Guid.NewGuid():N}-{name}");
        File.WriteAllBytes(path, [0, 1, 2, 3]);
        return path;
    }

    private static PixelSize ReadSize(string path)
    {
        using var bitmap = Bitmap.FromFile(path);
        return new PixelSize(bitmap.Width, bitmap.Height);
    }

    private sealed class FakeExecutor : IGenerativeNodeExecutor
    {
        public string Result { get; set; } = string.Empty;

        public GenerativeExecutionException? Failure { get; set; }

        public List<GenerativeRequest> Requests { get; } = [];

        public Task<GenerativeExecutionResult> ExecuteAsync(
            GenerativeRequest request,
            IProgress<GenerativeProgress> progress,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (Failure is { } failure)
                throw failure;
            return Task.FromResult(new GenerativeExecutionResult(new Uri(Result), "model", 7));
        }
    }

    private sealed class FakeProbe : ITimelineMediaProbe
    {
        public Dictionary<string, TimeSpan> Durations { get; } = [];

        public TimeSpan? GetVideoDuration(string path) => Durations.TryGetValue(path, out TimeSpan duration) ? duration : null;

        public bool HasAudio(string path) => false;
    }

    private sealed class NoRegistry : IElementSourceHandlerRegistry
    {
        public ICoreReadOnlyList<ElementSourceHandlerDescriptor> Handlers { get; } = new CoreList<ElementSourceHandlerDescriptor>();

        public IElementSourceHandlerRegistration Register(ElementSourceHandlerRegistration registration) => new Registration();

        public bool TryAcquire(Type sourceType, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IElementSourceHandlerLease? lease)
        {
            lease = null;
            return false;
        }

        private sealed class Registration : IElementSourceHandlerRegistration
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    // Materializes through the generation's own handler and adds in one history step, as the
    // editor's adder does.
    private sealed class FakeAdder(Scene scene, HistoryManager history) : IElementAdder
    {
        private readonly GeneratedElementsSourceHandler _handler = new();

        public IElementSourceHandlerRegistry SourceHandlers { get; } = new NoRegistry();

        public async ValueTask<ElementAddResult> AddAsync(
            IReadOnlyList<ElementDescription> descriptions,
            CancellationToken cancellationToken)
        {
            var elements = new List<Element>();
            var groups = new List<IReadOnlySet<Guid>>();
            var items = new List<ElementAddItemResult>();
            foreach (ElementDescription description in descriptions)
            {
                ElementSourcePreflightResult preflight = await _handler.PreflightAsync(
                    new ElementSourcePreflightContext(scene, description),
                    cancellationToken);
                ElementSourceMaterializationResult materialized = await _handler.MaterializeAsync(
                    new ElementSourceMaterializationContext(scene, description),
                    preflight.Preflight!,
                    cancellationToken);
                ElementMaterialization materialization = materialized.Materialization!;
                elements.AddRange(materialization.Elements);
                groups.AddRange(materialization.Groups);
                items.Add(new ElementAddItemResult(
                    description,
                    materialization.PrimaryElement,
                    materialization.CompanionElements));
            }

            history.ExecuteInTransaction(() =>
            {
                foreach (Element element in elements)
                    scene.AddChild(element);
                foreach (IReadOnlySet<Guid> group in groups)
                    scene.Groups.Add([.. group]);
            });
            return ElementAddResult.Succeeded(items);
        }
    }
}
