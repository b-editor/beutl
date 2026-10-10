using System.ComponentModel.DataAnnotations;
using Beutl.Language;

namespace Beutl.Graphics.Transitions;

public enum ClipTransitionOrientation
{
    // A vertical band through the centre widens to the left and right.
    [Display(Name = nameof(GraphicsStrings.ClipTransitionOrientation_Horizontal), ResourceType = typeof(GraphicsStrings))]
    Horizontal,

    // A horizontal band through the centre widens up and down.
    [Display(Name = nameof(GraphicsStrings.ClipTransitionOrientation_Vertical), ResourceType = typeof(GraphicsStrings))]
    Vertical,
}
