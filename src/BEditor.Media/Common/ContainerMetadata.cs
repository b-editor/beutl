using System.Collections.Generic;

using FFmpeg.AutoGen;

namespace BEditor.Media.Common
{
    /// <summary>
    /// Represents multimedia file metadata info.
    /// </summary>
    public class ContainerMetadata
    {
        private const string TitleKey = "title";
        private const string AuthorKey = "author";
        private const string AlbumKey = "album";
        private const string YearKey = "year";
        private const string GenreKey = "genre";
        private const string DescriptionKey = "description";
        private const string LanguageKey = "language";
        private const string CopyrightKey = "copyright";
        private const string RatingKey = "rating";
        private const string TrackKey = "track";
        private const string DateKey = "date";

        /// <summary>
        /// Initializes a new instance of the <see cref="ContainerMetadata"/> class.
        /// </summary>
        public ContainerMetadata()
        {
            Metadata = new Dictionary<string, string>();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ContainerMetadata"/> class.
        /// </summary>
        /// <param name="sourceMetadata">The source metadata dictionary.</param>
        internal unsafe ContainerMetadata(AVDictionary* sourceMetadata)
        {
            Metadata = FFDictionary.ToDictionary(sourceMetadata, true);
        }

        /// <summary>
        /// Gets or sets the multimedia title.
        /// </summary>
        public string Title
        {
            get => Metadata.ContainsKey(TitleKey) ? Metadata[TitleKey] : string.Empty;
            set => Metadata[TitleKey] = value;
        }

        /// <summary>
        /// Gets or sets the multimedia author info.
        /// </summary>
        public string Author
        {
            get => Metadata.ContainsKey(AuthorKey) ? Metadata[AuthorKey] : string.Empty;
            set => Metadata[AuthorKey] = value;
        }

        /// <summary>
        /// Gets or sets the multimedia album name.
        /// </summary>
        public string Album
        {
            get => Metadata.ContainsKey(AlbumKey) ? Metadata[AlbumKey] : string.Empty;
            set => Metadata[AlbumKey] = value;
        }

        /// <summary>
        /// Gets or sets multimedia release date/year.
        /// </summary>
        public string Year
        {
            get => Metadata.ContainsKey(YearKey)
                ? Metadata[YearKey]
                : (Metadata.ContainsKey(DateKey) ? Metadata[DateKey] : string.Empty);
            set => Metadata[YearKey] = value;
        }

        /// <summary>
        /// Gets or sets the multimedia genre.
        /// </summary>
        public string Genre
        {
            get => Metadata.ContainsKey(GenreKey) ? Metadata[GenreKey] : string.Empty;
            set => Metadata[GenreKey] = value;
        }

        /// <summary>
        /// Gets or sets the multimedia description.
        /// </summary>
        public string Description
        {
            get => Metadata.ContainsKey(DescriptionKey) ? Metadata[DescriptionKey] : string.Empty;
            set => Metadata[DescriptionKey] = value;
        }

        /// <summary>
        /// Gets or sets the multimedia language.
        /// </summary>
        public string Language
        {
            get => Metadata.ContainsKey(LanguageKey) ? Metadata[LanguageKey] : string.Empty;
            set => Metadata[LanguageKey] = value;
        }

        /// <summary>
        /// Gets or sets the multimedia copyright info.
        /// </summary>
        public string Copyright
        {
            get => Metadata.ContainsKey(CopyrightKey) ? Metadata[CopyrightKey] : string.Empty;
            set => Metadata[CopyrightKey] = value;
        }

        /// <summary>
        /// Gets or sets the multimedia rating.
        /// </summary>
        public string Rating
        {
            get => Metadata.ContainsKey(RatingKey) ? Metadata[RatingKey] : string.Empty;
            set => Metadata[RatingKey] = value;
        }

        /// <summary>
        /// Gets or sets the multimedia track number string.
        /// </summary>
        public string TrackNumber
        {
            get => Metadata.ContainsKey(TrackKey) ? Metadata[TrackKey] : string.Empty;
            set => Metadata[TrackKey] = value;
        }

        /// <summary>
        /// Gets or sets the dictionary containing all metadata fields.
        /// </summary>
        public Dictionary<string, string> Metadata { get; set; }
    }
}