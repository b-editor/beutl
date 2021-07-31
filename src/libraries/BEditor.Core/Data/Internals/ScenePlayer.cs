// ScenePlayer.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Threading.Tasks;
using System.Timers;

using BEditor.Audio;
using BEditor.Audio.XAudio2;
using BEditor.Media;
using BEditor.Media.PCM;

using OpenTK.Audio.OpenAL;

using Vortice.Multimedia;

using Timer = System.Timers.Timer;

namespace BEditor.Data.Internals
{
    /// <summary>
    /// Represents a <see cref="Scene"/> player.
    /// </summary>
    internal class ScenePlayer : IPlayer
    {
        private readonly Timer _timer;
        private readonly double _framerate;
        private DateTime _startTime;
        private Frame _startframe;

        /// <summary>
        /// Initializes a new instance of the <see cref="ScenePlayer"/> class.
        /// </summary>
        /// <param name="scene">This is the scene to play.</param>
        public ScenePlayer(Scene scene)
        {
            Scene = scene;
            _framerate = scene.Parent!.Framerate;

            _timer = new Timer
            {
                Interval = 1d / _framerate,
            };

            _timer.Elapsed += Timer_Elapsed;
        }

        /// <inheritdoc/>
        public event EventHandler<PlayingEventArgs>? Playing;

        /// <inheritdoc/>
        public event EventHandler? Stopped;

        /// <summary>
        /// Gets the scene to play.
        /// </summary>
        public Scene Scene { get; }

        /// <inheritdoc/>
        public PlayerState State { get; private set; } = PlayerState.Stop;

        /// <inheritdoc/>
        public Frame CurrentFrame { get; private set; }

        /// <inheritdoc/>
        public void Play()
        {
            if (State is PlayerState.Playing) return;

            GC.Collect();
            State = PlayerState.Playing;
            _startTime = DateTime.Now;
            _startframe = Scene.PreviewFrame;

            _timer.Start();

            Playing?.Invoke(this, new(_startframe));

            Task.Run(() =>
            {
                var context = Scene.GetRequiredParent<IApplication>().AudioContext;
                if (context is AudioContext audioContext)
                {
                    PlayViaOpenAL(audioContext);
                }
                else if (context is XAudioContext xcontext)
                {
                    PlayViaXAudio2(xcontext);
                }
            });
        }

        /// <inheritdoc/>
        public void Stop()
        {
            State = PlayerState.Stop;

            _timer.Stop();

            GC.Collect();
            Stopped?.Invoke(this, EventArgs.Empty);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            _timer.Dispose();

            GC.SuppressFinalize(this);
        }

        private void PlayViaXAudio2(XAudioContext audioContext)
        {
            var context = Scene.SamplingContext!;
            context.Clear();
            int f = Scene.PreviewFrame;
            var sound = new Sound<StereoPCM16>(Scene.Parent.Samplingrate, Scene.Parent.Samplingrate);
            var fmt = new WaveFormat(Scene.Parent.Samplingrate, 2);
            using var source = new XAudioSource(audioContext);
            using var buffer = new XAudioBuffer();
            FillAudioData(sound, f);

            buffer.BufferData(sound.Data, fmt);
            source.QueueBuffer(buffer);
            source.Play();

            f += Scene.Parent.Framerate;

            while (f < Scene.TotalFrame)
            {
                if (State is PlayerState.Stop) break;

                FillAudioData(sound, f);

                while (source.BuffersQueued > 0)
                {
                    if (State is PlayerState.Stop) break;
                }

                buffer.BufferData(sound.Data, fmt);
                source.QueueBuffer(buffer);

                f += Scene.Parent.Framerate;
            }

            sound.Dispose();
            buffer.Dispose();
        }

        private void PlayViaOpenAL(AudioContext audioContext)
        {
            audioContext.MakeCurrent();

            var context = Scene.SamplingContext!;
            context.Clear();
            int f = Scene.PreviewFrame;
            var sound = new Sound<StereoPCMFloat>(Scene.Parent.Samplingrate, Scene.Parent.Samplingrate);
            using var buffer = new AudioBuffer();
            using var source = new AudioSource();
            source.QueueBuffer(buffer);
            source.Play();
            var state = 0;

            while (f < Scene.TotalFrame)
            {
                if (State is PlayerState.Stop) break;
                state = source.BuffersProcessed;

                if (state == 1)
                {
                    var bid = source.UnqueueBuffer();
                    FillAudioData(sound, f);

                    buffer.BufferData(sound);
                    source.QueueBuffer(buffer);
                    source.Play();

                    f += Scene.Parent.Framerate;
                }
            }
        }

        private void FillAudioData(Sound<StereoPCMFloat> sound, Frame f)
        {
            var context = Scene.SamplingContext!;
            var spf = context.SamplePerFrame;
            for (var i = 0; i < Scene.Parent.Framerate; i++)
            {
                using var tmp = Scene.Sample(f + i);
                tmp.Data.CopyTo(sound.Data.Slice(i * spf, spf));
            }
        }

        private void FillAudioData(Sound<StereoPCM16> sound, Frame f)
        {
            var context = Scene.SamplingContext!;
            var spf = context.SamplePerFrame;
            for (var i = 0; i < Scene.Parent.Framerate * 3; i++)
            {
                using var tmp = Scene.Sample(f + i);
                using var converted = tmp.Convert<StereoPCM16>();
                converted.Data.CopyTo(sound.Data.Slice(i * spf, spf));
            }
        }

        private void Timer_Elapsed(object sender, ElapsedEventArgs e)
        {
            var time = e.SignalTime - _startTime;
            var frame = Frame.FromTimeSpan(time, _framerate);

            frame += _startframe;

            if (frame > Scene.TotalFrame) Stop();

            CurrentFrame = frame;
            Scene.PreviewFrame = frame;
        }
    }
}