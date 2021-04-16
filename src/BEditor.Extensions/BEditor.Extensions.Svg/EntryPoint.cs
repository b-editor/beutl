
using BEditor.Data;
using BEditor.Plugin;

namespace BEditor.Extensions.Svg
{
    public class Plugin
    {
        public static void Register(string[] args)
        {
            PluginBuilder.Configure<SvgPlugin>()
                .With(ObjectMetadata.Create<SvgImage>("Svg‰æ‘œ"))
                .Register();
        }
    }
}