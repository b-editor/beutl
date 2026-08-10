using System.Runtime.ExceptionServices;

using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Rendering;

/// <summary>Identifies one authored input handle's contiguous values in a flattened execution session.</summary>
/// <param name="StartIndex">The zero-based index of the first value in the session's input list.</param>
/// <param name="Count">The number of runtime values produced by the authored input handle.</param>
public readonly record struct RenderExecutionInputRange(int StartIndex, int Count)
{
    /// <summary>Gets the exclusive end index in the session's input list.</summary>
    public int EndIndex => checked(StartIndex + Count);

    internal static IReadOnlyList<RenderExecutionInputRange> CopyAndValidate(
        IReadOnlyList<RenderExecutionInput> inputs,
        IReadOnlyList<RenderExecutionInputRange> inputRanges,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(inputRanges);
        RenderExecutionInputRange[] copiedRanges = inputRanges.ToArray();
        int expectedStart = 0;
        foreach (RenderExecutionInputRange range in copiedRanges)
        {
            if (range.StartIndex != expectedStart || range.Count < 0)
            {
                throw new ArgumentException(
                    "Execution input ranges must be non-negative, contiguous, and in authored order.",
                    parameterName);
            }

            expectedStart = range.EndIndex;
        }

        if (expectedStart != inputs.Count)
        {
            throw new ArgumentException(
                "Execution input ranges must cover every flattened execution input exactly once.",
                parameterName);
        }

        return Array.AsReadOnly(copiedRanges);
    }
}

public sealed class RenderExecutionInput
{
    private readonly RenderExecutionSessionToken _token;
    private readonly Rect _bounds;
    private readonly EffectiveScale _effectiveScale;
    private readonly PixelRect _deviceBounds;
    private readonly Rect _rasterBounds;
    private readonly Action<ImmediateCanvas, Rect> _draw;
    private readonly Action<ImmediateCanvas, Point> _drawDeviceSpace;
    private readonly Func<SKShaderTileMode, SKShaderTileMode, SKShader>? _createShader;
    private readonly Func<Bitmap>? _createSnapshot;
    private readonly bool _readbackDeclared;
    private bool _snapshotUsed;

    internal RenderExecutionInput(
        RenderExecutionSessionToken token,
        Rect bounds,
        EffectiveScale effectiveScale,
        Action<ImmediateCanvas, Rect> draw,
        Action<ImmediateCanvas, Point> drawDeviceSpace,
        Func<SKShaderTileMode, SKShaderTileMode, SKShader>? createShader,
        Func<Bitmap>? createSnapshot,
        bool readbackDeclared)
        : this(
            token,
            bounds,
            effectiveScale,
            PixelRect.FromRect(bounds, effectiveScale.Value),
            draw,
            drawDeviceSpace,
            createShader,
            createSnapshot,
            readbackDeclared)
    {
    }

    internal RenderExecutionInput(
        RenderExecutionSessionToken token,
        Rect bounds,
        EffectiveScale effectiveScale,
        PixelRect deviceBounds,
        Action<ImmediateCanvas, Rect> draw,
        Action<ImmediateCanvas, Point> drawDeviceSpace,
        Func<SKShaderTileMode, SKShaderTileMode, SKShader>? createShader,
        Func<Bitmap>? createSnapshot,
        bool readbackDeclared)
        : this(
            token,
            bounds,
            effectiveScale,
            deviceBounds,
            deviceBounds.ToRect(effectiveScale.Value),
            draw,
            drawDeviceSpace,
            createShader,
            createSnapshot,
            readbackDeclared)
    {
    }

    internal RenderExecutionInput(
        RenderExecutionSessionToken token,
        Rect bounds,
        EffectiveScale effectiveScale,
        PixelRect deviceBounds,
        Rect rasterBounds,
        Action<ImmediateCanvas, Rect> draw,
        Action<ImmediateCanvas, Point> drawDeviceSpace,
        Func<SKShaderTileMode, SKShaderTileMode, SKShader>? createShader,
        Func<Bitmap>? createSnapshot,
        bool readbackDeclared)
    {
        ArgumentNullException.ThrowIfNull(token);
        RenderDescriptionValidation.ThrowIfFiniteNonEmpty(bounds, nameof(bounds));
        if (effectiveScale.IsUnbounded)
        {
            throw new ArgumentException(
                "An execution input requires a concrete effective scale.",
                nameof(effectiveScale));
        }

        ArgumentNullException.ThrowIfNull(draw);
        ArgumentNullException.ThrowIfNull(drawDeviceSpace);
        if (readbackDeclared && createSnapshot is null)
        {
            throw new ArgumentException(
                "Declared input readback requires a snapshot provider.",
                nameof(createSnapshot));
        }

        _token = token;
        _bounds = bounds;
        _effectiveScale = effectiveScale;
        _deviceBounds = ValidateDeviceBounds(
            bounds,
            effectiveScale.Value,
            deviceBounds,
            rasterBounds);
        _rasterBounds = rasterBounds;
        _draw = draw;
        _drawDeviceSpace = drawDeviceSpace;
        _createShader = createShader;
        _createSnapshot = createSnapshot;
        _readbackDeclared = readbackDeclared;
    }

    internal RenderExecutionInput(
        RenderExecutionSessionToken token,
        Rect bounds,
        EffectiveScale effectiveScale,
        PixelRect deviceBounds,
        Rect rasterBounds,
        SKImage image,
        Func<Bitmap>? createSnapshot,
        bool readbackDeclared)
        : this(
            token,
            bounds,
            effectiveScale,
            deviceBounds,
            rasterBounds,
            (canvas, destination) => canvas.DrawExecutionInput(image, destination),
            (canvas, point) => canvas.DrawExecutionInputDeviceSpace(image, point),
            (x, y) => image.ToShader(
                x,
                y,
                SKSamplingOptions.Default,
                SKMatrix.CreateScaleTranslation(
                    1f / effectiveScale.Value,
                    1f / effectiveScale.Value,
                    (float)rasterBounds.X,
                    (float)rasterBounds.Y)),
            createSnapshot,
            readbackDeclared)
    {
        ArgumentNullException.ThrowIfNull(image);
    }

    public Rect Bounds
    {
        get { _token.ThrowIfInactive(); return _bounds; }
    }

    public EffectiveScale EffectiveScale
    {
        get { _token.ThrowIfInactive(); return _effectiveScale; }
    }

    public PixelRect DeviceBounds
    {
        get { _token.ThrowIfInactive(); return _deviceBounds; }
    }

    public PixelSize DeviceSize
    {
        get { _token.ThrowIfInactive(); return _deviceBounds.Size; }
    }

    /// <summary>
    /// Gets the translation from input-local coordinates to the composition-device grid used to
    /// round <see cref="DeviceBounds"/>.
    /// </summary>
    public Vector DeviceGridOffset
    {
        get
        {
            _token.ThrowIfInactive();
            return new Vector(
                (_deviceBounds.X / _effectiveScale.Value) - _rasterBounds.X,
                (_deviceBounds.Y / _effectiveScale.Value) - _rasterBounds.Y);
        }
    }

    /// <summary>
    /// Gets the pixel-aligned logical footprint represented by the complete backing image.
    /// This can conservatively extend beyond <see cref="Bounds"/> because of device-pixel rounding.
    /// </summary>
    public Rect RasterBounds
    {
        get { _token.ThrowIfInactive(); return _rasterBounds; }
    }

    public Point LogicalOrigin
    {
        get
        {
            _token.ThrowIfInactive();
            return _rasterBounds.Position;
        }
    }

    public void Draw(ImmediateCanvas canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        _token.VerifyActiveCanvas(canvas);
        _draw(canvas, _rasterBounds);
    }

    public void DrawDeviceSpace(ImmediateCanvas canvas, Point devicePoint)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        if (!float.IsFinite(devicePoint.X) || !float.IsFinite(devicePoint.Y))
            throw new ArgumentException("The device-space point must be finite.", nameof(devicePoint));

        PixelPoint canvasOrigin = _token.GetActiveCanvasDeviceOrigin(canvas);
        _drawDeviceSpace(
            canvas,
            new Point(devicePoint.X - canvasOrigin.X, devicePoint.Y - canvasOrigin.Y));
    }

    public void UseShader(
        Action<SKShader> use,
        SKShaderTileMode x = SKShaderTileMode.Decal,
        SKShaderTileMode y = SKShaderTileMode.Decal)
    {
        _token.ThrowIfInactive();
        ArgumentNullException.ThrowIfNull(use);
        if (!Enum.IsDefined(x))
            throw new ArgumentOutOfRangeException(nameof(x), x, "The shader tile mode is invalid.");
        if (!Enum.IsDefined(y))
            throw new ArgumentOutOfRangeException(nameof(y), y, "The shader tile mode is invalid.");
        if (_createShader is null)
            throw new InvalidOperationException("This execution input does not expose a GPU shader view.");

        using SKShader shader = _createShader(x, y)
            ?? throw new InvalidOperationException("The input shader provider returned null.");
        _token.AuthorizeResource(shader, () => use(shader));
    }

    public void UseSnapshot(Action<Bitmap> use)
    {
        _token.ThrowIfInactive();
        ArgumentNullException.ThrowIfNull(use);
        if (!_readbackDeclared || _createSnapshot is null)
            throw new InvalidOperationException("CPU readback was not declared for this execution input.");
        if (_snapshotUsed)
            throw new InvalidOperationException("An execution input snapshot is a one-shot lease.");

        _snapshotUsed = true;
        using Bitmap snapshot = _createSnapshot()
            ?? throw new InvalidOperationException("The input snapshot provider returned null.");
        _token.AuthorizeResource(snapshot, () => use(snapshot));
    }

    private static PixelRect ValidateDeviceBounds(
        Rect bounds,
        float density,
        PixelRect deviceBounds,
        Rect rasterBounds)
    {
        if (deviceBounds.Width <= 0 || deviceBounds.Height <= 0)
        {
            throw new ArgumentException(
                "An execution input requires non-empty device bounds.",
                nameof(deviceBounds));
        }

        if (!DeviceBoundsValidation.MatchesExtent(rasterBounds.Width, density, deviceBounds.Width)
            || !DeviceBoundsValidation.MatchesExtent(rasterBounds.Height, density, deviceBounds.Height)
            || rasterBounds.X > bounds.X
            || rasterBounds.Y > bounds.Y
            || rasterBounds.Right < bounds.Right
            || rasterBounds.Bottom < bounds.Bottom)
        {
            throw new ArgumentException(
                "Execution input raster bounds must match the backing size and contain the semantic bounds.",
                nameof(deviceBounds));
        }

        return deviceBounds;
    }
}

internal sealed class RenderExecutionSessionToken
{
    private readonly Dictionary<object, int> _authorizedResources = new(ReferenceEqualityComparer.Instance);
    private IDisposable? _callbackGuard = RenderExecutionCallbackGuard.Enter();
    private bool _active = true;
    private ImmediateCanvas? _activeCanvas;
    private RenderCallbackCanvas? _activeFacade;

    public void ThrowIfInactive()
    {
        if (!_active)
            throw new InvalidOperationException("The render execution callback has completed.");
    }

    public void Complete()
    {
        ThrowIfInactive();
        bool hasActiveCanvas = _activeCanvas is not null;
        _active = false;
        _activeCanvas = null;
        _activeFacade = null;
        _authorizedResources.Clear();
        Interlocked.Exchange(ref _callbackGuard, null)?.Dispose();
        if (hasActiveCanvas)
            throw new InvalidOperationException("An execution canvas is still active.");
    }

    public void RunAndComplete(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        RunAndComplete(
            () =>
            {
                action();
                return true;
            });
    }

    public T RunAndComplete<T>(Func<T> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        ExceptionDispatchInfo? primaryFailure = null;
        T result = default!;
        try
        {
            result = action();
        }
        catch (Exception ex)
        {
            primaryFailure = ExceptionDispatchInfo.Capture(ex);
        }
        finally
        {
            try
            {
                Complete();
            }
            catch when (primaryFailure is not null)
            {
                // The callback failure remains primary; session cleanup is best-effort on this path.
            }
        }

        primaryFailure?.Throw();
        return result;
    }

    public void EnterCanvas(ImmediateCanvas canvas, RenderCallbackCanvas? facade)
    {
        ThrowIfInactive();
        ArgumentNullException.ThrowIfNull(canvas);
        if (_activeCanvas is not null)
            throw new InvalidOperationException("Only one callback canvas may be active in an execution session.");

        _activeCanvas = canvas;
        _activeFacade = facade;
    }

    public void UseRawCanvas(ImmediateCanvas canvas, Action<ImmediateCanvas> use)
    {
        ThrowIfInactive();
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(use);
        EnterCanvas(canvas, facade: null);
        try
        {
            canvas.ConfigureRawExecutionCallback(this);
            use(canvas);
        }
        finally
        {
            try
            {
                canvas.CloseWithoutFlush();
            }
            finally
            {
                ExitCanvas(canvas);
            }
        }
    }

    public void ExitCanvas(ImmediateCanvas canvas)
    {
        if (!ReferenceEquals(_activeCanvas, canvas))
            throw new InvalidOperationException("The supplied canvas is not the active execution canvas.");

        _activeCanvas = null;
        _activeFacade = null;
    }

    public bool IsActiveCanvas(ImmediateCanvas canvas)
        => _active && ReferenceEquals(_activeCanvas, canvas);

    public ImmediateCanvas GetActiveCanvas(RenderCallbackCanvas facade)
    {
        ThrowIfInactive();
        if (_activeCanvas is null || !ReferenceEquals(_activeFacade, facade))
        {
            throw new InvalidOperationException(
                "The operation must run while this callback canvas facade is active.");
        }

        return _activeCanvas;
    }

    public void VerifyActiveCanvas(ImmediateCanvas canvas)
    {
        ThrowIfInactive();
        if (!ReferenceEquals(_activeCanvas, canvas) || _activeFacade is null)
        {
            throw new InvalidOperationException(
                "An execution input may be drawn only on the active same-session callback canvas.");
        }
    }

    public PixelPoint GetActiveCanvasDeviceOrigin(ImmediateCanvas canvas)
    {
        VerifyActiveCanvas(canvas);
        return _activeFacade!.DeviceOriginUnchecked;
    }

    public void AuthorizeResource(object resource, Action use)
    {
        ThrowIfInactive();
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(use);

        _authorizedResources.TryGetValue(resource, out int count);
        _authorizedResources[resource] = count + 1;
        try
        {
            use();
        }
        finally
        {
            if (count == 0)
                _authorizedResources.Remove(resource);
            else
                _authorizedResources[resource] = count;
        }
    }

    public void UseResource<T>(
        RenderResource<T> resource,
        IReadOnlyList<RenderResource> declaredResources,
        Action<T> use)
        where T : class
    {
        ThrowIfInactive();
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(declaredResources);
        ArgumentNullException.ThrowIfNull(use);
        if (!declaredResources.Any(declared => ReferenceEquals(declared.SlotIdentity, resource.SlotIdentity)))
        {
            throw new InvalidOperationException("The render resource was not declared by this operation.");
        }

        resource.Registry.Use(
            resource,
            value =>
            {
                AuthorizeResource(value, () => use(value));
                return true;
            });
    }

    public void UseDeclaredResource<T>(
        string name,
        IReadOnlyList<RenderResourceBinding> declaredResources,
        Action<T> use)
        where T : class
    {
        ThrowIfInactive();
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A declared resource name must be non-empty.", nameof(name));
        ArgumentNullException.ThrowIfNull(declaredResources);
        ArgumentNullException.ThrowIfNull(use);
        RenderResourceBinding? binding = declaredResources.FirstOrDefault(
            item => string.Equals(item.Name, name, StringComparison.Ordinal));
        if (binding is null)
            throw new KeyNotFoundException($"No render resource named '{name}' was declared by this callback.");

        if (binding.Resource is not RenderResource<T> resource)
        {
            Type declaredType = binding.Resource.GetType();
            string declaredValueType = declaredType.IsGenericType
                ? DescribeType(declaredType.GetGenericArguments()[0])
                : declaredType.Name;
            throw new InvalidOperationException(
                $"Declared resource '{name}' is a RenderResource<{declaredValueType}>, not a "
                + $"RenderResource<{DescribeType(typeof(T))}>.");
        }

        UseResource(resource, declaredResources.Select(static item => item.Resource).ToArray(), use);
    }

    public bool IsResourceAuthorized(object resource)
        => _active && _authorizedResources.ContainsKey(resource);

    // Nested resource types are all named "Resource", so the declaring type has to be part of the name for the
    // message to distinguish two declared resources.
    private static string DescribeType(Type type)
        => type.DeclaringType is { } declaring ? $"{declaring.Name}.{type.Name}" : type.Name;
}
