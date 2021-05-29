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
        /// <param name="element">Data for this object in Json to be set.</param>
        public void SetObjectData(JsonElement element);
    }
}