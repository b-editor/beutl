using System.ComponentModel.DataAnnotations;
using Beutl.Language;

namespace Beutl.Graphics;

public enum DrawableRepeaterMirror
{
    [Display(Name = nameof(GraphicsStrings.DrawableRepeaterMirror_None), ResourceType = typeof(GraphicsStrings))]
    None,

    [Display(Name = nameof(GraphicsStrings.DrawableRepeaterMirror_Columns), ResourceType = typeof(GraphicsStrings))]
    Columns,

    [Display(Name = nameof(GraphicsStrings.DrawableRepeaterMirror_Rows), ResourceType = typeof(GraphicsStrings))]
    Rows,

    [Display(Name = nameof(GraphicsStrings.DrawableRepeaterMirror_Both), ResourceType = typeof(GraphicsStrings))]
    Both,
}
