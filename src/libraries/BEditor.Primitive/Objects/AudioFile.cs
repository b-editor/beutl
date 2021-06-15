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
using BEditor.Media.PCM;
using BEditor.Primitive.Resources;

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
        public static readonly DirectEditingProperty<AudioFile, ValueProperty> StartProperty = EditingProperty.RegisterDirect<ValueProperty, AudioFile>(
            nameof(Start),
            owner => owner.Start,
            (owner, obj) => owner.Start = obj,
            EditingPropertyOptions<ValueProperty>.Create(new ValuePropertyMetadata(Strings.Start + "(Milliseconds)", 0, Min: 0)).Serialize());

        /// <summary>
        /// Defines the <see cref="File"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<AudioFile, FileProperty> FileProperty = EditingProperty.RegisterDirect<FileProperty, AudioFile>(
            nameof(File),
            owner => owner.File,
            (owner, obj) => owner.File = obj,
            EditingPropertyOptions<FileProperty>.Create(new FilePropertyMetadata(Strings.File, Filter: new(string.Empty, new FileExtension[] { new("mp3"), new("wav") }))).Serialize());

        private MediaFile? _mediaFile;

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
        /// Get the <see cref="EaseProperty"/> that represents the start position.
        /// </summary>
        [AllowNull]
        public ValueProperty Start { get; private set; }

        /// <summary>
        /// Get the <see cref="FileProperty"/> to select the file to reference.
        /// </summary>
        [AllowNull]
        public FileProperty File { get; private set; }

        /// <summary>
        /// Gets the opened decoder.
        /// </summary>
        public MediaFile? Decoder
        {
            get => _mediaFile;
            set
            {
                _mediaFile?.Dispose();
                _mediaFile = value;
            }
        }

        /// <inheritdoc/>
        public TimeSpan? Length => Decoder?.Audio?.Info?.Duration;

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> GetProperties()
        {
            yield return Volume;
            yield return Pitch;
            yield return Start;
            yield return File;
        }

        /// <inheritdoc/>
        protected override void OnLoad()
        {
            _disposable1 = File.Where(file => System.IO.File.Exists(file)).Subscribe(file =>
            {
                Decoder = MediaFile.Open(file, new()
                {
                    StreamsToLoad = MediaMode.Audio,
                    SampleRate = this.GetRequiredParent<Project>().Samplingrate,
                });
            });
        }

        /// <inheritdoc/>
        protected override void OnUnload()
        {
            _disposable1?.Dispose();
            _mediaFile?.Dispose();
            _mediaFile = null;
        }

        /// <inheritdoc/>
        protected override Sound<StereoPCMFloat>? OnSample(EffectApplyArgs args)
        {
            if (Decoder is null) return null;

            var proj = Parent.Parent.Parent;
            var context = Parent.Parent.SamplingContext!;
            var start = (args.Frame - Parent.Start).ToTimeSpan(proj.Framerate);
            var length = TimeSpan.FromSeconds(context.SamplePerFrame / (double)proj.Samplingrate);
            return Decoder.Audio?.GetFrame(start, length);
        }
    }
}