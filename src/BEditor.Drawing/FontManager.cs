// FontManager.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BEditor.Drawing
{
    /// <summary>
    /// Represents the manager of the font.
    /// </summary>
    public class FontManager
    {
        private readonly Font[] _loadedFonts;

        /// <summary>
        /// Initializes a new instance of the <see cref="FontManager"/> class.
        /// </summary>
        /// <param name="dirs">The font will be loaded from these directories.</param>
        public FontManager(IEnumerable<string> dirs)
        {
            _loadedFonts = dirs
                .Where(dir => Directory.Exists(dir))
                .Select(dir => Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories))
                .SelectMany(files => files)
                .Where(file => Path.GetExtension(file) is ".ttf" or ".ttc" or ".otf")
                .Select(file => new Font(file))
                .ToArray();

            Array.Sort(_loadedFonts, (x, y) => x.FamilyName.CompareTo(y.FamilyName));
        }

        /// <summary>
        /// Gets the default instance of the font manager.
        /// </summary>
        public static FontManager Default { get; } = new(Settings.Default.IncludeFontDir);

        /// <summary>
        /// Gets the loaded fonts.
        /// </summary>
        public IEnumerable<Font> LoadedFonts => _loadedFonts;

        /// <summary>
        /// Gets the number of loaded fonts.
        /// </summary>
        public int FontCount => _loadedFonts.Length;

        /// <summary>
        /// Searches for fonts matching the criteria defined by the given predicate and returns the first occurrence of the font in the loaded fonts.
        /// </summary>
        /// <param name="match">The predicate that defines the conditions of the font to search for.</param>
        /// <returns>Returns the first font that matches the criteria defined in the given predicate, if found, or <see langword="null"/> if not found.</returns>
        public Font? Find(Predicate<Font> match)
        {
            return Array.Find(_loadedFonts, match);
        }

        /// <summary>
        /// Retrieves all fonts that match the criteria defined in the given predicate.
        /// </summary>
        /// <param name="match">The <see cref="Predicate{T}"/> that defines the conditions of the fonts to search for.</param>
        /// <returns>If found, it is a System.Array containing all fonts matching the conditions defined in the given predicate, otherwise it is an empty System.Array.</returns>
        public Font?[] FindAll(Predicate<Font> match)
        {
            return Array.FindAll(_loadedFonts, match);
        }

        /// <summary>
        /// Searches for fonts matching the criteria defined by the given predicate and returns the last occurrence of the font among the loaded fonts.
        /// </summary>
        /// <param name="match">The <see cref="Predicate{T}"/> defines the conditions for the font to be searched.</param>
        /// <returns>Returns the last font that matches the condition defined by the given predicate, if found, or <see langword="null"/> if not found.</returns>
        public Font? FindLast(Predicate<Font> match)
        {
            return Array.FindLast(_loadedFonts, match);
        }
    }
}