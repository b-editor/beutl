using Reactive.Bindings;

namespace BeUtl.ViewModels.ExtensionsPages.DevelopPages.Dialogs;

public sealed class AddReleaseDialogViewModel
{
    public AddReleaseDialogViewModel()
    {
        Version.SetValidateNotifyError(str => System.Version.TryParse(str, out _) ? null : S.Message.InvalidString);

        Title.SetValidateNotifyError(NotNullOrWhitespace);
        Body.SetValidateNotifyError(NotNullOrWhitespace);

        IsValid = Version.ObserveHasErrors
            .CombineLatest(Title.ObserveHasErrors, Body.ObserveHasErrors)
            .Select(t => !(t.First || t.Second || t.Third))
            .ToReadOnlyReactivePropertySlim();
    }

    public ReactiveProperty<string> Title { get; } = new();

    public ReactiveProperty<string> Body { get; } = new();

    public ReactiveProperty<string> Version { get; } = new();

    public ReadOnlyReactivePropertySlim<bool> IsValid { get; }

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
