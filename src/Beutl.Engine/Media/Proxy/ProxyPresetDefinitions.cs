using System.Collections.ObjectModel;

namespace Beutl.Media.Proxy;

public readonly record struct ProxyEncodeParameters(
    float Scale,
    int Crf,
    int? LongEdgeClamp,
    string Tune,
    string Preset);

/// <summary>
/// Provides the encode parameters for the built-in <see cref="ProxyPreset"/> values.
/// </summary>
public static class ProxyPresetDefinitions
{
    private static readonly IReadOnlyDictionary<ProxyPreset, ProxyEncodeParameters> s_builtIn =
        new ReadOnlyDictionary<ProxyPreset, ProxyEncodeParameters>(
            new Dictionary<ProxyPreset, ProxyEncodeParameters>
            {
                [ProxyPreset.Half] = new(0.5f, 25, 1920, "fastdecode", "fast"),
                [ProxyPreset.Quarter] = new(0.25f, 26, 1280, "fastdecode", "fast"),
                [ProxyPreset.Eighth] = new(0.125f, 28, 960, "fastdecode", "fast"),
            });

    public static ProxyEncodeParameters Get(ProxyPreset preset)
    {
        if (s_builtIn.TryGetValue(preset, out ProxyEncodeParameters parameters))
            return parameters;

        throw new ArgumentOutOfRangeException(nameof(preset), preset, null);
    }

    /// <summary>
    /// A snapshot of the parameters for every built-in preset.
    /// </summary>
    public static IReadOnlyDictionary<ProxyPreset, ProxyEncodeParameters> All
        => new ReadOnlyDictionary<ProxyPreset, ProxyEncodeParameters>(
            new Dictionary<ProxyPreset, ProxyEncodeParameters>(s_builtIn));
}
