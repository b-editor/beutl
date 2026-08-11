using System.Reflection;
using Beutl.Graphics.Rendering;

namespace Beutl.PublicApiContractTests;

[TestFixture]
public sealed class EngineResourceIdentityContractTests
{
    [Test]
    public void ResourceAndRuntimeIdentityTypes_AreNotAuthoringSurface()
    {
        Assembly engine = typeof(RenderNode).Assembly;
        string[] removedTypes =
        [
            "Beutl.Engine.EngineResourceIdentity",
            "Beutl.Graphics.Rendering.RenderResourceIdentity",
            "Beutl.Graphics.Rendering.RenderRuntimeIdentity",
        ];

        string?[] exportedTypes = engine.GetExportedTypes().Select(static type => type.FullName).ToArray();
        Assert.Multiple(() =>
        {
            foreach (string removedType in removedTypes)
                Assert.That(exportedTypes, Does.Not.Contain(removedType), removedType);
        });
    }
}
