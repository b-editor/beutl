using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Language;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.Editor.Components.PropertyEditors.ViewModels;

public sealed class MeshWarpGridEditorViewModel : ValueEditorViewModel<MeshWarpGrid>
{
    public MeshWarpGridEditorViewModel(IPropertyAdapter<MeshWarpGrid> property) : base(property)
    {
        Summary = Value.Select(grid => grid is null ? string.Empty : $"{grid.Rows} × {grid.Columns}")
            .ToReadOnlyReactiveProperty()
            .DisposeWith(Disposables);
    }

    public ReadOnlyReactiveProperty<string?> Summary { get; }

    public void ResetMesh()
    {
        if (IsDisposed || !CanEdit.Value) return;
        if (Mirrors((editor, shift) => ((MeshWarpGridEditorViewModel)editor).ResetMeshAt(CurrentTimeValue + shift),
                commitName: GraphicsStrings.MeshWarp_Reset))
            return;

        ResetMeshAt(CurrentTimeValue);
    }

    private void ResetMeshAt(MediaTime time)
    {
        if (IsDisposed || !CanEdit.Value) return;
        if (PropertyAdapter.GetEngineProperty()?.GetOwnerObject() is MeshWarp effect)
        {
            effect.ResetMesh(time);
            Commit(GraphicsStrings.MeshWarp_Reset);
        }
        else if (Value.Value is { } grid)
        {
            SetValue(MeshWarpGrid.CreateIdentity(grid.Rows, grid.Columns));
        }
    }
}
