using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;

using BEditor.Drawing.Resources;

using SkiaSharp;

namespace BEditor.Drawing
{
    [Serializable]
    public class Font : ISerializable, IEquatable<Font?>
    {
        private SKTypeface? _typeface;

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

        public string Filename { get; }
        public string FamilyName { get; }
        public string Name { get; }
        public FontStyleWeight Weight { get; }
        public FontStyleWidth Width { get; }

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
        public void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            info.AddValue(nameof(Filename), Filename);
            info.AddValue(nameof(FamilyName), FamilyName);
            info.AddValue(nameof(Name), Name);
            info.AddValue(nameof(Weight), (int)Weight);
            info.AddValue(nameof(Width), (int)Width);
        }

        public override bool Equals(object? obj)
        {
            return Equals(obj as Font);
        }

        public bool Equals(Font? other)
        {
            return other != null &&
                   Filename == other.Filename;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Filename);
        }

        public static bool operator ==(Font? left, Font? right)
        {
            return EqualityComparer<Font>.Default.Equals(left, right);
        }

        public static bool operator !=(Font? left, Font? right)
        {
            return !(left == right);
        }
    }

    public enum VerticalAlign
    {
        /// <summary>
        /// The font aligns itself to the top of the image.
        /// </summary>
        Top = 0,

        /// <summary>
        /// The font centers itself within the image.
        /// </summary>
        Center = 1,

        /// <summary>
        /// The font aligns itself to the bottom of the image.
        /// </summary>
        Bottom = 2
    }

    public enum HorizontalAlign
    {
        /// <summary>
        /// The font aligns itself to the left of the image.
        /// </summary>
        Left = 0,

        /// <summary>
        /// The font centers itself in the image.
        /// </summary>
        Center = 1,

        /// <summary>
        /// The font aligns itself to the right of the image.
        /// </summary>
        Right = 2
    }

    public enum FontStyleWeight
    {
        Invisible = 0,
        Thin = 100,
        ExtraLight = 200,
        Light = 300,
        Normal = 400,
        Medium = 500,
        SemiBold = 600,
        Bold = 700,
        ExtraBold = 800,
        Black = 900,
        ExtraBlack = 1000
    }
    public enum FontStyleWidth
    {
        UltraCondensed = 1,
        ExtraCondensed = 2,
        Condensed = 3,
        SemiCondensed = 4,
        Normal = 5,
        SemiExpanded = 6,
        Expanded = 7,
        ExtraExpanded = 8,
        UltraExpanded = 9
    }
}