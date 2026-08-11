using System.Reflection;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// The channel through which a description's per-frame values reach its deferred execution callback.
/// </summary>
/// <remarks>
/// The channel, not the description, decides whether the produced output can satisfy a later request's cache
/// lookup: a state channel publishes its state as the runtime identity, a request-local channel publishes none.
/// It is a struct holding the callback directly, so wrapping a plain callback costs no allocation.
/// </remarks>
internal readonly struct RenderExecutionChannel<TSession>
{
    private readonly Action<TSession>? _execute;

    // A state binding is its own identity, so one field carries both and the channel stays two words wide.
    private readonly object? _bindingOrIdentityKey;

    private RenderExecutionChannel(Action<TSession>? execute, object? bindingOrIdentityKey)
    {
        _execute = execute;
        _bindingOrIdentityKey = bindingOrIdentityKey;
    }

    /// <summary>Gets the output-cache runtime identity, or null when the output must stay request-local.</summary>
    internal object? IdentityKey => _bindingOrIdentityKey;

    /// <summary>Gets the callback method identity used when the author declares no structural key.</summary>
    internal MethodInfo Method => _execute is not null ? _execute.Method : Binding.Method;

    private RenderExecutionBinding<TSession> Binding
        => (RenderExecutionBinding<TSession>)_bindingOrIdentityKey!;

    internal static RenderExecutionChannel<TSession> FromState<TState>(
        TState state,
        Action<TSession, TState> execute)
        where TState : notnull
        => new(null, new StateRenderExecutionBinding<TSession, TState>(state, execute));

    internal static RenderExecutionChannel<TSession> RequestLocal(Action<TSession> execute)
        => new(execute, null);

    /// <summary>
    /// Creates a channel for a capturing callback under an identity the engine declares for it.
    /// </summary>
    /// <remarks>
    /// Reserved for engine-owned factories whose callback is assembled by a shared recorder helper and reaches
    /// request-scoped resources and a recorded paint plan, neither of which can be part of a persistent
    /// identity. Nothing outside the engine can reach this shape, so an out-of-tree author cannot declare an
    /// identity that omits what the callback draws with.
    /// </remarks>
    internal static RenderExecutionChannel<TSession> DeclaredIdentity(
        Action<TSession> execute,
        RenderRuntimeIdentity? runtimeIdentity)
        => new(execute, runtimeIdentity?.Key);

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
/// Invokes a non-capturing callback with the state that is also its complete runtime identity.
/// </summary>
/// <remarks>
/// Because the callback cannot capture, <typeparamref name="TState"/> is the only channel carrying a per-frame
/// value into it, so no value can shape the produced pixels without being part of the cache key. Cache identity
/// compares and hashes the complete field graph instead of trusting author-provided equality
/// members. The state is stored in its own field rather than as an identity object, so a value-typed state is
/// never boxed while the binding is recorded or invoked.
/// </remarks>
internal sealed class StateRenderExecutionBinding<TSession, TState> : RenderExecutionBinding<TSession>
    where TState : notnull
{
    private readonly RenderStateIdentity<TState> _identity;
    private readonly Action<TSession, TState> _execute;

    internal StateRenderExecutionBinding(TState state, Action<TSession, TState> execute)
    {
        _identity = new RenderStateIdentity<TState>(state);
        _execute = execute;
    }

    internal override MethodInfo Method => _execute.Method;

    internal override void Invoke(TSession session) => _execute(session, _identity.State);

    public override bool Equals(object? obj)
        => obj is StateRenderExecutionBinding<TSession, TState> other
           && _execute.Equals(other._execute)
           && _identity.Equals(other._identity);

    public override int GetHashCode()
        => HashCode.Combine(_execute, _identity);

    public override string ToString() => $"{typeof(TState).Name} state '{_identity.State}'";
}

internal readonly struct RenderStateIdentity<TState>(TState state) : IEquatable<RenderStateIdentity<TState>>
    where TState : notnull
{
    internal TState State { get; } = state;

    public bool Equals(RenderStateIdentity<TState> other)
        => RenderIdentityKeyValidator.StateEquals(State, other.State);

    public override bool Equals(object? obj)
        => obj is RenderStateIdentity<TState> other && Equals(other);

    public override int GetHashCode()
        => RenderIdentityKeyValidator.StateHashCode(State);
}
