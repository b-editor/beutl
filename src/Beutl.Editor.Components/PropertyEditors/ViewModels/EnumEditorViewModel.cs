using Beutl.Controls.PropertyEditors;
using Beutl.Editor.PropertyEditors;

namespace Beutl.Editor.Components.PropertyEditors.ViewModels;

public sealed class EnumEditorViewModel<T> : PropertyEditorViewModel<T, int>
    where T : struct, Enum
{
    private readonly T[] _enumValues;
    private readonly EnumItem[] _enumItems;

    public EnumEditorViewModel(IPropertyAdapter<T> property) : base(property)
    {
        _enumValues = Enum.GetValues<T>();
        _enumItems = Enum.GetNames<T>()
            .Select(name => typeof(T).GetField(name)!)
            .Zip(_enumValues)
            .Select(t => new EnumItem(
                TypeDisplayHelpers.GetLocalizedName(t.First),
                TypeDisplayHelpers.GetLocalizedDescription(t.First),
                t.Second))
            .ToArray();
    }

    protected override int ToEdit(T value) => _enumValues.IndexOf(value);

    protected override T FromEdit(int edit) => _enumValues[Math.Clamp(edit, 0, _enumValues.Length - 1)];

    public override void ConfirmEdit(int oldValue, int newValue)
    {
        if (IsDisposed) return;
        SetValue(ShownValue.Value, FromEdit(newValue));
    }

    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        if (visitor is EnumEditor editor && !Disposables.IsDisposed)
        {
            editor.Items = _enumItems;
        }
    }
}
