namespace BeUtl.Graphics.Effects;

public interface IProcessor<T>
{
    void Process(in T src, out T dst);
}
