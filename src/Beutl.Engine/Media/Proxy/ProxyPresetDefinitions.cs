namespace Beutl.Media.Proxy;

public readonly record struct ProxyEncodeParameters(
    float Scale,
    int Crf,
    int? LongEdgeClamp,
    string Tune,
    string Preset);

public static class ProxyPresetDefinitions
{
    private static readonly IReadOnlyDictionary<ProxyPreset, ProxyEncodeParameters> s_parameters =
        new Dictionary<ProxyPreset, ProxyEncodeParameters>
        {
            [ProxyPreset.Half] = new(0.5f, 25, 1920, "fastdecode", "fast"),
            [ProxyPreset.Quarter] = new(0.25f, 26, 1280, "fastdecode", "fast"),
            [ProxyPreset.Eighth] = new(0.125f, 28, 960, "fastdecode", "fast"),
        };

    public static ProxyEncodeParameters Get(ProxyPreset preset)
    {
        if (s_parameters.TryGetValue(preset, out ProxyEncodeParameters parameters))
            return parameters;

        throw new ArgumentOutOfRangeException(nameof(preset), preset, null);
    }

    public static IReadOnlyDictionary<ProxyPreset, ProxyEncodeParameters> All => s_parameters;
}
