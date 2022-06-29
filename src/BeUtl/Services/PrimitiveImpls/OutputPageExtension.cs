using Avalonia.Media;

using BeUtl.Controls;
using BeUtl.Framework;
using BeUtl.ViewModels;

namespace BeUtl.Services.PrimitiveImpls;

[PrimitiveImpl]
public sealed class OutputPageExtension : PageExtension
{
    public static readonly OutputPageExtension Instance = new();

    public override Geometry FilledIcon { get; } = FluentIconsFilled.Arrow_Export_LTR.GetGeometry();

    public override Geometry RegularIcon { get; } = FluentIconsRegular.Arrow_Export_LTR.GetGeometry();

    public override ResourceReference<string> Header => "S.MainView.Output";

    public override Type Control => null!;

    public override Type Context => typeof(OutputPageViewModel);

    public override string Name => "OutputPage";

    public override string DisplayName => "OutputPage";
}
