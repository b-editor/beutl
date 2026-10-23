namespace Beutl.AgentToolkit.Tests.Helpers;

internal sealed class TestModel : CoreObject
{
    public TestModel()
    {
        ScanProperties<TestModel>();
    }

    public IProperty<int> Value { get; } = new ValueOnlyProperty<int>(0);
}
