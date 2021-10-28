using System;

using BEditor.Data;
using BEditor.Plugin;

namespace BEditor.Languages.Chinese
{
    public class Plugin : PluginObject
    {
        public Plugin(PluginConfig config)
            : base(config)
        {
        }

        public override string PluginName => "BEditor.Languages.Chinese";

        public override string Description => "中文插件";

        public override Guid Id { get; } = Guid.Parse("{E0F33915-6E45-4486-938F-4E246511B0B3}");

        public override SettingRecord Settings { get; set; } = new();

        public static void Register()
        {
            PluginBuilder.Configure<Plugin>()
                .Language(new(
                    new System.Globalization.CultureInfo("zh-CN"),
                    "中文(机器翻译)",
                    "BEditor.Languages.Chinese.Strings",
                    typeof(Plugin).Assembly))
                .Register();
        }
    }
}
