using System.Globalization;
using System.Reactive.Linq;

using BeUtl.Models.Extensions.Develop;

using Reactive.Bindings;

namespace BeUtl.ViewModels.ExtensionsPages.DevelopPages.Dialogs;

public sealed class AddResourceDialogViewModel
{
    public AddResourceDialogViewModel(IPackage.ILink package)
    {
        Package = package;

        CultureInput.SetValidateNotifyError(str =>
        {
            if (!string.IsNullOrWhiteSpace(str))
            {
                try
                {
                    CultureInfo.GetCultureInfo(str);
                    return null!;
                }
                catch { }
            }

            return "CultureNotFoundException";
        });

        Culture = CultureInput.Select(str =>
        {
            if (!string.IsNullOrWhiteSpace(str))
            {
                try
                {
                    return CultureInfo.GetCultureInfo(str);
                }
                catch { }
            }

            return null;
        })
            .ToReadOnlyReactivePropertySlim();

        DisplayName.SetValidateNotifyError(NotNullOrWhitespace);
        Description.SetValidateNotifyError(NotNullOrWhitespace);
        ShortDescription.SetValidateNotifyError(NotNullOrWhitespace);
        InheritDisplayName.Where(b => b).Subscribe(_ => DisplayName.Value = null);
        InheritDescription.Where(b => b).Subscribe(_ => Description.Value = null);
        InheritShortDescription.Where(b => b).Subscribe(_ => ShortDescription.Value = null);

        IsValid = CultureInput.ObserveHasErrors
            .CombineLatest(
                DisplayName.ObserveHasErrors,
                Description.ObserveHasErrors,
                ShortDescription.ObserveHasErrors)
            .Select(t => !(t.First || t.Second || t.Third || t.Fourth))
            .ToReadOnlyReactivePropertySlim();

        Add = new AsyncReactiveCommand(IsValid);
        Add.Subscribe(async () =>
        {
            if (Culture.Value != null)
            {
                await Package.AddResource(new LocalizedPackageResource(
                    DisplayName: InheritDisplayName.Value ? null : DisplayName.Value,
                    Description: InheritDescription.Value ? null : Description.Value,
                    ShortDescription: InheritShortDescription.Value ? null : ShortDescription.Value,
                    LogoImage: null,
                    Screenshots: Array.Empty<ImageLink>(),
                    Culture: Culture.Value));
            }
        });
    }

    public IPackage.ILink Package { get; }

    public ReactiveProperty<string> CultureInput { get; } = new();

    public ReadOnlyReactivePropertySlim<CultureInfo?> Culture { get; }

    public ReactiveProperty<string?> DisplayName { get; } = new("");

    public ReactiveProperty<string?> Description { get; } = new("");

    public ReactiveProperty<string?> ShortDescription { get; } = new("");

    public ReadOnlyReactivePropertySlim<bool> IsValid { get; }

    public ReactiveProperty<bool> InheritDisplayName { get; } = new();

    public ReactiveProperty<bool> InheritDescription { get; } = new();

    public ReactiveProperty<bool> InheritShortDescription { get; } = new();

    public AsyncReactiveCommand Add { get; }

    private static string NotNullOrWhitespace(string? str)
    {
        if (str == null || !string.IsNullOrWhiteSpace(str))
        {
            return null!;
        }
        else
        {
            return "Please enter a string.";
        }
    }
}
