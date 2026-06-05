using Beutl.Editor;
using Beutl.Editor.Observers;
using Beutl.Editor.Services;
using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl.UnitTests.Editor.Services;

[TestFixture]
public class ElementAttributeServiceTests
{
    private string _basePath = null!;
    private Scene _scene = null!;
    private HistoryManager _history = null!;
    private CoreObjectOperationObserver _observer = null!;
    private ElementAttributeService _service = null!;

    [SetUp]
    public void Setup()
    {
        _basePath = Path.Combine(Path.GetTempPath(), $"beutl_attr_{Guid.NewGuid():N}");
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
        _service = new ElementAttributeService(_history);
    }

    [TearDown]
    public void TearDown()
    {
        _observer.Dispose();
        _history.Dispose();
        if (Directory.Exists(_basePath)) Directory.Delete(_basePath, recursive: true);
    }

    private Element AddElement(bool isEnabled = true)
    {
        var element = new Element
        {
            Start = TimeSpan.FromSeconds(1),
            Length = TimeSpan.FromSeconds(2),
            ZIndex = 0,
            IsEnabled = isEnabled,
            Uri = new Uri(Path.Combine(_basePath, $"{Guid.NewGuid():N}.layer")),
        };
        _scene.Children.Add(element);
        return element;
    }

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
