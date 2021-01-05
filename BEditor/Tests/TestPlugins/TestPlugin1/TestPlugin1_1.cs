using System;

using BEditor.Core.Plugin;

namespace TestPlugin1
{
    public class TestPlugin1 : IPlugin
    {
        public string PluginName => nameof(TestPlugin1);
        public string Description => nameof(TestPlugin1);

        public void SettingCommand() => throw new NotImplementedException();
    }
}
