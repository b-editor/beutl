using System;
using System.Collections.Generic;

using BEditor.Core.Plugin;

namespace TestPlugin2
{
    public partial class TestPlugin2 : IPlugin
    {
        public string PluginName => nameof(TestPlugin2);
        public string Description => nameof(TestPlugin2);

        public SettingRecord Settings { get; set; } = new Setting(0, 0.1f, "文字");
    }
}
