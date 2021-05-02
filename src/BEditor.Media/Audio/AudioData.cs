using System;

using BEditor.Media.Common.Internal;
using BEditor.Media.PCM;

namespace BEditor.Media.Audio
{
    /// <summary>
    /// Represents a lightweight container for audio data.
    /// </summary>
    public ref struct AudioData
    {
        private readonly AudioFrame _frame;

        /// <summary>
        /// Initializes a new instance of the <see cref="AudioData"/> struct.
        /// </summary>
        /// <param name="frame">frame object containing raw audio data.</param>
        internal AudioData(AudioFrame frame)
        {
            this._frame = frame;
        }

        /// <summary>
        /// Gets the number of samples.
        /// </summary>
        public int NumSamples => _frame.NumSamples;

        /// <summary>
        /// Gets the number of channels.
        /// </summary>
        public int NumChannels => _frame.NumChannels;

        /// <summary>
        /// Fetches raw audio data from this audio frame for specified channel.
        /// </summary>
        /// <param name="channel">The index of audio channel that should be retrieved, allowed range: [0..<see cref="NumChannels"/>).</param>
        /// <returns>The span with samples in range of [-1.0, ..., 1.0].</returns>
        public Span<float> GetChannelData(uint channel)
        {
            return _frame.GetChannelData(channel);
        }

        /// <summary>
        /// Copies raw multichannel audio data from this frame to a heap allocated array.
        /// </summary>
        /// <returns>
        /// The span with <see cref="NumChannels"/> rows and <see cref="NumSamples"/> columns;
        /// samples in range of [-1.0, ..., 1.0].
        /// </returns>
        public float[][] GetSampleData()
        {
            return _frame.GetSampleData();
        }

        /// <summary>
        /// Updates the specified channel of this audio frame with the given sample data.
        /// </summary>
        /// <param name="samples">An array of samples with length <see cref="NumSamples"/>.</param>
        /// <param name="channel">The index of audio channel that should be updated, allowed range: [0..<see cref="NumChannels"/>).</param>
        public void UpdateChannelData(float[] samples, uint channel)
        {
            _frame.UpdateChannelData(samples, channel);
        }

        /// <summary>
        /// Updates this audio frame with the specified multi-channel sample data.
        /// </summary>
        /// <param name="samples">
        /// A 2D jagged array of multi-channel sample data
        /// with <see cref="NumChannels"/> rows and <see cref="NumSamples"/> columns.
        /// </param>
        public void UpdateFromSampleData(float[][] samples)
        {
            _frame.UpdateFromSampleData(samples);
        }

        /// <summary>
        /// Releases all unmanaged resources associated with this instance.
        /// </summary>
        public void Dispose()
        {
            _frame.Dispose();
        }

        public Sound<StereoPCMFloat> ToSound()
        {
            var sound = new Sound<StereoPCMFloat>(_frame.SampleRate, NumSamples);

            var left = GetChannelData(0);
            var right = NumChannels == 1 ? left : GetChannelData(1);

            for (var i = 0; i < sound.Data.Length; i++)
            {
                sound.Data[i] = new StereoPCMFloat(left[i], right[i]);
            }

            return sound;
        }
    }
}
