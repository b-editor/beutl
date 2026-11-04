using System.ComponentModel.DataAnnotations;
using Beutl.Language;

namespace Beutl.Graphics.Effects;

public enum EdgeDetectionOriginal
{
    /// <summary>Only the lines.</summary>
    [Display(Name = nameof(GraphicsStrings.EdgeDetectionOriginal_Hidden), ResourceType = typeof(GraphicsStrings))]
    Hidden,

    /// <summary>The picture behind the lines.</summary>
    [Display(Name = nameof(GraphicsStrings.EdgeDetectionOriginal_Behind), ResourceType = typeof(GraphicsStrings))]
    Behind,

    /// <summary>The picture in front of the lines, which show only around it and through its transparent parts.</summary>
    [Display(Name = nameof(GraphicsStrings.EdgeDetectionOriginal_InFront), ResourceType = typeof(GraphicsStrings))]
    InFront,
}
