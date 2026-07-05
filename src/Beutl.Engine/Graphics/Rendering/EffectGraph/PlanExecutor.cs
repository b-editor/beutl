using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Beutl.Graphics.Effects;
using SkiaSharp;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// Runs a <see cref="CompiledPlan"/> against the graphics context (feature 004, T023, D2/D5). A lone
/// <see cref="OpaqueLegacyPass"/> is delegated to <see cref="LegacyBridgeExecutor"/> over all inputs at once, so
/// bridged content stays byte-identical and its counters are unchanged. A descriptor plan runs each input through
/// the pass chain independently: a <see cref="FusedShaderPass"/> executes as one draw built by shader composition
/// (input image shader → <c>WithColorFilter</c> wraps → nested <c>SKRuntimeEffect</c> child shaders, adjacent
/// snippets merged into one program), a <see cref="SkiaFilterPass"/> as one filtered draw. Intermediates are
/// acquired from and released to the pool per pass; the RGBA16F premultiplied linear-light representation is
/// preserved between stages. Counters are emitted per §C8: one <see cref="PipelineDiagnostics.GpuPasses"/> per
/// executed pass, one <see cref="PipelineDiagnostics.ProgramCreations"/> per <c>SKRuntimeEffect</c> created.
/// </summary>
internal static class PlanExecutor
{
    public static RenderNodeOperation[] Execute(
        CompiledPlan plan,
        FrameResources resources,
        RenderNodeOperation[] inputs,
        Rect bounds,
        float outputScale,
        float workingScale,
        float maxWorkingScale,
        PipelineDiagnostics? diagnostics,
        RenderTargetPool? pool)
    {
        if (plan.Passes is [OpaqueLegacyPass opaque])
        {
            return LegacyBridgeExecutor.Execute(
                opaque.Context, inputs, bounds, outputScale, workingScale, maxWorkingScale, diagnostics, pool);
        }

        var results = new List<RenderNodeOperation>(inputs.Length);
        for (int i = 0; i < inputs.Length; i++)
        {
            try
            {
                results.Add(ExecuteChain(plan, resources, inputs[i], maxWorkingScale, diagnostics, pool));
            }
            catch
            {
                RenderNodeOperation.DisposeAll(CollectionsMarshal.AsSpan(results));
                RenderNodeOperation.DisposeAll(inputs.AsSpan(i + 1));
                throw;
            }
        }

        return results.ToArray();
    }

    private static RenderNodeOperation ExecuteChain(
        CompiledPlan plan, FrameResources resources, RenderNodeOperation input,
        float maxWorkingScale, PipelineDiagnostics? diagnostics, RenderTargetPool? pool)
    {
        RenderNodeOperation current = input;
        try
        {
            for (int k = 0; k < plan.Passes.Length; k++)
            {
                CompiledPass pass = plan.Passes[k];
                PassResolution pr = resources.Passes[k];
                if (pr.SkipEmpty)
                    continue;

                Rect outBounds = pass.OutputBounds.IsInvalid ? current.Bounds : pass.OutputBounds;
                RenderTarget target = RenderTargetPool.Acquire(pool, pr.Width, pr.Height, diagnostics)
                    ?? throw new InvalidOperationException(
                        $"Effect pass buffer allocation failed ({pr.Width}x{pr.Height} px, w {pr.WorkingScale}, bounds {outBounds}).");

                try
                {
                    switch (pass)
                    {
                        case FusedShaderPass fused:
                            ExecuteFused(fused, target, pr.WorkingScale, outBounds, current, maxWorkingScale, diagnostics);
                            break;
                        case SkiaFilterPass skia:
                            ExecuteSkia(skia, target, pr.WorkingScale, outBounds, current, maxWorkingScale);
                            break;
                        default:
                            throw new NotSupportedException(
                                $"Pass '{pass.GetType().Name}' is not executable by the step-3a descriptor path.");
                    }
                }
                catch
                {
                    target.Dispose();
                    throw;
                }

                if (diagnostics != null)
                    diagnostics.GpuPasses++;

                current.Dispose();
                current = RenderNodeOperation.CreateFromRenderTarget(
                    outBounds, outBounds.Position, target, EffectiveScale.At(pr.WorkingScale));
            }

            return current;
        }
        catch
        {
            current.Dispose();
            throw;
        }
    }

    private static void ExecuteFused(
        FusedShaderPass pass, RenderTarget target, float w, Rect outBounds,
        RenderNodeOperation source, float maxWorkingScale, PipelineDiagnostics? diagnostics)
    {
        BakeSource(target, w, outBounds, source, maxWorkingScale, paint: null);

        using SKImage srcImage = target.Value.Snapshot();
        using SKShader srcShader = srcImage.ToShader(SKShaderTileMode.Decal, SKShaderTileMode.Decal);

        var disposables = new List<IDisposable>();
        try
        {
            SKShader composed = ComposeStages(pass.Stages, srcShader, diagnostics, disposables);
            using var paint = new SKPaint { Shader = composed };
            using var canvas = new ImmediateCanvas(target, w, maxWorkingScale, logicalSize: outBounds.Size);
            canvas.Clear();
            using (canvas.PushDeviceSpace())
            {
                canvas.Canvas.DrawRect(new SKRect(0, 0, target.Width, target.Height), paint);
            }
        }
        finally
        {
            for (int i = disposables.Count - 1; i >= 0; i--)
                disposables[i].Dispose();
        }
    }

    private static void ExecuteSkia(
        SkiaFilterPass pass, RenderTarget target, float w, Rect outBounds,
        RenderNodeOperation source, float maxWorkingScale)
    {
        SKImageFilter? filter = null;
        foreach (Func<SKImageFilter?, SKImageFilter?> factory in pass.Filters)
        {
            SKImageFilter? outer = factory(filter);
            if (outer != null)
            {
                filter?.Dispose();
                filter = outer;
            }
        }

        using SKPaint? paint = filter != null ? new SKPaint { ImageFilter = filter } : null;
        BakeSource(target, w, outBounds, source, maxWorkingScale, paint);
        filter?.Dispose();
    }

    private static void BakeSource(
        RenderTarget target, float w, Rect outBounds, RenderNodeOperation source, float maxWorkingScale, SKPaint? paint)
    {
        using var canvas = new ImmediateCanvas(target, w, maxWorkingScale, logicalSize: outBounds.Size);
        canvas.Clear();
        using (canvas.PushTransform(Matrix.CreateTranslation(-outBounds.X, -outBounds.Y)))
        using (paint != null ? canvas.PushPaint(paint) : default)
        {
            source.Render(canvas);
        }
    }

    private static SKShader ComposeStages(
        ImmutableArray<FusedStage> stages, SKShader srcShader,
        PipelineDiagnostics? diagnostics, List<IDisposable> disposables)
    {
        SKShader current = srcShader;
        int i = 0;
        while (i < stages.Length)
        {
            if (stages[i] is ColorFilterStage colorFilter)
            {
                SKColorFilter? filter = colorFilter.Factory();
                if (filter != null)
                {
                    disposables.Add(filter);
                    current = Track(current.WithColorFilter(filter), disposables);
                }

                i++;
            }
            else
            {
                int j = i;
                var run = new List<RuntimeShaderStage>();
                while (j < stages.Length && stages[j] is RuntimeShaderStage runtime)
                {
                    run.Add(runtime);
                    j++;
                }

                current = BuildRuntimeRun(run, current, diagnostics, disposables);
                i = j;
            }
        }

        return current;
    }

    private static SKShader BuildRuntimeRun(
        List<RuntimeShaderStage> run, SKShader srcChild,
        PipelineDiagnostics? diagnostics, List<IDisposable> disposables)
    {
        bool wholeSource = run.Count == 1 && run[0].Source.Kind == SkslSourceKind.WholeSource;
        string source = wholeSource ? run[0].Source.Source : SkslSnippetMerger.Merge(run.Select(s => s.Source).ToList());
        string childName = wholeSource ? "src" : SkslSnippetMerger.SourceChildName;

        SKRuntimeEffect effect = CreateEffect(source, diagnostics);
        disposables.Add(effect);
        var builder = new SKRuntimeShaderBuilder(effect);
        disposables.Add(builder);
        builder.Children[childName] = srcChild;

        for (int k = 0; k < run.Count; k++)
        {
            string prefix = wholeSource ? string.Empty : $"fe{k}_";
            foreach (UniformBinding uniform in run[k].Uniforms)
                uniform.Apply(builder, prefix + uniform.Name);
            foreach (SamplerBinding sampler in run[k].Samplers)
                builder.Children[prefix + sampler.Name] = sampler.Shader;
            foreach (ChildBinding child in run[k].Children)
                builder.Children[prefix + child.Name] = child.Shader;
        }

        return Track(builder.Build(), disposables);
    }

    private static SKRuntimeEffect CreateEffect(string source, PipelineDiagnostics? diagnostics)
    {
        SKRuntimeEffect? effect = SKRuntimeEffect.CreateShader(source, out string? error);
        if (effect == null || error != null)
        {
            effect?.Dispose();
            throw new InvalidOperationException($"Failed to compile fused SKSL program: {error}");
        }

        if (diagnostics != null)
            diagnostics.ProgramCreations++;

        return effect;
    }

    private static SKShader Track(SKShader shader, List<IDisposable> disposables)
    {
        disposables.Add(shader);
        return shader;
    }
}
