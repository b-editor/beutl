using Beutl.E2ETests.TestInfrastructure;
using Beutl.Editor;
using Beutl.Editor.Services;
using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl.E2ETests.Scenarios;

[TestFixture]
public class ClipboardPipelineTests
{
    private SceneHistoryHarness _harness = null!;
    private Scene _scene = null!;
    private HistoryManager _history = null!;
    private FakeClipboardGateway _clipboard = null!;
    private ElementClipboardService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _harness = new SceneHistoryHarness("beutl-e2e-clip", start: TimeSpan.Zero, duration: TimeSpan.FromSeconds(120));
        _scene = _harness.Scene;
        _history = _harness.History;
        _clipboard = new FakeClipboardGateway();
        _service = new ElementClipboardService(
            _history,
            _clipboard,
            new ElementDuplicateService(_history),
            imageAccentColorFactory: () => Colors.Magenta);
    }

    [TearDown]
    public void TearDown() => _harness.Dispose();

    [Test]
    public async Task Copy_then_paste_adds_a_cloned_element_at_the_clicked_position()
    {
        Element original = _harness.AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), 0);
        original.AccentColor = Color.FromArgb(255, 5, 6, 7);

        bool copied = await _service.CopyAsync([original]);
        Assert.That(copied, Is.True);

        int childrenBefore = _scene.Children.Count;
        ElementPasteOutcome outcome = await _service.PasteAsync(_scene, TimeSpan.FromSeconds(30), 2);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Pasted, Is.True);
            Assert.That(outcome.NewElements, Has.Count.EqualTo(1));
            Assert.That(_scene.Children.Count, Is.EqualTo(childrenBefore + 1));
            Assert.That(outcome.NewElements[0].Start, Is.EqualTo(TimeSpan.FromSeconds(30)));
            Assert.That(outcome.NewElements[0].ZIndex, Is.EqualTo(2));
            Assert.That(outcome.NewElements[0].Id, Is.Not.EqualTo(original.Id));
        });
    }

    [Test]
    public async Task Cut_removes_the_element_and_a_paste_restores_an_equivalent_clone()
    {
        Element original = _harness.AddElement(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(3), 1);
        int historyBefore = _history.UndoCount;

        bool cut = await _service.CutAsync(_scene, [original]);
        Assert.Multiple(() =>
        {
            Assert.That(cut, Is.True);
            Assert.That(_scene.Children, Does.Not.Contain(original));
            Assert.That(_history.UndoCount, Is.EqualTo(historyBefore + 1));
        });

        ElementPasteOutcome outcome = await _service.PasteAsync(_scene, TimeSpan.FromSeconds(40), 0);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Pasted, Is.True);
            Assert.That(_scene.Children, Has.Count.EqualTo(1));
            Assert.That(outcome.NewElements[0].Length, Is.EqualTo(TimeSpan.FromSeconds(3)));
        });
    }

    [Test]
    public async Task Cut_with_unavailable_clipboard_preserves_the_scene()
    {
        Element original = _harness.AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), 0);
        int childrenBefore = _scene.Children.Count;
        int historyBefore = _history.UndoCount;
        _clipboard.Unavailable = true;

        bool cut = await _service.CutAsync(_scene, [original]);

        Assert.Multiple(() =>
        {
            Assert.That(cut, Is.False);
            Assert.That(_scene.Children, Does.Contain(original));
            Assert.That(_scene.Children.Count, Is.EqualTo(childrenBefore));
            Assert.That(_history.UndoCount, Is.EqualTo(historyBefore));
        });
    }

    [Test]
    public async Task Paste_with_empty_clipboard_is_a_no_op()
    {
        ElementPasteOutcome outcome = await _service.PasteAsync(_scene, TimeSpan.Zero, 0);
        Assert.That(outcome.Pasted, Is.False);
    }

    private sealed class FakeClipboardGateway : IClipboardGateway
    {
        private readonly Dictionary<string, string> _entries = new();

        public bool Unavailable { get; set; }

        public Task<IReadOnlyList<string>> GetFormatsAsync()
            => Task.FromResult<IReadOnlyList<string>>(new List<string>(_entries.Keys));

        public Task<string?> TryGetStringAsync(string format)
            => Task.FromResult(_entries.TryGetValue(format, out string? value) ? value : null);

        public Task<IReadOnlyList<string>?> TryGetFilePathsAsync()
            => Task.FromResult<IReadOnlyList<string>?>(null);

        public Task<ReadOnlyMemory<byte>?> TryGetBitmapPngAsync()
            => Task.FromResult<ReadOnlyMemory<byte>?>(null);

        public Task<bool> SetAsync(IReadOnlyList<ClipboardEntry> entries)
        {
            if (Unavailable) return Task.FromResult(false);

            _entries.Clear();
            foreach (ClipboardEntry entry in entries)
            {
                if (entry.Text is not null) _entries[entry.Format] = entry.Text;
            }

            return Task.FromResult(true);
        }

        public Task ClearAsync()
        {
            _entries.Clear();
            return Task.CompletedTask;
        }
    }
}
