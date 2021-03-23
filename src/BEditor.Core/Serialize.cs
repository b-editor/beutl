using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

using BEditor.Data.Property;
using BEditor.Data.Property.PrimitiveGroup;
using BEditor.Data;
using System.Collections.ObjectModel;
using System.Xml;
using System.Diagnostics;
using BEditor.Data.Property.Easing;
using BEditor.Data.Primitive;
using System.Text.Json;

namespace BEditor
{
    /// <summary>
    /// Represents a class that uses the <see cref="System.Text.Json"/> to provide methods for serialization, cloning, etc.
    /// </summary>
    public static class Serialize
    {
        /// <summary>
        /// Reads and restores the contents of an object from a stream
        /// </summary>
        /// <param name="stream">Stream to load.</param>
        /// <param name="mode">This is the mode of serialization.</param>
        /// <returns>Returns the restored object on success, <see langword="null"/> otherwise.</returns>
        public static async Task<T?> LoadFromStreamAsync<T>(Stream stream, SerializeMode mode = SerializeMode.Binary) where T : IJsonObject
        {
            try
            {
                stream.Position = 0;

                var obj = (T)FormatterServices.GetUninitializedObject(typeof(T));
                using var doc = await JsonDocument.ParseAsync(stream);
                obj.SetObjectData(doc.RootElement);

                return obj;
            }
            catch
            {
                Debug.Assert(false);
                return default;
            }
        }
        /// <summary>
        /// Reads and restores the contents of an object from a stream
        /// </summary>
        /// <param name="stream">Stream to load.</param>
        /// <param name="mode">This is the mode of serialization.</param>
        /// <returns>Returns the restored object on success, <see langword="null"/> otherwise.</returns>
        public static T? LoadFromStream<T>(Stream stream, SerializeMode mode = SerializeMode.Binary) where T : IJsonObject
        {
            try
            {
                stream.Position = 0;

                var obj = (T)FormatterServices.GetUninitializedObject(typeof(T));
                using var doc = JsonDocument.Parse(stream);
                obj.SetObjectData(doc.RootElement);

                return obj;
            }
            catch
            {
                Debug.Assert(false);
                return default;
            }
        }

        /// <summary>
        /// Reads and restores the contents of an object from a file.
        /// </summary>
        /// <param name="path">The name of the file to load.</param>
        /// <param name="mode">This is the mode of serialization.</param>
        /// <returns>Returns the restored object on success, <see langword="null"/> otherwise.</returns>
        public static async Task<T?> LoadFromFileAsync<T>(string path, SerializeMode mode = SerializeMode.Binary) where T : IJsonObject
        {
            try
            {
                await using var stream = new FileStream(path, FileMode.Open);

                var obj = (T)FormatterServices.GetUninitializedObject(typeof(T));
                using var doc = await JsonDocument.ParseAsync(stream);
                obj.SetObjectData(doc.RootElement);

                return obj;
            }
            catch
            {
                Debug.Assert(false);
                return default;
            }
        }
        /// <summary>
        /// Reads and restores the contents of an object from a file.
        /// </summary>
        /// <param name="path">The name of the file to load.</param>
        /// <param name="mode">This is the mode of serialization.</param>
        /// <returns>Returns the restored object on success, <see langword="null"/> otherwise.</returns>
        public static T? LoadFromFile<T>(string path, SerializeMode mode = SerializeMode.Binary) where T : IJsonObject
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open);

                var obj = (T)FormatterServices.GetUninitializedObject(typeof(T));
                using var doc = JsonDocument.Parse(stream);
                obj.SetObjectData(doc.RootElement);

                return obj;
            }
            catch
            {
                Debug.Assert(false);
                return default;
            }
        }

        /// <summary>
        /// Save the contents of an object to a stream.
        /// </summary>
        /// <param name="obj">The object to save.</param>
        /// <param name="stream">The stream to save to.</param>
        /// <param name="mode">This is the mode of serialization.</param>
        public static async Task<bool> SaveToStreamAsync<T>(T obj, Stream stream, SerializeMode mode = SerializeMode.Binary) where T : IJsonObject
        {
            try
            {
                stream.Position = 0;
                await using var writer = new Utf8JsonWriter(stream, new() { Indented = true });

                writer.WriteStartObject();
                {
                    obj.GetObjectData(writer);
                }
                writer.WriteEndObject();

                await writer.FlushAsync();

                return true;
            }
            catch
            {
                Debug.Assert(false);
                return false;
            }
        }
        /// <summary>
        /// Save the contents of an object to a stream.
        /// </summary>
        /// <param name="obj">The object to save.</param>
        /// <param name="stream">The stream to save to.</param>
        /// <param name="mode">This is the mode of serialization.</param>
        public static bool SaveToStream<T>(T obj, Stream stream, SerializeMode mode = SerializeMode.Binary) where T : IJsonObject
        {
            try
            {
                stream.Position = 0;
                using var writer = new Utf8JsonWriter(stream, new() { Indented = true });

                writer.WriteStartObject();
                {
                    obj.GetObjectData(writer);
                }
                writer.WriteEndObject();

                writer.Flush();

                return true;
            }
            catch
            {
                Debug.Assert(false);
                return false;
            }
        }

        /// <summary>
        /// Saves the contents of an object to a file.
        /// </summary>
        /// <param name="obj">The object to save.</param>
        /// <param name="path">The name of the file to save to.</param>
        /// <param name="mode">This is the mode of serialization.</param>
        public static async Task<bool> SaveToFileAsync<T>(T obj, string path, SerializeMode mode = SerializeMode.Binary) where T : IJsonObject
        {
            try
            {
                await using var stream = new FileStream(path, FileMode.Create);
                await using var writer = new Utf8JsonWriter(stream, new() { Indented = true });

                writer.WriteStartObject();
                {
                    obj.GetObjectData(writer);
                }
                writer.WriteEndObject();

                await writer.FlushAsync();

                return true;
            }
            catch
            {
                Debug.Assert(false);
                return false;
            }
        }
        
        /// <summary>
        /// Saves the contents of an object to a file.
        /// </summary>
        /// <param name="obj">The object to save.</param>
        /// <param name="path">The name of the file to save to.</param>
        /// <param name="mode">This is the mode of serialization.</param>
        public static bool SaveToFile<T>(T obj, string path, SerializeMode mode = SerializeMode.Binary) where T : IJsonObject
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Create);
                using var writer = new Utf8JsonWriter(stream, new() { Indented = true });

                writer.WriteStartObject();
                {
                    obj.GetObjectData(writer);
                }
                writer.WriteEndObject();

                writer.Flush();

                return true;
            }
            catch
            {
                Debug.Assert(false);
                return false;
            }
        }

        /// <summary>
        /// DeepClone using <see cref="System.Text.Json"/>.
        /// </summary>
        public static async Task<T?> DeepCloneAsync<T>(this T obj) where T : IJsonObject
        {
            using var ms = new MemoryStream();
            if (await SaveToStreamAsync(obj, ms))
            {
                return await LoadFromStreamAsync<T>(ms);
            }
            else
            {
                return default;
            }
        }
        /// <summary>
        /// DeepClone using <see cref="System.Text.Json"/>.
        /// </summary>
        public static T? DeepClone<T>(this T obj) where T : IJsonObject
        {
            using var ms = new MemoryStream();
            if (SaveToStream(obj, ms))
            {
                return LoadFromStream<T>(ms);
            }
            else
            {
                return default;
            }
        }
    }

    /// <summary>
    /// Represents the mode of serialization.
    /// </summary>
    public enum SerializeMode
    {
        /// <summary>
        /// The binary.
        /// </summary>
        Binary,
        /// <summary>
        /// The json.
        /// </summary>
        Json
    }
}
