using BeUtl.Animation;
using BeUtl.Collections;

namespace BeUtl.Styling;

public interface ISetter
{
    CoreProperty Property { get; }

    object? Value { get; }

    ICoreReadOnlyList<IAnimation> Animations { get; }

    ISetterBatch CreateBatch(IStyleable target);

    ISetterInstance Instance(IStyleable target);
}
