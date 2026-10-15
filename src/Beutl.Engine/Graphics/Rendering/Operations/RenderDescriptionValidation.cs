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
    /// A hit outside the recorded query region is one no consumer sized itself for, and of the hit-testing kinds
    /// only <see cref="RenderHitTestContractKind.None"/> never answers true for a zero-area region.
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
    /// <remarks>Metadata callbacks are pure, so the identity is the declared method rather than the delegate.</remarks>
    public static MethodInfo StructuralIdentityOf(Delegate callback) => callback.Method;

    /// <summary>What an execution callback contributes to the structural identity of the operation holding it.</summary>
    /// <remarks>
    /// An execution callback is not held to be pure, so what it closed over keeps separating it: the identity is
    /// the delegate, except for a target that is the declaring <see cref="RenderNode"/>, which is re-read on every
    /// recording and governed by <see cref="RenderNode.MarkChanged"/>, so the method is enough.
    /// </remarks>
    public static object StructuralIdentityOfExecution(Delegate callback)
        => callback.Target is RenderNode ? callback.Method : callback;

    /// <summary>
    /// Checks a description's bindings and copies them into the list the description keeps.
    /// </summary>
    /// <remarks>The description keeps the list after the call returns, so the caller's list is copied.</remarks>
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

    /// <summary>Checks every binding, scanning earlier ones for a repeated slot so that no set is allocated.</summary>
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

    public static void ThrowUnlessFiniteNonEmpty(Rect bounds, string parameterName)
    {
        RenderRectValidation.ThrowIfInvalidInput(bounds, parameterName);
        if (bounds.Width == 0 || bounds.Height == 0)
            throw new ArgumentException("Bounds must be non-empty.", parameterName);
    }

    /// <summary>Copies authored input readbacks, rejecting an uninitialized entry.</summary>
    public static RenderInputReadback[] CopyInputReadbacks(
        IEnumerable<RenderInputReadback>? inputReadbacks)
    {
        if (inputReadbacks is null)
            return [];

        RenderInputReadback[] result = inputReadbacks.ToArray();
        foreach (RenderInputReadback inputReadback in result)
            inputReadback.ThrowIfUninitialized(nameof(inputReadbacks));
        return result;
    }

    /// <summary>
    /// Resolves the declared input readbacks against the authored input count: none declared means no readback
    /// for any input, otherwise the counts must agree.
    /// </summary>
    public static IReadOnlyList<RenderInputReadback> ResolveInputReadbacks(
        IReadOnlyList<RenderInputReadback> declared,
        int inputCount,
        string parameterName,
        string mismatchMessage)
    {
        if (declared.Count == 0)
            return Enumerable.Repeat(RenderInputReadback.None, inputCount).ToArray();
        if (declared.Count != inputCount)
            throw new ArgumentException(mismatchMessage, parameterName);
        return declared;
    }

    private static void ThrowIfExecutionFacadeIdentity(object value, string parameterName)
    {
        if (value is RenderExecutionInput
            or RenderCallbackCanvas
            or OpaqueRenderSession
            or OpaqueRenderOutput
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
