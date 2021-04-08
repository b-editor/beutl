using System.Linq;

using BEditor.Drawing;
using BEditor.Resources;

namespace BEditor.Data.Property
{
    /// <summary>
    /// The metadata of <see cref="FontProperty"/>.
    /// </summary>
    public record FontPropertyMetadata : PropertyElementMetadata, IEditingPropertyInitializer<FontProperty>
    {
        /// <summary>
        /// The metadata of <see cref="FontProperty"/>.
        /// </summary>
        public FontPropertyMetadata()
            : base(Strings.Font)
        {
            SelectItem = FontManager.Default.LoadedFonts.FirstOrDefault()!;
        }

        /// <summary>
        /// The default selected item.
        /// </summary>
        public Font SelectItem { get; init; }

        /// <inheritdoc/>
        public FontProperty Create()
        {
            return new(this);
        }
    }
}
