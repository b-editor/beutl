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
using BEditor.Media;
using BEditor.Media.PCM;

using OpenTK.Audio.OpenAL;

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
                Scene.GetRequiredParent<IApplication>().AudioContext?.MakeCurrent();
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