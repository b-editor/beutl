using Beutl.Editor;
using Beutl.Engine;
using Beutl.Media;
using Beutl.UnitTests.TestInfrastructure;

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
        Assert.Throws<ArgumentNullException>(() =>
            ExternalResourceCollector.Collect(null!, _testProjectDir));
    }

    [Test]
    public void Collect_WithNullProjectDirectory_ThrowsArgumentNullException()
    {
        var root = new TestHierarchical();

        Assert.Throws<ArgumentNullException>(() =>
            ExternalResourceCollector.Collect(root, null!));
    }

    [Test]
    public void Collect_WithEmptyHierarchy_ReturnsEmptyCollections()
    {
        var root = new TestHierarchical();

        ExternalResourceCollector collector = ExternalResourceCollector.Collect(root, _testProjectDir);

        Assert.Multiple(() =>
        {
            Assert.That(collector.FileSources, Is.Empty);
            Assert.That(collector.FontFamilies, Is.Empty);
        });
    }

    [Test]
    public void Collect_WithEngineObjectContainingExternalFileSource_CollectsFileSource()
    {
        string externalFile = Path.Combine(_externalDir, "test.png");
        File.WriteAllText(externalFile, "dummy content");

        var fileSource = new TestFileSource(new Uri(externalFile));
        var engineObj = new TestEngineObjectWithFileSource(fileSource);
        var root = new TestHierarchical();
        root.AddChild(engineObj);

        ExternalResourceCollector collector = ExternalResourceCollector.Collect(root, _testProjectDir);

        Assert.That(collector.FileSources, Has.Count.EqualTo(1));
        Assert.That(collector.FileSources.First().Object, Is.EqualTo(engineObj.Id));
        Assert.That(collector.FileSources.First().OriginalUri.LocalPath, Is.EqualTo(externalFile));
    }

    [Test]
    public void Collect_WithEngineObjectContainingInternalFileSource_DoesNotCollect()
    {
        string internalFile = Path.Combine(_testProjectDir, "test.png");
        File.WriteAllText(internalFile, "dummy content");

        var fileSource = new TestFileSource(new Uri(internalFile));
        var engineObj = new TestEngineObjectWithFileSource(fileSource);
        var root = new TestHierarchical();
        root.AddChild(engineObj);

        ExternalResourceCollector collector = ExternalResourceCollector.Collect(root, _testProjectDir);

        Assert.That(collector.FileSources, Is.Empty);
    }

    [Test]
    public void Collect_WithEngineObjectContainingFontFamily_CollectsFontFamily()
    {
        var fontFamily = new FontFamily("TestFont");
        var engineObj = new TestEngineObjectWithFontFamily(fontFamily);
        var root = new TestHierarchical();
        root.AddChild(engineObj);

        ExternalResourceCollector collector = ExternalResourceCollector.Collect(root, _testProjectDir);

        Assert.That(collector.FontFamilies, Has.Count.EqualTo(1));
        Assert.That(collector.FontFamilies.First().Name, Is.EqualTo("TestFont"));
    }

    [Test]
    public void Collect_WithEngineObjectContainingNullFontFamily_DoesNotCollect()
    {
        var engineObj = new TestEngineObjectWithFontFamily(null);
        var root = new TestHierarchical();
        root.AddChild(engineObj);

        ExternalResourceCollector collector = ExternalResourceCollector.Collect(root, _testProjectDir);

        Assert.That(collector.FontFamilies, Is.Empty);
    }

    [Test]
    public void Collect_WithDuplicateFileSources_RecordsOnePerOwner()
    {
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

        ExternalResourceCollector collector = ExternalResourceCollector.Collect(root, _testProjectDir);

        Assert.That(collector.FileSources, Has.Count.EqualTo(2));
    }

    [Test]
    public void Collect_WithDuplicateFontFamilies_CollectsOnlyOnce()
    {
        var fontFamily1 = new FontFamily("TestFont");
        var fontFamily2 = new FontFamily("TestFont");
        var engineObj1 = new TestEngineObjectWithFontFamily(fontFamily1);
        var engineObj2 = new TestEngineObjectWithFontFamily(fontFamily2);
        var root = new TestHierarchical();
        root.AddChild(engineObj1);
        root.AddChild(engineObj2);

        ExternalResourceCollector collector = ExternalResourceCollector.Collect(root, _testProjectDir);

        Assert.That(collector.FontFamilies, Has.Count.EqualTo(1));
    }

    [Test]
    public void Collect_WithRootAsEngineObject_CollectsFromRoot()
    {
        string externalFile = Path.Combine(_externalDir, "test.png");
        File.WriteAllText(externalFile, "dummy content");

        var fileSource = new TestFileSource(new Uri(externalFile));
        var root = new TestEngineObjectWithFileSource(fileSource);

        ExternalResourceCollector collector = ExternalResourceCollector.Collect(root, _testProjectDir);

        Assert.That(collector.FileSources, Has.Count.EqualTo(1));
    }

    [Test]
    public void Collect_WithNestedHierarchy_CollectsFromAllLevels()
    {
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

        ExternalResourceCollector collector = ExternalResourceCollector.Collect(root, _testProjectDir);

        Assert.That(collector.FileSources, Has.Count.EqualTo(2));
    }

    [Test]
    public void Collect_WithNonFileUri_DoesNotCollect()
    {
        var fileSource = new TestFileSource(new Uri("http://example.com/test.png"));
        var engineObj = new TestEngineObjectWithFileSource(fileSource);
        var root = new TestHierarchical();
        root.AddChild(engineObj);

        ExternalResourceCollector collector = ExternalResourceCollector.Collect(root, _testProjectDir);

        Assert.That(collector.FileSources, Is.Empty);
    }

    [Test]
    public void Collect_WithNestedIHierarchicalInProperty_CollectsFromNestedObjects()
    {
        string externalFile = Path.Combine(_externalDir, "nested.png");
        File.WriteAllText(externalFile, "dummy content");

        var fileSource = new TestFileSource(new Uri(externalFile));
        var nestedEngineObj = new TestEngineObjectWithFileSource(fileSource);
        var engineObjWithNested = new TestEngineObjectWithNestedHierarchical(nestedEngineObj);
        var root = new TestHierarchical();
        root.AddChild(engineObjWithNested);

        ExternalResourceCollector collector = ExternalResourceCollector.Collect(root, _testProjectDir);

        Assert.That(collector.FileSources, Has.Count.EqualTo(1));
    }

    [Test]
    public void Collect_WithNestedHierarchicalContainingEngineObjectChildren_CollectsFromChildren()
    {
        // The nested hierarchical's children are reached by the hierarchy walk.
        string externalFile = Path.Combine(_externalDir, "child_in_nested.png");
        File.WriteAllText(externalFile, "dummy content");

        var fileSource = new TestFileSource(new Uri(externalFile));
        var childEngineObj = new TestEngineObjectWithFileSource(fileSource);

        var nestedHierarchical = new TestHierarchical();
        nestedHierarchical.AddChild(childEngineObj);

        var engineObjWithNested = new TestEngineObjectWithNestedHierarchical(nestedHierarchical);
        var root = new TestHierarchical();
        root.AddChild(engineObjWithNested);

        ExternalResourceCollector collector = ExternalResourceCollector.Collect(root, _testProjectDir);

        Assert.That(collector.FileSources, Has.Count.EqualTo(1));
    }

    [Test]
    public void Collect_WithPropertyHavingNonIFileSourceNonFontNonHierarchical_SkipsProperty()
    {
        // EngineObject with just a string property should not cause issues
        var engineObj = new TestEngineObjectWithStringProperty("test value");
        var root = new TestHierarchical();
        root.AddChild(engineObj);

        ExternalResourceCollector collector = ExternalResourceCollector.Collect(root, _testProjectDir);

        Assert.Multiple(() =>
        {
            Assert.That(collector.FileSources, Is.Empty);
            Assert.That(collector.FontFamilies, Is.Empty);
        });
    }

    [Test]
    public void Collect_WithMultipleFileSources_CollectsAllUnique()
    {
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

        ExternalResourceCollector collector = ExternalResourceCollector.Collect(root, _testProjectDir);

        Assert.That(collector.FileSources, Has.Count.EqualTo(3));
    }

    [Test]
    public void Collect_WithMultipleDifferentFontFamilies_CollectsAll()
    {
        var engineObj1 = new TestEngineObjectWithFontFamily(new FontFamily("Font1"));
        var engineObj2 = new TestEngineObjectWithFontFamily(new FontFamily("Font2"));
        var engineObj3 = new TestEngineObjectWithFontFamily(new FontFamily("Font3"));
        var root = new TestHierarchical();
        root.AddChild(engineObj1);
        root.AddChild(engineObj2);
        root.AddChild(engineObj3);

        ExternalResourceCollector collector = ExternalResourceCollector.Collect(root, _testProjectDir);

        Assert.That(collector.FontFamilies, Has.Count.EqualTo(3));
    }

    [Test]
    public void Collect_WithSiblingDirectoryHavingSamePrefix_TreatsAsSiblingAsExternal()
    {
        // Create proj and projbar to test path boundary detection
        string projectDir = Path.Combine(_testProjectDir, "proj");
        string siblingDir = Path.Combine(_testProjectDir, "projbar");
        Directory.CreateDirectory(projectDir);
        Directory.CreateDirectory(siblingDir);

        string siblingFile = Path.Combine(siblingDir, "test.png");
        File.WriteAllText(siblingFile, "dummy content");

        var fileSource = new TestFileSource(new Uri(siblingFile));
        var engineObj = new TestEngineObjectWithFileSource(fileSource);
        var root = new TestHierarchical();
        root.AddChild(engineObj);

        ExternalResourceCollector collector = ExternalResourceCollector.Collect(root, projectDir);

        Assert.That(collector.FileSources, Has.Count.EqualTo(1));
    }

    [Test]
    public void Collect_WithCaseVariantSibling_RelocatesTheReference()
    {
        string projectDir = Path.Combine(_testProjectDir, "Project");
        string siblingDir = Path.Combine(_testProjectDir, "project");
        Directory.CreateDirectory(projectDir);
        Directory.CreateDirectory(siblingDir);
        string asset = Path.Combine(siblingDir, "asset.png");
        File.WriteAllText(asset, "image");
        var root = new TestEngineObjectWithFileSource(new TestFileSource(new Uri(asset)));

        ExternalResourceCollector collector = ExternalResourceCollector.Collect(root, projectDir);

        // Even if the current volume aliases the two names, relocate the reference so a
        // relative ../project URI remains valid when imported on a case-sensitive volume.
        Assert.That(collector.FileSources.Select(source => source.OriginalUri), Is.EqualTo(new[] { new Uri(asset) }));
    }

    private class TestHierarchical : Hierarchical
    {
        public void AddChild(IHierarchical child)
        {
            HierarchicalChildren.Add(child);
        }
    }

    [SuppressResourceClassGeneration]
    private class TestEngineObjectWithFontFamily : EngineObject
    {
        public TestEngineObjectWithFontFamily(FontFamily? fontFamily)
        {
            ScanProperties<TestEngineObjectWithFontFamily>();
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
}
