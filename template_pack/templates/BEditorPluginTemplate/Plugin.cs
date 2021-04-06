using System;

using BEditor.Plugin;

namespace BEditorPluginTemplate
{
    public class Plugin : PluginObject
    {
        public Plugin(PluginConfig config) : base(config)
        {

        }

        public override string PluginName => "Plugin name";
        public override string Description => "Description";

        public override SettingRecord Settings { get; set; } = new();

        static void Register(string[] args)
        {
            PluginBuilder.Configure<Plugin>()
                .Register();
        }
    }
}