namespace Beutl.Graphics.Rendering;

/// <summary>
/// Declares conservative forward output bounds and backward required-input bounds for recorded work.
/// </summary>
public readonly struct RenderBoundsContract
{
    private readonly Func<Rect, Rect>? _transformBounds;
    private readonly Func<Rect, Rect>? _getRequiredInputBounds;
    private readonly object? _structuralIdentity;

    private RenderBoundsContract(
        Func<Rect, Rect> transformBounds,
        Func<Rect, Rect> getRequiredInputBounds,
        bool requiresFullInput,
        object structuralIdentity)
    {
        _transformBounds = transformBounds;
        _getRequiredInputBounds = getRequiredInputBounds;
        RequiresFullInput = requiresFullInput;
        _structuralIdentity = structuralIdentity;
    }

    public static RenderBoundsContract Identity { get; } = new(
        IdentityMap,
        IdentityMap,
        requiresFullInput: false,
        RenderBoundsStructuralIdentity.Identity);

    public static RenderBoundsContract FullInput { get; } = new(
        IdentityMap,
        IdentityMap,
        requiresFullInput: true,
        RenderBoundsStructuralIdentity.FullInput);

    public bool RequiresFullInput { get; }

    public static RenderBoundsContract Create(
        Func<Rect, Rect> transformBounds,
        Func<Rect, Rect> getRequiredInputBounds)
    {
        ArgumentNullException.ThrowIfNull(transformBounds);
        ArgumentNullException.ThrowIfNull(getRequiredInputBounds);
        RenderDescriptionValidation.ValidatePureMetadataCallback(
            transformBounds,
            nameof(transformBounds));
        RenderDescriptionValidation.ValidatePureMetadataCallback(
            getRequiredInputBounds,
            nameof(getRequiredInputBounds));
        return new RenderBoundsContract(
            transformBounds,
            getRequiredInputBounds,
            requiresFullInput: false,
            RenderBoundsStructuralIdentity.Create(transformBounds, getRequiredInputBounds));
    }

    public static RenderBoundsContract CreateFullInput(
        Func<Rect, Rect> transformBounds)
    {
        ArgumentNullException.ThrowIfNull(transformBounds);
        RenderDescriptionValidation.ValidatePureMetadataCallback(
            transformBounds,
            nameof(transformBounds));
        return new RenderBoundsContract(
            transformBounds,
            IdentityMap,
            requiresFullInput: true,
            RenderBoundsStructuralIdentity.CreateFullInput(transformBounds));
    }

    /// <summary>
    /// Creates a bounds contract whose mappings read call-owned state instead of closing over it.
    /// </summary>
    /// <typeparam name="TState">The immutable state the mappings read.</typeparam>
    /// <param name="state">
    /// The per-recording values the mappings need. They are request data, not plan identity: a recording that
    /// changes only this reruns the compiled plan rather than compiling a second one.
    /// </param>
    /// <param name="transformBounds">
    /// A pure forward mapping. Declare it <see langword="static"/>; the plan is keyed by which callback it is,
    /// and only a static callback is the same delegate on every frame.
    /// </param>
    /// <param name="getRequiredInputBounds">A pure backward mapping, declared the same way.</param>
    public static RenderBoundsContract Create<TState>(
        TState state,
        Func<TState, Rect, Rect> transformBounds,
        Func<TState, Rect, Rect> getRequiredInputBounds)
    {
        ArgumentNullException.ThrowIfNull(transformBounds);
        ArgumentNullException.ThrowIfNull(getRequiredInputBounds);
        RenderDescriptionValidation.ValidatePureMetadataCallback(
            transformBounds,
            nameof(transformBounds));
        RenderDescriptionValidation.ValidatePureMetadataCallback(
            getRequiredInputBounds,
            nameof(getRequiredInputBounds));
        var binding = new BoundsMapping<TState>(state, transformBounds, getRequiredInputBounds);
        return new RenderBoundsContract(
            binding.TransformBounds,
            binding.GetRequiredInputBounds,
            requiresFullInput: false,
            RenderBoundsStructuralIdentity.Create(transformBounds, getRequiredInputBounds));
    }

    /// <summary>
    /// Creates a full-input bounds contract whose forward mapping reads call-owned state.
    /// </summary>
    /// <typeparam name="TState">The immutable state the mapping reads.</typeparam>
    /// <param name="state">The per-recording values the mapping needs, which are request data.</param>
    /// <param name="transformBounds">A pure forward mapping, declared <see langword="static"/>.</param>
    public static RenderBoundsContract CreateFullInput<TState>(
        TState state,
        Func<TState, Rect, Rect> transformBounds)
    {
        ArgumentNullException.ThrowIfNull(transformBounds);
        RenderDescriptionValidation.ValidatePureMetadataCallback(
            transformBounds,
            nameof(transformBounds));
        var binding = new BoundsMapping<TState>(state, transformBounds, transformBounds);
        return new RenderBoundsContract(
            binding.TransformBounds,
            IdentityMap,
            requiresFullInput: true,
            RenderBoundsStructuralIdentity.CreateFullInput(transformBounds));
    }

    public Rect TransformBounds(Rect inputBounds)
    {
        ThrowIfNotInitialized();
        RenderRectValidation.ThrowIfInvalidInput(inputBounds, nameof(inputBounds));
        Rect result = _transformBounds!(inputBounds);
        RenderRectValidation.ThrowIfInvalidResult(result, "The forward bounds mapping returned an invalid rectangle.");
        return result;
    }

    public Rect GetRequiredInputBounds(Rect requestedOutputBounds)
    {
        ThrowIfNotInitialized();
        RenderRectValidation.ThrowIfInvalidInput(requestedOutputBounds, nameof(requestedOutputBounds));
        Rect result = _getRequiredInputBounds!(requestedOutputBounds);
        RenderRectValidation.ThrowIfInvalidResult(result, "The backward bounds mapping returned an invalid rectangle.");
        return result;
    }

    internal object StructuralIdentity
    {
        get
        {
            ThrowIfNotInitialized();
            return _structuralIdentity!;
        }
    }

    internal void ThrowIfUninitialized(string parameterName)
    {
        if (_transformBounds is null || _getRequiredInputBounds is null || _structuralIdentity is null)
        {
            throw new ArgumentException(
                "default(RenderBoundsContract) is uninitialized; use Identity, FullInput, Create, or CreateFullInput.",
                parameterName);
        }
    }

    private void ThrowIfNotInitialized()
    {
        if (_transformBounds is null || _getRequiredInputBounds is null || _structuralIdentity is null)
        {
            throw new InvalidOperationException(
                "default(RenderBoundsContract) is uninitialized; use Identity, FullInput, Create, or CreateFullInput.");
        }
    }

    private static Rect IdentityMap(Rect value) => value;

    /// <summary>Holds one recording's state so the mappings themselves stay static.</summary>
    private sealed class BoundsMapping<TState>(
        TState state,
        Func<TState, Rect, Rect> transformBounds,
        Func<TState, Rect, Rect> getRequiredInputBounds)
    {
        public Rect TransformBounds(Rect value) => transformBounds(state, value);

        public Rect GetRequiredInputBounds(Rect value) => getRequiredInputBounds(state, value);
    }
}

internal readonly record struct RenderBoundsStructuralIdentity(
    RenderBoundsContractKind Kind,
    object? ForwardMap,
    object? BackwardMap)
{
    public static RenderBoundsStructuralIdentity Identity { get; } =
        new(RenderBoundsContractKind.Identity, null, null);

    public static RenderBoundsStructuralIdentity FullInput { get; } =
        new(RenderBoundsContractKind.FullInput, null, null);

    public static RenderBoundsStructuralIdentity Create(
        Delegate transformBounds,
        Delegate getRequiredInputBounds)
        => new(
            RenderBoundsContractKind.Custom,
            RenderDescriptionValidation.StructuralIdentityOf(transformBounds),
            RenderDescriptionValidation.StructuralIdentityOf(getRequiredInputBounds));

    public static RenderBoundsStructuralIdentity CreateFullInput(Delegate transformBounds)
        => new(
            RenderBoundsContractKind.CustomFullInput,
            RenderDescriptionValidation.StructuralIdentityOf(transformBounds),
            null);
}
