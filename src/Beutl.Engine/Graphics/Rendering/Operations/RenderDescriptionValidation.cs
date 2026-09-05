using System.Reflection;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering.Requests;
using Beutl.Graphics.Shaders;

namespace Beutl.Graphics.Rendering;

internal static class RenderDescriptionValidation
{
    /// <summary>
    /// Binds an execution callback to the state one description carries.
    /// </summary>
    public static RenderExecutionChannel<TSession> CreateStateChannel<TSession, TState>(
        TState state,
        Action<TSession, TState> execute,
        string stateParameterName,
        string executeParameterName)
        where TState : notnull
    {
        ValidateStatePassingCallback(state, execute, stateParameterName, executeParameterName);
        return RenderExecutionChannel<TSession>.FromState(state, execute);
    }

    /// <summary>
    /// Enforces the state-passing rule: every per-recording value reaches the callback through its call state.
    /// </summary>
    public static void ValidateStatePassingCallback<TState>(
        TState state,
        Delegate execute,
        string stateParameterName,
        string executeParameterName)
        where TState : notnull
    {
        ArgumentNullException.ThrowIfNull(execute, executeParameterName);

        // typeof(TState).IsValueType is a JIT-time constant, so a value-typed state never reaches the
        // object-taking checks below and is never boxed on the recording path.
        if (!typeof(TState).IsValueType)
        {
            if (state is null)
                throw new ArgumentNullException(stateParameterName);

            ThrowIfExecutionFacadeIdentity(state, stateParameterName);
        }

    }

    public static RenderExecutionChannel<TSession> CreateRequestLocalChannel<TSession>(
        Action<TSession> execute,
        string executeParameterName)
    {
        ArgumentNullException.ThrowIfNull(execute, executeParameterName);
        return RenderExecutionChannel<TSession>.RequestLocal(execute);
    }

    /// <summary>
    /// A recorded query region is the whole region the operation reports to Measure and ROI, so a hit outside it
    /// is a hit no consumer sized itself for. A zero-area region reports nothing, yet every hit-testing kind can
    /// still answer true somewhere: <see cref="RenderHitTestContractKind.OutputBounds"/> because
    /// <see cref="Rect.Contains"/> is edge-inclusive and an empty rectangle still holds its own origin,
    /// <see cref="RenderHitTestContractKind.AnyInput"/> because it delegates to input regions the operation never
    /// declared, and <see cref="RenderHitTestContractKind.Custom"/> because the callback answers for any point at
    /// all. Only <see cref="RenderHitTestContractKind.None"/> is confined to an empty region.
    /// </summary>
    public static void ThrowIfQueryContributionIncoherent(
        Rect queryBounds,
        RenderHitTestContract hitTest,
        string parameterName)
    {
        if ((queryBounds.Width > 0 && queryBounds.Height > 0)
            || hitTest.Kind == RenderHitTestContractKind.None)
        {
            return;
        }

        throw new ArgumentException(
            "A zero-area queryBounds contributes no query region, so the hit-test contract must be "
            + "RenderHitTestContract.None.",
            parameterName);
    }

    public static void ValidatePureMetadataCallback(Delegate callback, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(callback);
        object? target = callback.Target;
        if (target is null)
            return;

        ThrowIfExecutionFacadeIdentity(target, parameterName);
        RenderIdentityKeyValidator.ThrowIfInvalid(target, parameterName);
    }

    /// <summary>What a metadata callback contributes to the structural identity of the operation holding it.</summary>
    /// <remarks>
    /// <para>
    /// The method, not the delegate. A structural identity says which plan a recording can be served by, and a
    /// plan is the shape of the work: what a callback answers is request data the plan is re-run over, which is
    /// why a recording that only resizes reuses its plan rather than compiling a second one. A callback the
    /// engine holds to being a pure function of its arguments therefore contributes which declaration it is and
    /// nothing about the instance it reads, so two nodes of one type that read different values of their own
    /// share the plan the way two calls of one static callback already do.
    /// </para>
    /// <para>
    /// This is confined to the callbacks <see cref="ValidatePureMetadataCallback"/> gates. An execution
    /// callback carries no such promise, so <see cref="StructuralIdentityOfExecution"/> reads its target
    /// before deciding the same way.
    /// </para>
    /// <para>
    /// <see cref="Delegate.Method"/> is cached by the runtime, so reading it allocates nothing.
    /// </para>
    /// </remarks>
    public static MethodInfo StructuralIdentityOf(Delegate callback) => callback.Method;

    /// <summary>What an execution callback contributes to the structural identity of the operation holding it.</summary>
    /// <remarks>
    /// <para>
    /// The method when the callback's target is the <see cref="RenderNode"/> that declared it, and the
    /// delegate otherwise. A metadata callback can take the method unconditionally because the engine holds
    /// it to being a pure function of its arguments and can therefore ignore what it reads. No such promise
    /// covers an execution callback, so what it closed over has to keep separating it - which is what the
    /// request-local overloads rest on: their callback closes over a recording, arrives with a compiler
    /// display class as its target, and keeps the fresh per-recording identity that bars it from a later
    /// request's cache lookup.
    /// </para>
    /// <para>
    /// A node is not something the callback closed over. It is the one target
    /// <see cref="RenderIdentityKeyValidator"/> admits, it is re-read on every recording rather than
    /// snapshotted at one, and what it holds is governed by <see cref="RenderNode.MarkChanged"/> - the same
    /// contract that already governs the state a non-capturing callback is handed, and the one BESG005
    /// reports an unmarked write against. So it belongs on the request-data side of the split the plan key
    /// draws: two nodes of one type share the shape of the work and re-run it over their own values, exactly
    /// as they already do for the metadata callbacks those nodes declare.
    /// </para>
    /// <para>
    /// A static callback is unaffected either way: the compiler caches one delegate per declaration, so the
    /// delegate was already as stable as the method.
    /// </para>
    /// </remarks>
    public static object StructuralIdentityOfExecution(Delegate callback)
        => callback.Target is RenderNode ? callback.Method : callback;

    /// <summary>
    /// Runs every per-element check a bare resource declaration owes - not null, still declarable - over the
    /// list it arrived in.
    /// </summary>
    /// <remarks>
    /// Nothing is copied: neither caller retains the list after creating one engine binding per entry.
    /// </remarks>
    public static void ThrowIfResourcesUndeclarable(
        IReadOnlyList<RenderResource> resources,
        string parameterName)
    {
        for (int index = 0; index < resources.Count; index++)
        {
            RenderResource? resource = resources[index];
            if (resource is null)
                throw new ArgumentException("A declared render resource cannot be null.", parameterName);
            if (resource.RegistrationState == RenderResourceRegistrationState.Released)
                throw new ArgumentException("A released render resource cannot be declared.", parameterName);
        }
    }

    /// <summary>
    /// Checks a description's bindings and copies them into the list the description keeps.
    /// </summary>
    /// <remarks>
    /// The one copy left on this path, and not a defensive one: what this returns outlives the call on the
    /// description built from it, so a caller mutating its own list afterwards would otherwise change what a
    /// recorded operation says it binds. The stability a read-only interface promises covers the length of
    /// the call, not the lifetime of the description built during it.
    /// </remarks>
    public static IReadOnlyList<RenderResourceBinding> CopyResourceBindings(
        IReadOnlyList<RenderResourceBinding>? resources,
        string parameterName)
    {
        // An empty list has nothing to check, and the recording paths reach this once per operation per
        // frame with no resources at all - by far the common case - so the copy is made only once there is
        // something to put in it.
        if (resources is null || resources.Count == 0)
            return Array.Empty<RenderResourceBinding>();

        ThrowIfBindingsUndeclarable(resources, parameterName);

        var copy = new RenderResourceBinding[resources.Count];
        for (int index = 0; index < copy.Length; index++)
            copy[index] = resources[index];

        return Array.AsReadOnly(copy);
    }

    /// <summary>
    /// Runs every per-element check a description's bindings owe - initialized, addressing a distinct slot,
    /// carrying a resource that can still be declared - over a list that already offers indexed reads.
    /// </summary>
    /// <remarks>
    /// The list is read where it lies. Resource declarations are short, so scanning the preceding bindings
    /// avoids allocating a set on the render path.
    /// </remarks>
    internal static void ThrowIfBindingsUndeclarable(
        IReadOnlyList<RenderResourceBinding> bindings,
        string parameterName)
    {
        for (int index = 0; index < bindings.Count; index++)
        {
            RenderResourceBinding binding = bindings[index];
            if (!binding.IsInitialized)
            {
                throw new ArgumentException(
                    "A declared render resource binding cannot be uninitialized.",
                    parameterName);
            }

            for (int bound = 0; bound < index; bound++)
            {
                if (ReferenceEquals(bindings[bound].SlotIdentity, binding.SlotIdentity))
                {
                    throw new ArgumentException(
                        "A render resource slot cannot be bound more than once.",
                        parameterName);
                }
            }

            ThrowIfUndeclarable(binding.Resource, parameterName);
        }
    }

    public static void ThrowIfUndeclarable(RenderResource resource, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(resource, parameterName);
        if (resource.RegistrationState == RenderResourceRegistrationState.Released)
            throw new ArgumentException("A released render resource cannot be declared.", parameterName);
    }

    public static void ThrowIfFiniteNonEmpty(Rect bounds, string parameterName)
    {
        RenderRectValidation.ThrowIfInvalidInput(bounds, parameterName);
        if (bounds.Width == 0 || bounds.Height == 0)
            throw new ArgumentException("Bounds must be non-empty.", parameterName);
    }

    private static void ThrowIfExecutionFacadeIdentity(object value, string parameterName)
    {
        if (value is RenderExecutionInput
            or RenderCallbackCanvas
            or OpaqueRenderSession
            or OpaqueRenderOutput
            or GeometrySession
            or ShaderExecutionContext
            or ShaderUniformWriter
            or ShaderResourceWriter
            or TargetScopeSession
            or TargetCommandSession
            or RawTargetScopeSession
            or RawTargetCommandSession)
        {
            throw new ArgumentException(
                "A persistent identity or pure metadata callback cannot retain an execution session or facade.",
                parameterName);
        }
    }
}
