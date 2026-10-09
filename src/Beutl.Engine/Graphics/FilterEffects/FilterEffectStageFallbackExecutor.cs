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

    /// <summary>The render-wide inputs every stage of one fallback application shares.</summary>
    private readonly record struct StageEnvironment(
        float OutputScale,
        float WorkingScale,
        float MaxWorkingScale,
        RenderIntent Intent,
        RenderRequestPurpose Purpose,
        RenderTargetLeaseSession? LeaseSession,
        BufferDimensionBudget Budget);

    public static void ApplyShader(
        EffectTargets targets,
        ShaderDescription description,
        float outputScale,
        float workingScale,
        float maxWorkingScale,
        RenderIntent intent,
        RenderRequestPurpose purpose,
        SkRuntimeEffectProgramAcquirer acquireProgram,
        RenderTargetLeaseSession? leaseSession,
        BufferDimensionBudget? budget = null)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(description);
        BufferDimensionBudget.ThrowIfUninitialized(budget, nameof(budget));
        BufferDimensionBudget resolvedBudget = budget ?? BufferDimensionBudget.Resolve(BufferBudgetScope.Allocation);
        ArgumentNullException.ThrowIfNull(acquireProgram);
        var environment = new StageEnvironment(
            outputScale,
            workingScale,
            maxWorkingScale,
            intent,
            purpose,
            leaseSession,
            resolvedBudget);
        ReplaceTargets(
            targets,
            target => ExecuteShader(target, description, acquireProgram, environment));
    }

    public static void ApplyGeometry(
        EffectTargets targets,
        GeometryDescription description,
        float outputScale,
        float workingScale,
        float maxWorkingScale,
        RenderIntent intent,
        RenderRequestPurpose purpose,
        RenderTargetLeaseSession? leaseSession,
        BufferDimensionBudget? budget = null)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(description);
        BufferDimensionBudget.ThrowIfUninitialized(budget, nameof(budget));
        BufferDimensionBudget resolvedBudget = budget ?? BufferDimensionBudget.Resolve(BufferBudgetScope.Allocation);
        var environment = new StageEnvironment(
            outputScale,
            workingScale,
            maxWorkingScale,
            intent,
            purpose,
            leaseSession,
            resolvedBudget);
        ReplaceTargets(
            targets,
            target => ExecuteGeometry(target, description, environment));
    }

    private static EffectTarget? ExecuteShader(
        EffectTarget source,
        ShaderDescription description,
        SkRuntimeEffectProgramAcquirer acquireProgram,
        StageEnvironment environment)
    {
        using EffectTarget? input = NormalizeInput(source, environment);
        if (input?.RenderTarget is not { } inputTarget)
            return null;

        Rect outputBounds = description.Bounds.TransformBounds(input.Bounds);
        if (IsEmpty(outputBounds))
            return null;

        // A current-pixel stage rewrites the colour of the sample it was handed, so its supply is the
        // input's own density; a whole-source stage resolves one from the supply the way any stage does.
        float density = description.Kind == ShaderDescriptionKind.CurrentPixel
            ? input.Scale.Value
            : ResolveSupplyDensity(input, environment);
        EffectTarget? output = AllocateStageOutput(input, outputBounds, density, environment);
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
                environment);

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
        StageEnvironment environment)
    {
        density = environment.Budget.ClampWorkingScaleToExactFootprint(
            outputBounds.Translate(input.DeviceGridOffset),
            density);
        return AllocateTarget(
            outputBounds,
            density,
            environment,
            deviceGridOffset: input.DeviceGridOffset);
    }

    /// <summary>Resolves the density a stage's output is rasterized at from the supply of its input.</summary>
    private static float ResolveSupplyDensity(EffectTarget input, StageEnvironment environment)
        => RenderScaleUtilities.ResolveWorkingScale(
            [input.Scale],
            environment.OutputScale,
            environment.MaxWorkingScale);

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
        StageEnvironment environment)
    {
        ShaderProgram program = ResolveShaderProgram(description);
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
                environment);
            PaintShaderStage(
                lease.Program.Effect,
                uniforms,
                runtimeChildren,
                output,
                outputTarget,
                environment.MaxWorkingScale,
                environment.Intent);
        }
        finally
        {
            // Children are released in the reverse of the order they were created, which is what a
            // 'using' stack over them would have done had the count been known at compile time.
            for (int index = children.Count - 1; index >= 0; index--)
                children[index].Dispose();
        }
    }

    /// <summary>The program text a stage runs, and the name its upstream input is bound under.</summary>
    private readonly record struct ShaderProgram(string ChildName, string Source, SKShaderTileMode TileMode);

    /// <summary>
    /// Resolves the SkSL a stage runs from its description.
    /// </summary>
    /// <remarks>
    /// A current-pixel description declares only an <c>apply</c> over a colour, so the entry point that feeds
    /// it the fragment's own sample is written here rather than by the author; a whole-source description
    /// already carries its own entry point and is run as given.
    /// </remarks>
    private static ShaderProgram ResolveShaderProgram(ShaderDescription description)
    {
        if (description.Kind != ShaderDescriptionKind.CurrentPixel)
            return new ShaderProgram("src", description.Source.Text, description.SourceTileMode);

        return new ShaderProgram(
            SkslSource.CurrentPixelInputName,
            SkslSource.CreateStandaloneCurrentPixelProgram(description.Source.Text),
            SKShaderTileMode.Decal);
    }

    /// <summary>Runs the description's binders and hands their results to the runtime effect.</summary>
    private static void BindShaderStage(
        ShaderDescription description,
        ShaderProgram program,
        EffectTarget input,
        SKImage inputImage,
        RenderTarget inputTarget,
        EffectTarget output,
        Rect outputBounds,
        SKRuntimeEffectUniforms uniforms,
        SKRuntimeEffectChildren runtimeChildren,
        List<SKShader> children,
        StageEnvironment environment)
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
            environment);
    }

    /// <remarks>
    /// One execution session covers the whole phase: every binder is handed the same context, and the token
    /// is completed once so none of them can retain what it was given past the phase.
    /// </remarks>
    private static void BindShaderStageWithCallbacks(
        ShaderDescription description,
        ShaderProgram program,
        EffectTarget input,
        SKImage inputImage,
        RenderTarget inputTarget,
        EffectTarget output,
        Rect outputBounds,
        SKRuntimeEffectUniforms uniforms,
        SKRuntimeEffectChildren runtimeChildren,
        List<SKShader> children,
        StageEnvironment environment)
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
                    environment.OutputScale,
                    output.Scale.Value,
                    environment.MaxWorkingScale,
                    environment.Intent,
                    environment.Purpose);
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
        ShaderProgram program,
        EffectTarget input,
        SKImage inputImage,
        RenderTarget inputTarget,
        EffectTarget output,
        SKRuntimeEffectUniforms uniforms,
        SKRuntimeEffectChildren runtimeChildren,
        List<SKShader> children,
        ShaderExecutionContext? context)
    {
        for (int index = 0; index < description.Uniforms.Count; index++)
        {
            ShaderUniformBinding binding = description.Uniforms[index];
            if (!description.Source.Uniforms.TryGetValue(
                    binding.Name,
                    out SkslUniformDeclaration declaration))
            {
                throw new InvalidOperationException(
                    $"Shader uniform '{binding.Name}' was not declared.");
            }

            SkslUniformAssignment.SetUniform(
                uniforms,
                binding.Name,
                declaration,
                binding.Bind(declaration, context));
        }

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

        if (description.Resources.Count == 0)
            return;

        ShaderExecutionContext resourceContext = context
            ?? throw new InvalidOperationException("A shader resource binding requires an execution context.");
        for (int index = 0; index < description.Resources.Count; index++)
        {
            ShaderResourceBinding binding = description.Resources[index];
            SKShader child = binding.Bind(resourceContext);
            children.Add(child);
            runtimeChildren[binding.Name] = child;
        }
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
        StageEnvironment environment)
    {
        using EffectTarget? input = NormalizeInput(source, environment);
        if (input?.RenderTarget is not { } inputTarget)
            return null;

        Rect outputBounds = description.Bounds.TransformBounds(input.Bounds);
        if (IsEmpty(outputBounds))
            return null;

        float density = ResolveSupplyDensity(input, environment);
        EffectTarget? output = AllocateStageOutput(input, outputBounds, density, environment);
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
                environment);

            if (selectedBounds is not { Width: > 0, Height: > 0 } selected)
                return null;

            if (selected == outputBounds)
            {
                EffectTarget result = output;
                output = null;
                return result;
            }

            return CropTarget(output, selected, environment);
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
        StageEnvironment environment)
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
                        environment.MaxWorkingScale,
                        output.RasterBounds.Size,
                        environment.Intent,
                        output.DeviceBounds.Position),
                    CallbackCanvasCapability.Draw,
                    rasterBounds: output.RasterBounds);
                var session = new GeometrySession(
                    executionInput,
                    outputBounds,
                    environment.OutputScale,
                    environment.MaxWorkingScale,
                    environment.Intent,
                    environment.Purpose,
                    callbackCanvas,
                    description.Resources);
                description.Render(session);
                return session.IsOutputDiscarded
                    ? null
                    : session.OutputBounds.Intersect(outputBounds);
            });
    }

    private static EffectTarget? NormalizeInput(EffectTarget source, StageEnvironment environment)
    {
        if (source.RenderTarget is not { } sourceTarget)
            return null;

        float density = source.Scale.IsUnbounded ? environment.WorkingScale : source.Scale.Value;
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

        Rect physicalBounds = source.RasterBounds.Union(source.Bounds);
        density = environment.Budget.ClampWorkingScaleToExactFootprint(
            physicalBounds.Translate(source.DeviceGridOffset),
            density);
        PixelRect physicalDeviceBounds = PixelRect.FromRect(physicalBounds, density);
        EffectTarget? normalized = AllocateTarget(
            source.Bounds,
            density,
            environment,
            physicalDeviceBounds,
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
                environment.MaxWorkingScale,
                normalized.RasterBounds.Size,
                environment.Intent);
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

    private static EffectTarget? CropTarget(EffectTarget source, Rect selectedBounds, StageEnvironment environment)
    {
        if (source.RenderTarget is not { } sourceTarget)
            return null;

        EffectTarget? cropped = AllocateTarget(
            selectedBounds,
            source.Scale.Value,
            environment,
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
                environment.MaxWorkingScale,
                cropped.RasterBounds.Size,
                environment.Intent);
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
        StageEnvironment environment,
        PixelRect? physicalDeviceBounds = null,
        Vector deviceGridOffset = default)
    {
        if (IsEmpty(bounds))
            return null;

        if (physicalDeviceBounds is null)
        {
            density = environment.Budget.ClampWorkingScaleToExactFootprint(
                bounds.Translate(deviceGridOffset),
                density);
        }

        PixelRect deviceBounds = ResolveAllocationDeviceBounds(
            bounds,
            density,
            deviceGridOffset,
            physicalDeviceBounds);
        EffectTarget? result = EffectTargetAllocation.Allocate(
            environment.LeaseSession,
            bounds,
            density,
            deviceBounds,
            deviceGridOffset);
        if (result is null)
        {
            ReportAllocationFailure(bounds, density, deviceBounds, environment.Intent, environment.LeaseSession);
            return null;
        }

        try
        {
            using var canvas = ImmediateCanvas.CreateExecutorManaged(
                result.RenderTarget!,
                density,
                environment.MaxWorkingScale,
                result.RasterBounds.Size,
                environment.Intent,
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
        PixelRect? physicalDeviceBounds)
    {
        PixelRect semanticDeviceBounds = PixelRect.FromRect(
            bounds.Translate(deviceGridOffset),
            density);
        if (physicalDeviceBounds is not { } requestedPhysicalBounds)
            return semanticDeviceBounds;
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
            $"EffectItem typed-effect target allocation failed ({deviceBounds.Width}x{deviceBounds.Height} px, "
            + $"w {density}, bounds {bounds}).";
        s_logger.LogWarning(
            "{Message} Preview drops this target; delivery render fails fast.",
            message);
        if (intent == RenderIntent.Delivery)
            throw new InvalidOperationException(message);
        leaseSession?.MarkContentDropped();
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
        while (replacements.Count > 0)
            targets.Add(replacements.DetachAt(0));
    }

    private static bool IsEmpty(Rect bounds)
        => bounds.Width == 0 || bounds.Height == 0;
}
