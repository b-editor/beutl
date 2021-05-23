namespace BEditor.Models.ManagePlugins
{
    public record PluginUpdateOrInstall(Package.Package Target, PluginChangeType Type);
}