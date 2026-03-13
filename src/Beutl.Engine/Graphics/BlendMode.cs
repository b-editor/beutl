using System.ComponentModel.DataAnnotations;
using Beutl.Language;

namespace Beutl.Graphics;

public enum BlendMode
{
    [Display(
        Name = nameof(GraphicsStrings.BlendMode_Clear),
        Description = nameof(GraphicsStrings.BlendMode_Clear_Description),
        ResourceType = typeof(GraphicsStrings)
    )]
    Clear = 0,

    [Display(
        Name = nameof(GraphicsStrings.BlendMode_Src),
        Description = nameof(GraphicsStrings.BlendMode_Src_Description),
        ResourceType = typeof(GraphicsStrings)
    )]
    Src = 1,

    [Display(
        Name = nameof(GraphicsStrings.BlendMode_Dst),
        Description = nameof(GraphicsStrings.BlendMode_Dst_Description),
        ResourceType = typeof(GraphicsStrings)
    )]
    Dst = 2,

    [Display(
        Name = nameof(GraphicsStrings.BlendMode_SrcOver),
        Description = nameof(GraphicsStrings.BlendMode_SrcOver_Description),
        ResourceType = typeof(GraphicsStrings)
    )]
    SrcOver = 3,

    [Display(
        Name = nameof(GraphicsStrings.BlendMode_DstOver),
        Description = nameof(GraphicsStrings.BlendMode_DstOver_Description),
        ResourceType = typeof(GraphicsStrings)
    )]
    DstOver = 4,

    [Display(
        Name = nameof(GraphicsStrings.BlendMode_SrcIn),
        Description = nameof(GraphicsStrings.BlendMode_SrcIn_Description),
        ResourceType = typeof(GraphicsStrings)
    )]
    SrcIn = 5,

    [Display(
        Name = nameof(GraphicsStrings.BlendMode_DstIn),
        Description = nameof(GraphicsStrings.BlendMode_DstIn_Description),
        ResourceType = typeof(GraphicsStrings)
    )]
    DstIn = 6,

    [Display(
        Name = nameof(GraphicsStrings.BlendMode_SrcOut),
        Description = nameof(GraphicsStrings.BlendMode_SrcOut_Description),
        ResourceType = typeof(GraphicsStrings)
    )]
    SrcOut = 7,

    [Display(
        Name = nameof(GraphicsStrings.BlendMode_DstOut),
        Description = nameof(GraphicsStrings.BlendMode_DstOut_Description),
        ResourceType = typeof(GraphicsStrings)
    )]
    DstOut = 8,

    [Display(
        Name = nameof(GraphicsStrings.BlendMode_SrcATop),
        Description = nameof(GraphicsStrings.BlendMode_SrcATop_Description),
        ResourceType = typeof(GraphicsStrings)
    )]
    SrcATop = 9,

    [Display(
        Name = nameof(GraphicsStrings.BlendMode_DstATop),
        Description = nameof(GraphicsStrings.BlendMode_DstATop_Description),
        ResourceType = typeof(GraphicsStrings)
    )]
    DstATop = 10,

    [Display(
        Name = nameof(GraphicsStrings.BlendMode_Xor),
        Description = nameof(GraphicsStrings.BlendMode_Xor_Description),
        ResourceType = typeof(GraphicsStrings)
    )]
    Xor = 11,

    [Display(
        Name = nameof(GraphicsStrings.BlendMode_Plus),
        Description = nameof(GraphicsStrings.BlendMode_Plus_Description),
        ResourceType = typeof(GraphicsStrings)
    )]
    Plus = 12,

    [Display(
        Name = nameof(GraphicsStrings.BlendMode_Modulate),
        Description = nameof(GraphicsStrings.BlendMode_Modulate_Description),
        ResourceType = typeof(GraphicsStrings)
    )]
    Modulate = 13,

    [Display(
        Name = nameof(GraphicsStrings.BlendMode_Screen),
        Description = nameof(GraphicsStrings.BlendMode_Screen_Description),
        ResourceType = typeof(GraphicsStrings)
    )]
    Screen = 14,

    [Display(
        Name = nameof(GraphicsStrings.BlendMode_Overlay),
        Description = nameof(GraphicsStrings.BlendMode_Overlay_Description),
        ResourceType = typeof(GraphicsStrings)
    )]
    Overlay = 15,

    [Display(
        Name = nameof(GraphicsStrings.BlendMode_Darken),
        Description = nameof(GraphicsStrings.BlendMode_Darken_Description),
        ResourceType = typeof(GraphicsStrings)
    )]
    Darken = 16,

    [Display(
        Name = nameof(GraphicsStrings.BlendMode_Lighten),
        Description = nameof(GraphicsStrings.BlendMode_Lighten_Description),
        ResourceType = typeof(GraphicsStrings)
    )]
    Lighten = 17,

    [Display(
        Name = nameof(GraphicsStrings.BlendMode_ColorDodge),
        Description = nameof(GraphicsStrings.BlendMode_ColorDodge_Description),
        ResourceType = typeof(GraphicsStrings)
    )]
    ColorDodge = 18,

    [Display(
        Name = nameof(GraphicsStrings.BlendMode_ColorBurn),
        Description = nameof(GraphicsStrings.BlendMode_ColorBurn_Description),
        ResourceType = typeof(GraphicsStrings)
    )]
    ColorBurn = 19,

    [Display(
        Name = nameof(GraphicsStrings.BlendMode_HardLight),
        Description = nameof(GraphicsStrings.BlendMode_HardLight_Description),
        ResourceType = typeof(GraphicsStrings)
    )]
    HardLight = 20,

    [Display(
        Name = nameof(GraphicsStrings.BlendMode_SoftLight),
        Description = nameof(GraphicsStrings.BlendMode_SoftLight_Description),
        ResourceType = typeof(GraphicsStrings)
    )]
    SoftLight = 21,

    [Display(
        Name = nameof(GraphicsStrings.BlendMode_Difference),
        Description = nameof(GraphicsStrings.BlendMode_Difference_Description),
        ResourceType = typeof(GraphicsStrings)
    )]
    Difference = 22,

    [Display(
        Name = nameof(GraphicsStrings.BlendMode_Exclusion),
        Description = nameof(GraphicsStrings.BlendMode_Exclusion_Description),
        ResourceType = typeof(GraphicsStrings)
    )]
    Exclusion = 23,

    [Display(
        Name = nameof(GraphicsStrings.BlendMode_Multiply),
        Description = nameof(GraphicsStrings.BlendMode_Multiply_Description),
        ResourceType = typeof(GraphicsStrings)
    )]
    Multiply = 24,

    [Display(
        Name = nameof(GraphicsStrings.BlendMode_Hue),
        Description = nameof(GraphicsStrings.BlendMode_Hue_Description),
        ResourceType = typeof(GraphicsStrings)
    )]
    Hue = 25,

    [Display(
        Name = nameof(GraphicsStrings.BlendMode_Saturation),
        Description = nameof(GraphicsStrings.BlendMode_Saturation_Description),
        ResourceType = typeof(GraphicsStrings)
    )]
    Saturation = 26,

    [Display(
        Name = nameof(GraphicsStrings.BlendMode_Color),
        Description = nameof(GraphicsStrings.BlendMode_Color_Description),
        ResourceType = typeof(GraphicsStrings)
    )]
    Color = 27,

    [Display(
        Name = nameof(GraphicsStrings.BlendMode_Luminosity),
        Description = nameof(GraphicsStrings.BlendMode_Luminosity_Description),
        ResourceType = typeof(GraphicsStrings)
    )]
    Luminosity = 28,
}
