using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Skia;

using SkiaSharp;

namespace BEditor
{
    internal class CustomFontManagerImpl : IFontManagerImpl
    {
        private readonly Typeface[] _customTypefaces;
        private readonly string _defaultFamilyName;

        //Load font resources in the project, you can load multiple font resources
        private readonly Typeface _defaultTypeface = new(MatchFace().FamilyName);
        private static readonly string[] _familynames =
        {
            "Yu Gothic UI", "Segoe UI", "Hiragino Sans", "Helvetica Neue", "Ubuntu"
        };

        public CustomFontManagerImpl()
        {
            _customTypefaces = new[] { _defaultTypeface };
            _defaultFamilyName = _defaultTypeface.FontFamily.FamilyNames.PrimaryFamilyName;
        }

        public string GetDefaultFontFamilyName()
        {
            return _defaultFamilyName;
        }

        public IEnumerable<string> GetInstalledFontFamilyNames(bool checkForUpdates = false)
        {
            return SKFontManager.Default.FontFamilies;
        }

        private readonly string[] _bcp47 = { CultureInfo.CurrentCulture.ThreeLetterISOLanguageName, CultureInfo.CurrentCulture.TwoLetterISOLanguageName };

        public bool TryMatchCharacter(int codepoint, FontStyle fontStyle, FontWeight fontWeight, FontFamily fontFamily,
            CultureInfo culture, out Typeface typeface)
        {
            foreach (var customTypeface in _customTypefaces)
            {
                if (customTypeface.GlyphTypeface.GetGlyph((uint)codepoint) == 0)
                {
                    continue;
                }

                typeface = new Typeface(customTypeface.FontFamily.Name, fontStyle, fontWeight);

                return true;
            }

            var fallback = SKFontManager.Default.MatchCharacter(fontFamily?.Name, (SKFontStyleWeight)fontWeight,
                SKFontStyleWidth.Normal, (SKFontStyleSlant)fontStyle, _bcp47, codepoint);

            typeface = new Typeface(fallback?.FamilyName ?? _defaultFamilyName, fontStyle, fontWeight);

            return true;
        }

        public IGlyphTypefaceImpl CreateGlyphTypeface(Typeface typeface)
        {
            if (typeface.FontFamily.Name is "Inter") return new GlyphTypefaceImpl(MatchFace());

            foreach (var name in GetInstalledFontFamilyNames())
            {
                foreach (var typefaces in typeface.FontFamily.FamilyNames)
                {
                    if (name == typefaces)
                    {
                        return new GlyphTypefaceImpl(SKTypeface.FromFamilyName(name, (SKFontStyleWeight)typeface.Weight, SKFontStyleWidth.Normal, (SKFontStyleSlant)typeface.Style));
                    }
                }
            }

            using var stream = Assembly.GetExecutingAssembly()!.GetManifestResourceStream("BEditor.Assets.Fonts.NotoSansJP-Regular.otf");

            return new GlyphTypefaceImpl(SKTypeface.FromStream(stream));
        }

        private static SKTypeface MatchFace()
        {
            foreach (var name in SKFontManager.Default.FontFamilies)
            {
                foreach (var typefaces in _familynames)
                {
                    if (name == typefaces)
                    {
                        return SKTypeface.FromFamilyName(name, SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
                    }
                }
            }

            using var stream = Assembly.GetExecutingAssembly()!.GetManifestResourceStream("BEditor.Assets.Fonts.NotoSansJP-Regular.otf");

            return SKTypeface.FromStream(stream);
        }
    }
}