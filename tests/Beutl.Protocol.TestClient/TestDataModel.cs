using Beutl.Collections;
using Beutl.Engine;

namespace Beutl.Protocol.TestClient;

/// <summary>
/// Simple test data model for synchronization testing
/// </summary>
public class TestDataModel : EngineObject
{
    public static readonly CoreProperty<string> NameProperty;
    public static readonly CoreProperty<int> CountProperty;
    public static readonly CoreProperty<CoreList<string>> ItemsProperty;

    static TestDataModel()
    {
        NameProperty = ConfigureProperty<string, TestDataModel>(nameof(Name))
            .DefaultValue("Default")
            .Register();

        CountProperty = ConfigureProperty<int, TestDataModel>(nameof(Count))
            .DefaultValue(0)
            .Register();

        ItemsProperty = ConfigureProperty<CoreList<string>, TestDataModel>(nameof(Items))
            .DefaultValue(new CoreList<string>())
            .Register();
    }

    public string Name
    {
        get => GetValue(NameProperty);
        set => SetValue(NameProperty, value);
    }

    public int Count
    {
        get => GetValue(CountProperty);
        set => SetValue(CountProperty, value);
    }

    public CoreList<string> Items
    {
        get => GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }
}
