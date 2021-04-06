using System;

using BEditor.Plugin;

namespace BEditor.Extensions.Svg
{
    public class SvgPlugin : PluginObject
    {
        public SvgPlugin(PluginConfig config) : base(config)
        {
        }

        public override string PluginName => "BEditor.Extensions.Svg";

        public override string Description => "Svg画像を読み込む拡張機能です。";

        public override SettingRecord Settings { get; set; } = new();
    }
}
