using System;
using System.ComponentModel;

using BEditor.Core.Plugin;

namespace TestPlugin1
{
    public class Plugin : IPlugin
    {
        public string PluginName => nameof(TestPlugin1);
        public string Description => nameof(TestPlugin1);

        public SettingRecord Settings { get; set; } = new();

        static void Register(string[] args)
        {
            PluginBuilder.Configure<Plugin>()
                .Register();
        }
    }
}
