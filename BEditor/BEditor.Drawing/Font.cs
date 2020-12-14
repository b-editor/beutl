using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using SkiaSharp;

namespace BEditor.Drawing
{
    public class Font
    {
        public Font(string file)
        {
            if (!(Path.GetExtension(file) is ".ttf" or ".ttc" or ".otf")) throw new NotSupportedException();

            Filename = file;
            using var face = SKTypeface.FromFile(file);

            Weight = (FontStyleWeight)face.FontStyle.Weight;
            Width = (FontStyleWidth)face.FontStyle.Width;
            FamilyName = face.FamilyName;
            Name = FormatFamilyName(face);
        }

        public string Filename { get; }
        public string FamilyName { get; }
        public string Name { get; }
        public FontStyleWeight Weight { get; }
        public FontStyleWidth Width { get; }

        private static string FormatFamilyName(SKTypeface face)
        {
            var str = new StringBuilder(face.FamilyName);
            var weight = (FontStyleWeight)face.FontStyle.Weight;
            var width = (FontStyleWidth)face.FontStyle.Width;

            str.Append($" {weight:g}");
            str.Append($" {width:g}");

            return str.ToString();
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
