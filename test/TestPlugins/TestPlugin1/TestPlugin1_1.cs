using System;
using System.ComponentModel;

using BEditor.Plugin;

namespace TestPlugin1
{
    public class Plugin : PluginObject
    {
        public Plugin(PluginConfig config) : base(config)
        {

        }

        public override string PluginName => nameof(TestPlugin1);
        public override string Description => nameof(TestPlugin1);

        public override SettingRecord Settings { get; set; } = new();

        static void Register(string[] args)
        {
            PluginBuilder.Configure<Plugin>()
                .Register();
        }
    }
}