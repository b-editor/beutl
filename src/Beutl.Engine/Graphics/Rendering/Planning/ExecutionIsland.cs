using System.Collections.Immutable;

namespace Beutl.Graphics.Rendering;

internal sealed class ExecutionIsland
{
    public ExecutionIsland(
        ExecutionIslandId id,
        ExecutionIslandKind kind,
        ImmutableArray<RenderFragmentId> fragments,
        bool plansGpuPass,
        CompiledShaderRun? shaderRun = null)
    {
        if (id.Value <= 0)
            throw new ArgumentOutOfRangeException(nameof(id));
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (fragments.IsDefaultOrEmpty)
            throw new ArgumentException("An execution island must contain at least one fragment.", nameof(fragments));
        if ((kind == ExecutionIslandKind.ShaderRun) != (shaderRun is not null))
        {
            throw new ArgumentException(
                "Only Shader-run islands carry a compiled Shader run.",
                nameof(shaderRun));
        }
        if (kind == ExecutionIslandKind.ShaderRun && !plansGpuPass)
            throw new ArgumentException("A Shader-run island must plan one GPU pass.", nameof(plansGpuPass));

        Id = id;
        Kind = kind;
        Fragments = fragments;
        PlansGpuPass = plansGpuPass;
        ShaderRun = shaderRun;
    }

    public ExecutionIslandId Id { get; }

    public ExecutionIslandKind Kind { get; }

    public ImmutableArray<RenderFragmentId> Fragments { get; }

    public bool PlansGpuPass { get; }

    public CompiledShaderRun? ShaderRun { get; }
}
