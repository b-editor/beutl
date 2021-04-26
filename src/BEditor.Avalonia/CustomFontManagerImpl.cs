using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Resources;

using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Skia;
using Avalonia.Styling;

using SkiaSharp;

namespace BEditor
{
    public class CustomFontManagerImpl : IFontManagerImpl
    {
        private readonly Typeface[] _customTypefaces;
        private readonly string _defaultFamilyName;

        //Load font resources in the project, you can load multiple font resources
        private readonly Typeface _defaultTypeface = new("resm:BEditor.Assets.Fonts.NotoSansJP-Regular#Noto Sans JP");

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
            using var stream = Assembly.GetExecutingAssembly()!.GetManifestResourceStream("BEditor.Assets.Fonts.NotoSansJP-Regular.otf");
            var skTypeface = typeface.FontFamily.Name switch
            {
                FontFamily.DefaultFontFamilyName or "Noto Sans JP" or "Inter" => SKTypeface.FromStream(stream),
                _ => SKTypeface.FromFamilyName(typeface.FontFamily.Name, (SKFontStyleWeight)typeface.Weight, SKFontStyleWidth.Normal, (SKFontStyleSlant)typeface.Style),
            };

            return new GlyphTypefaceImpl(skTypeface);
        }
    }
}