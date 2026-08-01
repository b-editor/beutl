using System.Runtime.ExceptionServices;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Rendering;

/// <summary>Describes one complete request issued through a <see cref="RenderNodeRenderer"/>.</summary>
public sealed record RenderNodeRenderRequest
{
    /// <summary>Gets the intent that selects allocation-failure behavior.</summary>
    public RenderIntent Intent { get; init; } = RenderIntent.Preview;

    /// <summary>Gets the optional finite logical domain for target-less root target accesses.</summary>
    /// <remarks>
    /// A non-null value must be finite and non-empty. It is used by target-less renderer operations when a
    /// root fragment requires a target domain. Rendering into a supplied canvas uses its destination viewport
    /// instead. <see langword="null"/> is valid for self-bounded graphs that do not require a root
    /// <see cref="TargetRegion.Full"/> access.
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

    /// <summary>Gets the persistent render-node cache admission policy for this request.</summary>
    public RenderCacheOptions CacheOptions { get; init; } = RenderCacheOptions.Default;

    /// <summary>Gets the execution purpose observed by render callbacks and cache policy.</summary>
    /// <remarks>
    /// <see cref="RenderNodeRenderer.Render"/> and <see cref="RenderNodeRenderer.Rasterize"/> preserve this value.
    /// Metadata-only measurement and hit-testing use their dedicated engine purposes.
    /// </remarks>
    public RenderRequestPurpose Purpose { get; init; } = RenderRequestPurpose.Auxiliary;

    internal FusionMode FusionMode { get; init; } = FusionMode.Enabled;

    internal IRenderPipelineDiagnosticsState? Diagnostics { get; init; }
}

/// <summary>Configures renderer-lifetime ownership and the request used when an operation omits one.</summary>
public sealed class RenderNodeRendererOptions
{
    /// <summary>Gets the complete default request copied and sanitized for the renderer lifetime.</summary>
    public RenderNodeRenderRequest DefaultRequest { get; init; } = new();

    /// <summary>Gets the optional caller-owned factory for renderer-owned intermediate targets.</summary>
    /// <remarks><see langword="null"/> selects the engine's current-backend RGBA16F allocator.</remarks>
    public IRenderTargetFactory? TargetFactory { get; init; }
}

/// <summary>Identifies the pixel format required for a renderer-owned target allocation.</summary>
public enum RenderTargetPixelFormat : byte
{
    /// <summary>Linear-sRGB, premultiplied-alpha RGBA with 16-bit floating-point components.</summary>
    LinearPremultipliedRgba16Float,
}

/// <summary>Describes one renderer-owned target allocation.</summary>
public readonly record struct RenderTargetAllocationDescriptor
{
    internal RenderTargetAllocationDescriptor(
        PixelSize deviceSize,
        GRRecordingContext? graphicsContext,
        nint? graphicsContextHandle)
    {
        DeviceSize = deviceSize;
        GraphicsContext = graphicsContext;
        GraphicsContextHandle = graphicsContextHandle;
    }

    /// <summary>Gets the exact positive device-pixel size.</summary>
    public PixelSize DeviceSize { get; }

    /// <summary>Gets the required pixel format.</summary>
    public RenderTargetPixelFormat PixelFormat =>
        RenderTargetPixelFormat.LinearPremultipliedRgba16Float;

    /// <summary>
    /// Gets the borrowed Skia context for a context-bound GPU request, or <see langword="null"/> for a
    /// CPU request or a target-less request whose backend is not bound yet.
    /// </summary>
    /// <remarks>The factory may use this value only for the duration of <see cref="IRenderTargetFactory.Create"/>.</remarks>
    public GRRecordingContext? GraphicsContext { get; }

    /// <summary>
    /// Gets the required Skia context handle: a positive value for GPU, zero for CPU, or
    /// <see langword="null"/> when a target-less request has not bound a backend yet.
    /// </summary>
    public nint? GraphicsContextHandle { get; }

    /// <summary>Gets the required GPU backend, or <see langword="null"/> when no GPU context is bound.</summary>
    public GRBackend? GraphicsBackend => GraphicsContext?.Backend;
}

/// <summary>Creates fresh linear-premultiplied RGBA16F targets requested by a renderer.</summary>
public interface IRenderTargetFactory
{
    /// <summary>Creates a target satisfying the exact allocation requirements.</summary>
    /// <param name="allocation">The size, format, backend, and device/context requirements.</param>
    /// <returns>A new target, or <see langword="null"/> when allocation cannot be satisfied.</returns>
    /// <remarks>
    /// Every non-null return transfers exclusive ownership to the renderer immediately and must be fresh,
    /// unleased, and satisfy the size, format, and context requirements in <paramref name="allocation"/>.
    /// The renderer disposes an invalid non-null return. The factory itself remains caller-owned and is never
    /// disposed by the renderer.
    /// </remarks>
    RenderTarget? Create(RenderTargetAllocationDescriptor allocation);
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
    private RenderCacheDeviceContextIdentity? _programCacheContext;

    /// <summary>Creates a renderer for a caller-owned root node.</summary>
    /// <param name="root">The non-null caller-owned root recorded for every request.</param>
    /// <param name="options">
    /// Renderer ownership options and a default request copied for the renderer lifetime, or
    /// <see langword="null"/> to use defaults.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="root"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The configured render intent or request purpose is not defined.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// A configured target domain or requested region is not finite, or the target domain is empty.
    /// </exception>
    public RenderNodeRenderer(RenderNode root, RenderNodeRendererOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        options ??= new RenderNodeRendererOptions();
        ArgumentNullException.ThrowIfNull(options.DefaultRequest);

        Root = root;
        Options = new RenderNodeRendererOptions
        {
            DefaultRequest = CopyAndSanitizeRequest(options.DefaultRequest),
            TargetFactory = options.TargetFactory,
        };
        _targetRegistry = new RenderTargetLeaseRegistry(Options.TargetFactory);
        _structuralPlanCache = new StructuralPlanCache();
        _programCache = SkRuntimeEffectProgramCache.Create();
    }

    /// <summary>Gets the caller-owned root node.</summary>
    public RenderNode Root { get; }

    /// <summary>Gets the sanitized renderer option snapshot owned by this renderer.</summary>
    public RenderNodeRendererOptions Options { get; }

    /// <summary>Gets whether this renderer has released its owned state.</summary>
    public bool IsDisposed { get; private set; }

    internal RenderExecutionStatistics LastExecutionStatistics { get; private set; }

    internal StructuralPlanCacheStatistics StructuralPlanCacheStatistics
        => _structuralPlanCache.Statistics;

    internal ProgramCacheStatistics ProgramCacheStatistics => _programCache.Statistics;

    internal RenderTargetPoolStatistics TargetPoolStatistics => _targetRegistry.Statistics;

    internal long ReleaseRetainedTargets()
    {
        ThrowIfDisposed();
        return _targetRegistry.ReleaseRetainedTargets();
    }

    /// <summary>Synchronously renders the selected root stream into a borrowed destination.</summary>
    /// <param name="destination">The non-null caller-owned destination canvas.</param>
    /// <param name="requestOptions">
    /// A complete request, or <see langword="null"/> to use <see cref="RenderNodeRendererOptions.DefaultRequest"/>.
    /// The destination supplies output scale and target domain; its maximum working scale clamps this request.
    /// </param>
    /// <remarks>
    /// The call preserves the destination's active transform, clip, opacity, blend mode, density, and ownership.
    /// A singular active transform completes value-only self-bounded work as a successful no-op. Domain-independent
    /// target effects still execute for ordering, while work that requires the destination's root target domain
    /// remains invalid because no inverse domain exists. The call does not close, dispose, flush, submit, clear, or
    /// snapshot the destination implicitly.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="destination"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">This renderer or <paramref name="destination"/> is disposed.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The request purpose is reserved for metadata-only measurement or hit testing.
    /// </exception>
    public void Render(
        ImmediateCanvas destination,
        RenderNodeRenderRequest? requestOptions = null)
    {
        RenderExecutionCallbackGuard.ThrowIfRendererLaunchForbidden();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(destination);
        ObjectDisposedException.ThrowIf(destination.IsDisposed, destination);
        RenderNodeRenderRequest effectiveRequest = ResolveRequest(requestOptions);
        ThrowIfInvalidExecutionPurpose(effectiveRequest.Purpose);

        bool hasExplicitEmptySelection = effectiveRequest.RequestedRegion is { } requested
                                         && (requested.Width == 0 || requested.Height == 0);
        float maxWorkingScale = MathF.Min(effectiveRequest.MaxWorkingScale, destination.MaxWorkingScale);
        bool hasInvertibleDestination = TryResolveDestinationTargetDomain(destination, out Rect resolvedTargetDomain);
        Rect? targetDomain = hasInvertibleDestination ? resolvedTargetDomain : null;
        RenderTargetLeaseSession targets = _targetRegistry.BeginSession(
            effectiveRequest.Intent,
            destination._renderTarget);
        CompiledRenderRequest? request = null;
        RenderRequestOwner? owner = null;
        ExceptionDispatchInfo? primary = null;
        bool completeFailedDiagnostics = false;
        RenderPipelineFailurePhase preExecutionFailurePhase = RenderPipelineFailurePhase.Allocation;
        try
        {
            request = RecordAndCompile(
                effectiveRequest.Purpose,
                destination.Density,
                maxWorkingScale,
                targetDomain,
                targets,
                effectiveRequest,
                DeviceGridAlignment.ResolveLogicalOffset(destination));
            owner = request.Request.Options.Owner;
            var executor = new RenderRequestExecutor(targets, _programCache);
            if (!hasInvertibleDestination && !request.Measurement.HasTargetEffects)
            {
                preExecutionFailurePhase = RenderPipelineFailurePhase.Execution;
                executor.CompleteNoOp(request);
            }
            else if (hasExplicitEmptySelection)
            {
                preExecutionFailurePhase = RenderPipelineFailurePhase.Execution;
                executor.CompleteEmptySelection(request);
            }
            else if (request.ExecutionTargetBounds == request.SelectedOutputBounds)
            {
                preExecutionFailurePhase = RenderPipelineFailurePhase.Execution;
                executor.Execute(request, destination);
            }
            else
            {
                if (destination.HasActiveSaveLayer)
                {
                    throw new InvalidOperationException(
                        "Expanded render execution cannot copy a destination while an ImmediateCanvas SaveLayer scope is active. Close the layer before rendering the expanded request.");
                }

                ExecuteWithExpandedTarget(
                    request,
                    destination,
                    targets,
                    executor,
                    maxWorkingScale,
                    ref preExecutionFailurePhase);
            }
            LastExecutionStatistics = executor.Statistics;
        }
        catch (Exception ex)
        {
            primary = ExceptionDispatchInfo.Capture(ex);
            completeFailedDiagnostics = FailRequestFamilyBeforeExecution(
                request,
                ex,
                preExecutionFailurePhase);
        }
        finally
        {
            DisposeAndCapture(request, ref primary);
            DisposeAndCapture(targets, ref primary);
        }

        if (request is not null && completeFailedDiagnostics)
        {
            CompleteFailedFamilyDiagnostics(
                request,
                owner?.CleanupFailures ?? [],
                targets.CleanupFailures);
        }

        ThrowAfterCleanup(primary, owner, targets);
    }

    private static void ExecuteWithExpandedTarget(
        CompiledRenderRequest request,
        ImmediateCanvas destination,
        RenderTargetLeaseSession targets,
        RenderRequestExecutor executor,
        float maxWorkingScale,
        ref RenderPipelineFailurePhase preExecutionFailurePhase)
    {
        RenderTargetLease? executionLease = null;
        ImmediateCanvas? executionCanvas = null;
        ExceptionDispatchInfo? primary = null;

        void FinalizeExternalResources()
        {
            ImmediateCanvas? canvasToDispose = executionCanvas;
            executionCanvas = null;
            RenderTargetLease? leaseToDispose = executionLease;
            executionLease = null;
            DisposeExecutionResources(canvasToDispose, leaseToDispose);
        }

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
                executionLogicalSize,
                destination.DeviceOrigin);
            using (executionCanvas.PushDeviceSpace())
            using (SKImage priorTarget = destination._renderTarget.Value.Snapshot())
            using (var copyPaint = new SKPaint { BlendMode = SKBlendMode.Src })
            {
                executionCanvas.Canvas.DrawImage(priorTarget, 0, 0, copyPaint);
            }

            executionCanvas.Transform = destination.Transform;
            executionCanvas.Opacity = destination.Opacity;
            executionCanvas.BlendMode = destination.BlendMode;
            preExecutionFailurePhase = RenderPipelineFailurePhase.Execution;
            executor.Execute(
                request,
                executionCanvas,
                () => CommitExpandedTarget(
                    executionCanvas,
                    destination,
                    request.SelectedOutputBounds),
                request.ExecutionTargetBounds,
                FinalizeExternalResources);
        }
        catch (Exception ex)
        {
            primary = ExceptionDispatchInfo.Capture(ex);
        }
        finally
        {
            DisposeExecutionResourcesAndCapture(
                request.Request.Options.Owner,
                ref primary,
                executionCanvas,
                executionLease);
        }

        primary?.Throw();
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
    /// <remarks>
    /// The result exclusively owns its bitmap; callers dispose the result rather than the bitmap. A non-empty
    /// result reports the device-pixel-aligned cover of the selected output, so its bounds scaled by
    /// <see cref="RenderNodeRenderRequest.OutputScale"/> are exactly the returned bitmap's pixel extent and
    /// origin.
    /// </remarks>
    /// <param name="requestOptions">
    /// A complete request, or <see langword="null"/> to use <see cref="RenderNodeRendererOptions.DefaultRequest"/>.
    /// </param>
    /// <exception cref="ObjectDisposedException">This renderer is disposed.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The request purpose is reserved for metadata-only measurement or hit testing.
    /// </exception>
    public RenderNodeRasterization Rasterize(RenderNodeRenderRequest? requestOptions = null)
    {
        RenderExecutionCallbackGuard.ThrowIfRendererLaunchForbidden();
        ThrowIfDisposed();
        RenderNodeRenderRequest effectiveRequest = ResolveRequest(requestOptions);
        ThrowIfInvalidExecutionPurpose(effectiveRequest.Purpose);
        CompiledRenderRequest? request = null;
        RenderRequestOwner? owner = null;
        RenderTargetLeaseSession? targets = null;
        RenderTargetLease? rootLease = null;
        ImmediateCanvas? canvas = null;
        Bitmap? bitmap = null;
        Rect selectedBounds = default;
        ExceptionDispatchInfo? primary = null;
        bool completeFailedDiagnostics = false;
        RenderPipelineFailurePhase preExecutionFailurePhase = RenderPipelineFailurePhase.Allocation;
        try
        {
            targets = _targetRegistry.BeginSession(effectiveRequest.Intent);
            request = RecordAndCompile(
                effectiveRequest.Purpose,
                effectiveRequest.OutputScale,
                effectiveRequest.MaxWorkingScale,
                effectiveRequest.TargetDomain,
                targets,
                effectiveRequest);
            owner = request.Request.Options.Owner;
            selectedBounds = request.SelectedOutputBounds;
            if (selectedBounds.Width != 0 && selectedBounds.Height != 0)
            {
                PixelRect deviceBounds = PixelRect.FromRect(
                    request.ExecutionTargetBounds,
                    effectiveRequest.OutputScale);
                PixelRect selectedDeviceBounds = PixelRect.FromRect(selectedBounds, effectiveRequest.OutputScale);
                selectedBounds = selectedDeviceBounds.ToRect(effectiveRequest.OutputScale);
                Rect rasterBounds = deviceBounds.ToRect(effectiveRequest.OutputScale);
                rootLease = targets.Acquire(deviceBounds.Size);
                canvas = ImmediateCanvas.CreateExecutorManaged(
                    rootLease.Target,
                    effectiveRequest.OutputScale,
                    effectiveRequest.MaxWorkingScale,
                    rasterBounds.Size,
                    deviceBounds.Position);
                canvas.Clear();

                IDisposable? transform = canvas.PushTransform(
                    Matrix.CreateTranslation(-rasterBounds.X, -rasterBounds.Y));

                IDisposable?[] TakeExternalResources()
                {
                    IDisposable? transformToDispose = transform;
                    transform = null;
                    ImmediateCanvas? canvasToDispose = canvas;
                    canvas = null;
                    RenderTargetLease? leaseToDispose = rootLease;
                    rootLease = null;
                    return [transformToDispose, canvasToDispose, leaseToDispose];
                }

                void FinalizeExternalResources()
                    => DisposeExecutionResources(TakeExternalResources());

                void FinalizeExternalResourcesAndCapture(ref ExceptionDispatchInfo? failure)
                    => DisposeExecutionResourcesAndCapture(
                        owner!,
                        ref failure,
                        TakeExternalResources());

                ExceptionDispatchInfo? executionPrimary = null;
                try
                {
                    var executor = new RenderRequestExecutor(targets, _programCache);
                    preExecutionFailurePhase = RenderPipelineFailurePhase.Execution;
                    executor.Execute(
                        request,
                        canvas,
                        () =>
                        {
                            IDisposable? transformToDispose = transform;
                            transform = null;
                            transformToDispose?.Dispose();
                            var selectedSubset = new PixelRect(
                                selectedDeviceBounds.X - deviceBounds.X,
                                selectedDeviceBounds.Y - deviceBounds.Y,
                                selectedDeviceBounds.Width,
                                selectedDeviceBounds.Height);
                            Bitmap complete = rootLease.Target.Snapshot();
                            bitmap = TakeRasterizationBitmap(complete, selectedSubset);
                        },
                        request.ExecutionTargetBounds,
                        FinalizeExternalResources);
                    LastExecutionStatistics = executor.Statistics;
                }
                catch (Exception ex)
                {
                    executionPrimary = ExceptionDispatchInfo.Capture(ex);
                }
                finally
                {
                    FinalizeExternalResourcesAndCapture(ref executionPrimary);
                }

                executionPrimary?.Throw();
            }
            else
            {
                var executor = new RenderRequestExecutor(targets, _programCache);
                preExecutionFailurePhase = RenderPipelineFailurePhase.Execution;
                executor.CompleteEmptySelection(request);
                LastExecutionStatistics = executor.Statistics;
            }
        }
        catch (Exception ex)
        {
            primary = ExceptionDispatchInfo.Capture(ex);
            completeFailedDiagnostics = FailRequestFamilyBeforeExecution(
                request,
                ex,
                preExecutionFailurePhase);
        }
        finally
        {
            if (owner is not null)
            {
                DisposeExecutionResourcesAndCapture(owner, ref primary, canvas, rootLease);
            }
            else
            {
                DisposeAndCapture(canvas, ref primary);
                DisposeAndCapture(rootLease, ref primary);
            }
            DisposeAndCapture(request, ref primary);
            DisposeAndCapture(targets, ref primary);
        }

        if (request is not null && completeFailedDiagnostics)
        {
            CompleteFailedFamilyDiagnostics(
                request,
                owner?.CleanupFailures ?? [],
                targets?.CleanupFailures ?? []);
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

        return new RenderNodeRasterization(selectedBounds, effectiveRequest.OutputScale, bitmap);
    }

    internal static Bitmap TakeRasterizationBitmap(Bitmap complete, PixelRect selectedSubset)
    {
        ArgumentNullException.ThrowIfNull(complete);
        complete.ThrowIfDisposed();

        if (selectedSubset == new PixelRect(0, 0, complete.Width, complete.Height))
            return complete;

        try
        {
            return complete.ExtractSubset(selectedSubset);
        }
        finally
        {
            complete.Dispose();
        }
    }

    /// <summary>Resolves request-wide output and query metadata without executing deferred work.</summary>
    /// <returns>The resolved measurement.</returns>
    /// <remarks>This call performs no pixel callback, target allocation, readback, or cache publication.</remarks>
    /// <param name="requestOptions">
    /// A complete request, or <see langword="null"/> to use <see cref="RenderNodeRendererOptions.DefaultRequest"/>.
    /// </param>
    /// <exception cref="ObjectDisposedException">This renderer is disposed.</exception>
    public RenderNodeMeasurement Measure(RenderNodeRenderRequest? requestOptions = null)
    {
        RenderExecutionCallbackGuard.ThrowIfRendererLaunchForbidden();
        ThrowIfDisposed();
        RenderNodeRenderRequest effectiveRequest = ResolveRequest(requestOptions);
        RenderRequest request = CreateRequest(
            RenderRequestPurpose.Bounds,
            effectiveRequest.OutputScale,
            effectiveRequest.MaxWorkingScale,
            effectiveRequest.TargetDomain,
            effectiveRequest);
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
    /// <param name="requestOptions">
    /// A complete request, or <see langword="null"/> to use <see cref="RenderNodeRendererOptions.DefaultRequest"/>.
    /// </param>
    /// <returns><see langword="true"/> when a published fragment is hit.</returns>
    /// <remarks>This call performs no pixel callback, target allocation, or readback.</remarks>
    /// <exception cref="ObjectDisposedException">This renderer is disposed.</exception>
    public bool HitTest(Point point, RenderNodeRenderRequest? requestOptions = null)
    {
        RenderExecutionCallbackGuard.ThrowIfRendererLaunchForbidden();
        ThrowIfDisposed();
        RenderNodeRenderRequest effectiveRequest = ResolveRequest(requestOptions);
        bool pointInRequestedRegion = effectiveRequest.RequestedRegion is not { } requested
                                      || (requested.Width > 0
                                          && requested.Height > 0
                                          && requested.Contains(point));

        RenderRequest request = CreateRequest(
            RenderRequestPurpose.HitTest,
            effectiveRequest.OutputScale,
            effectiveRequest.MaxWorkingScale,
            effectiveRequest.TargetDomain,
            effectiveRequest);
        RenderRequestOwner owner = request.Options.Owner;
        bool result = false;
        ExceptionDispatchInfo? primary = null;
        try
        {
            var recorder = new RenderRequestRecorder(request);
            RecordedRenderGraph graph = recorder.Record(Root);
            var compiler = new RenderRequestCompiler();
            _ = compiler.ResolveMetadata(request, graph);
            if (pointInRequestedRegion)
            {
                var roots = RenderRequestCompiler.ResolveRoots(graph);
                for (int index = roots.Length - 1; index >= 0; index--)
                {
                    if (roots[index].HitTest(point))
                    {
                        result = true;
                        break;
                    }
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
        RenderTargetLeaseSession targets,
        RenderNodeRenderRequest renderRequest,
        Vector deviceGridOffset = default)
    {
        ArgumentNullException.ThrowIfNull(targets);
        RenderRequest request = CreateRequest(
            purpose,
            outputScale,
            maxWorkingScale,
            targetDomain,
            renderRequest);
        try
        {
            SynchronizeProgramCacheContext(targets);
            var recorder = new RenderRequestRecorder(request);
            RecordedRenderGraph graph = recorder.Record(Root);
            bool allowPersistentLookup = renderRequest.CacheOptions.IsEnabled
                                         && purpose is not (RenderRequestPurpose.Bounds or RenderRequestPurpose.HitTest);
            bool allowCapturePublication = allowPersistentLookup
                                           && purpose is RenderRequestPurpose.Frame or RenderRequestPurpose.CacheWarmup;
            var cacheContext = new RenderCacheResolutionContext(
                RenderCacheFormatIdentity.LinearPremultipliedRgba16Float,
                targets.CacheDeviceContextIdentity,
                allowPersistentLookup,
                allowCapturePublication,
                deviceGridOffset);
            SkslBackendBudget shaderBudget = SkslBackendBudgetResolver.Resolve(
                targets.ExternalTarget?.Value.Context?.Backend);
            return new RenderRequestCompiler(
                    _structuralPlanCache,
                    cacheContext,
                    allowPersistentLookup ? RenderNodeCacheLookup.Instance : null)
                .Compile(request, graph, shaderBudget);
        }
        catch (Exception ex)
        {
            ExceptionDispatchInfo? primary = ExceptionDispatchInfo.Capture(ex);
            DisposeAndCapture(request, ref primary);
            primary!.Throw();
            throw;
        }
    }

    private void SynchronizeProgramCacheContext(RenderTargetLeaseSession targets)
    {
        RenderCacheDeviceContextIdentity current = targets.CacheDeviceContextIdentity;
        if (_programCacheContext is { } previous && previous != current)
        {
            _programCache.EvictContext(
                previous.DeviceIdentity,
                previous.ContextIdentity);
        }

        _programCacheContext = current;
    }

    private RenderRequest CreateRequest(
        RenderRequestPurpose purpose,
        float outputScale,
        float maxWorkingScale,
        Rect? targetDomain,
        RenderNodeRenderRequest renderRequest)
        => new(new RenderRequestOptions(
            renderRequest.Intent,
            purpose,
            targetDomain,
            renderRequest.RequestedRegion,
            outputScale,
            maxWorkingScale,
            renderRequest.CacheOptions,
            renderRequest.FusionMode,
            diagnostics: renderRequest.Diagnostics));

    private RenderNodeRenderRequest ResolveRequest(RenderNodeRenderRequest? request)
        => request is null
            ? Options.DefaultRequest
            : CopyAndSanitizeRequest(request);

    private static RenderNodeRenderRequest CopyAndSanitizeRequest(RenderNodeRenderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.CacheOptions);
        if (!Enum.IsDefined(request.Intent))
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                request.Intent,
                "The render intent is not defined.");
        }
        if (!Enum.IsDefined(request.Purpose))
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                request.Purpose,
                "The render request purpose is not defined.");
        }

        ValidateTargetDomain(request.TargetDomain);
        ValidateRequestedRegion(request.RequestedRegion);
        return request with
        {
            OutputScale = SanitizeOutputScale(request.OutputScale),
            MaxWorkingScale = RenderScaleUtilities.SanitizeMaxWorkingScale(request.MaxWorkingScale),
        };
    }

    private static void ThrowIfInvalidExecutionPurpose(RenderRequestPurpose purpose)
    {
        if (purpose is not (RenderRequestPurpose.Frame
            or RenderRequestPurpose.CacheWarmup
            or RenderRequestPurpose.Auxiliary))
        {
            throw new ArgumentOutOfRangeException(
                nameof(purpose),
                purpose,
                "Render and Rasterize require Frame, CacheWarmup, or Auxiliary purpose.");
        }
    }

    private static bool TryResolveDestinationTargetDomain(ImmediateCanvas destination, out Rect domain)
    {
        Matrix rootToViewport = destination.Transform.Append(
            Matrix.CreateScale(1 / destination.Density, 1 / destination.Density));
        if (!rootToViewport.TryInvert(out Matrix inverse))
        {
            domain = default;
            return false;
        }

        Size viewportSize = destination.Density == 1f && destination.SurfaceDensity != 1f
            ? destination.DeviceSize.ToSize(1)
            : destination.LogicalSize;
        domain = new Rect(default, viewportSize).TransformToAABB(inverse);
        if (!RenderRectValidation.IsFiniteNonNegative(domain)
            || domain.Width == 0
            || domain.Height == 0)
        {
            throw new InvalidOperationException(
                "The destination's active transform did not produce a finite non-empty root target domain.");
        }

        return true;
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

    internal static void DisposeExecutionResourcesAndCapture(
        RenderRequestOwner owner,
        ref ExceptionDispatchInfo? primary,
        params IDisposable?[] resources)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (primary is not null)
            owner.RecordPrimaryFailure(primary.SourceException);

        try
        {
            DisposeExecutionResources(resources);
        }
        catch (Exception ex)
        {
            IEnumerable<Exception> cleanupFailures = ex is AggregateException aggregate
                ? aggregate.Flatten().InnerExceptions
                : [ex];
            foreach (Exception failure in cleanupFailures)
            {
                owner.RecordCleanupFailure(failure);
                primary ??= ExceptionDispatchInfo.Capture(failure);
            }
        }
    }

    private static void DisposeExecutionResources(params IDisposable?[] resources)
    {
        List<Exception>? failures = null;
        foreach (IDisposable? resource in resources)
        {
            try
            {
                resource?.Dispose();
            }
            catch (Exception ex)
            {
                (failures ??= []).Add(ex);
            }
        }

        if (failures is [var failure])
            ExceptionDispatchInfo.Capture(failure).Throw();
        if (failures is { Count: > 1 })
            throw new AggregateException(failures);
    }

    internal static bool FailRequestFamilyBeforeExecution(
        CompiledRenderRequest? request,
        Exception exception,
        RenderPipelineFailurePhase failurePhase)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (request is null || request.Request.State != RenderRequestState.Planned)
            return false;

        bool completeDiagnostics = RenderRequestDiagnostics.TryGet(request.Request) is not null;
        RenderRequestOwner owner = request.Request.Options.Owner;
        if (owner.PrimaryFailure is null)
            owner.RecordPrimaryFailure(exception);
        MarkFamilyFailedBeforeExecution(request, failurePhase);
        return completeDiagnostics;
    }

    private static void MarkFamilyFailedBeforeExecution(
        CompiledRenderRequest request,
        RenderPipelineFailurePhase failurePhase)
    {
        foreach (CompiledRenderRequest member in EnumerateFamilyDepthFirst(request))
        {
            RenderRequestDiagnostics.TryGet(member.Request)?.RecordFamilyFailure(failurePhase);
            member.Request.FailFamilyMember();
        }
    }

    private static void CompleteFailedFamilyDiagnostics(
        CompiledRenderRequest request,
        IReadOnlyList<Exception> ownerCleanupFailures,
        IReadOnlyList<Exception> targetCleanupFailures)
    {
        foreach (CompiledRenderRequest member in EnumerateFamilyDepthFirst(request))
        {
            RenderPipelineDiagnosticRecorder? diagnostics = RenderRequestDiagnostics.TryGet(member.Request);
            foreach (Exception _ in ownerCleanupFailures)
                diagnostics?.RecordCleanupFailure();
            foreach (Exception _ in targetCleanupFailures)
                diagnostics?.RecordCleanupFailure();
            RenderRequestDiagnostics.Complete(member.Request);
        }
    }

    private static IEnumerable<CompiledRenderRequest> EnumerateFamilyDepthFirst(
        CompiledRenderRequest request)
    {
        foreach (CompiledRenderRequest nested in request.NestedRequests)
        {
            foreach (CompiledRenderRequest member in EnumerateFamilyDepthFirst(nested))
                yield return member;
        }

        yield return request;
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
