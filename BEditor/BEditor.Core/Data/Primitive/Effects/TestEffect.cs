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
        public static readonly FolderPropertyMetadata FolderMetadata = new("Folder", null);
        public static readonly TextPropertyMetadata ValueMetadata = new("Value");

        public TestEffect()
        {
            Folder = new(FolderMetadata);
            Value = new(ValueMetadata);
            Dialog = new();
        }

        public override string Name => "TestEffect";
        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            Folder,
            Value,
            Dialog
        };
        [DataMember]
        public FolderProperty Folder { get; private set; }
        [DataMember]
        public TextProperty Value { get; private set; }
        [DataMember]
        public TestDialog Dialog { get; private set; }

        public override void Render(EffectRenderArgs args)
        {

        }
        public override void Loaded()
        {
            base.Loaded();
            Folder.ExecuteLoaded(FolderMetadata);
            Value.ExecuteLoaded(ValueMetadata);
            Dialog.ExecuteLoaded(null);
        }
        public override void Unloaded()
        {
            base.Unloaded();
            foreach (var pr in Children)
            {
                pr.Unloaded();
            }
        }

        public override void Render(EffectRenderArgs<Image<BGRA32>> args)
        {
            args.Value = Image.Text(Value.Value, FontProperty.FontList[0], 50, Color.Blue);
        }

        [DataContract]
        public class TestDialog : DialogProperty
        {
            private IDisposable disposable;

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

            public override void Loaded()
            {
                base.Loaded();

                EaseProperty.ExecuteLoaded(DepthTest.FarMetadata);
                Label.ExecuteLoaded(null);
                Button.ExecuteLoaded(new PropertyElementMetadata("sssssss"));

                disposable = Button.Subscribe(_ =>
                {
                    Label.Text = "Clicked";
                });
            }
            public override void Unloaded()
            {
                base.Unloaded();
                disposable?.Dispose();

                foreach (var pr in Children)
                {
                    pr.Unloaded();
                }
            }
        }
    }
}
