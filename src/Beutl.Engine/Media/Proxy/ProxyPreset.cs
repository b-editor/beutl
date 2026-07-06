namespace Beutl.Media.Proxy;

/// <summary>
/// Proxy quality presets shipped by the MVP. The preset <strong>value set</strong> (Half / Quarter / Eighth)
/// is a closed product surface per FR-017: adding a new preset value is an in-tree change (this enum plus
/// <see cref="ProxyPresetDefinitions"/>, the UI dropdown, and the int mapping in
/// <c>ProxyStoreConfig.DefaultPreset</c>). A future plugin-extensible preset system (string-keyed
/// <c>ProxyPresetId</c>) is an explicit follow-up, out of MVP scope. The encode <em>parameters</em> for an
/// existing preset value can be overridden without an in-tree change via
/// <see cref="ProxyPresetDefinitions.Register"/>.
/// </summary>
public enum ProxyPreset
{
    Half = 1,
    Quarter = 2,
    Eighth = 3,
}
