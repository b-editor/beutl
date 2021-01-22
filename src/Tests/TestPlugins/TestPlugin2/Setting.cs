using System.ComponentModel;

using BEditor.Core.Plugin;

namespace TestPlugin2
{
    public record Setting(
        [property: DisplayName("整数")]
        int integer,
        [property: DisplayName("浮動小数点")]
        float single,
        [property: DisplayName("文字列")]
        string text) : SettingRecord;
}
