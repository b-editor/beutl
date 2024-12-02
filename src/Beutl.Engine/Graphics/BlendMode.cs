using System.ComponentModel.DataAnnotations;
using Beutl.Language;

namespace Beutl.Graphics;

public enum BlendMode
{
    [Display(
        Name = nameof(Strings.BlendMode_Clear),
        Description = nameof(Strings.BlendMode_Clear_Description),
        ResourceType = typeof(Strings)
    )]
    Clear = 0,

    [Display(
        Name = nameof(Strings.BlendMode_Src),
        Description = nameof(Strings.BlendMode_Src_Description),
        ResourceType = typeof(Strings)
    )]
    Src = 1,

    [Display(
        Name = nameof(Strings.BlendMode_Dst),
        Description = nameof(Strings.BlendMode_Dst_Description),
        ResourceType = typeof(Strings)
    )]
    Dst = 2,

    [Display(
        Name = nameof(Strings.BlendMode_SrcOver),
        Description = nameof(Strings.BlendMode_SrcOver_Description),
        ResourceType = typeof(Strings)
    )]
    SrcOver = 3,

    [Display(
        Name = nameof(Strings.BlendMode_DstOver),
        Description = nameof(Strings.BlendMode_DstOver_Description),
        ResourceType = typeof(Strings)
    )]
    DstOver = 4,

    [Display(
        Name = nameof(Strings.BlendMode_SrcIn),
        Description = nameof(Strings.BlendMode_SrcIn_Description),
        ResourceType = typeof(Strings)
    )]
    SrcIn = 5,

    [Display(
        Name = nameof(Strings.BlendMode_DstIn),
        Description = nameof(Strings.BlendMode_DstIn_Description),
        ResourceType = typeof(Strings)
    )]
    DstIn = 6,

    [Display(
        Name = nameof(Strings.BlendMode_SrcOut),
        Description = nameof(Strings.BlendMode_SrcOut_Description),
        ResourceType = typeof(Strings)
    )]
    SrcOut = 7,

    [Display(
        Name = nameof(Strings.BlendMode_DstOut),
        Description = nameof(Strings.BlendMode_DstOut_Description),
        ResourceType = typeof(Strings)
    )]
    DstOut = 8,

    [Display(
        Name = nameof(Strings.BlendMode_SrcATop),
        Description = nameof(Strings.BlendMode_SrcATop_Description),
        ResourceType = typeof(Strings)
    )]
    SrcATop = 9,

    [Display(
        Name = nameof(Strings.BlendMode_DstATop),
        Description = nameof(Strings.BlendMode_DstATop_Description),
        ResourceType = typeof(Strings)
    )]
    DstATop = 10,

    [Display(
        Name = nameof(Strings.BlendMode_Xor),
        Description = nameof(Strings.BlendMode_Xor_Description),
        ResourceType = typeof(Strings)
    )]
    Xor = 11,

    [Display(
        Name = nameof(Strings.BlendMode_Plus),
        Description = nameof(Strings.BlendMode_Plus_Description),
        ResourceType = typeof(Strings)
    )]
    Plus = 12,

    [Display(
        Name = nameof(Strings.BlendMode_Modulate),
        Description = nameof(Strings.BlendMode_Modulate_Description),
        ResourceType = typeof(Strings)
    )]
    Modulate = 13,

    [Display(
        Name = nameof(Strings.BlendMode_Screen),
        Description = nameof(Strings.BlendMode_Screen_Description),
        ResourceType = typeof(Strings)
    )]
    Screen = 14,

    [Display(
        Name = nameof(Strings.BlendMode_Overlay),
        Description = nameof(Strings.BlendMode_Overlay_Description),
        ResourceType = typeof(Strings)
    )]
    Overlay = 15,

    [Display(
        Name = nameof(Strings.BlendMode_Darken),
        Description = nameof(Strings.BlendMode_Darken_Description),
        ResourceType = typeof(Strings)
    )]
    Darken = 16,

    [Display(
        Name = nameof(Strings.BlendMode_Lighten),
        Description = nameof(Strings.BlendMode_Lighten_Description),
        ResourceType = typeof(Strings)
    )]
    Lighten = 17,

    [Display(
        Name = nameof(Strings.BlendMode_ColorDodge),
        Description = nameof(Strings.BlendMode_ColorDodge_Description),
        ResourceType = typeof(Strings)
    )]
    ColorDodge = 18,

    [Display(
        Name = nameof(Strings.BlendMode_ColorBurn),
        Description = nameof(Strings.BlendMode_ColorBurn_Description),
        ResourceType = typeof(Strings)
    )]
    ColorBurn = 19,

    [Display(
        Name = nameof(Strings.BlendMode_HardLight),
        Description = nameof(Strings.BlendMode_HardLight_Description),
        ResourceType = typeof(Strings)
    )]
    HardLight = 20,

    [Display(
        Name = nameof(Strings.BlendMode_SoftLight),
        Description = nameof(Strings.BlendMode_SoftLight_Description),
        ResourceType = typeof(Strings)
    )]
    SoftLight = 21,

    [Display(
        Name = nameof(Strings.BlendMode_Difference),
        Description = nameof(Strings.BlendMode_Difference_Description),
        ResourceType = typeof(Strings)
    )]
    Difference = 22,

    [Display(
        Name = nameof(Strings.BlendMode_Exclusion),
        Description = nameof(Strings.BlendMode_Exclusion_Description),
        ResourceType = typeof(Strings)
    )]
    Exclusion = 23,

    [Display(
        Name = nameof(Strings.BlendMode_Multiply),
        Description = nameof(Strings.BlendMode_Multiply_Description),
        ResourceType = typeof(Strings)
    )]
    Multiply = 24,

    [Display(
        Name = nameof(Strings.BlendMode_Hue),
        Description = nameof(Strings.BlendMode_Hue_Description),
        ResourceType = typeof(Strings)
    )]
    Hue = 25,

    [Display(
        Name = nameof(Strings.BlendMode_Saturation),
        Description = nameof(Strings.BlendMode_Saturation_Description),
        ResourceType = typeof(Strings)
    )]
    Saturation = 26,

    [Display(
        Name = nameof(Strings.BlendMode_Color),
        Description = nameof(Strings.BlendMode_Color_Description),
        ResourceType = typeof(Strings)
    )]
    Color = 27,

    [Display(
        Name = nameof(Strings.BlendMode_Luminosity),
        Description = nameof(Strings.BlendMode_Luminosity_Description),
        ResourceType = typeof(Strings)
    )]
    Luminosity = 28,
}
