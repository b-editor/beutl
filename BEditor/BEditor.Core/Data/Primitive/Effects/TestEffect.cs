using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Command;
using BEditor.Core.Data.Primitive.Components;
using BEditor.Core.Data.Primitive.Properties;
using BEditor.Core.Data.Property;

namespace BEditor.Core.Data.Primitive.Effects
{
    [DataContract(Namespace ="")]
    public class TestEffect : EffectElement
    {
        public static readonly FolderPropertyMetadata FolderMetadata = new("Folder", "");

        public TestEffect()
        {
            Folder = new(FolderMetadata);
            Dialog = new();
        }

        public override string Name => "TestEffect";
        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            Folder,
            Dialog
        };
        [DataMember]
        public FolderProperty Folder { get; private set; }
        [DataMember]
        public TestDialog Dialog { get; private set; }

        public override void Render(EffectRenderArgs args)
        {

        }
        public override void PropertyLoaded()
        {
            base.PropertyLoaded();
            Folder.ExecuteLoaded(FolderMetadata);
            Dialog.ExecuteLoaded(null);
        }

        [DataContract(Namespace ="")]
        public class TestDialog : DialogProperty
        {
            public TestDialog()
            {
                EaseProperty = new(DepthTest.FarMetadata);
                Label = new();
                Button = new(new PropertyElementMetadata("sssssss"));
            }

            public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
            {
                EaseProperty,
                Label,
                Button
            };
            [DataMember]
            public EaseProperty EaseProperty { get; private set; }
            [DataMember]
            public LabelComponent Label { get; private set; }
            [DataMember]
            public ButtonComponent Button { get; private set; }

            public override void PropertyLoaded()
            {
                base.PropertyLoaded();

                EaseProperty.ExecuteLoaded(DepthTest.FarMetadata);
                Label.ExecuteLoaded(null);
                Button.ExecuteLoaded(new PropertyElementMetadata("sssssss"));

                Button.Subscribe(() =>
                {
                    Label.Text = "Clicked";
                });
            }
        }
    }
}
