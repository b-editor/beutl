using System.Globalization;
using System.Reactive.Linq;

using Google.Cloud.Firestore;

using Reactive.Bindings;

namespace BeUtl.ViewModels.ExtensionsPages.DevelopPages.Dialogs;

public sealed class AddResourceDialogViewModel
{
    public AddResourceDialogViewModel(CollectionReference reference)
    {
        Reference = reference;

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
                var dict = new Dictionary<string, object>
                {
                    ["culture"] = Culture.Value.Name
                };

                if (!InheritDisplayName.Value && DisplayName.Value != null)
                {
                    dict["displayName"] = DisplayName.Value;
                }

                if (!InheritDescription.Value && Description.Value != null)
                {
                    dict["description"] = Description.Value;
                }

                if (!InheritShortDescription.Value && ShortDescription.Value != null)
                {
                    dict["shortDescription"] = ShortDescription.Value;
                }

                await Reference.AddAsync(dict);
            }
        });
    }

    public CollectionReference Reference { get; }

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
