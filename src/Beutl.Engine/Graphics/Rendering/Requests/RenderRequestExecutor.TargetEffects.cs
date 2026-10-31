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
                using EffectTargets targets = CreateEffectTargets(inputs);
                Rect[] inputBounds = targets.Select(static target => target.Bounds).ToArray();
                Rect[] bufferBounds = FilterEffectWorkingScalePolicy.CalculateEffectItemBufferBounds(
                    inputBounds, program.GetOrderedItems(), _options.TargetDomain ?? requiredRegion);
                float density = policy.Resolve(
                    inputs.SelectToArray(static input => input.EffectiveScale), inputBounds, bufferBounds,
                    _options.OutputScale, _options.MaxWorkingScale).Value;
                using var builder = new SKImageFilterBuilder();
                using FilterEffectExecutor executor = CreateFilterEffectExecutor(targets, builder, density);
                using (destination.PushClip(requiredRegion))
                {
                    ApplyFilterEffects(executor, program, materializationRequired: true, density,
                        _options.MaxWorkingScale, destination);
                    foreach (EffectTarget target in targets)
                    {
                        if (target.RenderTarget is not { } surface) continue;
                        using MaterializedRenderValue value = MaterializeEffectItemTarget(target, surface, target.Bounds);
                        DrawValue(value, destination);
                    }
                }
                return true;
            }, normalizeGridPhase: true);
        }
    }
}
