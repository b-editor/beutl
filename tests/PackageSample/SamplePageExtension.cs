using System.Diagnostics.CodeAnalysis;

using Beutl.Framework;

using FluentAvalonia.UI.Controls;

namespace PackageSample;

public sealed class SamplePageViewModel : IPageContext
{
    public PageExtension Extension => SamplePageExtension.Instance;

    public string Header => "Mail";

    public void Dispose()
    {
    }
}

[Export]
public sealed class SamplePageExtension : PageExtension
{
    public SamplePageExtension()
    {
        Instance = this;
    }

    [AllowNull]
    public static SamplePageExtension Instance { get; private set; }

    // 本来はControlを返す。
    // nullを返すとErrorUIが表示される
    public override Type Control => null!;

    public override Type Context => typeof(SamplePageViewModel);

    public override string Name => "Sample page";

    public override string DisplayName => "Sample page";

    public override IconSource GetFilledIcon()
    {
        return new SymbolIconSource
        {
            Symbol = Symbol.MailFilled,
        };
    }

    public override IconSource GetRegularIcon()
    {
        return new SymbolIconSource
        {
            Symbol = Symbol.Mail
        };
    }
}
