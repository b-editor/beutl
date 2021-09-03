// AudioFile.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;

using BEditor.Audio;
using BEditor.Data;
using BEditor.Data.Primitive;
using BEditor.Data.Property;
using BEditor.Media;
using BEditor.Media.Decoding;
using BEditor.Media.Encoding;
using BEditor.Media.PCM;
using BEditor.Primitive.Resources;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BEditor.Primitive.Objects
{
    /// <summary>
    /// Represents an <see cref="ObjectElement"/> that references an audio file.
    /// </summary>
    public sealed class AudioFile : AudioObject, IMediaObject
    {
        /// <summary>
        /// Defines the <see cref="Start"/> property.
        /// </summary>
        public static readonly DirectProperty<AudioFile, ValueProperty> StartProperty = EditingProperty.RegisterDirect<ValueProperty, AudioFile>(
            nameof(Start),
            owner => owner.Start,
            (owner, obj) => owner.Start = obj,
            EditingPropertyOptions<ValueProperty>.Create(new ValuePropertyMetadata(Strings.Start + "(Milliseconds)", 0)).Serialize());

        /// <summary>
        /// Defines the <see cref="File"/> property.
        /// </summary>
        public static readonly DirectProperty<AudioFile, FileProperty> FileProperty = EditingProperty.RegisterDirect<FileProperty, AudioFile>(
            nameof(File),
            owner => owner.File,
            (owner, obj) => owner.File = obj,
            EditingPropertyOptions<FileProperty>.Create(
                new FilePropertyMetadata(
                    Strings.File,
                    Filter: new(Strings.AudioFile, DecodingRegistory.EnumerateDecodings()
                        .SelectMany(i => i.SupportExtensions())
                        .Distinct()
                        .Select(i => i.Trim('.'))
                        .ToArray())))
            .Serialize());

        // リソース
        private ResourceItem? _resource;

        // リソースへの参照を切る
        private IDisposable? _disposable;

        // File.Subscribe
        private IDisposable? _disposable1;

        /// <summary>
        /// Initializes a new instance of the <see cref="AudioFile"/> class.
        /// </summary>
        public AudioFile()
        {
        }

        /// <inheritdoc/>
        public override string Name => Strings.Audio;

        /// <summary>
        /// Gets the <see cref="EaseProperty"/> that represents the start position.
        /// </summary>
        [AllowNull]
        public ValueProperty Start { get; private set; }

        /// <summary>
        /// Gets the <see cref="FileProperty"/> to select the file to reference.
        /// </summary>
        [AllowNull]
        public FileProperty File { get; private set; }

        /// <inheritdoc/>
        public TimeSpan? Length => (_resource?.Value as Sound<StereoPCMFloat>)?.Duration;

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
        /// <returns>A new instance of <see cref="AudioFile"/>.</returns>
        public static AudioFile FromFile(string file)
        {
            return new AudioFile
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
            yield return Volume;
            yield return Start;
            yield return File;
        }

        /// <inheritdoc/>
        public override Sound<StereoPCMFloat>? OnSample(EffectApplyArgs args)
        {
            if (_resource?.Value is not Sound<StereoPCMFloat> sound) return null;

            var proj = Parent.Parent.Parent;
            var context = Parent.Parent.SamplingContext!;
            var start = (args.Frame - Parent.Start).ToTimeSpan(proj.Framerate);
            start = start.Add(TimeSpan.FromMilliseconds(Start.Value));
            var length = TimeSpan.FromSeconds(context.SamplePerFrame / (double)proj.Samplingrate);

            if (start >= TimeSpan.Zero)
            {
                // 開始位置がZero以上
                return sound.Slice(start, length).Clone();
            }
            else
            {
                return new Sound<StereoPCMFloat>(proj.Samplingrate, length);
            }
        }

        /// <inheritdoc/>
        protected override void OnLoad()
        {
            base.OnLoad();
            _disposable1 = File.Subscribe(file =>
            {
                _disposable?.Dispose();

                if (System.IO.File.Exists(File.Value))
                {
                    var mes = ServiceProvider?.GetService<IMessage>();

                    try
                    {
                        var project = this.GetRequiredParent<Project>();
                        _resource = new("Audio " + file, () =>
                        {
                            using var mediafile = MediaFile.Open(file, new()
                            {
                                StreamsToLoad = MediaMode.Audio,
                                SampleRate = project.Samplingrate,
                            });

                            if (mediafile.Audio is null)
                                return null;

                            return GetAllFrame(mediafile.Audio);
                        });
                        _resource = project.Resources.RegisterResource(_resource);
                        _disposable = _resource.MakeReference(this);

                        _resource.Build();
                    }
                    catch (DecoderNotFoundException e)
                    {
                        mes?.Snackbar(Strings.DecoderNotFound, string.Empty, IMessage.IconType.Warning);
                        ServicesLocator.Current.Logger?.LogError(e, Strings.DecoderNotFound);
                    }
                    catch (Exception ex)
                    {
                        mes?.Snackbar(string.Format(Strings.FailedToLoad, file), string.Empty, IMessage.IconType.Error);
                        ServicesLocator.Current.Logger?.LogError(ex, Strings.DecoderNotFound);
                    }
                }
                else
                {
                    _disposable = null;
                    _resource = null;
                }
            });
        }

        /// <inheritdoc/>
        protected override void OnUnload()
        {
            base.OnUnload();
            _disposable?.Dispose();
            _disposable1?.Dispose();
            _disposable = null;
            _disposable1 = null;
            _resource = null;
        }

        private static Sound<StereoPCMFloat> GetAllFrame(IAudioStream stream)
        {
            return stream.GetFrame(TimeSpan.Zero, stream.Info.Duration);
        }
    }
}