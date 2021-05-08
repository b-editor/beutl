using System.Collections.Generic;
using System.Text.Json;

using BEditor.Data;
using BEditor.Data.Property;

#nullable disable

namespace BEditor.Primitive.Objects
{
    internal class PropertyTest : ObjectElement
    {
        public static readonly EditingProperty<ButtonComponent> ButtonProperty = EditingProperty.RegisterSerializeDirect<ButtonComponent, PropertyTest>(
            nameof(Button), owner => owner.Button, (owner, obj) => owner.Button = obj,
            new ButtonComponentMetadata("Button"));

        public static readonly EditingProperty<CheckProperty> CheckProperty = EditingProperty.RegisterSerializeDirect<CheckProperty, PropertyTest>(
            nameof(Check), owner => owner.Check, (owner, obj) => owner.Check = obj,
            new CheckPropertyMetadata("Check"));

        public static readonly EditingProperty<ColorAnimationProperty> ColorAnimationProperty = EditingProperty.RegisterSerializeDirect<ColorAnimationProperty, PropertyTest>(
            nameof(ColorAnimation), owner => owner.ColorAnimation, (owner, obj) => owner.ColorAnimation = obj,
            new ColorAnimationPropertyMetadata("ColorAnimation"));

        public static readonly EditingProperty<ColorProperty> ColorProperty = EditingProperty.RegisterSerializeDirect<ColorProperty, PropertyTest>(
            nameof(Color), owner => owner.Color, (owner, obj) => owner.Color = obj,
            new ColorPropertyMetadata("Color", Drawing.Color.Light));

        public static readonly EditingProperty<DocumentProperty> DocumentProperty = EditingProperty.RegisterSerializeDirect<DocumentProperty, PropertyTest>(
            nameof(Document), owner => owner.Document, (owner, obj) => owner.Document = obj,
            new DocumentPropertyMetadata(string.Empty));

        public static readonly EditingProperty<EaseProperty> EaseProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, PropertyTest>(
            nameof(Ease), owner => owner.Ease, (owner, obj) => owner.Ease = obj,
            new EasePropertyMetadata("Ease"));

        public static readonly EditingProperty<FileProperty> FileProperty = EditingProperty.RegisterSerializeDirect<FileProperty, PropertyTest>(
            nameof(File), owner => owner.File, (owner, obj) => owner.File = obj,
            new FilePropertyMetadata("File"));

        public static readonly EditingProperty<FolderProperty> FolderProperty = EditingProperty.RegisterSerializeDirect<FolderProperty, PropertyTest>(
            nameof(Folder), owner => owner.Folder, (owner, obj) => owner.Folder = obj,
            new FolderPropertyMetadata("Folder"));

        public static readonly EditingProperty<LabelComponent> LabelProperty = EditingProperty.RegisterSerializeDirect<LabelComponent, PropertyTest>(
            nameof(Label), owner => owner.Label, (owner, obj) => owner.Label = obj,
            new LabelComponentMetadata());

        public static readonly EditingProperty<SelectorProperty> SelectorProperty = EditingProperty.RegisterSerializeDirect<SelectorProperty, PropertyTest>(
            nameof(Selector), owner => owner.Selector, (owner, obj) => owner.Selector = obj,
            new SelectorPropertyMetadata("Selector", new string[] { "One", "Two", "Three" }));

        public static readonly EditingProperty<SelectorProperty<StringWrapper>> SelectorGenProperty = EditingProperty.RegisterSerializeDirect<SelectorProperty<StringWrapper>, PropertyTest>(
            nameof(SelectorGen), owner => owner.SelectorGen, (owner, obj) => owner.SelectorGen = obj,
            new SelectorPropertyMetadata<StringWrapper>(
                "SelectorGen", new StringWrapper[] { new("One"), new("Two"), new("Three") }, i => i.Value));

        public static readonly EditingProperty<TextProperty> TextProperty = EditingProperty.RegisterSerializeDirect<TextProperty, PropertyTest>(
            nameof(Text), owner => owner.Text, (owner, obj) => owner.Text = obj,
            new TextPropertyMetadata("Text"));

        public static readonly EditingProperty<ValueProperty> ValueProperty = EditingProperty.RegisterSerializeDirect<ValueProperty, PropertyTest>(
            nameof(Value), owner => owner.Value, (owner, obj) => owner.Value = obj,
            new ValuePropertyMetadata("Value"));

        public PropertyTest()
        {
        }

        public override string Name => nameof(PropertyTest);
        public ButtonComponent Button { get; set; }
        public CheckProperty Check { get; set; }
        public ColorAnimationProperty ColorAnimation { get; set; }
        public ColorProperty Color { get; set; }
        public DocumentProperty Document { get; set; }
        public EaseProperty Ease { get; set; }
        public FileProperty File { get; set; }
        public FolderProperty Folder { get; set; }
        public LabelComponent Label { get; set; }
        public SelectorProperty Selector { get; set; }
        public SelectorProperty<StringWrapper> SelectorGen { get; set; }
        public TextProperty Text { get; set; }
        public ValueProperty Value { get; set; }

        public override void Apply(EffectApplyArgs args)
        {
        }

        public override IEnumerable<PropertyElement> GetProperties()
        {
            return new PropertyElement[]
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