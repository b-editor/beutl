namespace Beutl.Graphics.Rendering;

/// <summary>
/// The channel through which a description's per-frame values reach its deferred execution callback.
/// </summary>
/// <remarks>
/// It carries callback state only. Persistent-output reuse is governed by the owning node's
/// <see cref="RenderNode.HasChanges"/> lifecycle rather than by callback values.
/// </remarks>
internal readonly struct RenderExecutionChannel<TSession>
{
    private readonly Action<TSession>? _execute;

    private readonly RenderExecutionBinding<TSession>? _binding;

    private RenderExecutionChannel(Action<TSession>? execute, RenderExecutionBinding<TSession>? binding)
    {
        _execute = execute;
        _binding = binding;
    }

    private RenderExecutionBinding<TSession> Binding
        => _binding ?? throw new InvalidOperationException("The execution channel has no state binding.");

    internal static RenderExecutionChannel<TSession> FromState<TState>(
        TState state,
        Action<TSession, TState> execute)
        where TState : notnull
        => new(null, new StateRenderExecutionBinding<TSession, TState>(state, execute));

    internal static RenderExecutionChannel<TSession> RequestLocal(Action<TSession> execute)
        => new(execute, null);

    internal void Invoke(TSession session)
    {
        if (_execute is not null)
            _execute(session);
        else
            Binding.Invoke(session);
    }
}
