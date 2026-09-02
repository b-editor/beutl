using Beutl.Graphics.Effects;

namespace Beutl.Graphics.Rendering;

internal sealed record CompiledShaderStage(
    RenderFragmentId FragmentId,
    RenderFragmentReference Fragment,
    RenderFragmentKind Kind,
    ShaderDescription Description,
    SkslCoverageBehavior CoverageBehavior,
    int ProgramStageIndex);
