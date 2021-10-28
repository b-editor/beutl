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
    internal sealed class ScenePlayer : IPlayer
    {
        private const int _streamingBufferSize = 1;
        private readonly Timer _timer;
        private readonly double _frameRate;
        private DateTime _startTime;
        private Frame _startframe;

        /// <summary>
        /// Initializes a new instance of the <see cref="ScenePlayer"/> class.
        /// </summary>
        /// <param name="scene">This is the scene to play.</param>
        public ScenePlayer(Scene scene)
        {
            Scene = scene;
            _frameRate = scene.Parent.Framerate;

            _timer = new Timer
            {
                Interval = 1d / _frameRate,
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
        public double Speed { get; set; } = 1;

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
                    PlayWithOpenAL(audioContext);
                }
                else if (context is XAudioContext xcontext)
                {
                    PlayWithAudio2(xcontext);
                }
            });
        }

        /// <inheritdoc/>
        public void Stop()
        {
            State = PlayerState.Stop;
            if (Scene.GetParent<IApplication>() is IApplication app)
                app.AppStatus = Status.Edit;

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

        private static void Swap<T>(ref T x, ref T y)
        {
            var temp = x;
            x = y;
            y = temp;
        }

        private void PlayWithAudio2(XAudioContext audioContext)
        {
            var context = Scene.SamplingContext!;
            context.Clear();
            int f = Scene.PreviewFrame;
            var primary = new Sound<StereoPCM16>(Scene.Parent.Samplingrate, Scene.Parent.Samplingrate * _streamingBufferSize);
            var secondary = new Sound<StereoPCM16>(Scene.Parent.Samplingrate, Scene.Parent.Samplingrate * _streamingBufferSize);
            var fmt = new WaveFormat(Scene.Parent.Samplingrate, 2);
            var source = new XAudioSource(audioContext);
            var primaryBuffer = new XAudioBuffer();
            var secondaryBuffer = new XAudioBuffer();

            FillAudioData(primary, f);

            primaryBuffer.BufferData(primary.Data, fmt);
            source.QueueBuffer(primaryBuffer);
            source.Play();

            while (f < Scene.TotalFrame)
            {
                if (State is PlayerState.Stop) break;

                f += (Frame)(Scene.Parent.Framerate * Speed * _streamingBufferSize);

                FillAudioData(secondary, f);

                source.Flush();

                while (source.IsPlaying())
                {
                    if (State is PlayerState.Stop) break;
                }

                secondaryBuffer.BufferData(secondary.Data, fmt);
                source.QueueBuffer(secondaryBuffer);

                // バッファを入れ替える
                Swap(ref primary, ref secondary);
                Swap(ref primaryBuffer, ref secondaryBuffer);
            }

            primary.Dispose();
            secondary.Dispose();
            source.Dispose();
            primaryBuffer.Dispose();
            secondaryBuffer.Dispose();
        }

        private async void PlayWithOpenAL(AudioContext audioContext)
        {
            audioContext.MakeCurrent();

            var context = Scene.SamplingContext!;
            context.Clear();
            int f = Scene.PreviewFrame;
            var sound = new Sound<StereoPCM16>(Scene.Parent.Samplingrate, Scene.Parent.Samplingrate * _streamingBufferSize);
            var buffers = AL.GenBuffers(2);
            var source = AL.GenSource();

            foreach (var buffer in buffers)
            {
                FillAudioData(sound, f);

                f += (Frame)(Scene.Parent.Framerate * Speed * _streamingBufferSize);

                AL.BufferData(buffer, ALFormat.Stereo16, sound.Data, sound.SampleRate);
                AL.SourceQueueBuffer(source, buffer);
            }

            AL.SourcePlay(source);

            while (State == PlayerState.Playing)
            {
                AL.GetSource(source, ALGetSourcei.BuffersProcessed, out var processed);
                while (processed > 0)
                {
                    FillAudioData(sound, f);
                    f += (Frame)(Scene.Parent.Framerate * Speed * _streamingBufferSize);

                    var buffer = AL.SourceUnqueueBuffer(source);
                    AL.BufferData(buffer, ALFormat.Stereo16, sound.Data, sound.SampleRate);
                    AL.SourceQueueBuffer(source, buffer);
                    processed--;
                }

                if (f > Scene.TotalFrame)
                    break;
            }

            while (AL.GetSourceState(source) == ALSourceState.Playing)
            {
                await Task.Delay(100);
            }

            sound.Dispose();
            AL.DeleteBuffers(buffers);
            AL.DeleteSource(source);
        }

        private void FillAudioData(Sound<StereoPCM16> sound, Frame f)
        {
            var context = Scene.SamplingContext!;
            var spf = context.SpfRational;
            for (var i = 0; i < Scene.Parent.Framerate * _streamingBufferSize; i++)
            {
                using var tmp = Scene.Sample(f + i);
                using var converted = tmp.Convert<StereoPCM16>();
                converted.Data.CopyTo(sound.Data.Slice(new Rational(i) * spf, spf));
            }
        }

        private void Timer_Elapsed(object? sender, ElapsedEventArgs e)
        {
            var time = e.SignalTime - _startTime;
            var frame = Frame.FromTimeSpan(time, Scene.Parent.Framerate);

            frame += _startframe;

            if (frame > Scene.TotalFrame) Stop();

            frame = (Frame)(frame * Speed);
            CurrentFrame = frame;
            Scene.PreviewFrame = frame;
        }
    }
}