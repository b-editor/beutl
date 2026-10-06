using System.Collections.Immutable;
using System.Runtime.ExceptionServices;
using Beutl.Graphics.Effects;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Rendering.Requests;

internal sealed partial class RenderRequestExecutor
{
    private sealed partial class RenderRequestExecutionState
    {
        private IReadOnlyList<MaterializedRenderValue> Materialize(
            RenderFragmentReference fragment,
            ImmediateCanvas currentTarget,
            EffectiveScale? requestedScale = null)
        {
            if (fragment.EffectiveScale.IsUnbounded)
            {
                if (!_materializationDemands.TryGetValue(fragment, out EffectiveScale demand))
                {
                    throw new InvalidOperationException(
                        "An executable fragment is not reachable from the request publication roots.");
                }

                if (requestedScale is { } callerRequest)
                {
                    float callerDensity = MathF.Min(
                        callerRequest.Value,
                        RenderScaleUtilities.SanitizeMaxWorkingScale(_options.MaxWorkingScale));
                    callerDensity = RenderMaterializationDensityPolicy.Clamp(
                        fragment,
                        callerDensity);
                    if (callerDensity > demand.Value)
                    {
                        throw new InvalidOperationException(
                            "The compiled materialization demand does not cover its contextual caller.");
                    }
                }

                requestedScale = demand;
            }

            return MaterializeCore(
                fragment,
                currentTarget,
                requestedScale);
        }

        private IReadOnlyList<MaterializedRenderValue> MaterializeCore(
            RenderFragmentReference fragment,
            ImmediateCanvas currentTarget,
            EffectiveScale? requestedScale = null)
        {
            if (_values.TryGetValue(fragment, out IReadOnlyList<MaterializedRenderValue>? cached))
                return cached;

            IReadOnlyList<MaterializedRenderValue> result;
            bool cacheHit = TryMaterializeCacheHit(
                fragment,
                out IReadOnlyList<MaterializedRenderValue>? hitValues);
            if (cacheHit)
            {
                result = hitValues!;
            }
            else
            {
                result = ExecuteFragment(fragment, currentTarget, requestedScale);
            }
            StageCacheCaptures(fragment, result);
            _values.Add(fragment, result);
            AddValueReferences(result);
            if (fragment.Kind == RenderFragmentKind.ContributeValues && !cacheHit)
                CompleteFragmentUse(fragment.Inputs.Single());
            return result;
        }

        private IReadOnlyList<MaterializedRenderValue> ExecuteFragment(
            RenderFragmentReference fragment,
            ImmediateCanvas currentTarget,
            EffectiveScale? requestedScale)
        {
            if (_executionPlan.TryGetMembership(_graph, fragment, out ExecutionIslandMembership membership))
            {
                ExecutionIsland island = _executionLedger.Begin(membership);
                IReadOnlyList<MaterializedRenderValue> values = membership.Island.ShaderRun is { } run
                    ? ExecuteCompiledShaderRun(run, currentTarget, requestedScale)
                    : MaterializePlannedFragment(fragment, currentTarget, requestedScale);
                _executionLedger.Complete(island);
                return values;
            }

            return fragment.Kind switch
            {
                RenderFragmentKind.MaterializedInput => MaterializeExternal(fragment),
                RenderFragmentKind.ContributeValues => MaterializeSingleInput(fragment, currentTarget),
                _ => throw new InvalidOperationException(
                    $"Executable fragment '{fragment.Kind}' is not assigned to an execution island."),
            };
        }

        private IReadOnlyList<MaterializedRenderValue> MaterializePlannedFragment(
            RenderFragmentReference fragment,
            ImmediateCanvas currentTarget,
            EffectiveScale? requestedScale)
            => fragment.Kind switch
            {
                RenderFragmentKind.OpaqueSource
                    or RenderFragmentKind.OpaqueMap
                    or RenderFragmentKind.OpaqueCombine
                    or RenderFragmentKind.OpaqueExpand => ExecuteOpaque(fragment, currentTarget, requestedScale),
                RenderFragmentKind.FilterEffectSegment => ExecuteEffectItem(fragment, currentTarget, requestedScale),
                RenderFragmentKind.Shader => ExecuteShader(fragment, currentTarget, requestedScale),
                RenderFragmentKind.Geometry => ExecuteGeometry(fragment, currentTarget, requestedScale),
                RenderFragmentKind.Opacity => MaterializeOpacity(fragment, currentTarget, requestedScale),
                RenderFragmentKind.OpacityMask => MaterializeOpacityMask(fragment, currentTarget, requestedScale),
                RenderFragmentKind.Layer => MaterializeLayer(fragment, currentTarget, requestedScale),
                RenderFragmentKind.TargetCapture
                    or RenderFragmentKind.BuiltInBackdropCapture => CaptureTarget(fragment, currentTarget),
                RenderFragmentKind.TargetScope
                    when ((TargetScopeRenderFragmentPayload)fragment.Payload!).Description.IsValueReplayMap
                    => MaterializeValueReplayMap(fragment, currentTarget, requestedScale),
                _ => throw new NotSupportedException(
                    $"The planned fragment '{fragment.Kind}' cannot be materialized as a value."),
            };

        private IReadOnlyList<MaterializedRenderValue> MaterializeSingleInput(
            RenderFragmentReference fragment,
            ImmediateCanvas currentTarget,
            EffectiveScale? requestedScale = null)
        {
            if (fragment.Inputs.Length != 1)
                throw new InvalidOperationException("A unary recorded fragment requires exactly one input.");
            return Materialize(fragment.Inputs[0], currentTarget, requestedScale);
        }

    }
}
