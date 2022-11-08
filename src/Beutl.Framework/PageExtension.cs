using System.Diagnostics.CodeAnalysis;

using Avalonia.Controls;

using FluentAvalonia.UI.Controls;

namespace Beutl.Framework;

public abstract class PageExtension : Extension
{
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    public abstract Type Control { get; }

    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    public abstract Type Context { get; }

    public abstract IconSource GetFilledIcon();

    public abstract IconSource GetRegularIcon();
}

public abstract class PageExtension<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TView,
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TContext>
    : PageExtension
    where TView : IControl
    where TContext : IPageContext
{
    public override Type Control { get; } = typeof(TView);

    public override Type Context { get; } = typeof(TContext);
}

public interface IPageContext : IDisposable
{
    PageExtension Extension { get; }

    string Header { get; }
}
