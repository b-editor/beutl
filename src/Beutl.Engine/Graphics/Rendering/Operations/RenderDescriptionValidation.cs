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
    /// How many slots a declaration may hold before the duplicate check stops being a linear scan.
    /// </summary>
    /// <remarks>
    /// A node declares its slot list once and hands it over on every recording, so this runs on the render
    /// path. At the sizes a declaration actually reaches - two is the widest any built-in node declares -
    /// building a hash set to reject a repeat costs several times what comparing the handful of references
    /// already copied does, and the copy can be sized from the declaration instead of grown into. Past this
    /// width the quadratic scan is the more expensive of the two and the set is built after all.
    /// </remarks>
    private const int LinearSlotScanLimit = 8;

    public static IReadOnlyList<RenderResource> CopyResources(
        IEnumerable<RenderResource>? resources,
        string parameterName)
    {
        if (resources is null or IReadOnlyCollection<RenderResource> { Count: 0 })
            return Array.Empty<RenderResource>();

        // A bare declaration carries no cross-element check, so unlike the binding copy beside it this
        // needs no width limit to stay cheap: one indexed read per element serves any length.
        if (resources is IReadOnlyList<RenderResource> declared)
            return CopyDeclaredResources(declared, parameterName);

        var result = new List<RenderResource>();
        foreach (RenderResource? resource in resources)
        {
            if (resource is null)
                throw new ArgumentException("A declared render resource cannot be null.", parameterName);
            if (resource.RegistrationState == RenderResourceRegistrationState.Released)
                throw new ArgumentException("A released render resource cannot be declared.", parameterName);

            result.Add(resource);
        }

        return result.Count == 0 ? Array.Empty<RenderResource>() : result.AsReadOnly();
    }

    /// <remarks>
    /// Unlike the slot and binding copies beside it there is no cross-element scan to feed, so the copy is
    /// here for what it returns: the list keeps saying what was declared however the caller treats its own
    /// afterwards.
    /// </remarks>
    private static IReadOnlyList<RenderResource> CopyDeclaredResources(
        IReadOnlyList<RenderResource> resources,
        string parameterName)
    {
        var copy = new RenderResource[resources.Count];
        for (int index = 0; index < copy.Length; index++)
        {
            RenderResource resource = resources[index];
            if (resource is null)
                throw new ArgumentException("A declared render resource cannot be null.", parameterName);
            if (resource.RegistrationState == RenderResourceRegistrationState.Released)
                throw new ArgumentException("A released render resource cannot be declared.", parameterName);

            copy[index] = resource;
        }

        return Array.AsReadOnly(copy);
    }

    public static IReadOnlyList<RenderResourceBinding> CopyResourceBindings(
        IEnumerable<RenderResourceBinding>? resources,
        string parameterName)
    {
        // An empty sequence has nothing to check, and the recording paths reach this once per operation per
        // frame with no resources at all - by far the common case - so the working set is built only once
        // there is something to put in it.
        if (resources is null or IReadOnlyCollection<RenderResourceBinding> { Count: 0 })
            return Array.Empty<RenderResourceBinding>();

        if (resources is IReadOnlyList<RenderResourceBinding> { Count: <= LinearSlotScanLimit } declared)
            return CopyShortResourceBindings(declared, parameterName);

        var slots = new HashSet<RenderResourceSlot>(ReferenceEqualityComparer.Instance);
        var result = new List<RenderResourceBinding>();
        foreach (RenderResourceBinding? binding in resources)
        {
            if (binding is null)
                throw new ArgumentException("A declared render resource binding cannot be null.", parameterName);
            if (!slots.Add(binding.Slot))
                throw new ArgumentException("A render resource slot cannot be bound more than once.", parameterName);
            ThrowIfUndeclarable(binding.Resource, parameterName);
            result.Add(binding);
        }

        return result.Count == 0 ? Array.Empty<RenderResourceBinding>() : result.AsReadOnly();
    }

    /// <inheritdoc cref="CopyShortResourceSlots" path="/remarks"/>
    private static IReadOnlyList<RenderResourceBinding> CopyShortResourceBindings(
        IReadOnlyList<RenderResourceBinding> bindings,
        string parameterName)
    {
        ThrowIfBindingsUndeclarable(bindings, parameterName);

        var copy = new RenderResourceBinding[bindings.Count];
        for (int index = 0; index < copy.Length; index++)
            copy[index] = bindings[index];

        return Array.AsReadOnly(copy);
    }

    /// <summary>
    /// Runs every per-element check a description's bindings owe - not null, addressing a distinct slot,
    /// carrying a resource that can still be declared - over a list that already offers indexed reads.
    /// </summary>
    /// <remarks>
    /// A pass of its own rather than work folded into <see cref="OrderByDeclaredSlots"/>: that scan walks
    /// the declared slots, so a fault found in declared-slot order would answer a doubly-bound slot with
    /// the message for an undeclared one. Reading the bindings in their own order keeps each fault
    /// reported as itself.
    /// </remarks>
    private static void ThrowIfBindingsUndeclarable(
        IReadOnlyList<RenderResourceBinding> bindings,
        string parameterName)
    {
        if (bindings.Count > LinearSlotScanLimit)
        {
            var slots = new HashSet<RenderResourceSlot>(ReferenceEqualityComparer.Instance);
            for (int index = 0; index < bindings.Count; index++)
            {
                RenderResourceBinding? binding = bindings[index];
                if (binding is null)
                {
                    throw new ArgumentException(
                        "A declared render resource binding cannot be null.",
                        parameterName);
                }

                if (!slots.Add(binding.Slot))
                {
                    throw new ArgumentException(
                        "A render resource slot cannot be bound more than once.",
                        parameterName);
                }

                ThrowIfUndeclarable(binding.Resource, parameterName);
            }

            return;
        }

        for (int index = 0; index < bindings.Count; index++)
        {
            RenderResourceBinding? binding = bindings[index];
            if (binding is null)
                throw new ArgumentException("A declared render resource binding cannot be null.", parameterName);

            for (int bound = 0; bound < index; bound++)
            {
                if (ReferenceEquals(bindings[bound].Slot, binding.Slot))
                {
                    throw new ArgumentException(
                        "A render resource slot cannot be bound more than once.",
                        parameterName);
                }
            }

            ThrowIfUndeclarable(binding.Resource, parameterName);
        }
    }

    private static IReadOnlyList<RenderResourceSlot> CopyResourceSlots(
        IEnumerable<RenderResourceSlot>? slots,
        string parameterName)
    {
        if (slots is null or IReadOnlyCollection<RenderResourceSlot> { Count: 0 })
            return Array.Empty<RenderResourceSlot>();

        if (slots is IReadOnlyList<RenderResourceSlot> { Count: <= LinearSlotScanLimit } declared)
            return CopyShortResourceSlots(declared, parameterName);

        var seen = new HashSet<RenderResourceSlot>(ReferenceEqualityComparer.Instance);
        var result = new List<RenderResourceSlot>();
        foreach (RenderResourceSlot? slot in slots)
        {
            if (slot is null)
                throw new ArgumentException("A render resource slot cannot be null.", parameterName);
            if (!seen.Add(slot))
                throw new ArgumentException("A render resource slot cannot be declared more than once.", parameterName);
            result.Add(slot);
        }

        return result.Count == 0 ? Array.Empty<RenderResourceSlot>() : result.AsReadOnly();
    }

    /// <remarks>
    /// The copy is what the scan reads, so a caller handing over an array it mutates afterwards cannot
    /// change either the check or the list this returns.
    /// </remarks>
    private static IReadOnlyList<RenderResourceSlot> CopyShortResourceSlots(
        IReadOnlyList<RenderResourceSlot> slots,
        string parameterName)
    {
        var copy = new RenderResourceSlot[slots.Count];
        for (int index = 0; index < copy.Length; index++)
        {
            RenderResourceSlot slot = slots[index];
            if (slot is null)
                throw new ArgumentException("A render resource slot cannot be null.", parameterName);

            for (int declared = 0; declared < index; declared++)
            {
                if (ReferenceEquals(copy[declared], slot))
                {
                    throw new ArgumentException(
                        "A render resource slot cannot be declared more than once.",
                        parameterName);
                }
            }

            copy[index] = slot;
        }

        return Array.AsReadOnly(copy);
    }

    /// <summary>
    /// Puts a description's bindings into declared-slot order, refusing a set that does not match.
    /// </summary>
    /// <remarks>
    /// The bindings arrive already read into a list and already checked by
    /// <see cref="ThrowIfBindingsUndeclarable"/>, so this neither re-enumerates them nor re-checks what
    /// that pass refused, and it does not copy them first: every element is written into the array built
    /// here, so a copy taken beforehand would only be overwritten by it. Which binding answers for a
    /// declared slot is found by scanning, which for the widths a declaration reaches is cheaper than the
    /// index built to avoid the scan; past <see cref="LinearSlotScanLimit"/> that reverses and the index
    /// is built.
    /// </remarks>
    private static IReadOnlyList<RenderResourceBinding> OrderByDeclaredSlots(
        IReadOnlyList<RenderResourceSlot> declaredSlots,
        IReadOnlyList<RenderResourceBinding> bindings,
        string parameterName)
    {
        if (declaredSlots.Count != bindings.Count)
        {
            throw new ArgumentException(
                "A render description must bind every resource slot it declares exactly once.",
                parameterName);
        }

        Dictionary<RenderResourceSlot, RenderResourceBinding>? bySlot = null;
        if (bindings.Count > LinearSlotScanLimit)
        {
            bySlot = new Dictionary<RenderResourceSlot, RenderResourceBinding>(
                bindings.Count,
                ReferenceEqualityComparer.Instance);
            foreach (RenderResourceBinding binding in bindings)
                bySlot.Add(binding.Slot, binding);
        }

        var ordered = new RenderResourceBinding[declaredSlots.Count];
        for (int index = 0; index < ordered.Length; index++)
        {
            RenderResourceSlot slot = declaredSlots[index];
            RenderResourceBinding? bound = bySlot is null
                ? FindBinding(bindings, slot)
                : bySlot.GetValueOrDefault(slot);
            if (bound is null)
            {
                throw new ArgumentException(
                    "A render description contains a resource slot it did not declare.",
                    parameterName);
            }

            ordered[index] = bound;
        }

        return ordered;
    }

    private static RenderResourceBinding? FindBinding(
        IReadOnlyList<RenderResourceBinding> bindings,
        RenderResourceSlot slot)
    {
        for (int index = 0; index < bindings.Count; index++)
        {
            if (ReferenceEquals(bindings[index].Slot, slot))
                return bindings[index];
        }

        return null;
    }

    /// <summary>
    /// Applies a declared slot list to a factory that is handed bindings alone.
    /// </summary>
    /// <remarks>
    /// A bindings-only factory has no slot list of its own, so nothing there can tell a caller that bound one
    /// slot twice and another not at all. Passing the declared slots restores that check, and with it the
    /// normalization it performs: the returned bindings are in declared-slot order, so a structural identity
    /// built from them - <see cref="Beutl.Graphics.Effects.GeometryDescription"/>'s resource-type sequence
    /// among them - stops depending on the order the caller happened to write them in.
    /// <para>
    /// A <see langword="null"/> <paramref name="slots"/> declares none rather than opting out of the check,
    /// so an omitted slot list still reaches the same validation an empty one does. Bindings supplied against
    /// it are refused here: the recorded operation would otherwise carry resources in the order the caller
    /// wrote them, which is exactly the order dependence this normalization exists to remove.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<RenderResourceBinding> BindDeclaredSlots(
        IEnumerable<RenderResourceSlot>? slots,
        IEnumerable<RenderResourceBinding>? bindings,
        string slotsParameterName,
        string bindingsParameterName)
    {
        IReadOnlyList<RenderResourceSlot> declaredSlots = CopyResourceSlots(slots, slotsParameterName);

        // Read once, before the count is: a caller-supplied sequence may be a generator, so every check
        // below and the list this returns have to read one enumeration of it. A sequence that already
        // offers indexed reads is read where it lies - what arrives through a read-only interface is held
        // to being stable for the length of this call, and the ordering below writes every element into
        // an array of its own, so a copy taken here would only be overwritten by it.
        IReadOnlyList<RenderResourceBinding> declaredBindings =
            bindings as IReadOnlyList<RenderResourceBinding> ?? MaterializeBindings(bindings);
        ThrowIfBindingsUndeclarable(declaredBindings, bindingsParameterName);
        if (declaredSlots.Count == 0)
        {
            if (declaredBindings.Count > 0)
            {
                throw new ArgumentException(
                    "A render call that declares no resource slots cannot bind a resource. Declare the slots "
                    + "the bindings address, so each one is checked and ordered against that declaration.",
                    slotsParameterName);
            }

            // Declaring nothing and binding nothing is the default every recording path takes, and it is
            // already checked by the two counts above.
            return Array.Empty<RenderResourceBinding>();
        }

        return OrderByDeclaredSlots(declaredSlots, declaredBindings, bindingsParameterName);
    }

    /// <summary>Reads a bindings sequence that offers no indexed access into one that does.</summary>
    private static IReadOnlyList<RenderResourceBinding> MaterializeBindings(
        IEnumerable<RenderResourceBinding>? bindings)
        => bindings is null
            ? Array.Empty<RenderResourceBinding>()
            : new List<RenderResourceBinding>(bindings);

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
