using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Command;
using BEditor.Core.Data.Property;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;

namespace BEditor.Core.Data.Primitive.Effects
{
#pragma warning disable CS1591
    [DataContract]
    public class TestEffect : ImageEffect
    {
        public static readonly FolderPropertyMetadata FolderMetadata = new("Folder");
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
        protected override void OnLoad()
        {
            Folder.Load(FolderMetadata);
            Value.Load(ValueMetadata);
            Dialog.Load();
        }
        protected override void OnUnload()
        {
            foreach (var pr in Children)
            {
                pr.Unload();
            }
        }

        public override void Render(EffectRenderArgs<Image<BGRA32>> args)
        {
            args.Value = Image.Text(Value.Value, FontProperty.FontList[0], 50, Color.Blue);
        }

        [DataContract]
        public class TestDialog : DialogProperty
        {
            private IDisposable? disposable;

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

            protected override void OnLoad()
            {
                EaseProperty.Load(DepthTest.FarMetadata);
                Label.Load();
                Button.Load(new PropertyElementMetadata("sssssss"));

                disposable = Button.Subscribe(_ =>
                {
                    Label.Text = "Clicked";
                });
            }
            protected override void OnUnload()
            {
                disposable?.Dispose();

                foreach (var pr in Children)
                {
                    pr.Unload();
                }
            }
        }
    }
#pragma warning restore CS1591
}
