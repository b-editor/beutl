namespace Beutl.Audio.Effects;

// Ranges and defaults shared by DelayEffect's [Range] attributes and DelayNode.
internal static class DelayParameters
{
    // Delay time, in milliseconds. (double-operand range)
    public const float DelayTimeMin = 0f;
    public const float DelayTimeDefault = 200f;
    public const float DelayTimeMax = 5000f; // 5 seconds in milliseconds

    // Feedback amount, as a percentage (0-100). (int-operand range)
    public const int FeedbackMin = 0;
    public const float FeedbackDefault = 50f;
    public const int FeedbackMax = 100;

    // Dry-signal mix, as a percentage (0-100). (int-operand range)
    public const int DryMixMin = 0;
    public const float DryMixDefault = 60f;
    public const int DryMixMax = 100;

    // Wet-signal mix, as a percentage (0-100). (int-operand range)
    public const int WetMixMin = 0;
    public const float WetMixDefault = 40f;
    public const int WetMixMax = 100;
}
