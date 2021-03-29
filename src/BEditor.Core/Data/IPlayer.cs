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
        Stop,
    }

    /// <summary>
    /// Represents a media player.
    /// </summary>
    public interface IPlayer : IDisposable
    {
        /// <summary>
        /// Occurs when playback is started.
        /// </summary>
        public event EventHandler<PlayingEventArgs>? Playing;

        /// <summary>
        /// Occurs after playback has been stopped.
        /// </summary>
        public event EventHandler? Stopped;

        /// <summary>
        /// Get the status of this <see cref="IPlayer"/>.
        /// </summary>
        public PlayerState State { get; }

        /// <summary>
        /// Get the current frame in this <see cref="IPlayer"/>.
        /// </summary>
        public Frame CurrentFrame { get; }

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
}
