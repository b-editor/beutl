using Beutl.Animation;

namespace Beutl.UnitTests.Engine.Animation;

public class TestAnimatable : Animatable
{
    public static readonly CoreProperty<int> TestProperty = ConfigureProperty<int, TestAnimatable>(nameof(Test))
        .Register();

    [System.ComponentModel.DataAnnotations.Range(0, 15)]
    public int Test
    {
        get => GetValue(TestProperty);
        set => SetValue(TestProperty, value);
    }
}
