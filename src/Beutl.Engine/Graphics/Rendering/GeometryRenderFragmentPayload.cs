using Beutl.Graphics.Effects;

namespace Beutl.Graphics.Rendering;

internal sealed record GeometryRenderFragmentPayload(
    GeometryDescription Description,
    FilterEffectWorkingScalePolicy? WorkingScalePolicy = null);
