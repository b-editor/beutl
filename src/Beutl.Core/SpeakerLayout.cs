using System.ComponentModel.DataAnnotations;
using Beutl.Language;

namespace Beutl;

public enum SpeakerLayout
{
    [Display(Name = nameof(Strings.SpeakerLayout_Mono), ResourceType = typeof(Strings))]
    Mono,
    [Display(Name = nameof(Strings.SpeakerLayout_Stereo), ResourceType = typeof(Strings))]
    Stereo,
    [Display(Name = nameof(Strings.SpeakerLayout_Surround51), ResourceType = typeof(Strings))]
    Surround51,
    [Display(Name = nameof(Strings.SpeakerLayout_Surround71), ResourceType = typeof(Strings))]
    Surround71
}
