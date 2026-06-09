using Beutl.Editor;
using Beutl.Editor.Observers;
using Beutl.Editor.Services;
using Beutl.ProjectSystem;

namespace Beutl.UnitTests.Editor.Services;

[TestFixture]
public class ElementStructureServiceTests
{
    private string _basePath = null!;
    private Scene _scene = null!;
    private HistoryManager _history = null!;
    private CoreObjectOperationObserver _observer = null!;
    private ElementStructureService _service = null!;

    [SetUp]
    public void Setup()
    {
        _basePath = Path.Combine(Path.GetTempPath(), $"beutl_life_{Guid.NewGuid():N}");
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
        _service = new ElementStructureService(_history);
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
            IsEnabled = true,
            Uri = new Uri(Path.Combine(_basePath, $"{Guid.NewGuid():N}.layer")),
        };
        _scene.Children.Add(element);
        return element;
    }

    [Test]
    public void Constructor_NullHistoryManager_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ElementStructureService(null!));
    }

    [Test]
    public void Exclude_RemovesAllAndCommitsOnce()
    {
        Element e1 = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        Element e2 = AddElement(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2));
        int before = _history.UndoCount;

        _service.Exclude(_scene, [e1, e2]);

        Assert.Multiple(() =>
        {
            Assert.That(_scene.Children, Does.Not.Contain(e1));
            Assert.That(_scene.Children, Does.Not.Contain(e2));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void Delete_RemovesAllAndCommitsOnce()
    {
        Element element = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        int before = _history.UndoCount;

        _service.Delete(_scene, [element]);

        Assert.Multiple(() =>
        {
            Assert.That(_scene.Children, Does.Not.Contain(element));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void Exclude_EmptyList_NoCommit()
    {
        int before = _history.UndoCount;

        _service.Exclude(_scene, []);

        Assert.That(_history.UndoCount, Is.EqualTo(before));
    }

    [Test]
    public void Split_ShortBothSides_DoesNotCommit()
    {
        // Splitting a ~1-frame element leaves both halves under minDuration, so it must bail.
        Element element = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(20));
        int before = _history.UndoCount;

        SplitOutcome outcome = _service.Split(_scene, [element], element.Start + TimeSpan.FromMilliseconds(10));

        Assert.Multiple(() =>
        {
            Assert.That(outcome.NewElements, Is.Empty);
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void Split_AtMidPoint_ProducesBackwardClipAndCommits()
    {
        Element element = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(4));
        int before = _history.UndoCount;
        int childrenBefore = _scene.Children.Count;
        TimeSpan splitAt = TimeSpan.FromSeconds(3);

        SplitOutcome outcome = _service.Split(_scene, [element], splitAt);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.NewElements, Has.Count.EqualTo(1));
            Assert.That(element.Length, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(outcome.NewElements[0].Start, Is.EqualTo(splitAt));
            Assert.That(_scene.Children.Count, Is.EqualTo(childrenBefore + 1));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void Group_TwoIds_AddsGroupAndCommits()
    {
        Element e1 = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        Element e2 = AddElement(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2));
        int before = _history.UndoCount;
        int groupsBefore = _scene.Groups.Count;

        GroupOutcome outcome = _service.Group(_scene, [e1.Id, e2.Id]);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Created, Is.True);
            Assert.That(_scene.Groups.Count, Is.EqualTo(groupsBefore + 1));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void Group_SingleId_DoesNotCreateGroup()
    {
        Element element = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        int beforeUndo = _history.UndoCount;
        int beforeGroups = _scene.Groups.Count;

        GroupOutcome outcome = _service.Group(_scene, [element.Id]);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Created, Is.False);
            // Commit() must no-op when the transaction recorded no operations.
            Assert.That(_scene.Groups.Count, Is.EqualTo(beforeGroups));
            Assert.That(_history.UndoCount, Is.EqualTo(beforeUndo));
        });
    }

    [Test]
    public void Group_SingleId_PullsOutOfExistingGroup()
    {
        Element e1 = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        Element e2 = AddElement(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2));
        _service.Group(_scene, [e1.Id, e2.Id]);

        _service.Group(_scene, [e1.Id]);

        Assert.That(_scene.Groups, Is.Empty);
    }

    [Test]
    public void Ungroup_IdsNotInAnyGroup_IsNoOp()
    {
        Element element = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        int beforeUndo = _history.UndoCount;
        int beforeGroups = _scene.Groups.Count;

        _service.Ungroup(_scene, [element.Id]);

        Assert.Multiple(() =>
        {
            // Same contract as Group_SingleId: an unconditional Commit would pull in unrelated mutations.
            Assert.That(_scene.Groups.Count, Is.EqualTo(beforeGroups));
            Assert.That(_history.UndoCount, Is.EqualTo(beforeUndo));
        });
    }

    [Test]
    public void Ungroup_RemovesIdsFromGroups_AndCommits()
    {
        Element e1 = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        Element e2 = AddElement(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2));
        Element e3 = AddElement(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(2));
        _service.Group(_scene, [e1.Id, e2.Id, e3.Id]);
        int before = _history.UndoCount;

        _service.Ungroup(_scene, [e1.Id, e2.Id, e3.Id]);

        Assert.Multiple(() =>
        {
            Assert.That(_scene.Groups, Is.Empty);
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }
}
