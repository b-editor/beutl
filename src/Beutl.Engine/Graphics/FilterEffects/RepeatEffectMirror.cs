using System.ComponentModel.DataAnnotations;
using Beutl.Language;

namespace Beutl.Graphics.Effects;

public enum RepeatEffectMirror
{
    [Display(Name = nameof(GraphicsStrings.RepeatEffectMirror_None), ResourceType = typeof(GraphicsStrings))]
    None,

    [Display(Name = nameof(GraphicsStrings.RepeatEffectMirror_Columns), ResourceType = typeof(GraphicsStrings))]
    Columns,

    [Display(Name = nameof(GraphicsStrings.RepeatEffectMirror_Rows), ResourceType = typeof(GraphicsStrings))]
    Rows,

    [Display(Name = nameof(GraphicsStrings.RepeatEffectMirror_Both), ResourceType = typeof(GraphicsStrings))]
    Both,
}
