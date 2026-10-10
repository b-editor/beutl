using System.ComponentModel.DataAnnotations;
using Beutl.Language;

namespace Beutl.Graphics.Transitions;

/// <summary>
/// The outgoing clip fades out while the incoming clip fades in over it, the two summed so that opaque
/// clips stay opaque throughout.
/// </summary>
[Display(Name = nameof(GraphicsStrings.CrossDissolveTransition), ResourceType = typeof(GraphicsStrings))]
public sealed partial class CrossDissolveTransition : ClipTransition
{
    public CrossDissolveTransition()
    {
        ScanProperties<CrossDissolveTransition>();
    }
}
