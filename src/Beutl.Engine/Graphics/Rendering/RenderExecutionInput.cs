using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Rendering;

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
        _deviceBounds = ValidateDeviceBounds(bounds, effectiveScale.Value, deviceBounds);
        _rasterBounds = _deviceBounds.ToRect(effectiveScale.Value);
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
        SKImage image,
        Func<Bitmap>? createSnapshot,
        bool readbackDeclared)
        : this(
            token,
            bounds,
            effectiveScale,
            PixelRect.FromRect(bounds, effectiveScale.Value),
            image,
            createSnapshot,
            readbackDeclared)
    {
    }

    internal RenderExecutionInput(
        RenderExecutionSessionToken token,
        Rect bounds,
        EffectiveScale effectiveScale,
        PixelRect deviceBounds,
        SKImage image,
        Func<Bitmap>? createSnapshot,
        bool readbackDeclared)
        : this(
            token,
            bounds,
            effectiveScale,
            deviceBounds,
            (canvas, destination) => canvas.DrawExecutionInput(image, destination),
            (canvas, point) => canvas.DrawExecutionInputDeviceSpace(image, point),
            (x, y) => image.ToShader(
                x,
                y,
                SKSamplingOptions.Default,
                SKMatrix.CreateScaleTranslation(
                    1f / effectiveScale.Value,
                    1f / effectiveScale.Value,
                    deviceBounds.X / effectiveScale.Value,
                    deviceBounds.Y / effectiveScale.Value)),
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
            return new Point(
                _deviceBounds.X / _effectiveScale.Value,
                _deviceBounds.Y / _effectiveScale.Value);
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

        PixelRect canvasBounds = _token.GetActiveCanvasDeviceBounds(canvas);
        _drawDeviceSpace(
            canvas,
            new Point(devicePoint.X - canvasBounds.X, devicePoint.Y - canvasBounds.Y));
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

    private static PixelRect ValidateDeviceBounds(Rect bounds, float density, PixelRect deviceBounds)
    {
        if (deviceBounds.Width <= 0 || deviceBounds.Height <= 0)
        {
            throw new ArgumentException(
                "An execution input requires non-empty device bounds.",
                nameof(deviceBounds));
        }

        PixelRect semanticDeviceBounds = PixelRect.FromRect(bounds, density);
        if (deviceBounds.X > semanticDeviceBounds.X
            || deviceBounds.Y > semanticDeviceBounds.Y
            || deviceBounds.Right < semanticDeviceBounds.Right
            || deviceBounds.Bottom < semanticDeviceBounds.Bottom)
        {
            throw new ArgumentException(
                "Execution input device bounds must contain the semantic bounds at the effective scale.",
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

    public PixelRect GetActiveCanvasDeviceBounds(ImmediateCanvas canvas)
    {
        VerifyActiveCanvas(canvas);
        return _activeFacade!.DeviceBoundsUnchecked;
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

    public bool IsResourceAuthorized(object resource)
        => _active && _authorizedResources.ContainsKey(resource);
}
