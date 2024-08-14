using Reactive.Bindings;

namespace Beutl.ViewModels.Editors;

public sealed class TimeSpanEditorViewModel : ValueEditorViewModel<TimeSpan>
{
    public TimeSpanEditorViewModel(IPropertyAdapter<TimeSpan> property)
        : base(property)
    {
        Text = Value
            .Select(x => x.ToString() ?? "")
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables)!;
    }

    public ReadOnlyReactivePropertySlim<string> Text { get; }
}
