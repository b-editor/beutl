namespace Beutl.Editor.Components.PropertyEditors.ViewModels;

// An editor that points at another object, which the user picks from the targets.
public interface ITargetPickerViewModel
{
    bool IsDisposed { get; }

    IReadOnlyList<TargetObjectInfo> GetAvailableTargets();

    void SetTarget(CoreObject? target);
}
