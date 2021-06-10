// PropertyTest.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System.Collections.Generic;
using System.Text.Json;

using BEditor.Data;
using BEditor.Data.Property;
using BEditor.Drawing;

#nullable disable

namespace BEditor.Primitive.Objects
{
    internal class PropertyTest : ObjectElement
    {
        public static readonly EditingProperty<ButtonComponent> ButtonProperty = EditingProperty.RegisterDirect<ButtonComponent, PropertyTest>(
            nameof(Button),
            owner => owner.Button,
            (owner, obj) => owner.Button = obj,
            EditingPropertyOptions<ButtonComponent>.Create(new ButtonComponentMetadata("Button")).Serialize());

        public static readonly EditingProperty<CheckProperty> CheckProperty = EditingProperty.RegisterDirect<CheckProperty, PropertyTest>(
            nameof(Check),
            owner => owner.Check,
            (owner, obj) => owner.Check = obj,
            EditingPropertyOptions<CheckProperty>.Create(new CheckPropertyMetadata("Check")).Serialize());

        public static readonly EditingProperty<ColorAnimationProperty> ColorAnimationProperty = EditingProperty.RegisterDirect<ColorAnimationProperty, PropertyTest>(
            nameof(ColorAnimation),
            owner => owner.ColorAnimation,
            (owner, obj) => owner.ColorAnimation = obj,
            EditingPropertyOptions<ColorAnimationProperty>.Create(new ColorAnimationPropertyMetadata("ColorAnimation")).Serialize());

        public static readonly EditingProperty<ColorProperty> ColorProperty = EditingProperty.RegisterDirect<ColorProperty, PropertyTest>(
            nameof(Color),
            owner => owner.Color,
            (owner, obj) => owner.Color = obj,
            EditingPropertyOptions<ColorProperty>.Create(new ColorPropertyMetadata("Color", Colors.White)).Serialize());

        public static readonly EditingProperty<DocumentProperty> DocumentProperty = EditingProperty.RegisterDirect<DocumentProperty, PropertyTest>(
            nameof(Document),
            owner => owner.Document,
            (owner, obj) => owner.Document = obj,
            EditingPropertyOptions<DocumentProperty>.Create(new DocumentPropertyMetadata(string.Empty)).Serialize());

        public static readonly EditingProperty<EaseProperty> EaseProperty = EditingProperty.RegisterDirect<EaseProperty, PropertyTest>(
            nameof(Ease),
            owner => owner.Ease,
            (owner, obj) => owner.Ease = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata("Ease")).Serialize());

        public static readonly EditingProperty<FileProperty> FileProperty = EditingProperty.RegisterDirect<FileProperty, PropertyTest>(
            nameof(File),
            owner => owner.File,
            (owner, obj) => owner.File = obj,
            EditingPropertyOptions<FileProperty>.Create(new FilePropertyMetadata("File")).Serialize());

        public static readonly EditingProperty<FolderProperty> FolderProperty = EditingProperty.RegisterDirect<FolderProperty, PropertyTest>(
            nameof(Folder),
            owner => owner.Folder,
            (owner, obj) => owner.Folder = obj,
            EditingPropertyOptions<FolderProperty>.Create(new FolderPropertyMetadata("Folder")).Serialize());

        public static readonly EditingProperty<LabelComponent> LabelProperty = EditingProperty.RegisterDirect<LabelComponent, PropertyTest>(
            nameof(Label),
            owner => owner.Label,
            (owner, obj) => owner.Label = obj,
            EditingPropertyOptions<LabelComponent>.Create(new LabelComponentMetadata()).Serialize());

        public static readonly EditingProperty<SelectorProperty> SelectorProperty = EditingProperty.RegisterDirect<SelectorProperty, PropertyTest>(
            nameof(Selector),
            owner => owner.Selector,
            (owner, obj) => owner.Selector = obj,
            EditingPropertyOptions<SelectorProperty>.Create(new SelectorPropertyMetadata("Selector", new string[] { "One", "Two", "Three" })).Serialize());

        public static readonly EditingProperty<SelectorProperty<StringWrapper>> SelectorGenProperty = EditingProperty.RegisterDirect<SelectorProperty<StringWrapper>, PropertyTest>(
            nameof(SelectorGen),
            owner => owner.SelectorGen,
            (owner, obj) => owner.SelectorGen = obj,
            EditingPropertyOptions<SelectorProperty<StringWrapper>>
                .Create(new SelectorPropertyMetadata<StringWrapper>(
                    "SelectorGen",
                    new StringWrapper[] { new("One"), new("Two"), new("Three") }, i => i.Value))
                .Serialize());

        public static readonly EditingProperty<TextProperty> TextProperty = EditingProperty.RegisterDirect<TextProperty, PropertyTest>(
            nameof(Text),
            owner => owner.Text,
            (owner, obj) => owner.Text = obj,
            EditingPropertyOptions<TextProperty>.Create(new TextPropertyMetadata("Text")).Serialize());

        public static readonly EditingProperty<ValueProperty> ValueProperty = EditingProperty.RegisterDirect<ValueProperty, PropertyTest>(
            nameof(Value),
            owner => owner.Value,
            (owner, obj) => owner.Value = obj,
            EditingPropertyOptions<ValueProperty>.Create(new ValuePropertyMetadata("Value")).Serialize());

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
            yield return Button;
            yield return Check;
            yield return ColorAnimation;
            yield return Color;
            yield return Document;
            yield return Ease;
            yield return File;
            yield return Folder;
            yield return Label;
            yield return Selector;
            yield return SelectorGen;
            yield return Text;
            yield return Value;
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