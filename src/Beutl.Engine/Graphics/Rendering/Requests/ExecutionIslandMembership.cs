namespace Beutl.Graphics.Rendering.Requests;

internal readonly record struct ExecutionIslandMembership(
    ExecutionIsland Island,
    bool IsTerminal);
