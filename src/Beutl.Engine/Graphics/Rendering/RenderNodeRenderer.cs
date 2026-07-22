using System.Runtime.ExceptionServices;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Rendering;

/// <summary>Configures requests issued by a <see cref="RenderNodeRenderer"/>.</summary>
public sealed class RenderNodeRendererOptions
{
    /// <summary>Gets the intent that selects allocation-failure behavior.</summary>
    public RenderIntent Intent { get; init; } = RenderIntent.Preview;

    /// <summary>Gets the optional finite logical domain for target-less root target accesses.</summary>
    /// <remarks>
    /// A non-null value must be finite and non-empty. It is used by <see cref="RenderNodeRenderer.Rasterize"/>,
    /// <see cref="RenderNodeRenderer.Measure"/>, and <see cref="RenderNodeRenderer.HitTest(Point)"/> when a
    /// root fragment requires a target domain. <see cref="RenderNodeRenderer.Render(ImmediateCanvas)"/> uses
    /// its destination viewport instead. <see langword="null"/> is valid for self-bounded graphs that do not
    /// require a root <see cref="TargetRegion.Full"/> access.
    /// </remarks>
    public Rect? TargetDomain { get; init; }

    /// <summary>Gets the optional final logical output region requested by the caller.</summary>
    /// <remarks>
    /// <see langword="null"/> selects the complete conservative output extent. A finite empty rectangle is a
    /// successful empty request. This property does not provide or shrink <see cref="TargetDomain"/>.
    /// </remarks>
    public Rect? RequestedRegion { get; init; }

    /// <summary>Gets the requested device-pixel density for target-less rasterization and metadata queries.</summary>
    /// <remarks>
    /// Non-finite and non-positive values are sanitized to <c>1</c>. Rendering into a supplied canvas uses the
    /// destination density instead.
    /// </remarks>
    public float OutputScale { get; init; } = 1;

    /// <summary>Gets the maximum working density allowed for intermediate values.</summary>
    /// <remarks>
    /// NaN and non-positive values are sanitized to positive infinity. Positive finite values and positive
    /// infinity are preserved.
    /// </remarks>
    public float MaxWorkingScale { get; init; } = float.PositiveInfinity;

    /// <summary>Gets whether eligible persistent render-node cache entries may be read or published.</summary>
    public bool UseRenderCache { get; init; } = true;

    /// <summary>Gets the optional caller-owned factory for renderer-owned intermediate targets.</summary>
    /// <remarks><see langword="null"/> selects the engine's current-backend RGBA16F allocator.</remarks>
    public IRenderTargetFactory? TargetFactory { get; init; }

    internal FusionMode FusionMode { get; init; } = FusionMode.Enabled;

    internal RenderRequestPurpose RenderPurpose { get; init; } = RenderRequestPurpose.Auxiliary;

    internal RenderCacheRules CacheRules { get; init; } = RenderCacheRules.Default;

    internal IRenderPipelineDiagnosticsState? Diagnostics { get; init; }
}

/// <summary>Creates fresh linear-premultiplied RGBA16F targets requested by a renderer.</summary>
public interface IRenderTargetFactory
{
    /// <summary>Creates a target with the exact requested device size.</summary>
    /// <param name="deviceSize">The positive device-pixel size.</param>
    /// <returns>A new target, or <see langword="null"/> when allocation cannot be satisfied.</returns>
    /// <remarks>
    /// Every non-null return transfers exclusive ownership to the renderer immediately and must be fresh,
    /// unleased, format-compatible, and exactly <paramref name="deviceSize"/>. The renderer disposes an invalid
    /// non-null return. The factory itself remains caller-owned and is never disposed by the renderer.
    /// </remarks>
    RenderTarget? Create(PixelSize deviceSize);
}

/// <summary>
/// Records, plans, and executes one render-node root while retaining reusable plans, programs, and targets.
/// </summary>
/// <remarks>
/// The renderer borrows <see cref="Root"/>, its cache, <see cref="RenderNodeRendererOptions.TargetFactory"/>,
/// render destinations, and returned rasterizations. It owns its plan/program caches and pooled targets.
/// Public calls on one instance are synchronous and must not overlap. After <see cref="Dispose"/>, every public
/// rendering or metadata method throws <see cref="ObjectDisposedException"/>.
/// </remarks>
public sealed class RenderNodeRenderer : IDisposable
{
    private readonly RenderTargetLeaseRegistry _targetRegistry;
    private readonly StructuralPlanCache _structuralPlanCache;
    private readonly ProgramCache<CachedSkRuntimeEffect> _programCache;

    /// <summary>Creates a renderer for a caller-owned root node.</summary>
    /// <param name="root">The non-null caller-owned root recorded for every request.</param>
    /// <param name="options">
    /// Options copied and sanitized for the renderer lifetime, or <see langword="null"/> to use defaults.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="root"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The configured render intent is not defined.</exception>
    /// <exception cref="ArgumentException">
    /// A configured target domain or requested region is not finite, or the target domain is empty.
    /// </exception>
    public RenderNodeRenderer(RenderNode root, RenderNodeRendererOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        options ??= new RenderNodeRendererOptions();
        if (!Enum.IsDefined(options.Intent))
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.Intent,
                "The render intent is not defined.");
        }

        ValidateTargetDomain(options.TargetDomain);
        ValidateRequestedRegion(options.RequestedRegion);

        Root = root;
        Options = new RenderNodeRendererOptions
        {
            Intent = options.Intent,
            TargetDomain = options.TargetDomain,
            RequestedRegion = options.RequestedRegion,
            OutputScale = SanitizeOutputScale(options.OutputScale),
            MaxWorkingScale = RenderScaleUtilities.SanitizeMaxWorkingScale(options.MaxWorkingScale),
            UseRenderCache = options.UseRenderCache,
            TargetFactory = options.TargetFactory,
            FusionMode = options.FusionMode,
            RenderPurpose = options.RenderPurpose,
            CacheRules = options.CacheRules,
            Diagnostics = options.Diagnostics,
        };
        _targetRegistry = new RenderTargetLeaseRegistry(Options.TargetFactory);
        _structuralPlanCache = new StructuralPlanCache();
        _programCache = SkRuntimeEffectProgramCache.Create();
    }

    /// <summary>Gets the caller-owned root node.</summary>
    public RenderNode Root { get; }

    /// <summary>Gets the sanitized option snapshot owned by this renderer.</summary>
    public RenderNodeRendererOptions Options { get; }

    /// <summary>Gets whether this renderer has released its owned state.</summary>
    public bool IsDisposed { get; private set; }

    internal RenderExecutionStatistics LastExecutionStatistics { get; private set; }

    internal StructuralPlanCacheStatistics StructuralPlanCacheStatistics
        => _structuralPlanCache.Statistics;

    internal ProgramCacheStatistics ProgramCacheStatistics => _programCache.Statistics;

    internal RenderTargetPoolStatistics TargetPoolStatistics => _targetRegistry.Statistics;

    /// <summary>Synchronously renders the complete root stream into a borrowed destination.</summary>
    /// <param name="destination">The non-null caller-owned destination canvas.</param>
    /// <remarks>
    /// The call preserves the destination's active transform, clip, opacity, blend mode, density, and ownership.
    /// It does not close, dispose, flush, submit, clear, or snapshot the destination implicitly.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="destination"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">This renderer or <paramref name="destination"/> is disposed.</exception>
    public void Render(ImmediateCanvas destination)
    {
        RenderExecutionCallbackGuard.ThrowIfRendererLaunchForbidden();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(destination);
        ObjectDisposedException.ThrowIf(destination.IsDisposed, destination);
        if (Options.RequestedRegion is { } requested
            && (requested.Width == 0 || requested.Height == 0))
        {
            return;
        }

        float maxWorkingScale = MathF.Min(Options.MaxWorkingScale, destination.MaxWorkingScale);
        Rect targetDomain = ResolveDestinationTargetDomain(destination);
        using RenderTargetLeaseSession targets = _targetRegistry.BeginSession(
            Options.Intent,
            destination._renderTarget);
        CompiledRenderRequest? request = null;
        RenderRequestOwner? owner = null;
        ExceptionDispatchInfo? primary = null;
        try
        {
            request = RecordAndCompile(
                Options.RenderPurpose,
                destination.Density,
                maxWorkingScale,
                targetDomain,
                targets);
            owner = request.Request.Options.Owner;
            var executor = new RenderRequestExecutor(targets, _programCache);
            if (request.ExecutionTargetBounds == request.SelectedOutputBounds)
            {
                executor.Execute(request, destination);
            }
            else
            {
                ExecuteWithExpandedTarget(
                    request,
                    destination,
                    targets,
                    executor,
                    maxWorkingScale);
            }
            LastExecutionStatistics = executor.Statistics;
        }
        catch (Exception ex)
        {
            primary = ExceptionDispatchInfo.Capture(ex);
        }
        finally
        {
            DisposeAndCapture(request, ref primary);
            DisposeAndCapture(targets, ref primary);
        }

        ThrowAfterCleanup(primary, owner, targets);
    }

    private static void ExecuteWithExpandedTarget(
        CompiledRenderRequest request,
        ImmediateCanvas destination,
        RenderTargetLeaseSession targets,
        RenderRequestExecutor executor,
        float maxWorkingScale)
    {
        RenderTargetLease? executionLease = null;
        ImmediateCanvas? executionCanvas = null;
        try
        {
            executionLease = targets.Acquire(destination.DeviceSize);
            var executionLogicalSize = new Size(
                destination.DeviceSize.Width / destination.Density,
                destination.DeviceSize.Height / destination.Density);
            executionCanvas = ImmediateCanvas.CreateExecutorManaged(
                executionLease.Target,
                destination.Density,
                maxWorkingScale,
                executionLogicalSize);
            using (executionCanvas.PushDeviceSpace())
            using (SKImage priorTarget = destination._renderTarget.Value.Snapshot())
            using (var copyPaint = new SKPaint { BlendMode = SKBlendMode.Src })
            {
                executionCanvas.Canvas.DrawImage(priorTarget, 0, 0, copyPaint);
            }

            executionCanvas.Transform = destination.Transform;
            executionCanvas.Opacity = destination.Opacity;
            executionCanvas.BlendMode = destination.BlendMode;
            executor.Execute(
                request,
                executionCanvas,
                () => CommitExpandedTarget(
                    executionCanvas,
                    destination,
                    request.SelectedOutputBounds),
                request.ExecutionTargetBounds);
        }
        finally
        {
            executionCanvas?.Dispose();
            executionLease?.Dispose();
        }
    }

    private static void CommitExpandedTarget(
        ImmediateCanvas executionCanvas,
        ImmediateCanvas destination,
        Rect selectedOutputBounds)
    {
        if (selectedOutputBounds.Width == 0 || selectedOutputBounds.Height == 0)
            return;

        using SKImage completedTarget = executionCanvas._renderTarget.Value.Snapshot();
        using (destination.PushClip(selectedOutputBounds))
        using (destination.PushDeviceSpace())
        using (var commitPaint = new SKPaint { BlendMode = SKBlendMode.Src })
        {
            destination.Canvas.DrawImage(completedTarget, 0, 0, commitPaint);
        }
    }

    /// <summary>Synchronously rasterizes the selected output into a new caller-owned result.</summary>
    /// <returns>
    /// A non-null disposable result. Its bitmap is null only for a successful empty selection and remains valid
    /// after this renderer is disposed.
    /// </returns>
    /// <remarks>The result exclusively owns its bitmap; callers dispose the result rather than the bitmap.</remarks>
    /// <exception cref="ObjectDisposedException">This renderer is disposed.</exception>
    public RenderNodeRasterization Rasterize()
    {
        RenderExecutionCallbackGuard.ThrowIfRendererLaunchForbidden();
        ThrowIfDisposed();
        CompiledRenderRequest? request = null;
        RenderRequestOwner? owner = null;
        RenderTargetLeaseSession? targets = null;
        RenderTargetLease? rootLease = null;
        ImmediateCanvas? canvas = null;
        Bitmap? bitmap = null;
        Rect selectedBounds = default;
        ExceptionDispatchInfo? primary = null;
        try
        {
            targets = _targetRegistry.BeginSession(Options.Intent);
            request = RecordAndCompile(
                Options.RenderPurpose,
                Options.OutputScale,
                Options.MaxWorkingScale,
                Options.TargetDomain,
                targets);
            owner = request.Request.Options.Owner;
            selectedBounds = request.SelectedOutputBounds;
            if (selectedBounds.Width != 0 && selectedBounds.Height != 0)
            {
                Rect executionBounds = request.ExecutionTargetBounds;
                PixelRect deviceBounds = PixelRect.FromRect(executionBounds, Options.OutputScale);
                Rect rasterBounds = deviceBounds.ToRect(Options.OutputScale);
                rootLease = targets.Acquire(deviceBounds.Size);
                canvas = ImmediateCanvas.CreateExecutorManaged(
                    rootLease.Target,
                    Options.OutputScale,
                    Options.MaxWorkingScale,
                    rasterBounds.Size);
                canvas.Clear();
                IDisposable? transform = canvas.PushTransform(
                    Matrix.CreateTranslation(-rasterBounds.X, -rasterBounds.Y));
                try
                {
                    var executor = new RenderRequestExecutor(targets, _programCache);
                    executor.Execute(
                        request,
                        canvas,
                        () =>
                        {
                            transform.Dispose();
                            transform = null;
                            using Bitmap complete = rootLease.Target.Snapshot();
                            PixelRect selectedDeviceBounds = PixelRect.FromRect(
                                selectedBounds,
                                Options.OutputScale);
                            var selectedSubset = new PixelRect(
                                selectedDeviceBounds.X - deviceBounds.X,
                                selectedDeviceBounds.Y - deviceBounds.Y,
                                selectedDeviceBounds.Width,
                                selectedDeviceBounds.Height);
                            bitmap = complete.ExtractSubset(selectedSubset);
                            canvas.Dispose();
                            canvas = null;
                            rootLease.Dispose();
                            rootLease = null;
                        },
                        request.ExecutionTargetBounds);
                    LastExecutionStatistics = executor.Statistics;
                }
                finally
                {
                    transform?.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            primary = ExceptionDispatchInfo.Capture(ex);
        }
        finally
        {
            DisposeAndCapture(canvas, ref primary);
            DisposeAndCapture(rootLease, ref primary);
            DisposeAndCapture(request, ref primary);
            DisposeAndCapture(targets, ref primary);
        }

        try
        {
            ThrowAfterCleanup(primary, owner, targets);
        }
        catch
        {
            DisposeBestEffort(bitmap);
            throw;
        }

        return new RenderNodeRasterization(selectedBounds, Options.OutputScale, bitmap);
    }

    /// <summary>Resolves request-wide output and query metadata without executing deferred work.</summary>
    /// <returns>The resolved measurement.</returns>
    /// <remarks>This call performs no pixel callback, target allocation, readback, or cache publication.</remarks>
    /// <exception cref="ObjectDisposedException">This renderer is disposed.</exception>
    public RenderNodeMeasurement Measure()
    {
        RenderExecutionCallbackGuard.ThrowIfRendererLaunchForbidden();
        ThrowIfDisposed();
        RenderRequest request = CreateRequest(
            RenderRequestPurpose.Bounds,
            Options.OutputScale,
            Options.MaxWorkingScale,
            Options.TargetDomain);
        RenderRequestOwner owner = request.Options.Owner;
        RenderNodeMeasurement measurement = default;
        ExceptionDispatchInfo? primary = null;
        try
        {
            var recorder = new RenderRequestRecorder(request);
            RecordedRenderGraph graph = recorder.Record(Root);
            measurement = new RenderRequestCompiler().ResolveMetadata(request, graph);
            request.CompleteMetadataOnly();
        }
        catch (Exception ex)
        {
            primary = ExceptionDispatchInfo.Capture(ex);
        }
        finally
        {
            DisposeAndCapture(request, ref primary);
        }

        ThrowAfterCleanup(primary, owner, targets: null);
        return measurement;
    }

    /// <summary>Tests the root at a logical point using recorded CPU-only metadata.</summary>
    /// <param name="point">The point in root request coordinates.</param>
    /// <returns><see langword="true"/> when a published fragment is hit.</returns>
    /// <remarks>This call performs no pixel callback, target allocation, or readback.</remarks>
    /// <exception cref="ObjectDisposedException">This renderer is disposed.</exception>
    public bool HitTest(Point point)
    {
        RenderExecutionCallbackGuard.ThrowIfRendererLaunchForbidden();
        ThrowIfDisposed();
        if (Options.RequestedRegion is { } requested && !requested.Contains(point))
            return false;

        RenderRequest request = CreateRequest(
            RenderRequestPurpose.HitTest,
            Options.OutputScale,
            Options.MaxWorkingScale,
            Options.TargetDomain);
        RenderRequestOwner owner = request.Options.Owner;
        bool result = false;
        ExceptionDispatchInfo? primary = null;
        try
        {
            var recorder = new RenderRequestRecorder(request);
            RecordedRenderGraph graph = recorder.Record(Root);
            var compiler = new RenderRequestCompiler();
            _ = compiler.ResolveMetadata(request, graph);
            var roots = RenderRequestCompiler.ResolveRoots(graph);
            for (int index = roots.Length - 1; index >= 0; index--)
            {
                if (roots[index].HitTest(point))
                {
                    result = true;
                    break;
                }
            }

            request.CompleteMetadataOnly();
        }
        catch (Exception ex)
        {
            primary = ExceptionDispatchInfo.Capture(ex);
        }
        finally
        {
            DisposeAndCapture(request, ref primary);
        }

        ThrowAfterCleanup(primary, owner, targets: null);
        return result;
    }

    /// <summary>Releases renderer-owned plans, programs, and pooled targets.</summary>
    /// <remarks>
    /// Disposal is idempotent and attempts every owned cleanup while preserving the first failure. It does not
    /// dispose the root, root cache, target factory, destinations, or previously returned rasterizations.
    /// </remarks>
    public void Dispose()
    {
        if (IsDisposed)
            return;

        IsDisposed = true;
        Exception? primary = null;
        try
        {
            _targetRegistry.Dispose();
        }
        catch (Exception ex)
        {
            primary = ex;
        }

        try
        {
            _programCache.Dispose();
        }
        catch (Exception ex)
        {
            primary ??= ex;
        }

        try
        {
            _structuralPlanCache.Dispose();
        }
        catch (Exception ex)
        {
            primary ??= ex;
        }

        if (primary is not null)
            ExceptionDispatchInfo.Capture(primary).Throw();
    }

    private CompiledRenderRequest RecordAndCompile(
        RenderRequestPurpose purpose,
        float outputScale,
        float maxWorkingScale,
        Rect? targetDomain,
        RenderTargetLeaseSession targets)
    {
        ArgumentNullException.ThrowIfNull(targets);
        RenderRequest request = CreateRequest(purpose, outputScale, maxWorkingScale, targetDomain);
        try
        {
            var recorder = new RenderRequestRecorder(request);
            RecordedRenderGraph graph = recorder.Record(Root);
            bool allowPersistentLookup = Options.UseRenderCache
                                         && purpose is not (RenderRequestPurpose.Bounds or RenderRequestPurpose.HitTest);
            bool allowCapturePublication = allowPersistentLookup
                                           && purpose is RenderRequestPurpose.Frame or RenderRequestPurpose.CacheWarmup;
            var cacheContext = new RenderCacheResolutionContext(
                RenderCacheFormatIdentity.LinearPremultipliedRgba16Float,
                targets.CacheDeviceContextIdentity,
                allowPersistentLookup,
                allowCapturePublication);
            return new RenderRequestCompiler(
                    _structuralPlanCache,
                    cacheContext,
                    allowPersistentLookup ? RenderNodeCacheLookup.Instance : null)
                .Compile(request, graph);
        }
        catch (Exception ex)
        {
            ExceptionDispatchInfo? primary = ExceptionDispatchInfo.Capture(ex);
            DisposeAndCapture(request, ref primary);
            primary!.Throw();
            throw;
        }
    }

    private RenderRequest CreateRequest(
        RenderRequestPurpose purpose,
        float outputScale,
        float maxWorkingScale,
        Rect? targetDomain)
        => new(new RenderRequestOptions(
            Options.Intent,
            purpose,
            targetDomain,
            Options.RequestedRegion,
            outputScale,
            maxWorkingScale,
            new RenderCacheOptions(Options.UseRenderCache, Options.CacheRules),
            Options.FusionMode,
            diagnostics: Options.Diagnostics));

    private static Rect ResolveDestinationTargetDomain(ImmediateCanvas destination)
    {
        Matrix rootToViewport = destination.Transform.Append(
            Matrix.CreateScale(1 / destination.Density, 1 / destination.Density));
        if (!rootToViewport.TryInvert(out Matrix inverse))
        {
            throw new InvalidOperationException(
                "The destination's active transform must be invertible to resolve its root target domain.");
        }

        Rect domain = new Rect(default, destination.LogicalSize).TransformToAABB(inverse);
        if (!RenderRectValidation.IsFiniteNonNegative(domain)
            || domain.Width == 0
            || domain.Height == 0)
        {
            throw new InvalidOperationException(
                "The destination's active transform did not produce a finite non-empty root target domain.");
        }

        return domain;
    }

    private static void DisposeAndCapture(IDisposable? disposable, ref ExceptionDispatchInfo? primary)
    {
        try
        {
            disposable?.Dispose();
        }
        catch (Exception ex)
        {
            primary ??= ExceptionDispatchInfo.Capture(ex);
        }
    }

    private static void ThrowAfterCleanup(
        ExceptionDispatchInfo? primary,
        RenderRequestOwner? owner,
        RenderTargetLeaseSession? targets)
    {
        primary?.Throw();
        owner?.ThrowIfFailed();
        targets?.ThrowIfCleanupFailed();
    }

    private static float SanitizeOutputScale(float outputScale)
        => float.IsFinite(outputScale) && outputScale > 0 ? outputScale : 1;

    private static void ValidateTargetDomain(Rect? domain)
    {
        if (domain is not { } value)
            return;

        if (!RenderRectValidation.IsFiniteNonNegative(value)
            || value.Width == 0
            || value.Height == 0)
        {
            throw new ArgumentException(
                "A target domain must be finite and non-empty.",
                nameof(domain));
        }
    }

    private static void ValidateRequestedRegion(Rect? region)
    {
        if (region is { } value && !RenderRectValidation.IsFiniteNonNegative(value))
        {
            throw new ArgumentException(
                "A requested region must be finite and have non-negative dimensions.",
                nameof(region));
        }
    }

    private static void DisposeBestEffort(IDisposable? disposable)
    {
        try
        {
            disposable?.Dispose();
        }
        catch
        {
            // A teardown fault must not replace an in-flight render or allocation failure.
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
    }
}
