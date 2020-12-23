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
using BEditor.Drawing;
using BEditor.Drawing.Pixel;

namespace BEditor.Core.Data.Primitive.Effects
{
    [DataContract]
    public class TestEffect : ImageEffect
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

        public override void Render(EffectRenderArgs<Image<BGRA32>> args)
        {
            using var bgr = args.Value.Convert<BGRA32, BGR24>();
            var bgra = bgr.Convert<BGR24, BGRA32>();
            args.Value.Dispose();
            args.Value = bgra;
        }

        [DataContract]
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

                Button.Subscribe(_ =>
                {
                    Label.Text = "Clicked";
                });
            }
        }
    }
}
