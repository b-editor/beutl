namespace BEditor.Models.ManagePlugins
{
    public record PluginUpdateOrInstall(Packaging.Package Target, PluginChangeType Type);
}