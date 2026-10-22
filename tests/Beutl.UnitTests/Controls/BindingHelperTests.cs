using Avalonia.Data;
using Beutl.UnitTests.TestInfrastructure;
using Reactive.Bindings;

namespace Beutl.UnitTests.Controls;

[TestFixture]
public class BindingHelperTests
{
    [Test]
    public void ToPropertyBinding_DoesNotKeepAnUnloadedValueTypeAlive()
    {
        WeakReference type = CollectibleTypes.Use(static valueType =>
        {
            object property = Activator.CreateInstance(
                typeof(ReactivePropertySlim<>).MakeGenericType(valueType),
                Activator.CreateInstance(valueType), ReactivePropertyMode.Default, null)!;
            typeof(BindingHelper).GetMethod(nameof(BindingHelper.ToPropertyBinding))!
                .MakeGenericMethod(valueType)
                .Invoke(null, [property, BindingMode.Default]);
        });

        Assert.That(CollectibleTypes.IsCollected(type), Is.True);
    }
}
