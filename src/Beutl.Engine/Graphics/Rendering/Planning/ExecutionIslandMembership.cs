namespace Beutl.Graphics.Rendering;

internal readonly record struct ExecutionIslandMembership(
    ExecutionIsland Island,
    CompiledShaderRun? ShaderRun,
    bool IsTerminal);
