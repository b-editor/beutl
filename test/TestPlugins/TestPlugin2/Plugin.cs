using BEditor.Plugin;

namespace TestPlugin2
{
    public class Plugin
    {
        public static void Register()
        {
            PluginBuilder.Configure<TestPlugin2>()
                .SetCustomMenu("メニュー", new ICustomMenu[]
                {
                    new CustomMenu("Hello World", () => { /*Message.Snackbar("Hello World");*/ }),
                    new CustomMenu("Hello Dialog", () => { /*Message.Dialog("Hello Dialog");*/ })
                })
                .Register();
        }
    }
}