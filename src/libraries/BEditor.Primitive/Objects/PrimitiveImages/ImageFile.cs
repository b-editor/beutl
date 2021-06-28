// ImageFile.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reactive.Linq;

using BEditor.Data;
using BEditor.Data.Primitive;
using BEditor.Data.Property;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Primitive.Resources;

using Reactive.Bindings;

namespace BEditor.Primitive.Objects
{
    /// <summary>
    /// Represents an <see cref="ImageObject"/> that references an image file.
    /// </summary>
    public sealed class ImageFile : ImageObject
    {
        /// <summary>
        /// Defines the <see cref="File"/> property.
        /// </summary>
        public static readonly DirectProperty<ImageFile, FileProperty> FileProperty;

        /// <summary>
        /// The extension of the supported image files.
        /// </summary>
        public static readonly string[] SupportExtensions =
        {
            "png",
            "jpeg",
            "jpg",
            "bmp",
            "gif",
            "ico",
            "wbmp",
            "webp",
            "pkm",
            "ktx",
            "astc",
            "dng",
            "heif",
        };

        static ImageFile()
        {
            FileProperty = EditingProperty.RegisterDirect<FileProperty, ImageFile>(
                nameof(File),
                owner => owner.File,
                (owner, obj) => owner.File = obj,
                EditingPropertyOptions<FileProperty>.Create(new FilePropertyMetadata(Strings.File, string.Empty, new(Strings.ImageFile, SupportExtensions))).Serialize());
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ImageFile"/> class.
        /// </summary>
        public ImageFile()
        {
        }

        /// <inheritdoc/>
        public override string Name => Strings.Image;

        /// <summary>
        /// Get the <see cref="FileProperty"/> to select the image file to reference.
        /// </summary>
        [AllowNull]
        public FileProperty File { get; private set; }

        private ReactiveProperty<Image<BGRA32>?>? Source { get; set; }

        /// <summary>
        /// Gets whether the file name is supported.
        /// </summary>
        /// <param name="file">The name of the file to check if it is supported.</param>
        /// <returns>Returns true if supported, false otherwise.</returns>
        public static bool IsSupported(string file)
        {
            var ext = Path.GetExtension(file).Trim('.');
            return SupportExtensions.Contains(ext);
        }

        /// <summary>
        /// Creates an instance from a file name.
        /// </summary>
        /// <param name="file">The file name.</param>
        /// <returns>A new instance of <see cref="ImageFile"/>.</returns>
        public static ImageFile FromFile(string file)
        {
            return new ImageFile
            {
                File =
                {
                    Value = file,
                },
            };
        }

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> GetProperties()
        {
            yield return Coordinate;
            yield return Scale;
            yield return Blend;
            yield return Rotate;
            yield return Material;
            yield return File;
        }

        /// <inheritdoc/>
        protected override Image<BGRA32>? OnRender(EffectApplyArgs args)
        {
            return Source?.Value?.Clone();
        }

        /// <inheritdoc/>
        protected override void OnLoad()
        {
            base.OnLoad();

            Source = File.Where(file => System.IO.File.Exists(file))
                .Select(f =>
                {
                    Source?.Value?.Dispose();

                    using var stream = new FileStream(f, FileMode.Open);
                    return Image.Decode(stream);
                })
                .ToReactiveProperty();
        }

        /// <inheritdoc/>
        protected override void OnUnload()
        {
            base.OnUnload();

            Source?.Value?.Dispose();
            Source?.Dispose();
        }
    }
}