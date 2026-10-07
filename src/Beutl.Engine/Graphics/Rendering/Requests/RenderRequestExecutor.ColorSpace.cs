using Beutl.Media;

namespace Beutl.Graphics.Rendering.Requests;

internal sealed partial class RenderRequestExecutor
{
    private sealed partial class RenderRequestExecutionState
    {
        private MaterializedRenderValue LinearShaderInput(MaterializedRenderValue input)
        {
            // Oversized external CPU inputs may be sampled by a clamped SkSL destination without
            // allocating another oversized target. Skia transforms their child shader to the linear
            // destination; such a footprint cannot reach the Vulkan-native execution path.
            return _targets.ExceedsBufferBudget(input.DeviceBounds.Size)
                ? input
                : ConvertColorSpace(input, RenderTargetPixelFormat.LinearPremultipliedRgba16Float);
        }

        /// <summary>Copies the complete physical backing without changing its density or placement.</summary>
        private MaterializedRenderValue ConvertColorSpace(
            MaterializedRenderValue input,
            RenderTargetPixelFormat pixelFormat)
        {
            if (input.Target.ColorSpace == pixelFormat.GetColorSpace())
                return input;

            RenderTargetLease? lease = _targets.TryAcquire(input.DeviceBounds.Size, pixelFormat: pixelFormat);
            if (lease is null)
                throw new PreviewAllocationDropException();
            _intermediateTargetAcquisitions++;
            bool succeeded = false;
            try
            {
                var output = new MaterializedRenderValue(
                    lease,
                    input.Bounds,
                    input.EffectiveScale,
                    input.DeviceBounds,
                    input.DeviceGridOffset,
                    input.CompleteBounds,
                    input.PreserveImperativeRasterPlacement);
                using (var canvas = CreateValueCanvas(output))
                using (canvas.PushDeviceSpace())
                {
                    // Skia converts unpremultiplied RGB and preserves coverage alpha. No filter may
                    // be attached here: an image filter can run before DrawImage's color conversion.
                    canvas.DrawRenderTargetPixelsWithoutFlush(input.Target, 0, 0);
                }

                _ownedValues.Add(output);
                succeeded = true;
                return output;
            }
            finally
            {
                if (!succeeded)
                    lease.Dispose();
            }
        }
    }
}
