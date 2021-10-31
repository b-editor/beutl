// AudioFile.cs
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
using BEditor.LangResources;
using BEditor.Media;
using BEditor.Media.Decoding;
using BEditor.Media.PCM;

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
                        .SelectMany(i => i.GetSupportedAudioExt())
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
        public TimeSpan? Length
            => (_resource?.Value as Sound<StereoPCMFloat>)?.DurationRational is Rational duration
                ? TimeSpan.FromSeconds(duration)
                : null;

        /// <summary>
        /// Gets whether the file name is supported.
        /// </summary>
        /// <param name="file">The name of the file to check if it is supported.</param>
        /// <returns>Returns true if supported, false otherwise.</returns>
        public static bool IsSupported(string file)
        {
            var ext = Path.GetExtension(file);
            return DecodingRegistory.EnumerateDecodings()
                .SelectMany(i => i.GetSupportedAudioExt())
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

            // ここではフレーム数
            var start = new Rational(args.Frame - Parent.Start, 1);
            start.Numerator += Frame.FromMilliseconds(Start.Value, proj.Framerate);

            // サンプル数に変換
            start *= context.SpfRational;

            if (start >= 0)
            {
                // 開始位置がZero以上
                return sound.Slice(start, context.SpfRational).Clone();
            }
            else
            {
                return new Sound<StereoPCMFloat>(proj.Samplingrate, context.SpfRational);
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
                        mes?.Snackbar(
                            Strings.DecoderNotFound,
                            string.Empty,
                            IMessage.IconType.Warning,
                            actionName: Strings.SearchForDecoder,
                            action: _ => this.GetParent<IApplication>()?.Navigate("beditor://manage-plugin/search", "decoder decoding"));

                        ServicesLocator.Current.Logger?.LogError(e, "Decoder not found.");
                    }
                    catch (Exception ex)
                    {
                        mes?.Snackbar(string.Format(Strings.FailedToLoad, file), string.Empty, IMessage.IconType.Error);
                        ServicesLocator.Current.Logger?.LogError(ex, "Failed to load {fileName}.", file);
                    }
                }
                else
                {
                    _disposable = null;
                    _resource = null;
                }
            });

            var clip = this.GetParent<ClipElement>();
            if (clip != null)
            {
                clip.Splitted += Clip_Splitted;
            }
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

            var clip = this.GetParent<ClipElement>();
            if (clip != null)
            {
                clip.Splitted -= Clip_Splitted;
            }
        }

        private static Sound<StereoPCMFloat> GetAllFrame(IAudioStream stream)
        {
            return stream.GetFrame(TimeSpan.Zero, stream.Info.NumSamples);
        }

        private void Clip_Splitted(object? sender, ClipSplittedEventArgs e)
        {
            if (e.After.Effect[0] is AudioFile after)
            {
                var sub = (float)e.Before.Length.ToMilliseconds(this.GetRequiredParent<Project>().Framerate);
                after.Start.Value += sub;
            }
        }
    }
}
