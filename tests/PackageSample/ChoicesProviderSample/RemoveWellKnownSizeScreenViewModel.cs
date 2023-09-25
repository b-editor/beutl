using System.Reactive.Linq;

using Beutl.Collections;

using Reactive.Bindings;

namespace PackageSample;

public sealed class RemoveWellKnownSizeScreenViewModel
{
    public RemoveWellKnownSizeScreenViewModel()
    {
        Items = WellKnownSizesProvider.GetTypedChoices();

        Remove = SelectedItem.Select(v => v != null)
            .ToReactiveCommand()
            .WithSubscribe(RemoveCore);
    }

    public ICoreReadOnlyList<WellKnownSize> Items { get; }

    public ReactiveProperty<WellKnownSize?> SelectedItem { get; } = new();

    public ReactiveCommand Remove { get; }

    private void RemoveCore()
    {
        WellKnownSizesProvider.RemoveChoice(SelectedItem.Value!);
        SelectedItem.Value = null;
    }
}
