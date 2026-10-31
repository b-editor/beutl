using Beutl.Graphics;

namespace Beutl.Editor.Components.PropertyEditors.ViewModels;

// A relative point's or rectangle's editor: while its components are separated, the unit is picked beside them.
internal interface IRelativeUnitEditor
{
    // The unit of the value shown.
    IObservable<RelativeUnit> Unit { get; }

    // Writes the value being edited with unit in place of its own.
    void SetUnit(RelativeUnit unit);
}
