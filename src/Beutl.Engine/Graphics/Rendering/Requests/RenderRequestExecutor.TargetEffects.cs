using Beutl.Graphics.Effects;

namespace Beutl.Graphics.Rendering.Requests;

internal sealed partial class RenderRequestExecutor
{
    private sealed partial class RenderRequestExecutionState
    {
        private void ExecuteTargetFilterEffects(RenderFragmentReference fragment, FilterEffectContext program,
            FilterEffectWorkingScalePolicy policy, IReadOnlyList<MaterializedRenderValue> inputs,
            IReadOnlyList<RenderExecutionInputRange> inputRanges, ImmediateCanvas destination, Rect requiredRegion)
        {
            if (inputs.Count == 0)
                return;

            ExecuteOnDeviceGrid(destination, () =>
            {
                // Target work is given all of its inputs.
                using EffectTargets targets = CreateEffectTargets(inputs, takesParts: false, readRegion: null);
                Rect[] inputBounds = targets.Select(static target => target.Bounds).ToArray();
                Rect[] bufferBounds = FilterEffectWorkingScalePolicy.CalculateEffectItemBufferBounds(
                    inputBounds, program.GetOrderedItems(), _options.TargetDomain ?? requiredRegion);
                EffectiveScale[] supplies = inputs.SelectToArray(static input => input.EffectiveScale);
                float density = policy.Resolve(supplies, inputBounds, bufferBounds,
                    _options.OutputScale, _options.MaxWorkingScale).Value;
                using var builder = new SKImageFilterBuilder();
                using FilterEffectExecutor executor = CreateFilterEffectExecutor(targets, builder, density);
                using (destination.PushClip(requiredRegion))
                {
                    ApplyFilterEffects(executor, program, materializationRequired: true, density,
                        _options.MaxWorkingScale, destination, requiredRegion);
                    foreach (EffectTarget target in targets)
                    {
                        if (target.RenderTarget is not { } surface) continue;
                        using MaterializedRenderValue value = MaterializeEffectItemTarget(target, surface, target.Bounds);
                        DrawValue(value, destination);
                    }
                }

                // Ordered target work only knows its inputs' actual bounds and densities during execution.
                if (_options.CanReportPreviewWorkingScaleClamp && density == _options.MaxWorkingScale
                    && !requiredRegion.IsEmpty && !PreviewAllocationDropObserved
                    && !_options.Owner.PreviewDiagnostics.IsWorkingScaleClamped
                    && IsTargetEffectWorkingScaleClamped(fragment, policy, inputRanges, supplies, inputBounds, bufferBounds, density))
                {
                    _options.Owner.RecordPreviewWorkingScaleClamp();
                }

                return true;
            }, normalizeGridPhase: true);
        }

        private bool IsTargetEffectWorkingScaleClamped(RenderFragmentReference fragment, FilterEffectWorkingScalePolicy policy,
            IReadOnlyList<RenderExecutionInputRange> inputRanges, EffectiveScale[] supplies,
            Rect[] inputBounds, Rect[] bufferBounds, float density)
        {
            try
            {
                var uncapped = new RegionAnalyzer.UncappedWorkingScaleResolver(_options.OutputScale);
                for (int index = 0; index < inputRanges.Count; index++)
                {
                    RenderFragmentReference input = fragment.Inputs[index];
                    RenderExecutionInputRange range = inputRanges[index];
                    for (int valueIndex = range.StartIndex; valueIndex < range.StartIndex + range.Count; valueIndex++)
                    {
                        // Values with their own runtime densities retain those densities in the comparison.
                        if (supplies[valueIndex] == input.EffectiveScale)
                            supplies[valueIndex] = uncapped.Resolve(input);
                    }
                }

                return policy.Resolve(supplies, inputBounds, bufferBounds, _options.OutputScale,
                    float.PositiveInfinity).Value > density;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or ArithmeticException)
            {
                return false;
            }
        }
    }
}
