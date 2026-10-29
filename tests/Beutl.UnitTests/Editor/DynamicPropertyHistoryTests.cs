using System.Reactive;
using Beutl.Editor;
using Beutl.Editor.Observers;
using Beutl.Editor.Operations;

namespace Beutl.UnitTests.Editor;

[TestFixture]
public class DynamicPropertyHistoryTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void RegisteredPropertiesUseNormalHistoryAndRespectTrackingAttributes(bool notTracked)
    {
        var obj = new DynamicObject();
        var operations = new List<ChangeOperation>();
        using var observer = new CoreObjectOperationObserver(Observer.Create<ChangeOperation>(operations.Add),
            obj, new OperationSequenceGenerator());
        IProperty<int> property = obj.Add("Amount", notTracked);
        obj.RegisterAgain(property);
        property.Value = 12;
        if (notTracked)
        {
            Assert.That(operations, Is.Empty);
            return;
        }
        ChangeOperation operation = operations.Single();
        observer.Dispose();
        operation.Revert();
        Assert.That(property.Value, Is.Zero);
        operation.Apply();
        Assert.That(property.Value, Is.EqualTo(12));
        obj.Add("Later", false).Value = 4;
        property.Value = 7;
        Assert.That(operations, Has.Count.EqualTo(1), "Disposal unsubscribes both registration and value notifications.");
    }

    private sealed class DynamicObject : CoreObject
    {
        public IProperty<int> Add(string name, bool notTracked)
        {
            IProperty<int> property = Property.CreateValue(0);
            property.SetAttributes(name, notTracked ? [new NotTrackedAttribute()] : []);
            property.SetOwnerObject(this);
            RegisterProperty(property);
            return property;
        }

        public void RegisterAgain(IProperty property) => RegisterProperty(property);
    }
}
