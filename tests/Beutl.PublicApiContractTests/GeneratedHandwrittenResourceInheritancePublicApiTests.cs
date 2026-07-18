using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Particles;

namespace Beutl.PublicApiContractTests;

[TestFixture]
public sealed class GeneratedHandwrittenResourceInheritancePublicApiTests
{
    [Test]
    public void ShakeDerivedOwner_UsesGeneratedTypedResourceExtension()
    {
        var owner = new PublicGeneratedShakeEffect();
        owner.Marker.CurrentValue = 12.5f;
        using PublicGeneratedShakeEffect.Resource resource = owner.ToResource(CompositionContext.Default);

        Assert.Multiple(() =>
        {
            Assert.That(resource.Marker, Is.EqualTo(12.5f));
            Assert.That(resource.PostUpdateCount, Is.EqualTo(1));
            Assert.That(resource.GetOriginal(), Is.SameAs(owner));
        });

        AssertWrongOwnerDoesNotPublish(resource, owner, new ShakeEffect());
        Assert.That(resource.PostUpdateCount, Is.EqualTo(1));
    }

    [Test]
    public void DelayDerivedOwner_UsesGeneratedTypedResourceExtension()
    {
        var owner = new PublicGeneratedDelayAnimationEffect();
        owner.Marker.CurrentValue = 23.5f;
        using PublicGeneratedDelayAnimationEffect.Resource resource = owner.ToResource(CompositionContext.Default);

        Assert.Multiple(() =>
        {
            Assert.That(resource.Marker, Is.EqualTo(23.5f));
            Assert.That(resource.PostUpdateCount, Is.EqualTo(1));
            Assert.That(resource.GetOriginal(), Is.SameAs(owner));
        });

        AssertWrongOwnerDoesNotPublish(resource, owner, new DelayAnimationEffect());
        Assert.That(resource.PostUpdateCount, Is.EqualTo(1));
    }

    [Test]
    public void ParticleDerivedOwner_UsesGeneratedTypedResourceExtension()
    {
        var owner = new PublicGeneratedParticleEmitter();
        owner.Marker.CurrentValue = 34.5f;
        using PublicGeneratedParticleEmitter.Resource resource = owner.ToResource(CompositionContext.Default);

        Assert.Multiple(() =>
        {
            Assert.That(resource.Marker, Is.EqualTo(34.5f));
            Assert.That(resource.PostUpdateCount, Is.EqualTo(1));
            Assert.That(resource.GetOriginal(), Is.SameAs(owner));
        });

        AssertWrongOwnerDoesNotPublish(resource, owner, new ParticleEmitter());
        Assert.That(resource.PostUpdateCount, Is.EqualTo(1));
    }

    private static void AssertWrongOwnerDoesNotPublish(
        EngineObject.Resource resource,
        EngineObject expectedOwner,
        EngineObject wrongOwner)
    {
        Assert.Throws<InvalidCastException>(() =>
        {
            bool updateOnly = false;
            resource.Update(wrongOwner, CompositionContext.Default, ref updateOnly);
        });
        Assert.That(resource.GetOriginal(), Is.SameAs(expectedOwner),
            "owner compatibility must be checked before the published original changes");
    }
}

public sealed partial class PublicGeneratedShakeEffect : ShakeEffect
{
    public PublicGeneratedShakeEffect()
    {
        ScanProperties<PublicGeneratedShakeEffect>();
    }

    public IProperty<float> Marker { get; } = Property.Create(0f);

    public partial class Resource
    {
        public int PostUpdateCount { get; private set; }

        partial void PostUpdate(PublicGeneratedShakeEffect obj, CompositionContext context)
        {
            PostUpdateCount++;
        }
    }
}

public sealed partial class PublicGeneratedDelayAnimationEffect : DelayAnimationEffect
{
    public PublicGeneratedDelayAnimationEffect()
    {
        ScanProperties<PublicGeneratedDelayAnimationEffect>();
    }

    public IProperty<float> Marker { get; } = Property.Create(0f);

    public partial class Resource
    {
        public int PostUpdateCount { get; private set; }

        partial void PostUpdate(PublicGeneratedDelayAnimationEffect obj, CompositionContext context)
        {
            PostUpdateCount++;
        }
    }
}

public sealed partial class PublicGeneratedParticleEmitter : ParticleEmitter
{
    public PublicGeneratedParticleEmitter()
    {
        ScanProperties<PublicGeneratedParticleEmitter>();
    }

    public IProperty<float> Marker { get; } = Property.Create(0f);

    public partial class Resource
    {
        public int PostUpdateCount { get; private set; }

        partial void PostUpdate(PublicGeneratedParticleEmitter obj, CompositionContext context)
        {
            PostUpdateCount++;
        }
    }
}
