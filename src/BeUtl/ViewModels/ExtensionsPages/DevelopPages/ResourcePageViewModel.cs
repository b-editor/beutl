using System.Globalization;
using System.Reactive.Linq;

using Google.Cloud.Firestore;

using Reactive.Bindings;

namespace BeUtl.ViewModels.ExtensionsPages.DevelopPages;

public sealed class ResourcePageViewModel
{
    public ResourcePageViewModel(DocumentReference reference, PackageSettingsPageViewModel parent)
    {
        Reference = reference;
        Parent = parent;

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

        HasDisplayName = ActualDisplayName.Select(s => s != null).ToReadOnlyReactivePropertySlim();
        HasDescription = ActualDescription.Select(s => s != null).ToReadOnlyReactivePropertySlim();
        HasShortDescription = ActualShortDescription.Select(s => s != null).ToReadOnlyReactivePropertySlim();

        DisplayName.SetValidateNotifyError(NotWhitespace);
        Description.SetValidateNotifyError(NotWhitespace);
        ShortDescription.SetValidateNotifyError(NotWhitespace);

        InheritDisplayName.Where(b => b).Subscribe(_ => DisplayName.Value = null);
        InheritDescription.Where(b => b).Subscribe(_ => Description.Value = null);
        InheritShortDescription.Where(b => b).Subscribe(_ => ShortDescription.Value = null);

        IsChanging = Culture.CombineLatest(ActualCulture).Select(t => t.First?.Name == t.Second?.Name)
            .CombineLatest(
                DisplayName.CombineLatest(ActualDisplayName).Select(t => t.First == t.Second),
                Description.CombineLatest(ActualDescription).Select(t => t.First == t.Second),
                ShortDescription.CombineLatest(ActualShortDescription).Select(t => t.First == t.Second))
            .Select(t => !(t.First && t.Second && t.Third && t.Fourth))
            .ToReadOnlyReactivePropertySlim();

        Save = new AsyncReactiveCommand(CultureInput.ObserveHasErrors
            .CombineLatest(DisplayName.ObserveHasErrors, Description.ObserveHasErrors, ShortDescription.ObserveHasErrors)
            .Select(t => !(t.First || t.Second || t.Third || t.Fourth)));

        Save.Subscribe(async () =>
        {
            var dict = new Dictionary<string, object>
            {
                ["culture"] = (ActualCulture.Value = Culture.Value!).Name,
            };

            if (!InheritDisplayName.Value && DisplayName.Value != null)
            {
                dict["displayName"] = ActualDisplayName.Value = DisplayName.Value;
            }
            if (!InheritDescription.Value && Description.Value != null)
            {
                dict["description"] = ActualDescription.Value = Description.Value;
            }
            if (!InheritShortDescription.Value && ShortDescription.Value != null)
            {
                dict["shortDescription"] = ActualShortDescription.Value = ShortDescription.Value;
            }

            await Reference.SetAsync(dict, SetOptions.Overwrite);
        });

        DiscardChanges.Subscribe(async () => Update(await Reference.GetSnapshotAsync()));

        Delete.Subscribe(async () => await Reference.DeleteAsync());
    }

    public DocumentReference Reference { get; }

    public PackageSettingsPageViewModel Parent { get; }

    public ReactivePropertySlim<string?> ActualDisplayName { get; } = new();

    public ReactivePropertySlim<string?> ActualDescription { get; } = new();

    public ReactivePropertySlim<string?> ActualShortDescription { get; } = new();

    public ReadOnlyReactivePropertySlim<bool> HasDisplayName { get; }

    public ReadOnlyReactivePropertySlim<bool> HasDescription { get; }

    public ReadOnlyReactivePropertySlim<bool> HasShortDescription { get; }

    public ReactivePropertySlim<CultureInfo> ActualCulture { get; } = new();

    public ReactiveProperty<string?> DisplayName { get; } = new();

    public ReactiveProperty<string?> Description { get; } = new();

    public ReactiveProperty<string?> ShortDescription { get; } = new();

    public ReactiveProperty<bool> InheritDisplayName { get; } = new();

    public ReactiveProperty<bool> InheritDescription { get; } = new();

    public ReactiveProperty<bool> InheritShortDescription { get; } = new();

    public ReactiveProperty<string> CultureInput { get; } = new();

    public ReadOnlyReactivePropertySlim<CultureInfo?> Culture { get; }

    public ReadOnlyReactivePropertySlim<bool> IsChanging { get; }

    public AsyncReactiveCommand Save { get; }

    public AsyncReactiveCommand DiscardChanges { get; } = new();

    public ReactiveCommand Delete { get; } = new();

    public void Update(DocumentSnapshot snapshot)
    {
        void Set(string name, IReactiveProperty<string?> property1, IReactiveProperty<string?> property2, IReactiveProperty<bool> property3)
        {
            if (snapshot.TryGetValue<string>(name, out string? value))
            {
                property1.Value = value;
                property2.Value = value;
                property3.Value = false;
            }
            else
            {
                property1.Value = null;
                property2.Value = null;
                property3.Value = true;
            }
        }

        Set("displayName", ActualDisplayName, DisplayName, InheritDisplayName);
        Set("description", ActualDescription, Description, InheritDescription);
        Set("shortDescription", ActualShortDescription, ShortDescription, InheritShortDescription);

        ActualCulture.Value = new CultureInfo(CultureInput.Value = snapshot.GetValue<string>("culture"));
    }

    private static string NotWhitespace(string? str)
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
