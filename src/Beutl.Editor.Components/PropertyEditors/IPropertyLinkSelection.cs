using Beutl.Editor.PropertyEditors;
using Reactive.Bindings;

namespace Beutl.Editor.Components.PropertyEditors;

// Picks the property whose value another property follows, by clicking it in the property panel.
internal interface IPropertyLinkSelection
{
    IReadOnlyReactiveProperty<bool> IsSelectingLinkTarget { get; }

    // The property that receives the expression; null outside selection.
    PropertyEditorViewModel? Source { get; }

    Type? SourceValueType { get; }

    // A link whose SetExpression failed, for the source's menu to show the error as the expression editor does.
    IReadOnlyReactiveProperty<PropertyLinkFailure?> Failure { get; }

    bool CanLinkTo(PropertyEditorViewModel target);

    bool TryChooseLinkTarget(PropertyEditorViewModel target);

    void BeginLinkSelection(PropertyEditorViewModel source);

    void CancelLinkSelection();
}

// A property tab that shows editors from an editor with link selection, for the rows and the prompt.
internal interface IPropertyLinkSelectionHost
{
    IPropertyLinkSelection? LinkSelection { get; }
}

internal sealed record PropertyLinkFailure(PropertyEditorViewModel Source, string Error);
