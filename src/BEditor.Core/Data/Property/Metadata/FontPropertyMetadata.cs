using System.Linq;

using BEditor.Drawing;
using BEditor.Properties;

namespace BEditor.Data.Property
{
    /// <summary>
    /// The metadata of <see cref="FontProperty"/>.
    /// </summary>
    public record FontPropertyMetadata : PropertyElementMetadata, IPropertyBuilder<FontProperty>
    {
        /// <summary>
        /// The metadata of <see cref="FontProperty"/>.
        /// </summary>
        public FontPropertyMetadata()
            : base(Resources.Font)
        {
            SelectItem = FontManager.Default.LoadedFonts.FirstOrDefault()!;
        }

        /// <summary>
        /// The default selected item.
        /// </summary>
        public Font SelectItem { get; init; }

        /// <inheritdoc/>
        public FontProperty Build()
        {
            return new(this);
        }
    }
}
