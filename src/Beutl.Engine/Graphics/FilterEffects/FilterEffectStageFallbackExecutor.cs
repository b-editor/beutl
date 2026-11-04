using Beutl.Graphics.Backend;
using Beutl.Graphics.Backend.Vulkan;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Requests;
using Beutl.Graphics.Shaders;
using Beutl.Logging;
using Beutl.Media;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

internal static class FilterEffectStageFallbackExecutor
{
    private static readonly ILogger s_logger = Log.CreateLogger("FilterEffectStageFallbackExecutor");

    public static void ApplyShader(
        EffectTargets targets,
        ShaderDescription description,
        FilterEffectExecutionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(settings);
        SkRuntimeEffectProgramAcquirer acquireProgram = settings.ProgramAcquirer
            ?? throw new ArgumentException("A shader stage needs a program acquirer.", nameof(settings));
        settings = settings.WithResolvedBudget();
        ReplaceTargets(
            targets,
            target => ExecuteShader(target, description, acquireProgram, settings));
    }

    public static void ApplyGeometry(
        EffectTargets targets,
        GeometryDescription description,
        FilterEffectExecutionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(settings);
        settings = settings.WithResolvedBudget();
        ReplaceTargets(
            targets,
            target => ExecuteGeometry(target, description, settings));
    }

    public static void ApplyShaderGraph(EffectTargets targets, ShaderGraphDescription description,
        FilterEffectExecutionSettings settings)
    {
        ShaderGraphProgramAcquirer acquireProgram = settings.ShaderGraphProgramAcquirer
            ?? throw new ArgumentException("A shader graph stage needs a program acquirer.", nameof(settings));
        settings = settings.WithResolvedBudget();
        Rect referenceBounds = targets.CalculateBounds();
        ReplaceTargets(targets, source =>
        {
            using EffectTarget? normalized = NormalizeInput(source, settings);
            if (normalized is null)
                return source.IsMaterialized ? source.Clone() : null;
            PixelRect footprint = PixelRect.FromRect(normalized.Bounds.Translate(normalized.DeviceGridOffset), normalized.Scale.Value);
            using EffectTarget? cropped = normalized.DeviceBounds != footprint
                ? CropTarget(normalized, normalized.Bounds, settings) : null;
            EffectTarget? input = normalized.DeviceBounds != footprint ? cropped : normalized;
            if (input?.RenderTarget is not { } inputTarget)
                return source.Clone();
            Rect bounds = description.TransformBounds(input.Bounds, referenceBounds);
            if (IsEmpty(bounds))
                return null;
            ITexture2D texture = inputTarget.Texture
                ?? throw new NotSupportedException("The shader graph input requires a backend texture.");
            float density = description.ClampWorkingScale(input.Bounds,
                ResolveSupplyDensity(input, settings), input.DeviceGridOffset, referenceBounds,
                input.DeviceBounds.Size, ((VulkanTexture2D)texture).Usage);
            EffectTarget? output = AllocateStageOutput(input, bounds, density, settings, native: true);
            if (output?.RenderTarget is not { } outputTarget)
            {
                output?.Dispose();
                return source.Clone();
            }
            try
            {
                inputTarget.PrepareForSampling(RenderTargetSamplingIntent.BackendInterop);
                ShaderGraphExecutor.Execute(description, texture,
                    new(input.Bounds, input.RasterBounds, input.DeviceBounds.Size, input.Scale.Value, referenceBounds),
                    outputTarget.Texture ?? throw new NotSupportedException("The shader graph output requires a backend texture."),
                    output.Scale.Value, input.DeviceGridOffset, acquireProgram, retainedImages: settings.ShaderGraphImages);
                description.ExecutionObserver?.Invoke(null);
                EffectTarget result = output;
                output = null;
                return result;
            }
            catch (Exception ex) when (description.ExecutionObserver is { } observer)
            {
                observer(ex);
                return null;
            }
            finally
            {
                output?.Dispose();
            }
        });
    }

    private static EffectTarget? ExecuteShader(
        EffectTarget source,
        ShaderDescription description,
        SkRuntimeEffectProgramAcquirer acquireProgram,
        FilterEffectExecutionSettings settings)
    {
        using EffectTarget? input = NormalizeInput(source, settings);
        if (input?.RenderTarget is not { } inputTarget)
            return null;

        Rect outputBounds = description.Bounds.TransformBounds(input.Bounds);
        if (IsEmpty(outputBounds))
            return null;

        // A current-pixel stage rewrites the colour of the sample it was handed, so its supply is the
        // input's own density; a whole-source stage resolves one from the supply the way any stage does.
        float density = description.Kind == ShaderDescriptionKind.CurrentPixel
            ? input.Scale.Value
            : ResolveSupplyDensity(input, settings);
        EffectTarget? output = AllocateStageOutput(input, outputBounds, density, settings);
        if (output?.RenderTarget is not { } outputTarget)
        {
            output?.Dispose();
            return null;
        }

        try
        {
            using SurfaceSnapshot.Lease inputSnapshot = SurfaceSnapshot.Take(inputTarget.Value);
            SKImage inputImage = inputSnapshot.Image;
            RunShaderStage(
                description,
                input,
                inputImage,
                inputTarget,
                output,
                outputTarget,
                outputBounds,
                acquireProgram,
                settings);

            EffectTarget result = output;
            output = null;
            return result;
        }
        finally
        {
            output?.Dispose();
        }
    }

    /// <summary>Allocates the buffer a stage writes, at the density its supply and the axis limit allow.</summary>
    private static EffectTarget? AllocateStageOutput(
        EffectTarget input,
        Rect outputBounds,
        float density,
        FilterEffectExecutionSettings settings,
        bool native = false)
    {
        density = settings.ResolveBudget().ClampWorkingScaleToExactFootprint(
            outputBounds.Translate(input.DeviceGridOffset),
            density);
        return AllocateTarget(
            outputBounds,
            density,
            settings,
            deviceGridOffset: input.DeviceGridOffset,
            native: native);
    }

    /// <summary>Resolves the density a stage's output is rasterized at from the supply of its input.</summary>
    private static float ResolveSupplyDensity(EffectTarget input, FilterEffectExecutionSettings settings)
        => RenderScaleUtilities.ResolveWorkingScale(
            [input.Scale],
            settings.OutputScale,
            settings.MaxWorkingScale);

    /// <summary>Compiles the stage's program, binds its inputs, and fills the output buffer with it.</summary>
    private static void RunShaderStage(
        ShaderDescription description,
        EffectTarget input,
        SKImage inputImage,
        RenderTarget inputTarget,
        EffectTarget output,
        RenderTarget outputTarget,
        Rect outputBounds,
        SkRuntimeEffectProgramAcquirer acquireProgram,
        FilterEffectExecutionSettings settings)
    {
        StandaloneShaderProgram program = StandaloneShaderProgram.For(description);
        using ProgramCacheLease<CachedSkRuntimeEffect> lease = acquireProgram(output, program.Source);
        using var uniforms = new SKRuntimeEffectUniforms(lease.Program.Effect);
        using var runtimeChildren = new SKRuntimeEffectChildren(lease.Program.Effect);
        var children = new List<SKShader>();
        try
        {
            BindShaderStage(
                description,
                program,
                input,
                inputImage,
                inputTarget,
                output,
                outputBounds,
                uniforms,
                runtimeChildren,
                children,
                settings);
            PaintShaderStage(
                lease.Program.Effect,
                uniforms,
                runtimeChildren,
                output,
                outputTarget,
                settings.MaxWorkingScale,
                settings.Intent);
        }
        finally
        {
            // Children are released in the reverse of the order they were created, which is what a
            // 'using' stack over them would have done had the count been known at compile time.
            for (int index = children.Count - 1; index >= 0; index--)
                children[index].Dispose();
        }
    }

    /// <summary>Runs the description's binders and hands their results to the runtime effect.</summary>
    private static void BindShaderStage(
        ShaderDescription description,
        StandaloneShaderProgram program,
        EffectTarget input,
        SKImage inputImage,
        RenderTarget inputTarget,
        EffectTarget output,
        Rect outputBounds,
        SKRuntimeEffectUniforms uniforms,
        SKRuntimeEffectChildren runtimeChildren,
        List<SKShader> children,
        FilterEffectExecutionSettings settings)
    {
        if (!description.HasExecutionContextBinder)
        {
            BindShaderStageCore(
                description,
                program,
                input,
                inputImage,
                inputTarget,
                output,
                uniforms,
                runtimeChildren,
                children,
                context: null);
            return;
        }

        BindShaderStageWithCallbacks(
            description,
            program,
            input,
            inputImage,
            inputTarget,
            output,
            outputBounds,
            uniforms,
            runtimeChildren,
            children,
            settings);
    }

    /// <remarks>
    /// One execution session covers the whole phase: every binder is handed the same context, and the token
    /// is completed once so none of them can retain what it was given past the phase.
    /// </remarks>
    private static void BindShaderStageWithCallbacks(
        ShaderDescription description,
        StandaloneShaderProgram program,
        EffectTarget input,
        SKImage inputImage,
        RenderTarget inputTarget,
        EffectTarget output,
        Rect outputBounds,
        SKRuntimeEffectUniforms uniforms,
        SKRuntimeEffectChildren runtimeChildren,
        List<SKShader> children,
        FilterEffectExecutionSettings settings)
    {
        var bindingToken = new RenderExecutionSessionToken();
        bindingToken.RunAndComplete(
            () =>
            {
                var context = new ShaderExecutionContext(
                    bindingToken,
                    input.Bounds,
                    outputBounds,
                    outputBounds,
                    output.DeviceBounds,
                    output.RasterBounds.Position,
                    input.Scale,
                    settings.OutputScale,
                    output.Scale.Value,
                    settings.MaxWorkingScale,
                    settings.Intent,
                    settings.Purpose,
                    description.Kind == ShaderDescriptionKind.WholeSource
                        ? RasterShaderMapping.SemanticRasterOrigin(
                            input.Bounds,
                            input.Scale.Value,
                            input.DeviceBounds,
                            input.RasterBounds)
                        : null);
                BindShaderStageCore(
                    description,
                    program,
                    input,
                    inputImage,
                    inputTarget,
                    output,
                    uniforms,
                    runtimeChildren,
                    children,
                    context);
            });
    }

    private static void BindShaderStageCore(
        ShaderDescription description,
        StandaloneShaderProgram program,
        EffectTarget input,
        SKImage inputImage,
        RenderTarget inputTarget,
        EffectTarget output,
        SKRuntimeEffectUniforms uniforms,
        SKRuntimeEffectChildren runtimeChildren,
        List<SKShader> children,
        ShaderExecutionContext? context)
    {
        SKShader inputShader = RasterShaderMapping.CreateSemanticImageShader(
            inputImage,
            inputTarget.RawValue.Context,
            input.Bounds,
            input.Scale.Value,
            input.DeviceBounds,
            input.RasterBounds,
            output.Scale.Value,
            output.RasterBounds,
            program.TileMode);
        children.Add(inputShader);
        runtimeChildren[program.ChildName] = inputShader;
        ShaderArgumentBinder.BindStandalone(description, context, uniforms, runtimeChildren, children);
    }

    /// <summary>Fills the output buffer with the bound program.</summary>
    private static void PaintShaderStage(
        SKRuntimeEffect effect,
        SKRuntimeEffectUniforms uniforms,
        SKRuntimeEffectChildren runtimeChildren,
        EffectTarget output,
        RenderTarget outputTarget,
        float maxWorkingScale,
        RenderIntent intent)
    {
        using SKShader shader = effect.ToShader(uniforms, runtimeChildren);
        using var paint = new SKPaint { Shader = shader };
        using var canvas = ImmediateCanvas.CreateExecutorManaged(
            outputTarget,
            output.Scale.Value,
            maxWorkingScale,
            output.RasterBounds.Size,
            intent);
        canvas.Clear();
        using (canvas.PushDeviceSpace())
        {
            canvas.Canvas.DrawRect(
                SKRect.Create(outputTarget.Width, outputTarget.Height),
                paint);
        }
    }

    private static EffectTarget? ExecuteGeometry(
        EffectTarget source,
        GeometryDescription description,
        FilterEffectExecutionSettings settings)
    {
        using EffectTarget? input = NormalizeInput(source, settings);
        if (input?.RenderTarget is not { } inputTarget)
            return null;

        Rect outputBounds = description.Bounds.TransformBounds(input.Bounds);
        if (IsEmpty(outputBounds))
            return null;

        float density = ResolveSupplyDensity(input, settings);
        EffectTarget? output = AllocateStageOutput(input, outputBounds, density, settings);
        if (output?.RenderTarget is not { } outputTarget)
        {
            output?.Dispose();
            return null;
        }

        try
        {
            using SurfaceSnapshot.Lease inputSnapshot = SurfaceSnapshot.Take(inputTarget.Value);
            SKImage inputImage = inputSnapshot.Image;
            Rect? selectedBounds = RenderGeometryStage(
                description,
                input,
                inputImage,
                inputTarget,
                output,
                outputTarget,
                outputBounds,
                settings);

            if (selectedBounds is not { Width: > 0, Height: > 0 } selected)
                return null;

            if (selected == outputBounds)
            {
                EffectTarget result = output;
                output = null;
                return result;
            }

            return CropTarget(output, selected, settings);
        }
        finally
        {
            output?.Dispose();
        }
    }

    /// <summary>
    /// Runs the description's render callback against the output buffer, and reports what it painted.
    /// </summary>
    /// <returns>
    /// The part of <paramref name="outputBounds"/> the callback selected, or <see langword="null"/> when it
    /// discarded its output altogether.
    /// </returns>
    private static Rect? RenderGeometryStage(
        GeometryDescription description,
        EffectTarget input,
        SKImage inputImage,
        RenderTarget inputTarget,
        EffectTarget output,
        RenderTarget outputTarget,
        Rect outputBounds,
        FilterEffectExecutionSettings settings)
    {
        var token = new RenderExecutionSessionToken();
        return token.RunAndComplete<Rect?>(
            () =>
            {
                Func<Bitmap>? createSnapshot = description.RequiresReadback
                    ? inputTarget.Snapshot
                    : null;
                var executionInput = new RenderExecutionInput(
                    token,
                    input.Bounds,
                    input.Scale,
                    input.DeviceBounds,
                    input.RasterBounds,
                    inputImage,
                    createSnapshot);
                var callbackCanvas = new RenderCallbackCanvas(
                    token,
                    output.Scale.Value,
                    outputBounds,
                    output.DeviceBounds,
                    () => ImmediateCanvas.CreateExecutorManaged(
                        outputTarget,
                        output.Scale.Value,
                        settings.MaxWorkingScale,
                        output.RasterBounds.Size,
                        settings.Intent,
                        output.DeviceBounds.Position),
                    CallbackCanvasCapability.Draw,
                    rasterBounds: output.RasterBounds);
                var session = new GeometrySession(
                    executionInput,
                    outputBounds,
                    settings.OutputScale,
                    settings.MaxWorkingScale,
                    settings.Intent,
                    settings.Purpose,
                    callbackCanvas,
                    description.Resources);
                description.Render(session);
                return session.IsOutputDiscarded
                    ? null
                    : session.OutputBounds.Intersect(outputBounds);
            });
    }

    private static EffectTarget? NormalizeInput(EffectTarget source, FilterEffectExecutionSettings settings)
    {
        if (source.RenderTarget is not { } sourceTarget)
            return null;

        float density = source.Scale.IsUnbounded ? settings.WorkingScale : source.Scale.Value;
        PixelRect semanticDeviceBounds = PixelRect.FromRect(
            source.Bounds.Translate(source.DeviceGridOffset),
            density);
        if (source.RasterBounds
                == source.DeviceBounds
                    .ToRect(density)
                    .Translate(-source.DeviceGridOffset)
            && source.DeviceBounds.Contains(semanticDeviceBounds))
        {
            return source.Clone();
        }

        EffectTarget? normalized = AllocateTarget(
            source.Bounds,
            density,
            settings,
            source.RasterBounds.Union(source.Bounds),
            source.DeviceGridOffset);
        if (normalized?.RenderTarget is not { } normalizedTarget)
        {
            normalized?.Dispose();
            return null;
        }

        try
        {
            Vector rasterTranslation = DeviceGridAlignment.ResolveRasterTranslation(
                normalized.DeviceBounds,
                normalized.DeviceGridOffset,
                normalized.Scale.Value);
            using var canvas = ImmediateCanvas.CreateExecutorManaged(
                normalizedTarget,
                normalized.Scale.Value,
                settings.MaxWorkingScale,
                normalized.RasterBounds.Size,
                settings.Intent);
            using (canvas.PushTransform(Matrix.CreateTranslation(
                       rasterTranslation.X,
                       rasterTranslation.Y)))
            {
                canvas.DrawRenderTargetScaledWithoutFlush(sourceTarget, source.RasterBounds);
            }

            normalized.OriginalBounds = source.Bounds;
            return normalized;
        }
        catch
        {
            normalized.Dispose();
            throw;
        }
    }

    private static EffectTarget? CropTarget(EffectTarget source, Rect selectedBounds, FilterEffectExecutionSettings settings)
    {
        if (source.RenderTarget is not { } sourceTarget)
            return null;

        EffectTarget? cropped = AllocateTarget(
            selectedBounds,
            source.Scale.Value,
            settings,
            deviceGridOffset: source.DeviceGridOffset);
        if (cropped?.RenderTarget is not { } croppedTarget)
        {
            cropped?.Dispose();
            return null;
        }

        try
        {
            Vector rasterTranslation = DeviceGridAlignment.ResolveRasterTranslation(
                cropped.DeviceBounds,
                cropped.DeviceGridOffset,
                cropped.Scale.Value);
            using var canvas = ImmediateCanvas.CreateExecutorManaged(
                croppedTarget,
                cropped.Scale.Value,
                settings.MaxWorkingScale,
                cropped.RasterBounds.Size,
                settings.Intent);
            using (canvas.PushTransform(Matrix.CreateTranslation(
                       rasterTranslation.X,
                       rasterTranslation.Y)))
            {
                canvas.ClipRect(selectedBounds);
                canvas.DrawRenderTargetScaledWithoutFlush(sourceTarget, source.RasterBounds);
            }

            return cropped;
        }
        catch
        {
            cropped.Dispose();
            throw;
        }
    }

    private static EffectTarget? AllocateTarget(
        Rect bounds,
        float density,
        FilterEffectExecutionSettings settings,
        Rect? physicalBounds = null,
        Vector deviceGridOffset = default,
        bool native = false)
    {
        if (IsEmpty(bounds))
            return null;

        PixelRect deviceBounds = FitAllocationToBudget(
            bounds,
            ref density,
            settings.ResolveBudget(),
            physicalBounds,
            deviceGridOffset);
        EffectTarget? result = EffectTargetAllocation.Allocate(
            settings.RenderTargetLeaseSession,
            bounds,
            density,
            deviceBounds,
            deviceGridOffset,
            preferLease: native,
            clearContents: !native);
        if (result is null)
        {
            ReportAllocationFailure(bounds, density, deviceBounds, settings.Intent, settings.RenderTargetLeaseSession);
            return null;
        }

        // Graph attachments and the output-copy pass initialize their own pixels. A deferred Skia
        // clear here could otherwise overwrite native output when another target flushes that surface.
        if (native)
            return result;

        try
        {
            using var canvas = ImmediateCanvas.CreateExecutorManaged(
                result.RenderTarget!,
                density,
                settings.MaxWorkingScale,
                result.RasterBounds.Size,
                settings.Intent,
                result.DeviceBounds.Position);
            canvas.Clear();
            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }

    // A physical footprint keeps the apron its local grid rounds, which can come out up to two pixels wider than the
    // footprint on the device grid that the clamp measures, so the clamp gives a pixel back until the buffer fits.
    private static PixelRect FitAllocationToBudget(
        Rect bounds,
        ref float density,
        BufferDimensionBudget budget,
        Rect? physicalBounds,
        Vector deviceGridOffset)
    {
        Rect footprint = (physicalBounds ?? bounds).Translate(deviceGridOffset);
        float requested = density;
        PixelRect deviceBounds = default;
        int lowest = Math.Max(budget.MaxDimension - 2, 1);
        for (int maxDimension = budget.MaxDimension; maxDimension >= lowest; maxDimension--)
        {
            density = BufferDimensionBudget.Named(maxDimension).ClampWorkingScaleToExactFootprint(footprint, requested);
            deviceBounds = ResolveAllocationDeviceBounds(bounds, density, deviceGridOffset, physicalBounds);
            if (budget.Fits(deviceBounds.Size))
                break;
        }

        return deviceBounds;
    }

    /// <summary>Places the buffer on the device grid, keeping the caller's apron around the semantic area.</summary>
    /// <remarks>
    /// Without a requested physical footprint the buffer is exactly the semantic rectangle. With one, the
    /// apron it carries on each side is measured in the caller's own local space and re-applied around the
    /// semantic device rectangle, because rounding the physical rectangle and the grid offset separately
    /// cannot reproduce the rounding the semantic rectangle already used.
    /// </remarks>
    private static PixelRect ResolveAllocationDeviceBounds(
        Rect bounds,
        float density,
        Vector deviceGridOffset,
        Rect? physicalBounds)
    {
        PixelRect semanticDeviceBounds = PixelRect.FromRect(
            bounds.Translate(deviceGridOffset),
            density);
        if (physicalBounds is not { } physical)
            return semanticDeviceBounds;

        PixelRect requestedPhysicalBounds = PixelRect.FromRect(physical, density);
        if (deviceGridOffset == default)
            return requestedPhysicalBounds;

        PixelRect localSemanticBounds = PixelRect.FromRect(bounds, density);
        int leftApron = localSemanticBounds.X - requestedPhysicalBounds.X;
        int topApron = localSemanticBounds.Y - requestedPhysicalBounds.Y;
        int rightApron = requestedPhysicalBounds.Right - localSemanticBounds.Right;
        int bottomApron = requestedPhysicalBounds.Bottom - localSemanticBounds.Bottom;
        return new PixelRect(
            semanticDeviceBounds.X - leftApron,
            semanticDeviceBounds.Y - topApron,
            semanticDeviceBounds.Width + leftApron + rightApron,
            semanticDeviceBounds.Height + topApron + bottomApron);
    }

    /// <summary>Reports a stage buffer the allocator would not give.</summary>
    private static void ReportAllocationFailure(
        Rect bounds,
        float density,
        PixelRect deviceBounds,
        RenderIntent intent,
        RenderTargetLeaseSession? leaseSession)
    {
        string message =
            $"Effect-item typed-effect target allocation failed ({deviceBounds.Width}x{deviceBounds.Height} px, "
            + $"w {density}, bounds {bounds}).";
        s_logger.LogWarning(
            "{Message} Preview drops this target; delivery render fails fast.",
            message);
        EffectTargetAllocation.FailDeliveryOrDrop(intent, leaseSession, message);
    }

    private static void ReplaceTargets(
        EffectTargets targets,
        Func<EffectTarget, EffectTarget?> execute)
    {
        using var replacements = new EffectTargets();
        foreach (EffectTarget target in targets)
        {
            EffectTarget? replacement = execute(target);
            if (replacement is not null)
                replacements.Add(replacement);
        }

        // Clear disposes the originals; the replacements move across alive.
        targets.Clear();
        targets.AddRange(replacements);
    }

    private static bool IsEmpty(Rect bounds)
        => bounds.Width == 0 || bounds.Height == 0;
}
