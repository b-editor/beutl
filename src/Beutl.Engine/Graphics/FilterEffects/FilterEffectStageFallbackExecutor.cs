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
        float outputScale,
        float workingScale,
        float maxWorkingScale,
        RenderIntent intent,
        RenderRequestPurpose purpose,
        SkRuntimeEffectProgramAcquirer acquireProgram,
        RenderTargetLeaseSession? leaseSession)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(acquireProgram);
        ReplaceTargets(
            targets,
            target => ExecuteShader(
                target,
                description,
                outputScale,
                workingScale,
                maxWorkingScale,
                intent,
                purpose,
                acquireProgram,
                leaseSession));
    }

    public static void ApplyGeometry(
        EffectTargets targets,
        GeometryDescription description,
        float outputScale,
        float workingScale,
        float maxWorkingScale,
        RenderIntent intent,
        RenderRequestPurpose purpose,
        RenderTargetLeaseSession? leaseSession)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(description);
        ReplaceTargets(
            targets,
            target => ExecuteGeometry(
                target,
                description,
                outputScale,
                workingScale,
                maxWorkingScale,
                intent,
                purpose,
                leaseSession));
    }

    private static EffectTarget? ExecuteShader(
        EffectTarget source,
        ShaderDescription description,
        float outputScale,
        float workingScale,
        float maxWorkingScale,
        RenderIntent intent,
        RenderRequestPurpose purpose,
        SkRuntimeEffectProgramAcquirer acquireProgram,
        RenderTargetLeaseSession? leaseSession)
    {
        using EffectTarget? input = NormalizeInput(
            source,
            workingScale,
            maxWorkingScale,
            intent,
            leaseSession);
        if (input?.RenderTarget is not { } inputTarget)
            return null;

        Rect outputBounds = description.Bounds.TransformBounds(input.Bounds);
        if (IsEmpty(outputBounds))
            return null;

        // A current-pixel stage rewrites the colour of the sample it was handed, so its supply is the
        // input's own density; a whole-source stage resolves one from the supply the way any stage does.
        float density = description.Kind == ShaderDescriptionKind.CurrentPixel
            ? input.Scale.Value
            : RenderScaleUtilities.ResolveWorkingScale(
                [input.Scale],
                outputScale,
                maxWorkingScale);
        EffectTarget? output = AllocateStageOutput(
            input,
            outputBounds,
            density,
            maxWorkingScale,
            intent,
            leaseSession);
        if (output?.RenderTarget is not { } outputTarget)
        {
            output?.Dispose();
            return null;
        }

        try
        {
            using SKImage inputImage = inputTarget.Value.Snapshot();
            RunShaderStage(
                description,
                input,
                inputImage,
                inputTarget,
                output,
                outputTarget,
                outputBounds,
                outputScale,
                maxWorkingScale,
                intent,
                purpose,
                acquireProgram);

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
        float maxWorkingScale,
        RenderIntent intent,
        RenderTargetLeaseSession? leaseSession)
    {
        density = RenderScaleUtilities.ClampWorkingScaleToExactDeviceBufferBudget(
            outputBounds.Translate(input.DeviceGridOffset),
            density);
        return AllocateTarget(
            outputBounds,
            density,
            maxWorkingScale,
            intent,
            leaseSession,
            deviceGridOffset: input.DeviceGridOffset);
    }

    /// <summary>Compiles the stage's program, binds its inputs, and fills the output buffer with it.</summary>
    private static void RunShaderStage(
        ShaderDescription description,
        EffectTarget input,
        SKImage inputImage,
        RenderTarget inputTarget,
        EffectTarget output,
        RenderTarget outputTarget,
        Rect outputBounds,
        float outputScale,
        float maxWorkingScale,
        RenderIntent intent,
        RenderRequestPurpose purpose,
        SkRuntimeEffectProgramAcquirer acquireProgram)
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
                outputScale,
                maxWorkingScale,
                intent,
                purpose,
                uniforms,
                runtimeChildren,
                children);
            PaintShaderStage(
                lease.Program.Effect,
                uniforms,
                runtimeChildren,
                output,
                outputTarget,
                maxWorkingScale,
                intent);
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

        const string childName = "__beutl_src";
        return new ShaderProgram(
            childName,
            $"uniform shader {childName};\n{description.Source.Text}\n"
            + $"half4 main(float2 __beutl_coord) {{ return apply({childName}.eval(__beutl_coord)); }}\n",
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
        float outputScale,
        float maxWorkingScale,
        RenderIntent intent,
        RenderRequestPurpose purpose,
        SKRuntimeEffectUniforms uniforms,
        SKRuntimeEffectChildren runtimeChildren,
        List<SKShader> children)
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
            outputScale,
            maxWorkingScale,
            intent,
            purpose,
            uniforms,
            runtimeChildren,
            children);
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
        float outputScale,
        float maxWorkingScale,
        RenderIntent intent,
        RenderRequestPurpose purpose,
        SKRuntimeEffectUniforms uniforms,
        SKRuntimeEffectChildren runtimeChildren,
        List<SKShader> children)
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
                    output.RasterBounds,
                    input.Scale,
                    outputScale,
                    output.Scale.Value,
                    maxWorkingScale,
                    intent,
                    purpose);
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
        foreach (ShaderUniformBinding binding in description.Uniforms)
        {
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
        foreach (ShaderResourceBinding binding in description.Resources)
        {
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
        float outputScale,
        float workingScale,
        float maxWorkingScale,
        RenderIntent intent,
        RenderRequestPurpose purpose,
        RenderTargetLeaseSession? leaseSession)
    {
        using EffectTarget? input = NormalizeInput(
            source,
            workingScale,
            maxWorkingScale,
            intent,
            leaseSession);
        if (input?.RenderTarget is not { } inputTarget)
            return null;

        Rect outputBounds = description.Bounds.TransformBounds(input.Bounds);
        if (IsEmpty(outputBounds))
            return null;

        float density = RenderScaleUtilities.ResolveWorkingScale(
            [input.Scale],
            outputScale,
            maxWorkingScale);
        EffectTarget? output = AllocateStageOutput(
            input,
            outputBounds,
            density,
            maxWorkingScale,
            intent,
            leaseSession);
        if (output?.RenderTarget is not { } outputTarget)
        {
            output?.Dispose();
            return null;
        }

        try
        {
            using SKImage inputImage = inputTarget.Value.Snapshot();
            Rect? selectedBounds = RenderGeometryStage(
                description,
                input,
                inputImage,
                inputTarget,
                output,
                outputTarget,
                outputBounds,
                outputScale,
                maxWorkingScale,
                intent,
                purpose);

            if (selectedBounds is not { Width: > 0, Height: > 0 } selected)
                return null;

            if (selected == outputBounds)
            {
                EffectTarget result = output;
                output = null;
                return result;
            }

            return CropTarget(output, selected, maxWorkingScale, intent, leaseSession);
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
        float outputScale,
        float maxWorkingScale,
        RenderIntent intent,
        RenderRequestPurpose purpose)
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
                    createSnapshot,
                    description.RequiresReadback);
                var callbackCanvas = new RenderCallbackCanvas(
                    token,
                    output.Scale.Value,
                    outputBounds,
                    output.DeviceBounds,
                    () => ImmediateCanvas.CreateExecutorManaged(
                        outputTarget,
                        output.Scale.Value,
                        maxWorkingScale,
                        output.RasterBounds.Size,
                        intent,
                        output.DeviceBounds.Position),
                    CallbackCanvasCapability.Draw,
                    rasterBounds: output.RasterBounds);
                var session = new GeometrySession(
                    token,
                    executionInput,
                    outputBounds,
                    outputBounds,
                    output.DeviceBounds,
                    outputScale,
                    output.Scale.Value,
                    maxWorkingScale,
                    intent,
                    purpose,
                    callbackCanvas,
                    description.Resources);
                description.Render(session);
                return session.IsOutputDiscarded
                    ? null
                    : session.OutputBounds.Intersect(outputBounds);
            });
    }

    private static EffectTarget? NormalizeInput(
        EffectTarget source,
        float workingScale,
        float maxWorkingScale,
        RenderIntent intent,
        RenderTargetLeaseSession? leaseSession)
    {
        if (source.RenderTarget is not { } sourceTarget)
            return null;

        float density = source.Scale.IsUnbounded ? workingScale : source.Scale.Value;
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
        density = RenderScaleUtilities.ClampWorkingScaleToExactDeviceBufferBudget(
            physicalBounds.Translate(source.DeviceGridOffset),
            density);
        PixelRect physicalDeviceBounds = PixelRect.FromRect(physicalBounds, density);
        EffectTarget? normalized = AllocateTarget(
            source.Bounds,
            density,
            maxWorkingScale,
            intent,
            leaseSession,
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
                maxWorkingScale,
                normalized.RasterBounds.Size,
                intent);
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

    private static EffectTarget? CropTarget(
        EffectTarget source,
        Rect selectedBounds,
        float maxWorkingScale,
        RenderIntent intent,
        RenderTargetLeaseSession? leaseSession)
    {
        if (source.RenderTarget is not { } sourceTarget)
            return null;

        EffectTarget? cropped = AllocateTarget(
            selectedBounds,
            source.Scale.Value,
            maxWorkingScale,
            intent,
            leaseSession,
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
                maxWorkingScale,
                cropped.RasterBounds.Size,
                intent);
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
        float maxWorkingScale,
        RenderIntent intent,
        RenderTargetLeaseSession? leaseSession,
        PixelRect? physicalDeviceBounds = null,
        Vector deviceGridOffset = default)
    {
        if (IsEmpty(bounds))
            return null;

        if (physicalDeviceBounds is null)
        {
            density = RenderScaleUtilities.ClampWorkingScaleToExactDeviceBufferBudget(
                bounds.Translate(deviceGridOffset),
                density);
        }

        PixelRect deviceBounds = ResolveAllocationDeviceBounds(
            bounds,
            density,
            deviceGridOffset,
            physicalDeviceBounds);
        EffectTarget? result = EffectTargetAllocation.Allocate(
            leaseSession,
            bounds,
            density,
            deviceBounds,
            deviceGridOffset);
        if (result is null)
        {
            ReportAllocationFailure(bounds, density, deviceBounds, intent, leaseSession);
            return null;
        }

        try
        {
            using var canvas = ImmediateCanvas.CreateExecutorManaged(
                result.RenderTarget!,
                density,
                maxWorkingScale,
                result.RasterBounds.Size,
                intent,
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

        foreach (EffectTarget target in targets)
            target.Dispose();
        targets.Clear();
        while (replacements.Count > 0)
        {
            EffectTarget replacement = replacements[0];
            replacements.RemoveAt(0);
            targets.Add(replacement);
        }
    }

    private static bool IsEmpty(Rect bounds)
        => bounds.Width == 0 || bounds.Height == 0;
}
