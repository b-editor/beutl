using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

using BEditor.Core.Data;
using BEditor.Core.Data.Primitive.Properties;
using BEditor.Core.Data.Property;
using BEditor.Core.Plugin;

namespace TestPlugin2
{
    public class TestPlugin2 : IPlugin, IEffects
    {
        public string PluginName => nameof(TestPlugin2);
        public string Infomation => nameof(TestPlugin2);
        public IEnumerable<EffectMetadata> Effects => new EffectMetadata[]
        {
            new()
            {
                Type = typeof(TestEffect),
                CreateFunc = () => new TestEffect(),
                Name = nameof(TestEffect)
            }
        };

        public void SettingCommand() => throw new NotImplementedException();
    }

    [DataContract(Namespace ="")]
    public class TestEffect : EffectElement
    {
        public static readonly CheckPropertyMetadata CheckMetadata = new("");

        public TestEffect()
        {
            Check = new(CheckMetadata);
        }

        public override string Name => nameof(TestEffect);
        public override IEnumerable<PropertyElement> Properties { get; }
        [DataMember]
        public CheckProperty Check { get; private set; }

        public override void Render(EffectRenderArgs args) { }
        public override void PropertyLoaded()
        {
            Check.PropertyLoaded();
            Check.PropertyMetadata = CheckMetadata;
        }
    }
}
