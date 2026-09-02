using Beutl.Graphics.Effects;
using Beutl.Graphics.Shaders;

namespace Beutl.Graphics.Rendering;

internal sealed record OpacityRenderFragmentPayload(
    float Opacity,
    ShaderDescription FusionDescription);
