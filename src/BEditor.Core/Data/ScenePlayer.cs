using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;

using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Media;

using Timer = System.Timers.Timer;

namespace BEditor.Data
{
    /// <summary>
    /// Represents a media player.
    /// </summary>
    public interface IPlayer : IDisposable
    {
        /// <summary>
        /// Get the status of this <see cref="IPlayer"/>.
        /// </summary>
        public PlayerState State { get; }
        /// <summary>
        /// Get the current frame in this <see cref="IPlayer"/>.
        /// </summary>
        public Frame CurrentFrame { get; }

        /// <summary>
        /// Occurs when playback is started.
        /// </summary>
        public event EventHandler<PlayingEventArgs>? Playing;
        /// <summary>
        /// Occurs after playback has been stopped.
        /// </summary>
        public event EventHandler? Stopped;

        /// <summary>
        /// Start playing.
        /// </summary>
        public void Play();
        /// <summary>
        /// Stop playing.
        /// </summary>
        public void Stop();
    }
    /// <summary>
    /// Represents the event argument at the start of playback.
    /// </summary>
    public class PlayingEventArgs : EventArgs
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PlayingEventArgs"/> class.
        /// </summary>
        /// <param name="frame">The frame at the start of playback.</param>
        public PlayingEventArgs(Frame frame)
        {
            StartFrame = frame;
        }

        /// <summary>
        /// Get the frame at the start of playback.
        /// </summary>
        public Frame StartFrame { get; }
    }

    /// <summary>
    /// Represents a <see cref="Scene"/> player.
    /// </summary>
    public class ScenePlayer : IPlayer
    {
        private readonly Timer timer;
        private DateTime startTime;
        private readonly double framerate;
        private Frame startframe;

        /// <summary>
        /// Initializes a new instance of the <see cref="ScenePlayer"/> class.
        /// </summary>
        /// <param name="scene">This is the scene to play.</param>
        public ScenePlayer(Scene scene)
        {
            Scene = scene;
            framerate = scene.Parent!.Framerate;

            timer = new Timer
            {
                Interval = (double)1 / framerate
            };

            timer.Elapsed += Timer_Elapsed;
        }

        /// <summary>
        /// Get the scene to play.
        /// </summary>
        public Scene Scene { get; }
        /// <inheritdoc/>
        public PlayerState State { get; private set; } = PlayerState.Stop;
        /// <inheritdoc/>
        public Frame CurrentFrame { get; private set; }

        /// <inheritdoc/>
        public event EventHandler<PlayingEventArgs>? Playing;
        /// <inheritdoc/>
        public event EventHandler? Stopped;

        /// <inheritdoc/>
        public void Play()
        {
            if (State is PlayerState.Playing) return;

            State = PlayerState.Playing;
            startTime = DateTime.Now;
            startframe = Scene.PreviewFrame;

            timer.Start();

            Playing?.Invoke(this, new(startframe));
        }
        private void Timer_Elapsed(object sender, ElapsedEventArgs e)
        {
            var time = e.SignalTime - startTime;
            var frame = Frame.FromTimeSpan(time, framerate);

            frame += startframe;

            if (frame > Scene.TotalFrame) Stop();

            CurrentFrame = frame;
            Scene.PreviewFrame = frame;
        }
        /// <inheritdoc/>
        public void Stop()
        {
            State = PlayerState.Stop;

            timer.Stop();

            Stopped?.Invoke(this, EventArgs.Empty);
        }
        /// <inheritdoc/>
        public void Dispose()
        {
            timer.Dispose();
        }
    }

    /// <summary>
    /// Represents the status of <see cref="IPlayer"/>.
    /// </summary>
    public enum PlayerState
    {
        /// <summary>
        /// Represents that the <see cref="IPlayer"/> is playing.
        /// </summary>
        Playing,
        /// <summary>
        /// Represents that the <see cref="IPlayer"/> is stopped.
        /// </summary>
        Stop
    }
}
