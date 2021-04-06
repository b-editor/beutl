using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BEditor.Drawing
{
    public class FontManager
    {
        private readonly Font[] _loadedFonts;

        public FontManager(IEnumerable<string> dirs)
        {
            _loadedFonts = dirs
                .Where(dir => Directory.Exists(dir))
                .Select(dir => Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories))
                .SelectMany(files => files)
                .Where(file => Path.GetExtension(file) is ".ttf" or ".ttc" or ".otf")
                .Select(file => new Font(file))
                .OrderBy(f => f.FamilyName)
                .ToArray();
        }

        public static FontManager Default { get; set; } = new(Settings.Default.IncludeFontDir);

        public IEnumerable<Font> LoadedFonts => _loadedFonts;

        public int FontCount => _loadedFonts.Length;

        public Font? Find(Predicate<Font> match)
        {
            return Array.Find(_loadedFonts, match);
        }

        public Font?[] FindAll(Predicate<Font> match)
        {
            return Array.FindAll(_loadedFonts, match);
        }

        public Font? FindLast(Predicate<Font> match)
        {
            return Array.FindLast(_loadedFonts, match);
        }
    }
}
