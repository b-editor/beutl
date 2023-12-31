using System.Diagnostics.CodeAnalysis;

using Avalonia.Controls;

using Beutl.Extensibility;

using FluentAvalonia.UI.Controls;

namespace PackageSample;

public sealed class SamplePageViewModel(SamplePageExtension extension) : IPageContext
{
    public PageExtension Extension { get; } = extension;

    public string Header => "Mail";

    public void Dispose()
    {
    }
}

[Export]
public sealed class SamplePageExtension : PageExtension
{
    public override string Name => "Sample page";

    public override string DisplayName => "Sample page";

    public override IPageContext CreateContext()
    {
        return new SamplePageViewModel(this);
    }

    // 本来はControlを返す。
    // nullを返すとErrorUIが表示される
    public override Control CreateControl()
    {
        return null!;
    }

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
