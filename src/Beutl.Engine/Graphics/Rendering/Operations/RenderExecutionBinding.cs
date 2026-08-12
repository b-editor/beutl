using System.Reflection;

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

    /// <summary>Gets the callback method used to derive an internal definition fingerprint.</summary>
    internal MethodInfo Method => _execute is not null ? _execute.Method : Binding.Method;

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

internal abstract class RenderExecutionBinding<TSession>
{
    internal abstract MethodInfo Method { get; }

    internal abstract void Invoke(TSession session);
}

/// <summary>
/// Invokes a non-capturing callback with its call-owned state.
/// </summary>
/// <remarks>
/// The state is stored in its own field so a value-typed state is never boxed while the binding is recorded or
/// invoked. A node must set <see cref="RenderNode.HasChanges"/> before a changed state can alter cached output.
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

    internal override MethodInfo Method => _execute.Method;

    internal override void Invoke(TSession session) => _execute(session, _state);
}
