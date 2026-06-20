using Beutl.Editor;
using Beutl.Editor.Services;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Editor.Services;

[TestFixture]
public class ElementAttributeServiceTests
{
    private SceneHistoryHarness _harness = null!;
    private Scene _scene = null!;
    private HistoryManager _history = null!;
    private ElementAttributeService _service = null!;

    [SetUp]
    public void Setup()
    {
        _harness = new SceneHistoryHarness("beutl_attr", start: TimeSpan.Zero, duration: TimeSpan.FromSeconds(60));
        _scene = _harness.Scene;
        _history = _harness.History;
        _service = new ElementAttributeService(_history);
    }

    [TearDown]
    public void TearDown()
    {
        _harness.Dispose();
    }

    private Element AddElement(bool isEnabled = true)
        => _harness.AddElement(isEnabled);

    [Test]
    public void Constructor_NullHistoryManager_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ElementAttributeService(null!));
    }

    [Test]
    public void SetEnabled_TogglesAndCommitsOnce()
    {
        Element element = AddElement();
        int before = _history.UndoCount;

        _service.SetEnabled(element, false);

        Assert.Multiple(() =>
        {
            Assert.That(element.IsEnabled, Is.False);
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void SetEnabled_NoChange_NoCommit()
    {
        Element element = AddElement();
        int before = _history.UndoCount;

        _service.SetEnabled(element, element.IsEnabled);

        Assert.That(_history.UndoCount, Is.EqualTo(before));
    }

    [Test]
    public void SetAccentColor_AppliesAndCommitsOnce()
    {
        Element element = AddElement();
        int before = _history.UndoCount;
        var target = Color.FromArgb(255, 10, 20, 30);

        _service.SetAccentColor(element, target);

        Assert.Multiple(() =>
        {
            Assert.That(element.AccentColor, Is.EqualTo(target));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void SetAccentColor_NoChange_NoCommit()
    {
        Element element = AddElement();
        Color initial = element.AccentColor;
        int before = _history.UndoCount;

        _service.SetAccentColor(element, initial);

        Assert.That(_history.UndoCount, Is.EqualTo(before));
    }
}
