using System.Collections.Immutable;
using Beutl.Graphics.Shaders;

namespace Beutl.Graphics.Rendering.Requests;

internal sealed class ExecutionIsland
{
    public ExecutionIsland(
        ExecutionIslandId id,
        ExecutionIslandKind kind,
        ImmutableArray<RenderFragmentId> fragments,
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
        Id = id;
        Kind = kind;
        Fragments = fragments;
        ShaderRun = shaderRun;
    }

    public ExecutionIslandId Id { get; }

    public ExecutionIslandKind Kind { get; }

    public ImmutableArray<RenderFragmentId> Fragments { get; }

    public CompiledShaderRun? ShaderRun { get; }
}
