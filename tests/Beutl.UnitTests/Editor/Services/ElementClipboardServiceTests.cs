using Beutl.Editor;
using Beutl.Editor.Observers;
using Beutl.Editor.Services;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.UnitTests.Editor.Services;

[TestFixture]
public class ElementClipboardServiceTests
{
    private string _basePath = null!;
    private Scene _scene = null!;
    private HistoryManager _history = null!;
    private CoreObjectOperationObserver _observer = null!;
    private InMemoryClipboardGateway _clipboard = null!;
    private ElementDuplicateService _duplicateService = null!;
    private ElementClipboardService _service = null!;

    [SetUp]
    public void Setup()
    {
        _basePath = Path.Combine(Path.GetTempPath(), $"beutl_clip_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_basePath);

        _scene = new Scene(100, 100, string.Empty)
        {
            Uri = new Uri(Path.Combine(_basePath, "test.scene")),
            Start = TimeSpan.Zero,
            Duration = TimeSpan.FromSeconds(60),
        };
        var sequence = new OperationSequenceGenerator();
        _history = new HistoryManager(_scene, sequence);
        _observer = new CoreObjectOperationObserver(null, _scene, sequence);
        _history.Subscribe(_observer);
        _clipboard = new InMemoryClipboardGateway();
        _duplicateService = new ElementDuplicateService(_history);
        _service = new ElementClipboardService(
            _history,
            _clipboard,
            _duplicateService,
            imageAccentColorFactory: () => Colors.Magenta);
    }

    [TearDown]
    public void TearDown()
    {
        _observer.Dispose();
        _history.Dispose();
        if (Directory.Exists(_basePath)) Directory.Delete(_basePath, recursive: true);
    }

    private Element AddElement(TimeSpan start, TimeSpan length, int zIndex = 0)
    {
        var element = new Element
        {
            Start = start,
            Length = length,
            ZIndex = zIndex,
            Uri = new Uri(Path.Combine(_basePath, $"{Guid.NewGuid():N}.layer")),
        };
        _scene.Children.Add(element);
        return element;
    }

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
    public async Task PasteAsync_NoMatchingFormat_ReturnsEmpty()
    {
        ElementPasteOutcome outcome = await _service.PasteAsync(_scene, TimeSpan.Zero, 0);

        Assert.That(outcome.Pasted, Is.False);
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

        // The spiral search anchors at (clickedFrame, clickedLayer); with an
        // empty timeline area around (20s, layer 3) the duplicates land
        // exactly there. Previously this test would have placed copies at
        // (0s, layer 0) regardless of the click — the regression Copilot
        // flagged.
        Assert.Multiple(() =>
        {
            Assert.That(outcome.Pasted, Is.True);
            Assert.That(outcome.ScrollTo.Start, Is.EqualTo(clicked));
            Assert.That(outcome.ScrollToZIndex, Is.EqualTo(clickedLayer));
        });
    }

    [Test]
    public async Task CopyAsync_ExposesPlainTextAlongsideElementJson()
    {
        Element element = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));

        await _service.CopyAsync([element]);

        // Both the JSON format and the platform text slot must carry the
        // payload — the gateway used to drop "text/plain" silently.
        IReadOnlyList<string> formats = await _clipboard.GetFormatsAsync();
        Assert.Multiple(() =>
        {
            Assert.That(formats, Contains.Item(BeutlClipboardFormats.Element));
            Assert.That(formats, Contains.Item(BeutlClipboardFormats.Text));
        });
    }

    private sealed class InMemoryClipboardGateway : IClipboardGateway
    {
        private readonly Dictionary<string, string> _entries = new();
        private IReadOnlyList<string>? _filePaths;
        private byte[]? _bitmapPng;

        public void SetSingle(string format, string content) => _entries[format] = content;

        public void SetFiles(IReadOnlyList<string> files) => _filePaths = files;

        public void SetBitmap(byte[] png) => _bitmapPng = png;

        public Task<IReadOnlyList<string>> GetFormatsAsync()
        {
            var formats = new List<string>(_entries.Keys);
            if (_filePaths is not null) formats.Add(BeutlClipboardFormats.Files);
            if (_bitmapPng is not null) formats.Add(BeutlClipboardFormats.Bitmap);
            return Task.FromResult<IReadOnlyList<string>>(formats);
        }

        public Task<string?> TryGetStringAsync(string format)
            => Task.FromResult(_entries.TryGetValue(format, out string? value) ? value : null);

        public Task<IReadOnlyList<string>?> TryGetFilePathsAsync() => Task.FromResult(_filePaths);

        public Task<ReadOnlyMemory<byte>?> TryGetBitmapPngAsync()
            => Task.FromResult<ReadOnlyMemory<byte>?>(_bitmapPng is null ? null : _bitmapPng);

        public Task SetAsync(IReadOnlyList<ClipboardEntry> entries)
        {
            _entries.Clear();
            foreach (ClipboardEntry entry in entries)
            {
                if (entry.Text is not null) _entries[entry.Format] = entry.Text;
            }

            return Task.CompletedTask;
        }

        public Task ClearAsync()
        {
            _entries.Clear();
            _filePaths = null;
            _bitmapPng = null;
            return Task.CompletedTask;
        }
    }
}
