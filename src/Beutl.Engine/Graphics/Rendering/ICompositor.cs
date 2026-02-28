namespace Beutl.Graphics.Rendering;

public interface ICompositor : IDisposable
{
    CompositionFrame Evaluate(TimeSpan time);
}
