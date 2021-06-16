// SoundExtension.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

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
        /// <returns>Returns the converted sound.</returns>
        public static Sound<TConvert> Convert<TConvert, TSource>(this Sound<TSource> sound)
            where TConvert : unmanaged, IPCM<TConvert>
            where TSource : unmanaged, IPCM<TSource>, IPCMConvertable<TConvert>
        {
            var result = new Sound<TConvert>(sound.SampleRate, sound.NumSamples);

            Parallel.For(0, sound.NumSamples, i => sound.Data[i].ConvertTo(out result.Data[i]));

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
                var soundlength = sound.NumSamples * 2;
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
#pragma warning disable RCS1176 // Use 'var' instead of explicit type (when the type is not obvious).
            fixed (StereoPCMFloat* dst = &sound.Data[start])
#pragma warning restore RCS1176 // Use 'var' instead of explicit type (when the type is not obvious).
            {
                var dataf = (float*)dst;
                var soundlength = (sound.NumSamples - start) * 2;
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
        /// <returns>Returns an array with the left channel data in the first and the right channel data in the second.</returns>
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
        /// <returns>Returns a sound that has been resampled to the specified frequency.</returns>
        public static Sound<StereoPCMFloat> Resamples(this Sound<StereoPCMFloat> sound, int frequency)
        {
            if (sound.SampleRate == frequency) return sound.Clone();

            var param = GetConvertParam(sound.SampleRate, frequency);
            var degree = GetDegree(100, ref param);
            var corre = Sinc(100, degree, ref param);
            fixed (void* ptr = sound.Data)
            {
                var resample = Resampling(corre, ref param, new Span<float>(ptr, sound.NumSamples * 2), sound.NumSamples, 2);

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
        /// Adjusts the gain of the sound.
        /// </summary>
        /// <param name="sound">The sound to adjust the gain.</param>
        /// <param name="gain">The gain.</param>
        public static void Gain(this Sound<StereoPCMFloat> sound, float gain)
        {
            Parallel.For(0, sound.NumSamples, i =>
            {
                sound.Data[i].Left *= gain;
                sound.Data[i].Right *= gain;
            });
        }

        /// <summary>
        /// Calculates the RMS of a sound.
        /// </summary>
        /// <param name="sound">The sound to calculate RMS.</param>
        /// <returns>Returns the RMS.</returns>
        public static (double Left, double Right) RMS(this Sound<StereoPCMFloat> sound)
        {
            var left = 0.0;
            var right = 0.0;
            var data = sound.Data;
            for (var i = 0; i < data.Length; i++)
            {
                var normalized = data[i];
                left += normalized.Left * normalized.Left;
                right += normalized.Right * normalized.Right;
            }

            left = Math.Sqrt(left / data.Length);
            right = Math.Sqrt(right / data.Length);
            var leftRms = 20 * Math.Log10(left);
            var rightRms = 20 * Math.Log10(right);
            return (double.IsFinite(leftRms) ? leftRms : -90, double.IsFinite(rightRms) ? rightRms : -90);
        }

        /// <summary>
        /// Applies the delay effect.
        /// </summary>
        /// <param name="sound">The sound to apply effect.</param>
        /// <param name="amp">The attenuation rate.</param>
        /// <param name="delay">The delay time.</param>
        /// <param name="repeat">The number of repetitions.</param>
        public static void Delay(this Sound<StereoPCMFloat> sound, float amp, float delay, int repeat)
        {
            using var src_ = sound.Clone();
            var src = src_.Data;
            var dst = sound.Data;
            var d = sound.SampleRate * delay;

            for (var n = 0; n < sound.NumSamples; n++)
            {
                for (var i = 1; i <= repeat; i++)
                {
                    var m = (int)(n - (i * d));

                    if (m >= 0)
                    {
                        var value = MathF.Pow(amp, i);
                        dst[n].Left += value * src[m].Left;
                        dst[n].Right += value * src[m].Right;
                    }
                }
            }
        }
    }
}