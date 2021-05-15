using System.ComponentModel;

using BEditor.Data;
using BEditor.Plugin;

using Microsoft.Extensions.DependencyInjection;

using Neo.IronLua;

namespace BEditor.Extensions.AviUtl
{
    public class Plugin : PluginObject
    {
        public Plugin(PluginConfig config) : base(config)
        {
        }

        public override string PluginName => "BEditor.Extensions.AviUtl";

        public override string Description => string.Empty;

        public override SettingRecord Settings { get; set; } = new CustomSettings();

        public static void Register(string[] args)
        {
            PluginBuilder.Configure<Plugin>()
                .ConfigureServices(s => s.AddSingleton(_ => LuaScript.LuaGlobal))
                .With(new EffectMetadata("AviUtl")
                {
                    Children = new[] { EffectMetadata.Create<LuaScript>("スクリプト制御") }
                })
                .Register();
        }
    }

    public record CustomSettings(
        [property: DisplayName("Y軸の値を反転する")]
        bool ReverseYAsis = true) : SettingRecord;
}