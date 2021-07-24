// ColorPalette.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using BEditor.Data;

namespace BEditor.Drawing
{
    /// <summary>
    /// The color palette.
    /// </summary>
    public sealed class ColorPalette : EditingObject
    {
        /// <summary>
        /// Defines the <see cref="Colors"/> property.
        /// </summary>
        public static readonly EditingProperty<Dictionary<string, Color>> ColorsProperty
            = EditingProperty.Register<Dictionary<string, Color>, ColorPalette>(
                nameof(Colors),
                EditingPropertyOptions<Dictionary<string, Color>>.Create().Initialize(InitializeDictionary).Serialize(WriteDictionary, ReadDictionary));

        /// <summary>
        /// Defines the <see cref="Name"/> property.
        /// </summary>
        public static readonly EditingProperty<string?> NameProperty
            = EditingProperty.Register<string?, ColorPalette>(nameof(Name), EditingPropertyOptions<string?>.Create().Notify(true));

        /// <summary>
        /// Gets the colors of this color palette.
        /// </summary>
        public Dictionary<string, Color> Colors => GetValue(ColorsProperty);

        /// <summary>
        /// Gets or sets the name of this color palette.
        /// </summary>
        public string? Name
        {
            get => GetValue(NameProperty);
            set => SetValue(NameProperty, value);
        }

        /// <inheritdoc/>
        public override void GetObjectData(Utf8JsonWriter writer)
        {
            ColorsProperty.Serializer!.Write(writer, Colors);
        }

        /// <inheritdoc/>
        public override void SetObjectData(JsonElement element)
        {
            Id = Guid.NewGuid();
            SetValue(ColorsProperty, ColorsProperty.Serializer!.Read(element));
        }

        private static void WriteDictionary(Utf8JsonWriter arg1, Dictionary<string, Color> arg2)
        {
            arg1.WriteStartArray();

            foreach (var (key, value) in arg2)
            {
                arg1.WriteStartObject();
                arg1.WriteString("Name", key);
                arg1.WriteString("Color", value.ToString("#argb"));
                arg1.WriteEndObject();
            }

            arg1.WriteEndArray();
        }

        private static Dictionary<string, Color> ReadDictionary(JsonElement arg)
        {
            return arg.EnumerateArray()
                .Select(i =>
                {
                    var color = i.TryGetProperty("Color", out var colorElm)
                        ? Color.Parse(colorElm.GetString())
                        : Drawing.Colors.White;

                    var name = (i.TryGetProperty("Name", out var nameElm)
                        ? nameElm.GetString()
                        : color.ToString("#argb")) ?? color.ToString("#argb");

                    return new KeyValuePair<string, Color>(name, color);
                })
                .ToDictionary(i => i.Key, i => i.Value);
        }

        private static Dictionary<string, Color> InitializeDictionary()
        {
            return new();
        }
    }
}
