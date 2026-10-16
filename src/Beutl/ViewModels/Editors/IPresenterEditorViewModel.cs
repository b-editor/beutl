using Beutl.Engine;
using Reactive.Bindings;

namespace Beutl.ViewModels.Editors;

// An editor whose value can be a presenter of another object, which the user picks from the targets.
public interface IPresenterEditorViewModel
{
    bool IsDisposed { get; }

    ReadOnlyReactivePropertySlim<bool> IsPresenter { get; }

    IReadOnlyList<TargetObjectInfo> GetAvailableTargets();

    void SetTarget(CoreObject? target);
}
