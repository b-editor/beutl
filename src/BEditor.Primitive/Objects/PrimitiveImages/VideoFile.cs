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
        public static readonly DirectEditingProperty<VideoFile, EaseProperty> SpeedProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, VideoFile>(
            nameof(Speed),
            owner => owner.Speed,
            (owner, obj) => owner.Speed = obj,
            new EasePropertyMetadata(Strings.Speed, 100));

        /// <summary>
        /// Defines the <see cref="Start"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<VideoFile, EaseProperty> StartProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, VideoFile>(
            nameof(Start),
            owner => owner.Start,
            (owner, obj) => owner.Start = obj,
            new EasePropertyMetadata(Strings.Start, 1, float.NaN, 0));

        /// <summary>
        /// Defines the <see cref="File"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<VideoFile, FileProperty> FileProperty = EditingProperty.RegisterSerializeDirect<FileProperty, VideoFile>(
            nameof(File),
            owner => owner.File,
            (owner, obj) => owner.File = obj,
            new FilePropertyMetadata(Strings.File, "", new(Strings.VideoFile, new FileExtension[]
            {
                new("mp4"),
                new("avi"),
                new("wmv"),
                new("mov")
            })));

        /// <summary>
        /// Defines the <see cref="SetLength"/> property.
        /// </summary>
        public static readonly EditingProperty<ButtonComponent> SetLengthProperty = EditingProperty.Register<ButtonComponent, VideoFile>(
            nameof(SetLength),
            new ButtonComponentMetadata(Strings.ClipLengthAsVideoLength));

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
        /// 
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

            return _mediaFile.Video.GetFrame(time).ToDrawing();
        }

        /// <inheritdoc/>
        protected override void OnLoad()
        {
            base.OnLoad();

            if (System.IO.File.Exists(File.Value))
            {
                _mediaFile = MediaFile.Open(File.Value);
            }

            _disposable1 = File.Subscribe(filename =>
            {
                _mediaFile?.Dispose();

                try
                {
                    _mediaFile = MediaFile.Open(filename);
                }
                catch (Exception)
                {
                    var mes = ServiceProvider?.GetService<IMessage>();
                    mes?.Snackbar(string.Format(Strings.FailedToLoad, filename));
                }
            });

            _disposable2 = SetLength.Where(_ => _mediaFile?.Video?.Info?.NumberOfFrames is not null).Subscribe(_ =>
            {
                Parent.ChangeLength(Parent.Start, Parent.Start + (int)_mediaFile!.Video!.Info.NumberOfFrames!).Execute();
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