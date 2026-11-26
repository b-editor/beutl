using Beutl.Animation.Easings;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.PropertyEditors;
using Beutl.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.Editor.Components.PropertyEditors.ViewModels;

public sealed class EasingEditorViewModel<T>(IPropertyAdapter<T?> property) : PropertyEditorViewModel<T?, Easing?>(property)
    where T : Easing
{
    protected override bool CanEditUnderExpression => true;

    protected override Easing? ToEdit(T? value) => value;

    protected override T? FromEdit(Easing? edit) => edit as T;

    public override Easing? PreviewEdit(Easing? value)
    {
        // A validator may keep the current value, which raises no change to refresh the graph.
        return !IsDisposed && value is T newValue ? SetCurrentValueAndGetCoerced(newValue) : value;
    }

    public override Easing? PreviewEdit(Easing? value, bool relative) => PreviewEdit(value);

    public override void ConfirmEdit(Easing? oldValue, Easing? newValue)
    {
        if (IsDisposed || newValue is not T value) return;

        if (!HasExpression.Value && ReferenceEquals(oldValue, value))
        {
            // A drag that ended where it began: write the original back without committing. The
            // drag's merged operation then changes nothing. Rolling the pending transaction back
            // instead would also discard other features' uncommitted edits, such as a timeline nudge.
            SetCurrentValueAndGetCoerced(value);
        }
        else
        {
            SetValue(oldValue as T, value);
        }
    }

    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        if (visitor is EasingEditor editor && !Disposables.IsDisposed)
        {
            editor.Items = EasingItems.CreateEditorItems(typeof(T), this.GetRequiredService<LibraryService>());
        }
    }
}
