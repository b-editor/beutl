// PaletteRegistry.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

using Microsoft.Extensions.Logging;

namespace BEditor.Drawing
{
    /// <summary>
    /// Tracks registered <see cref="ColorPalette"/> instances.
    /// </summary>
    public static class PaletteRegistry
    {
        private static readonly List<ColorPalette> _registered = new();
        private static readonly string _path = Path.Combine(ServicesLocator.GetUserFolder(), "color_palettes");

        /// <summary>
        /// Gets all registered <see cref="ColorPalette"/>s.
        /// </summary>
        /// <returns>A collection of <see cref="ColorPalette"/>.</returns>
        public static IReadOnlyList<ColorPalette> GetRegistered()
        {
            return _registered;
        }

        /// <summary>
        /// Finds a registered palette by Id.
        /// </summary>
        /// <param name="id">The paletteId.</param>
        /// <returns>The registered palette or null if no matching palette found.</returns>
        public static ColorPalette? FindRegistered(Guid id)
        {
            foreach (var item in _registered)
            {
                if (item.Id == id) return item;
            }

            return null;
        }

        /// <summary>
        /// Removes a registered palette by id.
        /// </summary>
        /// <param name="id">The id.</param>
        /// <returns>True if the color palette is removed, otherwise false.</returns>
        public static bool RemoveRegistered(Guid id)
        {
            var item = FindRegistered(id);
            if (item is not null)
            {
                _registered.Remove(item);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Checks whether a <see cref="ColorPalette"/> is registered.
        /// </summary>
        /// <param name="id">The id.</param>
        /// <returns>True if the color palette is registered, otherwise false.</returns>
        public static bool IsRegistered(Guid id)
        {
            foreach (var item in _registered)
            {
                if (item.Id == id) return true;
            }

            return false;
        }

        /// <summary>
        /// Registers a <see cref="ColorPalette"/>.
        /// </summary>
        /// <param name="palette">The color palette.</param>
        public static void Register(ColorPalette palette)
        {
            if (palette is null) throw new ArgumentNullException(nameof(palette));

            _registered.Add(palette);
        }

        /// <summary>
        /// Loads a palette from the specified directory.
        /// </summary>
        /// <param name="directory">The directory where palettes are stored.</param>
        public static void Load(string? directory = null)
        {
            try
            {
                _registered.Clear();
                directory ??= _path;
                if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);

                foreach (var item in Directory.EnumerateFiles(directory, "*.json"))
                {
                    using var stream = new FileStream(item, FileMode.Open);

                    var obj = (ColorPalette)FormatterServices.GetUninitializedObject(typeof(ColorPalette));
                    using var doc = JsonDocument.Parse(stream);
                    obj.SetObjectData(doc.RootElement);

                    obj.Name = Path.GetFileNameWithoutExtension(item);

                    _registered.Add(obj);
                }
            }
            catch (Exception e)
            {
                ServicesLocator.Current.Logger.LogWarning(e, "Failed to serialize or deserialize.");
            }
        }

        /// <summary>
        /// Saves all palettes in the specified directory.
        /// </summary>
        /// <param name="directory">The directory where palettes are stored.</param>
        public static void Save(string? directory = null)
        {
            try
            {
                directory ??= _path;
                if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
                foreach (var file in Directory.EnumerateFiles(directory))
                {
                    File.Delete(file);
                }

                foreach (var item in _registered)
                {
                    using var stream = new FileStream(Path.Combine(directory, (item.Name ?? item.Id.ToString()) + ".json"), FileMode.Create);
                    using var writer = new Utf8JsonWriter(stream, new()
                    {
                        Indented = true,
                        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
                    });

                    item.GetObjectData(writer);

                    writer.Flush();
                }
            }
            catch (Exception e)
            {
                ServicesLocator.Current.Logger.LogWarning(e, "Failed to serialize or deserialize.");
            }
        }
    }
}
