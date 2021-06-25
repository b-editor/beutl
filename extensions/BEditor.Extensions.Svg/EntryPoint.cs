
using BEditor.Data;
using BEditor.Plugin;

namespace BEditor.Extensions.Svg
{
    public class Plugin
    {
        public static void Register()
        {
            PluginBuilder.Configure<SvgPlugin>()
                .With(ObjectMetadata.Create<SvgImage>("Svg‰æ‘œ", null))
                .Register();
        }
    }
}