using System.ComponentModel.DataAnnotations;
using Beutl.Language;

namespace Beutl.Audio.Effects;

/// <summary>How <see cref="ResamplingEffect"/> fills the time between samples when it converts audio to the
/// project's sample rate.</summary>
public enum ResamplingMethod
{
    /// <summary>Holds each sample until the next one, as the simplest hardware does.</summary>
    [Display(Name = nameof(AudioStrings.ResamplingMethod_Hold), ResourceType = typeof(AudioStrings))]
    Hold,

    /// <summary>Draws a straight line between neighbouring samples.</summary>
    [Display(Name = nameof(AudioStrings.ResamplingMethod_Linear), ResourceType = typeof(AudioStrings))]
    Linear,

    /// <summary>A cubic through four samples whose slopes follow the neighbours on either side.</summary>
    [Display(Name = nameof(AudioStrings.ResamplingMethod_CatmullRom), ResourceType = typeof(AudioStrings))]
    CatmullRom,

    /// <summary>The cubic polynomial that passes through four samples.</summary>
    [Display(Name = nameof(AudioStrings.ResamplingMethod_Lagrange), ResourceType = typeof(AudioStrings))]
    Lagrange,

    /// <summary>A windowed sinc over 16 samples, which also takes out the frequencies the conversion would add or
    /// fold back.</summary>
    [Display(Name = nameof(AudioStrings.ResamplingMethod_Sinc16), ResourceType = typeof(AudioStrings))]
    Sinc16,

    /// <summary>A windowed sinc over 64 samples, with a steeper and deeper cut than 16.</summary>
    [Display(Name = nameof(AudioStrings.ResamplingMethod_Sinc64), ResourceType = typeof(AudioStrings))]
    Sinc64,
}
