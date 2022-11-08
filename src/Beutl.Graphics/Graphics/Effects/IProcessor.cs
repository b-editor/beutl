namespace Beutl.Graphics.Effects;

public interface IProcessor<T>
{
    void Process(in T src, out T dst);
}
