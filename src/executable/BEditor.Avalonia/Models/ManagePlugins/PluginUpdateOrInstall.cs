using BEditor.Packaging;

namespace BEditor.Models.ManagePlugins
{
    public sealed record PluginUpdateOrInstall(Package Target, PackageVersion Version, PluginChangeType Type);
}