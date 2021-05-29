// ScenePlayer.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Timers;

using BEditor.Media;

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