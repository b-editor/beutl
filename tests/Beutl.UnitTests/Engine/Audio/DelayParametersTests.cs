using Beutl.Audio.Effects;

namespace Beutl.UnitTests.Engine.Audio;

[TestFixture]
public class DelayParametersTests
{
    // (name, min, default, max) for every DelayEffect/DelayNode parameter.
    private static readonly object[] s_parameterCases =
    [
        new object[] { "DelayTime", (float)DelayParameters.DelayTimeMin, DelayParameters.DelayTimeDefault, (float)DelayParameters.DelayTimeMax },
        new object[] { "Feedback", (float)DelayParameters.FeedbackMin, DelayParameters.FeedbackDefault, (float)DelayParameters.FeedbackMax },
        new object[] { "DryMix", (float)DelayParameters.DryMixMin, DelayParameters.DryMixDefault, (float)DelayParameters.DryMixMax },
        new object[] { "WetMix", (float)DelayParameters.WetMixMin, DelayParameters.WetMixDefault, (float)DelayParameters.WetMixMax },
    ];

    [TestCaseSource(nameof(s_parameterCases))]
    public void Default_is_within_min_and_max(string name, float min, float @default, float max)
    {
        Assert.That(min, Is.LessThanOrEqualTo(@default), $"{name}: Min must be <= Default");
        Assert.That(@default, Is.LessThanOrEqualTo(max), $"{name}: Default must be <= Max");
        Assert.That(min, Is.LessThan(max), $"{name}: Min must be < Max");
    }

    // Characterization: lock the consolidated constants to the literals that DelayEffect/DelayNode
    // previously declared independently, so the single-source refactor stays behavior-preserving.
    [Test]
    public void Constants_match_the_original_literals()
    {
        Assert.Multiple(() =>
        {
            Assert.That(DelayParameters.DelayTimeMin, Is.EqualTo(0f));
            Assert.That(DelayParameters.DelayTimeDefault, Is.EqualTo(200f));
            Assert.That(DelayParameters.DelayTimeMax, Is.EqualTo(5000f));

            Assert.That(DelayParameters.FeedbackMin, Is.EqualTo(0));
            Assert.That(DelayParameters.FeedbackDefault, Is.EqualTo(50f));
            Assert.That(DelayParameters.FeedbackMax, Is.EqualTo(100));

            Assert.That(DelayParameters.DryMixMin, Is.EqualTo(0));
            Assert.That(DelayParameters.DryMixDefault, Is.EqualTo(60f));
            Assert.That(DelayParameters.DryMixMax, Is.EqualTo(100));

            Assert.That(DelayParameters.WetMixMin, Is.EqualTo(0));
            Assert.That(DelayParameters.WetMixDefault, Is.EqualTo(40f));
            Assert.That(DelayParameters.WetMixMax, Is.EqualTo(100));
        });
    }
}
