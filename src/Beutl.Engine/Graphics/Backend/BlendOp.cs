namespace Beutl.Graphics.Backend;

/// <summary>
/// Specifies blend operations.
/// </summary>
public enum BlendOp
{
    /// <summary>
    /// Result = Source + Destination.
    /// </summary>
    Add = 0,

    /// <summary>
    /// Result = Source - Destination.
    /// </summary>
    Subtract = 1,

    /// <summary>
    /// Result = Destination - Source.
    /// </summary>
    ReverseSubtract = 2,

    /// <summary>
    /// Result = min(Source, Destination).
    /// </summary>
    Min = 3,

    /// <summary>
    /// Result = max(Source, Destination).
    /// </summary>
    Max = 4
}
