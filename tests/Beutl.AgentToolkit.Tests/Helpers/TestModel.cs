namespace Beutl.AgentToolkit.Tests.Helpers;

internal sealed class TestModel : CoreObject
{
    public TestModel()
    {
        ScanProperties<TestModel>();
    }

    public IProperty<int> ValueProperty { get; } = new ValueOnlyProperty<int>(0);

    public int Value
    {
        get => ValueProperty.Value;
        set => ValueProperty.Value = value;
    }
}
