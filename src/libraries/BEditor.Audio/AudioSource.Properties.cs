using System;
using System.Numerics;

using BEditor.Audio.Resources;

using OpenTK.Audio.OpenAL;

namespace BEditor.Audio
{
    public partial class AudioSource
    {
        private AudioBuffer? _buffer;

        /// <summary>
        /// Gets or sets the buffer that will serve the sound sample.
        /// </summary>
        /// <exception cref="ObjectDisposedException">This object has been discarded.</exception>
        /// <exception cref="AudioException">An error has occurred in OpenAL.</exception>
        /// <exception cref="ArgumentNullException">value is null.</exception>
        public AudioBuffer? Buffer
        {
            get
            {
                ThrowIfDisposed();

                return _buffer;
            }
            set
            {
                if (value is null) throw new ArgumentNullException(nameof(value));
                ThrowIfDisposed();
                SetInt(ALSourcei.Buffer, value.Handle);
                _buffer = value;
            }
        }
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
        /// Gets or sets the angle of the inner cone angle, in degree.
        /// <para>Range: [0 - 360] Default: 360</para>
        /// </summary>
        /// <exception cref="ObjectDisposedException">This object has been discarded.</exception>
        /// <exception cref="AudioException">An error has occurred in OpenAL.</exception>
        public float ConeInnerAngle
        {
            get => GetFloat(ALSourcef.ConeInnerAngle);
            set
            {
                value = Math.Clamp(value, 0, 360);
                SetFloat(ALSourcef.ConeInnerAngle, value);
            }
        }
        /// <summary>
        /// Gets or sets the angle of the outer cone angle, in degree.
        /// <para>Range: [0 - 360] Default: 360</para>
        /// </summary>
        /// <exception cref="ObjectDisposedException">This object has been discarded.</exception>
        /// <exception cref="AudioException">An error has occurred in OpenAL.</exception>
        public float ConeOuterAngle
        {
            get => GetFloat(ALSourcef.ConeOuterAngle);
            set
            {
                value = Math.Clamp(value, 0, 360);
                SetFloat(ALSourcef.ConeOuterAngle, value);
            }
        }
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
        /// <summary>
        /// Gets or sets the minimum attenuation of the source.
        /// <para>Range: [0.0f - 1.0f]</para>
        /// </summary>
        /// <exception cref="ObjectDisposedException">This object has been discarded.</exception>
        /// <exception cref="AudioException">An error has occurred in OpenAL.</exception>
        public float MinGain
        {
            get => GetFloat(ALSourcef.MinGain);
            set => SetFloat(ALSourcef.MinGain, value);
        }
        /// <summary>
        /// Gets or sets the maximum attenuation of the source.
        /// <para>Range: [0.0f - 1.0f]</para>
        /// </summary>
        /// <exception cref="ObjectDisposedException">This object has been discarded.</exception>
        /// <exception cref="AudioException">An error has occurred in OpenAL.</exception>
        public float MaxGain
        {
            get => GetFloat(ALSourcef.MaxGain);
            set => SetFloat(ALSourcef.MaxGain, value);
        }
        /// <summary>
        /// Gets or sets the source-specific reference distance.
        /// <para>Range: [0.0f - float.PositiveInfinity] Default: 1.0f</para>
        /// </summary>
        /// <exception cref="ObjectDisposedException">This object has been discarded.</exception>
        /// <exception cref="AudioException">An error has occurred in OpenAL.</exception>
        public float ReferenceDistance
        {
            get => GetFloat(ALSourcef.ReferenceDistance);
            set => SetFloat(ALSourcef.ReferenceDistance, value);
        }
        /// <summary>
        /// Gets or sets the source-specific roll-off factor.
        /// <para>Range: [0.0f - float.PositiveInfinity]</para>
        /// </summary>
        /// <exception cref="ObjectDisposedException">This object has been discarded.</exception>
        /// <exception cref="AudioException">An error has occurred in OpenAL.</exception>
        public float RolloffFactor
        {
            get => GetFloat(ALSourcef.RolloffFactor);
            set => SetFloat(ALSourcef.RolloffFactor, value);
        }
        /// <summary>
        /// Gets or sets the outer cone gain.
        /// <para>Range: [0.0f - 1.0] Default: 0.0f</para>
        /// </summary>
        /// <exception cref="ObjectDisposedException">This object has been discarded.</exception>
        /// <exception cref="AudioException">An error has occurred in OpenAL.</exception>
        public float ConeOuterGain
        {
            get => GetFloat(ALSourcef.ConeOuterGain);
            set => SetFloat(ALSourcef.ConeOuterGain, value);
        }
        /// <summary>
        /// Get or set the distance above which the source will not decay using the inverse clamp distance model.
        /// <para>Range: [0.0f - float.PositiveInfinity] Default: float.PositiveInfinity</para>
        /// </summary>
        /// <exception cref="ObjectDisposedException">This object has been discarded.</exception>
        /// <exception cref="AudioException">An error has occurred in OpenAL.</exception>
        public float MaxDistance
        {
            get => GetFloat(ALSourcef.MaxDistance);
            set => SetFloat(ALSourcef.MaxDistance, value);
        }
        /// <summary>
        /// Gets or sets the number of seconds for the playback position.
        /// </summary>
        /// <exception cref="ObjectDisposedException">This object has been discarded.</exception>
        /// <exception cref="AudioException">An error has occurred in OpenAL.</exception>
        public float SecOffset
        {
            get => GetFloat(ALSourcef.SecOffset);
            set => SetFloat(ALSourcef.SecOffset, value);
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

        #region 3Float
        /// <summary>
        /// Gets or sets the current position.
        /// </summary>
        /// <exception cref="ObjectDisposedException">This object has been discarded.</exception>
        /// <exception cref="AudioException">An error has occurred in OpenAL.</exception>
        public Vector3 Position
        {
            get => Get3Float(ALSource3f.Position);
            set => Set3Float(ALSource3f.Position, value.X, value.Y, value.Z);
        }
        /// <summary>
        /// Gets or sets the current direction vector.
        /// </summary>
        /// <exception cref="ObjectDisposedException">This object has been discarded.</exception>
        /// <exception cref="AudioException">An error has occurred in OpenAL.</exception>
        public Vector3 Direction
        {
            get => Get3Float(ALSource3f.Direction);
            set => Set3Float(ALSource3f.Direction, value.X, value.Y, value.Z);
        }
        /// <summary>
        /// Gets or sets the current velocity in three-dimensional space.
        /// </summary>
        /// <exception cref="ObjectDisposedException">This object has been discarded.</exception>
        /// <exception cref="AudioException">An error has occurred in OpenAL.</exception>
        public Vector3 Velocity
        {
            get => Get3Float(ALSource3f.Velocity);
            set => Set3Float(ALSource3f.Velocity, value.X, value.Y, value.Z);
        }

        private Vector3 Get3Float(ALSource3f type)
        {
            ThrowIfDisposed();
            AL.GetSource(Handle, type, out var v);

            ThrowError();
            return v.ToNumerics();
        }
        private void Set3Float(ALSource3f type, float v1, float v2, float v3)
        {
            ThrowIfDisposed();
            AL.Source(Handle, type, v1, v2, v3);

            ThrowError();
        }
        #endregion

        #region Bool
        /// <summary>
        /// Gets or sets whether the source has relative coordinates.
        /// </summary>
        /// <exception cref="ObjectDisposedException">This object has been discarded.</exception>
        /// <exception cref="AudioException">An error has occurred in OpenAL.</exception>
        public bool SourceRelative
        {
            get => GetBool(ALSourceb.SourceRelative);
            set => SetBool(ALSourceb.SourceRelative, value);
        }
        /// <summary>
        /// Gets or sets whether the source is looping or not.
        /// </summary>
        /// <exception cref="ObjectDisposedException">This object has been discarded.</exception>
        /// <exception cref="AudioException">An error has occurred in OpenAL.</exception>
        public bool Looping
        {
            get => GetBool(ALSourceb.Looping);
            set => SetBool(ALSourceb.Looping, value);
        }

        internal bool GetBool(ALSourceb type)
        {
            ThrowIfDisposed();
            AL.GetSource(Handle, type, out var v);

            ThrowError();

            return v;
        }
        internal void SetBool(ALSourceb type, bool value)
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