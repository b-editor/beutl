using System;

using BEditor.Core.Plugin;

namespace TestPlugin1
{
    public class TestPlugin1_1 : IPlugin
    {
        public string PluginName => nameof(TestPlugin1_1);
        public string Infomation => nameof(TestPlugin1_1);

        public void SettingCommand() => throw new NotImplementedException();
    }
    public class TestPlugin1_2 : IPlugin
    {
        public string PluginName => nameof(TestPlugin1_2);
        public string Infomation => nameof(TestPlugin1_2);

        public void SettingCommand() => throw new NotImplementedException();
    }
}
