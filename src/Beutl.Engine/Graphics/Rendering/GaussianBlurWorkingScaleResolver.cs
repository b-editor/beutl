namespace Beutl.Graphics.Rendering;

internal readonly record struct GaussianBlurWorkingScaleResolver(float MaxLogicalSigma)
{
    public const float MaxDeviceSigma = 500f;

    public RenderScaleContract CreateContract()
    {
        return RenderScaleContract.Custom(Resolve, typeof(GaussianBlurWorkingScaleResolver));
    }

    private float Resolve(RenderScaleContext context)
    {
        float standardWorkingScale = RenderScaleUtilities.ResolveWorkingScale(
            context.InputSupplies.ToArray(),
            context.OutputScale,
            context.MaxWorkingScale);
        if (!float.IsFinite(MaxLogicalSigma) || MaxLogicalSigma <= 0f)
            return standardWorkingScale;

        float sigmaLimitedScale = MaxDeviceSigma / MaxLogicalSigma;
        return MathF.Min(standardWorkingScale, sigmaLimitedScale);
    }
}
