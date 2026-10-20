using System.Reflection;
using Beutl.Extensibility;
using Beutl.ProjectSystem;

namespace Beutl.PublicApiContractTests;

// The property adapter interfaces live in Beutl.Engine, in the Beutl.Extensibility namespace, so that the document
// model does not reference Beutl.Extensibility and Beutl.Extensibility can later reference it (#2878).
[TestFixture]
public sealed class PropertyAdapterAssemblyContractTests
{
    [TestCase(typeof(IPropertyAdapter))]
    [TestCase(typeof(IPropertyAdapter<>))]
    [TestCase(typeof(IAnimatablePropertyAdapter))]
    [TestCase(typeof(IAnimatablePropertyAdapter<>))]
    [TestCase(typeof(IExpressionPropertyAdapter))]
    [TestCase(typeof(IExpressionPropertyAdapter<>))]
    public void Adapter_interfaces_are_defined_in_Beutl_Engine(Type type)
    {
        Assert.Multiple(() =>
        {
            Assert.That(type.Assembly.GetName().Name, Is.EqualTo("Beutl.Engine"));
            Assert.That(type.Namespace, Is.EqualTo("Beutl.Extensibility"));
        });
    }

    [Test]
    public void ProjectSystem_does_not_reference_Beutl_Extensibility()
    {
        AssemblyName[] references = typeof(Scene).Assembly.GetReferencedAssemblies();

        Assert.That(references.Select(name => name.Name), Has.No.Member("Beutl.Extensibility"));
    }
}
