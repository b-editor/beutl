using System;
using System.Threading.Tasks;

using BEditor.Media.PCM;

namespace BEditor.Media
{
    /// <summary>
    /// Provides extended methods for <see cref="Sound{T}"/>.
    /// </summary>
    public static unsafe partial class Sound
    {
        /// <summary>
        /// Converts the data in this <see cref="Sound{T}"/> to the specified type.
        /// </summary>
        /// <typeparam name="TConvert">The type of audio data to convert to.</typeparam>
        /// <typeparam name="TSource">The type of audio data from which to convert.</typeparam>
        /// <param name="sound">The Sound to convert.</param>
        public static Sound<TConvert> Convert<TConvert, TSource>(this Sound<TSource> sound)
            where TConvert : unmanaged, IPCM<TConvert>
            where TSource : unmanaged, IPCM<TSource>, IPCMConvertable<TConvert>
        {
            var result = new Sound<TConvert>(sound.SampleRate, sound.Length);

            Parallel.For(0, sound.Length, i => sound.Data[i].ConvertTo(out result.Data[i]));

            return result;
        }

        /// <summary>
        /// Set the channel data.
        /// </summary>
        /// <param name="sound">The Sound to set the channel data.</param>
        /// <param name="channel">The number of channels to set.</param>
        /// <param name="data">The channel data to be set.</param>
        public static void SetChannelData(this Sound<StereoPCMFloat> sound, int channel, Span<float> data)
        {
            fixed (StereoPCMFloat* dst = sound.Data)
            {
                var dataf = (float*)dst;
                var soundlength = sound.Length * 2;
                var length = data.Length;
                var l = 0;

                for (var i = channel; l < length && i < soundlength; i += 2, l++)
                {
                    dataf[i] = data[l];
                }
            }
        }

        /// <summary>
        /// Set the channel data.
        /// </summary>
        /// <param name="sound">The Sound to set the channel data.</param>
        /// <param name="start">The number of samples to start with.</param>
        /// <param name="channel">The number of channels to set.</param>
        /// <param name="data">The channel data to be set.</param>
        public static void SetChannelData(this Sound<StereoPCMFloat> sound, int start, int channel, Span<float> data)
        {
#pragma warning disable RCS1176
            fixed (StereoPCMFloat* dst = &sound.Data[start])
#pragma warning restore RCS1176
            {
                var dataf = (float*)dst;
                var soundlength = (sound.Length - start) * 2;
                var length = data.Length;
                var l = 0;

                for (var i = channel; l < length && i < soundlength; i += 2, l++)
                {
                    dataf[i] = data[l];
                }
            }
        }

        /// <summary>
        /// Extracts the channel data into multiple arrays.
        /// </summary>
        /// <param name="sound">The <see cref="Sound{T}"/> that expands the channel data.</param>
        public static float[][] Extract(this Sound<StereoPCMFloat> sound)
        {
            var left = new float[sound.Data.Length];
            var right = new float[sound.Data.Length];

            for (var i = 0; i < sound.Data.Length; i++)
            {
                left[i] = sound.Data[i].Left;
                right[i] = sound.Data[i].Right;
            }

            return new float[][] { left, right };
        }

        /// <summary>
        /// Resamples the <see cref="Sound{T}"/>.
        /// </summary>
        /// <param name="sound">The sound to resamples.</param>
        /// <param name="frequency">The new sampling frequency.</param>
        public static Sound<StereoPCMFloat> Resamples(this Sound<StereoPCMFloat> sound, int frequency)
        {
            if (sound.SampleRate == frequency) return sound.Clone();

            var param = GetConvertParam(sound.SampleRate, frequency);
            var degree = GetDegree(100, ref param);
            var corre = Sinc(100, degree, ref param);
            fixed (void* ptr = sound.Data)
            {
                var resample = Resampling(corre, ref param, new Span<float>(ptr, sound.Length * 2), sound.Length, 2);

                fixed (float* resampled = resample)
                {
                    var sp = new Span<StereoPCMFloat>(resampled, resample.Length / 2);
                    var result = new Sound<StereoPCMFloat>(frequency, sp.Length);

                    sp.CopyTo(result.Data);

                    return result;
                }
            }
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="sound"></param>
        /// <param name="gain"></param>
        public static void Gain(this Sound<StereoPCMFloat> sound, float gain)
        {
            Parallel.For(0, sound.Length, i =>
            {
                sound.Data[i].Left *= gain;
                sound.Data[i].Right *= gain;
            });
        }
    }
}