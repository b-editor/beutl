using System.Collections.Concurrent;
using System.Collections.ObjectModel;

namespace Beutl.Media.Proxy;

public readonly record struct ProxyEncodeParameters(
    float Scale,
    int Crf,
    int? LongEdgeClamp,
    string Tune,
    string Preset);

/// <summary>
/// Provides and overrides the encode parameters (CRF / scale / long-edge clamp / tune / preset) for the
/// built-in <see cref="ProxyPreset"/> values. The preset <strong>value set</strong> is closed per FR-017
/// (see <see cref="ProxyPreset"/>); the encode <strong>parameters</strong> for those built-in presets ARE
/// overridable via <see cref="Register"/> / <see cref="Unregister"/>. <see cref="Unregister"/> restores the
/// built-in default rather than removing the key, so <see cref="Get"/> always returns a usable value for a
/// built-in preset.
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

    private static readonly ConcurrentDictionary<ProxyPreset, ProxyEncodeParameters> s_active =
        new(s_builtIn.ToDictionary(kv => kv.Key, kv => kv.Value));

    public static ProxyEncodeParameters Get(ProxyPreset preset)
    {
        if (s_active.TryGetValue(preset, out ProxyEncodeParameters parameters))
            return parameters;

        throw new ArgumentOutOfRangeException(nameof(preset), preset, null);
    }

    /// <summary>
    /// Overrides (add/update) the encode parameters for a built-in <paramref name="preset"/>. Only the
    /// three built-in <see cref="ProxyPreset"/> values are accepted (the preset value set is closed per
    /// FR-017).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="preset"/> is not a built-in value.</exception>
    public static void Register(ProxyPreset preset, ProxyEncodeParameters parameters)
    {
        if (!Enum.IsDefined(preset))
            throw new ArgumentOutOfRangeException(nameof(preset), preset, "Only built-in proxy presets can be overridden.");

        s_active[preset] = parameters;
    }

    /// <summary>
    /// Restores the built-in default encode parameters for <paramref name="preset"/>. The entry is NOT
    /// removed — that would make <see cref="Get"/> throw for a built-in preset. Returns <see langword="true"/>
    /// if this call observed a change (the active value differed from the built-in default).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="preset"/> is not a built-in value.</exception>
    public static bool Unregister(ProxyPreset preset)
    {
        if (!Enum.IsDefined(preset))
            throw new ArgumentOutOfRangeException(nameof(preset), preset, "Only built-in proxy presets can be restored.");

        ProxyEncodeParameters builtIn = s_builtIn[preset];
        ProxyEncodeParameters current = s_active[preset];
        s_active[preset] = builtIn;
        return !current.Equals(builtIn);
    }

    /// <summary>
    /// A non-mutating snapshot of the active parameters for every built-in preset. Callers cannot mutate
    /// the live registry through the returned view.
    /// </summary>
    public static IReadOnlyDictionary<ProxyPreset, ProxyEncodeParameters> All
        => new ReadOnlyDictionary<ProxyPreset, ProxyEncodeParameters>(
            new Dictionary<ProxyPreset, ProxyEncodeParameters>(s_active));
}
