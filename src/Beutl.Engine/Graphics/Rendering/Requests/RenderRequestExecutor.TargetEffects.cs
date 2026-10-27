using Beutl.Graphics.Effects;

namespace Beutl.Graphics.Rendering.Requests;

internal sealed partial class RenderRequestExecutor
{
    private sealed partial class RenderRequestExecutionState
    {
        private void ExecuteTargetFilterEffects(FilterEffectContext program, FilterEffectWorkingScalePolicy policy,
            IReadOnlyList<MaterializedRenderValue> inputs, ImmediateCanvas destination, Rect requiredRegion)
        {
            if (inputs.Count == 0)
                return;

            ExecuteOnDeviceGrid(destination, () =>
            {
                using var targets = new EffectTargets();
                foreach (MaterializedRenderValue input in inputs)
                {
                    bool complete = input.RasterBounds.Contains(input.CompleteBounds);
                    Rect bounds = complete ? input.CompleteBounds : input.Bounds;
                    targets.Add(new EffectTarget(input.Target, bounds, input.EffectiveScale, input.DeviceBounds,
                        input.DeviceGridOffset, input.PreserveImperativeRasterPlacement && complete)
                    {
                        OriginalBounds = new Rect(default, bounds.Size),
                        Bounds = bounds,
                    });
                }
                Rect[] inputBounds = targets.Select(static target => target.Bounds).ToArray();
                Rect[] bufferBounds = FilterEffectWorkingScalePolicy.CalculateEffectItemBufferBounds(
                    inputBounds, program.GetOrderedItems(), _options.TargetDomain ?? requiredRegion);
                float density = policy.Resolve(
                    inputs.SelectToArray(static input => input.EffectiveScale), inputBounds, bufferBounds,
                    _options.OutputScale, _options.MaxWorkingScale).Value;
                using var execution = new RenderExecutionContext(_renderDependency, density,
                    _options.MaxWorkingScale, destination);
                using var builder = new SKImageFilterBuilder();
                using var executor = new FilterEffectExecutor(targets, builder, _options.Intent, _options.Purpose,
                    _options.OutputScale, density, _options.MaxWorkingScale, _activeDeviceGridOffset,
                    (target, source) => AcquireStandaloneProgram(target, source), _drawableBrushMaterializer,
                    useExecutorManagedCanvas: true, renderTargetLeaseSession: _targets,
                    targetDomain: _options.TargetDomain, acquireShaderGraphProgram: AcquireShaderGraphProgram)
                { ExecutionContext = execution };
                using (destination.PushClip(requiredRegion))
                {
                    executor.Apply(program);
                    executor.CompletePolicyBoundary(materializationRequired: true);
                    foreach (EffectTarget target in targets)
                        if (target.RenderTarget is { } surface)
                            destination.DrawRenderTargetScaled(surface, target.Bounds);
                }
                return true;
            }, normalizeGridPhase: true);
        }
    }
}
