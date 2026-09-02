using Beutl.Graphics.Effects;
using Beutl.Graphics.Shaders;

namespace Beutl.Graphics.Rendering;

internal sealed record ShaderRenderFragmentPayload(
    ShaderDescription Description,
    FilterEffectWorkingScalePolicy? WorkingScalePolicy = null);
