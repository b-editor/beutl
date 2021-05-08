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
    }
}