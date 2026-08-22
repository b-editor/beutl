using System.Reflection;
using Beutl.Graphics.Rendering;

namespace Beutl.PublicApiContractTests;

[TestFixture]
public sealed class EngineResourceIdentityContractTests
{
    private const string EngineResourceIdentityName = "Beutl.Graphics.Rendering.EngineResourceIdentity";

    [Test]
    public void ResourceAndRuntimeIdentityTypes_AreNotAuthoringSurface()
    {
        Assembly engine = typeof(RenderNode).Assembly;
        string?[] exportedTypes = engine.GetExportedTypes().Select(static type => type.FullName).ToArray();

        Assert.Multiple(() =>
        {
            // Anchored on the live type: a guard spelled only as a string keeps passing after a rename or a
            // namespace move, which is exactly when the surface is most likely to slip out.
            Assert.That(
                engine.GetType(EngineResourceIdentityName, throwOnError: false),
                Is.Not.Null,
                "The engine-only resource identity helper was renamed or moved; retarget this guard.");
            Assert.That(exportedTypes, Does.Not.Contain(EngineResourceIdentityName));
            Assert.That(
                exportedTypes,
                Does.Not.Contain("Beutl.Graphics.Rendering.RenderRuntimeIdentity"),
                "RenderRuntimeIdentity was removed from the authoring surface and must not return.");
        });
    }
}
