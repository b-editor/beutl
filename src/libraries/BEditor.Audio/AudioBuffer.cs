using System;
using System.Collections.Generic;

using BEditor.Media;
using BEditor.Media.PCM;

using OpenTK.Audio.OpenAL;

using static BEditor.Audio.AudioSource;

namespace BEditor.Audio
{
    public class AudioBuffer : AudioLibraryObject, IEquatable<AudioBuffer?>
    {
        public AudioBuffer(Sound<PCM16> sound)
        {
            Handle = Tool.GenBuffer();

            Tool.BufferData(Handle, ALFormat.Mono16, sound.Data, sound.SampleRate);
        }
        public AudioBuffer(Sound<StereoPCM16> sound)
        {
            Handle = Tool.GenBuffer();

            Tool.BufferData(Handle, ALFormat.Stereo16, sound.Data, sound.SampleRate);
        }
        public AudioBuffer(Sound<PCMFloat> sound)
        {
            Handle = Tool.GenBuffer();

            using var converted = sound.Convert<PCM16>();

            Tool.BufferData(Handle, ALFormat.Mono16, converted.Data, converted.SampleRate);
        }
        public AudioBuffer(Sound<StereoPCMFloat> sound)
        {
            Handle = Tool.GenBuffer();

            using var converted = sound.Convert<StereoPCM16>();

            Tool.BufferData(Handle, ALFormat.Stereo16, converted.Data, converted.SampleRate);
        }
        public AudioBuffer()
        {
            Handle = Tool.GenBuffer();
        }

        public AudioBuffer(int handle)
        {
            if (!AL.IsBuffer(handle))
            {
                throw new AudioException();
            }

            Handle = handle;
        }

        public override int Handle { get; }
        /// <summary>
        /// Gets the frequency of the sound sample.
        /// </summary>
        /// <exception cref="ObjectDisposedException">This object has been discarded.</exception>
        /// <exception cref="AudioException">An error has occurred in OpenAL.</exception>
        public int Frequency
        {
            get
            {
                ThrowIfDisposed();
                Tool.GetBuffer(Handle, ALGetBufferi.Frequency, out var v);
                CheckError();

                return v;
            }
        }
        /// <summary>
        /// Gets the bit depth of the buffer.
        /// </summary>
        /// <exception cref="ObjectDisposedException">This object has been discarded.</exception>
        /// <exception cref="AudioException">An error has occurred in OpenAL.</exception>
        public int Bits
        {
            get
            {
                ThrowIfDisposed();
                Tool.GetBuffer(Handle, ALGetBufferi.Bits, out var v);
                CheckError();

                return v;
            }
        }
        /// <summary>
        /// Gets the number of channels in the buffer.
        /// </summary>
        /// <exception cref="ObjectDisposedException">This object has been discarded.</exception>
        /// <exception cref="AudioException">An error has occurred in OpenAL.</exception>
        public int Channels
        {
            get
            {
                ThrowIfDisposed();
                Tool.GetBuffer(Handle, ALGetBufferi.Channels, out var v);

                return v;
            }
        }
        /// <summary>
        /// Gets the size of the buffer in bytes.
        /// </summary>
        /// <exception cref="ObjectDisposedException">This object has been discarded.</exception>
        /// <exception cref="AudioException">An error has occurred in OpenAL.</exception>
        public int Size
        {
            get
            {
                ThrowIfDisposed();
                Tool.GetBuffer(Handle, ALGetBufferi.Size, out var v);

                return v;
            }
        }

        public override bool Equals(object? obj)
        {
            return Equals(obj as AudioBuffer);
        }
        public bool Equals(AudioBuffer? other)
        {
            return other != null &&
                   Handle == other.Handle;
        }
        public override int GetHashCode()
        {
            return HashCode.Combine(Handle);
        }
        protected override void OnDispose()
        {
            Tool.DeleteBuffer(Handle);
        }

        public static bool operator ==(AudioBuffer? left, AudioBuffer? right)
        {
            return EqualityComparer<AudioBuffer>.Default.Equals(left, right);
        }
        public static bool operator !=(AudioBuffer? left, AudioBuffer? right)
        {
            return !(left == right);
        }
    }
}