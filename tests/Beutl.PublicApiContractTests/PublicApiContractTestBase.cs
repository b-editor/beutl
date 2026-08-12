using System.Reflection;
using System.Runtime.CompilerServices;
using Beutl.Graphics.Rendering;

namespace Beutl.PublicApiContractTests;

public abstract class PublicApiContractTestBase
{
    protected static void AssertDoesNotHaveFriendAccess(Assembly targetAssembly)
    {
        string contractAssemblyName = typeof(PublicApiContractTestBase).Assembly.GetName().Name!;
        string?[] friendAssemblyNames = targetAssembly
            .GetCustomAttributes<InternalsVisibleToAttribute>()
            .Select(static attribute => new AssemblyName(attribute.AssemblyName).Name)
            .ToArray();

        Assert.That(friendAssemblyNames, Does.Not.Contain(contractAssemblyName));
    }
}

[TestFixture]
public sealed class ProjectShapeContractTests : PublicApiContractTestBase
{
    [Test]
    public void Engine_DoesNotGrantFriendAccessToContractAssembly()
    {
        AssertDoesNotHaveFriendAccess(typeof(RenderNode).Assembly);
    }
}
