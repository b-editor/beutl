using Beutl.Graphics.Shaders;

namespace Beutl.Graphics.Rendering.Requests;

internal readonly record struct ExecutionIslandMembership(
    ExecutionIsland Island,
    CompiledShaderRun? ShaderRun,
    bool IsTerminal);
