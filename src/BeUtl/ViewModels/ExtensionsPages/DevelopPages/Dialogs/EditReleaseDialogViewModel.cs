using BeUtl.Models.Extensions.Develop;

using Reactive.Bindings;

namespace BeUtl.ViewModels.ExtensionsPages.DevelopPages.Dialogs;

public sealed class EditReleaseResourceDialogViewModel
{
    public EditReleaseResourceDialogViewModel(ILocalizedReleaseResource.ILink resource)
    {
        Resource = resource;
        Title.Value = resource.Title;
        Body.Value = resource.Body;
        CultureInput.Value = resource.Culture.Name;
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

            return S.Message.InvalidString;
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

        Title.SetValidateNotifyError(NotNullOrWhitespace);
        Body.SetValidateNotifyError(NotNullOrWhitespace);

        IsValid = CultureInput.ObserveHasErrors
            .CombineLatest(Title.ObserveHasErrors, Body.ObserveHasErrors)
            .Select(t => !(t.First || t.Second || t.Third))
            .ToReadOnlyReactivePropertySlim();

        Apply.Subscribe(async () =>
        {
            await resource.SyncronizeToAsync(
                new LocalizedReleaseResource(Title.Value, Body.Value, Culture.Value!),
                 ReleaseResourceFields.None);
        });
    }

    public ILocalizedReleaseResource.ILink Resource { get; }

    public ReactiveProperty<string> Title { get; } = new();

    public ReactiveProperty<string> Body { get; } = new();

    public ReactiveProperty<string> CultureInput { get; } = new();

    public ReadOnlyReactivePropertySlim<CultureInfo?> Culture { get; }

    public ReadOnlyReactivePropertySlim<bool> IsValid { get; }

    public AsyncReactiveCommand Apply { get; } = new();

    private static string NotNullOrWhitespace(string str)
    {
        if (!string.IsNullOrWhiteSpace(str))
        {
            return null!;
        }
        else
        {
            return S.Message.PleaseEnterString;
        }
    }
}
