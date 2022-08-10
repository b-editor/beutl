using System.Reactive.Linq;

using Avalonia.Media;

using BeUtl;
using BeUtl.Controls;
using BeUtl.Framework;

namespace PackageSample;

public sealed class SamplePageViewModel
{

}

public sealed class SamplePageExtension : PageExtension
{
    public override Geometry FilledIcon { get; } = FluentIconsFilled.Mail.GetGeometry();

    public override Geometry RegularIcon { get; } = FluentIconsRegular.Mail.GetGeometry();

    public override IObservable<string> Header => Observable.Return("Mail");

    // 本来はControlを返す。
    // nullを返すとErrorUIが表示される
    public override Type Control => null!;

    public override Type Context => typeof(SamplePageViewModel);

    public override string Name => "Sample page";

    public override string DisplayName => "Sample page";
}
