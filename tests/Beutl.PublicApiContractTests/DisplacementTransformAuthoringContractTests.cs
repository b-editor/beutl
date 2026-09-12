using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Media;

namespace Beutl.PublicApiContractTests;

/// <summary>
/// <see cref="DisplacementMapTransform"/> is the extension point <see cref="DisplacementMapEffect.Transform"/>
/// advertises. This project is not a friend of the engine assembly, so a derivation that compiles here and is
/// dispatched to by the effect is what proves the point is usable from a plugin.
/// </summary>
[TestFixture]
public sealed class DisplacementTransformAuthoringContractTests
{
    [Test]
    public void PluginTransform_IsLoweredByDisplacementMapEffect()
    {
        var transform = new PluginDisplacementTransform();
        transform.Strength.CurrentValue = 0.25f;
        var effect = new DisplacementMapEffect();
        effect.DisplacementMap.CurrentValue = new SolidColorBrush(Colors.White);
        effect.Transform.CurrentValue = transform;
        effect.SpreadMethod.CurrentValue = GradientSpreadMethod.Repeat;
        effect.Channel.CurrentValue = DisplacementMapChannel.Green;
        effect.Signed.CurrentValue = true;

        using DisplacementMapEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        var pluginResource = resource.Transform as PluginDisplacementTransform.Resource;
        Assert.That(pluginResource, Is.Not.Null, "The effect resource must snapshot the plugin transform.");

        using var context = new FilterEffectContext(new Rect(0, 0, 8, 8));
        int itemsBefore = context.CountItems();

        effect.ApplyTo(context, resource);

        Assert.Multiple(() =>
        {
            Assert.That(pluginResource!.Invocations, Is.EqualTo(1));
            Assert.That(pluginResource.LastDisplacementMap, Is.SameAs(resource.DisplacementMap));
            Assert.That(pluginResource.LastSpreadMethod, Is.EqualTo(GradientSpreadMethod.Repeat));
            Assert.That(pluginResource.LastChannel, Is.EqualTo(DisplacementMapChannel.Green));
            Assert.That(pluginResource.LastSigned, Is.True);
            Assert.That(pluginResource.LastStrength, Is.EqualTo(0.25f));
            Assert.That(
                context.CountItems(),
                Is.GreaterThan(itemsBefore),
                "The stage the plugin recorded must reach the effect context.");
        });
    }
}

/// <summary>A displacement transform authored outside the engine assembly.</summary>
public sealed partial class PluginDisplacementTransform : DisplacementMapTransform
{
    public PluginDisplacementTransform()
    {
        ScanProperties<PluginDisplacementTransform>();
    }

    public IProperty<float> Strength { get; } = Property.CreateAnimatable<float>();

    public partial class Resource
    {
        public int Invocations { get; private set; }

        public Brush.Resource? LastDisplacementMap { get; private set; }

        public GradientSpreadMethod LastSpreadMethod { get; private set; }

        public DisplacementMapChannel LastChannel { get; private set; }

        public bool LastSigned { get; private set; }

        public float LastStrength { get; private set; }

        public override void ApplyTo(
            Brush.Resource displacementMap, GradientSpreadMethod spreadMethod,
            DisplacementMapChannel channel, bool signed, FilterEffectContext context)
        {
            Invocations++;
            LastDisplacementMap = displacementMap;
            LastSpreadMethod = spreadMethod;
            LastChannel = channel;
            LastSigned = signed;
            LastStrength = Strength;

            // A public stage stands in for a real displacement shader. The contract under test is that the
            // effect hands its resolved arguments to an out-of-tree transform and records what it lowers.
            context.Brightness(Strength);
        }
    }
}
