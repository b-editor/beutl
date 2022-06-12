using System.Globalization;
using System.Reactive.Disposables;
using System.Reactive.Linq;

using Google.Cloud.Firestore;

using Reactive.Bindings;

namespace BeUtl.ViewModels.ExtensionsPages.DevelopPages;

public sealed class ResourcePageViewModel
{
    public ResourcePageViewModel(DocumentReference reference, MoreResourcesPageViewModel parent)
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

        DisplayName.SetValidateNotifyError(NotNullOrWhitespace);
        Description.SetValidateNotifyError(NotNullOrWhitespace);
        ShortDescription.SetValidateNotifyError(NotNullOrWhitespace);

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
            await Reference.UpdateAsync(new Dictionary<string, object>
            {
                ["culture"] = (ActualCulture.Value = Culture.Value!).Name,
                ["displayName"] = ActualDisplayName.Value = DisplayName.Value,
                ["description"] = ActualDescription.Value = Description.Value,
                ["shortDescription"] = ActualShortDescription.Value = ShortDescription.Value
            });
        });

        DiscardChanges.Subscribe(async () =>
        {
            DocumentSnapshot snapshot = await Reference.GetSnapshotAsync();
            ActualDisplayName.Value = DisplayName.Value = snapshot.GetValue<string>("displayName");
            ActualDescription.Value = Description.Value = snapshot.GetValue<string>("description");
            ActualShortDescription.Value = ShortDescription.Value = snapshot.GetValue<string>("shortDescription");
            CultureInput.Value = snapshot.GetValue<string>("culture");
            ActualCulture.Value = Culture.Value!;
        });

        Delete.Subscribe(async () => await Reference.DeleteAsync());
    }

    public DocumentReference Reference { get; }

    public MoreResourcesPageViewModel Parent { get; }

    public ReactivePropertySlim<string> ActualDisplayName { get; } = new();

    public ReactivePropertySlim<string> ActualDescription { get; } = new();

    public ReactivePropertySlim<string> ActualShortDescription { get; } = new();

    public ReactivePropertySlim<CultureInfo> ActualCulture { get; } = new();

    public ReactiveProperty<string> DisplayName { get; } = new();

    public ReactiveProperty<string> Description { get; } = new();

    public ReactiveProperty<string> ShortDescription { get; } = new();

    public ReactiveProperty<string> CultureInput { get; } = new();

    public ReadOnlyReactivePropertySlim<CultureInfo?> Culture { get; }

    public ReadOnlyReactivePropertySlim<bool> IsChanging { get; }

    public AsyncReactiveCommand Save { get; }

    public AsyncReactiveCommand DiscardChanges { get; } = new();

    public ReactiveCommand Delete { get; } = new();

    public void Update(DocumentSnapshot snapshot)
    {
        ActualDisplayName.Value = DisplayName.Value = snapshot.GetValue<string>("displayName");
        ActualDescription.Value = Description.Value = snapshot.GetValue<string>("description");
        ActualShortDescription.Value = ShortDescription.Value = snapshot.GetValue<string>("shortDescription");
        ActualCulture.Value = new CultureInfo(CultureInput.Value = snapshot.GetValue<string>("culture"));
    }

    private static string NotNullOrWhitespace(string str)
    {
        if (!string.IsNullOrWhiteSpace(str))
        {
            return null!;
        }
        else
        {
            return "Please enter a string.";
        }
    }
}
