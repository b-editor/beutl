using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

using BEditor.Plugin;

namespace BEditor.Models.ManagePlugins
{
    public sealed class DummyPlugin : PluginObject
    {
        public DummyPlugin(PluginConfig config, string pluginName, string description, Guid id, Assembly assembly)
            : base(config)
        {
            PluginName = pluginName;
            Description = description;
            Id = id;
            Assembly = assembly;
        }

        public Assembly Assembly { get; }

        public override string PluginName { get; }

        public override string Description { get; }

        public override Guid Id { get; }

        public override SettingRecord Settings { get; set; } = new();
    }
}
