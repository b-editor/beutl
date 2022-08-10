using Avalonia.Media;

using BeUtl.Controls;
using BeUtl.Framework;
using BeUtl.ViewModels;

namespace BeUtl.Services.PrimitiveImpls;

[PrimitiveImpl]
public sealed class EditPageExtension : PageExtension
{
    public static readonly EditPageExtension Instance = new();

    public override Geometry FilledIcon { get; } = FluentIconsFilled.Edit.GetGeometry();

    public override Geometry RegularIcon { get; } = FluentIconsRegular.Edit.GetGeometry();

    public override IObservable<string> Header => S.MainView.IndexEditObservable;

    public override Type Control => typeof(Pages.EditPage);

    public override Type Context => typeof(EditPageViewModel);

    public override string Name => "EditPage";

    public override string DisplayName => "EditPage";
}
