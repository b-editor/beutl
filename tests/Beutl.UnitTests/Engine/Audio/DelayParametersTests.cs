using Beutl.Audio.Effects;

using static Beutl.UnitTests.Engine.Audio.AudioEffectAssertions;

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
        => AssertRangeIsConsistent(min, @default, max);
}
