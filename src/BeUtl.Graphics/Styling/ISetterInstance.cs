
using BeUtl.Animation;

namespace BeUtl.Styling;

public interface ISetterInstance : IDisposable
{
    CoreProperty Property { get; }

    ISetter Setter { get; }

    void Apply(ISetterBatch batch, IClock clock);

    void Unapply(ISetterBatch batch);
}
