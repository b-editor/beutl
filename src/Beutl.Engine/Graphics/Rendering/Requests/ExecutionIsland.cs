using System.Collections.Immutable;
using Beutl.Graphics.Shaders;

namespace Beutl.Graphics.Rendering.Requests;

internal readonly struct ExecutionIsland
{
    public ExecutionIsland(
        int index,
        ImmutableArray<int> fragmentIndices,
        CompiledShaderRun? shaderRun = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        if (fragmentIndices.IsDefaultOrEmpty)
        {
            throw new ArgumentException(
                "An execution island must contain at least one fragment.",
                nameof(fragmentIndices));
        }
        foreach (int fragmentIndex in fragmentIndices)
            ArgumentOutOfRangeException.ThrowIfNegative(fragmentIndex, nameof(fragmentIndices));
        if (shaderRun is null && fragmentIndices.Length != 1)
        {
            throw new ArgumentException(
                "A non-Shader execution island must identify exactly one semantic fragment.",
                nameof(fragmentIndices));
        }
        if (shaderRun is not null
            && !fragmentIndices.AsSpan().SequenceEqual(shaderRun.StageFragmentIndices.AsSpan()))
        {
            throw new ArgumentException(
                "A Shader-run island must contain exactly its compiled stages in execution order.",
                nameof(fragmentIndices));
        }
        Index = index;
        FragmentIndices = fragmentIndices;
        ShaderRun = shaderRun;
    }

    public int Index { get; }

    public ImmutableArray<int> FragmentIndices { get; }

    public CompiledShaderRun? ShaderRun { get; }
}
