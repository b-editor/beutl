using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor;
using BEditor.Plugin;

using Microsoft.VisualBasic;

namespace BEditor.Extensions.AviUtl
{
    public class Plugin : PluginObject
    {
        private static Plugin? current;

        public Plugin(PluginConfig config) : base(config)
        {
            current = this;
        }

        public override string PluginName => "BEditor.Extensions.AviUtl";
        public override string Description => string.Empty;
        public override SettingRecord Settings { get; set; } = new SettingRecord();

        public static void Register(string[] args)
        {
            PluginBuilder.Configure<Plugin>()
                .Register();
        }
    }
}
