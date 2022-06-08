
using Avalonia.Media;

using BeUtl.Controls;
using BeUtl.Framework;
using BeUtl.Pages;
using BeUtl.ViewModels;

namespace BeUtl.Services;

[PrimitiveImpl]
public sealed class ExtensionsPageExtension : PageExtension
{
    public static readonly ExtensionsPageExtension Instance = new();

    public override Geometry FilledIcon { get; } = FluentIconsFilled.Puzzle_piece.GetGeometry();

    public override Geometry RegularIcon { get; } = FluentIconsRegular.Puzzle_piece.GetGeometry();

    public override ResourceReference<string> Header => "S.MainView.Extensions";

    public override Type Control => typeof(ExtensionsPage);

    public override Type Context => typeof(ExtensionsPageViewModel);

    public override string Name => "ExtensionsPage";

    public override string DisplayName => "ExtensionsPage";
}
