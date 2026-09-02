namespace Beutl.Graphics.Rendering;

public readonly struct RenderHitTestContract
{
    private readonly RenderHitTestContractKind _kind;
    private readonly Func<RenderHitTestContext, Point, bool>? _hitTest;
    private readonly object? _structuralIdentity;

    private RenderHitTestContract(RenderHitTestContractKind kind, object structuralIdentity)
    {
        _kind = kind;
        _hitTest = null;
        _structuralIdentity = structuralIdentity;
    }

    private RenderHitTestContract(
        Func<RenderHitTestContext, Point, bool> hitTest,
        object structuralIdentity)
    {
        _kind = RenderHitTestContractKind.Custom;
        _hitTest = hitTest;
        _structuralIdentity = structuralIdentity;
    }

    public static RenderHitTestContract None { get; } = new(
        RenderHitTestContractKind.None,
        RenderHitTestContractKind.None);

    public static RenderHitTestContract OutputBounds { get; } = new(
        RenderHitTestContractKind.OutputBounds,
        RenderHitTestContractKind.OutputBounds);

    public static RenderHitTestContract AnyInput { get; } = new(
        RenderHitTestContractKind.AnyInput,
        RenderHitTestContractKind.AnyInput);

    public static RenderHitTestContract Custom(
        Func<RenderHitTestContext, Point, bool> hitTest)
    {
        ArgumentNullException.ThrowIfNull(hitTest);
        RenderDescriptionValidation.ValidatePureMetadataCallback(hitTest, nameof(hitTest));
        return new RenderHitTestContract(
            hitTest,
            RenderDescriptionValidation.StructuralIdentityOf(hitTest));
    }

    /// <summary>
    /// Creates a hit test that reads call-owned state instead of closing over it.
    /// </summary>
    /// <typeparam name="TState">The immutable state the test reads.</typeparam>
    /// <param name="state">
    /// The per-recording values the test needs. They are request data, not plan identity: a recording that
    /// changes only this reruns the compiled plan rather than compiling a second one.
    /// </param>
    /// <param name="hitTest">
    /// The pure test. Declare it <see langword="static"/>; the plan is keyed by which callback it is, and only
    /// a static callback is the same delegate on every frame.
    /// </param>
    public static RenderHitTestContract Custom<TState>(
        TState state,
        Func<TState, RenderHitTestContext, Point, bool> hitTest)
    {
        ArgumentNullException.ThrowIfNull(hitTest);
        RenderDescriptionValidation.ValidatePureMetadataCallback(hitTest, nameof(hitTest));
        var binding = new HitTestBinding<TState>(state, hitTest);
        return new RenderHitTestContract(
            binding.HitTest,
            RenderDescriptionValidation.StructuralIdentityOf(hitTest));
    }

    /// <summary>
    /// Creates a hit test that reads the resource a call bound to <paramref name="slot"/>.
    /// </summary>
    /// <typeparam name="T">The raw resource type the slot addresses.</typeparam>
    /// <param name="slot">A slot the owning description declares.</param>
    /// <param name="hitTest">
    /// The pure test, given the bound resource. It must not capture a resource of its own; the slot is
    /// resolved against the bindings of the description being tested, so one hit test can be reused across
    /// recordings that bind different resources.
    /// </param>
    public static RenderHitTestContract FromSlot<T>(
        RenderResourceSlot<T> slot,
        Func<T, Point, bool> hitTest)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(slot);
        ArgumentNullException.ThrowIfNull(hitTest);
        RenderDescriptionValidation.ValidatePureMetadataCallback(hitTest, nameof(hitTest));
        return new RenderHitTestContract(
            (context, point) => context.UseResource(slot, value => hitTest(value, point)),
            RenderDescriptionValidation.StructuralIdentityOf(hitTest));
    }

    /// <summary>
    /// Creates a hit test that reads the resource a call bound to <paramref name="slot"/> and also
    /// consults the operation's output bounds and inputs.
    /// </summary>
    /// <typeparam name="T">The raw resource type the slot addresses.</typeparam>
    /// <param name="slot">A slot the owning description declares.</param>
    /// <param name="hitTest">
    /// The pure test, given the bound resource and the hit-test context. It must not capture a resource
    /// of its own.
    /// </param>
    public static RenderHitTestContract FromSlot<T>(
        RenderResourceSlot<T> slot,
        Func<T, RenderHitTestContext, Point, bool> hitTest)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(slot);
        ArgumentNullException.ThrowIfNull(hitTest);
        RenderDescriptionValidation.ValidatePureMetadataCallback(hitTest, nameof(hitTest));
        return new RenderHitTestContract(
            (context, point) => context.UseResource(slot, value => hitTest(value, context, point)),
            RenderDescriptionValidation.StructuralIdentityOf(hitTest));
    }

    internal static RenderHitTestContract FromResource<T>(
        RenderResource<T> resource,
        Func<T, Point, bool> hitTest)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(hitTest);
        return new RenderHitTestContract(
            (_, point) => resource.Registry.Use(resource, value => hitTest(value, point)),
            RenderDescriptionValidation.StructuralIdentityOf(hitTest));
    }

    internal static RenderHitTestContract FromResource<T>(
        RenderResource<T> resource,
        Func<T, RenderHitTestContext, Point, bool> hitTest)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(hitTest);
        return new RenderHitTestContract(
            (context, point) => resource.Registry.Use(
                resource,
                value => hitTest(value, context, point)),
            RenderDescriptionValidation.StructuralIdentityOf(hitTest));
    }

    internal static RenderHitTestContract FromResource<T, TState>(
        RenderResource<T> resource,
        TState state,
        Func<T, TState, Point, bool> hitTest)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(hitTest);
        RenderDescriptionValidation.ValidatePureMetadataCallback(hitTest, nameof(hitTest));
        var binding = new ResourceHitTestBinding<T, TState>(resource, state, hitTest);
        return new RenderHitTestContract(
            binding.HitTest,
            RenderDescriptionValidation.StructuralIdentityOf(hitTest));
    }

    /// <summary>Holds one recording's state so the test itself stays static.</summary>
    private sealed class HitTestBinding<TState>(
        TState state,
        Func<TState, RenderHitTestContext, Point, bool> hitTest)
    {
        public bool HitTest(RenderHitTestContext context, Point point) => hitTest(state, context, point);
    }

    /// <summary>Holds one recording's resource and state so the test itself stays static.</summary>
    private sealed class ResourceHitTestBinding<T, TState>(
        RenderResource<T> resource,
        TState state,
        Func<T, TState, Point, bool> hitTest)
        where T : class
    {
        public bool HitTest(RenderHitTestContext context, Point point)
            => resource.Registry.Use(resource, value => hitTest(value, state, point));
    }

    internal RenderHitTestContractKind Kind => _kind;

    internal object StructuralIdentity
    {
        get
        {
            ThrowIfNotInitialized();
            return _structuralIdentity!;
        }
    }

    internal bool Evaluate(
        Rect outputBounds,
        IReadOnlyList<RenderHitTestInput> inputs,
        IReadOnlyList<RenderResourceBinding> resources,
        Point point)
    {
        ThrowIfNotInitialized();
        RenderRectValidation.ThrowIfInvalidInput(outputBounds, nameof(outputBounds));
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(resources);

        return _kind switch
        {
            RenderHitTestContractKind.None => false,
            RenderHitTestContractKind.OutputBounds => outputBounds.Contains(point),
            RenderHitTestContractKind.AnyInput => AnyInputAccepts(inputs, point),
            RenderHitTestContractKind.Custom =>
                _hitTest!(new RenderHitTestContext(outputBounds, inputs, resources), point),
            _ => throw new InvalidOperationException("The hit-test contract is invalid."),
        };
    }

    // A predicate closing over the point would put its display class at the top of Evaluate, so every
    // contract kind would pay for it, and a foreach over the interface would box an enumerator.
    private static bool AnyInputAccepts(IReadOnlyList<RenderHitTestInput> inputs, Point point)
    {
        for (int index = 0; index < inputs.Count; index++)
        {
            if (inputs[index].HitTest(point))
                return true;
        }

        return false;
    }

    internal void ThrowIfUninitialized(string parameterName)
    {
        if (_kind == RenderHitTestContractKind.Uninitialized || _structuralIdentity is null)
        {
            throw new ArgumentException(
                "default(RenderHitTestContract) is uninitialized; use None, OutputBounds, AnyInput, or Custom.",
                parameterName);
        }
    }

    private void ThrowIfNotInitialized()
    {
        if (_kind == RenderHitTestContractKind.Uninitialized || _structuralIdentity is null)
        {
            throw new InvalidOperationException(
                "default(RenderHitTestContract) is uninitialized; use None, OutputBounds, AnyInput, or Custom.");
        }
    }
}
