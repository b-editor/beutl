
using BeUtl.Animation;

namespace BeUtl.Styling;

public interface ISetterInstance : IDisposable
{
    CoreProperty Property { get; }

    ISetter Setter { get; }

    IStyleable Target { get; }

    void Apply(IClock clock);
}
