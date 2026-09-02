using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;

namespace Beutl.Graphics.Shaders;

internal sealed record CompiledShaderStage(
    RenderFragmentId FragmentId,
    RenderFragmentReference Fragment,
    RenderFragmentKind Kind,
    ShaderDescription Description,
    SkslCoverageBehavior CoverageBehavior,
    int ProgramStageIndex);
