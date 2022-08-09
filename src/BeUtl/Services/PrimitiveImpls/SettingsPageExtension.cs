using Avalonia.Media;

using BeUtl.Controls;
using BeUtl.Framework;
using BeUtl.Pages;
using BeUtl.ViewModels;

namespace BeUtl.Services.PrimitiveImpls;

[PrimitiveImpl]
public sealed class SettingsPageExtension : PageExtension
{
    public static readonly SettingsPageExtension Instance = new();

    public override Geometry FilledIcon { get; } = FluentIconsFilled.Settings.GetGeometry();

    public override Geometry RegularIcon { get; } = FluentIconsRegular.Settings.GetGeometry();

    public override IObservable<string> Header => S.MainView.SettingsObservable;

    public override Type Control => typeof(SettingsPage);

    public override Type Context => typeof(SettingsPageViewModel);

    public override string Name => "SettingsPage";

    public override string DisplayName => "SettingsPage";
}
