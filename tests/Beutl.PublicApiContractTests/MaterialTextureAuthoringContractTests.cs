using System.Numerics;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics.Backend;
using Beutl.Graphics3D;
using Beutl.Graphics3D.Materials;
using Beutl.Graphics3D.Textures;

namespace Beutl.PublicApiContractTests;

[TestFixture]
public sealed class MaterialTextureAuthoringContractTests : PublicApiContractTestBase
{
    [Test]
    public void MaterialResource_TextureEnumerationRemainsAnOptionalExtensionPoint()
    {
        AssertDoesNotHaveFriendAccess(typeof(Material3D).Assembly);
        var material = new PluginMaterial();
        using PluginMaterial.Resource evaluated = material.ToResource(CompositionContext.Default);
        using var texture = new DrawableTextureSource.Resource();
        using var resource = new PluginMaterial.Resource(texture);

        Assert.Multiple(() =>
        {
            Assert.That(typeof(MaterialResourceUsingDefaultTextureEnumeration).IsAbstract, Is.True);
            Assert.That(typeof(PluginMaterial).IsAbstract, Is.False);
            Assert.That(typeof(PluginMaterial.Resource).IsAbstract, Is.False);
            Assert.That(evaluated, Is.TypeOf<PluginMaterial.Resource>());
            Assert.That(resource.DeclaredTextures, Is.EqualTo(new TextureSource.Resource[] { texture }));
        });
    }

    private abstract class MaterialResourceUsingDefaultTextureEnumeration : Material3D.Resource;

    [SuppressResourceClassGeneration]
    private sealed partial class PluginMaterial : Material3D
    {
        public override Resource ToResource(CompositionContext context)
        {
            var resource = new Resource(null);
            bool updateOnly = false;
            resource.Update(this, context, ref updateOnly);
            return resource;
        }

        public new sealed class Resource(DrawableTextureSource.Resource? texture) : Material3D.Resource
        {
            protected override IPipeline3D? Pipeline => null;

            public TextureSource.Resource[] DeclaredTextures => [.. EnumerateTextureSources()];

            protected override IEnumerable<TextureSource.Resource> EnumerateTextureSources()
            {
                if (texture is not null)
                    yield return texture;
            }

            public override void EnsurePipeline(RenderContext3D context)
            {
            }

            public override void Bind(RenderContext3D context, Object3D.Resource obj, Matrix4x4 worldMatrix)
            {
            }
        }
    }
}
