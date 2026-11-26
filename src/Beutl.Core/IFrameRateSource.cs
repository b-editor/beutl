namespace Beutl;

internal interface IFrameRateSource
{
    Rational GetFrameRate();

    internal static Rational DefaultFrameRate => new(30);

    internal static Rational Normalize(Rational rate)
        => rate is { Numerator: > 0, Denominator: > 0 } ? rate : DefaultFrameRate;

    internal static Rational ReadFromHierarchy(IHierarchical? obj)
        => obj?.FindHierarchicalParent<IFrameRateSource>(includeSelf: true)?.GetFrameRate() ?? DefaultFrameRate;
}
