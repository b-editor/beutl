// Font.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;

using BEditor.Drawing.Resources;

using SkiaSharp;

namespace BEditor.Drawing
{
    /// <summary>
    /// Represents the font.
    /// </summary>
    [Serializable]
    public class Font : ISerializable, IEquatable<Font?>
    {
        private SKTypeface? _typeface;

        /// <summary>
        /// Initializes a new instance of the <see cref="Font"/> class.
        /// </summary>
        /// <param name="file">The font file name.</param>
        public Font(string file)
        {
            if (Path.GetExtension(file) is not (".ttf" or ".ttc" or ".otf"))
                throw new NotSupportedException(Strings.FontException);

            Filename = file;
            var face = GetTypeface();

            Weight = (FontStyleWeight)face.FontStyle.Weight;
            Width = (FontStyleWidth)face.FontStyle.Width;
            FamilyName = face.FamilyName;
            Name = FormatFamilyName();
        }

        private Font(SerializationInfo info, StreamingContext context)
        {
            Filename = info.GetString(nameof(Filename)) ?? throw new Exception();
            FamilyName = info.GetString(nameof(FamilyName)) ?? throw new Exception();
            Name = info.GetString(nameof(Name)) ?? throw new Exception();
            Weight = (FontStyleWeight)info.GetInt32(nameof(Weight));
            Width = (FontStyleWidth)info.GetInt32(nameof(Width));
        }

        /// <summary>
        /// Gets the filename of this font.
        /// </summary>
        public string Filename { get; }

        /// <summary>
        /// Gets the familyname of this font.
        /// </summary>
        public string FamilyName { get; }

        /// <summary>
        /// Gets the formatted font name. [eg: "Roboto Bold Expanded"].
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the weight of this font style.
        /// </summary>
        public FontStyleWeight Weight { get; }

        /// <summary>
        /// Gets the width of this font style.
        /// </summary>
        public FontStyleWidth Width { get; }

        /// <summary>
        /// Indicates whether two <see cref="Font"/> instances are equal.
        /// </summary>
        /// <param name="left">The first font to compare.</param>
        /// <param name="right">The second font to compare.</param>
        /// <returns>true if the values of <paramref name="left"/> and <paramref name="right"/> are equal; otherwise, false.</returns>
        public static bool operator ==(Font? left, Font? right)
        {
            return EqualityComparer<Font>.Default.Equals(left, right);
        }

        /// <summary>
        /// Indicates whether two <see cref="Font"/> instances are not equal.
        /// </summary>
        /// <param name="left">The first font to compare.</param>
        /// <param name="right">The second font to compare.</param>
        /// <returns>true if the values of <paramref name="left"/> and <paramref name="right"/> are not equal; otherwise, false.</returns>
        public static bool operator !=(Font? left, Font? right)
        {
            return !(left == right);
        }

        /// <inheritdoc/>
        public void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            info.AddValue(nameof(Filename), Filename);
            info.AddValue(nameof(FamilyName), FamilyName);
            info.AddValue(nameof(Name), Name);
            info.AddValue(nameof(Weight), (int)Weight);
            info.AddValue(nameof(Width), (int)Width);
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return Equals(obj as Font);
        }

        /// <inheritdoc/>
        public bool Equals(Font? other)
        {
            return other != null &&
                   Filename == other.Filename;
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCode.Combine(Filename);
        }

        internal SKTypeface GetTypeface()
        {
            return _typeface ??= SKTypeface.FromFile(Filename);
        }

        private string FormatFamilyName()
        {
            var str = new StringBuilder(FamilyName);
            var weight = Weight;
            var width = Width;

            str.Append(weight.ToString("g"));
            str.Append(width.ToString("g"));

            return str.ToString();
        }
    }
}