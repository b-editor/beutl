// PropertyJsonSerializer.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System.Runtime.Serialization;
using System.Text.Json;

namespace BEditor.Data.Internals
{
    internal class PropertyJsonSerializer<TValue> : IEditingPropertySerializer<TValue>
        where TValue : IJsonObject
    {
        public static readonly PropertyJsonSerializer<TValue> Current = new();

        public TValue Read(DeserializeContext context)
        {
            var value = (TValue)FormatterServices.GetUninitializedObject(typeof(TValue));
            value.SetObjectData(context);

            return value;
        }

        public void Write(Utf8JsonWriter writer, TValue value)
        {
            writer.WriteStartObject();
            value.GetObjectData(writer);
            writer.WriteEndObject();
        }
    }
}