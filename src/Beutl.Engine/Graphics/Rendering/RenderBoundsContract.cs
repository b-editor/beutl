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
        Func<Rect, Rect> getRequiredInputBounds,
        object? structuralKey = null)
    {
        ArgumentNullException.ThrowIfNull(transformBounds);
        ArgumentNullException.ThrowIfNull(getRequiredInputBounds);
        RenderDescriptionValidation.ValidatePureMetadataCallback(
            transformBounds,
            nameof(transformBounds));
        RenderDescriptionValidation.ValidatePureMetadataCallback(
            getRequiredInputBounds,
            nameof(getRequiredInputBounds));
        if (structuralKey is not null)
        {
            RenderIdentityKeyValidator.ThrowIfInvalid(structuralKey, nameof(structuralKey));
        }

        return new RenderBoundsContract(
            transformBounds,
            getRequiredInputBounds,
            requiresFullInput: false,
            RenderBoundsStructuralIdentity.Create(transformBounds, getRequiredInputBounds, structuralKey));
    }

    public static RenderBoundsContract CreateFullInput(
        Func<Rect, Rect> transformBounds,
        object? structuralKey = null)
    {
        ArgumentNullException.ThrowIfNull(transformBounds);
        RenderDescriptionValidation.ValidatePureMetadataCallback(
            transformBounds,
            nameof(transformBounds));
        if (structuralKey is not null)
        {
            RenderIdentityKeyValidator.ThrowIfInvalid(structuralKey, nameof(structuralKey));
        }

        return new RenderBoundsContract(
            transformBounds,
            IdentityMap,
            requiresFullInput: true,
            RenderBoundsStructuralIdentity.CreateFullInput(transformBounds, structuralKey));
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
}

internal readonly record struct RenderBoundsStructuralIdentity(
    RenderBoundsContractKind Kind,
    object? ForwardMethod,
    object? BackwardMethod,
    object? ExplicitKey)
{
    public static RenderBoundsStructuralIdentity Identity { get; } =
        new(RenderBoundsContractKind.Identity, null, null, null);

    public static RenderBoundsStructuralIdentity FullInput { get; } =
        new(RenderBoundsContractKind.FullInput, null, null, null);

    public static RenderBoundsStructuralIdentity Create(
        Func<Rect, Rect> transformBounds,
        Func<Rect, Rect> getRequiredInputBounds,
        object? structuralKey)
        => structuralKey is null
            ? new(
                RenderBoundsContractKind.Custom,
                transformBounds.Method,
                getRequiredInputBounds.Method,
                null)
            : new(RenderBoundsContractKind.Custom, null, null, structuralKey);

    public static RenderBoundsStructuralIdentity CreateFullInput(
        Func<Rect, Rect> transformBounds,
        object? structuralKey)
        => structuralKey is null
            ? new(RenderBoundsContractKind.CustomFullInput, transformBounds.Method, null, null)
            : new(RenderBoundsContractKind.CustomFullInput, null, null, structuralKey);
}

internal enum RenderBoundsContractKind : byte
{
    Identity,
    FullInput,
    Custom,
    CustomFullInput,
}

internal static class RenderRectValidation
{
    public static bool IsFiniteNonNegative(Rect value)
        => float.IsFinite(value.X)
           && float.IsFinite(value.Y)
           && float.IsFinite(value.Width)
           && float.IsFinite(value.Height)
           && value.Width >= 0
           && value.Height >= 0;

    public static void ThrowIfInvalidInput(Rect value, string parameterName)
    {
        if (!IsFiniteNonNegative(value))
        {
            throw new ArgumentException(
                "Bounds must be finite and have non-negative dimensions.",
                parameterName);
        }
    }

    public static void ThrowIfInvalidResult(Rect value, string message)
    {
        if (!IsFiniteNonNegative(value))
            throw new InvalidOperationException(message);
    }
}
