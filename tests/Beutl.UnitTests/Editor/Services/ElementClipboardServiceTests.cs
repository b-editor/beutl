using Beutl.Editor;
using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Editor.Services;

[TestFixture]
public class ElementClipboardServiceTests
{
    private SceneHistoryHarness _harness = null!;
    private Scene _scene = null!;
    private HistoryManager _history = null!;
    private InMemoryClipboardGateway _clipboard = null!;
    private ElementDuplicateService _duplicateService = null!;
    private ElementClipboardService _service = null!;

    [SetUp]
    public void Setup()
    {
        _harness = new SceneHistoryHarness("beutl_clip", start: TimeSpan.Zero, duration: TimeSpan.FromSeconds(60));
        _scene = _harness.Scene;
        _history = _harness.History;
        _clipboard = new InMemoryClipboardGateway();
        _duplicateService = new ElementDuplicateService(_history);
        _service = CreateService();
    }

    private ElementClipboardService CreateService(IElementAdder? elementAdder = null)
    {
        return new ElementClipboardService(
            _history,
            _clipboard,
            _duplicateService,
            imageAccentColorFactory: () => Colors.Magenta,
            elementAdder);
    }

    [TearDown]
    public void TearDown()
    {
        _harness.Dispose();
    }

    private Element AddElement(TimeSpan start, TimeSpan length, int zIndex = 0)
        => _harness.AddElement(start, length, zIndex);

    [Test]
    public async Task CopyAsync_SingleElement_WritesElementFormat()
    {
        Element element = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));

        await _service.CopyAsync([element]);

        IReadOnlyList<string> formats = await _clipboard.GetFormatsAsync();
        Assert.That(formats, Does.Contain(BeutlClipboardFormats.Element));
    }

    [Test]
    public async Task CopyAsync_MultipleElements_WritesElementsFormat()
    {
        Element e1 = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        Element e2 = AddElement(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2));

        await _service.CopyAsync([e1, e2]);

        IReadOnlyList<string> formats = await _clipboard.GetFormatsAsync();
        Assert.Multiple(() =>
        {
            Assert.That(formats, Does.Contain(BeutlClipboardFormats.Element));
            Assert.That(formats, Does.Contain(BeutlClipboardFormats.Elements));
        });
    }

    [Test]
    public async Task CopyAsync_EmptyList_NoOp()
    {
        await _service.CopyAsync([]);

        IReadOnlyList<string> formats = await _clipboard.GetFormatsAsync();
        Assert.That(formats, Is.Empty);
    }

    [Test]
    public async Task CutAsync_RemovesElementAndCommitsOnce()
    {
        Element element = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        int before = _history.UndoCount;

        bool result = await _service.CutAsync(_scene, [element]);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.True);
            Assert.That(_scene.Children, Does.Not.Contain(element));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public async Task CutAsync_PrunesCutIdsFromGroups()
    {
        Element e1 = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        Element e2 = AddElement(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2));
        Element e3 = AddElement(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(2));
        _scene.Groups.Add([e1.Id, e2.Id, e3.Id]);

        bool result = await _service.CutAsync(_scene, [e1]);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.True);
            Assert.That(_scene.Groups, Has.Count.EqualTo(1));
            Assert.That(_scene.Groups[0], Does.Not.Contain(e1.Id));
            Assert.That(_scene.Groups[0], Is.EquivalentTo(new[] { e2.Id, e3.Id }));
        });
    }

    [Test]
    public async Task CutAsync_LockedElement_IsPreserved()
    {
        Element locked = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        locked.IsLocked = true;
        int before = _history.UndoCount;

        bool result = await _service.CutAsync(_scene, [locked]);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.False);
            Assert.That(_scene.Children, Does.Contain(locked));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public async Task CutAsync_LockDuringClipboardWrite_PreservesElement()
    {
        Element element = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        int before = _history.UndoCount;
        // The clip is locked while the clipboard write is in-flight, after the pre-await filter.
        _clipboard.OnSetAsync = () => element.IsLocked = true;

        bool result = await _service.CutAsync(_scene, [element]);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.True, "the copy succeeded, so the clip stays available to paste");
            Assert.That(_scene.Children, Does.Contain(element), "a clip locked mid-cut is not removed");
            Assert.That(_history.UndoCount, Is.EqualTo(before), "nothing removed, so no commit");
        });
    }

    [Test]
    public async Task PasteAsync_SingleElementFormat_LockDuringClipboardRead_IsRefused()
    {
        Element original = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        string json = CoreSerializer.SerializeToJsonString(original);
        _clipboard.SetSingle(BeutlClipboardFormats.Element, json);
        int childrenBefore = _scene.Children.Count;
        // The destination row is locked while the clipboard read is in-flight, after the pre-await guard.
        _clipboard.OnTryGetString = () => _scene.Layers.Add(new TimelineLayer { ZIndex = 1, IsLocked = true });

        ElementPasteOutcome outcome = await _service.PasteAsync(_scene, TimeSpan.FromSeconds(10), 1);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Pasted, Is.False, "a row locked mid-paste is refused");
            Assert.That(_scene.Children.Count, Is.EqualTo(childrenBefore), "no element added");
        });
    }

    [Test]
    public async Task PasteAsync_Bitmap_LockDuringClipboardRead_IsRefused()
    {
        _clipboard.SetBitmap([1, 2, 3, 4]);
        int childrenBefore = _scene.Children.Count;
        // The destination row is locked while the clipboard read is in-flight, after the pre-await guard.
        _clipboard.OnTryGetBitmap = () => _scene.Layers.Add(new TimelineLayer { ZIndex = 2, IsLocked = true });

        ElementPasteOutcome outcome = await _service.PasteAsync(_scene, TimeSpan.FromSeconds(5), 2);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Pasted, Is.False, "a row locked mid-paste is refused");
            Assert.That(_scene.Children.Count, Is.EqualTo(childrenBefore), "no element added");
        });
    }

    [Test]
    public async Task PasteAsync_NoMatchingFormat_ReturnsEmpty()
    {
        ElementPasteOutcome outcome = await _service.PasteAsync(_scene, TimeSpan.Zero, 0);

        Assert.That(outcome.Pasted, Is.False);
    }

    [Test]
    public async Task PasteAsync_FilesFormat_WhenElementAdderRefuses_ReturnsEmpty()
    {
        var elementAdder = new StubElementAdder([]);
        ElementClipboardService service = CreateService(elementAdder);
        _clipboard.SetFiles(["first.png", "second.png"]);

        ElementPasteOutcome outcome = await service.PasteAsync(
            _scene,
            TimeSpan.FromSeconds(12),
            4);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Pasted, Is.False);
            Assert.That(outcome.AddFailure, Is.TypeOf<ElementMaterializationFailure>());
            Assert.That(elementAdder.CallCount, Is.EqualTo(1));
            Assert.That(elementAdder.Descriptions, Has.Count.EqualTo(2));
            Assert.That(elementAdder.Descriptions, Has.All.Matches<ElementDescription>(description =>
                description.Start == TimeSpan.FromSeconds(12)
                && description.Length == TimeSpan.FromSeconds(5)
                && description.Layer == 4
                && description.Source is ElementSource.File));
        });
    }

    [Test]
    public async Task PasteAsync_FilesFormat_WhenElementAdderSucceeds_ReturnsCreatedElements()
    {
        Element[] created =
        [
            new Element
            {
                Start = TimeSpan.FromSeconds(12),
                Length = TimeSpan.FromSeconds(5),
                ZIndex = 4,
            },
            new Element
            {
                Start = TimeSpan.FromSeconds(12),
                Length = TimeSpan.FromSeconds(7),
                ZIndex = 5,
            },
        ];
        var elementAdder = new StubElementAdder(created);
        ElementClipboardService service = CreateService(elementAdder);
        _clipboard.SetFiles(["video.mp4"]);

        ElementPasteOutcome outcome = await service.PasteAsync(
            _scene,
            TimeSpan.FromSeconds(12),
            4);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Pasted, Is.True);
            Assert.That(outcome.NewElements, Is.EqualTo(created));
            Assert.That(outcome.ScrollTo, Is.EqualTo(created[^1].Range));
            Assert.That(outcome.ScrollToZIndex, Is.EqualTo(created[^1].ZIndex));
            Assert.That(elementAdder.CallCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task PasteAsync_SingleElementFormat_AddsClonedElement()
    {
        Element original = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        string json = CoreSerializer.SerializeToJsonString(original);
        _clipboard.SetSingle(BeutlClipboardFormats.Element, json);
        int childrenBefore = _scene.Children.Count;
        int historyBefore = _history.UndoCount;

        ElementPasteOutcome outcome = await _service.PasteAsync(_scene, TimeSpan.FromSeconds(10), 1);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Pasted, Is.True);
            Assert.That(_scene.Children.Count, Is.EqualTo(childrenBefore + 1));
            Assert.That(_history.UndoCount, Is.EqualTo(historyBefore + 1));
            Assert.That(outcome.NewElements, Has.Count.EqualTo(1));
            Assert.That(outcome.NewElements[0].Start, Is.EqualTo(TimeSpan.FromSeconds(10)));
            Assert.That(outcome.NewElements[0].ZIndex, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task PasteAsync_SingleElementFormat_LockedTargetLayer_IsRefused()
    {
        Element original = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        string json = CoreSerializer.SerializeToJsonString(original);
        _clipboard.SetSingle(BeutlClipboardFormats.Element, json);
        _scene.Layers.Add(new TimelineLayer { ZIndex = 1, IsLocked = true });
        int childrenBefore = _scene.Children.Count;

        ElementPasteOutcome outcome = await _service.PasteAsync(_scene, TimeSpan.FromSeconds(10), 1);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Pasted, Is.False);
            Assert.That(_scene.Children.Count, Is.EqualTo(childrenBefore));
        });
    }

    [Test]
    public async Task CutAsync_ClipboardUnavailable_PreservesScene()
    {
        Element element = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        int childrenBefore = _scene.Children.Count;
        int historyBefore = _history.UndoCount;
        _clipboard.SimulateUnavailable = true;

        bool result = await _service.CutAsync(_scene, [element]);

        // Guard against data loss: a failed clipboard write must not let Cut remove + commit.
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.False);
            Assert.That(_scene.Children, Does.Contain(element));
            Assert.That(_scene.Children.Count, Is.EqualTo(childrenBefore));
            Assert.That(_history.UndoCount, Is.EqualTo(historyBefore));
        });
    }

    [Test]
    public async Task PasteAsync_ElementsFormat_RespectsClickedPosition()
    {
        Element e1 = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), 0);
        Element e2 = AddElement(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(2), 0);
        var array = new System.Text.Json.Nodes.JsonArray(
            CoreSerializer.SerializeToJsonObject(e1),
            CoreSerializer.SerializeToJsonObject(e2));
        _clipboard.SetSingle(BeutlClipboardFormats.Elements, array.ToJsonString());

        TimeSpan clicked = TimeSpan.FromSeconds(20);
        int clickedLayer = 3;

        ElementPasteOutcome outcome = await _service.PasteAsync(_scene, clicked, clickedLayer);

        // Spiral search anchors at (clickedFrame, clickedLayer); empty area means duplicates land
        // exactly there. Regression: copies used to land at (0s, layer 0) regardless of the click.
        Assert.Multiple(() =>
        {
            Assert.That(outcome.Pasted, Is.True);
            Assert.That(outcome.ScrollTo.Start, Is.EqualTo(clicked));
            Assert.That(outcome.ScrollToZIndex, Is.EqualTo(clickedLayer));
        });
    }

    [Test]
    public async Task PasteAsync_ElementsFormat_LockedTargetLayer_IsRefused()
    {
        Element e1 = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), 0);
        Element e2 = AddElement(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(2), 0);
        var array = new System.Text.Json.Nodes.JsonArray(
            CoreSerializer.SerializeToJsonObject(e1),
            CoreSerializer.SerializeToJsonObject(e2));
        _clipboard.SetSingle(BeutlClipboardFormats.Elements, array.ToJsonString());
        _scene.Layers.Add(new TimelineLayer { ZIndex = 3, IsLocked = true });
        int childrenBefore = _scene.Children.Count;

        ElementPasteOutcome outcome = await _service.PasteAsync(_scene, TimeSpan.FromSeconds(20), 3);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Pasted, Is.False);
            Assert.That(_scene.Children.Count, Is.EqualTo(childrenBefore));
        });
    }

    [Test]
    public async Task CopyAsync_ExposesPlainTextAlongsideElementJson()
    {
        Element element = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));

        await _service.CopyAsync([element]);

        // Both JSON and the platform text slot must carry the payload; "text/plain" used to be dropped.
        IReadOnlyList<string> formats = await _clipboard.GetFormatsAsync();
        Assert.Multiple(() =>
        {
            Assert.That(formats, Contains.Item(BeutlClipboardFormats.Element));
            Assert.That(formats, Contains.Item(BeutlClipboardFormats.Text));
        });
    }

    private sealed class StubElementAdder(IReadOnlyList<Element> result) : IElementAdder
    {
        public IElementSourceHandlerRegistry SourceHandlers { get; } = new ElementSourceHandlerRegistry();

        public int CallCount { get; private set; }

        public IReadOnlyList<ElementDescription> Descriptions { get; private set; } = [];

        public ValueTask<ElementAddResult> AddAsync(
            IReadOnlyList<ElementDescription> descriptions,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            Descriptions = descriptions;
            if (result.Count == 0)
            {
                return ValueTask.FromResult(ElementAddResult.Failed(
                    new ElementMaterializationFailure("The test element could not be materialized.")));
            }

            ElementAddItemResult[] items = result
                .Select((element, index) =>
                {
                    ElementDescription description = descriptions[Math.Min(index, descriptions.Count - 1)];
                    return new ElementAddItemResult(description, element, []);
                })
                .ToArray();
            return ValueTask.FromResult(ElementAddResult.Succeeded(items));
        }
    }
}
