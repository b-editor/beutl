using Beutl.Graphics.Effects;

namespace Beutl.Graphics.Rendering;

internal sealed record OpacityRenderFragmentPayload(
    float Opacity,
    ShaderDescription FusionDescription);
