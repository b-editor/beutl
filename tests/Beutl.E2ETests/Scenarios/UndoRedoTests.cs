using Beutl.Editor;
using Beutl.Editor.Services;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.E2ETests.Scenarios;

[TestFixture]
public class UndoRedoTests
{
    private SceneHistoryHarness _harness = null!;
    private Scene _scene = null!;
    private HistoryManager _history = null!;

    [SetUp]
    public void SetUp()
    {
        _harness = new SceneHistoryHarness("beutl-e2e-history", start: MediaTime.Zero, duration: MediaTime.FromSeconds(120));
        _scene = _harness.Scene;
        _history = _harness.History;
    }

    [TearDown]
    public void TearDown() => _harness.Dispose();

    [Test]
    public void Undo_reverts_each_edit_and_redo_replays_to_final_state()
    {
        var attributes = new ElementAttributeService(_history);
        var move = new ElementMoveService(_history, new ElementDuplicateService(_history));
        Element element = _harness.AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2), 0);

        bool originalEnabled = element.IsEnabled;
        Color originalColor = element.AccentColor;
        MediaTime originalStart = element.Start;
        int originalZ = element.ZIndex;

        var newColor = Color.FromArgb(255, 11, 22, 33);
        attributes.SetEnabled(element, false);
        attributes.SetAccentColor(element, newColor);
        move.Move(_scene, [element], MediaTime.FromSeconds(3), 2);

        Assert.Multiple(() =>
        {
            Assert.That(_history.UndoCount, Is.EqualTo(3));
            Assert.That(_history.CanUndo, Is.True);
            Assert.That(_history.CanRedo, Is.False);
            Assert.That(element.IsEnabled, Is.False);
            Assert.That(element.AccentColor, Is.EqualTo(newColor));
            Assert.That(element.Start, Is.EqualTo(originalStart + MediaTime.FromSeconds(3)));
            Assert.That(element.ZIndex, Is.EqualTo(2));
        });

        Assert.That(_history.Undo(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(element.Start, Is.EqualTo(originalStart));
            Assert.That(element.ZIndex, Is.EqualTo(originalZ));
            Assert.That(element.AccentColor, Is.EqualTo(newColor));
        });

        Assert.That(_history.Undo(), Is.True);
        Assert.That(element.AccentColor, Is.EqualTo(originalColor));

        Assert.That(_history.Undo(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(element.IsEnabled, Is.EqualTo(originalEnabled));
            Assert.That(_history.UndoCount, Is.EqualTo(0));
            Assert.That(_history.CanUndo, Is.False);
            Assert.That(_history.RedoCount, Is.EqualTo(3));
        });

        Assert.That(_history.Undo(), Is.False);

        Assert.That(_history.Redo(), Is.True);
        Assert.That(element.IsEnabled, Is.False);
        Assert.That(_history.Redo(), Is.True);
        Assert.That(element.AccentColor, Is.EqualTo(newColor));
        Assert.That(_history.Redo(), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(element.Start, Is.EqualTo(originalStart + MediaTime.FromSeconds(3)));
            Assert.That(element.ZIndex, Is.EqualTo(2));
            Assert.That(_history.UndoCount, Is.EqualTo(3));
            Assert.That(_history.CanRedo, Is.False);
            Assert.That(_history.Redo(), Is.False);
        });
    }

    [Test]
    public void New_edit_after_undo_truncates_the_redo_branch()
    {
        var attributes = new ElementAttributeService(_history);
        Element element = _harness.AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2), 0);

        attributes.SetEnabled(element, false);
        attributes.SetAccentColor(element, Color.FromArgb(255, 1, 1, 1));
        Assert.That(_history.UndoCount, Is.EqualTo(2));

        _history.Undo();
        Assert.That(_history.RedoCount, Is.EqualTo(1));

        attributes.SetAccentColor(element, Color.FromArgb(255, 9, 9, 9));

        Assert.Multiple(() =>
        {
            Assert.That(_history.CanRedo, Is.False);
            Assert.That(_history.RedoCount, Is.EqualTo(0));
            Assert.That(_history.UndoCount, Is.EqualTo(2));
            Assert.That(element.AccentColor, Is.EqualTo(Color.FromArgb(255, 9, 9, 9)));
        });
    }

    [Test]
    public void JumpTo_moves_directly_across_several_entries()
    {
        var attributes = new ElementAttributeService(_history);
        var resize = new ElementResizeService(_history);
        Element element = _harness.AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2), 0);
        MediaTime originalLength = element.Length;

        attributes.SetEnabled(element, false);
        resize.Resize(_scene, [new ElementResizeRequest(element, element.Start, MediaTime.FromSeconds(5), element.ZIndex)]);
        attributes.SetAccentColor(element, Color.FromArgb(255, 7, 7, 7));

        // Commits push entries onto the initial one, so there are 4 entries (index 0 = initial)
        // for 3 committed edits.
        Assert.That(_history.Entries, Has.Count.EqualTo(4));
        Assert.That(_history.CurrentIndex, Is.EqualTo(3));

        Assert.That(_history.JumpTo(0), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(_history.CurrentIndex, Is.EqualTo(0));
            Assert.That(element.IsEnabled, Is.True);
            Assert.That(element.Length, Is.EqualTo(originalLength));
        });

        Assert.That(_history.JumpTo(2), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(_history.CurrentIndex, Is.EqualTo(2));
            Assert.That(element.IsEnabled, Is.False);
            Assert.That(element.Length, Is.EqualTo(MediaTime.FromSeconds(5)));
        });

        Assert.That(_history.JumpTo(3), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(_history.CurrentIndex, Is.EqualTo(3));
            Assert.That(element.AccentColor, Is.EqualTo(Color.FromArgb(255, 7, 7, 7)));
        });

        Assert.That(_history.JumpTo(_history.CurrentIndex), Is.False);
        Assert.That(_history.JumpTo(99), Is.False);
    }
}
