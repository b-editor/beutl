using BeUtl.Models.Extensions.Develop;

using Reactive.Bindings;

namespace BeUtl.ViewModels.ExtensionsPages.DevelopPages.Dialogs;

public sealed class AddReleaseResourceDialogViewModel
{
    public AddReleaseResourceDialogViewModel(IPackageRelease.ILink release)
    {
        Release = release;
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

            return StringResources.Message.InvalidString;
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

        Add.Subscribe(async () => await Release.AddResource(new LocalizedReleaseResource(Title.Value, Body.Value, Culture.Value!)));
    }

    public IPackageRelease.ILink Release { get; }

    public ReactiveProperty<string> Title { get; } = new();

    public ReactiveProperty<string> Body { get; } = new();

    public ReactiveProperty<string> CultureInput { get; } = new();

    public ReadOnlyReactivePropertySlim<CultureInfo?> Culture { get; }

    public ReadOnlyReactivePropertySlim<bool> IsValid { get; }

    public AsyncReactiveCommand Add { get; } = new();

    private static string NotNullOrWhitespace(string str)
    {
        if (!string.IsNullOrWhiteSpace(str))
        {
            return null!;
        }
        else
        {
            return StringResources.Message.PleaseEnterString;
        }
    }
}
