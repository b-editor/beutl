using Beutl.Graphics.Rendering;
using Beutl.Logging;
using Beutl.Media;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

internal static class LegacyFilterEffectCompatibilityExecutor
{
    private static readonly ILogger s_logger = Log.CreateLogger("LegacyFilterEffectCompatibilityExecutor");

    public static void ApplyShader(
        EffectTargets targets,
        ShaderDescription description,
        float outputScale,
        float workingScale,
        float maxWorkingScale,
        RenderIntent intent,
        RenderRequestPurpose purpose)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(description);
        ReplaceTargets(
            targets,
            target => ExecuteShader(
                target,
                description,
                outputScale,
                workingScale,
                maxWorkingScale,
                intent,
                purpose));
    }

    public static void ApplyGeometry(
        EffectTargets targets,
        GeometryDescription description,
        float outputScale,
        float workingScale,
        float maxWorkingScale,
        RenderIntent intent,
        RenderRequestPurpose purpose)
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
                purpose));
    }

    private static EffectTarget? ExecuteShader(
        EffectTarget source,
        ShaderDescription description,
        float outputScale,
        float workingScale,
        float maxWorkingScale,
        RenderIntent intent,
        RenderRequestPurpose purpose)
    {
        using EffectTarget? input = NormalizeInput(
            source,
            workingScale,
            maxWorkingScale,
            intent);
        if (input?.RenderTarget is not { } inputTarget)
            return null;

        Rect outputBounds = description.Bounds.TransformBounds(input.Bounds);
        if (IsEmpty(outputBounds))
            return null;

        float density = description.Kind == ShaderDescriptionKind.CurrentPixel
            ? input.Scale.Value
            : RenderScaleUtilities.ResolveWorkingScale(
                [input.Scale],
                outputScale,
                maxWorkingScale);
        density = RenderScaleUtilities.ClampWorkingScaleToExactBufferBudget(outputBounds, density);
        EffectTarget? output = AllocateTarget(outputBounds, density, maxWorkingScale, intent);
        if (output?.RenderTarget is not { } outputTarget)
        {
            output?.Dispose();
            return null;
        }

        try
        {
            using SKImage inputImage = inputTarget.Value.Snapshot();
            string childName;
            string programSource;
            SKShaderTileMode tileMode;
            if (description.Kind == ShaderDescriptionKind.CurrentPixel)
            {
                childName = "__beutl_src";
                tileMode = SKShaderTileMode.Decal;
                programSource = $"uniform shader {childName};\n{description.Source.Text}\n"
                    + $"half4 main(float2 __beutl_coord) {{ return apply({childName}.eval(__beutl_coord)); }}\n";
            }
            else
            {
                childName = "src";
                tileMode = description.SourceTileMode;
                programSource = description.Source.Text;
            }

            using SKRuntimeEffect effect = CreateRuntimeEffect(programSource);
            using var builder = new SKRuntimeShaderBuilder(effect);
            var bindingToken = new RenderExecutionSessionToken();
            var context = new ShaderExecutionContext(
                bindingToken,
                input.Bounds,
                outputBounds,
                outputBounds,
                output.DeviceBounds,
                input.Scale,
                outputScale,
                output.Scale.Value,
                maxWorkingScale,
                intent,
                purpose);
            var children = new List<SKShader>();
            try
            {
                try
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

                        SetUniform(builder.Uniforms, binding.Name, declaration, binding.Bind(declaration, context));
                    }

                    SKShader inputShader = inputImage.ToShader(
                        tileMode,
                        tileMode,
                        SKSamplingOptions.Default,
                        RasterShaderMapping.CreateLocalMatrix(
                            output.Scale.Value,
                            input.Scale.Value,
                            output.RasterBounds,
                            input.RasterBounds));
                    children.Add(inputShader);
                    builder.Children[childName] = inputShader;

                    foreach (ShaderResourceBinding binding in description.Resources)
                    {
                        SKShader child = binding.Bind(context);
                        children.Add(child);
                        builder.Children[binding.Name] = child;
                    }
                }
                finally
                {
                    bindingToken.Complete();
                }

                using SKShader shader = builder.Build();
                using var paint = new SKPaint { Shader = shader };
                using var canvas = ImmediateCanvas.CreateExecutorManaged(
                    outputTarget,
                    output.Scale.Value,
                    maxWorkingScale,
                    output.RasterBounds.Size);
                canvas.Clear();
                using (canvas.PushDeviceSpace())
                {
                    canvas.Canvas.DrawRect(
                        SKRect.Create(outputTarget.Width, outputTarget.Height),
                        paint);
                }
            }
            finally
            {
                foreach (SKShader child in children.AsEnumerable().Reverse())
                    child.Dispose();
            }

            EffectTarget result = output;
            output = null;
            return result;
        }
        finally
        {
            output?.Dispose();
        }
    }

    private static EffectTarget? ExecuteGeometry(
        EffectTarget source,
        GeometryDescription description,
        float outputScale,
        float workingScale,
        float maxWorkingScale,
        RenderIntent intent,
        RenderRequestPurpose purpose)
    {
        using EffectTarget? input = NormalizeInput(
            source,
            workingScale,
            maxWorkingScale,
            intent);
        if (input?.RenderTarget is not { } inputTarget)
            return null;

        Rect outputBounds = description.Bounds.TransformBounds(input.Bounds);
        if (IsEmpty(outputBounds))
            return null;

        float density = RenderScaleUtilities.ResolveWorkingScale(
            [input.Scale],
            outputScale,
            maxWorkingScale);
        density = RenderScaleUtilities.ClampWorkingScaleToExactBufferBudget(outputBounds, density);
        EffectTarget? output = AllocateTarget(outputBounds, density, maxWorkingScale, intent);
        if (output?.RenderTarget is not { } outputTarget)
        {
            output?.Dispose();
            return null;
        }

        try
        {
            var token = new RenderExecutionSessionToken();
            using SKImage inputImage = inputTarget.Value.Snapshot();
            Rect? selectedBounds;
            try
            {
                Func<Bitmap>? createSnapshot = description.RequiresReadback
                    ? inputTarget.Snapshot
                    : null;
                var executionInput = new RenderExecutionInput(
                    token,
                    input.Bounds,
                    input.Scale,
                    input.DeviceBounds,
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
                        output.RasterBounds.Size),
                    CallbackCanvasCapability.Draw);
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
                selectedBounds = session.IsOutputDiscarded
                    ? null
                    : session.OutputBounds.Intersect(outputBounds);
            }
            finally
            {
                token.Complete();
            }

            if (selectedBounds is not { Width: > 0, Height: > 0 } selected)
                return null;

            if (selected == outputBounds)
            {
                EffectTarget result = output;
                output = null;
                return result;
            }

            return CropTarget(output, selected, maxWorkingScale, intent);
        }
        finally
        {
            output?.Dispose();
        }
    }

    private static EffectTarget? NormalizeInput(
        EffectTarget source,
        float workingScale,
        float maxWorkingScale,
        RenderIntent intent)
    {
        if (source.RenderTarget is not { } sourceTarget)
            return null;

        float density = source.Scale.IsUnbounded ? workingScale : source.Scale.Value;
        PixelRect semanticDeviceBounds = PixelRect.FromRect(source.Bounds, density);
        if (source.RasterBounds == source.DeviceBounds.ToRect(density)
            && Contains(source.DeviceBounds, semanticDeviceBounds))
        {
            return source.Clone();
        }

        Rect physicalBounds = source.RasterBounds.Union(source.Bounds);
        density = RenderScaleUtilities.ClampWorkingScaleToExactBufferBudget(physicalBounds, density);
        PixelRect physicalDeviceBounds = PixelRect.FromRect(physicalBounds, density);
        EffectTarget? normalized = AllocateTarget(
            source.Bounds,
            density,
            maxWorkingScale,
            intent,
            physicalDeviceBounds);
        if (normalized?.RenderTarget is not { } normalizedTarget)
        {
            normalized?.Dispose();
            return null;
        }

        try
        {
            using var canvas = ImmediateCanvas.CreateExecutorManaged(
                normalizedTarget,
                normalized.Scale.Value,
                maxWorkingScale,
                normalized.RasterBounds.Size);
            using (canvas.PushTransform(Matrix.CreateTranslation(
                       -normalized.RasterBounds.X,
                       -normalized.RasterBounds.Y)))
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
        RenderIntent intent)
    {
        if (source.RenderTarget is not { } sourceTarget)
            return null;

        EffectTarget? cropped = AllocateTarget(
            selectedBounds,
            source.Scale.Value,
            maxWorkingScale,
            intent);
        if (cropped?.RenderTarget is not { } croppedTarget)
        {
            cropped?.Dispose();
            return null;
        }

        try
        {
            using var canvas = ImmediateCanvas.CreateExecutorManaged(
                croppedTarget,
                cropped.Scale.Value,
                maxWorkingScale,
                cropped.RasterBounds.Size);
            using (canvas.PushTransform(Matrix.CreateTranslation(
                       -cropped.RasterBounds.X,
                       -cropped.RasterBounds.Y)))
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
        PixelRect? physicalDeviceBounds = null)
    {
        if (IsEmpty(bounds))
            return null;

        if (physicalDeviceBounds is null)
            density = RenderScaleUtilities.ClampWorkingScaleToExactBufferBudget(bounds, density);
        PixelRect deviceBounds = physicalDeviceBounds ?? PixelRect.FromRect(bounds, density);
        using RenderTarget? renderTarget = RenderTarget.Create(deviceBounds.Width, deviceBounds.Height);
        if (renderTarget is null)
        {
            string message =
                $"Legacy typed-effect target allocation failed ({deviceBounds.Width}x{deviceBounds.Height} px, "
                + $"w {density}, bounds {bounds}).";
            s_logger.LogWarning(
                "{Message} Preview drops this target; delivery render fails fast.",
                message);
            if (intent == RenderIntent.Delivery)
                throw new InvalidOperationException(message);
            return null;
        }

        var result = new EffectTarget(
            renderTarget,
            bounds,
            EffectiveScale.At(density),
            deviceBounds);
        try
        {
            using var canvas = ImmediateCanvas.CreateExecutorManaged(
                result.RenderTarget!,
                density,
                maxWorkingScale,
                result.RasterBounds.Size);
            canvas.Clear();
            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
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

    private static SKRuntimeEffect CreateRuntimeEffect(string source)
    {
        SKRuntimeEffect? effect = SKRuntimeEffect.CreateShader(source, out string? errorText);
        if (effect is not null && string.IsNullOrWhiteSpace(errorText))
            return effect;

        effect?.Dispose();
        throw new InvalidOperationException(
            $"SkSL program validation failed: {errorText ?? "the backend returned no program"}");
    }

    private static void SetUniform(
        SKRuntimeEffectUniforms uniforms,
        string name,
        SkslUniformDeclaration declaration,
        ShaderUniformValue value)
    {
        if (value.IsInteger)
        {
            uniforms[name] = declaration.ArrayExtent is null
                && declaration.Type is "int" or "bool"
                    ? value.Integers![0]
                    : value.Integers!;
        }
        else
        {
            uniforms[name] = declaration.ArrayExtent is null
                && declaration.Type is "float" or "half"
                    ? value.Floats![0]
                    : value.Floats!;
        }
    }

    private static bool Contains(PixelRect outer, PixelRect inner)
        => outer.X <= inner.X
           && outer.Y <= inner.Y
           && outer.Right >= inner.Right
           && outer.Bottom >= inner.Bottom;

    private static bool IsEmpty(Rect bounds)
        => bounds.Width == 0 || bounds.Height == 0;
}
