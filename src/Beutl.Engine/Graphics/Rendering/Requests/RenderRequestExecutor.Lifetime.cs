using System.Runtime.ExceptionServices;

namespace Beutl.Graphics.Rendering.Requests;

internal sealed partial class RenderRequestExecutor
{
    private sealed partial class RenderRequestExecutionState
    {
        private void RecordSynchronization()
        {
            _synchronizations = checked(_synchronizations + 1);
        }

        public void DisposeNonCacheValues()
        {
            try
            {
                DisposeValues(static (_, isCapture) => !isCapture);
            }
            finally
            {
                _values.Clear();
                _valueReferences.Clear();
                _backdropCaptures = null;
            }
        }

        public void Dispose()
        {
            List<Exception>? failures = null;
            try
            {
                DisposeValues(static (_, _) => true);
            }
            catch (AggregateException aggregate)
            {
                (failures ??= []).AddRange(aggregate.Flatten().InnerExceptions);
            }
            catch (Exception ex)
            {
                (failures ??= []).Add(ex);
            }
            finally
            {
                Array.Clear(_cacheCaptures);
                ClearCacheCaptureValues();
            }

            try
            {
                RejectBuiltInBackdropCaptures();
            }
            catch (AggregateException aggregate)
            {
                (failures ??= []).AddRange(aggregate.Flatten().InnerExceptions);
            }
            catch (Exception ex)
            {
                (failures ??= []).Add(ex);
            }

            if (failures is null)
                return;
            if (failures.Count == 1)
                ExceptionDispatchInfo.Capture(failures[0]).Throw();
            if (failures.Count > 1)
                throw new AggregateException("One or more execution-state resources failed to dispose.", failures);
        }

        private void DisposeValues(Func<MaterializedRenderValue, bool, bool> predicate)
        {
            List<Exception>? failures = null;
            // The loop removes from _ownedValues, so it needs a snapshot; Reverse() already buffers the set
            // into an array that ToArray() then copies a second time, and a set has no order to preserve.
            var snapshot = new MaterializedRenderValue[_ownedValues.Count];
            _ownedValues.CopyTo(snapshot);
            foreach (MaterializedRenderValue value in snapshot)
            {
                bool isCapture = IsCacheCaptureValue(value);
                if (!predicate(value, isCapture))
                    continue;

                _ownedValues.Remove(value);
                RemoveCacheCaptureValue(value);
                try
                {
                    DisposeOwnedValue(value);
                }
                catch (Exception ex)
                {
                    (failures ??= []).Add(ex);
                }
            }

            if (failures is null)
                return;
            if (failures.Count == 1)
                ExceptionDispatchInfo.Capture(failures[0]).Throw();
            throw new AggregateException("One or more render values failed to dispose.", failures);
        }

        public RenderExecutionStatistics CreateStatistics()
            => new(
                _shaderRunExecutions,
                _shaderStageExecutions,
                _fusedShaderRunExecutions,
                _spirvShaderRunExecutions,
                _intermediateTargetAcquisitions,
                _programCacheHits,
                _synchronizations);

        public void ValidateExecutionCompleted(bool allowSkippedIslands)
            => _executionLedger.ValidateCompleted(
                allowSkippedIslands || PreviewAllocationDropObserved,
                _regions);
    }
}
