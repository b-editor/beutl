namespace Beutl.Engine;

/// <summary>Specifies how an expression extends a keyframed animation.</summary>
public enum LoopMode
{
    /// <summary>Repeats the keyframe range in the same direction.</summary>
    Cycle,

    /// <summary>Alternates between playing the range forward and backward.</summary>
    PingPong,

    /// <summary>Repeats the range with the change in value carried across cycles.</summary>
    Offset,

    /// <summary>Extrapolates from the boundary using its sampled velocity.</summary>
    Continue
}
