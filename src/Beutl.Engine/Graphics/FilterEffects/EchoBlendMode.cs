using System.ComponentModel.DataAnnotations;
using Beutl.Language;

namespace Beutl.Graphics.Effects;

public enum EchoBlendMode
{
    [Display(Name = nameof(GraphicsStrings.EchoBlendMode_NewestOnTop), ResourceType = typeof(GraphicsStrings))]
    NewestOnTop,

    [Display(Name = nameof(GraphicsStrings.EchoBlendMode_Add), ResourceType = typeof(GraphicsStrings))]
    Add,
}
