using Avalonia.Media;

using Beutl.Controls;
using Beutl.Framework;
using Beutl.ViewModels;

namespace Beutl.Services.PrimitiveImpls;

[PrimitiveImpl]
public sealed class EditPageExtension : PageExtension
{
    public static readonly EditPageExtension Instance = new();

    public override Geometry FilledIcon => FluentIconsFilled.Edit.GetGeometry();

    public override Geometry RegularIcon => FluentIconsRegular.Edit.GetGeometry();

    public override string Header => Strings.Edit;

    public override Type Control => typeof(Pages.EditPage);

    public override Type Context => typeof(EditPageViewModel);

    public override string Name => "EditPage";

    public override string DisplayName => "EditPage";
}
