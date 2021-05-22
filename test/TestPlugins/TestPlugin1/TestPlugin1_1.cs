
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

        public override SettingRecord Settings { get; set; } = new Settings("", 0, 0, false);

        public static void Register()
        {
            PluginBuilder.Configure<Plugin>()
                .Register();
        }
    }

    public record Settings(string String, int Integer, float Float, bool Boolean) : SettingRecord;
}