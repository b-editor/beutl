
using OpenTK.Audio.OpenAL;

namespace BEditor.Audio
{
    public partial class AudioSource : AudioLibraryObject
    {
        public AudioSource()
        {
            Handle = AL.GenSource();
            CheckError();
        }

        public override int Handle { get; }
        
        protected override void OnDispose()
        {
            AL.DeleteSource(Handle);
        }
        public void Play()
        {
            ThrowIfDisposed();
            AL.SourcePlay(Handle);

            CheckError();
        }
        public void Stop()
        {
            ThrowIfDisposed();
            AL.SourceStop(Handle);

            CheckError();
        }
        public void Pause()
        {
            ThrowIfDisposed();
            AL.SourcePause(Handle);

            CheckError();
        }
    }
}
