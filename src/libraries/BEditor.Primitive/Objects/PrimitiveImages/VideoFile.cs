// VideoFile.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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
    public sealed class VideoFile : ImageObject
    {
        /// <summary>
        /// Defines the <see cref="Speed"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<VideoFile, EaseProperty> SpeedProperty = EditingProperty.RegisterDirect<EaseProperty, VideoFile>(
            nameof(Speed),
            owner => owner.Speed,
            (owner, obj) => owner.Speed = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.Speed, 100)).Serialize());

        /// <summary>
        /// Defines the <see cref="Start"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<VideoFile, EaseProperty> StartProperty = EditingProperty.RegisterDirect<EaseProperty, VideoFile>(
            nameof(Start),
            owner => owner.Start,
            (owner, obj) => owner.Start = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.Start, 1, float.NaN, 0)).Serialize());

        /// <summary>
        /// Defines the <see cref="File"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<VideoFile, FileProperty> FileProperty = EditingProperty.RegisterDirect<FileProperty, VideoFile>(
            nameof(File),
            owner => owner.File,
            (owner, obj) => owner.File = obj,
            EditingPropertyOptions<FileProperty>.Create(new FilePropertyMetadata(Strings.File, string.Empty, new(Strings.VideoFile, new FileExtension[]
            {
                new("mp4"),
                new("avi"),
                new("wmv"),
                new("mov"),
            }))).Serialize());

        /// <summary>
        /// Defines the <see cref="SetLength"/> property.
        /// </summary>
        public static readonly EditingProperty<ButtonComponent> SetLengthProperty = EditingProperty.Register<ButtonComponent, VideoFile>(
            nameof(SetLength),
            EditingPropertyOptions<ButtonComponent>.Create(new ButtonComponentMetadata(Strings.ClipLengthAsVideoLength)).Serialize());

        private static readonly MediaOptions _options = new()
        {
            StreamsToLoad = MediaMode.Video,
        };

        private MediaFile? _mediaFile;

        private IDisposable? _disposable1;

        private IDisposable? _disposable2;

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

        /// <summary>
        /// Gets the command to set the length of the clip.
        /// </summary>
        [AllowNull]
        public ButtonComponent SetLength => GetValue(SetLengthProperty);

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
            yield return SetLength;
        }

        /// <inheritdoc/>
        protected override Image<BGRA32>? OnRender(EffectApplyArgs args)
        {
            if (_mediaFile?.Video is null) return null;

            float speed = Speed[args.Frame] / 100;
            var start = (int)Start[args.Frame];
            var time = new Frame((int)((start + args.Frame - Parent!.Start) * speed)).ToTimeSpan(Parent.Parent.Parent!.Framerate);

            return _mediaFile.Video.GetFrame(time);
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
                    try
                    {
                        _mediaFile = MediaFile.Open(filename, _options);
                    }
                    catch (Exception e)
                    {
                        var mes = ServiceProvider?.GetService<IMessage>();
                        var msg = string.Format(Strings.FailedToLoad, filename);
                        mes?.Snackbar(msg);
                        LogManager.Logger?.LogError(e, msg);
                    }
                }
                else
                {
                    _mediaFile = null;
                }
            });

            _disposable2 = SetLength.Subscribe(_ =>
            {
                if (_mediaFile?.Video is not null)
                {
                    Parent.ChangeLength(Parent.Start, Parent.Start + _mediaFile.Video.Info.NumberOfFrames).Execute();
                }
            });
        }

        /// <inheritdoc/>
        protected override void OnUnload()
        {
            base.OnUnload();

            _mediaFile?.Dispose();
            _disposable1?.Dispose();
            _disposable2?.Dispose();
        }
    }
}