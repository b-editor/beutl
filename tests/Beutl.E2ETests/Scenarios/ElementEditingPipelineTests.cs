using Beutl.E2ETests.TestInfrastructure;
using Beutl.Editor;
using Beutl.Editor.Services;
using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl.E2ETests.Scenarios;

[TestFixture]
public class ElementEditingPipelineTests
{
    private SceneHistoryHarness _harness = null!;
    private Scene _scene = null!;
    private HistoryManager _history = null!;

    [SetUp]
    public void SetUp()
    {
        _harness = new SceneHistoryHarness("beutl-e2e-edit", start: TimeSpan.Zero, duration: TimeSpan.FromSeconds(120));
        _scene = _harness.Scene;
        _history = _harness.History;
    }

    [TearDown]
    public void TearDown() => _harness.Dispose();

    [Test]
    public void Add_move_resize_delete_pipeline_drives_scene_state()
    {
        var move = new ElementMoveService(_history, new ElementDuplicateService(_history));
        var resize = new ElementResizeService(_history);
        var structure = new ElementStructureService(_history);

        Element a = _harness.AddElement(TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(3), 0);
        Element b = _harness.AddElement(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(3), 1);
        Element c = _harness.AddElement(TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(3), 2);
        Assert.That(_scene.Children, Has.Count.EqualTo(3));

        ElementMoveOutcome moved = move.Move(_scene, [a], TimeSpan.FromSeconds(5), 1);
        Assert.Multiple(() =>
        {
            Assert.That(moved, Is.EqualTo(ElementMoveOutcome.Moved));
            Assert.That(a.Start, Is.EqualTo(TimeSpan.FromSeconds(5)));
            Assert.That(a.ZIndex, Is.EqualTo(1));
        });

        resize.Resize(_scene, [new ElementResizeRequest(b, b.Start, TimeSpan.FromSeconds(7), b.ZIndex)]);
        Assert.That(b.Length, Is.EqualTo(TimeSpan.FromSeconds(7)));

        structure.Delete(_scene, [c]);
        Assert.Multiple(() =>
        {
            Assert.That(_scene.Children, Does.Not.Contain(c));
            Assert.That(_scene.Children, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public void Move_multiple_elements_is_a_single_history_entry()
    {
        var move = new ElementMoveService(_history, new ElementDuplicateService(_history));
        Element a = _harness.AddElement(TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(2), 0);
        Element b = _harness.AddElement(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(2), 1);
        int before = _history.UndoCount;

        ElementMoveOutcome outcome = move.Move(_scene, [a, b], TimeSpan.FromSeconds(3), 0);

        Assert.Multiple(() =>
        {
            Assert.That(outcome, Is.EqualTo(ElementMoveOutcome.Moved));
            Assert.That(a.Start, Is.EqualTo(TimeSpan.FromSeconds(3)));
            Assert.That(b.Start, Is.EqualTo(TimeSpan.FromSeconds(13)));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void Split_produces_a_second_clip_at_the_split_point()
    {
        var structure = new ElementStructureService(_history);
        Element element = _harness.AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(8), 0);
        int childrenBefore = _scene.Children.Count;
        TimeSpan splitAt = TimeSpan.FromSeconds(4);

        SplitOutcome outcome = structure.Split(_scene, [element], splitAt);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.NewElements, Has.Count.EqualTo(1));
            Assert.That(element.Length, Is.EqualTo(splitAt - element.Start));
            Assert.That(outcome.NewElements[0].Start, Is.EqualTo(splitAt));
            Assert.That(_scene.Children.Count, Is.EqualTo(childrenBefore + 1));
        });
    }

    [Test]
    public void Attribute_service_toggles_enabled_and_accent_color()
    {
        var attributes = new ElementAttributeService(_history);
        Element element = _harness.AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), 0);
        var newColor = Color.FromArgb(255, 64, 128, 192);

        attributes.SetEnabled(element, false);
        attributes.SetAccentColor(element, newColor);

        Assert.Multiple(() =>
        {
            Assert.That(element.IsEnabled, Is.False);
            Assert.That(element.AccentColor, Is.EqualTo(newColor));
        });
    }

    [Test]
    public void Group_and_ungroup_mutate_scene_groups()
    {
        var structure = new ElementStructureService(_history);
        Element a = _harness.AddElement(TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(2), 0);
        Element b = _harness.AddElement(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(2), 1);

        GroupOutcome grouped = structure.Group(_scene, [a.Id, b.Id]);
        Assert.Multiple(() =>
        {
            Assert.That(grouped.Created, Is.True);
            Assert.That(_scene.Groups, Has.Count.EqualTo(1));
            Assert.That(_scene.Groups[0], Does.Contain(a.Id).And.Contain(b.Id));
        });

        structure.Ungroup(_scene, [a.Id, b.Id]);
        Assert.That(_scene.Groups, Is.Empty);
    }
}
