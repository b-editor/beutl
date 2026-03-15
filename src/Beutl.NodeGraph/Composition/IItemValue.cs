using Beutl.Animation;
using Beutl.Extensibility;

namespace Beutl.NodeGraph.Composition;

public enum PropagateResult
{
    Success,
    Converted,
    Failed
}

public interface IItemValue : IDisposable
{
    PropagateResult PropagateFrom(IItemValue source);

    bool TryCopyFrom(IPropertyAdapter source);

    void SetFromObject(object? value);

    object? GetBoxed();

    bool TryLoadFromAnimation(IAnimation animation, TimeSpan time);
}
