namespace Beutl.Audio.Effects.Equalizer;

/// <summary>
/// Defines the types of BiQuad filters.
/// </summary>
public enum BiQuadFilterType
{
    /// <summary>
    /// Low-pass filter - passes frequencies below the cutoff frequency.
    /// </summary>
    LowPass,

    /// <summary>
    /// High-pass filter - passes frequencies above the cutoff frequency.
    /// </summary>
    HighPass,

    /// <summary>
    /// Band-pass filter - passes frequencies around the center frequency.
    /// </summary>
    BandPass,

    /// <summary>
    /// Notch filter - cuts frequencies around the center frequency.
    /// </summary>
    Notch,

    /// <summary>
    /// Peaking filter - adjusts the gain at the center frequency (basic equalizer filter).
    /// </summary>
    Peak,

    /// <summary>
    /// Low-shelf filter - adjusts the gain of frequencies below the cutoff frequency.
    /// </summary>
    LowShelf,

    /// <summary>
    /// High-shelf filter - adjusts the gain of frequencies above the cutoff frequency.
    /// </summary>
    HighShelf
}
