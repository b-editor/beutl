using System.Reactive.Linq;
using Beutl.Api.Services;
using Reactive.Bindings;

namespace Beutl.ViewModels.ExtensionsPages.DiscoverPages;

/// <summary>
/// The store's kind selector. The tab strip binds <see cref="SelectedIndex"/>, so the
/// order here is the order of the items declared in XAML.
/// </summary>
public sealed class PackageKindFilterViewModel : IDisposable
{
    private static readonly PackageKindFilter[] s_order =
    [
        PackageKindFilter.All,
        PackageKindFilter.Extension,
        PackageKindFilter.Material,
        PackageKindFilter.Template
    ];

    private readonly IDisposable _subscription;

    public PackageKindFilterViewModel(Action onChanged)
    {
        _subscription = SelectedIndex.Skip(1).Subscribe(_ => onChanged());
    }

    public ReactivePropertySlim<int> SelectedIndex { get; } = new();

    public PackageKindFilter Selected => s_order[Math.Clamp(SelectedIndex.Value, 0, s_order.Length - 1)];

    public void Dispose()
    {
        _subscription.Dispose();
        SelectedIndex.Dispose();
    }
}
