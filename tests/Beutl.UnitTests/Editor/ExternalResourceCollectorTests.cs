using Beutl.Editor;
using Beutl.Engine;
using Beutl.IO;
using Beutl.Media;

namespace Beutl.UnitTests.Editor;

public class ExternalResourceCollectorTests
{
    private string _testProjectDir = null!;
    private string _externalDir = null!;

    [SetUp]
    public void Setup()
    {
        _testProjectDir = Path.Combine(Path.GetTempPath(), $"beutl_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testProjectDir);

        _externalDir = Path.Combine(Path.GetTempPath(), $"beutl_external_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_externalDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_testProjectDir))
            Directory.Delete(_testProjectDir, recursive: true);

        if (Directory.Exists(_externalDir))
            Directory.Delete(_externalDir, recursive: true);
    }

    [Test]
    public void Collect_WithNullRoot_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            ExternalResourceCollector.Collect(null!, _testProjectDir));
    }

    [Test]
    public void Collect_WithNullProjectDirectory_ThrowsArgumentNullException()
    {
        // Arrange
        var root = new TestHierarchical();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            ExternalResourceCollector.Collect(root, null!));
    }

    [Test]
    public void Collect_WithEmptyHierarchy_ReturnsEmptyCollections()
    {
        // Arrange
        var root = new TestHierarchical();

        // Act
        ExternalResourceCollector collector = ExternalResourceCollector.Collect(root, _testProjectDir);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(collector.FileSources, Is.Empty);
            Assert.That(collector.FontFamilies, Is.Empty);
        });
    }

    [Test]
    public void Collect_WithEngineObjectContainingExternalFileSource_CollectsFileSource()
    {
        // Arrange
        string externalFile = Path.Combine(_externalDir, "test.png");
        File.WriteAllText(externalFile, "dummy content");

        var fileSource = new TestFileSource(new Uri(externalFile));
        var engineObj = new TestEngineObjectWithFileSource(fileSource);
        var root = new TestHierarchical();
        root.AddChild(engineObj);

        // Act
        ExternalResourceCollector collector = ExternalResourceCollector.Collect(root, _testProjectDir);

        // Assert
        Assert.That(collector.FileSources, Has.Count.EqualTo(1));
        Assert.That(collector.FileSources.First().Object, Is.EqualTo(engineObj.Id));
        Assert.That(collector.FileSources.First().OriginalUri.LocalPath, Is.EqualTo(externalFile));
    }

    [Test]
    public void Collect_WithEngineObjectContainingInternalFileSource_DoesNotCollect()
    {
        // Arrange
        string internalFile = Path.Combine(_testProjectDir, "test.png");
        File.WriteAllText(internalFile, "dummy content");

        var fileSource = new TestFileSource(new Uri(internalFile));
        var engineObj = new TestEngineObjectWithFileSource(fileSource);
        var root = new TestHierarchical();
        root.AddChild(engineObj);

        // Act
        ExternalResourceCollector collector = ExternalResourceCollector.Collect(root, _testProjectDir);

        // Assert
        Assert.That(collector.FileSources, Is.Empty);
    }

    [Test]
    public void Collect_WithEngineObjectContainingFontFamily_CollectsFontFamily()
    {
        // Arrange
        var fontFamily = new FontFamily("TestFont");
        var engineObj = new TestEngineObjectWithFontFamily(fontFamily);
        var root = new TestHierarchical();
        root.AddChild(engineObj);

        // Act
        ExternalResourceCollector collector = ExternalResourceCollector.Collect(root, _testProjectDir);

        // Assert
        Assert.That(collector.FontFamilies, Has.Count.EqualTo(1));
        Assert.That(collector.FontFamilies.First().Name, Is.EqualTo("TestFont"));
    }

    [Test]
    public void Collect_WithEngineObjectContainingNullFontFamily_DoesNotCollect()
    {
        // Arrange
        var engineObj = new TestEngineObjectWithNullableFontFamily(null);
        var root = new TestHierarchical();
        root.AddChild(engineObj);

        // Act
        ExternalResourceCollector collector = ExternalResourceCollector.Collect(root, _testProjectDir);

        // Assert
        Assert.That(collector.FontFamilies, Is.Empty);
    }

    [Test]
    public void Collect_WithDuplicateFileSources_CollectsOnlyOnce()
    {
        // Arrange
        string externalFile = Path.Combine(_externalDir, "test.png");
        File.WriteAllText(externalFile, "dummy content");
        Uri fileUri = new(externalFile);

        var fileSource1 = new TestFileSource(fileUri);
        var fileSource2 = new TestFileSource(fileUri);
        var engineObj1 = new TestEngineObjectWithFileSource(fileSource1);
        var engineObj2 = new TestEngineObjectWithFileSource(fileSource2);
        var root = new TestHierarchical();
        root.AddChild(engineObj1);
        root.AddChild(engineObj2);

        // Act
        ExternalResourceCollector collector = ExternalResourceCollector.Collect(root, _testProjectDir);

        // Assert
        Assert.That(collector.FileSources, Has.Count.EqualTo(2));
    }

    [Test]
    public void Collect_WithDuplicateFontFamilies_CollectsOnlyOnce()
    {
        // Arrange
        var fontFamily1 = new FontFamily("TestFont");
        var fontFamily2 = new FontFamily("TestFont");
        var engineObj1 = new TestEngineObjectWithFontFamily(fontFamily1);
        var engineObj2 = new TestEngineObjectWithFontFamily(fontFamily2);
        var root = new TestHierarchical();
        root.AddChild(engineObj1);
        root.AddChild(engineObj2);

        // Act
        ExternalResourceCollector collector = ExternalResourceCollector.Collect(root, _testProjectDir);

        // Assert
        Assert.That(collector.FontFamilies, Has.Count.EqualTo(1));
    }

    [Test]
    public void Collect_WithRootAsEngineObject_CollectsFromRoot()
    {
        // Arrange
        string externalFile = Path.Combine(_externalDir, "test.png");
        File.WriteAllText(externalFile, "dummy content");

        var fileSource = new TestFileSource(new Uri(externalFile));
        var root = new TestEngineObjectWithFileSource(fileSource);

        // Act
        ExternalResourceCollector collector = ExternalResourceCollector.Collect(root, _testProjectDir);

        // Assert
        Assert.That(collector.FileSources, Has.Count.EqualTo(1));
    }

    [Test]
    public void Collect_WithNestedHierarchy_CollectsFromAllLevels()
    {
        // Arrange
        string externalFile1 = Path.Combine(_externalDir, "test1.png");
        string externalFile2 = Path.Combine(_externalDir, "test2.png");
        File.WriteAllText(externalFile1, "dummy content 1");
        File.WriteAllText(externalFile2, "dummy content 2");

        var fileSource1 = new TestFileSource(new Uri(externalFile1));
        var fileSource2 = new TestFileSource(new Uri(externalFile2));
        var child = new TestEngineObjectWithFileSource(fileSource2);
        var parent = new TestEngineObjectWithFileSource(fileSource1);
        parent.AddChild(child);
        var root = new TestHierarchical();
        root.AddChild(parent);

        // Act
        ExternalResourceCollector collector = ExternalResourceCollector.Collect(root, _testProjectDir);

        // Assert
        Assert.That(collector.FileSources, Has.Count.EqualTo(2));
    }

    [Test]
    public void Collect_WithNonFileUri_DoesNotCollect()
    {
        // Arrange
        var fileSource = new TestFileSource(new Uri("http://example.com/test.png"));
        var engineObj = new TestEngineObjectWithFileSource(fileSource);
        var root = new TestHierarchical();
        root.AddChild(engineObj);

        // Act
        ExternalResourceCollector collector = ExternalResourceCollector.Collect(root, _testProjectDir);

        // Assert
        Assert.That(collector.FileSources, Is.Empty);
    }

    [Test]
    public void Collect_WithFileSourceHavingNullUri_DoesNotCollect()
    {
        // Arrange
        var fileSource = new TestFileSource(null);
        var engineObj = new TestEngineObjectWithFileSource(fileSource);
        var root = new TestHierarchical();
        root.AddChild(engineObj);

        // Act
        ExternalResourceCollector collector = ExternalResourceCollector.Collect(root, _testProjectDir);

        // Assert
        Assert.That(collector.FileSources, Is.Empty);
    }

    [Test]
    public void Collect_WithNullablePropertyContainingValue_CollectsFontFamily()
    {
        // Arrange
        var fontFamily = new FontFamily("TestFont");
        var engineObj = new TestEngineObjectWithNullableFontFamily(fontFamily);
        var root = new TestHierarchical();
        root.AddChild(engineObj);

        // Act
        ExternalResourceCollector collector = ExternalResourceCollector.Collect(root, _testProjectDir);

        // Assert
        Assert.That(collector.FontFamilies, Has.Count.EqualTo(1));
    }

    [Test]
    public void Collect_WithNestedIHierarchicalInProperty_CollectsFromNestedObjects()
    {
        // Arrange
        string externalFile = Path.Combine(_externalDir, "nested.png");
        File.WriteAllText(externalFile, "dummy content");

        var fileSource = new TestFileSource(new Uri(externalFile));
        var nestedEngineObj = new TestEngineObjectWithFileSource(fileSource);
        var engineObjWithNested = new TestEngineObjectWithNestedHierarchical(nestedEngineObj);
        var root = new TestHierarchical();
        root.AddChild(engineObjWithNested);

        // Act
        ExternalResourceCollector collector = ExternalResourceCollector.Collect(root, _testProjectDir);

        // Assert
        Assert.That(collector.FileSources, Has.Count.EqualTo(1));
    }

    [Test]
    public void Collect_WithNestedHierarchicalContainingEngineObjectChildren_CollectsFromChildren()
    {
        // Arrange - This tests the inner loop in CollectFromObject that processes
        // child EngineObjects within an IHierarchical property
        string externalFile = Path.Combine(_externalDir, "child_in_nested.png");
        File.WriteAllText(externalFile, "dummy content");

        var fileSource = new TestFileSource(new Uri(externalFile));
        var childEngineObj = new TestEngineObjectWithFileSource(fileSource);

        // Create a nested hierarchical that contains the child as a hierarchical child
        var nestedHierarchical = new TestNestedHierarchicalWithChildren();
        nestedHierarchical.AddChild(childEngineObj);

        // Create an engine object with the nested hierarchical as a property value
        var engineObjWithNested = new TestEngineObjectWithNestedHierarchical(nestedHierarchical);
        var root = new TestHierarchical();
        root.AddChild(engineObjWithNested);

        // Act
        ExternalResourceCollector collector = ExternalResourceCollector.Collect(root, _testProjectDir);

        // Assert
        Assert.That(collector.FileSources, Has.Count.EqualTo(1));
    }

    [Test]
    public void Collect_WithPropertyHavingNonIFileSourceNonFontNonHierarchical_SkipsProperty()
    {
        // Arrange - EngineObject with just a string property should not cause issues
        var engineObj = new TestEngineObjectWithStringProperty("test value");
        var root = new TestHierarchical();
        root.AddChild(engineObj);

        // Act
        ExternalResourceCollector collector = ExternalResourceCollector.Collect(root, _testProjectDir);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(collector.FileSources, Is.Empty);
            Assert.That(collector.FontFamilies, Is.Empty);
        });
    }

    [Test]
    public void Collect_WithMultipleFileSources_CollectsAllUnique()
    {
        // Arrange
        string externalFile1 = Path.Combine(_externalDir, "test1.png");
        string externalFile2 = Path.Combine(_externalDir, "test2.png");
        string externalFile3 = Path.Combine(_externalDir, "test3.png");
        File.WriteAllText(externalFile1, "content 1");
        File.WriteAllText(externalFile2, "content 2");
        File.WriteAllText(externalFile3, "content 3");

        var engineObj1 = new TestEngineObjectWithFileSource(new TestFileSource(new Uri(externalFile1)));
        var engineObj2 = new TestEngineObjectWithFileSource(new TestFileSource(new Uri(externalFile2)));
        var engineObj3 = new TestEngineObjectWithFileSource(new TestFileSource(new Uri(externalFile3)));
        var root = new TestHierarchical();
        root.AddChild(engineObj1);
        root.AddChild(engineObj2);
        root.AddChild(engineObj3);

        // Act
        ExternalResourceCollector collector = ExternalResourceCollector.Collect(root, _testProjectDir);

        // Assert
        Assert.That(collector.FileSources, Has.Count.EqualTo(3));
    }

    [Test]
    public void Collect_WithMultipleDifferentFontFamilies_CollectsAll()
    {
        // Arrange
        var engineObj1 = new TestEngineObjectWithFontFamily(new FontFamily("Font1"));
        var engineObj2 = new TestEngineObjectWithFontFamily(new FontFamily("Font2"));
        var engineObj3 = new TestEngineObjectWithFontFamily(new FontFamily("Font3"));
        var root = new TestHierarchical();
        root.AddChild(engineObj1);
        root.AddChild(engineObj2);
        root.AddChild(engineObj3);

        // Act
        ExternalResourceCollector collector = ExternalResourceCollector.Collect(root, _testProjectDir);

        // Assert
        Assert.That(collector.FontFamilies, Has.Count.EqualTo(3));
    }

    // Test helper classes
    private class TestHierarchical : Hierarchical
    {
        public void AddChild(IHierarchical child)
        {
            HierarchicalChildren.Add(child);
        }
    }

    private class TestFileSource : IFileSource
    {
        public TestFileSource(Uri? uri)
        {
            Uri = uri!;
        }

        public Uri Uri { get; private set; }

        public void ReadFrom(Uri uri)
        {
            Uri = uri;
        }
    }

    [SuppressResourceClassGeneration]
    private class TestEngineObjectWithFileSource : EngineObject
    {
        public TestEngineObjectWithFileSource(IFileSource? fileSource)
        {
            ScanProperties<TestEngineObjectWithFileSource>();
            FileSource.CurrentValue = fileSource;
        }

        public IProperty<IFileSource?> FileSource { get; } = Property.Create<IFileSource?>();

        public void AddChild(IHierarchical child)
        {
            HierarchicalChildren.Add(child);
        }
    }

    [SuppressResourceClassGeneration]
    private class TestEngineObjectWithFontFamily : EngineObject
    {
        public TestEngineObjectWithFontFamily(FontFamily fontFamily)
        {
            ScanProperties<TestEngineObjectWithFontFamily>();
            FontFamily.CurrentValue = fontFamily;
        }

        public IProperty<FontFamily> FontFamily { get; } = Property.Create<FontFamily>();
    }

    [SuppressResourceClassGeneration]
    private class TestEngineObjectWithNullableFontFamily : EngineObject
    {
        public TestEngineObjectWithNullableFontFamily(FontFamily? fontFamily)
        {
            ScanProperties<TestEngineObjectWithNullableFontFamily>();
            FontFamily.CurrentValue = fontFamily;
        }

        public IProperty<FontFamily?> FontFamily { get; } = Property.Create<FontFamily?>();
    }

    [SuppressResourceClassGeneration]
    private class TestEngineObjectWithNestedHierarchical : EngineObject
    {
        public TestEngineObjectWithNestedHierarchical(IHierarchical? nested)
        {
            ScanProperties<TestEngineObjectWithNestedHierarchical>();
            Nested.CurrentValue = nested;
        }

        public IProperty<IHierarchical?> Nested { get; } = Property.Create<IHierarchical?>();
    }

    [SuppressResourceClassGeneration]
    private class TestEngineObjectWithStringProperty : EngineObject
    {
        public TestEngineObjectWithStringProperty(string? value)
        {
            ScanProperties<TestEngineObjectWithStringProperty>();
            StringValue.CurrentValue = value;
        }

        public IProperty<string?> StringValue { get; } = Property.Create<string?>();
    }

    private class TestNestedHierarchicalWithChildren : Hierarchical
    {
        public void AddChild(IHierarchical child)
        {
            HierarchicalChildren.Add(child);
        }
    }
}
