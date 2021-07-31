using System.Collections;
using System.Collections.Generic;
using System.Linq;

using OpenTK.Audio.OpenAL;

namespace BEditor.Audio
{
    public partial class AudioSource : AudioLibraryObject
    {
        public AudioSource()
        {
            Handle = Tool.GenSource();
        }

        public override int Handle { get; }

        protected override void OnDispose()
        {
            Tool.DeleteSource(Handle);
        }
        public void Play()
        {
            ThrowIfDisposed();
            Tool.SourcePlay(Handle);
        }
        public void Stop()
        {
            ThrowIfDisposed();
            Tool.SourceStop(Handle);
        }
        public void Pause()
        {
            ThrowIfDisposed();
            Tool.SourcePause(Handle);
        }
        public void QueueBuffer(AudioBuffer buffer)
        {
            AL.SourceQueueBuffer(Handle, buffer.Handle);
        }
        public void QueueBuffer(params AudioBuffer[] buffer)
        {
            AL.SourceQueueBuffers(Handle, buffer.Select(i => i.Handle).ToArray());
        }
        public int UnqueueBuffer()
        {
            return AL.SourceUnqueueBuffer(Handle);
        }
        public void UnqueueBuffer(int count)
        {
            AL.SourceUnqueueBuffers(Handle, count);
        }
    }
}