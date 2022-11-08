
using Beutl.Animation;

namespace Beutl.Styling;

public interface ISetterInstance : IDisposable
{
    CoreProperty Property { get; }

    ISetter Setter { get; }

    IStyleable Target { get; }

    void Apply(IClock clock);

    void Begin();

    void End();
}
