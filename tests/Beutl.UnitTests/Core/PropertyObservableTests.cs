namespace Beutl.UnitTests.Core;

// GetObservable on an IProperty.
[TestFixture]
public class PropertyObservableTests
{
    [Test]
    public void GetObservable_GivesTheValueAndThenEachNewOne()
    {
        var property = new ValueOnlyProperty<int>(1);
        property.SetAttributes("Count", []);
        var values = new List<int>();

        using (property.GetObservable().Subscribe(values.Add))
        {
            property.Value = 2;
            property.Value = 2;
            property.Value = 3;
        }

        property.Value = 4;

        Assert.That(values, Is.EqualTo(new[] { 1, 2, 3 }));
    }
}
