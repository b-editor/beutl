// VideoFile.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
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
using BEditor.Media;
using BEditor.Media.Decoding;
using BEditor.Primitive.Resources;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BEditor.Primitive.Objects
{
    /// <summary>
    /// Represents an <see cref="ImageObject"/> that references an video file.
    /// </summary>
    public sealed class VideoFile : ImageObject, IMediaObject
    {
        /// <summary>
        /// Defines the <see cref="Speed"/> property.
        /// </summary>
        public static readonly DirectProperty<VideoFile, EaseProperty> SpeedProperty = EditingProperty.RegisterDirect<EaseProperty, VideoFile>(
            nameof(Speed),
            owner => owner.Speed,
            (owner, obj) => owner.Speed = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.Speed, 100)).Serialize());

        /// <summary>
        /// Defines the <see cref="Start"/> property.
        /// </summary>
        public static readonly DirectProperty<VideoFile, EaseProperty> StartProperty = EditingProperty.RegisterDirect<EaseProperty, VideoFile>(
            nameof(Start),
            owner => owner.Start,
            (owner, obj) => owner.Start = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.Start, 1, float.NaN, 0)).Serialize());

        /// <summary>
        /// Defines the <see cref="File"/> property.
        /// </summary>
        public static readonly DirectProperty<VideoFile, FileProperty> FileProperty = EditingProperty.RegisterDirect<FileProperty, VideoFile>(
            nameof(File),
            owner => owner.File,
            (owner, obj) => owner.File = obj,
            EditingPropertyOptions<FileProperty>.Create(new FilePropertyMetadata(Strings.File, string.Empty, new(Strings.VideoFile, DecodingRegistory.EnumerateDecodings()
                .SelectMany(i => i.SupportExtensions())
                .Distinct()
                .Select(i => i.Trim('.'))
                .ToArray()))).Serialize());

        private static readonly MediaOptions _options = new()
        {
            StreamsToLoad = MediaMode.Video,
        };

        private MediaFile? _mediaFile;

        private IDisposable? _disposable1;

        /// <summary>
        /// Initializes a new instance of the <see cref="VideoFile"/> class.
        /// </summary>
        public VideoFile()
        {
        }

        /// <inheritdoc/>
        public override string Name => Strings.Video;

        /// <summary>
        /// Gets the playback speed.
        /// </summary>
        [AllowNull]
        public EaseProperty Speed { get; private set; }

        /// <summary>
        /// Gets the start position.
        /// </summary>
        [AllowNull]
        public EaseProperty Start { get; private set; }

        /// <summary>
        /// Gets the video file to reference.
        /// </summary>
        [AllowNull]
        public FileProperty File { get; private set; }

        /// <inheritdoc/>
        public TimeSpan? Length => _mediaFile?.Video?.Info?.Duration;

        /// <summary>
        /// Gets whether the file name is supported.
        /// </summary>
        /// <param name="file">The name of the file to check if it is supported.</param>
        /// <returns>Returns true if supported, false otherwise.</returns>
        public static bool IsSupported(string file)
        {
            var ext = Path.GetExtension(file);
            return DecodingRegistory.EnumerateDecodings()
                .SelectMany(i => i.SupportExtensions())
                .Contains(ext);
        }

        /// <summary>
        /// Creates an instance from a file name.
        /// </summary>
        /// <param name="file">The file name.</param>
        /// <returns>A new instance of <see cref="VideoFile"/>.</returns>
        public static VideoFile FromFile(string file)
        {
            return new VideoFile
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
            yield return Speed;
            yield return Start;
            yield return File;
        }

        /// <inheritdoc/>
        protected override Image<BGRA32>? OnRender(EffectApplyArgs args)
        {
            if (_mediaFile?.Video is null) return null;

            var speed = Speed[args.Frame] / 100;
            var start = (int)Start[args.Frame];
            var time = new Frame((int)((start + args.Frame - Parent!.Start) * speed)).ToTimeSpan(Parent.Parent.Parent!.Framerate);

            try
            {
                return time < Length ? _mediaFile.Video.GetFrame(time) : null;
            }
            catch
            {
                return null;
            }
        }

        /// <inheritdoc/>
        protected override void OnLoad()
        {
            base.OnLoad();

            _disposable1 = File.Subscribe(filename =>
            {
                _mediaFile?.Dispose();

                if (System.IO.File.Exists(File.Value))
                {
                    var mes = ServiceProvider?.GetService<IMessage>();

                    try
                    {
                        _mediaFile = MediaFile.Open(filename, _options);
                    }
                    catch (DecoderNotFoundException e)
                    {
                        mes?.Snackbar(Strings.DecoderNotFound);
                        ServicesLocator.Current.Logger?.LogError(e, Strings.DecoderNotFound);
                    }
                    catch (Exception ex)
                    {
                        mes?.Snackbar(string.Format(Strings.FailedToLoad, filename));
                        ServicesLocator.Current.Logger?.LogError(ex, Strings.DecoderNotFound);
                    }
                }
                else
                {
                    _mediaFile = null;
                }
            });
        }

        /// <inheritdoc/>
        protected override void OnUnload()
        {
            base.OnUnload();

            _mediaFile?.Dispose();
            _disposable1?.Dispose();
        }
    }
}