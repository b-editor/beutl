
using BEditor.Data;
using BEditor.Plugin;

namespace TestPlugin2
{
    public class Plugin
    {
        public static void Register(string[] args)
        {
            PluginBuilder.Configure<TestPlugin2>()
                .With(new EffectMetadata(nameof(TestPlugin2))
                {
                    Children = new EffectMetadata[]
                    {
                        new EffectMetadata(nameof(TestEffect), () => new TestEffect())
                    }
                })
                .SetCustomMenu("メニュー", new ICustomMenu[]
                {
                    new CustomMenu("Hello World", () => { /*Message.Snackbar("Hello World");*/ }),
                    new CustomMenu("Hello Dialog", () => { /*Message.Dialog("Hello Dialog");*/ })
                })
                .Register();
        }
    }
}
