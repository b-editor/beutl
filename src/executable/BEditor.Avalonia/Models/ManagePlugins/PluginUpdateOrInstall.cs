using BEditor.Packaging;

namespace BEditor.Models.ManagePlugins
{
    public record PluginUpdateOrInstall(Package Target, PackageVersion Version, PluginChangeType Type);
}