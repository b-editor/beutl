using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using BEditor.Data;
using BEditor.Data.Property;

namespace BEditor.Primitive.Objects
{
    internal class PropertyTest : ObjectElement
    {
        public static readonly ButtonComponentMetadata ButtonMetadata = new("Button");
        public static readonly CheckPropertyMetadata CheckMetadata = new("Check");
        public static readonly ColorAnimationPropertyMetadata ColorAnimationMetadata = new("ColorAnimation");
        public static readonly ColorPropertyMetadata ColorMetadata = new("Color", Drawing.Color.Light);
        public static readonly DocumentPropertyMetadata DocumentMetadata = new(string.Empty);
        public static readonly EasePropertyMetadata EaseMetadata = new("Ease");
        public static readonly FilePropertyMetadata FileMetadata = new("File");
        public static readonly FolderPropertyMetadata FolderMetadata = new("Folder");
        //public static readonly LabelComponentMetadata LabelMetadata = new();
        public static readonly SelectorPropertyMetadata SelectorMetadata = new("Selector", new string[] { "One", "Two", "Three" });
        public static readonly SelectorPropertyMetadata<StringWrapper> SelectorGenMetadata = new(
            "SelectorGen",
            new StringWrapper[] { new("One"), new("Two"), new("Three") },
            i => i.Value);
        public static readonly TextPropertyMetadata TextMetadata = new("Text");
        public static readonly ValuePropertyMetadata ValueMetadata = new("Value");

        public PropertyTest()
        {
            Button = new(ButtonMetadata);
            Check = new(CheckMetadata);
            ColorAnimation = new(ColorAnimationMetadata);
            Color = new(ColorMetadata);
            Document = new(DocumentMetadata);
            Ease = new(EaseMetadata);
            File = new(FileMetadata);
            Folder = new(FolderMetadata);
            Label = new();
            Selector = new(SelectorMetadata);
            SelectorGen = new(SelectorGenMetadata);
            Text = new(TextMetadata);
            Value = new(ValueMetadata);
        }

        public override string Name => nameof(PropertyTest);
        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            Button,
            Check,
            ColorAnimation,
            Color,
            Document,
            Ease,
            File,
            Folder,
            Label,
            Selector,
            SelectorGen,
            Text,
            Value
        };
        [DataMember]
        public ButtonComponent Button { get; set; }
        [DataMember]
        public CheckProperty Check { get; set; }
        [DataMember]
        public ColorAnimationProperty ColorAnimation { get; set; }
        [DataMember]
        public ColorProperty Color { get; set; }
        [DataMember]
        public DocumentProperty Document { get; set; }
        [DataMember]
        public EaseProperty Ease { get; set; }
        [DataMember]
        public FileProperty File { get; set; }
        [DataMember]
        public FolderProperty Folder { get; set; }
        [DataMember]
        public LabelComponent Label { get; set; }
        [DataMember]
        public SelectorProperty Selector { get; set; }
        [DataMember]
        public SelectorProperty<StringWrapper> SelectorGen { get; set; }
        [DataMember]
        public TextProperty Text { get; set; }
        [DataMember]
        public ValueProperty Value { get; set; }

        public override void Render(EffectRenderArgs args)
        {
        }

        protected override void OnLoad()
        {
            base.OnLoad();
            Button.Load(ButtonMetadata);
            Check.Load(CheckMetadata);
            ColorAnimation.Load(ColorAnimationMetadata);
            Color.Load(ColorMetadata);
            Document.Load(DocumentMetadata);
            Ease.Load(EaseMetadata);
            File.Load(FileMetadata);
            Folder.Load(FolderMetadata);
            Label.Load();
            Selector.Load(SelectorMetadata);
            SelectorGen.Load(SelectorGenMetadata);
            Text.Load(TextMetadata);
            Value.Load(ValueMetadata);
        }

        protected override void OnUnload()
        {
            base.OnUnload();
            Button.Unload();
            Check.Unload();
            ColorAnimation.Unload();
            Color.Unload();
            Document.Unload();
            Ease.Unload();
            File.Unload();
            Folder.Unload();
            Label.Unload();
            Selector.Unload();
            SelectorGen.Unload();
            Text.Unload();
            Value.Unload();
        }
    }

    internal record StringWrapper : IJsonObject
    {
        public StringWrapper(string value)
        {
            Value = value;
        }

        public string Value { get; set; }

        public void GetObjectData(Utf8JsonWriter writer)
        {
            writer.WriteStringValue(Value);
        }

        public void SetObjectData(JsonElement element)
        {
            Value = element.GetString() ?? string.Empty;
        }
    }
}
