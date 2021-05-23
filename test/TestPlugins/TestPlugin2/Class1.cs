
using System;

using BEditor.Plugin;

namespace TestPlugin2
{
    public partial class TestPlugin2 : PluginObject
    {
        public TestPlugin2(PluginConfig config) : base(config)
        {

        }

        public override string PluginName => nameof(TestPlugin2);

        public override string Description => nameof(TestPlugin2);

        public override SettingRecord Settings { get; set; } = new Setting(0, 0.1f, "文字");

        public override Guid Id { get; } = Guid.Parse("A7064F63-D933-4149-8F28-B1EBCCE7825F");
    }
}