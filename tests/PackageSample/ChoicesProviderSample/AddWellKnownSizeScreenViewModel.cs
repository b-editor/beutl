using System.ComponentModel.DataAnnotations;
using System.Reactive.Linq;

using Reactive.Bindings;

namespace PackageSample;

public sealed class AddWellKnownSizeScreenViewModel
{
    public AddWellKnownSizeScreenViewModel()
    {
        Name.SetValidateAttribute(() => Name);
        Width.SetValidateAttribute(() => Width);
        Height.SetValidateAttribute(() => Height);

        Add = Name.ObserveHasErrors
            .CombineLatest(Width.ObserveHasErrors, Height.ObserveHasErrors)
            .Select(t => !(t.First || t.Second || t.Third))
            .ToReactiveCommand()
            .WithSubscribe(AddCore);
    }

    [Required]
    [StringLength(100, MinimumLength = 1)]
    public ReactiveProperty<string> Name { get; } = new();

    [Range(0, int.MaxValue)]
    public ReactiveProperty<int> Width { get; } = new();

    [Range(0, int.MaxValue)]
    public ReactiveProperty<int> Height { get; } = new();

    public ReactiveCommand Add { get; }

    private void AddCore()
    {
        WellKnownSizesProvider.AddChoice(Name.Value, new(Width.Value, Height.Value));
        Name.Value = string.Empty;
        Width.Value = 0;
        Height.Value = 0;
    }
}
