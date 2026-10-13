using System.Reflection;
using System.Runtime.CompilerServices;
using Beutl.Api.Services;
using Beutl.Editor;
using Beutl.Extensibility;
using Beutl.Graphics.Rendering;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.PublicApiContractTests;

[TestFixture]
public sealed class ProjectShapeContractTests
{
    [TestCase(typeof(RenderNode))]
    [TestCase(typeof(IContextCommandHandler))]
    [TestCase(typeof(AiModelCatalog))]
    [TestCase(typeof(HistoryManager))]
    [TestCase(typeof(CoreSerializer))]
    [TestCase(typeof(Element))]
    public void ReferencedAssembly_DoesNotGrantFriendAccessToContractAssembly(Type typeInAssembly)
    {
        AssertDoesNotHaveFriendAccess(typeInAssembly.Assembly);
    }

    private static void AssertDoesNotHaveFriendAccess(Assembly targetAssembly)
    {
        string contractAssemblyName = typeof(ProjectShapeContractTests).Assembly.GetName().Name!;
        string?[] friendAssemblyNames = targetAssembly
            .GetCustomAttributes<InternalsVisibleToAttribute>()
            .Select(static attribute => new AssemblyName(attribute.AssemblyName).Name)
            .ToArray();

        Assert.That(friendAssemblyNames, Does.Not.Contain(contractAssemblyName));
    }
}
