// Serialize.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.IO;
using System.Runtime.Serialization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using System.Threading.Tasks;

using BEditor.Data;

using Microsoft.Extensions.Logging;

namespace BEditor
{
    /// <summary>
    /// Represents a class that uses the <see cref="System.Text.Json"/> to provide methods for serialization, cloning, etc.
    /// </summary>
    public static class Serialize
    {
        internal static readonly JsonWriterOptions _options = new()
        {
            Indented = true,
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        };

        /// <summary>
        /// Reads and restores the contents of an object from a stream.
        /// </summary>
        /// <typeparam name="T">Type of the object to be loaded.</typeparam>
        /// <param name="stream">Stream to load.</param>
        /// <returns>Returns the restored object on success, <see langword="null"/> otherwise.</returns>
        public static async Task<T?> LoadFromStreamAsync<T>(Stream stream)
            where T : IJsonObject
        {
            try
            {
                stream.Position = 0;

                var obj = (T)FormatterServices.GetUninitializedObject(typeof(T));
                using var doc = await JsonDocument.ParseAsync(stream);
                obj.SetObjectData(doc.RootElement);

                return obj;
            }
            catch (Exception e)
            {
                Log(e);
                return default;
            }
        }

        /// <summary>
        /// Reads and restores the contents of an object from a stream.
        /// </summary>
        /// <typeparam name="T">Type of the object to be loaded.</typeparam>
        /// <param name="stream">Stream to load.</param>
        /// <returns>Returns the restored object on success, <see langword="null"/> otherwise.</returns>
        public static T? LoadFromStream<T>(Stream stream)
            where T : IJsonObject
        {
            try
            {
                stream.Position = 0;

                var obj = (T)FormatterServices.GetUninitializedObject(typeof(T));
                using var doc = JsonDocument.Parse(stream);
                obj.SetObjectData(doc.RootElement);

                return obj;
            }
            catch (Exception e)
            {
                Log(e);
                return default;
            }
        }

        /// <summary>
        /// Reads and restores the contents of an object from a file.
        /// </summary>
        /// <typeparam name="T">Type of the object to be loaded.</typeparam>
        /// <param name="path">The name of the file to load.</param>
        /// <returns>Returns the restored object on success, <see langword="null"/> otherwise.</returns>
        public static async Task<T?> LoadFromFileAsync<T>(string path)
            where T : IJsonObject
        {
            try
            {
                await using var stream = new FileStream(path, FileMode.Open);

                var obj = (T)FormatterServices.GetUninitializedObject(typeof(T));
                using var doc = await JsonDocument.ParseAsync(stream);
                obj.SetObjectData(doc.RootElement);

                return obj;
            }
            catch (Exception e)
            {
                Log(e);
                return default;
            }
        }

        /// <summary>
        /// Reads and restores the contents of an object from a file.
        /// </summary>
        /// <typeparam name="T">Type of the object to be loaded.</typeparam>
        /// <param name="path">The name of the file to load.</param>
        /// <returns>Returns the restored object on success, <see langword="null"/> otherwise.</returns>
        public static T? LoadFromFile<T>(string path)
            where T : IJsonObject
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open);

                var obj = (T)FormatterServices.GetUninitializedObject(typeof(T));
                using var doc = JsonDocument.Parse(stream);
                obj.SetObjectData(doc.RootElement);

                return obj;
            }
            catch (Exception e)
            {
                Log(e);
                return default;
            }
        }

        /// <summary>
        /// Save the contents of an object to a stream.
        /// </summary>
        /// <typeparam name="T">Type of the object to be saved.</typeparam>
        /// <param name="obj">The object to save.</param>
        /// <param name="stream">The stream to save to.</param>
        /// <returns>Returns <see langword="true"/> if the save was successful, <see langword="false"/> otherwise.</returns>
        public static async Task<bool> SaveToStreamAsync<T>(T obj, Stream stream)
            where T : IJsonObject
        {
            try
            {
                stream.Position = 0;
                await using var writer = new Utf8JsonWriter(stream, _options);

                writer.WriteStartObject();

                obj.GetObjectData(writer);

                writer.WriteEndObject();

                await writer.FlushAsync();

                return true;
            }
            catch (Exception e)
            {
                Log(e);
                return false;
            }
        }

        /// <summary>
        /// Save the contents of an object to a stream.
        /// </summary>
        /// <typeparam name="T">Type of the object to be saved.</typeparam>
        /// <param name="obj">The object to save.</param>
        /// <param name="stream">The stream to save to.</param>
        /// <returns>Returns <see langword="true"/> if the save was successful, <see langword="false"/> otherwise.</returns>
        public static bool SaveToStream<T>(T obj, Stream stream)
            where T : IJsonObject
        {
            try
            {
                stream.Position = 0;
                using var writer = new Utf8JsonWriter(stream, _options);

                writer.WriteStartObject();

                obj.GetObjectData(writer);

                writer.WriteEndObject();

                writer.Flush();

                return true;
            }
            catch (Exception e)
            {
                Log(e);
                return false;
            }
        }

        /// <summary>
        /// Saves the contents of an object to a file.
        /// </summary>
        /// <typeparam name="T">Type of the object to be saved.</typeparam>
        /// <param name="obj">The object to save.</param>
        /// <param name="path">The name of the file to save to.</param>
        /// <returns>Returns <see langword="true"/> if the save was successful, <see langword="false"/> otherwise.</returns>
        public static async Task<bool> SaveToFileAsync<T>(T obj, string path)
            where T : IJsonObject
        {
            try
            {
                await using var stream = new FileStream(path, FileMode.Create);
                await using var writer = new Utf8JsonWriter(stream, _options);

                writer.WriteStartObject();

                obj.GetObjectData(writer);

                writer.WriteEndObject();

                await writer.FlushAsync();

                return true;
            }
            catch (Exception e)
            {
                Log(e);
                return false;
            }
        }

        /// <summary>
        /// Saves the contents of an object to a file.
        /// </summary>
        /// <typeparam name="T">Type of the object to be saved.</typeparam>
        /// <param name="obj">The object to save.</param>
        /// <param name="path">The name of the file to save to.</param>
        /// <returns>Returns <see langword="true"/> if the save was successful, <see langword="false"/> otherwise.</returns>
        public static bool SaveToFile<T>(T obj, string path)
            where T : IJsonObject
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Create);
                using var writer = new Utf8JsonWriter(stream, _options);

                writer.WriteStartObject();

                obj.GetObjectData(writer);

                writer.WriteEndObject();

                writer.Flush();

                return true;
            }
            catch (Exception e)
            {
                Log(e);
                return false;
            }
        }

        /// <summary>
        /// DeepClone using <see cref="System.Text.Json"/>.
        /// </summary>
        /// <typeparam name="T">Type of the object to be clone.</typeparam>
        /// <param name="obj">The object to clone.</param>
        /// <returns>Returns the cloned object if it can be replicated, or <see langword="null"/> if it fails.</returns>
        public static async Task<T?> DeepCloneAsync<T>(this T obj)
            where T : IJsonObject
        {
            using var ms = new MemoryStream();
            return await SaveToStreamAsync(obj, ms) ? await LoadFromStreamAsync<T>(ms) : default;
        }

        /// <summary>
        /// DeepClone using <see cref="System.Text.Json"/>.
        /// </summary>
        /// <typeparam name="T">Type of the object to be clone.</typeparam>
        /// <param name="obj">The object to clone.</param>
        /// <returns>Returns the cloned object if it can be replicated, or <see langword="null"/> if it fails.</returns>
        public static T? DeepClone<T>(this T obj)
            where T : IJsonObject
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

        private static void Log(Exception e)
        {
            LogManager.Logger?.LogWarning(e, "Failed to serialize or deserialize.");
        }
    }
}