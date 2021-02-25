using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

using BEditor.Drawing.Properties;

using SkiaSharp;

namespace BEditor.Drawing
{
    [Serializable]
    public record Font : ISerializable
    {
        public Font(string file)
        {
            if (Path.GetExtension(file) is not (".ttf" or ".ttc" or ".otf"))
                throw new NotSupportedException(Resources.FontException);
            
            using var face = SKTypeface.FromFile(file);

            Weight = (FontStyleWeight)face.FontStyle.Weight;
            Width = (FontStyleWidth)face.FontStyle.Width;
            FamilyName = face.FamilyName;
            Filename = file;
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

        private string FormatFamilyName()
        {
            var str = new StringBuilder(FamilyName);
            var weight = Weight;
            var width = Width;

            str.Append($" {weight:g}");
            str.Append($" {width:g}");

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
    public enum FontStyleSlant
    {
        Upright = 0,
        Italic = 1,
        Oblique = 2
    }
}
