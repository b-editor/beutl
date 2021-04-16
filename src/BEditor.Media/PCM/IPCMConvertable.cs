namespace BEditor.Media.PCM
{
    public interface IPCMConvertable<T> where T : unmanaged, IPCM<T>
    {
        public void ConvertTo(out T dst);
        public void ConvertFrom(T src);
    }
}