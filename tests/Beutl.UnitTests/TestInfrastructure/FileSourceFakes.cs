using Beutl.Engine;
using Beutl.IO;

namespace Beutl.UnitTests.TestInfrastructure;

public sealed class TestFileSource : IFileSource
{
    public TestFileSource(Uri uri)
    {
        Uri = uri;
    }

    public Uri Uri { get; private set; }

    public void ReadFrom(Uri uri)
    {
        Uri = uri;
    }
}

[SuppressResourceClassGeneration]
public sealed class TestEngineObjectWithFileSource : EngineObject
{
    public TestEngineObjectWithFileSource() : this(null)
    {
    }

    public TestEngineObjectWithFileSource(IFileSource? fileSource)
    {
        ScanProperties<TestEngineObjectWithFileSource>();
        FileSource.CurrentValue = fileSource;
    }

    public IExpressionProperty<IFileSource?> FileSource { get; } = Property.CreateWithExpression<IFileSource?>();

    public void AddChild(IHierarchical child)
    {
        HierarchicalChildren.Add(child);
    }
}
