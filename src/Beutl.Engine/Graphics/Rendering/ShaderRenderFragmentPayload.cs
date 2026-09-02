using Beutl.Graphics.Effects;

namespace Beutl.Graphics.Rendering;

internal sealed record ShaderRenderFragmentPayload(
    ShaderDescription Description,
    FilterEffectWorkingScalePolicy? WorkingScalePolicy = null);
