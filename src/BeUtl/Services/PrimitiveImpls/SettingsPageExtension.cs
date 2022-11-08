using Avalonia.Media;

using Beutl.Controls;
using Beutl.Framework;
using Beutl.Pages;
using Beutl.ViewModels;

namespace Beutl.Services.PrimitiveImpls;

[PrimitiveImpl]
public sealed class SettingsPageExtension : PageExtension
{
    public static readonly SettingsPageExtension Instance = new();

    public override Geometry FilledIcon { get; } = FluentIconsFilled.Settings.GetGeometry();

    public override Geometry RegularIcon { get; } = FluentIconsRegular.Settings.GetGeometry();

    public override string Header => Strings.Settings;

    public override Type Control => typeof(Pages.SettingsPage);

    public override Type Context => typeof(SettingsPageViewModel);

    public override string Name => "SettingsPage";

    public override string DisplayName => "SettingsPage";
}
