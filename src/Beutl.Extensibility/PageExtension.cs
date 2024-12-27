using Avalonia.Controls;

using FluentAvalonia.UI.Controls;

namespace Beutl.Extensibility;

[Obsolete("Use ToolWindowExtension instead.")]
public abstract class PageExtension : Extension
{
    public abstract Control CreateControl();

    public abstract IPageContext CreateContext();

    public abstract IconSource GetFilledIcon();

    public abstract IconSource GetRegularIcon();
}

[Obsolete("Use ToolWindowContext instead.")]
public interface IPageContext : IDisposable
{
    PageExtension Extension { get; }

    string Header { get; }
}
