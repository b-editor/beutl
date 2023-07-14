#if false

using Avalonia.Media;
using Avalonia.Media.Fonts;
using Avalonia.Platform;

using Avalonia.Skia;

using Beutl.Media;

using SkiaSharp;

using FontFamily = Avalonia.Media.FontFamily;
using FontStyle = Avalonia.Media.FontStyle;
using FontWeight = Avalonia.Media.FontWeight;
using Typeface = Avalonia.Media.Typeface;

namespace Beutl;

#nullable disable

internal sealed class CustomFontManagerImpl : IFontManagerImpl
{
    [ThreadStatic]
    private static string[] s_languageTagBuffer;

    private readonly SKTypeface _defaultTypeface;
    private SKFontManager _skFontManager = SKFontManager.Default;

    public CustomFontManagerImpl()
    {
        Media.Typeface defaultTf = Media.FontManager.Instance.DefaultTypeface;
        _defaultTypeface = defaultTf.ToSkia();
    }

    public string GetDefaultFontFamilyName()
    {
        return _defaultTypeface.FamilyName;
    }

    public IEnumerable<string> GetInstalledFontFamilyNames(bool checkForUpdates = false)
    {
        if (checkForUpdates)
        {
            _skFontManager = SKFontManager.CreateDefault();
        }

        return _skFontManager.FontFamilies;
    }

    public bool TryMatchCharacter(int codepoint, FontStyle fontStyle, FontWeight fontWeight,
        FontStretch fontStretch, FontFamily fontFamily, CultureInfo culture, out Typeface typeface)
    {
        SKFontStyle skFontStyle = fontWeight switch
        {
            FontWeight.Normal when fontStyle == FontStyle.Normal => SKFontStyle.Normal,
            FontWeight.Normal when fontStyle == FontStyle.Italic => SKFontStyle.Italic,
            FontWeight.Bold when fontStyle == FontStyle.Normal => SKFontStyle.Bold,
            FontWeight.Bold when fontStyle == FontStyle.Italic => SKFontStyle.BoldItalic,
            _ => new SKFontStyle((SKFontStyleWeight)fontWeight, SKFontStyleWidth.Normal, (SKFontStyleSlant)fontStyle),
        };
        culture ??= CultureInfo.CurrentUICulture;

        s_languageTagBuffer ??= new string[2];

        s_languageTagBuffer[0] = culture.TwoLetterISOLanguageName;
        s_languageTagBuffer[1] = culture.ThreeLetterISOLanguageName;

        if (fontFamily?.FamilyNames.HasFallbacks == true)
        {
            FamilyNameCollection familyNames = fontFamily.FamilyNames;

            for (int i = 1; i < familyNames.Count; i++)
            {
                SKTypeface skTypeface =
                    _skFontManager.MatchCharacter(familyNames[i], skFontStyle, s_languageTagBuffer, codepoint);

                if (skTypeface == null)
                {
                    continue;
                }

                typeface = new Typeface(skTypeface.FamilyName, fontStyle, fontWeight);

                return true;
            }
        }
        else
        {
            SKTypeface skTypeface = _skFontManager.MatchCharacter(null, skFontStyle, s_languageTagBuffer, codepoint);

            if (skTypeface != null)
            {
                typeface = new Typeface(skTypeface.FamilyName, fontStyle, fontWeight);

                return true;
            }
        }

        typeface = default;

        return false;
    }

    public IGlyphTypeface CreateGlyphTypeface(Typeface typeface)
    {
        SKTypeface skTypeface = null;

        if (typeface.FontFamily.Key == null)
        {
            string defaultName = _defaultTypeface.FamilyName;
            var fontStyle = new SKFontStyle((SKFontStyleWeight)typeface.Weight, SKFontStyleWidth.Normal, (SKFontStyleSlant)typeface.Style);

            foreach (string familyName in typeface.FontFamily.FamilyNames)
            {
                skTypeface = _skFontManager.MatchFamily(familyName, fontStyle);

                if (skTypeface is null
                    || (!skTypeface.FamilyName.Equals(familyName, StringComparison.Ordinal)
                        && defaultName.Equals(skTypeface.FamilyName, StringComparison.Ordinal)))
                {
                    continue;
                }

                break;
            }

            //skTypeface ??= _skFontManager.MatchTypeface(_defaultTypeface, fontStyle);
            skTypeface ??= _defaultTypeface;
        }
        else
        {
            SKTypefaceCollection fontCollection = SKTypefaceCollectionCache.GetOrAddTypefaceCollection(typeface.FontFamily);

            skTypeface = fontCollection.Get(typeface);
        }

        if (skTypeface == null)
        {
            throw new InvalidOperationException(
                $"Could not create glyph typeface for: {typeface.FontFamily.Name}.");
        }

        bool isFakeBold = (int)typeface.Weight >= 600 && !skTypeface.IsBold;

        bool isFakeItalic = typeface.Style == FontStyle.Italic && !skTypeface.IsItalic;
        FontSimulations fontSimulations = FontSimulations.None;
        if (isFakeBold)
            fontSimulations |= FontSimulations.Bold;
        if (isFakeItalic)
            fontSimulations |= FontSimulations.Oblique;

        return new GlyphTypefaceImpl(skTypeface, fontSimulations);
    }
}
#endif
