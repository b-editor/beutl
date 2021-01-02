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

namespace BEditor.Core.Data
{
    public interface IPlayer : IDisposable
    {
        public PlayerState State { get; }
        public Frame CurrentFrame { get; }

        public event EventHandler<PlayingEventArgs> Playing;
        public event EventHandler Stopped;

        public void Play();
        public void Stop();
    }
    public class PlayingEventArgs : EventArgs
    {
        public PlayingEventArgs(Frame frame)
        {
            StartFrame = frame;
        }

        public Frame StartFrame { get; }
    }

    public class ScenePlayer : IPlayer
    {
        private readonly Timer timer;
        private DateTime startTime;
        private readonly double framerate;
        private Frame startframe;

        public ScenePlayer(Scene scene)
        {
            Scene = scene;
            framerate = scene.Parent.Framerate;

            timer = new Timer
            {
                Interval = (double)1 / framerate
            };

            timer.Elapsed += Timer_Elapsed;
        }

        public Scene Scene { get; }
        public PlayerState State { get; private set; } = PlayerState.Stop;
        public Frame CurrentFrame { get; private set; }

        public event EventHandler<PlayingEventArgs> Playing;
        public event EventHandler Stopped;

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
        public void Stop()
        {
            State = PlayerState.Stop;

            timer.Stop();

            Stopped?.Invoke(this, EventArgs.Empty);
        }
        public void Dispose()
        {
            timer.Dispose();
        }
    }

    public enum PlayerState
    {
        Playing,
        Stop
    }
}
