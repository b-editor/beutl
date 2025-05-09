﻿using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Beutl.Configuration;
using Beutl.Logging;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Beutl.Media;

public sealed class FontManager
{
    public static readonly FontManager Instance = new();
    private readonly ILogger _logger = Log.CreateLogger<FontManager>();
    internal readonly Dictionary<FontFamily, FrozenDictionary<Typeface, SKTypeface>> _fonts = [];
    internal readonly Dictionary<FontFamily, FontName> _fontNames = [];
    private readonly string[] _fontDirs;

    private FontManager()
    {
        Typeface GetDefaultTypeface()
        {
            if (OperatingSystem.IsLinux())
            {
                var output = new StringBuilder();
                string applicationPath = "/usr/bin/fc-match";
                var paths = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? [];
                foreach (var path in paths)
                {
                    var fullPath = Path.Combine(path, "fc-match");
                    if (File.Exists(fullPath))
                    {
                        applicationPath = fullPath;
                        break;
                    }
                }
                using Process process = Process.Start(new ProcessStartInfo(applicationPath, "--format %{file}")
                {
                    RedirectStandardOutput = true
                })!;
                process.OutputDataReceived += (sender, e) =>
                {
                    if (e.Data != null)
                        output.Append(e.Data);
                };
                process.BeginOutputReadLine();
                process.WaitForExit();

                process.CancelOutputRead();

                string file = output.ToString();
                SKTypeface? sktypeface = SKTypeface.FromFile(file);
                if (sktypeface != null)
                {
                    Typeface typeface = Typeface.FromSKTypeface(sktypeface);
                    bool isAdded = AddFont(sktypeface);
                    if (!isAdded)
                    {
                        sktypeface.Dispose();
                    }
                    return typeface;
                }
            }

            SKTypeface sk = SKTypeface.Default;
            AddFont(sk);
            return Typeface.FromSKTypeface(sk);
        }

        _fontDirs = [.. GlobalConfiguration.Instance.FontConfig.FontDirectories];
        var list = new List<SKTypeface>();

        foreach (string file in _fontDirs
            .Where(dir => Directory.Exists(dir))
            .Select(dir => Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories))
            .SelectMany(files => files)
            .Where(file =>
            {
                ReadOnlySpan<char> ext = Path.GetExtension(file.AsSpan());
                return ext.Equals(".ttf", StringComparison.OrdinalIgnoreCase)
                    || ext.Equals(".ttc", StringComparison.OrdinalIgnoreCase)
                    || ext.Equals(".otf", StringComparison.OrdinalIgnoreCase);
            }))
        {
            SKTypeface? face = LoadFont(file);

            if (face != null)
            {
                list.Add(face);
            }
        }

        foreach (IGrouping<string, SKTypeface> item in list.GroupBy(i => i.FamilyName))
        {
            var family = new FontFamily(item.Key);
            SKTypeface[] typefaces = [.. item];
            if (typefaces.Length == 0) continue;
            _fonts.Add(family, TypefaceCollection.Create(typefaces));

            if (!_fontNames.ContainsKey(family))
            {
                try
                {
                    // name
                    byte[]? buffer = typefaces[0].GetTableData(0x6E616D65);
                    using var ms = new MemoryStream(buffer);
                    var fontName = FontName.ReadFontName(ms);
                    _fontNames.Add(family, fontName);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to read font name from {FontFamily}", family);
                }
            }
        }

        DefaultTypeface = GetDefaultTypeface();
    }

    public IEnumerable<FontFamily> FontFamilies => _fonts.Keys;

    public int FontFamilyCount => _fonts.Count;

    public Typeface DefaultTypeface { get; }

    public void AddFont(Stream stream)
    {
        AddFont(SKTypeface.FromStream(stream));
    }

    private bool AddFont(SKTypeface typeface)
    {
        string familyName = typeface.FamilyName;
        var fontFamily = new FontFamily(familyName);

        ref FrozenDictionary<Typeface, SKTypeface>? value
            = ref CollectionsMarshal.GetValueRefOrAddDefault(_fonts, fontFamily, out bool exists);

        if (exists)
        {
            var tf = Typeface.FromSKTypeface(typeface);

            if (!value!.ContainsKey(tf))
            {
                value = value.Append(new(tf, typeface))
                    .ToFrozenDictionary();

                return true;
            }
            else
            {
                return false;
            }
        }
        else
        {
            value = TypefaceCollection.Create([typeface]);
            return true;
        }
    }

    private SKTypeface? LoadFont(string file)
    {
        try
        {
            return SKTypeface.FromFile(file);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load font from {File}", file);
            return null;
        }
    }

    public ImmutableArray<Typeface> GetTypefaces(FontFamily fontFamily)
    {
        return _fonts.TryGetValue(fontFamily, out FrozenDictionary<Typeface, SKTypeface>? value)
            ? value.Keys
            : [];
    }
}

internal static class TypefaceCollection
{
    public static FrozenDictionary<Typeface, SKTypeface> Create(SKTypeface[] typefaces)
    {
        var list = new List<KeyValuePair<Typeface, SKTypeface>>(typefaces.Length);
        foreach (SKTypeface typeface in typefaces)
        {
            list.Add(new(Typeface.FromSKTypeface(typeface), typeface));
        }

        return list.ToFrozenDictionary();
    }

    public static SKTypeface Get(this FrozenDictionary<Typeface, SKTypeface> typefaces, Typeface typeface)
    {
        return GetNearestMatch(typefaces, typeface);
    }

    private static SKTypeface GetNearestMatch(FrozenDictionary<Typeface, SKTypeface> typefaces, Typeface key)
    {
        if (typefaces.TryGetValue(key, out SKTypeface? typeface))
        {
            return typeface;
        }

        int initialWeight = (int)key.Weight;

        int weight = (int)key.Weight;

        weight -= weight % 50; // make sure we start at a full weight

        for (int i = 0; i < 2; i++)
        {
            for (int j = 0; j < initialWeight; j += 50)
            {
                if (weight - j >= 100)
                {
                    if (typefaces.TryGetValue(new Typeface(key.FontFamily, (FontStyle)i, (FontWeight)(weight - j)), out typeface))
                    {
                        return typeface;
                    }
                }

                if (weight + j > 900)
                {
                    continue;
                }

                if (typefaces.TryGetValue(new Typeface(key.FontFamily, (FontStyle)i, (FontWeight)(weight + j)), out typeface))
                {
                    return typeface;
                }
            }
        }

        //Nothing was found so we try to get a regular typeface.
        return typefaces.TryGetValue(new Typeface(key.FontFamily), out typeface) ? typeface : typefaces.Values[0];
    }
}
