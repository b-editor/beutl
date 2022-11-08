using Avalonia.Media;

using Beutl.Controls;
using Beutl.Framework;
using Beutl.Pages;
using Beutl.ViewModels;

namespace Beutl.Services.PrimitiveImpls;

[PrimitiveImpl]
public sealed class ExtensionsPageExtension : PageExtension
{
    public static readonly ExtensionsPageExtension Instance = new();

    public override Geometry FilledIcon => FluentIconsFilled.Puzzle_piece.GetGeometry();

    public override Geometry RegularIcon => FluentIconsRegular.Puzzle_piece.GetGeometry();

    public override string Header => Strings.Extensions;

    public override Type Control => typeof(Pages.ExtensionsPage);

    public override Type Context => typeof(ExtensionsPageViewModel);

    public override string Name => "ExtensionsPage";

    public override string DisplayName => "ExtensionsPage";
}
