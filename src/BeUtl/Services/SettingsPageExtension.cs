
using Avalonia.Media;

using BeUtl.Controls;
using BeUtl.Framework;
using BeUtl.Pages;
using BeUtl.ViewModels;

namespace BeUtl.Services;

[PrimitiveImpl]
public sealed class SettingsPageExtension : PageExtension
{
    public static readonly SettingsPageExtension Instance = new();

    public override Geometry FilledIcon { get; } = FluentIconsFilled.Settings.GetGeometry();

    public override Geometry RegularIcon { get; } = FluentIconsRegular.Settings.GetGeometry();

    public override ResourceReference<string> Header => "S.MainView.Settings";

    public override Type Control => typeof(SettingsPage);

    public override Type Context => typeof(SettingsPageViewModel);

    public override string Name => "SettingsPage";

    public override string DisplayName => "SettingsPage";
}
