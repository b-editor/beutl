using Beutl.Engine.Expressions;

namespace Beutl.UnitTests.Core;

// Any CoreObject can host IProperty members, which steps converting document and settings types build on.
[TestFixture]
public class CorePropertyHostingTests
{
    private sealed class Host : CoreObject
    {
        public Host() => ScanProperties<Host>();

        public IExpressionProperty<double> Value { get; } = new SimpleProperty<double>(1);

        public IListProperty<string> Items { get; } = new ListProperty<string>();
    }

    [Test]
    public void ACoreObject_RegistersAndOwnsItsProperties()
    {
        var host = new Host();

        using (Assert.EnterMultipleScope())
        {
            // Id and Name, which every object has, come after the type's own properties.
            Assert.That(host.Properties, Is.EqualTo(new IProperty[] { host.Value, host.Items, host.Id, host.Name }));
            Assert.That(host.Value.Name, Is.EqualTo(nameof(Host.Value)));
            Assert.That(host.Value.GetOwnerObject(), Is.SameAs(host));
            Assert.That(host.Items.GetOwnerObject(), Is.SameAs(host));
        }
    }

    [Test]
    public void APropertyEvaluatesAnExpressionWithAnEvaluationContext()
    {
        var host = new Host();
        host.Value.Expression = Expression.Create<double>("Time + 1");

        Assert.That(host.Value.Evaluate(new EvaluationContext(MediaTime.FromSeconds(2))), Is.EqualTo(3));
    }
}
