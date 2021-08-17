// IJsonObject.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System.Text.Json;

namespace BEditor.Data
{
    /// <summary>
    /// Represents the object that can be stored in Json.
    /// </summary>
    public interface IJsonObject
    {
        /// <summary>
        /// Get the Object data from json.
        /// </summary>
        /// <param name="writer">Write the data of this object <see cref="Utf8JsonWriter"/>.</param>
        public void GetObjectData(Utf8JsonWriter writer);

        /// <summary>
        /// Set the Json data to this object.
        /// </summary>
        /// <param name="context">Data for this object in Json to be set.</param>
        public void SetObjectData(DeserializeContext context);
    }

    /// <summary>
    /// The deserialize context.
    /// </summary>
    public struct DeserializeContext
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DeserializeContext"/> struct.
        /// </summary>
        /// <param name="element">The json element.</param>
        /// <param name="parent">The parent.</param>
        public DeserializeContext(JsonElement element, object? parent = null)
        {
            Element = element;
            Parent = parent;
            Version = string.Empty;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DeserializeContext"/> struct.
        /// </summary>
        /// <param name="element">The json element.</param>
        /// <param name="parent">The parent.</param>
        /// <param name="version">The version.</param>
        public DeserializeContext(JsonElement element, object? parent, string version)
        {
            Element = element;
            Parent = parent;
            Version = version;
        }

        /// <summary>
        /// Gets or sets the json element.
        /// </summary>
        public JsonElement Element { get; set; }

        /// <summary>
        /// Gets or sets the version.
        /// </summary>
        public string Version { get; set; }

        /// <summary>
        /// Gets or sets the parent.
        /// </summary>
        public object? Parent { get; set; }

        /// <summary>
        ///  Returns a new <see cref="DeserializeContext"/> with the specified json element.
        /// </summary>
        /// <param name="element">The json element.</param>
        /// <returns>The new <see cref="DeserializeContext"/>.</returns>
        public DeserializeContext WithElement(JsonElement element)
        {
            var obj = this;
            obj.Element = element;
            return obj;
        }

        /// <summary>
        ///  Returns a new <see cref="DeserializeContext"/> with the specified parent.
        /// </summary>
        /// <param name="parent">The parent.</param>
        /// <returns>The new <see cref="DeserializeContext"/>.</returns>
        public DeserializeContext WithParent(object? parent)
        {
            var obj = this;
            obj.Parent = parent;
            return obj;
        }
    }
}