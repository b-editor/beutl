using Avalonia.Controls;

using FluentAvalonia.UI.Controls;

namespace Beutl.Extensibility;

[Obsolete("Use ToolWindowExtension instead.")]
public abstract class PageExtension : Extension
{
    public abstract Control CreateControl();

    public abstract IPageContext CreateContext();

    [Obsolete]
    public abstract FAIconSource GetFilledIcon();

    public abstract FAIconSource GetRegularIcon();
}

[Obsolete("Use IToolWindowContext instead.")]
public interface IPageContext : IDisposable
{
    PageExtension Extension { get; }

    string Header { get; }
}
