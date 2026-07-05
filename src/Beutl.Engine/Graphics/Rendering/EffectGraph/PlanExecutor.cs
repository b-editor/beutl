using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Beutl.Graphics.Effects;
using SkiaSharp;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// Runs a <see cref="CompiledPlan"/> against the graphics context (feature 004, T023, D2/D5). A plan is a
/// schedule of passes threaded over the input operation set. Two pass families interleave:
/// <list type="bullet">
/// <item><description>A descriptor pass (<see cref="FusedShaderPass"/>, <see cref="SkiaFilterPass"/>) transforms
/// each current operation independently — a fused pass executes as one draw built by shader composition (input
/// image shader → <c>WithColorFilter</c> wraps → nested <c>SKRuntimeEffect</c> child shaders, adjacent snippets
/// merged into one program), a Skia-filter pass as one filtered draw. The RGBA16F premultiplied linear-light
/// representation is preserved between stages.</description></item>
/// <item><description>An <see cref="OpaqueLegacyPass"/> segment runs the retained (internal-only) activator over
/// the <em>whole</em> current set via <see cref="LegacyBridgeExecutor"/> (T019 bridge). Its input is the upstream
/// pass's output and its output feeds the downstream passes, so an unmigrated effect fuses correctly between
/// migrated ones. All of a segment's counter attribution stays inside the legacy machinery (never additionally
/// counted here), keeping bridged content's counters byte-identical to today's.</description></item>
/// </list>
/// A plan that is a single opaque pass over all inputs keeps the whole-plan fast path (byte-identity + counter
/// parity for a fully-unmigrated chain). Descriptor-pass counters follow §C8: one
/// <see cref="PipelineDiagnostics.GpuPasses"/> per executed draw, one
/// <see cref="PipelineDiagnostics.ProgramCreations"/> per <c>SKRuntimeEffect</c> created.
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
        // Whole-plan fast path: a fully-unmigrated chain is one opaque pass over every input at once. Delegating
        // to the bridge keeps the render byte-identical and its counters unchanged from the pre-redesign pipeline.
        if (plan.Passes is [OpaqueLegacyPass onlyOpaque])
        {
            return LegacyBridgeExecutor.Execute(
                onlyOpaque.Context, inputs, bounds, outputScale, workingScale, maxWorkingScale, diagnostics, pool);
        }

        // Mixed plan: thread the whole operation set through the schedule pass by pass, so an opaque segment can
        // consume the upstream set and hand its output to the downstream passes.
        var current = new List<RenderNodeOperation>(inputs);
        try
        {
            for (int k = 0; k < plan.Passes.Length; k++)
            {
                CompiledPass pass = plan.Passes[k];
                if (pass is OpaqueLegacyPass opaque)
                {
                    ExecuteOpaqueSegment(
                        opaque, current, bounds, outputScale, workingScale, maxWorkingScale, diagnostics, pool);
                }
                else
                {
                    MapDescriptorPass(
                        pass, resources.Passes[k], current, workingScale, maxWorkingScale, diagnostics, pool);
                }
            }

            return current.ToArray();
        }
        catch
        {
            RenderNodeOperation.DisposeAll(CollectionsMarshal.AsSpan(current));
            current.Clear();
            throw;
        }
    }

    // Runs a bridged legacy segment over the whole current set. The bridge takes ownership of the handed-in
    // operations (same contract as the whole-plan fast path); on success its output replaces the set.
    private static void ExecuteOpaqueSegment(
        OpaqueLegacyPass opaque, List<RenderNodeOperation> current, Rect bounds, float outputScale,
        float workingScale, float maxWorkingScale, PipelineDiagnostics? diagnostics, RenderTargetPool? pool)
    {
        RenderNodeOperation[] segmentInputs = current.ToArray();
        current.Clear();
        RenderNodeOperation[] produced = LegacyBridgeExecutor.Execute(
            opaque.Context, segmentInputs, bounds, outputScale, workingScale, maxWorkingScale, diagnostics, pool);
        current.AddRange(produced);
    }

    // Applies a descriptor pass to every current operation independently. A single upstream operation uses the
    // per-frame resolution (resolved size, working-scale carry, empty-ROI skip); a fanned-out set (an upstream
    // opaque split) sizes each branch from its own bounds — a coordinate-invariant fused pass is identity, so an
    // operation's output bounds equal its input bounds.
    private static void MapDescriptorPass(
        CompiledPass pass, PassResolution resolution, List<RenderNodeOperation> current,
        float workingScale, float maxWorkingScale, PipelineDiagnostics? diagnostics, RenderTargetPool? pool)
    {
        bool linear = current.Count == 1;
        var outputs = new List<RenderNodeOperation>(current.Count);
        try
        {
            for (int i = 0; i < current.Count; i++)
            {
                RenderNodeOperation op = current[i];
                current[i] = null!;
                outputs.Add(MapOneOperation(
                    pass, resolution, linear, op, workingScale, maxWorkingScale, diagnostics, pool));
            }
        }
        catch
        {
            RenderNodeOperation.DisposeAll(CollectionsMarshal.AsSpan(outputs));
            throw;
        }

        current.Clear();
        current.AddRange(outputs);
    }

    private static RenderNodeOperation MapOneOperation(
        CompiledPass pass, PassResolution resolution, bool linear, RenderNodeOperation op,
        float workingScale, float maxWorkingScale, PipelineDiagnostics? diagnostics, RenderTargetPool? pool)
    {
        // A coordinate-invariant fused pass is identity: its output bounds are the operation's own bounds. Sizing
        // from the operation (rather than the pass's described output bounds) both survives an upstream opaque
        // node that did not advance the builder's logical bounds and sizes each fan-out branch correctly.
        Rect outBounds;
        int width, height;
        float w;
        bool skip;
        if (pass is FusedShaderPass || !linear)
        {
            outBounds = pass.OutputBounds.IsInvalid || pass is FusedShaderPass ? op.Bounds : pass.OutputBounds;
            w = RenderNodeContext.ClampWorkingScaleToBufferBudget(outBounds, workingScale);
            (width, height) = CustomFilterEffectContext.DeviceBufferSize(outBounds, w);
            skip = width <= 0 || height <= 0;
        }
        else
        {
            outBounds = pass.OutputBounds.IsInvalid ? op.Bounds : pass.OutputBounds;
            width = resolution.Width;
            height = resolution.Height;
            w = resolution.WorkingScale;
            skip = resolution.SkipEmpty;
        }

        if (skip)
        {
            return op;
        }

        RenderTarget target;
        try
        {
            target = RenderTargetPool.Acquire(pool, width, height, diagnostics)
                ?? throw new InvalidOperationException(
                    $"Effect pass buffer allocation failed ({width}x{height} px, w {w}, bounds {outBounds}).");
        }
        catch
        {
            op.Dispose();
            throw;
        }

        try
        {
            switch (pass)
            {
                case FusedShaderPass fused:
                    ExecuteFused(fused, target, w, outBounds, op, maxWorkingScale, diagnostics);
                    break;
                case SkiaFilterPass skia:
                    ExecuteSkia(skia, target, w, outBounds, op, maxWorkingScale);
                    break;
                default:
                    throw new NotSupportedException(
                        $"Pass '{pass.GetType().Name}' is not executable by the descriptor path.");
            }
        }
        catch
        {
            target.Dispose();
            op.Dispose();
            throw;
        }

        if (diagnostics != null)
            diagnostics.GpuPasses++;

        op.Dispose();
        return RenderNodeOperation.CreateFromRenderTarget(
            outBounds, outBounds.Position, target, EffectiveScale.At(w));
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
        string childName = wholeSource ? "src" : SkslSnippetMerger.SourceChildName;

        // The program (merged/whole SKSL parse) is structural, so it is cached process-wide by a source-identity
        // signature: a warm run neither re-merges nor re-parses, keeping ProgramCreations at zero (SC-002). The
        // cached builder (which owns its SKRuntimeEffect) is reused, its per-frame uniforms/children overwritten and
        // Build() re-run below; it is NOT disposed here — disposing it would free the shared effect (the cache
        // disposes it on eviction). Its built shader is independent and IS disposed per frame.
        string signature = ProgramSignature(run, wholeSource);
        SKRuntimeShaderBuilder builder = ProgramCache.GetOrCreate(
            signature,
            () => wholeSource ? run[0].Source.Source : SkslSnippetMerger.Merge(run.Select(s => s.Source).ToList()),
            diagnostics);
        builder.Children[childName] = srcChild;

        for (int k = 0; k < run.Count; k++)
        {
            string prefix = wholeSource ? string.Empty : $"fe{k}_";
            foreach (UniformBinding uniform in run[k].Uniforms)
                uniform.Apply(builder, prefix + uniform.Name);
            // A per-frame sampler/child shader (a LUT, curve textures) is bound here but owned by the graph, which
            // releases it after execution even when this pass is skipped for an empty ROI (contract A2).
            foreach (SamplerBinding sampler in run[k].Samplers)
                builder.Children[prefix + sampler.Name] = sampler.Shader;
            foreach (ChildBinding child in run[k].Children)
                builder.Children[prefix + child.Name] = child.Shader;
        }

        return Track(builder.Build(), disposables);
    }

    // The program-cache key: the ordered source identities of a runtime run. A whole-source stage keys on its one
    // source; a merged snippet run keys on the sequence of snippet hashes, which fully determines the merged text.
    private static string ProgramSignature(List<RuntimeShaderStage> run, bool wholeSource)
    {
        if (wholeSource)
            return "w:" + run[0].Source.IdentityHash;

        var sb = new System.Text.StringBuilder("m:");
        for (int i = 0; i < run.Count; i++)
        {
            if (i > 0)
                sb.Append(',');
            sb.Append(run[i].Source.IdentityHash);
        }

        return sb.ToString();
    }

    private static SKShader Track(SKShader shader, List<IDisposable> disposables)
    {
        disposables.Add(shader);
        return shader;
    }
}
