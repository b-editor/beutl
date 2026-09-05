namespace Beutl.Graphics.Rendering;

internal abstract class RenderExecutionBinding<TSession>
{
    internal abstract void Invoke(TSession session);
}

/// <summary>
/// Invokes a non-capturing callback with its call-owned state.
/// </summary>
/// <remarks>
/// The state is stored in its own field so a value-typed state is never boxed while the binding is recorded or
/// invoked. A node must call <see cref="RenderNode.MarkChanged"/> before a changed state can alter cached output.
/// </remarks>
internal sealed class StateRenderExecutionBinding<TSession, TState> : RenderExecutionBinding<TSession>
    where TState : notnull
{
    private readonly TState _state;
    private readonly Action<TSession, TState> _execute;

    internal StateRenderExecutionBinding(TState state, Action<TSession, TState> execute)
    {
        _state = state;
        _execute = execute;
    }

    internal override void Invoke(TSession session) => _execute(session, _state);
}
