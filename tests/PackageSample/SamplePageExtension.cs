using System.Reactive.Linq;

using Avalonia.Media;

using Beutl;
using Beutl.Controls;
using Beutl.Framework;

namespace PackageSample;

public sealed class SamplePageViewModel
{

}

[Export]
public sealed class SamplePageExtension : PageExtension
{
    public override Geometry FilledIcon => FluentIconsFilled.Mail.GetGeometry();

    public override Geometry RegularIcon => FluentIconsRegular.Mail.GetGeometry();

    public override string Header => "Mail";

    // 本来はControlを返す。
    // nullを返すとErrorUIが表示される
    public override Type Control => null!;

    public override Type Context => typeof(SamplePageViewModel);

    public override string Name => "Sample page";

    public override string DisplayName => "Sample page";
}
