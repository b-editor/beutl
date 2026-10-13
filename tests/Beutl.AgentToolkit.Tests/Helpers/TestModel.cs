namespace Beutl.AgentToolkit.Tests.Helpers;

internal sealed class TestModel : CoreObject
{
    public static readonly CoreProperty<int> ValueProperty =
        ConfigureProperty<int, TestModel>(nameof(Value))
            .DefaultValue(0)
            .Register();

    public int Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }
}
