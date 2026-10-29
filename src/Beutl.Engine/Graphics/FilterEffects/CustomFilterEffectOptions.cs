namespace Beutl.Graphics.Effects;

/// <summary>Execution requirements of an opaque custom effect.</summary>
[Flags]
internal enum CustomFilterEffectOptions
{
    None = 0,
    /// <summary>The callback owns the recorded suffix and explicitly chooses whether and how often to execute it.</summary>
    ControlsContinuation = 1,
    /// <summary>The callback reads or replaces the enclosing composition target. Recorded as ordered target work.</summary>
    AccessesTarget = 2,
}
