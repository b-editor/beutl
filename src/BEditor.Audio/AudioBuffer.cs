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
            Handle = AL.GenBuffer();
            AL.BufferData(Handle, ALFormat.Mono16, sound.Pcm, (int)sound.Samplingrate);
            CheckError();
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
                AL.GetBuffer(Handle, ALGetBufferi.Frequency, out var v);
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
                AL.GetBuffer(Handle, ALGetBufferi.Bits, out var v);
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
                AL.GetBuffer(Handle, ALGetBufferi.Channels, out var v);
                CheckError();

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
                AL.GetBuffer(Handle, ALGetBufferi.Size, out var v);
                CheckError();

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
            AL.DeleteBuffer(Handle);
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
