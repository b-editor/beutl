namespace Beutl.Audio.Effects;

/// <summary>
/// Single source of truth for the <see cref="DelayEffect"/> / <see cref="Beutl.Audio.Graph.Nodes.DelayNode"/>
/// parameter ranges and defaults.
/// </summary>
/// <remarks>
/// Shared by the <c>[Range]</c> attributes on <see cref="DelayEffect"/> and the per-sample processing in
/// <c>DelayNode</c> so the two cannot drift. Plain constants (not <c>[ModuleInitializer]</c>) so they stay
/// usable in attribute arguments. The boundary constants keep their original literal types (delay-time as
/// <c>float</c>, percentages as <c>int</c>) so the
/// <see cref="System.ComponentModel.DataAnnotations.RangeAttribute"/> overload and <c>OperandType</c> are unchanged.
/// </remarks>
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
