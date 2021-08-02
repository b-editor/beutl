using System;
using System.Numerics;

using BEditor.Audio.Resources;

using OpenTK.Audio.OpenAL;

namespace BEditor.Audio
{
    public partial class AudioSource
    {
        /// <summary>
        /// Gets the status of this AudioSource.
        /// </summary>
        /// <exception cref="ObjectDisposedException">This object has been discarded.</exception>
        /// <exception cref="AudioException">An error has occurred in OpenAL.</exception>
        public AudioSourceState State
        {
            get
            {
                ThrowIfDisposed();
                var r = (AudioSourceState)AL.GetSourceState(Handle);
                CheckError();

                return r;
            }
        }

        /// <summary>
        /// Gets or sets the source type (static, streaming, or undetermined).
        /// </summary>
        /// <exception cref="ObjectDisposedException">This object has been discarded.</exception>
        /// <exception cref="AudioException">An error has occurred in OpenAL.</exception>
        public AudioSourceType SourceType
        {
            get => (AudioSourceType)GetInt(ALGetSourcei.SourceType);
            set => SetInt(ALSourcei.SourceType, (int)value);
        }

        #region Float
        /// <summary>
        /// Gets or sets the pitch to be applied to the source or mixer result.
        /// <para>Range: [0.5f - 2.0f] Default: 1.0f</para>
        /// </summary>
        /// <exception cref="ObjectDisposedException">This object has been discarded.</exception>
        /// <exception cref="AudioException">An error has occurred in OpenAL.</exception>
        public float Pitch
        {
            get => GetFloat(ALSourcef.Pitch);
            set
            {
                value = Math.Clamp(value, 0.5f, 2.0f);
                SetFloat(ALSourcef.Pitch, value);
            }
        }
        /// <summary>
        /// Gets or sets the applied gain (volume amplification).
        /// <para>Range: [0.0f - ?]</para>
        /// </summary>
        /// <exception cref="ObjectDisposedException">This object has been discarded.</exception>
        /// <exception cref="AudioException">An error has occurred in OpenAL.</exception>
        public float Gain
        {
            get => GetFloat(ALSourcef.Gain);
            set => SetFloat(ALSourcef.Gain, value);
        }

        internal float GetFloat(ALSourcef type)
        {
            ThrowIfDisposed();
            AL.GetSource(Handle, type, out var v);

            ThrowError();
            return v;
        }
        internal void SetFloat(ALSourcef type, float value)
        {
            ThrowIfDisposed();
            AL.Source(Handle, type, value);

            ThrowError();
        }
        #endregion

        #region Int
        /// <summary>
        /// Gets or sets the playback position as samples.
        /// </summary>
        /// <exception cref="ObjectDisposedException">This object has been discarded.</exception>
        /// <exception cref="AudioException">An error has occurred in OpenAL.</exception>
        public int SampleOffset
        {
            get => GetInt(ALGetSourcei.SampleOffset);
            set => SetInt(ALSourcei.SampleOffset, value);
        }
        /// <summary>
        /// Gets the number of buffers queued on this source.
        /// </summary>
        /// <exception cref="ObjectDisposedException">This object has been discarded.</exception>
        /// <exception cref="AudioException">An error has occurred in OpenAL.</exception>
        public int BuffersQueued
        {
            get => GetInt(ALGetSourcei.BuffersQueued);
        }
        /// <summary>
        /// Gets the number of buffers in the queue that have been processed.
        /// </summary>
        /// <exception cref="ObjectDisposedException">This object has been discarded.</exception>
        /// <exception cref="AudioException">An error has occurred in OpenAL.</exception>
        public int BuffersProcessed
        {
            get => GetInt(ALGetSourcei.BuffersProcessed);
        }
        /// <summary>
        /// Gets or sets the playback position as bytes.
        /// </summary>
        /// <exception cref="ObjectDisposedException">This object has been discarded.</exception>
        /// <exception cref="AudioException">An error has occurred in OpenAL.</exception>
        public int ByteOffset
        {
            get => GetInt(ALGetSourcei.ByteOffset);
            set => SetInt(ALSourcei.ByteOffset, value);
        }

        internal int GetInt(ALGetSourcei type)
        {
            ThrowIfDisposed();
            AL.GetSource(Handle, type, out var v);

            ThrowError();

            return v;
        }
        internal void SetInt(ALSourcei type, int value)
        {
            ThrowIfDisposed();
            AL.Source(Handle, type, value);

            ThrowError();
        }
        #endregion

        internal static void CheckError()
        {
            var error = AL.GetError();

            if (error is not ALError.NoError)
            {
                throw new AudioException(AL.GetErrorString(error));
            }
        }
        private static void ThrowError()
        {
            var error = AL.GetError();

            if (error is ALError.InvalidValue)
            {
                throw new AudioException(Strings.ThrowErrorInvalidValue);
            }
            else if (error is ALError.InvalidEnum)
            {
                throw new AudioException(Strings.ThrowErrorInvalidEnum);
            }
            else if (error is ALError.InvalidName)
            {
                throw new AudioException(Strings.ThrowErrorInvalidName);
            }
            else if (error is ALError.InvalidOperation)
            {
                throw new AudioException(Strings.ThrowErrorInvalidOperation);
            }
        }
    }
}