
using Beutl.Animation;

namespace Beutl.Styling;

public interface IStyleInstance : IDisposable
{
    IStyleInstance? BaseStyle { get; }

    ICoreObject Target { get; }

    IStyle Source { get; }

    ReadOnlySpan<ISetterInstance> Setters { get; }

    void Apply(IClock clock);

    void Begin();

    void End();
}
