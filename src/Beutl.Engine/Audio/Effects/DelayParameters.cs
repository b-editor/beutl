namespace Beutl.Audio.Effects;

/// <summary>
/// Single source of truth for the <see cref="DelayEffect"/> / <see cref="Beutl.Audio.Graph.Nodes.DelayNode"/>
/// parameter ranges and defaults.
/// </summary>
/// <remarks>
/// The same constants drive the <c>[Range]</c> attributes on <see cref="DelayEffect"/> and the per-sample
/// processing in <c>DelayNode</c> (default fallbacks and the delay-line buffer sizing). Keeping them here
/// prevents the two declarations from drifting apart. Deliberately avoids <c>[ModuleInitializer]</c> so the
/// values stay plain compile-time constants usable inside attribute arguments.
///
/// The boundary constants intentionally keep their original literal types so the
/// <see cref="System.ComponentModel.DataAnnotations.RangeAttribute"/> overload resolution — and therefore the
/// resulting <c>OperandType</c> — is unchanged: the delay-time bounds were authored as <c>float</c>
/// (double operand) and the percentage bounds as <c>int</c> (int operand).
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
